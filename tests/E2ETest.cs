using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace PlumeE2E
{
    /// <summary>
    /// Test de bout en bout : lance Plume (profil temporaire, sans voix), ouvre sa propre
    /// fenêtre de texte, y tape des phrases et vérifie le texte obtenu. S'arrête aussitôt
    /// si la fenêtre de test perd le focus, pour ne jamais écrire ailleurs.
    /// Usage : PlumeE2E.exe dossier_bin dossier_captures
    /// </summary>
    /// <summary>Fenêtre « toujours visible » dès sa création.</summary>
    internal sealed class TopForm : Form
    {
        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= 0x8; // WS_EX_TOPMOST
                return cp;
            }
        }
    }

    internal static class E2E
    {
        [StructLayout(LayoutKind.Sequential)]
        struct INPUT
        {
            public uint type;
            public InputUnion U;
        }

        [StructLayout(LayoutKind.Explicit)]
        struct InputUnion
        {
            [FieldOffset(0)] public MOUSEINPUT mi;
            [FieldOffset(0)] public KEYBDINPUT ki;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct MOUSEINPUT
        {
            public int dx, dy;
            public uint mouseData, dwFlags, time;
            public UIntPtr dwExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct KEYBDINPUT
        {
            public ushort wVk, wScan;
            public uint dwFlags, time;
            public UIntPtr dwExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct RECT
        {
            public int Left, Top, Right, Bottom;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct POINT
        {
            public int X, Y;
        }

        delegate bool EnumProc(IntPtr hwnd, IntPtr l);

        [DllImport("user32.dll")] static extern uint SendInput(uint n, INPUT[] inputs, int size);
        [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr h);
        [DllImport("user32.dll")] static extern bool EnumWindows(EnumProc proc, IntPtr l);
        [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
        [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr h);
        [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr h, out RECT r);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetWindowText(IntPtr h, StringBuilder sb, int n);
        [DllImport("user32.dll")] static extern IntPtr WindowFromPoint(POINT p);
        [DllImport("user32.dll")] static extern bool SetCursorPos(int x, int y);
        [DllImport("user32.dll")] static extern bool GetCursorPos(out POINT p);
        [DllImport("user32.dll")] static extern IntPtr GetKeyboardLayout(uint thread);
        [DllImport("user32.dll")] static extern bool SetProcessDpiAwarenessContext(IntPtr value);
        [DllImport("user32.dll")] static extern int GetWindowLong(IntPtr h, int i);
        [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);

        const ushort VK_BACK = 0x08, VK_SHIFT = 0x10, VK_ESCAPE = 0x1B, VK_OEM_6 = 0xDD;
        const uint KEYUP = 0x2, UNICODE = 0x4;

        static Form form;
        static TextBox box;
        static Process plume;
        static string outDir, bin, profile;
        static int failures, checks;
        static bool onlyRead;

        [STAThread]
        static int Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;
            try
            {
                SetProcessDpiAwarenessContext(new IntPtr(-4));
            }
            catch (Exception)
            {
            }
            bin = args[0];
            outDir = args[1];
            onlyRead = args.Length > 2 && args[2] == "lecture";
            Directory.CreateDirectory(outDir);
            profile = Path.Combine(Path.GetTempPath(), "PlumeE2E");
            if (Directory.Exists(profile))
                Directory.Delete(profile, true);
            Directory.CreateDirectory(profile);
            // Volume à zéro : le test de lecture à voix haute reste silencieux.
            File.WriteAllText(Path.Combine(profile, "reglages.ini"), "Volume=0\r\n");

            // Filet de sécurité : on remet le texte du presse-papiers tel qu'il était.
            string clip = Clipboard.ContainsText() ? Clipboard.GetText() : null;

            plume = Process.Start(Path.Combine(bin, "Plume.exe"), "--profile \"" + profile + "\" --no-speech");
            form = new TopForm
            {
                Text = "Test Plume", StartPosition = FormStartPosition.Manual, Location = new Point(120, 120),
                Size = new Size(900, 360)
            };
            box = new TextBox { Multiline = true, Dock = DockStyle.Fill, Font = new Font("Segoe UI", 16f), AcceptsReturn = true };
            form.Controls.Add(box);
            form.Shown += async delegate
            {
                try
                {
                    await Scenario();
                }
                catch (Exception ex)
                {
                    Fail("arrêt : " + ex.Message);
                }
                form.Close();
            };
            Application.Run(form);
            Process.Start(Path.Combine(bin, "Plume.exe"), "--profile \"" + profile + "\" --quit").WaitForExit();
            if (!plume.WaitForExit(5000))
            {
                Fail("Plume ne s'est pas fermé avec --quit");
                plume.Kill();
            }
            if (clip != null && Clipboard.GetText() != clip)
            {
                Clipboard.SetText(clip);
                Fail("le presse-papiers avait changé (remis en état)");
            }
            string log = Path.Combine(profile, "plume.log");
            if (File.Exists(log))
                Console.WriteLine("--- journal de Plume ---\n" + File.ReadAllText(log));
            Console.WriteLine(failures == 0 ? "Test de bout en bout réussi (" + checks + " vérifications)" : failures + " échec(s) sur " + checks);
            return failures == 0 ? 0 : 1;
        }

        static void Check(bool ok, string what)
        {
            checks++;
            Console.WriteLine((ok ? "ok     " : "ÉCHEC  ") + what);
            if (!ok)
                failures++;
        }

        static void Fail(string what)
        {
            Check(false, what);
        }

        static async Task Scenario()
        {
            await Task.Delay(4000); // chargement du dictionnaire
            if (!FindFreeSpot())
                throw new InvalidOperationException("aucun endroit de l'écran n'est libre pour la fenêtre de test ; "
                    + "topmost=" + ((GetWindowLong(form.Handle, -20) & 8) != 0) + " ordre : " + ZOrder());
            form.Activate();
            SetForegroundWindow(form.Handle);
            if (GetForegroundWindow() != form.Handle)
            {
                // Windows refuse souvent le focus à un programme lancé en arrière-plan : on clique dedans.
                Point c = box.PointToScreen(new Point(box.Width / 2, box.Height / 2));
                var cp = new POINT { X = c.X, Y = c.Y };
                IntPtr under = WindowFromPoint(cp);
                if (under != box.Handle)
                {
                    var t = new StringBuilder(128);
                    GetWindowText(under, t, t.Capacity);
                    uint upid;
                    GetWindowThreadProcessId(under, out upid);
                    throw new InvalidOperationException("la fenêtre de test est masquée en " + c + " par « " + t
                        + " » (processus " + upid + ", test " + Process.GetCurrentProcess().Id + ", visible=" + form.Visible
                        + ", bornes " + form.Bounds + ")");
                }
                POINT old;
                GetCursorPos(out old);
                SetCursorPos(c.X, c.Y);
                await Task.Delay(100);
                Mouse(0x2);
                Mouse(0x4);
                await Task.Delay(200);
                SetCursorPos(old.X, old.Y);
            }
            box.Focus();
            box.SelectionStart = box.TextLength;
            await Task.Delay(300);
            ShotWindow("Plume", "0-barre");
            if (onlyRead)
            {
                await Type("Bonjour tout le monde.");
                await ReadTest();
                return;
            }

            await Type("Bonjour, je ");
            await Task.Delay(500);
            Shot("1-mot-suivant");
            string before = box.Text;
            await Type("2");
            await Task.Delay(400);
            Check(!box.Text.Contains("2") && box.Text.Length > before.Length + 1 && box.Text.EndsWith(" "),
                "chiffre 2 = 2e mot proposé : « " + box.Text + " »");

            await Type("vais à l'éc");
            await Task.Delay(500);
            Shot("2-ecole");
            await Type("1");
            await Task.Delay(400);
            Check(box.Text.EndsWith("l'école "), "« l'éc » + 1 -> « l'école » : « " + box.Text + " »");

            await Type(".");
            await Task.Delay(300);
            Check(box.Text.EndsWith("l'école."), "ponctuation collée au mot : « " + box.Text + " »");

            await Type(" Il y ");
            await Task.Delay(500);
            Shot("3-il-y");
            await Key(VK_ESCAPE, false);
            await Type("2");
            await Task.Delay(300);
            Check(box.Text.EndsWith("Il y 2"), "Échap puis chiffre tape le chiffre : « " + box.Text + " »");

            await Type(" ans. Ma maiso");
            await Key(VK_BACK, false);
            await Key(VK_BACK, false);
            await Task.Delay(500);
            Shot("4-retour-arriere");
            await Type("1");
            await Task.Delay(400);
            string last = LastWord(box.Text);
            Check(last.StartsWith("mai") && !box.Text.EndsWith("1"), "retour arrière suivi : « " + box.Text + " »");

            uint layout = (uint)GetKeyboardLayout(0).ToInt64();
            ushort lang = (ushort)(layout & 0xFFFF);
            if (layout == 0xF0010409 || lang == 0x040C || lang == 0x0C0C)
            {
                // Touche morte ^ : Maj+6 en US-International, touche ^ en AZERTY.
                await Type(" C'est la f");
                if (lang == 0x0409)
                {
                    await Key(VK_SHIFT, true);
                    await Key('6', false);
                    await Key(VK_SHIFT, true, true);
                }
                else
                {
                    await Key(VK_OEM_6, false);
                }
                await Key('E', false);
                await Key('T', false);
                await Task.Delay(500);
                Shot("5-touche-morte");
                Check(box.Text.EndsWith("la fêt"), "touche morte intacte dans l'application : « " + box.Text + " »");
                await Type("1");
                await Task.Delay(400);
                Check(LastWord(box.Text).StartsWith("fêt") && !box.Text.Contains("fêtfêt"), "mot choisi après touche morte : « " + box.Text + " »");
            }
            else
            {
                Console.WriteLine("(test des touches mortes ignoré pour cette disposition " + layout.ToString("X") + ")");
            }

            await Type(" Nous somm");
            await Task.Delay(600);
            Shot("6-souris");
            IntPtr list = FindWindow("Plume - mots proposés");
            if (list == IntPtr.Zero)
            {
                Fail("liste de mots introuvable");
                return;
            }
            RECT r;
            GetWindowRect(list, out r);
            var p = new POINT { X = (r.Left + r.Right) / 2, Y = r.Top + 22 };
            uint pid;
            GetWindowThreadProcessId(WindowFromPoint(p), out pid);
            if (pid != plume.Id)
            {
                Fail("la liste n'est pas sous le point visé, clic annulé");
                return;
            }
            POINT saved;
            GetCursorPos(out saved);
            SetCursorPos(p.X, p.Y);
            await Task.Delay(150);
            Mouse(0x2);
            Mouse(0x4);
            await Task.Delay(400);
            SetCursorPos(saved.X, saved.Y);
            Check(box.Text.EndsWith("Nous sommes "), "clic sur le 1er mot : « " + box.Text + " »");
            Check(GetForegroundWindow() == form.Handle, "le focus reste dans l'application après le clic");
            Shot("7-final");

            await ReadTest();
        }

        static async Task ReadTest()
        {
            // Lecture de la sélection avec le raccourci Ctrl+Maj+F12.
            box.SelectionStart = 0;
            box.SelectionLength = 7;
            await Task.Delay(200);
            await Key(0x11, true);
            await Key(VK_SHIFT, true);
            await Key(0x7B, false);
            await Key(VK_SHIFT, true, true);
            await Key(0x11, true, true);
            await Task.Delay(5000);
            string log = File.Exists(Path.Combine(profile, "plume.log")) ? File.ReadAllText(Path.Combine(profile, "plume.log")) : "";
            Check(log.Contains("Lecture (sélection) : 7 caractères"), "Ctrl+Maj+F12 lit la sélection « Bonjour »");
            plume.Refresh();
            Console.WriteLine("       mémoire de Plume : " + (plume.WorkingSet64 / (1024 * 1024)) + " Mo");
        }

        /// <summary>Place la fenêtre de test là où rien ne la recouvre.</summary>
        static bool FindFreeSpot()
        {
            Rectangle wa = Screen.PrimaryScreen.WorkingArea;
            foreach (var pos in new[] { new Point(120, 120), new Point(wa.Left + 20, wa.Bottom - 600), new Point(wa.Right - 940, wa.Top + 80),
                new Point(wa.Right - 940, wa.Bottom - 600), new Point(wa.Left + 20, wa.Top + 20), new Point(wa.Left + wa.Width / 3, wa.Top + wa.Height / 3) })
            {
                form.Location = pos;
                SetWindowPos(form.Handle, new IntPtr(-1), 0, 0, 0, 0, 0x1 | 0x2 | 0x10);
                Application.DoEvents();
                if (Uncovered(box))
                    return true;
            }
            return false;
        }

        static string ZOrder()
        {
            var names = new List<string>();
            EnumWindows((h, l) =>
            {
                if (!IsWindowVisible(h))
                    return true;
                uint pid;
                GetWindowThreadProcessId(h, out pid);
                RECT r;
                GetWindowRect(h, out r);
                string n = pid == Process.GetCurrentProcess().Id ? "TEST" : Process.GetProcessById((int)pid).ProcessName;
                names.Add(n + (((GetWindowLong(h, -20) & 8) != 0) ? "*" : "") + "(" + r.Left + "," + r.Top + ")");
                return names.Count < 8;
            }, IntPtr.Zero);
            return string.Join(" > ", names);
        }

        static bool Uncovered(Control c)
        {
            foreach (var f in new[] { new PointF(0.1f, 0.1f), new PointF(0.5f, 0.5f), new PointF(0.9f, 0.9f), new PointF(0.9f, 0.1f), new PointF(0.1f, 0.9f) })
            {
                Point sp = c.PointToScreen(new Point((int)(c.Width * f.X), (int)(c.Height * f.Y)));
                if (WindowFromPoint(new POINT { X = sp.X, Y = sp.Y }) != c.Handle)
                    return false;
            }
            return true;
        }

        static string LastWord(string text)
        {
            string[] parts = text.TrimEnd().Split(' ');
            return parts[parts.Length - 1];
        }

        static void EnsureFocus()
        {
            IntPtr fg = GetForegroundWindow();
            if (fg != form.Handle)
            {
                var sb = new StringBuilder(128);
                GetWindowText(fg, sb, sb.Capacity);
                throw new InvalidOperationException("la fenêtre de test a perdu le focus (au profit de « " + sb + " »)");
            }
        }

        static async Task Type(string text)
        {
            foreach (char c in text)
            {
                EnsureFocus();
                Send(new[] { KeyInput(0, c, UNICODE), KeyInput(0, c, UNICODE | KEYUP) });
                await Task.Delay(70);
            }
        }

        static async Task Key(ushort vk, bool down, bool up = false)
        {
            EnsureFocus();
            if (down || up)
                Send(new[] { KeyInput(vk, 0, up ? KEYUP : 0) });
            else
                Send(new[] { KeyInput(vk, 0, 0), KeyInput(vk, 0, KEYUP) });
            await Task.Delay(70);
        }

        static INPUT KeyInput(ushort vk, ushort scan, uint flags)
        {
            var i = new INPUT { type = 1 };
            i.U.ki = new KEYBDINPUT { wVk = vk, wScan = scan, dwFlags = flags };
            return i;
        }

        static void Mouse(uint flags)
        {
            var i = new INPUT { type = 0 };
            i.U.mi = new MOUSEINPUT { dwFlags = flags };
            Send(new[] { i });
        }

        static void Send(INPUT[] inputs)
        {
            SendInput((uint)inputs.Length, inputs, Marshal.SizeOf(typeof(INPUT)));
        }

        static IntPtr FindWindow(string title)
        {
            IntPtr found = IntPtr.Zero;
            EnumWindows((h, l) =>
            {
                uint pid;
                GetWindowThreadProcessId(h, out pid);
                if (pid == plume.Id && IsWindowVisible(h))
                {
                    var sb = new StringBuilder(128);
                    GetWindowText(h, sb, sb.Capacity);
                    if (sb.ToString() == title)
                    {
                        found = h;
                        return false;
                    }
                }
                return true;
            }, IntPtr.Zero);
            return found;
        }

        /// <summary>Capture la fenêtre de test et la liste de mots (rien d'autre de l'écran).</summary>
        static void Shot(string name)
        {
            Rectangle area = form.Bounds;
            IntPtr list = FindWindow("Plume - mots proposés");
            if (list != IntPtr.Zero)
            {
                RECT r;
                GetWindowRect(list, out r);
                area = Rectangle.Union(area, Rectangle.FromLTRB(r.Left, r.Top, r.Right, r.Bottom));
            }
            Capture(area, name);
        }

        static void ShotWindow(string title, string name)
        {
            IntPtr h = FindWindow(title);
            if (h == IntPtr.Zero)
            {
                Fail("fenêtre « " + title + " » introuvable");
                return;
            }
            RECT r;
            GetWindowRect(h, out r);
            Capture(Rectangle.FromLTRB(r.Left, r.Top, r.Right, r.Bottom), name);
            checks++;
            Console.WriteLine("ok     barre d'outils affichée");
        }

        static void Capture(Rectangle area, string name)
        {
            using (var bmp = new Bitmap(area.Width, area.Height))
            {
                using (Graphics g = Graphics.FromImage(bmp))
                    g.CopyFromScreen(area.Location, Point.Empty, area.Size);
                bmp.Save(Path.Combine(outDir, name + ".png"), ImageFormat.Png);
            }
        }
    }
}
