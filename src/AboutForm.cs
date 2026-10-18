using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Text;
using System.Windows.Forms;

namespace Plume
{
    /// <summary>
    /// Fenêtre « À propos » : version, licences et attribution des sources des dictionnaires
    /// (exigée par leurs licences Creative Commons), liens vers la documentation.
    /// </summary>
    internal sealed class AboutForm : Form
    {
        const string Repo = "https://github.com/fbastin/Plume";
        const int TextWidth = 480;

        public AboutForm()
        {
            // Coordonnées pour un écran à 100 % ; mise à l'échelle appliquée à la fin (ResumeLayout).
            // Les paragraphes s'empilent et passent à la ligne d'eux-mêmes : rien n'est tronqué si le texte s'allonge.
            SuspendLayout();
            Text = L.T("À propos de Plume", "About Plume");
            Font = new Font("Segoe UI", 9.5f);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;
            ShowInTaskbar = true;

            var flow = new FlowLayoutPanel
            {
                FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink, Padding = new Padding(16, 16, 16, 12)
            };

            var header = new Panel { Size = new Size(TextWidth, 58), Margin = new Padding(0, 0, 0, 6) };
            var logo = new PictureBox { Location = new Point(0, 2), Size = new Size(48, 48), SizeMode = PictureBoxSizeMode.Zoom };
            var title = new Label { Text = "Plume", Font = new Font("Segoe UI Semibold", 15f), Location = new Point(62, 0), AutoSize = true };
            var version = new Label
            {
                Text = "Version " + Assembly.GetExecutingAssembly().GetName().Version.ToString(3),
                Location = new Point(64, 34), AutoSize = true, ForeColor = SystemColors.GrayText
            };
            header.Controls.AddRange(new Control[] { logo, title, version });
            flow.Controls.Add(header);

            flow.Controls.Add(Para(Installer.Description + "."));
            flow.Controls.Add(Para(L.T("© 2026 Fabian Bastin. Logiciel libre, sous {licence MIT}.",
                "© 2026 Fabian Bastin. Free software, under the {MIT license}."), "LICENSE"));
            flow.Controls.Add(Para(L.T("Inspiré de WordQ, de Quillsoft. Plume est un projet indépendant, sans lien avec cette société.",
                "Inspired by WordQ, by Quillsoft. Plume is an independent project, not affiliated with that company.")));

            Label heading = Para(L.T("Dictionnaires", "Dictionaries"));
            heading.Font = new Font(Font, FontStyle.Bold);
            heading.Margin = new Padding(0, 8, 0, 4);
            flow.Controls.Add(heading);
            flow.Controls.Add(Para(L.T("Sous licence {CC BY-SA 4.0}, ils sont calculés à partir de :",
                "Under the {CC BY-SA 4.0} license, they are computed from:"), @"data\LICENSE.md"));
            flow.Controls.Add(Item(L.T("{FrequencyWords}, de Hermit Dave : fréquences tirées des sous-titres d'OpenSubtitles 2018 (CC BY-SA 4.0) ;",
                "{FrequencyWords}, by Hermit Dave: frequencies from OpenSubtitles 2018 subtitles (CC BY-SA 4.0);"),
                "https://github.com/hermitdave/FrequencyWords"));
            flow.Controls.Add(Item(L.T("{Tatoeba} : phrases d'exemple de ses contributeurs (CC BY 2.0 FR) ;",
                "{Tatoeba}: example sentences by its contributors (CC BY 2.0 FR);"), "https://tatoeba.org"));
            flow.Controls.Add(Item(L.T("{Leipzig Corpora Collection} : actualités 2023 (CC BY). D. Goldhahn, T. Eckart, U. Quasthoff, "
                + "« Building Large Monolingual Dictionaries at the Leipzig Corpora Collection: From 100 to 200 Languages », LREC 2012.",
                "{Leipzig Corpora Collection}: 2023 news (CC BY). D. Goldhahn, T. Eckart, U. Quasthoff, "
                + "“Building Large Monolingual Dictionaries at the Leipzig Corpora Collection: From 100 to 200 Languages”, LREC 2012."),
                "https://wortschatz.uni-leipzig.de"));

            Label links = Para(L.T("{Page de Plume}   ·   {Mode d'emploi}   ·   {Nouvelles versions}",
                "{Plume's page}   ·   {User guide}   ·   {New versions}"),
                "https://www.slashbin.net/plume.php?lang=" + L.T("fr", "en"),
                L.T(Repo + "#readme", Repo + "/blob/main/README.en.md"), Repo + "/releases");
            links.Margin = new Padding(0, 12, 0, 2);
            flow.Controls.Add(links);
            flow.Controls.Add(Para(L.T("{Signaler un problème}   ·   {Code source}", "{Report a problem}   ·   {Source code}"),
                Repo + "/issues", Repo));

            var close = new Button
            {
                Text = L.T("Fermer", "Close"), Size = new Size(90, 30), Anchor = AnchorStyles.Right,
                Margin = new Padding(0, 10, 0, 0), AutoSize = true, AutoSizeMode = AutoSizeMode.GrowOnly
            };
            close.Click += delegate { Close(); };
            flow.Controls.Add(close);
            Controls.Add(flow);
            AcceptButton = close;
            CancelButton = close;

            AutoScaleDimensions = new SizeF(96f, 96f);
            AutoScaleMode = AutoScaleMode.Dpi;
            ResumeLayout(false);
            PerformLayout();

            // Image prise dans le fichier .ico intégré (build.cmd), à la taille du logo une fois mis à l'échelle : nette à 150 %.
            try
            {
                using (Stream ico = Assembly.GetExecutingAssembly().GetManifestResourceStream("plume.ico"))
                {
                    if (ico != null)
                        using (var icon = new Icon(ico, logo.Width, logo.Height))
                            logo.Image = icon.ToBitmap();
                }
            }
            catch (Exception)
            {
            }
        }

