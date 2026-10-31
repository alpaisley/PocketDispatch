using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace PocketDispatch.App
{
    /// <summary>Dark "night cockpit" palette, fonts and drawing helpers.</summary>
    internal static class Theme
    {
        public static readonly Color Bg = Color.FromArgb(14, 22, 33);
        public static readonly Color BgDeep = Color.FromArgb(9, 15, 23);
        public static readonly Color Card = Color.FromArgb(22, 33, 46);
        public static readonly Color Tile = Color.FromArgb(29, 43, 59);
        public static readonly Color Border = Color.FromArgb(38, 56, 76);
        public static readonly Color Text = Color.FromArgb(236, 242, 248);
        public static readonly Color Muted = Color.FromArgb(127, 147, 168);
        public static readonly Color Amber = Color.FromArgb(255, 179, 0);
        public static readonly Color AmberHover = Color.FromArgb(255, 199, 64);
        public static readonly Color Cyan = Color.FromArgb(77, 208, 225);
        public static readonly Color Green = Color.FromArgb(61, 220, 132);
        public static readonly Color Red = Color.FromArgb(255, 107, 107);
        public static readonly Color Magenta = Color.FromArgb(232, 121, 249);
        public static readonly Color Input = Color.FromArgb(15, 25, 36);

        public static Font UI(float size, FontStyle style = FontStyle.Regular) { return new Font("Segoe UI", size, style); }
        /// <summary>Bahnschrift (DIN-style, ships with Windows 10/11) for numbers and codes.</summary>
        public static Font Din(float size, FontStyle style = FontStyle.Regular) { return new Font("Bahnschrift", size, style); }
        public static Font Mono(float size) { return new Font("Consolas", size); }

        public static GraphicsPath Round(RectangleF r, float radius)
        {
            var p = new GraphicsPath();
            float d = Math.Max(1f, Math.Min(radius * 2, Math.Min(r.Width, r.Height)));
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        public static void FillRound(Graphics g, Color c, RectangleF r, float radius)
        {
            using (var b = new SolidBrush(c))
            using (var p = Round(r, radius)) g.FillPath(b, p);
        }

        public static void StrokeRound(Graphics g, Color c, RectangleF r, float radius, float width = 1f)
        {
            using (var pen = new Pen(c, width))
            using (var p = Round(r, radius)) g.DrawPath(pen, p);
        }

        // Plane silhouette (unit square, nose up) - same shape as the app icon.
        static readonly PointF[] PlaneHalf =
        {
            new PointF(0.5f, 0.04f), new PointF(0.545f, 0.10f), new PointF(0.555f, 0.36f), new PointF(0.95f, 0.58f),
            new PointF(0.95f, 0.66f), new PointF(0.555f, 0.53f), new PointF(0.545f, 0.77f), new PointF(0.70f, 0.88f),
            new PointF(0.70f, 0.94f), new PointF(0.5f, 0.89f),
        };

        /// <summary>Draw the plane centred at (cx, cy), <paramref name="size"/> px, rotated <paramref name="deg"/>.</summary>
        public static void Plane(Graphics g, Color c, float cx, float cy, float size, float deg)
        {
            var pts = new PointF[PlaneHalf.Length * 2 - 2];
            int n = 0;
            foreach (var p in PlaneHalf) pts[n++] = p;
            for (int i = PlaneHalf.Length - 2; i >= 1; i--) pts[n++] = new PointF(1 - PlaneHalf[i].X, PlaneHalf[i].Y);
            double a = deg * Math.PI / 180;
            for (int i = 0; i < pts.Length; i++)
            {
                float x = (pts[i].X - 0.5f) * size, y = (pts[i].Y - 0.5f) * size;
                pts[i] = new PointF(cx + (float)(x * Math.Cos(a) - y * Math.Sin(a)), cy + (float)(x * Math.Sin(a) + y * Math.Cos(a)));
            }
            using (var b = new SolidBrush(c)) g.FillPolygon(b, pts);
        }

        /// <summary>The amber app badge (used in the header and as a fallback window icon).</summary>
        public static void Badge(Graphics g, RectangleF r)
        {
            FillRound(g, Amber, r, r.Width * 0.22f);
            Plane(g, BgDeep, r.X + r.Width / 2, r.Y + r.Height / 2, r.Width * 0.78f, 45);
        }

        public static Icon MakeIcon()
        {
            try
            {
                var exe = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
                if (exe != null) return exe;
            }
            catch { }
            var bmp = new Bitmap(64, 64);
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                Badge(g, new RectangleF(1, 1, 62, 62));
            }
            return Icon.FromHandle(bmp.GetHicon());
        }

        public static Button Button(string text, bool primary)
        {
            var b = new Button
            {
                Text = text,
                FlatStyle = FlatStyle.Flat,
                AutoSize = true,
                Cursor = Cursors.Hand,
                Padding = primary ? new Padding(18, 8, 18, 8) : new Padding(10, 5, 10, 5),
                Margin = new Padding(0, 0, 8, 8),
                Font = primary ? UI(11f, FontStyle.Bold) : UI(9.5f),
                BackColor = primary ? Amber : Tile,
                ForeColor = primary ? BgDeep : Text,
                UseVisualStyleBackColor = false,
            };
            b.FlatAppearance.BorderSize = primary ? 0 : 1;
            b.FlatAppearance.BorderColor = Border;
            b.FlatAppearance.MouseOverBackColor = primary ? AmberHover : Border;
            b.FlatAppearance.MouseDownBackColor = primary ? Color.FromArgb(230, 150, 0) : Card;
            return b;
        }

        public static void StyleInput(Control c)
        {
            c.BackColor = Input;
            c.ForeColor = Text;
            var tb = c as TextBox;
            if (tb != null) tb.BorderStyle = BorderStyle.FixedSingle;
            var cb = c as ComboBox;
            if (cb != null) cb.FlatStyle = FlatStyle.Flat;
        }

        [DllImport("dwmapi.dll")]
        static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

        /// <summary>Dark title bar on Windows 10 (1809+) / 11; ignored elsewhere.</summary>
        public static void DarkTitleBar(Form f)
        {
            try
            {
                int on = 1;
                if (DwmSetWindowAttribute(f.Handle, 20, ref on, 4) != 0)
                    DwmSetWindowAttribute(f.Handle, 19, ref on, 4);
            }
            catch { }
        }
    }
}
