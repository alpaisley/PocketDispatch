using System.Globalization;
using System.Text;

namespace PocketDispatch.Core;

public enum Font { Helv, HelvB, Cour, CourB }

public readonly record struct Rgb(double R, double G, double B)
{
    public static Rgb Hex(string h) => new(
        Convert.ToInt32(h.Substring(1, 2), 16) / 255.0,
        Convert.ToInt32(h.Substring(3, 2), 16) / 255.0,
        Convert.ToInt32(h.Substring(5, 2), 16) / 255.0);
}

/// <summary>
/// Minimal PDF writer: standard fonts (WinAnsi), text, filled/stroked
/// rectangles, lines and JPEG images. Coordinates are points from the
/// TOP-LEFT of the page.
/// </summary>
public sealed class PdfDoc
{
    private readonly List<PdfPage> _pages = new();
    public string Title { get; set; } = "";

    public PdfPage AddPage(double width, double height)
    {
        var p = new PdfPage(width, height);
        _pages.Add(p);
        return p;
    }

    public static double Width(string s, Font f, double size)
    {
        var tbl = f switch
        {
            Font.HelvB => FontMetrics.HelvB,
            Font.Cour => FontMetrics.Cour,
            Font.CourB => FontMetrics.CourB,
            _ => FontMetrics.Helv,
        };
        double w = 0;
        foreach (var b in PdfPage.Encode(s)) w += tbl[b] == 0 ? 500 : tbl[b];
        return w * size / 1000.0;
    }

    public void Save(string path)
    {
        using var fs = File.Create(path);
        Write(fs);
    }

    public void Write(Stream output)
    {
        var objs = new List<byte[]>();          // object bodies, index+1 = object number
        int Reserve() { objs.Add(Array.Empty<byte>()); return objs.Count; }
        void Set(int n, string body) => objs[n - 1] = Latin1(body);
        void SetBytes(int n, byte[] body) => objs[n - 1] = body;

        int catalog = Reserve(), pagesObj = Reserve();
        var fontNames = new[] { "Helvetica", "Helvetica-Bold", "Courier", "Courier-Bold" };
        var fontObjs = new int[4];
        for (int i = 0; i < 4; i++)
        {
            fontObjs[i] = Reserve();
            Set(fontObjs[i], $"<< /Type /Font /Subtype /Type1 /BaseFont /{fontNames[i]} /Encoding /WinAnsiEncoding >>");
        }
        var fontRes = $"/Font << /F0 {fontObjs[0]} 0 R /F1 {fontObjs[1]} 0 R /F2 {fontObjs[2]} 0 R /F3 {fontObjs[3]} 0 R >>";

        var kids = new List<int>();
        foreach (var page in _pages)
        {
            var imgRes = new StringBuilder();
            for (int i = 0; i < page.Images.Count; i++)
            {
                var img = page.Images[i];
                int io = Reserve();
                var head = Latin1($"<< /Type /XObject /Subtype /Image /Width {img.W} /Height {img.H} " +
                                  $"/ColorSpace /DeviceRGB /BitsPerComponent 8 /Filter /DCTDecode /Length {img.Jpeg.Length} >>\nstream\n");
                SetBytes(io, Concat(head, img.Jpeg, Latin1("\nendstream")));
                imgRes.Append($"/Im{i} {io} 0 R ");
            }
            var content = Latin1(page.Content.ToString());
            int co = Reserve();
            SetBytes(co, Concat(Latin1($"<< /Length {content.Length} >>\nstream\n"), content, Latin1("\nendstream")));
            int po = Reserve();
            var xo = imgRes.Length > 0 ? $" /XObject << {imgRes}>>" : "";
            Set(po, $"<< /Type /Page /Parent {pagesObj} 0 R /MediaBox [0 0 {N(page.W)} {N(page.H)}] " +
                    $"/Resources << {fontRes}{xo} >> /Contents {co} 0 R >>");
            kids.Add(po);
        }
        Set(pagesObj, $"<< /Type /Pages /Count {kids.Count} /Kids [{string.Join(" ", kids.Select(k => $"{k} 0 R"))}] >>");
        Set(catalog, $"<< /Type /Catalog /Pages {pagesObj} 0 R >>");
        int info = Reserve();
        Set(info, $"<< /Title ({PdfPage.EscapeText(Title)}) /Producer (Pocket Dispatch) >>");

        var offsets = new long[objs.Count];
        void W(byte[] b) => output.Write(b, 0, b.Length);
        long pos = 0;
        void Out(byte[] b) { W(b); pos += b.Length; }
        Out(Latin1("%PDF-1.4\n%\xE2\xE3\xCF\xD3\n"));
        for (int i = 0; i < objs.Count; i++)
        {
            offsets[i] = pos;
            Out(Latin1($"{i + 1} 0 obj\n"));
            Out(objs[i]);
            Out(Latin1("\nendobj\n"));
        }
        long xref = pos;
        var sb = new StringBuilder();
        sb.Append($"xref\n0 {objs.Count + 1}\n0000000000 65535 f \n");
        foreach (var o in offsets) sb.Append($"{o:D10} 00000 n \n");
        sb.Append($"trailer\n<< /Size {objs.Count + 1} /Root {catalog} 0 R /Info {info} 0 R >>\nstartxref\n{xref}\n%%EOF\n");
        Out(Latin1(sb.ToString()));
    }

