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

        ComboBox language, keys, placement, voiceFr, voiceEn, theme, uiLang, toolbarStyle;
        TabControl tabs;
        NumericUpDown count, minLearn;
        CheckBox tabSelects, nextWord, addSpace, smartPunct, spelling, learn, terminals, speakHover, speakChosen, echoLetters, echoWords, echoSentences;
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

            tabs = new TabControl { Location = new Point(10, 10), Size = new Size(540, 460) };
            tabs.TabPages.Add(PredictionPage());
            tabs.TabPages.Add(SpeechPage());
            tabs.TabPages.Add(AppearancePage());
            tabs.TabPages.Add(VocabularyPage());
            tabs.TabPages.Add(ShortcutsPage());
            Controls.Add(tabs);

            // La fenêtre est ouverte sans bloquer Plume (Show) : DialogResult ne la fermerait pas, on ferme nous-mêmes.
            var ok = new Button { Text = "OK", Location = new Point(286, 480), Size = new Size(84, 28) };
            var cancel = new Button { Text = L.T("Annuler", "Cancel"), Location = new Point(376, 480), Size = new Size(84, 28) };
            var apply = new Button { Text = L.T("Appliquer", "Apply"), Location = new Point(466, 480), Size = new Size(84, 28) };
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

        /// <summary>Onglet affiché (pour rouvrir la fenêtre au même endroit).</summary>
        public int SelectedTab
        {
            get { return tabs.SelectedIndex; }
            set { tabs.SelectedIndex = Math.Max(0, Math.Min(tabs.TabCount - 1, value)); }
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
            var p = Page(L.T("Prédiction", "Prediction"));
            AddLabel(p, L.T("Langue d'écriture :", "Writing language:"), 12, 16);
            language = AddCombo(p, new[] { "Français", "English" }, s.Language == "en" ? 1 : 0, 230, 14, 260);
            AddLabel(p, L.T("Nombre de mots proposés :", "Number of suggestions:"), 12, 50);
            count = new NumericUpDown { Minimum = 1, Maximum = 9, Value = s.PredictionCount, Location = new Point(230, 48), Width = 60 };
            p.Controls.Add(count);
            AddLabel(p, L.T("Choisir un mot avec :", "Choose a word with:"), 12, 84);
            keys = AddCombo(p, new[]
            {
                L.T("les chiffres 1 à 9", "the number keys 1 to 9"), L.T("les touches F1 à F9", "the F1 to F9 keys"),
                L.T("la souris seulement", "the mouse only")
            }, (int)s.Keys, 230, 82, 260);
            AddLabel(p, L.T("Position de la liste :", "List position:"), 12, 118);
            placement = AddCombo(p, new[]
            {
                L.T("sous le curseur de texte", "below the text cursor"), L.T("position fixe (glisser la liste)", "fixed (drag the list)")
            }, (int)s.Placement, 230, 116, 260);
            tabSelects = AddCheck(p, L.T("Tab choisit le premier mot pendant la frappe d'un mot",
                "Tab chooses the first word while typing a word"), s.TabSelectsFirst, 12, 160);
            nextWord = AddCheck(p, L.T("Proposer le mot suivant après une espace", "Suggest the next word after a space"), s.PredictNextWord, 12, 188);
            addSpace = AddCheck(p, L.T("Ajouter une espace après le mot choisi", "Add a space after the chosen word"), s.AddSpace, 12, 216);
            smartPunct = AddCheck(p, L.T("Ponctuation intelligente (retirer l'espace avant . , )",
                "Smart punctuation (remove the space before . , )"), s.SmartPunctuation, 12, 244);
            spelling = AddCheck(p, L.T("Aide orthographique (mots qui se prononcent de la même façon)",
                "Spelling help (words that sound the same)"), s.SpellingHelp, 12, 272);
            var help = new Label
            {
                Text = L.T("Astuce : appuyez sur Échap pour masquer la liste, puis tapez un chiffre ou une tabulation. "
                         + "Sur un clavier AZERTY, utilisez le pavé numérique ou Maj + chiffre.",
                           "Tip: press Esc to hide the list, then type a digit or a Tab. "
                         + "On an AZERTY keyboard, use the numeric keypad or Shift + digit."),
                Location = new Point(12, 318), Size = new Size(498, 70), ForeColor = SystemColors.GrayText
            };
            p.Controls.Add(help);
            return p;
        }

        TabPage SpeechPage()
        {
            var p = Page(L.T("Parole", "Speech"));
            List<string> fr = Speaker.Voices("fr"), en = Speaker.Voices("en");
            AddLabel(p, L.T("Voix française :", "French voice:"), 12, 16);
            voiceFr = AddCombo(p, fr.ToArray(), Math.Max(0, fr.IndexOf(s.VoiceFr)), 170, 14, 250);
            AddLabel(p, L.T("Voix anglaise :", "English voice:"), 12, 50);
            voiceEn = AddCombo(p, en.ToArray(), Math.Max(0, en.IndexOf(s.VoiceEn)), 170, 48, 250);
            var test = new Button { Text = L.T("Écouter", "Listen"), Location = new Point(430, 30), Size = new Size(80, 28) };
            test.Click += delegate
            {
                Collect();
                testVoice(language.SelectedIndex == 1 ? "en" : "fr", s);
            };
            p.Controls.Add(test);
            AddLabel(p, L.T("Vitesse :", "Speed:"), 12, 92);
            rate = new TrackBar { Minimum = -10, Maximum = 10, Value = Math.Max(-10, Math.Min(10, s.Rate)), TickFrequency = 2, Location = new Point(170, 84), Width = 250 };
            p.Controls.Add(rate);
            AddLabel(p, L.T("Volume :", "Volume:"), 12, 136);
            volume = new TrackBar { Minimum = 0, Maximum = 100, Value = Math.Max(0, Math.Min(100, s.Volume)), TickFrequency = 10, Location = new Point(170, 128), Width = 250 };
            p.Controls.Add(volume);
            AddLabel(p, L.T("Lire à voix haute :", "Read aloud:"), 12, 184);
            speakHover = AddCheck(p, L.T("les mots proposés quand la souris passe dessus", "suggested words when the mouse hovers over them"), s.SpeakOnHover, 24, 210);
            speakChosen = AddCheck(p, L.T("le mot choisi dans la liste", "the word chosen from the list"), s.SpeakChosen, 24, 236);
            echoLetters = AddCheck(p, L.T("chaque lettre tapée", "each letter typed"), s.EchoLetters, 24, 262);
            echoWords = AddCheck(p, L.T("chaque mot tapé", "each word typed"), s.EchoWords, 24, 288);
            echoSentences = AddCheck(p, L.T("chaque phrase terminée (. ! ?)", "each finished sentence (. ! ?)"), s.EchoSentences, 24, 314);
            return p;
        }

        TabPage AppearancePage()
        {
            var p = Page(L.T("Apparence", "Appearance"));
            AddLabel(p, L.T("Langue de l'interface :", "Interface language:"), 12, 16);
            uiLang = AddCombo(p, new[] { "Français", "English" }, s.InterfaceLanguage == "en" ? 1 : 0, 170, 14, 250);
            AddLabel(p, L.T("Police de la liste :", "List font:"), 12, 58);
            fontButton = new Button { Location = new Point(170, 54), Size = new Size(250, 30) };
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
            AddLabel(p, L.T("Couleurs :", "Colors:"), 12, 100);
            var themeNames = new string[Theme.Names.Length];
            for (int i = 0; i < themeNames.Length; i++)
                themeNames[i] = Theme.DisplayName(Theme.Names[i]);
            theme = AddCombo(p, themeNames, Array.IndexOf(Theme.Names, s.Theme), 170, 98, 250);
            AddLabel(p, L.T("Barre d'outils :", "Toolbar:"), 12, 142);
            toolbarStyle = AddCombo(p, new[] { L.T("icônes et texte", "icons and text"), L.T("icônes seulement", "icons only") },
                s.ToolbarIconsOnly ? 1 : 0, 170, 140, 250);
            var note = new Label
            {
                Text = L.T("Polices adaptées à la dyslexie : installez par exemple OpenDyslexic ou Andika,\npuis choisissez-la ci-dessus.",
                           "Dyslexia-friendly fonts: install for example OpenDyslexic or Andika,\nthen choose it above."),
                Location = new Point(12, 184), Size = new Size(490, 48), ForeColor = SystemColors.GrayText
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
            var p = Page(L.T("Vocabulaire", "Vocabulary"));
            learn = AddCheck(p, L.T("Apprendre les mots que j'utilise", "Learn the words I use"), s.Learn, 12, 12);
            AddLabel(p, L.T("Proposer un mot nouveau après", "Suggest a new word after"), 12, 42);
            minLearn = new NumericUpDown { Minimum = 1, Maximum = 10, Value = Math.Max(1, Math.Min(10, s.MinLearnCount)), Location = new Point(210, 40), Width = 50 };
            p.Controls.Add(minLearn);
            AddLabel(p, L.T("utilisations", "uses"), 266, 42);
            AddLabel(p, L.T("Langue :", "Language:"), 348, 42);
            vocabLang = AddCombo(p, new[] { "Français", "English" }, s.Language == "en" ? 1 : 0, 420, 40, 90);

            AddLabel(p, L.T("Mes mots :", "My words:"), 12, 76);
            learned = new ListBox { Location = new Point(12, 98), Size = new Size(240, 170), IntegralHeight = false };
            p.Controls.Add(learned);
            newWord = new TextBox { Location = new Point(12, 274), Width = 150 };
            p.Controls.Add(newWord);
            var add = new Button { Text = L.T("Ajouter", "Add"), Location = new Point(166, 272), Size = new Size(86, 26) };
            var remove = new Button { Text = L.T("Retirer", "Remove"), Location = new Point(166, 302), Size = new Size(86, 26) };
            p.Controls.Add(add);
            p.Controls.Add(remove);

            AddLabel(p, L.T("Mots à ne plus proposer :", "Words never to suggest:"), 270, 76);
            blockedList = new ListBox { Location = new Point(270, 98), Size = new Size(240, 170), IntegralHeight = false };
            p.Controls.Add(blockedList);
            var unblock = new Button { Text = L.T("Proposer à nouveau", "Suggest again"), Location = new Point(270, 272), Size = new Size(150, 26) };
            p.Controls.Add(unblock);
            var forget = new Button
            {
                Text = L.T("Oublier tout ce que Plume a appris", "Forget everything Plume has learned"),
                Location = new Point(270, 302), Size = new Size(240, 26)
            };
            p.Controls.Add(forget);

            AddLabel(p, L.T("Abréviations (une par ligne : abréviation = texte) :", "Abbreviations (one per line: abbreviation = text):"), 12, 336);
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
            forget.Click += delegate
            {
                if (MessageBox.Show(this, L.T(
                        "Effacer tous les mots et enchaînements que Plume a appris ou que vous avez ajoutés, en français et en anglais ?\n\n"
                        + "Les abréviations et les mots à ne plus proposer sont conservés.",
                        "Erase all the words and word pairs that Plume has learned or that you added, in French and English?\n\n"
                        + "Abbreviations and words never to suggest are kept."), "Plume",
                        MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes)
                    return;
                foreach (string lang in new[] { "fr", "en" })
                {
                    UserModel u = userFor(lang);
                    if (u == null)
                        continue;
                    u.ForgetLearned();
                    u.Save(); // le fichier est réécrit tout de suite, sans attendre la fermeture
                }
                refresh();
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
            var p = Page(L.T("Raccourcis", "Shortcuts"));
            AddLabel(p, L.T("Lire la sélection / arrêter :", "Read selection / stop:"), 12, 16);
            hotRead = new TextBox { Text = s.HotkeyRead, Location = new Point(230, 14), Width = 200 };
            p.Controls.Add(hotRead);
            AddLabel(p, L.T("Activer / désactiver la prédiction :", "Word prediction on / off:"), 12, 50);
            hotToggle = new TextBox { Text = s.HotkeyToggle, Location = new Point(230, 48), Width = 200 };
            p.Controls.Add(hotToggle);
            var hint = new Label
            {
                Text = L.T("Exemples : Ctrl+Shift+F12, Ctrl+Alt+L, Pause. Laisser vide pour aucun raccourci.",
                           "Examples: Ctrl+Shift+F12, Ctrl+Alt+L, Pause. Leave empty for no shortcut."),
                Location = new Point(12, 80), Size = new Size(490, 22), ForeColor = SystemColors.GrayText
            };
            p.Controls.Add(hint);
            AddLabel(p, L.T("Applications où Plume reste silencieux\n(noms de programme séparés par ;) :",
                "Applications where Plume stays silent\n(program names separated by ;):"), 12, 118);
            excluded = new TextBox { Text = s.ExcludedApps, Location = new Point(12, 158), Width = 498 };
            p.Controls.Add(excluded);
            terminals = AddCheck(p, L.T("Utiliser Plume aussi dans les terminaux (mots de passe non protégés)",
                "Also use Plume in terminals (password prompts are not protected)"), s.PredictInTerminals, 12, 192);
            var priv = new Label
            {
                Text = L.T("Plume ne fait rien dans les champs de mot de passe, et n'apprend rien quand la prédiction est "
                         + "désactivée. Les mots tapés et leurs enchaînements sont notés en clair dans le dossier "
                         + "%APPDATA%\\Plume, qui ne quitte pas l'ordinateur (voir Vocabulaire pour tout effacer).",
                           "Plume does nothing in password fields, and learns nothing while prediction is turned off. "
                         + "Typed words and word pairs are stored in plain text in the %APPDATA%\\Plume folder, "
                         + "which never leaves the computer (see Vocabulary to erase everything)."),
                Location = new Point(12, 228), Size = new Size(498, 100), ForeColor = SystemColors.GrayText
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
            s.TabSelectsFirst = tabSelects.Checked;
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
            s.Theme = Theme.Names[Math.Max(0, theme.SelectedIndex)];
            s.ToolbarIconsOnly = toolbarStyle.SelectedIndex == 1;
            s.InterfaceLanguage = uiLang.SelectedIndex == 1 ? "en" : "fr";
            s.Learn = learn.Checked;
            s.MinLearnCount = (int)minLearn.Value;
            s.HotkeyRead = hotRead.Text.Trim();
            s.HotkeyToggle = hotToggle.Text.Trim();
            s.ExcludedApps = excluded.Text.Trim();
            s.PredictInTerminals = terminals.Checked;
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
                    MessageBox.Show(this, L.T("Raccourci non reconnu : ", "Unrecognized shortcut: ") + hk, "Plume", MessageBoxButtons.OK, MessageBoxIcon.Warning);
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
