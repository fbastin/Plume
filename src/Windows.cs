using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace Plume
{
    internal sealed class Theme
    {
        public string Name;
        public Color Back, Text, Muted, Hover, Border, Accent, AccentText, BadgeBack, BadgeText;

        // Identifiants enregistrés dans les réglages (ne pas traduire) ; DisplayName donne le libellé affiché.
        public static readonly string[] Names = { "Clair", "Sombre", "Crème", "Contraste élevé" };

        public static string DisplayName(string id)
        {
            switch (id)
            {
                case "Sombre": return L.T("Sombre", "Dark");
                case "Crème": return L.T("Crème", "Cream");
                case "Contraste élevé": return L.T("Contraste élevé", "High contrast");
                default: return L.T("Clair", "Light");
            }
        }

        public static Theme Get(string name)
        {
            switch (name)
            {
                case "Sombre":
                    return new Theme
                    {
                        Name = name, Back = Color.FromArgb(32, 33, 36), Text = Color.FromArgb(241, 243, 244),
                        Muted = Color.FromArgb(154, 160, 166), Hover = Color.FromArgb(53, 66, 92),
                        Border = Color.FromArgb(70, 72, 77), Accent = Color.FromArgb(138, 180, 248),
                        AccentText = Color.FromArgb(32, 33, 36), BadgeBack = Color.FromArgb(60, 64, 67),
                        BadgeText = Color.FromArgb(232, 234, 237)
                    };
                case "Crème":
                    return new Theme
                    {
                        Name = name, Back = Color.FromArgb(253, 246, 227), Text = Color.FromArgb(51, 42, 30),
                        Muted = Color.FromArgb(128, 112, 90), Hover = Color.FromArgb(240, 226, 190),
                        Border = Color.FromArgb(214, 198, 160), Accent = Color.FromArgb(150, 85, 20),
                        AccentText = Color.White, BadgeBack = Color.FromArgb(236, 222, 186),
                        BadgeText = Color.FromArgb(90, 70, 40)
                    };
                case "Contraste élevé":
                    return new Theme
                    {
                        Name = name, Back = Color.Black, Text = Color.Yellow, Muted = Color.White,
                        Hover = Color.FromArgb(0, 70, 140), Border = Color.Yellow, Accent = Color.Yellow,
                        AccentText = Color.Black, BadgeBack = Color.Yellow, BadgeText = Color.Black
                    };
                default:
                    return new Theme
                    {
                        Name = "Clair", Back = Color.White, Text = Color.FromArgb(28, 28, 30),
                        Muted = Color.FromArgb(110, 110, 118), Hover = Color.FromArgb(226, 236, 252),
                        Border = Color.FromArgb(200, 203, 210), Accent = Color.FromArgb(37, 99, 235),
                        AccentText = Color.White, BadgeBack = Color.FromArgb(237, 240, 245),
                        BadgeText = Color.FromArgb(70, 75, 85)
                    };
            }
        }
    }

    /// <summary>Fenêtre flottante qui ne prend jamais le focus (l'utilisateur continue d'écrire).</summary>
    internal class FloatingWindow : Form
    {
        public FloatingWindow()
        {
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            DoubleBuffered = true;
            SetStyle(ControlStyles.ResizeRedraw | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
        }

        protected override bool ShowWithoutActivation
        {
            get { return true; }
        }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= Native.WS_EX_NOACTIVATE | Native.WS_EX_TOOLWINDOW | Native.WS_EX_TOPMOST;
                cp.ClassStyle |= Native.CS_DROPSHADOW;
                return cp;
            }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            Native.RoundCorners(Handle);
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == Native.WM_MOUSEACTIVATE)
            {
                m.Result = (IntPtr)Native.MA_NOACTIVATE;
                return;
            }
            base.WndProc(ref m);
        }

        protected float DpiScale
        {
            get
            {
                try
                {
                    return IsHandleCreated ? Native.GetDpiForWindow(Handle) / 96f : DeviceDpi / 96f;
                }
                catch (EntryPointNotFoundException)
                {
                    return DeviceDpi / 96f;
                }
            }
        }

        /// <summary>Repasse devant les autres fenêtres « toujours visibles », sans prendre le focus.</summary>
        public void BringToTop()
        {
            if (IsHandleCreated)
                Native.SetWindowPos(Handle, Native.HWND_TOPMOST, 0, 0, 0, 0,
                    Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);
        }

        protected static GraphicsPath RoundRect(RectangleF r, float radius)
        {
            var p = new GraphicsPath();
            float d = radius * 2;
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        /// <summary>Garde la fenêtre entièrement visible sur l'écran qui contient le point.</summary>
        protected Point Clamp(Point p, Size size)
        {
            Rectangle wa = Screen.FromPoint(p).WorkingArea;
            int x = Math.Max(wa.Left, Math.Min(p.X, wa.Right - size.Width));
            int y = Math.Max(wa.Top, Math.Min(p.Y, wa.Bottom - size.Height));
            return new Point(x, y);
        }
    }

    /// <summary>Liste des mots proposés.</summary>
    internal sealed class PredictionWindow : FloatingWindow
    {
        List<Prediction> items = new List<Prediction>();
        int hover = -1;
        Theme theme = Theme.Get("Clair");
        string fontName = "Segoe UI";
        float fontSize = 15f;
        SelectionKeys keys = SelectionKeys.Digits;
        Font font, badgeFont;
        float fontScale;
        int rowH, badgeW, pad;
        readonly Timer hoverTimer = new Timer { Interval = 300 };
        bool dragging;
        Point dragOffset;

        public event Action<int> ItemClicked;
        public event Action<int> ItemHovered;
        public event Action<int, Point> ItemRightClicked;
        public event Action<Point> Moved;
        public bool Draggable;

        public PredictionWindow()
        {
            Text = "Plume - mots proposés";
            hoverTimer.Tick += delegate
            {
                hoverTimer.Stop();
                if (hover >= 0 && ItemHovered != null)
                    ItemHovered(hover);
            };
        }

        public int Count
        {
            get { return items.Count; }
        }

        public void Configure(Theme t, string name, float size, SelectionKeys k)
        {
            theme = t;
            fontName = name;
            fontSize = size;
            keys = k;
            fontScale = 0;
            Relayout();
        }

        public void SetItems(List<Prediction> preds)
        {
            items = preds;
            hover = -1;
            Relayout();
        }

        void EnsureFonts()
        {
            float s = DpiScale;
            if (font != null && Math.Abs(s - fontScale) < 0.01f)
                return;
            fontScale = s;
            if (font != null)
            {
                font.Dispose();
                badgeFont.Dispose();
            }
            font = new Font(fontName, fontSize * s * 96f / 72f, FontStyle.Regular, GraphicsUnit.Pixel);
            badgeFont = new Font("Segoe UI Semibold", fontSize * 0.7f * s * 96f / 72f, FontStyle.Regular, GraphicsUnit.Pixel);
        }

        string Label(int i)
        {
            if (keys == SelectionKeys.FunctionKeys)
                return "F" + (i + 1);
            if (keys == SelectionKeys.MouseOnly)
                return "";
            return (i + 1).ToString();
        }

        void Relayout()
        {
            EnsureFonts();
            float s = fontScale;
            pad = (int)(6 * s);
            rowH = (int)(font.Height * 1.45f);
            badgeW = keys == SelectionKeys.MouseOnly ? 0 : TextRenderer.MeasureText("F9", badgeFont).Width + (int)(8 * s);
            int textW = (int)(120 * s);
            foreach (var p in items)
                textW = Math.Max(textW, TextRenderer.MeasureText(p.Text, font).Width);
            var size = new Size(pad * 2 + badgeW + (badgeW > 0 ? pad * 2 : pad) + textW + pad,
                pad * 2 + rowH * Math.Max(1, items.Count));
            if (Size != size)
                Size = size;
            Invalidate();
        }

        Rectangle RowRect(int i)
        {
            return new Rectangle(pad, pad + i * rowH, ClientSize.Width - pad * 2, rowH);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            EnsureFonts();
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(theme.Back);
            using (var pen = new Pen(theme.Border))
                g.DrawRectangle(pen, 0, 0, ClientSize.Width - 1, ClientSize.Height - 1);
            float s = fontScale;
            for (int i = 0; i < items.Count; i++)
            {
                Rectangle r = RowRect(i);
                if (i == hover)
                {
                    using (var path = RoundRect(r, 6 * s))
                    using (var b = new SolidBrush(theme.Hover))
                        g.FillPath(b, path);
                }
                int x = r.X + pad;
                if (badgeW > 0)
                {
                    var badge = new RectangleF(x, r.Y + (rowH - badgeFont.Height * 1.35f) / 2, badgeW, badgeFont.Height * 1.35f);
                    using (var path = RoundRect(badge, 5 * s))
                    using (var b = new SolidBrush(theme.BadgeBack))
                        g.FillPath(b, path);
                    TextRenderer.DrawText(g, Label(i), badgeFont, Rectangle.Round(badge), theme.BadgeText,
                        TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
                    x += badgeW + pad;
                }
                var textRect = new Rectangle(x, r.Y, r.Right - x, r.Height);
                TextRenderer.DrawText(g, items[i].Text, font, textRect, theme.Text,
                    TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine);
            }
        }

        int HitTest(Point p)
        {
            for (int i = 0; i < items.Count; i++)
                if (RowRect(i).Contains(p))
                    return i;
            return -1;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (dragging)
            {
                Location = new Point(Cursor.Position.X - dragOffset.X, Cursor.Position.Y - dragOffset.Y);
                return;
            }
            int h = HitTest(e.Location);
            if (h != hover)
            {
                hover = h;
                hoverTimer.Stop();
                if (h >= 0)
                    hoverTimer.Start();
                Invalidate();
            }
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            hover = -1;
            hoverTimer.Stop();
            Invalidate();
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button == MouseButtons.Left && Draggable && HitTest(e.Location) < 0)
            {
                dragging = true;
                dragOffset = e.Location;
                Capture = true;
            }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (dragging)
            {
                dragging = false;
                Capture = false;
                if (Moved != null)
                    Moved(Location);
                return;
            }
            int i = HitTest(e.Location);
            if (i < 0)
                return;
            if (e.Button == MouseButtons.Left && ItemClicked != null)
                ItemClicked(i);
            else if (e.Button == MouseButtons.Right && ItemRightClicked != null)
                ItemRightClicked(i, PointToScreen(e.Location));
        }

        /// <summary>Affiche la liste juste sous le curseur de texte (ou au-dessus si pas de place).</summary>
        public void ShowNear(Rectangle caret)
        {
            float before = fontScale;
            Point p = PlaceNear(caret);
            Location = p;
            if (!Visible)
                Show();
            BringToTop();
            if (Math.Abs(DpiScale - before) > 0.01f)
            {
                Relayout();
                Location = PlaceNear(caret);
            }
        }

        Point PlaceNear(Rectangle caret)
        {
            int gap = (int)(6 * fontScale);
            Rectangle wa = Screen.FromPoint(caret.Location).WorkingArea;
            int y = caret.Bottom + gap;
            if (y + Height > wa.Bottom)
                y = caret.Top - gap - Height;
            return Clamp(new Point(caret.Left - pad - badgeW - pad, y), Size);
        }

        public void ShowAt(Point p)
        {
            Location = Clamp(p, Size);
            if (!Visible)
                Show();
            BringToTop();
        }
    }

    /// <summary>Barre d'outils flottante : prédiction, parole, lecture, langue, options.</summary>
    internal sealed class Toolbar : FloatingWindow
    {
        sealed class Button
        {
            public string Id, Glyph, Label, Tip;
            public bool Toggle, On;
            public RectangleF Bounds;
        }

        readonly List<Button> buttons = new List<Button>();
        Theme theme = Theme.Get("Clair");
        Font font, glyphFont;
        float fontScale;
        int hover = -1, pressed = -1;
        bool dragging;
        Point dragOffset;
        readonly ToolTip tip = new ToolTip { ShowAlways = true };
        static readonly bool HasGlyphFont = FontExists("Segoe Fluent Icons") || FontExists("Segoe MDL2 Assets");

        public event Action<string> Clicked;
        public event Action<Point> Moved;
        public event Action<Point> MenuRequested;

        public Toolbar()
        {
            Text = "Plume";
            Add("predict", "", true);
            Add("speech", "", true);
            Add("read", "", false);
            Add("lang", "", false);
            Add("options", "", false);
            Add("hide", "", false);
            Add("quit", "", false);
            Localize();
        }

        /// <summary>Libellés et bulles d'aide dans la langue de l'interface.</summary>
        public void Localize()
        {
            Set("predict", L.T("Prédire", "Predict"), L.T("Prédiction de mots (activer / désactiver)", "Word prediction (on / off)"));
            Set("speech", L.T("Parler", "Speak"), L.T("Retour vocal (activer / désactiver)", "Speech feedback (on / off)"));
            Set("read", L.T("Lire", "Read"), L.T("Lire la sélection ou la dernière phrase", "Read the selection or the last sentence"));
            Set("lang", null, L.T("Changer la langue d'écriture", "Switch writing language"));
            Set("options", "", L.T("Options", "Options"));
            Set("hide", "", L.T("Masquer la barre (Plume continue de fonctionner)", "Hide the toolbar (Plume keeps running)"));
            Set("quit", "", L.T("Quitter Plume", "Quit Plume"));
            if (IsHandleCreated)
                Relayout();
        }

        void Set(string id, string label, string tipText)
        {
            Button b = Find(id);
            if (label != null)
                b.Label = label;
            b.Tip = tipText;
        }

        /// <summary>Texte des boutons sans libellé quand la police d'icônes est absente.</summary>
        static string FallbackLabel(string id)
        {
            return id == "options" ? "Options" : id == "quit" ? "×" : "–";
        }

        static bool FontExists(string name)
        {
            using (var f = new Font(name, 10f))
                return f.Name == name;
        }

        void Add(string id, string glyph, bool toggle)
        {
            buttons.Add(new Button { Id = id, Glyph = glyph, Label = "", Tip = "", Toggle = toggle });
        }

        Button Find(string id)
        {
            foreach (var b in buttons)
                if (b.Id == id)
                    return b;
            return null;
        }

        public void SetState(string id, bool on)
        {
            Find(id).On = on;
            if (id == "speech")
                Find(id).Glyph = on ? "" : "";
            Invalidate();
        }

        public void SetLabel(string id, string label)
        {
            Find(id).Label = label;
            Relayout();
        }

        public void SetGlyph(string id, string glyph, string label)
        {
            Button b = Find(id);
            b.Glyph = glyph;
            b.Label = label;
            Relayout();
        }

        public void Configure(Theme t)
        {
            theme = t;
            Relayout();
        }

        void EnsureFonts()
        {
            float s = DpiScale;
            if (font != null && Math.Abs(s - fontScale) < 0.01f)
                return;
            fontScale = s;
            if (font != null)
            {
                font.Dispose();
                glyphFont.Dispose();
            }
            font = new Font("Segoe UI Semibold", 13f * s, FontStyle.Regular, GraphicsUnit.Pixel);
            glyphFont = new Font(FontExists("Segoe Fluent Icons") ? "Segoe Fluent Icons" : "Segoe MDL2 Assets",
                15f * s, FontStyle.Regular, GraphicsUnit.Pixel);
        }

        void Relayout()
        {
            EnsureFonts();
            float s = fontScale;
            float x = 16 * s, h = 36 * s, padY = 5 * s;
            foreach (var b in buttons)
            {
                float w = 10 * s;
                if (HasGlyphFont && b.Glyph.Length > 0)
                    w += 18 * s;
                if (b.Label.Length > 0)
                    w += TextRenderer.MeasureText(b.Label, font).Width + (HasGlyphFont && b.Glyph.Length > 0 ? 2 * s : 0);
                if (!HasGlyphFont && b.Label.Length == 0)
                    w += TextRenderer.MeasureText(FallbackLabel(b.Id), font).Width;
                b.Bounds = new RectangleF(x, padY, w, h - padY * 2);
                x += w + 3 * s;
            }
            var size = new Size((int)(x + 3 * s), (int)h);
            if (Size != size)
                Size = size;
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            EnsureFonts();
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(theme.Back);
            float s = fontScale;
            using (var pen = new Pen(theme.Border))
                g.DrawRectangle(pen, 0, 0, ClientSize.Width - 1, ClientSize.Height - 1);
            // Poignée de déplacement.
            using (var b = new SolidBrush(theme.Muted))
            {
                for (int row = 0; row < 3; row++)
                    for (int col = 0; col < 2; col++)
                        g.FillEllipse(b, 5 * s + col * 4 * s, ClientSize.Height / 2f - 6 * s + row * 5 * s, 2.2f * s, 2.2f * s);
            }
            for (int i = 0; i < buttons.Count; i++)
            {
                Button b = buttons[i];
                Color fg = theme.Text;
                if (b.Toggle && b.On)
                {
                    using (var path = RoundRect(b.Bounds, 6 * s))
                    using (var br = new SolidBrush(theme.Accent))
                        g.FillPath(br, path);
                    fg = theme.AccentText;
                }
                else if (i == hover || i == pressed)
                {
                    using (var path = RoundRect(b.Bounds, 6 * s))
                    using (var br = new SolidBrush(theme.Hover))
                        g.FillPath(br, path);
                }
                if (b.Toggle && !b.On)
                    fg = theme.Muted;
                float x = b.Bounds.X + 5 * s;
                var rect = Rectangle.Round(b.Bounds);
                if (HasGlyphFont && b.Glyph.Length > 0)
                {
                    var gr = new Rectangle((int)x, rect.Y, (int)(18 * s), rect.Height);
                    TextRenderer.DrawText(g, b.Glyph, glyphFont, gr, fg, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
                    x += 20 * s;
                }
                string label = b.Label.Length > 0 || HasGlyphFont ? b.Label : (FallbackLabel(b.Id));
                if (label.Length > 0)
                {
                    var tr = new Rectangle((int)x, rect.Y, (int)(b.Bounds.Right - x), rect.Height);
                    TextRenderer.DrawText(g, label, font, tr, fg, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
                }
            }
        }

        int HitTest(Point p)
        {
            for (int i = 0; i < buttons.Count; i++)
                if (buttons[i].Bounds.Contains(p))
                    return i;
            return -1;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (dragging)
            {
                Location = new Point(Cursor.Position.X - dragOffset.X, Cursor.Position.Y - dragOffset.Y);
                return;
            }
            int h = HitTest(e.Location);
            if (h != hover)
            {
                hover = h;
                tip.SetToolTip(this, h >= 0 ? buttons[h].Tip : L.T("Déplacer la barre (clic droit : menu)", "Drag to move (right-click: menu)"));
                Invalidate();
            }
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            hover = -1;
            Invalidate();
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left)
                return;
            pressed = HitTest(e.Location);
            if (pressed < 0)
            {
                dragging = true;
                dragOffset = e.Location;
                Capture = true;
            }
            Invalidate();
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (e.Button == MouseButtons.Right)
            {
                if (MenuRequested != null)
                    MenuRequested(PointToScreen(e.Location));
                return;
            }
            if (dragging)
            {
                dragging = false;
                Capture = false;
                if (Moved != null)
                    Moved(Location);
                return;
            }
            int i = HitTest(e.Location);
            if (i >= 0 && i == pressed && Clicked != null)
                Clicked(buttons[i].Id);
            pressed = -1;
            Invalidate();
        }

        public void PlaceDefault()
        {
            Rectangle wa = Screen.PrimaryScreen.WorkingArea;
            Location = new Point(wa.Right - Width - 24, wa.Top + 24);
        }
    }
}
