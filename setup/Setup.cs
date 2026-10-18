using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Windows.Forms;

[assembly: AssemblyTitle("Installation de Plume")]
[assembly: AssemblyDescription("Installe Plume pour l'utilisateur courant")]
[assembly: AssemblyProduct("Plume")]
[assembly: AssemblyVersion("1.0.0.0")]

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
                MessageBox.Show("Ce programme d'installation est incomplet (Plume.exe absent).", "Plume", MessageBoxButtons.OK, MessageBoxIcon.Error);
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
                if (name == "Plume.exe" || name == "Plume.exe.config" || name.StartsWith("data/", StringComparison.Ordinal))
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
            Text = update ? "Mise à jour de Plume" : "Installation de Plume";
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
                Text = update ? "Mettre à jour Plume" : "Installer Plume",
                Font = new Font("Segoe UI Semibold", 15f), Location = new Point(84, 18), AutoSize = true
            };
            var desc = new Label
            {
                Text = Installer.Description + ".",
                Location = new Point(86, 52), Size = new Size(420, 22), ForeColor = SystemColors.GrayText
            };
            var where = new Label
            {
                Text = "Dossier : " + Installer.InstallDir,
                Location = new Point(20, 92), Size = new Size(480, 40), ForeColor = SystemColors.GrayText
            };
            desktop = new CheckBox
            {
                Text = "Ajouter un raccourci sur le Bureau",
                Checked = !update || File.Exists(Installer.DesktopShortcut), Location = new Point(20, 136), AutoSize = true
            };
            startup = new CheckBox
            {
                Text = "Lancer Plume à l'ouverture de session",
                Checked = Installer.StartsWithWindows, Location = new Point(20, 166), AutoSize = true
            };
            launch = new CheckBox { Text = "Démarrer Plume maintenant", Checked = true, Location = new Point(20, 196), AutoSize = true };
            var note = new Label
            {
                Text = "Aucun droit d'administrateur n'est nécessaire. Pour désinstaller : Paramètres > Applications > "
                     + "Applications installées > Plume.",
                Location = new Point(20, 232), Size = new Size(480, 40), ForeColor = SystemColors.GrayText
            };
            install = new Button { Text = update ? "Mettre à jour" : "Installer", Location = new Point(300, 286), Size = new Size(110, 30) };
            cancel = new Button { Text = "Annuler", Location = new Point(416, 286), Size = new Size(90, 30) };
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
                MessageBox.Show(this, "Plume est installé.\n\nVous le trouverez dans le menu Démarrer (tapez « Plume »).",
                    Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
                Close();
            }
            catch (Exception ex)
            {
                UseWaitCursor = false;
                install.Enabled = cancel.Enabled = true;
                MessageBox.Show(this, "L'installation a échoué :\n" + ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }
}