        /// <summary>Paragraphe ; les passages entre accolades sont des liens, vers les cibles données dans l'ordre.</summary>
        static Label Para(string text, params string[] targets)
        {
            var sb = new StringBuilder();
            var spans = new List<LinkLabel.Link>();
            int start = 0;
            foreach (char c in text)
            {
                if (c == '{')
                    start = sb.Length;
                else if (c == '}')
                    spans.Add(new LinkLabel.Link(start, sb.Length - start, targets[spans.Count]));
                else
                    sb.Append(c);
            }
            Label label = spans.Count == 0 ? new Label() : new LinkLabel();
            label.Text = sb.ToString();
            label.AutoSize = true;
            label.MaximumSize = new Size(TextWidth, 0);
            label.Margin = new Padding(0, 0, 0, 4);
            var link = label as LinkLabel;
            if (link != null)
            {
                link.Links.Clear();
                foreach (LinkLabel.Link s in spans)
                    link.Links.Add(s);
                link.LinkClicked += (o, e) => Open((string)e.Link.LinkData);
            }
            return label;
        }

        /// <summary>Élément de liste, en retrait sous sa puce.</summary>
        static Control Item(string text, params string[] targets)
        {
            Label body = Para(text, targets);
            body.MaximumSize = new Size(TextWidth - 18, 0);
            body.Margin = new Padding(0);
            var row = new FlowLayoutPanel
            {
                FlowDirection = FlowDirection.LeftToRight, WrapContents = false, AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink, Margin = new Padding(4, 0, 0, 4), Padding = new Padding(0)
            };
            row.Controls.Add(new Label { Text = "•", AutoSize = true, Margin = new Padding(0, 0, 4, 0) });
            row.Controls.Add(body);
            return row;
        }

        /// <summary>Ouvre une page web, ou un fichier de licence installé avec Plume (sa version en ligne s'il manque).</summary>
        static void Open(string target)
        {
            try
            {
                if (target.StartsWith("https://", StringComparison.Ordinal))
                {
                    Process.Start(target);
                    return;
                }
                string file = Path.Combine(Path.GetDirectoryName(Application.ExecutablePath), target);
                if (File.Exists(file))
                    Process.Start("notepad.exe", "\"" + file + "\"");
                else
                    Process.Start(Repo + "/blob/main/" + target.Replace('\\', '/'));
            }
            catch (Exception ex)
            {
                Log.Write("Ouverture de " + target + " : " + ex.Message);
            }
        }
    }
}
