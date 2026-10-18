using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace Plume
{
    internal enum KeyKind
    {
        Text,        // caractère(s) tapé(s)
        Backspace,
        Enter,
        Reset,       // le curseur a pu bouger (flèches, raccourci, clic…)
        Escape,
        Selected,    // un mot proposé a été inséré au clavier
        Punctuated,  // ponctuation collée au mot inséré (espace automatique retirée)
        MouseDown
    }

    internal sealed class KeyEvent
    {
        public KeyKind Kind;
        public string Text;
        public int Index;
        public IntPtr Foreground;
        public PredictionSnapshot Snapshot;
        public Native.POINT Point;
        public int Seq;          // numéro de l'état du texte après cet événement
    }

    /// <summary>Propositions affichées, telles que le crochet clavier peut les insérer.</summary>
    internal sealed class PredictionSnapshot
    {
        public string[] Words;
        public int[] Backspaces;
        public string[] Inserts;
        public int Seq;          // état du texte pour lequel ces propositions ont été calculées
    }

    /// <summary>
    /// Crochets clavier et souris globaux, sur un fil dédié pour ne jamais ralentir la frappe.
    /// Le fil décide seul (et vite) s'il faut intercepter une touche de sélection ;
    /// tout le reste est transmis au fil de l'interface par <see cref="Post"/>.
    /// </summary>
    internal sealed class InputHooks : IDisposable
    {
        public static readonly UIntPtr Magic = new UIntPtr(0x504C554D); // « PLUM » : nos propres frappes

        const int VK_BACK = 0x08, VK_TAB = 0x09, VK_RETURN = 0x0D, VK_SHIFT = 0x10, VK_CONTROL = 0x11,
            VK_MENU = 0x12, VK_CAPITAL = 0x14, VK_ESCAPE = 0x1B, VK_PRIOR = 0x21, VK_DOWN = 0x28,
            VK_INSERT = 0x2D, VK_DELETE = 0x2E, VK_LWIN = 0x5B, VK_RWIN = 0x5C, VK_APPS = 0x5D,
            VK_F1 = 0x70, VK_F24 = 0x87, VK_NUMLOCK = 0x90, VK_SCROLL = 0x91, VK_LSHIFT = 0xA0,
            VK_RMENU = 0xA5, VK_PACKET = 0xE7;

        // État partagé avec le fil de l'interface.
        public volatile PredictionSnapshot Snapshot;
        public volatile bool AutoSpaceArmed;
        public volatile string AttachChars = ".,)…";
        public volatile int SelectionMode = (int)SelectionKeys.Digits;

        public Action<KeyEvent> Post;
        public Func<IntPtr, bool> IsOwnWindow;

        int changeSeq;           // modifié uniquement sur le fil des crochets

        Thread thread;
        uint threadId;
        IntPtr kbHook, msHook;
        Native.LowLevelProc kbProc, msProc;
        readonly HashSet<uint> swallowedUp = new HashSet<uint>();
        char pendingDead;
        readonly ManualResetEvent started = new ManualResetEvent(false);

        public void Start()
        {
            thread = new Thread(Run) { IsBackground = true, Name = "Plume hooks" };
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            started.WaitOne(3000);
        }

        void Run()
        {
            threadId = Native.GetCurrentThreadId();
            kbProc = KeyboardProc;
            msProc = MouseProc;
            IntPtr mod = Native.GetModuleHandle(null);
            kbHook = Native.SetWindowsHookEx(Native.WH_KEYBOARD_LL, kbProc, mod, 0);
            msHook = Native.SetWindowsHookEx(Native.WH_MOUSE_LL, msProc, mod, 0);
            if (kbHook == IntPtr.Zero)
                Log.Write("Impossible d'installer le crochet clavier : " + Marshal.GetLastWin32Error());
            started.Set();
            Native.MSG msg;
            while (Native.GetMessage(out msg, IntPtr.Zero, 0, 0) > 0)
            {
                Native.TranslateMessage(ref msg);
                Native.DispatchMessage(ref msg);
            }
            if (kbHook != IntPtr.Zero)
                Native.UnhookWindowsHookEx(kbHook);
            if (msHook != IntPtr.Zero)
                Native.UnhookWindowsHookEx(msHook);
        }

        public void Dispose()
        {
            if (thread != null)
            {
                Native.PostThreadMessage(threadId, Native.WM_QUIT, IntPtr.Zero, IntPtr.Zero);
                thread.Join(1000);
                thread = null;
            }
        }

        IntPtr MouseProc(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0)
            {
                int msg = wParam.ToInt32();
                if (msg == Native.WM_LBUTTONDOWN || msg == Native.WM_RBUTTONDOWN || msg == Native.WM_MBUTTONDOWN
                    || msg == Native.WM_XBUTTONDOWN)
                {
                    var m = (Native.MSLLHOOKSTRUCT)Marshal.PtrToStructure(lParam, typeof(Native.MSLLHOOKSTRUCT));
                    var own = IsOwnWindow;
                    if (m.dwExtraInfo != Magic && (own == null || !own(Native.WindowFromPoint(m.pt))))
                    {
                        // Un clic ailleurs déplace sans doute le curseur de texte.
                        AutoSpaceArmed = false;
                        EmitChange(new KeyEvent { Point = m.pt }, KeyKind.MouseDown);
                    }
                }
            }
            return Native.CallNextHookEx(msHook, nCode, wParam, lParam);
        }

        IntPtr KeyboardProc(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0)
            {
                try
                {
                    var k = (Native.KBDLLHOOKSTRUCT)Marshal.PtrToStructure(lParam, typeof(Native.KBDLLHOOKSTRUCT));
                    if (k.dwExtraInfo != Magic)
                    {
                        int msg = wParam.ToInt32();
                        if (msg == Native.WM_KEYDOWN || msg == Native.WM_SYSKEYDOWN)
                        {
                            if (OnKeyDown(k))
                            {
                                swallowedUp.Add(k.vkCode);
                                return (IntPtr)1;
                            }
                        }
                        else if (swallowedUp.Remove(k.vkCode))
                        {
                            return (IntPtr)1;
                        }
                    }
                }
                catch (Exception ex)
                {
                    Log.Write("Crochet clavier : " + ex);
                }
            }
            return Native.CallNextHookEx(kbHook, nCode, wParam, lParam);
        }

        void Emit(KeyEvent e)
        {
            var post = Post;
            if (post != null)
                post(e);
        }

        static bool IsModifier(uint vk)
        {
            return vk == VK_SHIFT || vk == VK_CONTROL || vk == VK_MENU || (vk >= VK_LSHIFT && vk <= VK_RMENU)
                || vk == VK_LWIN || vk == VK_RWIN || vk == VK_CAPITAL || vk == VK_NUMLOCK || vk == VK_SCROLL;
        }

        /// <summary>Renvoie vrai pour intercepter la touche.</summary>
        bool OnKeyDown(Native.KBDLLHOOKSTRUCT k)
        {
            uint vk = k.vkCode;
            if (IsModifier(vk))
                return false;

            bool armed = AutoSpaceArmed;
            AutoSpaceArmed = false;
            PredictionSnapshot snap = Snapshot;

            bool shift = Native.IsKeyDown(VK_SHIFT);
            bool ctrl = Native.IsKeyDown(VK_CONTROL);
            bool alt = Native.IsKeyDown(VK_MENU);
            bool win = Native.IsKeyDown(VK_LWIN) || Native.IsKeyDown(VK_RWIN);
            bool altGr = ctrl && Native.IsKeyDown(VK_RMENU);
            IntPtr fg = Native.GetForegroundWindow();
            var ev = new KeyEvent { Foreground = fg };

            if (win || (ctrl && !altGr) || (alt && !ctrl))
            {
                // Raccourci : copier ne change rien, le reste peut déplacer le curseur.
                if (!(ctrl && !alt && !win && (vk == 'C' || vk == VK_INSERT)))
                    EmitChange(ev, KeyKind.Reset);
                return false;
            }

            switch (vk)
            {
                case VK_BACK:
                    pendingDead = '\0';
                    EmitChange(ev, KeyKind.Backspace);
                    return false;
                case VK_RETURN:
                    pendingDead = '\0';
                    EmitChange(ev, KeyKind.Enter);
                    return false;
                case VK_TAB:
                    EmitChange(ev, KeyKind.Reset);
                    return false;
                case VK_ESCAPE:
                    pendingDead = '\0';
                    EmitChange(ev, KeyKind.Escape);
                    return false;
                case VK_DELETE:
                case VK_APPS:
                    return false;
            }
            if (vk >= VK_PRIOR && vk <= VK_DOWN || vk == VK_INSERT)
            {
                EmitChange(ev, KeyKind.Reset);
                return false;
            }
            if (vk >= VK_F1 && vk <= VK_F24)
            {
                int index = (int)(vk - VK_F1);
                if (snap != null && snap.Seq == changeSeq && SelectionMode == (int)SelectionKeys.FunctionKeys && !shift && index < snap.Words.Length)
                    return Select(ev, snap, index);
                return false;
            }

            // VK_PACKET : caractère Unicode envoyé par un clavier visuel ou un autre outil.
            string text = vk == VK_PACKET ? ((char)k.scanCode).ToString() : Translate(vk, k.scanCode, shift, altGr, fg);
            if (text == null || (text.Length == 1 && char.IsControl(text[0])))
                return false;

            if (snap != null && snap.Seq != changeSeq)
                snap = null;
            if (snap != null && SelectionMode == (int)SelectionKeys.Digits && text.Length == 1
                && text[0] >= '1' && text[0] <= '9' && text[0] - '1' < snap.Words.Length)
            {
                return Select(ev, snap, text[0] - '1');
            }

            if (armed && text.Length == 1 && AttachChars.IndexOf(text[0]) >= 0)
            {
                // « mot ,» -> « mot, » : on retire l'espace ajoutée automatiquement.
                Inserter.Send(1, text);
                ev.Text = text;
                EmitChange(ev, KeyKind.Punctuated);
                return true;
            }

            ev.Text = text;
            EmitChange(ev, KeyKind.Text);
            return false;
        }

        void EmitChange(KeyEvent ev, KeyKind kind)
        {
            // Les propositions affichées ne correspondent plus au texte : on les désactive tout de suite.
            Snapshot = null;
            ev.Kind = kind;
            ev.Seq = ++changeSeq;
            Emit(ev);
        }

        bool Select(KeyEvent ev, PredictionSnapshot snap, int index)
        {
            pendingDead = '\0';
            Snapshot = null;
            Inserter.Send(snap.Backspaces[index], snap.Inserts[index]);
            AutoSpaceArmed = snap.Inserts[index].EndsWith(" ");
            ev.Kind = KeyKind.Selected;
            ev.Seq = ++changeSeq;
            ev.Index = index;
            ev.Snapshot = snap;
            Emit(ev);
            return true;
        }

        /// <summary>
        /// Traduit une touche en texte selon la disposition du clavier de l'application active
        /// (AZERTY, QWERTY…), sans perturber les touches mortes (^, ¨) de cette application.
        /// </summary>
        string Translate(uint vk, uint scan, bool shift, bool altGr, IntPtr fg)
        {
            var state = new byte[256];
            if (shift)
            {
                state[VK_SHIFT] = 0x80;
                state[VK_LSHIFT] = 0x80;
            }
            if (altGr)
            {
                state[VK_CONTROL] = 0x80;
                state[0xA2] = 0x80;
                state[VK_MENU] = 0x80;
                state[VK_RMENU] = 0x80;
            }
            if ((Native.GetKeyState(VK_CAPITAL) & 1) != 0)
                state[VK_CAPITAL] = 0x01;
            if ((Native.GetKeyState(VK_NUMLOCK) & 1) != 0)
                state[VK_NUMLOCK] = 0x01;

            uint pid;
            IntPtr hkl = Native.GetKeyboardLayout(Native.GetWindowThreadProcessId(fg, out pid));
            var buf = new char[8];
            // Option 4 : ne pas modifier l'état du clavier (Windows 10 1607+).
            int n = Native.ToUnicodeEx(vk, scan, state, buf, buf.Length, 4, hkl);
            if (n < 0)
            {
                // Touche morte : on retient l'accent pour l'appliquer à la lettre suivante.
                string pending = pendingDead != '\0' ? pendingDead.ToString() : null;
                pendingDead = buf[0];
                return pending;
            }
            if (n == 0)
                return null;
            string text = new string(buf, 0, n);
            if (text.Length == 1 && char.IsControl(text[0]))
                return null;
            if (pendingDead != '\0')
            {
                text = Compose(pendingDead, text);
                pendingDead = '\0';
            }
            return text;
        }

        static string Compose(char dead, string text)
        {
            char mark;
            switch (dead)
            {
                case '^': mark = '̂'; break;
                case '¨':
                case '"': mark = '̈'; break;
                case '`': mark = '̀'; break;
                case '´':
                case '\'': mark = '́'; break;
                case '~': mark = '̃'; break;
                case '¸': mark = '̧'; break;
                case '°': mark = '̊'; break;
                default: return dead + text;
            }
            if (text == " ")
                return dead.ToString();
            // Déjà composé par Windows (« ê ») ou combinaison impossible (« ^x ») : on garde tel quel.
            if (text[0] == dead || text.Normalize(NormalizationForm.FormD).IndexOf(mark) >= 0)
                return text;
            string composed = (text + mark).Normalize(NormalizationForm.FormC);
            return composed.Length == 1 ? composed : dead + text;
        }
    }

    /// <summary>Envoie des frappes à l'application active.</summary>
    internal static class Inserter
    {
        const ushort VK_BACK = 0x08, VK_RETURN = 0x0D, VK_CONTROL = 0x11;

        public static void Send(int backspaces, string text)
        {
            var inputs = new List<Native.INPUT>();
            for (int i = 0; i < backspaces; i++)
            {
                inputs.Add(Key(VK_BACK, 0, 0));
                inputs.Add(Key(VK_BACK, 0, Native.KEYEVENTF_KEYUP));
            }
            foreach (char c in text)
            {
                if (c == '\n')
                {
                    inputs.Add(Key(VK_RETURN, 0, 0));
                    inputs.Add(Key(VK_RETURN, 0, Native.KEYEVENTF_KEYUP));
                    continue;
                }
                inputs.Add(Key(0, c, Native.KEYEVENTF_UNICODE));
                inputs.Add(Key(0, c, Native.KEYEVENTF_UNICODE | Native.KEYEVENTF_KEYUP));
            }
            SendAll(inputs);
        }

        /// <summary>Ctrl+C pour copier la sélection de l'application active.</summary>
        public static void SendCopy()
        {
            var inputs = new List<Native.INPUT>
            {
                Key(VK_CONTROL, 0, 0),
                Key('C', 0, 0),
                Key('C', 0, Native.KEYEVENTF_KEYUP),
                Key(VK_CONTROL, 0, Native.KEYEVENTF_KEYUP)
            };
            SendAll(inputs);
        }

        static void SendAll(List<Native.INPUT> inputs)
        {
            if (inputs.Count == 0)
                return;
            uint sent = Native.SendInput((uint)inputs.Count, inputs.ToArray(), Marshal.SizeOf(typeof(Native.INPUT)));
            if (sent != inputs.Count)
                Log.Write("SendInput : " + sent + "/" + inputs.Count + " (application protégée ?) erreur " + Marshal.GetLastWin32Error());
        }

        static Native.INPUT Key(ushort vk, ushort scan, uint flags)
        {
            var input = new Native.INPUT { type = Native.INPUT_KEYBOARD };
            input.U.ki = new Native.KEYBDINPUT { wVk = vk, wScan = scan, dwFlags = flags, dwExtraInfo = InputHooks.Magic };
            return input;
        }
    }
}
