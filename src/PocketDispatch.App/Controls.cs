using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Windows.Forms;
using PocketDispatch.Core;

namespace PocketDispatch.App
{
    /// <summary>Top banner: badge, app name, tag line. Hosts the auto-download pill.</summary>
    internal sealed class HeaderBar : Panel
    {
        public HeaderBar()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);
            Height = 78;
            Dock = DockStyle.Top;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            using (var b = new LinearGradientBrush(ClientRectangle, Color.FromArgb(10, 17, 27), Color.FromArgb(22, 40, 64), 0f))
                g.FillRectangle(b, ClientRectangle);
            // thin amber "glare shield" line
            using (var b = new LinearGradientBrush(new Rectangle(0, Height - 3, Width, 3), Theme.Amber, Color.FromArgb(0, Theme.Amber), 0f))
                g.FillRectangle(b, 0, Height - 3, Width, 3);

            Theme.Badge(g, new RectangleF(18, 15, 46, 46));
            using (var f = Theme.Din(20f, FontStyle.Bold))
            using (var br = new SolidBrush(Theme.Text))
                g.DrawString("POCKET DISPATCH", f, br, 74, 13);
            using (var f = Theme.UI(9.5f))
            using (var br = new SolidBrush(Theme.Muted))
                g.DrawString("SimBrief  →  AviTab brief  +  X-Plane FMS plan", f, br, 77, 45);
        }
    }

    /// <summary>Rounded on/off toggle: "AUTO-DOWNLOAD  ON".</summary>
    internal sealed class Pill : Control
    {
        bool _on;
        bool _hover;
        public event EventHandler Toggled;

        public bool On
        {
            get { return _on; }
            set { _on = value; Invalidate(); }
        }

        public string Detail { get; set; }

        public Pill()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw |
                     ControlStyles.UserPaint | ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
            Size = new Size(230, 40);
            Cursor = Cursors.Hand;
            Detail = "";
        }

        protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnClick(EventArgs e)
        {
            On = !On;
            if (Toggled != null) Toggled(this, EventArgs.Empty);
            base.OnClick(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            var r = new RectangleF(1, 1, Width - 3, Height - 3);
            var accent = _on ? Theme.Green : Theme.Muted;
            Theme.FillRound(g, _on ? Color.FromArgb(26, 58, 44) : Color.FromArgb(_hover ? 40 : 28, 42, 56), r, r.Height / 2);
            Theme.StrokeRound(g, accent, r, r.Height / 2, 1.2f);
            // switch knob
            var track = new RectangleF(12, Height / 2f - 8, 30, 16);
            Theme.FillRound(g, _on ? Theme.Green : Color.FromArgb(70, 84, 100), track, 8);
            using (var kb = new SolidBrush(Theme.Text))
                g.FillEllipse(kb, _on ? track.Right - 14 : track.X + 2, track.Y + 2, 12, 12);
            using (var f = Theme.Din(9.5f, FontStyle.Bold))
            using (var br = new SolidBrush(_on ? Theme.Green : Theme.Text))
                g.DrawString(_on ? "AUTO-DOWNLOAD  ON" : "AUTO-DOWNLOAD  OFF", f, br, 50, Height / 2f - 15);
            using (var f = Theme.UI(7.5f))
            using (var br = new SolidBrush(Theme.Muted))
                g.DrawString(_on ? Detail : "click to watch SimBrief", f, br, 51, Height / 2f + 1);
        }
    }

    /// <summary>Big card showing the latest flight plan on SimBrief.</summary>
    internal sealed class FlightCard : Panel
    {
        OfpData _data;
        string _status = "";
        Color _statusColor = Theme.Muted;

        public FlightCard()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);
            BackColor = Theme.Bg;
        }

        public void Display(OfpData data, string status, Color statusColor)
        {
            _data = data;
            _status = status;
            _statusColor = statusColor;
            Invalidate();
        }

        public void SetStatus(string status, Color color)
        {
            _status = status;
            _statusColor = color;
            Invalidate();
        }

        static string Up(string s) { return s == "-" ? "" : s.ToUpperInvariant(); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            g.Clear(BackColor);
            var card = new RectangleF(0.5f, 0.5f, Width - 2, Height - 2);
            Theme.FillRound(g, Theme.Card, card, 16);
            Theme.StrokeRound(g, Theme.Border, card, 16);

            float pad = 22, w = Width - 2 * pad;
            using (var small = Theme.UI(8f, FontStyle.Bold))
            using (var muted = new SolidBrush(Theme.Muted))
            using (var stBrush = new SolidBrush(_statusColor))
            {
                g.DrawString("LATEST FLIGHT PLAN", small, muted, pad, 18);
                var sz = g.MeasureString(_status, small);
                if (_status.Length > 0)
                {
                    var pr = new RectangleF(Width - pad - sz.Width - 16, 13, sz.Width + 16, sz.Height + 8);
                    Theme.FillRound(g, Color.FromArgb(40, _statusColor), pr, pr.Height / 2);
                    g.DrawString(_status, small, stBrush, pr.X + 8, pr.Y + 4);
                }
            }

            if (_data == null)
            {
                using (var f = Theme.Din(22f, FontStyle.Bold))
                using (var br = new SolidBrush(Theme.Text))
                    g.DrawString("No flight plan yet", f, br, pad, 70);
                using (var f = Theme.UI(10f))
                using (var br = new SolidBrush(Theme.Muted))
                    g.DrawString("Generate a flight on simbrief.com, then hit FETCH -\nor switch on auto-download and it will appear here.", f, br, pad + 2, 112);
                Theme.Plane(g, Color.FromArgb(40, Theme.Amber), Width - 110, Height - 110, 150, 45);
                return;
            }

            var o = _data;
            // ---- route: EGPH ----✈---- EGKK
            float y = 44;
            using (var big = Theme.Din(34f, FontStyle.Bold))
            using (var white = new SolidBrush(Theme.Text))
            using (var nameF = Theme.UI(8.5f))
            using (var muted = new SolidBrush(Theme.Muted))
            {
                var dep = o.Origin;
                var arr = o.Destination;
                var dsz = g.MeasureString(dep, big);
                var asz = g.MeasureString(arr, big);
                g.DrawString(dep, big, white, pad - 4, y);
                g.DrawString(arr, big, white, Width - pad - asz.Width + 4, y);
                float lx1 = pad + dsz.Width + 6, lx2 = Width - pad - asz.Width - 6, ly = y + dsz.Height / 2;
                using (var pen = new Pen(Theme.Border, 2f) { DashStyle = DashStyle.Dash })
                    g.DrawLine(pen, lx1, ly, lx2, ly);
                using (var dot = new SolidBrush(Theme.Cyan))
                {
                    g.FillEllipse(dot, lx1 - 4, ly - 4, 8, 8);
                    g.FillEllipse(dot, lx2 - 4, ly - 4, 8, 8);
                }
                float mid = (lx1 + lx2) / 2;
                using (var bg = new SolidBrush(Theme.Card)) g.FillEllipse(bg, mid - 18, ly - 18, 36, 36);
                Theme.Plane(g, Theme.Amber, mid, ly, 30, 90);
                var dist = o.Get("route_dist");
                if (dist != "-")
                {
                    var t = dist + " nm  ·  " + o.Get("t_air");
                    var ts = g.MeasureString(t, nameF);
                    g.DrawString(t, nameF, muted, mid - ts.Width / 2, ly + 18);
                }
                g.DrawString(Up(o.Get("dep_name")), nameF, muted, pad, y + dsz.Height - 6);
                var an = Up(o.Get("arr_name"));
                g.DrawString(an, nameF, muted, Width - pad - g.MeasureString(an, nameF).Width, y + dsz.Height - 6);
                y += dsz.Height + 22;
            }

            // ---- chips: callsign · type · reg · FL
            using (var f = Theme.Din(10.5f, FontStyle.Bold))
            {
                float x = pad;
                foreach (var chip in new[] { o.Get("callsign"), o.Get("type"), o.Get("reg"), o.Get("cruise_fl").Split(' ')[0], o.Get("t_out") == "-" ? "" : "OUT " + o.Get("t_out") })
                {
                    if (string.IsNullOrEmpty(chip) || chip == "-") continue;
                    var sz = g.MeasureString(chip, f);
                    var r = new RectangleF(x, y, sz.Width + 14, sz.Height + 6);
                    if (r.Right > Width - pad) break;
                    Theme.StrokeRound(g, Theme.Border, r, 6);
                    using (var br = new SolidBrush(Theme.Cyan)) g.DrawString(chip, f, br, x + 7, y + 3);
                    x = r.Right + 8;
                }
                y += 38;
            }

            // ---- stat tiles 3 x 2
            var wu = o.Get("wu");
            var tiles = new[]
            {
                new[] { "BLOCK FUEL", o.Get("block_f"), wu },
                new[] { "ZFW", o.Get("zfw_f"), wu },
                new[] { "RESERVE", o.Get("reserve_f"), wu + "  (FINRES+ALTN)" },
                new[] { "DEP RWY", o.Get("to_rwy"), o.Get("to_ils") == "-" ? "" : "ILS " + o.Get("to_ils") },
                new[] { "V1 / VR / V2", o.Get("to_v1") == "-" ? "-" : o.Get("to_v1") + "/" + o.Get("to_vr") + "/" + o.Get("to_v2"), o.Get("to_flap") == "-" ? "kt" : "kt  ·  flap " + o.Get("to_flap") },
                new[] { "ARR RWY", o.Get("ld_rwy"), o.Get("ld_vref") == "-" ? "" : "VREF " + o.Get("ld_vref") + (o.Get("ld_ils") == "-" ? "" : "  ·  ILS " + o.Get("ld_ils")) },
            };
            float gap = 10, tw = (w - 2 * gap) / 3, th = 74;
            using (var lab = Theme.UI(7.5f, FontStyle.Bold))
            using (var val = Theme.Din(17f, FontStyle.Bold))
            using (var unit = Theme.UI(8f))
            using (var muted = new SolidBrush(Theme.Muted))
            using (var white = new SolidBrush(Theme.Text))
            {
                for (int i = 0; i < tiles.Length; i++)
                {
                    float tx = pad + (i % 3) * (tw + gap), ty = y + (i / 3) * (th + gap);
                    if (ty + th > Height - 8) break;
                    var r = new RectangleF(tx, ty, tw, th);
                    Theme.FillRound(g, Theme.Tile, r, 10);
                    using (var acc = new SolidBrush(i == 4 ? Theme.Amber : Theme.Cyan))
                        g.FillRectangle(acc, tx, ty + 12, 3, th - 24);
                    g.DrawString(tiles[i][0], lab, muted, tx + 12, ty + 9);
                    g.DrawString(tiles[i][1], val, white, new RectangleF(tx + 10, ty + 23, tw - 14, 30), new StringFormat(StringFormatFlags.NoWrap) { Trimming = StringTrimming.EllipsisCharacter });
                    if (tiles[i][2].Length > 0)
                        g.DrawString(tiles[i][2], unit, muted, new RectangleF(tx + 12, ty + th - 21, tw - 16, 16), new StringFormat(StringFormatFlags.NoWrap) { Trimming = StringTrimming.EllipsisCharacter });
                }
                y += 2 * th + gap + 14;
            }

            // ---- route strip (fills the rest of the card)
            float rh = Height - y - pad + 4;
            if (rh > 34)
            {
                var rr = new RectangleF(pad, y, w, rh);
                Theme.FillRound(g, Theme.BgDeep, rr, 10);
                using (var lab = Theme.UI(7.5f, FontStyle.Bold))
                using (var muted = new SolidBrush(Theme.Muted))
                using (var mono = Theme.Mono(9f))
                using (var amber = new SolidBrush(Theme.Amber))
                using (var cyan = new SolidBrush(Theme.Cyan))
                using (var white = new SolidBrush(Theme.Text))
                {
                    g.DrawString("ROUTE", lab, muted, rr.X + 12, rr.Y + 8);
                    float lx = rr.X + 12, ly = rr.Y + 24, right = rr.Right - 12;
                    // monospace: measure one character, so spaces are counted reliably
                    float cw = g.MeasureString("MMMMMMMMMM", mono).Width / 10f, lh = mono.GetHeight(g) + 1;
                    var parts = new[] {
                        new { T = o.Get("sid"), B = (Brush)amber },
                        new { T = o.Get("enroute"), B = (Brush)white },
                        new { T = o.Get("star"), B = (Brush)amber },
                    };
                    foreach (var part in parts)
                    {
                        if (part.T == "-") continue;
                        foreach (var word in part.T.Split(' '))
                        {
                            float ww = word.Length * cw;
                            if (lx + ww > right) { lx = rr.X + 12; ly += lh; }
                            if (ly + lh > rr.Bottom - 4) break;
                            g.DrawString(word, mono, part.B, lx, ly);
                            lx += ww + cw;
                        }
                    }
                    var alt = o.Get("altn");
                    if (alt != "-")
                    {
                        var at = "ALTN " + alt;
                        var asz = g.MeasureString(at, lab);
                        g.DrawString(at, lab, cyan, rr.Right - 12 - asz.Width, rr.Y + 8);
                    }
                }
            }
        }
    }
}
