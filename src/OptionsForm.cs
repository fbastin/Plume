using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace Plume
{
    /// <summary>Fenêtre des options.</summary>
    internal sealed class OptionsForm : Form
    {
        readonly Settings s;
        readonly Func<string, UserModel> userFor;
        readonly Action<string, Settings> testVoice;
        public event Action<Settings> Applied;

        ComboBox language, keys, placement, voiceFr, voiceEn, theme;
        NumericUpDown count, minLearn;
        CheckBox nextWord, addSpace, smartPunct, spelling, learn, speakHover, speakChosen, echoLetters, echoWords, echoSentences;
        TrackBar rate, volume;
        Button fontButton;
        TextBox hotRead, hotToggle, excluded, abbreviations, newWord;
        UserModel vocabUser;
        ListBox learned, blockedList;
        ComboBox vocabLang;
        string fontName;
        float fontSize;

        public OptionsForm(Settings current, Func<string, UserModel> userFor, Action<string, Settings> testVoice)
        {
            s = current.Clone();
            this.userFor = userFor;
            this.testVoice = testVoice;
            fontName = s.FontName;
            fontSize = s.FontSize;

            // Positions et tailles ci-dessous sont données pour un écran à 100 % ; Windows Forms les agrandit
            // à la fin (ResumeLayout) selon la mise à l'échelle de l'écran (125 %, 150 %…).
            SuspendLayout();
            Text = "Plume - Options";
            Font = new Font("Segoe UI", 9.5f);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(560, 520);
            ShowInTaskbar = true;

            var tabs = new TabControl { Location = new Point(10, 10), Size = new Size(540, 460) };
            tabs.TabPages.Add(PredictionPage());
            tabs.TabPages.Add(SpeechPage());
            tabs.TabPages.Add(AppearancePage());
            tabs.TabPages.Add(VocabularyPage());
            tabs.TabPages.Add(ShortcutsPage());
            Controls.Add(tabs);

            // La fenêtre est ouverte sans bloquer Plume (Show) : DialogResult ne la fermerait pas, on ferme nous-mêmes.
            var ok = new Button { Text = "OK", Location = new Point(286, 480), Size = new Size(84, 28) };
            var cancel = new Button { Text = "Annuler", Location = new Point(376, 480), Size = new Size(84, 28) };
            var apply = new Button { Text = "Appliquer", Location = new Point(466, 480), Size = new Size(84, 28) };
            ok.Click += delegate
            {
                if (Apply())
                    Close();
            };
            cancel.Click += delegate { Close(); };
            apply.Click += delegate { Apply(); };
            Controls.AddRange(new Control[] { ok, cancel, apply });
            AcceptButton = ok;
            CancelButton = cancel;

            // La mise à l'échelle doit être demandée une fois tous les contrôles ajoutés, sinon
            // elle s'applique trop tôt et seuls les textes grandissent.
            AutoScaleDimensions = new SizeF(96f, 96f);
            AutoScaleMode = AutoScaleMode.Dpi;
            ResumeLayout(false);
            PerformLayout();
            GrowButtonsToFit(this);
        }

        /// <summary>Un bouton s'élargit si son texte ne tient pas (police plus grande, autre écran).</summary>
        static void GrowButtonsToFit(Control parent)
        {
            foreach (Control c in parent.Controls)
            {
                var b = c as Button;
                if (b != null)
                {
                    b.AutoSizeMode = AutoSizeMode.GrowOnly;
                    b.AutoSize = true;
                }
                GrowButtonsToFit(c);
            }
        }

        // ---- Construction des onglets ----

        static TabPage Page(string title)
        {
            return new TabPage(title) { Padding = new Padding(12), UseVisualStyleBackColor = true };
        }

        static Label AddLabel(Control parent, string text, int x, int y)
        {
            var l = new Label { Text = text, Location = new Point(x, y + 3), AutoSize = true };
            parent.Controls.Add(l);
            return l;
        }

        static CheckBox AddCheck(Control parent, string text, bool value, int x, int y)
        {
            var c = new CheckBox { Text = text, Checked = value, Location = new Point(x, y), AutoSize = true };
            parent.Controls.Add(c);
            return c;
        }

        static ComboBox AddCombo(Control parent, string[] items, int selected, int x, int y, int w)
        {
            var c = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Location = new Point(x, y), Width = w };
            c.Items.AddRange(items);
            c.SelectedIndex = Math.Max(0, Math.Min(items.Length - 1, selected));
            parent.Controls.Add(c);
            return c;
        }

        TabPage PredictionPage()
        {
            var p = Page("Prédiction");
            AddLabel(p, "Langue d'écriture :", 12, 16);
            language = AddCombo(p, new[] { "Français", "English" }, s.Language == "en" ? 1 : 0, 230, 14, 260);
            AddLabel(p, "Nombre de mots proposés :", 12, 50);
            count = new NumericUpDown { Minimum = 1, Maximum = 9, Value = s.PredictionCount, Location = new Point(230, 48), Width = 60 };
            p.Controls.Add(count);
            AddLabel(p, "Choisir un mot avec :", 12, 84);
            keys = AddCombo(p, new[] { "les chiffres 1 à 9", "les touches F1 à F9", "la souris seulement" }, (int)s.Keys, 230, 82, 260);
            AddLabel(p, "Position de la liste :", 12, 118);
            placement = AddCombo(p, new[] { "sous le curseur de texte", "position fixe (glisser la liste)" }, (int)s.Placement, 230, 116, 260);
            nextWord = AddCheck(p, "Proposer le mot suivant après une espace", s.PredictNextWord, 12, 160);
            addSpace = AddCheck(p, "Ajouter une espace après le mot choisi", s.AddSpace, 12, 188);
            smartPunct = AddCheck(p, "Ponctuation intelligente (retirer l'espace avant . , )", s.SmartPunctuation, 12, 216);
            spelling = AddCheck(p, "Aide orthographique (mots qui se prononcent de la même façon)", s.SpellingHelp, 12, 244);
            var help = new Label
            {
                Text = "Astuce : appuyez sur Échap pour masquer la liste et taper un chiffre.\n"
                     + "Sur un clavier AZERTY, utilisez le pavé numérique ou Maj + chiffre.",
                Location = new Point(12, 290), Size = new Size(490, 48), ForeColor = SystemColors.GrayText
            };
            p.Controls.Add(help);
            return p;
        }

        TabPage SpeechPage()
        {
            var p = Page("Parole");
            List<string> fr = Speaker.Voices("fr"), en = Speaker.Voices("en");
            AddLabel(p, "Voix française :", 12, 16);
            voiceFr = AddCombo(p, fr.ToArray(), Math.Max(0, fr.IndexOf(s.VoiceFr)), 170, 14, 250);
            AddLabel(p, "Voix anglaise :", 12, 50);
            voiceEn = AddCombo(p, en.ToArray(), Math.Max(0, en.IndexOf(s.VoiceEn)), 170, 48, 250);
            var test = new Button { Text = "Écouter", Location = new Point(430, 30), Size = new Size(80, 28) };
            test.Click += delegate
            {
                Collect();
                testVoice(language.SelectedIndex == 1 ? "en" : "fr", s);
            };
            p.Controls.Add(test);
            AddLabel(p, "Vitesse :", 12, 92);
            rate = new TrackBar { Minimum = -10, Maximum = 10, Value = Math.Max(-10, Math.Min(10, s.Rate)), TickFrequency = 2, Location = new Point(170, 84), Width = 250 };
            p.Controls.Add(rate);
            AddLabel(p, "Volume :", 12, 136);
            volume = new TrackBar { Minimum = 0, Maximum = 100, Value = Math.Max(0, Math.Min(100, s.Volume)), TickFrequency = 10, Location = new Point(170, 128), Width = 250 };
            p.Controls.Add(volume);
            AddLabel(p, "Lire à voix haute :", 12, 184);
            speakHover = AddCheck(p, "les mots proposés quand la souris passe dessus", s.SpeakOnHover, 24, 210);
            speakChosen = AddCheck(p, "le mot choisi dans la liste", s.SpeakChosen, 24, 236);
            echoLetters = AddCheck(p, "chaque lettre tapée", s.EchoLetters, 24, 262);
            echoWords = AddCheck(p, "chaque mot tapé", s.EchoWords, 24, 288);
            echoSentences = AddCheck(p, "chaque phrase terminée (. ! ?)", s.EchoSentences, 24, 314);
            return p;
        }

        TabPage AppearancePage()
        {
            var p = Page("Apparence");
            AddLabel(p, "Police de la liste :", 12, 16);
            fontButton = new Button { Location = new Point(170, 12), Size = new Size(250, 30) };
            UpdateFontButton();
            fontButton.Click += delegate
            {
                using (var dlg = new FontDialog { Font = new Font(fontName, fontSize), ShowEffects = false, MinSize = 8, MaxSize = 48 })
                {
                    if (dlg.ShowDialog(this) == DialogResult.OK)
                    {
                        fontName = dlg.Font.Name;
                        fontSize = dlg.Font.Size;
                        UpdateFontButton();
                    }
                }
            };
            p.Controls.Add(fontButton);
            AddLabel(p, "Couleurs :", 12, 58);
            theme = AddCombo(p, Theme.Names, Array.IndexOf(Theme.Names, s.Theme), 170, 56, 250);
            var note = new Label
            {
                Text = "Polices adaptées à la dyslexie : installez par exemple OpenDyslexic ou Andika,\npuis choisissez-la ci-dessus.",
                Location = new Point(12, 100), Size = new Size(490, 48), ForeColor = SystemColors.GrayText
            };
            p.Controls.Add(note);
            return p;
        }

        void UpdateFontButton()
        {
            fontButton.Text = fontName + ", " + fontSize.ToString("0.#") + " pt";
        }

        TabPage VocabularyPage()
        {
            var p = Page("Vocabulaire");
            learn = AddCheck(p, "Apprendre les mots que j'utilise", s.Learn, 12, 12);
            AddLabel(p, "Proposer un mot nouveau après", 12, 42);
            minLearn = new NumericUpDown { Minimum = 1, Maximum = 10, Value = Math.Max(1, Math.Min(10, s.MinLearnCount)), Location = new Point(210, 40), Width = 50 };
            p.Controls.Add(minLearn);
            AddLabel(p, "utilisations", 266, 42);
            AddLabel(p, "Langue :", 360, 42);
            vocabLang = AddCombo(p, new[] { "Français", "English" }, s.Language == "en" ? 1 : 0, 420, 40, 90);

            AddLabel(p, "Mes mots :", 12, 76);
            learned = new ListBox { Location = new Point(12, 98), Size = new Size(240, 170), IntegralHeight = false };
            p.Controls.Add(learned);
            newWord = new TextBox { Location = new Point(12, 274), Width = 150 };
            p.Controls.Add(newWord);
            var add = new Button { Text = "Ajouter", Location = new Point(166, 272), Size = new Size(86, 26) };
            var remove = new Button { Text = "Retirer", Location = new Point(166, 302), Size = new Size(86, 26) };
            p.Controls.Add(add);
            p.Controls.Add(remove);

            AddLabel(p, "Mots à ne plus proposer :", 270, 76);
            blockedList = new ListBox { Location = new Point(270, 98), Size = new Size(240, 170), IntegralHeight = false };
            p.Controls.Add(blockedList);
            var unblock = new Button { Text = "Proposer à nouveau", Location = new Point(270, 272), Size = new Size(150, 26) };
            p.Controls.Add(unblock);

            AddLabel(p, "Abréviations (une par ligne : abréviation = texte) :", 12, 336);
            abbreviations = new TextBox
            {
                Multiline = true, AcceptsReturn = true, ScrollBars = ScrollBars.Vertical,
                Location = new Point(12, 358), Size = new Size(498, 64)
            };
            p.Controls.Add(abbreviations);

            Action refresh = delegate
            {
                if (vocabUser != null)
                    vocabUser.SetAbbreviations(abbreviations.Text);
                vocabUser = userFor(vocabLang.SelectedIndex == 1 ? "en" : "fr");
                learned.Items.Clear();
                blockedList.Items.Clear();
                if (vocabUser == null)
                    return;
                foreach (var kv in vocabUser.AllWords())
                    learned.Items.Add(kv.Key);
                foreach (string w in vocabUser.BlockedWords())
                    blockedList.Items.Add(w);
                abbreviations.Text = vocabUser.AbbreviationsText();
            };
            vocabLang.SelectedIndexChanged += delegate { refresh(); };
            add.Click += delegate
            {
                string w = newWord.Text.Trim();
                if (vocabUser == null || w.Length == 0)
                    return;
                vocabUser.AddWord(w, 10);
                newWord.Clear();
                refresh();
            };
            remove.Click += delegate
            {
                if (vocabUser != null && learned.SelectedItem != null)
                {
                    vocabUser.RemoveWord((string)learned.SelectedItem);
                    refresh();
                }
            };
            unblock.Click += delegate
            {
                if (vocabUser != null && blockedList.SelectedItem != null)
                {
                    vocabUser.Unblock((string)blockedList.SelectedItem);
                    refresh();
                }
            };
            refresh();
            return p;
        }

        TabPage ShortcutsPage()
        {
            var p = Page("Raccourcis");
            AddLabel(p, "Lire la sélection / arrêter :", 12, 16);
            hotRead = new TextBox { Text = s.HotkeyRead, Location = new Point(230, 14), Width = 200 };
            p.Controls.Add(hotRead);
            AddLabel(p, "Activer / désactiver la prédiction :", 12, 50);
            hotToggle = new TextBox { Text = s.HotkeyToggle, Location = new Point(230, 48), Width = 200 };
            p.Controls.Add(hotToggle);
            var hint = new Label
            {
                Text = "Exemples : Ctrl+Shift+F12, Ctrl+Alt+L, Pause. Laisser vide pour aucun raccourci.",
                Location = new Point(12, 80), Size = new Size(490, 22), ForeColor = SystemColors.GrayText
            };
            p.Controls.Add(hint);
            AddLabel(p, "Applications où Plume reste silencieux\n(noms de programme séparés par ;) :", 12, 118);
            excluded = new TextBox { Text = s.ExcludedApps, Location = new Point(12, 158), Width = 498 };
            p.Controls.Add(excluded);
            var priv = new Label
            {
                Text = "Plume ne fait rien dans les champs de mot de passe. Les mots appris restent sur\ncet ordinateur (dossier %APPDATA%\\Plume).",
                Location = new Point(12, 196), Size = new Size(498, 48), ForeColor = SystemColors.GrayText
            };
            p.Controls.Add(priv);
            return p;
        }

        // ---- Validation ----

        void Collect()
        {
            s.Language = language.SelectedIndex == 1 ? "en" : "fr";
            s.PredictionCount = (int)count.Value;
            s.Keys = (SelectionKeys)keys.SelectedIndex;
            s.Placement = (Placement)placement.SelectedIndex;
            s.PredictNextWord = nextWord.Checked;
            s.AddSpace = addSpace.Checked;
            s.SmartPunctuation = smartPunct.Checked;
            s.SpellingHelp = spelling.Checked;
            s.VoiceFr = voiceFr.SelectedItem as string ?? "";
            s.VoiceEn = voiceEn.SelectedItem as string ?? "";
            s.Rate = rate.Value;
            s.Volume = volume.Value;
            s.SpeakOnHover = speakHover.Checked;
            s.SpeakChosen = speakChosen.Checked;
            s.EchoLetters = echoLetters.Checked;
            s.EchoWords = echoWords.Checked;
            s.EchoSentences = echoSentences.Checked;
            s.FontName = fontName;
            s.FontSize = fontSize;
            s.Theme = theme.SelectedItem as string ?? "Clair";
            s.Learn = learn.Checked;
            s.MinLearnCount = (int)minLearn.Value;
            s.HotkeyRead = hotRead.Text.Trim();
            s.HotkeyToggle = hotToggle.Text.Trim();
            s.ExcludedApps = excluded.Text.Trim();
        }

        /// <summary>Applique les réglages ; renvoie faux si un raccourci est invalide.</summary>
        bool Apply()
        {
            Collect();
            foreach (string hk in new[] { s.HotkeyRead, s.HotkeyToggle })
            {
                uint mods, vk;
                if (hk.Length > 0 && !Hotkeys.TryParse(hk, out mods, out vk))
                {
                    MessageBox.Show(this, "Raccourci non reconnu : " + hk, "Plume", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return false;
                }
            }
            if (vocabUser != null)
                vocabUser.SetAbbreviations(abbreviations.Text);
            if (Applied != null)
                Applied(s.Clone());
            return true;
        }
    }
}
