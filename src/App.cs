using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Timer = System.Windows.Forms.Timer;

[assembly: System.Reflection.AssemblyTitle("Plume")]
[assembly: System.Reflection.AssemblyDescription("Assistant d'écriture / Writing assistant")]
[assembly: System.Reflection.AssemblyProduct("Plume")]
[assembly: System.Reflection.AssemblyVersion("1.0.2.0")]
[assembly: System.Runtime.Versioning.TargetFramework(".NETFramework,Version=v4.8", FrameworkDisplayName = ".NET Framework 4.8")]

namespace Plume
{
    internal static class Program
    {
        [STAThread]
        static void Main(string[] args)
        {
            string profile = null;
            bool noSpeech = false, quit = false, uninstall = false, quiet = false;
            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] == "--profile" && i + 1 < args.Length)
                    profile = args[++i];
                else if (args[i] == "--no-speech")
                    noSpeech = true;
                else if (args[i] == "--quit")
                    quit = true;
                else if (args[i] == "--uninstall")
                    uninstall = true;
                else if (args[i] == "--quiet")
                    quiet = true;
            }
            if (uninstall)
            {
                // Lancé par Windows depuis « Applications installées ».
                L.Lang = L.FromProfile(Installer.ProfileFolder);
                Application.EnableVisualStyles();
                Installer.Uninstall(quiet);
                return;
            }
            Settings.Folder = profile ?? Installer.ProfileFolder;
            Directory.CreateDirectory(Settings.Folder);
            string id = Installer.InstanceId(Settings.Folder);
            L.Lang = L.FromProfile(Settings.Folder);

            if (quit)
            {
                // « Plume.exe --quit » ferme l'instance en cours (scripts, désinstallation).
                EventWaitHandle ev;
                if (EventWaitHandle.TryOpenExisting(id + ".Quit", out ev))
                    ev.Set();
                return;
            }

            bool created;
            using (var mutex = new Mutex(true, id + ".Instance", out created))
            using (var quitEvent = new EventWaitHandle(false, EventResetMode.AutoReset, id + ".Quit"))
            {
                if (!created)
                {
                    MessageBox.Show(L.T("Plume est déjà ouvert (icône dans la zone de notification).",
                        "Plume is already running (icon in the notification area)."), "Plume",
                        MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.ThreadException += (s, e) => Log.Write("Erreur : " + e.Exception);
                AppDomain.CurrentDomain.UnhandledException += (s, e) => Log.Write("Erreur fatale : " + e.ExceptionObject);
                Log.Write("Démarrage de Plume");
                var controller = new Controller(noSpeech);
                ThreadPool.RegisterWaitForSingleObject(quitEvent, (state, timedOut) => controller.Quit(), null, -1, true);
                Application.Run(controller);
            }
        }
    }

    /// <summary>Texte tapé depuis la dernière fois que le curseur a bougé.</summary>
    internal sealed class TextTracker
    {
        readonly StringBuilder text = new StringBuilder();
        const int Max = 600;
        public bool StartKnown;
        public int KeysSinceReset;

        public string Text
        {
            get { return text.ToString(); }
        }

        public void Reset()
        {
            text.Clear();
            StartKnown = false;
            KeysSinceReset = 0;
        }

        public void Type(string s)
        {
            text.Append(s);
            KeysSinceReset++;
            Trim();
        }

        public void Backspace()
        {
            KeysSinceReset++;
            RemoveLast();
        }

        void RemoveLast()
        {
            if (text.Length == 0)
            {
                StartKnown = false; // on efface du texte que l'on ne connaît pas
                return;
            }
            int n = text.Length >= 2 && char.IsLowSurrogate(text[text.Length - 1]) ? 2 : 1;
            text.Length -= n;
        }

        public void Replace(int backspaces, string insert)
        {
            for (int i = 0; i < backspaces; i++)
                RemoveLast();
            text.Append(insert);
            KeysSinceReset++;
            Trim();
        }

        public void Seed(string before, bool startKnown)
        {
            text.Clear();
            text.Append(before);
            StartKnown = startKnown;
            Trim();
        }

        void Trim()
        {
            if (text.Length > Max)
            {
                text.Remove(0, text.Length - Max);
                StartKnown = false;
            }
        }

        public TextContext Analyze(string lang)
        {
            return Tokenizer.Analyze(text.ToString(), StartKnown, lang);
        }
    }

    internal static class Hotkeys
    {
        public static bool TryParse(string text, out uint mods, out uint vk)
        {
            mods = 0;
            vk = 0;
            if (string.IsNullOrWhiteSpace(text))
                return false;
            foreach (string raw in text.Split('+'))
            {
                string t = raw.Trim().ToLowerInvariant();
                switch (t)
                {
                    case "ctrl":
                    case "control":
                    case "ctl": mods |= Native.MOD_CONTROL; continue;
                    case "shift":
                    case "maj": mods |= Native.MOD_SHIFT; continue;
                    case "alt": mods |= Native.MOD_ALT; continue;
                    case "win": mods |= Native.MOD_WIN; continue;
                    case "espace": t = "space"; break;
                    case "échap":
                    case "echap": t = "escape"; break;
                }
                if (t.Length == 1 && char.IsDigit(t[0]))
                    t = "d" + t;
                Keys key;
                if (vk != 0 || !Enum.TryParse(t, true, out key) || key == Keys.None)
                    return false;
                vk = (uint)key;
            }
            return vk != 0;
        }
    }

    /// <summary>Fenêtre invisible : reçoit les raccourcis globaux et sert de point d'entrée au fil de l'interface.</summary>
    internal sealed class HostWindow : Form
    {
        public event Action<int> Hotkey;

        public HostWindow()
        {
            ShowInTaskbar = false;
            FormBorderStyle = FormBorderStyle.None;
            Opacity = 0;
            Size = new Size(1, 1);
            CreateHandle();
        }

        protected override void SetVisibleCore(bool value)
        {
            base.SetVisibleCore(false);
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == Native.WM_HOTKEY && Hotkey != null)
                Hotkey(m.WParam.ToInt32());
            base.WndProc(ref m);
        }
    }

    /// <summary>Chef d'orchestre : relie clavier, moteur de prédiction, parole et fenêtres.</summary>
    internal sealed class Controller : ApplicationContext
    {
        Settings settings;
        readonly string dataDir;
        readonly HostWindow host = new HostWindow();
        readonly InputHooks hooks = new InputHooks();
        readonly TextTracker tracker = new TextTracker();
        readonly PredictionWindow predWin = new PredictionWindow();
        readonly Toolbar toolbar = new Toolbar();
        readonly NotifyIcon tray = new NotifyIcon();
        readonly Speaker speaker;
        readonly AccessibilityWorker worker;
        readonly Dictionary<string, LanguageModel> models = new Dictionary<string, LanguageModel>();
        readonly Dictionary<string, UserModel> users = new Dictionary<string, UserModel>();
        readonly Timer caretTimer = new Timer { Interval = 25 };
        readonly Timer saveTimer = new Timer { Interval = 60000 };
        readonly Timer focusTimeout = new Timer { Interval = 700 };
        readonly int ownPid = Process.GetCurrentProcess().Id;
        readonly Dictionary<uint, string> processNames = new Dictionary<uint, string>();
        readonly Native.WinEventProc winEventProc;
        readonly List<IntPtr> winHooks = new List<IntPtr>();
        Predictor predictor;
        OptionsForm options;
        AboutForm about;

        List<Prediction> pendingItems;
        PredictionSnapshot pendingSnapshot, shown;
        int lastSeq;
        int caretRequest;
        int resetGeneration;
        IntPtr fgHwnd, focusHwnd;
        string focusTarget = "";
        bool fieldBlocked, focusChecked, learnAllowed, suppressed;
        Rectangle lastCaret = Rectangle.Empty;
        IntPtr lastCaretHwnd;
        Point lastClick = Point.Empty;
        IntPtr lastClickWindow;

        public Controller(bool noSpeech)
        {
            settings = Settings.Load();
            dataDir = Path.Combine(Path.GetDirectoryName(Application.ExecutablePath), "data");
            worker = new AccessibilityWorker(a => Ui(a));
            speaker = new Speaker();
            if (noSpeech)
                settings.SpeechEnabled = false;

            Icon icon = null;
            try
            {
                icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
            }
            catch (Exception)
            {
            }
            tray.Icon = icon ?? SystemIcons.Application;
            tray.Text = "Plume";
            tray.ContextMenuStrip = BuildTrayMenu();
            tray.DoubleClick += delegate { ShowToolbar(true); };
            tray.Visible = true;

            predWin.ItemClicked += OnItemClicked;
            predWin.ItemHovered += i =>
            {
                if (settings.SpeakOnHover && shown != null && i < shown.Words.Length)
                    speaker.Speak(shown.Words[i], true);
            };
            predWin.ItemRightClicked += OnItemRightClicked;
            predWin.Moved += p =>
            {
                settings.FixedX = p.X;
                settings.FixedY = p.Y;
                settings.Save();
            };
            toolbar.Clicked += OnToolbar;
            toolbar.MenuRequested += p => tray.ContextMenuStrip.Show(p);
            toolbar.Moved += p =>
            {
                settings.ToolbarX = p.X;
                settings.ToolbarY = p.Y;
                settings.Save();
            };
            speaker.StateChanged += delegate { Ui(UpdateReadButton); };

            caretTimer.Tick += delegate
            {
                caretTimer.Stop();
                PositionAndShow();
            };
            saveTimer.Tick += delegate { SaveUsers(); };
            saveTimer.Start();
            focusTimeout.Tick += delegate
            {
                focusTimeout.Stop();
                focusChecked = true; // pas de réponse : on prédit, sans apprendre dans ce champ
            };

            host.Hotkey += OnHotkey;
            hooks.Post = e => Ui(() => OnInput(e));
            hooks.IsOwnWindow = h => Native.ProcessIdOf(h) == ownPid;

            winEventProc = OnWinEvent;
            winHooks.Add(Native.SetWinEventHook(Native.EVENT_SYSTEM_FOREGROUND, Native.EVENT_SYSTEM_FOREGROUND, IntPtr.Zero,
                winEventProc, 0, 0, Native.WINEVENT_OUTOFCONTEXT | Native.WINEVENT_SKIPOWNPROCESS));
            winHooks.Add(Native.SetWinEventHook(Native.EVENT_OBJECT_FOCUS, Native.EVENT_OBJECT_FOCUS, IntPtr.Zero,
                winEventProc, 0, 0, Native.WINEVENT_OUTOFCONTEXT | Native.WINEVENT_SKIPOWNPROCESS));

            ApplySettings(false);
            hooks.Start();
            ShowToolbar(settings.ToolbarVisible);
            if (!File.Exists(Path.Combine(Settings.Folder, "reglages.ini")))
            {
                tray.ShowBalloonTip(5000, L.T("Plume est prêt", "Plume is ready"),
                    L.T("Écrivez dans n'importe quelle application : les mots proposés apparaissent sous le curseur. "
                        + "Choisissez-en un avec son chiffre.",
                        "Type in any application: suggested words appear below the cursor. "
                        + "Pick one with its number."), ToolTipIcon.Info);
                settings.Save();
            }
        }

        void Ui(Action a)
        {
            try
            {
                if (!host.IsDisposed)
                    host.BeginInvoke(a);
            }
            catch (InvalidOperationException)
            {
            }
        }

        string Lang
        {
            get { return settings.Language; }
        }

        // ---------------------------------------------------------------- réglages

        void ApplySettings(bool save)
        {
            if (L.Lang != settings.InterfaceLanguage)
            {
                L.Lang = settings.InterfaceLanguage;
                toolbar.Localize();
                ContextMenuStrip old = tray.ContextMenuStrip;
                tray.ContextMenuStrip = BuildTrayMenu();
                if (old != null)
                    old.Dispose();
                UpdateReadButton();
            }
            Theme theme = Theme.Get(settings.Theme);
            predWin.Configure(theme, settings.FontName, settings.FontSize, settings.Keys);
            predWin.Draggable = settings.Placement == Placement.Fixed;
            toolbar.Configure(theme);
            toolbar.SetState("predict", settings.PredictionEnabled);
            toolbar.SetState("speech", settings.SpeechEnabled);
            toolbar.SetLabel("lang", Lang.ToUpperInvariant());
            hooks.SelectionMode = (int)settings.Keys;
            hooks.AttachChars = !settings.SmartPunctuation ? "" : Lang == "fr" ? ".,)…" : ".,;:!?)…";
            speaker.Enabled = settings.SpeechEnabled;
            speaker.Configure(Lang, settings.VoiceFor(Lang), settings.Rate, settings.Volume);
            RegisterHotkeys();
            LoadLanguage(Lang);
            UpdateTrayMenu();
            if (save)
                settings.Save();
            HidePredictions();
        }

        void RegisterHotkeys()
        {
            Native.UnregisterHotKey(host.Handle, 1);
            Native.UnregisterHotKey(host.Handle, 2);
            uint mods, vk;
            if (Hotkeys.TryParse(settings.HotkeyRead, out mods, out vk)
                && !Native.RegisterHotKey(host.Handle, 1, mods | Native.MOD_NOREPEAT, vk))
                Log.Write("Raccourci déjà utilisé : " + settings.HotkeyRead);
            if (Hotkeys.TryParse(settings.HotkeyToggle, out mods, out vk)
                && !Native.RegisterHotKey(host.Handle, 2, mods | Native.MOD_NOREPEAT, vk))
                Log.Write("Raccourci déjà utilisé : " + settings.HotkeyToggle);
        }

        UserModel UserFor(string lang)
        {
            UserModel u;
            if (!users.TryGetValue(lang, out u))
                users[lang] = u = new UserModel(lang, Settings.Folder);
            return u;
        }

        void LoadLanguage(string lang)
        {
            LanguageModel lm;
            if (models.TryGetValue(lang, out lm))
            {
                SetPredictor(lm);
                return;
            }
            predictor = null;
            toolbar.SetLabel("lang", lang.ToUpperInvariant() + "…");
            string path = Path.Combine(dataDir, lang + ".plm");
            Task.Factory.StartNew(() => LanguageModel.Load(path)).ContinueWith(t => Ui(delegate
            {
                if (t.IsFaulted)
                {
                    Log.Write("Chargement du modèle " + path + " : " + t.Exception.InnerException);
                    toolbar.SetLabel("lang", lang.ToUpperInvariant() + " !");
                    tray.ShowBalloonTip(8000, "Plume", L.T("Dictionnaire introuvable ou abîmé : ", "Dictionary missing or damaged: ") + path, ToolTipIcon.Error);
                    return;
                }
                models[lang] = t.Result;
                if (Lang == lang)
                    SetPredictor(t.Result);
                // Le fichier décompressé n'est plus utile : on rend la mémoire tout de suite.
                GC.Collect();
            }));
        }

        void SetPredictor(LanguageModel lm)
        {
            predictor = new Predictor(lm, UserFor(lm.Lang))
            {
                SpellingHelp = settings.SpellingHelp,
                MinLearnCount = settings.MinLearnCount
            };
            toolbar.SetLabel("lang", lm.Lang.ToUpperInvariant());
        }

        void SaveUsers()
        {
            foreach (var u in users.Values)
                u.Save();
        }

        // ---------------------------------------------------------------- frappe

        bool IsOwn(IntPtr hwnd)
        {
            return hwnd != IntPtr.Zero && Native.ProcessIdOf(hwnd) == ownPid;
        }

        void OnInput(KeyEvent e)
        {
            lastSeq = e.Seq;
            if (e.Kind == KeyKind.MouseDown)
            {
                lastClick = new Point(e.Point.X, e.Point.Y);
                lastClickWindow = Native.GetForegroundWindow();
                ResetContext(false);
                return;
            }
            if (IsOwn(e.Foreground))
            {
                HidePredictions();
                return;
            }
            CheckFocus(e.Foreground);

            TextContext before = tracker.Analyze(Lang);
            switch (e.Kind)
            {
                case KeyKind.Text:
                    tracker.Type(e.Text);
                    OnTyped(before, e.Text);
                    break;
                case KeyKind.Backspace:
                    tracker.Backspace();
                    break;
                case KeyKind.Enter:
                    tracker.Type("\n");
                    suppressed = false;
                    break;
                case KeyKind.Reset:
                    ResetContext(false);
                    return;
                case KeyKind.Escape:
                    suppressed = true;
                    HidePredictions();
                    return;
                case KeyKind.Selected:
                    ApplySelection(e.Snapshot, e.Index, before);
                    break;
                case KeyKind.Punctuated:
                    tracker.Backspace();
                    tracker.Type(e.Text);
                    OnTyped(tracker.Analyze(Lang), e.Text);
                    break;
            }
            UpdatePredictions();
        }

        static bool IsSeparator(string text)
        {
            foreach (char c in text)
                if (char.IsLetterOrDigit(c) || c == '\'' || c == '-' || c == '’')
                    return false;
            return text.Length > 0;
        }

        static bool HasLetter(string s)
        {
            foreach (char c in s)
                if (char.IsLetter(c))
                    return true;
            return false;
        }

        void OnTyped(TextContext before, string text)
        {
            if (fieldBlocked)
                return;
            bool sep = IsSeparator(text);
            if (sep)
                suppressed = false;
            if (settings.EchoLetters && text.Length == 1 && char.IsLetter(text[0]))
                speaker.Speak(text, true);
            bool sentenceEnd = text.IndexOfAny(new[] { '.', '!', '?', '…' }) >= 0;
            if (sep && HasLetter(before.Partial))
            {
                string word = before.Partial.TrimEnd('-', '\'');
                Learn(before.Prev1, word, before.SentenceStart);
                if (settings.EchoWords && !(sentenceEnd && settings.EchoSentences))
                    speaker.Speak(word, false);
            }
            if (sentenceEnd && settings.EchoSentences)
            {
                string all = tracker.Text;
                // « 3.5 » ou « ... » répétés : on ne lit qu'une fin de phrase après une lettre.
                char prev = all.Length >= 2 ? all[all.Length - 2] : ' ';
                if (char.IsLetter(prev) || prev == ')' || prev == '"' || prev == '»' || prev == ' ')
                {
                    string sentence = Tokenizer.LastSentence(all);
                    if (HasLetter(sentence))
                        speaker.Speak(sentence, !settings.EchoWords);
                }
            }
        }

        void Learn(string prev, string word, bool sentenceStart)
        {
            // Prédiction désactivée (bouton Prédire, Ctrl+Maj+F11) : c'est une pause complète, rien n'est noté.
            if (!settings.Learn || !settings.PredictionEnabled || !learnAllowed || predictor == null
                || word.Length < 2 || word.IndexOf(' ') >= 0)
                return;
            LanguageModel lm = predictor.Model;
            if (lm.IdOf(word) < 0)
            {
                // Mot inconnu qui se prononce comme un mot connu : probablement une faute, on ne l'apprend pas.
                if (word.Length > 25 || lm.PhonSet.Contains(TextUtil.Phonetic(word, lm.Lang)))
                    return;
            }
            predictor.User.Observe(prev, word, sentenceStart);
        }

        void ApplySelection(PredictionSnapshot snap, int index, TextContext before)
        {
            tracker.Replace(snap.Backspaces[index], snap.Inserts[index]);
            suppressed = false;
            string word = snap.Words[index];
            if (settings.SpeakChosen)
                speaker.Speak(word, true);
            Learn(before.Prev1, word, before.SentenceStart);
        }

        void OnItemClicked(int index)
        {
            PredictionSnapshot snap = shown;
            if (snap == null || index >= snap.Words.Length)
                return;
            TextContext before = tracker.Analyze(Lang);
            hooks.Snapshot = null;
            Inserter.Send(snap.Backspaces[index], snap.Inserts[index]);
            hooks.AutoSpaceArmed = snap.Inserts[index].EndsWith(" ");
            ApplySelection(snap, index, before);
            UpdatePredictions();
        }

        void OnItemRightClicked(int index, Point screen)
        {
            PredictionSnapshot snap = shown;
            if (snap == null || index >= snap.Words.Length)
                return;
            string word = snap.Words[index];
            IntPtr fg = Native.GetForegroundWindow();
            var menu = new ContextMenuStrip();
            menu.Items.Add(L.T("Écouter « " + word + " »", "Listen to “" + word + "”"), null, delegate { speaker.SpeakAlways(word); });
            menu.Items.Add(L.T("Ne plus proposer « " + word + " »", "Never suggest “" + word + "”"), null, delegate
            {
                if (predictor != null)
                    predictor.User.Block(word);
                HidePredictions();
            });
            menu.Closed += delegate
            {
                Native.SetForegroundWindow(fg);
                Ui(() => menu.Dispose());
            };
            menu.Show(screen);
        }

        // ---------------------------------------------------------------- champ actif

        static IntPtr FocusOf(IntPtr fg, out Rectangle caret)
        {
            caret = Rectangle.Empty;
            uint pid;
            uint tid = Native.GetWindowThreadProcessId(fg, out pid);
            var gi = new Native.GUITHREADINFO { cbSize = Marshal.SizeOf(typeof(Native.GUITHREADINFO)) };
            if (!Native.GetGUIThreadInfo(tid, ref gi))
                return fg;
            if (gi.hwndCaret != IntPtr.Zero && gi.rcCaret.Bottom > gi.rcCaret.Top)
            {
                var p = new Native.POINT { X = gi.rcCaret.Left, Y = gi.rcCaret.Top };
                Native.ClientToScreen(gi.hwndCaret, ref p);
                if (p.X != 0 || p.Y != 0)
                    caret = new Rectangle(p.X, p.Y, Math.Max(1, gi.rcCaret.Right - gi.rcCaret.Left), gi.rcCaret.Bottom - gi.rcCaret.Top);
            }
            return gi.hwndFocus != IntPtr.Zero ? gi.hwndFocus : fg;
        }

        void CheckFocus(IntPtr fg)
        {
            Rectangle caret;
            IntPtr focus = FocusOf(fg, out caret);
            if (fg != fgHwnd || focus != focusHwnd)
            {
                fgHwnd = fg;
                focusHwnd = focus;
                ResetContext(true);
            }
        }

        void OnWinEvent(IntPtr hook, uint evt, IntPtr hwnd, int idObject, int idChild, uint thread, uint time)
        {
            if (IsOwn(hwnd))
                return;
            string target = hwnd.ToString() + "/" + idObject + "/" + idChild;
            if (evt == Native.EVENT_OBJECT_FOCUS && target == focusTarget)
                return;
            focusTarget = target;
            IntPtr fg = Native.GetForegroundWindow();
            if (IsOwn(fg))
                return;
            Rectangle caret;
            fgHwnd = fg;
            focusHwnd = FocusOf(fg, out caret);
            ResetContext(true);
        }

        void ResetContext(bool newField)
        {
            tracker.Reset();
            suppressed = false;
            HidePredictions();
            IntPtr fg = Native.GetForegroundWindow();
            if (IsOwn(fg))
                return;
            if (newField)
            {
                fieldBlocked = IsExcluded(fg);
                focusChecked = false;
                learnAllowed = false;
                focusTimeout.Stop();
                focusTimeout.Start();
            }
            Rectangle caret;
            IntPtr focus = FocusOf(fg, out caret);
            int generation = ++resetGeneration;
            worker.Run("focus", () => AccessibilityWorker.InspectFocus(focus), r =>
            {
                var info = r as FocusInfo;
                if (newField && fg == Native.GetForegroundWindow())
                {
                    focusTimeout.Stop();
                    focusChecked = true;
                    bool password = info != null && info.IsPassword;
                    fieldBlocked = password || IsExcluded(fg);
                    learnAllowed = info != null && !password;
                    if (fieldBlocked)
                        HidePredictions();
                    else if (generation == resetGeneration && tracker.KeysSinceReset > 0)
                        UpdatePredictions();
                }
                // Contexte réel avant le curseur (si l'utilisateur n'a encore rien tapé).
                if (info != null && info.TextBefore != null && generation == resetGeneration
                    && tracker.KeysSinceReset == 0 && !info.IsPassword)
                    tracker.Seed(info.TextBefore, info.StartKnown);
            });
        }

        string ProcessNameOf(IntPtr hwnd)
        {
            uint pid = Native.ProcessIdOf(hwnd);
            string name;
            if (!processNames.TryGetValue(pid, out name))
            {
                name = AccessibilityWorker.ProcessName(pid).ToLowerInvariant();
                if (processNames.Count > 200)
                    processNames.Clear();
                processNames[pid] = name;
            }
            return name;
        }

        bool IsTerminal(IntPtr hwnd)
        {
            var sb = new StringBuilder(64);
            Native.GetClassName(hwnd, sb, sb.Capacity);
            return Terminals.Is(sb.ToString(), ProcessNameOf(hwnd));
        }

        bool IsExcluded(IntPtr fg)
        {
            // Les invites de mot de passe des terminaux (sudo, ssh) ne sont pas protégées par Windows.
            if (!settings.PredictInTerminals && IsTerminal(fg))
                return true;
            string name = ProcessNameOf(fg);
            foreach (string ex in settings.ExcludedApps.Split(';', ','))
            {
                string n = ex.Trim().ToLowerInvariant();
                if (n.EndsWith(".exe"))
                    n = n.Substring(0, n.Length - 4);
                if (n.Length > 0 && n == name)
                    return true;
            }
            return false;
        }

        // ---------------------------------------------------------------- propositions

        void HidePredictions()
        {
            hooks.Snapshot = null;
            shown = null;
            pendingSnapshot = null;
            caretTimer.Stop();
            if (predWin.Visible)
                predWin.Hide();
        }

        void UpdatePredictions()
        {
            if (predictor == null || !settings.PredictionEnabled || fieldBlocked || !focusChecked || suppressed)
            {
                HidePredictions();
                return;
            }
            TextContext ctx = tracker.Analyze(Lang);
            bool show = ctx.Partial.Length > 0
                ? HasLetter(ctx.Partial)
                : settings.PredictNextWord && ctx.AfterSeparator && ctx.Prev1 != null;
            if (!show)
            {
                HidePredictions();
                return;
            }
            predictor.SpellingHelp = settings.SpellingHelp;
            predictor.MinLearnCount = settings.MinLearnCount;
            List<Prediction> preds = predictor.Predict(ctx, settings.PredictionCount);
            if (preds.Count == 0)
            {
                HidePredictions();
                return;
            }
            var snap = new PredictionSnapshot
            {
                Words = new string[preds.Count],
                Backspaces = new int[preds.Count],
                Inserts = new string[preds.Count],
                Seq = lastSeq
            };
            string partial = ctx.Partial;
            for (int i = 0; i < preds.Count; i++)
            {
                string w = preds[i].Text;
                int common = 0;
                while (common < partial.Length && common < w.Length && partial[common] == w[common])
                    common++;
                bool space = settings.AddSpace && !w.EndsWith("'") && !w.EndsWith("-");
                snap.Words[i] = w;
                snap.Backspaces[i] = partial.Length - common;
                snap.Inserts[i] = w.Substring(common) + (space ? " " : "");
            }
            hooks.Snapshot = null;
            shown = null;
            pendingItems = preds;
            pendingSnapshot = snap;
            caretTimer.Stop();
            caretTimer.Start();
        }

        void PositionAndShow()
        {
            if (pendingSnapshot == null)
                return;
            predWin.SetItems(pendingItems);
            if (settings.Placement == Placement.Fixed)
            {
                if (settings.FixedX >= 0)
                    predWin.ShowAt(new Point(settings.FixedX, settings.FixedY));
                else
                    predWin.ShowAt(new Point(toolbar.Left, toolbar.Bottom + 8));
                Publish();
                return;
            }
            IntPtr fg = Native.GetForegroundWindow();
            Rectangle caret;
            IntPtr focus = FocusOf(fg, out caret);
            if (!caret.IsEmpty)
            {
                lastCaret = caret;
                lastCaretHwnd = focus;
                predWin.ShowNear(caret);
                Publish();
                return;
            }
            if (lastCaretHwnd == focus && !lastCaret.IsEmpty)
            {
                predWin.ShowNear(lastCaret);
                Publish();
            }
            int request = ++caretRequest;
            PredictionSnapshot snap = pendingSnapshot;
            worker.Run("caret", () => AccessibilityWorker.FindCaret(focus), r =>
            {
                if (request != caretRequest || pendingSnapshot != snap)
                    return;
                var rect = r is Rectangle ? (Rectangle)r : Rectangle.Empty;
                if (!rect.IsEmpty)
                {
                    lastCaret = rect;
                    lastCaretHwnd = focus;
                    predWin.ShowNear(rect);
                }
                else if (!predWin.Visible)
                {
                    ShowFallback(fg);
                }
                Publish();
            });
        }

        /// <summary>Curseur introuvable : près du dernier clic dans cette fenêtre, sinon sous la barre d'outils.</summary>
        void ShowFallback(IntPtr fg)
        {
            if (lastClickWindow == fg && lastClick != Point.Empty)
                predWin.ShowNear(new Rectangle(lastClick.X, lastClick.Y - 10, 2, 20));
            else if (toolbar.Visible)
                predWin.ShowAt(new Point(toolbar.Left, toolbar.Bottom + 8));
            else
            {
                Rectangle wa = Screen.PrimaryScreen.WorkingArea;
                predWin.ShowAt(new Point(wa.Right - predWin.Width - 24, wa.Bottom - predWin.Height - 24));
            }
        }

        void Publish()
        {
            if (pendingSnapshot == null)
                return;
            shown = pendingSnapshot;
            hooks.Snapshot = shown;
        }

        // ---------------------------------------------------------------- lecture

        void OnHotkey(int id)
        {
            if (id == 1)
                ReadAloud();
            else if (id == 2)
                Toggle("predict");
        }

        Task<object> RunWorker(string slot, Func<object> work, int timeoutMs)
        {
            var tcs = new TaskCompletionSource<object>();
            worker.Run(slot, work, r => tcs.TrySetResult(r));
            Task.Delay(timeoutMs).ContinueWith(_ => tcs.TrySetResult(null));
            return tcs.Task;
        }

        async void ReadAloud()
        {
            if (speaker.IsSpeaking)
            {
                speaker.Stop();
                return;
            }
            IntPtr fg = Native.GetForegroundWindow();
            if (IsOwn(fg))
                return;
            string text = null, source = "";
            if (!fieldBlocked)
            {
                object sel = await RunWorker("read", AccessibilityWorker.SelectedText, 800);
                text = sel as string;
                source = "sélection";
                if (text == null && !IsTerminal(fg))
                {
                    text = await CopySelection();
                    source = "copie";
                }
                if (string.IsNullOrWhiteSpace(text))
                {
                    text = Tokenizer.LastSentence(tracker.Text);
                    source = "dernière phrase";
                }
                if (string.IsNullOrWhiteSpace(text))
                {
                    text = await RunWorker("read", AccessibilityWorker.CurrentParagraph, 800) as string;
                    source = "paragraphe";
                }
            }
            // Le journal ne garde que la longueur, jamais le texte lu.
            Log.Write("Lecture (" + source + ") : " + (text == null ? 0 : text.Trim().Length) + " caractères");
            if (string.IsNullOrWhiteSpace(text))
            {
                tray.ShowBalloonTip(3000, "Plume", L.T("Sélectionnez du texte à lire, puis appuyez sur « Lire ».",
                    "Select some text, then press “Read”."), ToolTipIcon.Info);
                return;
            }
            speaker.SpeakAlways(text);
        }

        /// <summary>Copie la sélection avec Ctrl+C, puis remet le presse-papiers comme avant.</summary>
        async Task<string> CopySelection()
        {
            for (int i = 0; i < 50 && (Native.IsKeyDown(0x10) || Native.IsKeyDown(0x11) || Native.IsKeyDown(0x12)
                || Native.IsKeyDown(0x5B) || Native.IsKeyDown(0x5C)); i++)
                await Task.Delay(20);
            // Sans sauvegarde complète du presse-papiers, on ne copie rien : on ne perd jamais ce que l'utilisateur y avait mis.
            DataObject saved = new DataObject();
            try
            {
                IDataObject old = Clipboard.GetDataObject();
                if (old != null)
                {
                    foreach (string f in old.GetFormats(false))
                    {
                        object d = old.GetData(f, false);
                        if (d != null)
                            saved.SetData(f, d);
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Write("Presse-papiers illisible, lecture par copie annulée : " + ex.Message);
                return null;
            }
            uint seq = Native.GetClipboardSequenceNumber();
            Inserter.SendCopy();
            for (int i = 0; i < 25 && Native.GetClipboardSequenceNumber() == seq; i++)
                await Task.Delay(20);
            if (Native.GetClipboardSequenceNumber() == seq)
                return null;
            await Task.Delay(30);
            string text = null;
            try
            {
                text = Clipboard.GetText();
            }
            catch (Exception)
            {
            }
            try
            {
                if (saved.GetFormats().Length > 0)
                    Clipboard.SetDataObject(saved, true);
                else
                    Clipboard.Clear();
            }
            catch (Exception ex)
            {
                Log.Write("Restauration du presse-papiers : " + ex.Message);
            }
            return text;
        }

        void UpdateReadButton()
        {
            if (speaker.IsSpeaking)
                toolbar.SetGlyph("read", "", L.T("Arrêter", "Stop"));
            else
                toolbar.SetGlyph("read", "", L.T("Lire", "Read"));
        }

        // ---------------------------------------------------------------- barre d'outils et menu

        void OnToolbar(string id)
        {
            switch (id)
            {
                case "predict":
                case "speech":
                    Toggle(id);
                    break;
                case "read":
                    ReadAloud();
                    break;
                case "lang":
                    SwitchLanguage(Lang == "fr" ? "en" : "fr");
                    break;
                case "options":
                    ShowOptions();
                    break;
                case "hide":
                    ShowToolbar(false);
                    tray.ShowBalloonTip(3000, "Plume", L.T("Plume continue de fonctionner. Double-cliquez sur l'icône pour revoir la barre.",
                        "Plume keeps running. Double-click the icon to bring the toolbar back."), ToolTipIcon.Info);
                    break;
                case "quit":
                    // Confirmation : un clic de travers ne doit pas couper l'aide à l'écriture.
                    if (MessageBox.Show(toolbar, L.T("Quitter Plume ?\n\nLa prédiction de mots et la lecture s'arrêteront.",
                        "Quit Plume?\n\nWord prediction and reading aloud will stop."), "Plume",
                        MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) == DialogResult.Yes)
                        ExitThread();
                    break;
            }
        }

        void Toggle(string id)
        {
            if (id == "predict")
                settings.PredictionEnabled = !settings.PredictionEnabled;
            else
                settings.SpeechEnabled = !settings.SpeechEnabled;
            if (!settings.SpeechEnabled)
                speaker.Stop();
            ApplySettings(true);
            if (id == "speech" && settings.SpeechEnabled)
                speaker.Speak(Lang == "fr" ? "Parole activée" : "Speech on", true);
        }

        void SwitchLanguage(string lang)
        {
            settings.Language = lang;
            ApplySettings(true);
            tracker.Reset();
        }

        void ShowToolbar(bool visible)
        {
            settings.ToolbarVisible = visible;
            if (visible)
            {
                if (!toolbar.Visible)
                {
                    toolbar.Show();
                    if (settings.ToolbarX >= 0 && Screen.FromPoint(new Point(settings.ToolbarX, settings.ToolbarY))
                        .WorkingArea.Contains(settings.ToolbarX, settings.ToolbarY))
                        toolbar.Location = new Point(settings.ToolbarX, settings.ToolbarY);
                    else
                        toolbar.PlaceDefault();
                }
                toolbar.BringToTop();
            }
            else
            {
                toolbar.Hide();
            }
            UpdateTrayMenu();
            settings.Save();
        }

        ToolStripMenuItem menuToolbar, menuPredict, menuSpeech, menuFr, menuEn, menuStartup;

        static bool StartsWithWindows
        {
            get { return Installer.StartsWithWindows; }
            set { Installer.SetStartup(value, Application.ExecutablePath); }
        }

        ContextMenuStrip BuildTrayMenu()
        {
            var m = new ContextMenuStrip();
            menuToolbar = new ToolStripMenuItem(L.T("Afficher la barre d'outils", "Show toolbar"), null, delegate { ShowToolbar(!settings.ToolbarVisible); });
            menuPredict = new ToolStripMenuItem(L.T("Prédiction de mots", "Word prediction"), null, delegate { Toggle("predict"); });
            menuSpeech = new ToolStripMenuItem(L.T("Retour vocal", "Speech feedback"), null, delegate { Toggle("speech"); });
            menuFr = new ToolStripMenuItem("Français", null, delegate { SwitchLanguage("fr"); });
            menuEn = new ToolStripMenuItem("English", null, delegate { SwitchLanguage("en"); });
            var lang = new ToolStripMenuItem(L.T("Langue d'écriture", "Writing language"));
            lang.DropDownItems.Add(menuFr);
            lang.DropDownItems.Add(menuEn);
            m.Items.Add(menuToolbar);
            m.Items.Add(new ToolStripSeparator());
            m.Items.Add(menuPredict);
            m.Items.Add(menuSpeech);
            m.Items.Add(lang);
            m.Items.Add(new ToolStripSeparator());
            m.Items.Add(L.T("Lire la sélection", "Read selection"), null, delegate { ReadAloud(); });
            m.Items.Add("Options…", null, delegate { ShowOptions(); });
            menuStartup = new ToolStripMenuItem(L.T("Lancer Plume au démarrage de Windows", "Start Plume with Windows"), null, delegate
            {
                try
                {
                    StartsWithWindows = !StartsWithWindows;
                }
                catch (Exception ex)
                {
                    Log.Write("Démarrage automatique : " + ex.Message);
                }
                UpdateTrayMenu();
            });
            m.Items.Add(menuStartup);
            m.Items.Add(L.T("Ouvrir le dossier de mes mots", "Open my words folder"), null, delegate { Process.Start("explorer.exe", "\"" + Settings.Folder + "\""); });
            m.Items.Add(new ToolStripSeparator());
            m.Items.Add(L.T("À propos de Plume…", "About Plume…"), null, delegate { ShowAbout(); });
            m.Items.Add(L.T("Quitter Plume", "Quit Plume"), null, delegate { ExitThread(); });
            return m;
        }

        void UpdateTrayMenu()
        {
            if (menuToolbar == null)
                return;
            menuToolbar.Checked = settings.ToolbarVisible;
            menuPredict.Checked = settings.PredictionEnabled;
            menuSpeech.Checked = settings.SpeechEnabled;
            menuFr.Checked = Lang == "fr";
            menuEn.Checked = Lang == "en";
            try
            {
                menuStartup.Checked = StartsWithWindows;
            }
            catch (Exception)
            {
            }
            tray.Text = "Plume (" + (Lang == "fr" ? L.T("français", "French") : L.T("anglais", "English")) + ")";
        }

        void ShowOptions()
        {
            if (options != null && !options.IsDisposed)
            {
                options.Activate();
                return;
            }
            options = new OptionsForm(settings, UserFor, (lang, s) =>
            {
                speaker.Configure(lang, s.VoiceFor(lang), s.Rate, s.Volume);
                speaker.SpeakAlways(lang == "fr" ? "Bonjour, je suis la voix de Plume." : "Hello, this is the voice of Plume.");
            });
            try
            {
                options.Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
            }
            catch (Exception)
            {
            }
            options.Applied += s =>
            {
                bool uiLanguageChanged = s.InterfaceLanguage != settings.InterfaceLanguage;
                int x = settings.ToolbarX, y = settings.ToolbarY, fx = settings.FixedX, fy = settings.FixedY;
                bool tb = settings.ToolbarVisible, pe = settings.PredictionEnabled, se = settings.SpeechEnabled;
                settings = s;
                settings.ToolbarX = x;
                settings.ToolbarY = y;
                settings.FixedX = fx;
                settings.FixedY = fy;
                settings.ToolbarVisible = tb;
                settings.PredictionEnabled = pe;
                settings.SpeechEnabled = se;
                ApplySettings(true);
                SaveUsers();
                if (uiLanguageChanged)
                    Ui(ReopenOptions);
            };
            options.FormClosed += delegate
            {
                speaker.Configure(Lang, settings.VoiceFor(Lang), settings.Rate, settings.Volume);
                SaveUsers();
                options = null;
            };
            options.Show();
            options.Activate();
        }

        void ShowAbout()
        {
            if (about == null || about.IsDisposed)
            {
                about = new AboutForm();
                try
                {
                    about.Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
                }
                catch (Exception)
                {
                }
                about.FormClosed += delegate { about = null; };
                about.Show();
            }
            about.Activate();
        }

        /// <summary>Après « Appliquer » avec une autre langue d'interface : la fenêtre reparaît traduite.</summary>
        void ReopenOptions()
        {
            if (options == null || options.IsDisposed || !options.Visible)
                return; // fermée par « OK »
            int tab = options.SelectedTab;
            options.Close();
            ShowOptions();
            options.SelectedTab = tab;
        }

        /// <summary>Peut être appelé depuis n'importe quel fil.</summary>
        public void Quit()
        {
            Ui(ExitThread);
        }

        protected override void ExitThreadCore()
        {
            hooks.Dispose();
            foreach (IntPtr h in winHooks)
                Native.UnhookWinEvent(h);
            SaveUsers();
            settings.Save();
            speaker.Dispose();
            tray.Visible = false;
            tray.Dispose();
            predWin.Close();
            toolbar.Close();
            host.Close();
            Log.Write("Arrêt de Plume");
            base.ExitThreadCore();
        }
    }
}
