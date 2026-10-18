using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

namespace Plume
{
    /// <summary>
    /// Installation pour l'utilisateur courant, sans droits d'administrateur : copie dans
    /// %LOCALAPPDATA%\Programs\Plume, raccourcis (menu Démarrer, Bureau) et inscription dans
    /// « Applications installées » de Windows, d'où l'on peut désinstaller.
    /// Partagé entre Plume.exe (--uninstall) et PlumeSetup.exe.
    /// </summary>
    internal static class Installer
    {
        public static string Description
        {
            get { return L.T("Assistant d'écriture : prédiction de mots et lecture à voix haute", "Writing assistant: word prediction and reading aloud"); }
        }

        // Pour les tests : PLUME_INSTALL_TEST=dossier redirige fichiers, raccourcis et registre vers un bac à sable.
        static readonly string TestRoot = Environment.GetEnvironmentVariable("PLUME_INSTALL_TEST");

        static bool TestMode
        {
            get { return !string.IsNullOrEmpty(TestRoot); }
        }

        public static string InstallDir
        {
            get
            {
                return TestMode ? Path.Combine(TestRoot, "app")
                    : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Plume");
            }
        }

        public static string ExePath
        {
            get { return Path.Combine(InstallDir, "Plume.exe"); }
        }

        /// <summary>Dossier des réglages et mots appris.</summary>
        public static string ProfileFolder
        {
            get
            {
                return TestMode ? Path.Combine(TestRoot, "profil")
                    : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Plume");
            }
        }

        public static string RunKey
        {
            get { return TestMode ? @"Software\PlumeInstallTest\Run" : @"Software\Microsoft\Windows\CurrentVersion\Run"; }
        }

        static string UninstallKey
        {
            get { return TestMode ? @"Software\PlumeInstallTest\Uninstall\Plume" : @"Software\Microsoft\Windows\CurrentVersion\Uninstall\Plume"; }
        }

        static string MenuShortcut
        {
            get
            {
                return Path.Combine(TestMode ? Path.Combine(TestRoot, "menu")
                    : Environment.GetFolderPath(Environment.SpecialFolder.Programs), "Plume.lnk");
            }
        }

        public static string DesktopShortcut
        {
            get
            {
                return Path.Combine(TestMode ? Path.Combine(TestRoot, "bureau")
                    : Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "Plume.lnk");
            }
        }

        /// <summary>
        /// Nom des objets système (instance unique, demande de fermeture) pour un dossier de profil.
        /// FNV-1a plutôt que GetHashCode, qui varie entre processus 32 et 64 bits.
        /// </summary>
        public static string InstanceId(string profileFolder)
        {
            uint h = 2166136261;
            foreach (char c in profileFolder.ToLowerInvariant())
            {
                h ^= c;
                h *= 16777619;
            }
            return "Plume." + h.ToString("x8");
        }

        public static bool IsInstalled
        {
            get
            {
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(UninstallKey))
                    return k != null;
            }
        }

