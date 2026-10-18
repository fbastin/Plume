using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Text;
using System.Threading;
using System.Windows.Automation;
using System.Windows.Automation.Text;

namespace Plume
{
    /// <summary>Informations sur le champ de texte actif.</summary>
    internal sealed class FocusInfo
    {
        public bool IsPassword;
        public string TextBefore;   // texte avant le curseur (null si inaccessible)
        public bool StartKnown;     // TextBefore commence au début du champ
    }

    /// <summary>
    /// Interroge les applications (UI Automation / MSAA) sur un fil à part : ces appels
    /// traversent les processus et peuvent être lents. Les demandes d'un même type se
    /// remplacent (seule la plus récente compte).
    /// </summary>
    internal sealed class AccessibilityWorker
    {
        sealed class Job
        {
            public Func<object> Work;
            public Action<object> Done;
        }

        readonly object gate = new object();
        readonly Dictionary<string, Job> pending = new Dictionary<string, Job>();
        readonly List<string> order = new List<string>();
        readonly Action<Action> toUi;
        Thread thread;
        DateTime busySince = DateTime.MaxValue;

        public AccessibilityWorker(Action<Action> toUi)
        {
            this.toUi = toUi;
        }

        public void Run(string slot, Func<object> work, Action<object> done)
        {
            lock (gate)
            {
                if (!pending.ContainsKey(slot))
                    order.Add(slot);
                pending[slot] = new Job { Work = work, Done = done };
                // Une application bloquée peut figer le fil : on en démarre un neuf.
                if (thread == null || (DateTime.UtcNow - busySince).TotalSeconds > 3)
                {
                    thread = new Thread(Loop) { IsBackground = true, Name = "Plume accessibilité" };
                    thread.SetApartmentState(ApartmentState.MTA);
                    busySince = DateTime.MaxValue;
                    thread.Start(thread);
                }
                Monitor.PulseAll(gate);
            }
        }

        void Loop(object self)
        {
            while (true)
            {
                Job job;
                lock (gate)
                {
                    while (order.Count == 0)
                    {
                        if (thread != self)
                            return;
                        Monitor.Wait(gate);
                    }
                    if (thread != self)
                        return;
                    job = pending[order[0]];
                    pending.Remove(order[0]);
                    order.RemoveAt(0);
                    busySince = DateTime.UtcNow;
                }
                object result = null;
                try
                {
                    result = job.Work();
                }
                catch (Exception ex)
                {
                    Log.Write("Accessibilité : " + ex.GetType().Name + " " + ex.Message);
                }
                lock (gate)
                {
                    if (thread != self)
                        return;
                    busySince = DateTime.MaxValue;
                }
                var done = job.Done;
                toUi(delegate { done(result); });
            }
        }

        // ---- Requêtes (exécutées sur le fil de travail) ----

        /// <summary>Position du curseur de texte via MSAA puis UI Automation.</summary>
        public static object FindCaret(IntPtr focusHwnd)
        {
            Rectangle r = CaretFromMsaa(focusHwnd);
            if (r.IsEmpty)
                r = CaretFromUia();
            return r;
        }

        static Rectangle CaretFromMsaa(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero)
                return Rectangle.Empty;
            try
            {
                object obj;
                Guid iid = Native.IID_IAccessible;
                if (Native.AccessibleObjectFromWindow(hwnd, Native.OBJID_CARET, ref iid, out obj) != 0 || obj == null)
                    return Rectangle.Empty;
                var acc = obj as Accessibility.IAccessible;
                if (acc == null)
                    return Rectangle.Empty;
                int l, t, w, h;
                acc.accLocation(out l, out t, out w, out h, 0);
                if (l == 0 && t == 0)
                    return Rectangle.Empty;
                return new Rectangle(l, t, Math.Max(1, w), Math.Max(1, h));
            }
            catch (Exception)
            {
                return Rectangle.Empty;
            }
        }

