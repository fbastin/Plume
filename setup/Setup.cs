using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Windows.Forms;

[assembly: AssemblyTitle("Installation de Plume / Plume setup")]
[assembly: AssemblyDescription("Installe Plume pour l'utilisateur courant / Installs Plume for the current user")]
[assembly: AssemblyProduct("Plume")]
[assembly: AssemblyVersion("1.0.3.0")]

namespace Plume
{
    /// <summary>
    /// PlumeSetup.exe : un seul fichier qui contient Plume (ressources intégrées par build.cmd).
    /// « PlumeSetup.exe /quiet » installe sans rien demander ; « /nolaunch » ne démarre pas Plume ensuite.
    /// </summary>
    internal static class SetupProgram
    {
        [STAThread]
        static int Main(string[] args)
        {
            // Langue du profil Plume existant, sinon celle de Windows.
            L.Lang = L.FromProfile(Installer.ProfileFolder);
            bool quiet = false, launch = true;
            foreach (string a in args)
            {
                string arg = a.ToLowerInvariant();
                if (arg == "/quiet" || arg == "--quiet")
                    quiet = true;
                else if (arg == "/nolaunch" || arg == "--nolaunch")
                    launch = false;
            }
            Dictionary<string, Func<Stream>> files = EmbeddedFiles();
            if (!files.ContainsKey("Plume.exe"))
            {
                MessageBox.Show(L.T("Ce programme d'installation est incomplet (Plume.exe absent).",
                    "This setup program is incomplete (Plume.exe is missing)."), "Plume", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return 2;
            }
            if (quiet)
            {
                try
                {
                    Installer.Install(files, new Installer.Options
                    {
                        DesktopShortcut = File.Exists(Installer.DesktopShortcut),
                        StartWithWindows = Installer.StartsWithWindows,
                        Launch = launch
                    });
                    return 0;
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine(ex.Message);
                    return 1;
                }
            }
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new SetupForm(files));
            return 0;
        }

        static Dictionary<string, Func<Stream>> EmbeddedFiles()
        {
            var files = new Dictionary<string, Func<Stream>>(StringComparer.OrdinalIgnoreCase);
            Assembly asm = Assembly.GetExecutingAssembly();
            foreach (string name in asm.GetManifestResourceNames())
            {
                if (name == "Plume.exe" || name == "Plume.exe.config" || name == "LICENSE" || name.StartsWith("data/", StringComparison.Ordinal))
                {
                    string n = name;
                    files[n] = () => asm.GetManifestResourceStream(n);
                }
            }
            return files;
        }
    }

    internal sealed class SetupForm : Form
    {
        readonly CheckBox desktop, startup, launch;
        readonly Button install, cancel;
        readonly Dictionary<string, Func<Stream>> files;

        public SetupForm(Dictionary<string, Func<Stream>> files)
        {
            this.files = files;
            bool update = Installer.IsInstalled;

            // Coordonnées pour un écran à 100 % ; mise à l'échelle appliquée à la fin (ResumeLayout).
            SuspendLayout();
            Text = update ? L.T("Mise à jour de Plume", "Plume update") : L.T("Installation de Plume", "Plume setup");
            Font = new Font("Segoe UI", 10f);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(520, 330);
            try
            {
                Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
            }
            catch (Exception)
            {
            }

            var logo = new PictureBox { Location = new Point(20, 20), Size = new Size(48, 48), SizeMode = PictureBoxSizeMode.Zoom };
            try
            {
                logo.Image = new Icon(Icon, 48, 48).ToBitmap();
            }
            catch (Exception)
            {
            }
            var title = new Label
            {
                Text = update ? L.T("Mettre à jour Plume", "Update Plume") : L.T("Installer Plume", "Install Plume"),
                Font = new Font("Segoe UI Semibold", 15f), Location = new Point(84, 18), AutoSize = true
            };
            var desc = new Label
            {
                Text = Installer.Description + ".",
                Location = new Point(86, 52), Size = new Size(420, 22), ForeColor = SystemColors.GrayText
            };
            var where = new Label
            {
                Text = L.T("Dossier : ", "Folder: ") + Installer.InstallDir,
                Location = new Point(20, 92), Size = new Size(480, 40), ForeColor = SystemColors.GrayText
            };
            desktop = new CheckBox
            {
                Text = L.T("Ajouter un raccourci sur le Bureau", "Add a desktop shortcut"),
                Checked = !update || File.Exists(Installer.DesktopShortcut), Location = new Point(20, 136), AutoSize = true
            };
            startup = new CheckBox
            {
                Text = L.T("Lancer Plume à l'ouverture de session", "Start Plume when I sign in"),
                Checked = Installer.StartsWithWindows, Location = new Point(20, 166), AutoSize = true
            };
            launch = new CheckBox { Text = L.T("Démarrer Plume maintenant", "Start Plume now"), Checked = true, Location = new Point(20, 196), AutoSize = true };
            var note = new Label
            {
                Text = L.T("Aucun droit d'administrateur n'est nécessaire. Pour désinstaller : Paramètres > Applications > "
                         + "Applications installées > Plume.",
                           "No administrator rights are needed. To uninstall: Settings > Apps > Installed apps > Plume."),
                Location = new Point(20, 232), Size = new Size(480, 40), ForeColor = SystemColors.GrayText
            };
            install = new Button { Text = update ? L.T("Mettre à jour", "Update") : L.T("Installer", "Install"), Location = new Point(300, 286), Size = new Size(110, 30) };
            cancel = new Button { Text = L.T("Annuler", "Cancel"), Location = new Point(416, 286), Size = new Size(90, 30) };
            install.Click += delegate { DoInstall(); };
            cancel.Click += delegate { Close(); };
            Controls.AddRange(new Control[] { logo, title, desc, where, desktop, startup, launch, note, install, cancel });
            AcceptButton = install;
            CancelButton = cancel;

            AutoScaleDimensions = new SizeF(96f, 96f);
            AutoScaleMode = AutoScaleMode.Dpi;
            ResumeLayout(false);
            PerformLayout();
        }

        void DoInstall()
        {
            install.Enabled = cancel.Enabled = false;
            UseWaitCursor = true;
            Application.DoEvents();
            try
            {
                Installer.Install(files, new Installer.Options
                {
                    DesktopShortcut = desktop.Checked,
                    StartWithWindows = startup.Checked,
                    Launch = launch.Checked
                });
                UseWaitCursor = false;
                MessageBox.Show(this, L.T("Plume est installé.\n\nVous le trouverez dans le menu Démarrer (tapez « Plume »).",
                    "Plume is installed.\n\nYou will find it in the Start menu (type “Plume”)."),
                    Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
                Close();
            }
            catch (Exception ex)
            {
                UseWaitCursor = false;
                install.Enabled = cancel.Enabled = true;
                MessageBox.Show(this, L.T("L'installation a échoué :\n", "Installation failed:\n") + ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }
}