        public static bool StartsWithWindows
        {
            get
            {
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(RunKey))
                    return k != null && k.GetValue("Plume") != null;
            }
        }

        public static void SetStartup(bool on, string exe)
        {
            using (RegistryKey k = Registry.CurrentUser.CreateSubKey(RunKey))
            {
                if (on)
                    k.SetValue("Plume", "\"" + exe + "\"");
                else
                    k.DeleteValue("Plume", false);
            }
        }

        /// <summary>Demande à Plume de se fermer s'il tourne, et attend qu'il soit sorti.</summary>
        public static void StopRunning()
        {
            string id = InstanceId(ProfileFolder);
            EventWaitHandle quit;
            if (!EventWaitHandle.TryOpenExisting(id + ".Quit", out quit))
                return;
            using (quit)
                quit.Set();
            Mutex running;
            if (!Mutex.TryOpenExisting(id + ".Instance", out running))
                return;
            using (running)
            {
                try
                {
                    if (running.WaitOne(10000))
                        running.ReleaseMutex();
                }
                catch (AbandonedMutexException)
                {
                    running.ReleaseMutex();
                }
            }
            Thread.Sleep(300); // le temps que Windows libère le fichier Plume.exe
        }

        public sealed class Options
        {
            public bool DesktopShortcut = true;
            public bool StartWithWindows = false;
            public bool Launch = true;
        }

        /// <summary>Installe (ou met à jour) Plume à partir des fichiers fournis (chemin relatif -> contenu).</summary>
        public static void Install(IDictionary<string, Func<Stream>> files, Options options)
        {
            StopRunning();
            Directory.CreateDirectory(InstallDir);
            long size = 0;
            foreach (var f in files)
            {
                string dest = Path.Combine(InstallDir, f.Key.Replace('/', '\\'));
                Directory.CreateDirectory(Path.GetDirectoryName(dest));
                using (Stream src = f.Value())
                    WriteWithRetry(src, dest);
                size += new FileInfo(dest).Length;
            }

            CreateShortcut(MenuShortcut, ExePath);
            if (options.DesktopShortcut)
                CreateShortcut(DesktopShortcut, ExePath);
            else
                TryDelete(DesktopShortcut);

            Version v = AssemblyName.GetAssemblyName(ExePath).Version;
            using (RegistryKey k = Registry.CurrentUser.CreateSubKey(UninstallKey))
            {
                k.SetValue("DisplayName", "Plume");
                k.SetValue("DisplayVersion", v.ToString(3));
                k.SetValue("Publisher", "Plume");
                k.SetValue("Comments", Description);
                k.SetValue("DisplayIcon", ExePath + ",0");
                k.SetValue("InstallLocation", InstallDir);
                k.SetValue("InstallDate", DateTime.Now.ToString("yyyyMMdd"));
                k.SetValue("UninstallString", "\"" + ExePath + "\" --uninstall");
                k.SetValue("QuietUninstallString", "\"" + ExePath + "\" --uninstall --quiet");
                k.SetValue("EstimatedSize", (int)(size / 1024), RegistryValueKind.DWord);
                k.SetValue("NoModify", 1, RegistryValueKind.DWord);
                k.SetValue("NoRepair", 1, RegistryValueKind.DWord);
            }
            SetStartup(options.StartWithWindows, ExePath);
            if (options.Launch)
                Process.Start(ExePath);
        }

        static void WriteWithRetry(Stream src, string dest)
        {
            for (int attempt = 0; ; attempt++)
            {
                try
                {
                    using (var fs = new FileStream(dest, FileMode.Create, FileAccess.Write))
                        src.CopyTo(fs);
                    return;
                }
                catch (IOException)
                {
                    if (attempt >= 10)
                        throw;
                    Thread.Sleep(500);
                }
            }
        }

        /// <summary>Désinstalle Plume (lancé par Windows via « Plume.exe --uninstall »).</summary>
        public static void Uninstall(bool quiet)
        {
            if (!quiet && MessageBox.Show(L.T("Désinstaller Plume ?", "Uninstall Plume?"), "Plume", MessageBoxButtons.YesNo,
                    MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) != DialogResult.Yes)
                return;
            StopRunning();
            SetStartup(false, ExePath);
            TryDelete(MenuShortcut);
            TryDelete(DesktopShortcut);
            Registry.CurrentUser.DeleteSubKeyTree(UninstallKey, false);

            if (!quiet && Directory.Exists(ProfileFolder) && MessageBox.Show(
                    L.T("Supprimer aussi vos mots appris, abréviations et réglages ?\n\nSi vous répondez Non, ils seront retrouvés "
                        + "en cas de réinstallation.",
                        "Also delete your learned words, abbreviations and settings?\n\nIf you answer No, they will be kept "
                        + "for a future reinstallation.") + "\n(" + ProfileFolder + ")", "Plume", MessageBoxButtons.YesNo,
                    MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) == DialogResult.Yes)
            {
                try
                {
                    Directory.Delete(ProfileFolder, true);
                }
                catch (Exception ex)
                {
                    MessageBox.Show(L.T("Le dossier " + ProfileFolder + " n'a pas pu être supprimé : ",
                        "The folder " + ProfileFolder + " could not be deleted: ") + ex.Message, "Plume");
                }
            }
            if (!quiet)
                MessageBox.Show(L.T("Plume a été désinstallé.", "Plume has been uninstalled."), "Plume", MessageBoxButtons.OK, MessageBoxIcon.Information);
            RemoveInstallDirAfterExit();
        }

        /// <summary>
        /// Le désinstalleur tourne depuis le dossier d'installation : on le fait effacer
        /// par une commande lancée juste avant de quitter.
        /// </summary>
        static void RemoveInstallDirAfterExit()
        {
            string dir = Path.GetFullPath(InstallDir);
            // Garde-fou : on n'efface qu'un dossier absolu nommé « Plume » (ou « app » en test) contenant Plume.exe.
            string name = Path.GetFileName(dir.TrimEnd('\\'));
            if (!Path.IsPathRooted(dir) || !(name == "Plume" || (TestMode && name == "app"))
                || !File.Exists(Path.Combine(dir, "Plume.exe")))
                return;
            var psi = new ProcessStartInfo("cmd.exe", "/c ping -n 3 127.0.0.1 >nul & rmdir /s /q \"" + dir + "\"")
            {
                CreateNoWindow = true,
                UseShellExecute = false,
                WorkingDirectory = Path.GetTempPath()
            };
            Process.Start(psi);
        }

        static void TryDelete(string file)
        {
            try
            {
                if (File.Exists(file))
                    File.Delete(file);
            }
            catch (Exception)
            {
            }
        }

        // ---- Raccourcis (.lnk) via l'interface Windows IShellLink ----

        [ComImport, Guid("00021401-0000-0000-C000-000000000046")]
        class ShellLink
        {
        }

        [ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown), Guid("000214F9-0000-0000-C000-000000000046")]
        interface IShellLinkW
        {
            void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder file, int cch, IntPtr findData, int flags);
            void GetIDList(out IntPtr pidl);
            void SetIDList(IntPtr pidl);
            void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder name, int cch);
            void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string name);
            void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder dir, int cch);
            void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string dir);
            void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder args, int cch);
            void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string args);
            void GetHotkey(out short hotkey);
            void SetHotkey(short hotkey);
            void GetShowCmd(out int showCmd);
            void SetShowCmd(int showCmd);
            void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder path, int cch, out int index);
            void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string path, int index);
            void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string path, int reserved);
            void Resolve(IntPtr hwnd, int flags);
            void SetPath([MarshalAs(UnmanagedType.LPWStr)] string file);
        }

        static void CreateShortcut(string lnk, string target)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(lnk));
            var link = (IShellLinkW)new ShellLink();
            try
            {
                link.SetPath(target);
                link.SetWorkingDirectory(Path.GetDirectoryName(target));
                link.SetDescription(Description);
                link.SetIconLocation(target, 0);
                ((IPersistFile)link).Save(lnk, true);
            }
            finally
            {
                Marshal.ReleaseComObject(link);
            }
        }
    }
}