    internal static string N(double v) => Math.Round(v, 2).ToString("0.##", CultureInfo.InvariantCulture);
    private static byte[] Latin1(string s) => Encoding.Latin1.GetBytes(s);
    private static byte[] Concat(params byte[][] parts)
    {
        var r = new byte[parts.Sum(p => p.Length)];
        int o = 0;
        foreach (var p in parts) { Buffer.BlockCopy(p, 0, r, o, p.Length); o += p.Length; }
        return r;
    }
}

public sealed class PdfPage
{
    internal readonly StringBuilder Content = new();
    internal readonly List<(byte[] Jpeg, int W, int H)> Images = new();
    public double W { get; }
    public double H { get; }

    internal PdfPage(double w, double h) { W = w; H = h; }

    private static string N(double v) => PdfDoc.N(v);
    private static string C(Rgb c) => $"{N(c.R)} {N(c.G)} {N(c.B)}";

    /// <summary>Draw text with its baseline at (x, y) measured from the top-left.</summary>
    public void Text(double x, double y, string s, Font f, double size, Rgb color)
    {
        if (string.IsNullOrEmpty(s)) return;
        Content.Append($"BT /F{(int)f} {N(size)} Tf {C(color)} rg {N(x)} {N(H - y)} Td ({EscapeText(s)}) Tj ET\n");
    }

    public void TextCentered(double cx, double y, string s, Font f, double size, Rgb color) =>
        Text(cx - PdfDoc.Width(s, f, size) / 2, y, s, f, size, color);

    public void TextRight(double rx, double y, string s, Font f, double size, Rgb color) =>
        Text(rx - PdfDoc.Width(s, f, size), y, s, f, size, color);

    public void FillRect(double x, double y, double w, double h, Rgb c) =>
        Content.Append($"{C(c)} rg {N(x)} {N(H - y - h)} {N(w)} {N(h)} re f\n");

    public void StrokeRect(double x, double y, double w, double h, Rgb c, double lw = 0.5) =>
        Content.Append($"{C(c)} RG {N(lw)} w {N(x)} {N(H - y - h)} {N(w)} {N(h)} re S\n");

    public void Line(double x1, double y1, double x2, double y2, Rgb c, double lw = 0.5) =>
        Content.Append($"{C(c)} RG {N(lw)} w {N(x1)} {N(H - y1)} m {N(x2)} {N(H - y2)} l S\n");

    /// <summary>Place a baseline JPEG (RGB) image.</summary>
    public void Jpeg(byte[] jpeg, int pxW, int pxH, double x, double y, double w, double h)
    {
        Images.Add((jpeg, pxW, pxH));
        Content.Append($"q {N(w)} 0 0 {N(h)} {N(x)} {N(H - y - h)} cm /Im{Images.Count - 1} Do Q\n");
    }

    internal static byte[] Encode(string s)
    {
        var b = new List<byte>(s.Length);
        foreach (var ch in s)
        {
            switch (ch)
            {
                case '→': b.AddRange("->"u8.ToArray()); continue;
                case '–': b.Add(0x96); continue;
                case '—': b.Add(0x97); continue;
                case '•': b.Add(0x95); continue;
                case '‘': b.Add(0x91); continue;
                case '’': b.Add(0x92); continue;
                case '“': b.Add(0x93); continue;
                case '”': b.Add(0x94); continue;
            }
            b.Add(ch < 256 ? (byte)ch : (byte)'?');
        }
        return b.ToArray();
    }

    internal static string EscapeText(string s)
    {
        var sb = new StringBuilder();
        foreach (var by in Encode(s))
        {
            char c = (char)by;
            if (c is '(' or ')' or '\\') sb.Append('\\').Append(c);
            else if (by < 32) sb.Append(' ');
            else sb.Append(c);
        }
        return sb.ToString();
    }
}