        static Rectangle CaretFromUia()
        {
            TextPatternRange sel = Selection();
            if (sel == null)
                return Rectangle.Empty;
            System.Windows.Rect[] rects = sel.GetBoundingRectangles();
            if (rects.Length > 0)
                return ToRect(rects[0], false);
            // Curseur sans sélection : on mesure le caractère précédent (ou suivant).
            TextPatternRange c = sel.Clone();
            if (c.MoveEndpointByUnit(TextPatternRangeEndpoint.Start, TextUnit.Character, -1) != 0)
            {
                rects = c.GetBoundingRectangles();
                if (rects.Length > 0)
                    return ToRect(rects[rects.Length - 1], true);
            }
            c = sel.Clone();
            if (c.MoveEndpointByUnit(TextPatternRangeEndpoint.End, TextUnit.Character, 1) != 0)
            {
                rects = c.GetBoundingRectangles();
                if (rects.Length > 0)
                    return ToRect(rects[0], false);
            }
            return Rectangle.Empty;
        }

        static Rectangle ToRect(System.Windows.Rect r, bool rightEdge)
        {
            if (r.IsEmpty || double.IsInfinity(r.Left) || double.IsNaN(r.Left))
                return Rectangle.Empty;
            int x = (int)(rightEdge ? r.Right : r.Left);
            return new Rectangle(x, (int)r.Top, 2, Math.Max(1, (int)r.Height));
        }

        static TextPatternRange Selection()
        {
            AutomationElement el = AutomationElement.FocusedElement;
            if (el == null)
                return null;
            object pat;
            if (!el.TryGetCurrentPattern(TextPattern.Pattern, out pat))
                return null;
            TextPatternRange[] sel = ((TextPattern)pat).GetSelection();
            return sel.Length > 0 ? sel[0] : null;
        }

        /// <summary>Champ mot de passe ? Texte avant le curseur ?</summary>
        public static object InspectFocus(IntPtr focusHwnd)
        {
            var info = new FocusInfo();
            if (focusHwnd != IntPtr.Zero)
            {
                var cls = new StringBuilder(64);
                Native.GetClassName(focusHwnd, cls, cls.Capacity);
                if (cls.ToString().IndexOf("Edit", StringComparison.OrdinalIgnoreCase) >= 0
                    && (Native.GetWindowLong(focusHwnd, Native.GWL_STYLE) & Native.ES_PASSWORD) != 0)
                {
                    info.IsPassword = true;
                    return info;
                }
            }
            AutomationElement el = AutomationElement.FocusedElement;
            if (el == null)
                return info;
            if (el.Current.IsPassword)
            {
                info.IsPassword = true;
                return info;
            }
            object pat;
            if (!el.TryGetCurrentPattern(TextPattern.Pattern, out pat))
                return info;
            TextPatternRange[] sel = ((TextPattern)pat).GetSelection();
            if (sel.Length == 0)
                return info;
            const int Max = 300;
            TextPatternRange before = sel[0].Clone();
            before.MoveEndpointByRange(TextPatternRangeEndpoint.End, sel[0], TextPatternRangeEndpoint.Start);
            int moved = before.MoveEndpointByUnit(TextPatternRangeEndpoint.Start, TextUnit.Character, -Max);
            info.TextBefore = before.GetText(Max + 50) ?? "";
            info.StartKnown = -moved < Max;
            return info;
        }

        /// <summary>Texte sélectionné (null si l'application ne le permet pas).</summary>
        public static object SelectedText()
        {
            TextPatternRange sel = Selection();
            if (sel == null)
                return null;
            return sel.GetText(20000) ?? "";
        }

        /// <summary>Paragraphe qui contient le curseur.</summary>
        public static object CurrentParagraph()
        {
            TextPatternRange sel = Selection();
            if (sel == null)
                return null;
            TextPatternRange p = sel.Clone();
            p.ExpandToEnclosingUnit(TextUnit.Paragraph);
            return p.GetText(20000);
        }

        public static string ProcessName(uint pid)
        {
            try
            {
                using (var p = Process.GetProcessById((int)pid))
                    return p.ProcessName;
            }
            catch (Exception)
            {
                return "";
            }
        }
    }
}
