namespace PocketDispatch.Core;

public sealed class BriefOptions
{
    public bool A5 { get; set; }
}

/// <summary>Draws the two-page landscape brief (set-up + in-flight).</summary>
public static class BriefRenderer
{
    static readonly Rgb Ink = Rgb.Hex("#111111");
    static readonly Rgb Muted = Rgb.Hex("#666666");
    static readonly Rgb Navy = Rgb.Hex("#1F3A68");
    static readonly Rgb Rule = Rgb.Hex("#C9CFD8");
    static readonly Rgb Head = Rgb.Hex("#E9EDF4");
    static readonly Rgb Write = Rgb.Hex("#FFF6D6");
    static readonly Rgb Key = Rgb.Hex("#F2F6FC");
    static readonly Rgb White = new(1, 1, 1);

    // ------------------------------------------------------------ blocks
    abstract class Block
    {
        public abstract double Measure(double w, double s);
        public abstract void Draw(PdfPage p, double x, double y, double w, double s);
    }

    sealed class Heading : Block
    {
        readonly string _t;
        public Heading(string t) => _t = t;
        public override double Measure(double w, double s) => 24 * s;
        public override void Draw(PdfPage p, double x, double y, double w, double s) =>
            p.Text(x, y + 17 * s, _t, Font.HelvB, 12 * s, Navy);
    }

    sealed class Note : Block
    {
        readonly string _t;
        public Note(string t) => _t = t;
        public override double Measure(double w, double s) => Wrap(_t, Font.Helv, 8 * s, w).Count * 10 * s + 4 * s;
        public override void Draw(PdfPage p, double x, double y, double w, double s)
        {
            var yy = y + 3 * s;
            foreach (var l in Wrap(_t, Font.Helv, 8 * s, w)) { yy += 10 * s; p.Text(x, yy - 2 * s, l, Font.Helv, 8 * s, Muted); }
        }
    }

    public sealed record Row(string Label, string Value, bool Big = false, bool WriteIn = false);

    /// <summary>"Field | Entry" table with a header row.</summary>
    sealed class FieldTable : Block
    {
        readonly List<Row> _rows;
        readonly bool _header;
        readonly double _split;
        public FieldTable(List<Row> rows, bool header = true, double split = 0.46) { _rows = rows; _header = header; _split = split; }

        (double h, List<string> l, List<string> v) RowLayout(Row r, double w, double s)
        {
            double pad = 5 * s;
            var lf = r.Big ? Font.HelvB : Font.Helv;
            var l = Wrap(r.Label, lf, 11.5 * s, w * _split - 2 * pad);
            var vSize = r.Big ? 15 * s : 11.5 * s;
            var v = r.WriteIn ? new List<string> { "" } : Wrap(r.Value, r.Big ? Font.HelvB : Font.Helv, vSize, w * (1 - _split) - 2 * pad);
            double lh = Math.Max(l.Count * 13.5 * s, v.Count * (r.Big ? 17 : 13.5) * s);
            return (lh + 7 * s, l, v);
        }

        public override double Measure(double w, double s) =>
            (_header ? 21 * s : 0) + _rows.Sum(r => RowLayout(r, w, s).h);

        public override void Draw(PdfPage p, double x, double y, double w, double s)
        {
            double pad = 5 * s, lw = w * _split, top = y;
            var lines = new List<double>();
            if (_header)
            {
                p.FillRect(x, y, w, 21 * s, Head);
                p.Text(x + pad, y + 14.5 * s, "Field", Font.HelvB, 11 * s, Ink);
                p.Text(x + lw + pad, y + 14.5 * s, "Entry", Font.HelvB, 11 * s, Ink);
                y += 21 * s;
            }
            foreach (var r in _rows)
            {
                var (h, l, v) = RowLayout(r, w, s);
                if (r.Big) p.FillRect(x, y, w, h, Key);
                if (r.WriteIn) p.FillRect(x + lw, y, w - lw, h, Write);
                double ly = y + (h - l.Count * 13.5 * s) / 2 + 10.5 * s;
                foreach (var t in l) { p.Text(x + pad, ly, t, r.Big ? Font.HelvB : Font.Helv, 11.5 * s, Ink); ly += 13.5 * s; }
                double vl = (r.Big ? 17 : 13.5) * s;
                double vy = y + (h - v.Count * vl) / 2 + (r.Big ? 13 : 10.5) * s;
                foreach (var t in v) { p.Text(x + lw + pad, vy, t, r.Big ? Font.HelvB : Font.Helv, (r.Big ? 15 : 11.5) * s, Ink); vy += vl; }
                y += h;
                lines.Add(y);
            }
            foreach (var ly2 in lines) p.Line(x, ly2, x + w, ly2, Rule);
            p.StrokeRect(x, top, w, y - top, Rule);
            p.Line(x + lw, top, x + lw, y, Rule);
        }
    }

    /// <summary>ICAO | monospace text (METAR / TAF / NOTAM).</summary>
    sealed class CodeTable : Block
    {
        readonly List<(string k, string v)> _rows;
        readonly Font _font;
        readonly double _size;
        public CodeTable(List<(string, string)> rows, Font font = Font.Cour, double size = 9.5) { _rows = rows; _font = font; _size = size; }
        const double Split = 0.17;

        List<string> Lines(string v, double w, double s)
        {
            var res = new List<string>();
            foreach (var para in v.Split('\n')) res.AddRange(Wrap(para, _font, _size * s, w * (1 - Split) - 10 * s));
            return res;
        }

        public override double Measure(double w, double s) =>
            _rows.Sum(r => Lines(r.v, w, s).Count * _size * 1.22 * s + 7 * s);

        public override void Draw(PdfPage p, double x, double y, double w, double s)
        {
            double top = y, kw = w * Split, lead = _size * 1.22 * s;
            foreach (var (k, v) in _rows)
            {
                var lines = Lines(v, w, s);
                double h = lines.Count * lead + 7 * s;
                p.Text(x + 5 * s, y + 3.5 * s + 10 * s, k, Font.HelvB, 10 * s, Ink);
                double ly = y + 3.5 * s + _size * s;
                foreach (var l in lines) { p.Text(x + kw + 5 * s, ly, l, _font, _size * s, Ink); ly += lead; }
                p.Line(x, y + h, x + w, y + h, Rule);
                y += h;
            }
            p.StrokeRect(x, top, w, y - top, Rule);
            p.Line(x + kw, top, x + kw, y, Rule);
        }
    }

    sealed class Gap : Block
    {
        readonly double _h;
        public Gap(double h) => _h = h;
        public override double Measure(double w, double s) => _h * s;
        public override void Draw(PdfPage p, double x, double y, double w, double s) { }
    }

    /// <summary>Draw blocks in a column, shrinking everything until it fits.</summary>
    static void Column(PdfPage p, List<Block> blocks, double x, double y, double w, double h, double baseScale)
    {
        double s = baseScale;
        while (s > baseScale * 0.55 && blocks.Sum(b => b.Measure(w, s)) > h) s -= baseScale * 0.03;
        foreach (var b in blocks) { b.Draw(p, x, y, w, s); y += b.Measure(w, s); }
    }

    // ------------------------------------------------------------ public
    public static void Render(OfpData o, AircraftProfile prof, BriefOptions opt, Stream output)
    {
        double W = opt.A5 ? 595.28 : 841.89, H = opt.A5 ? 419.53 : 595.28;
        double k = W / 841.89;
        double baseScale = Math.Max(k, 0.78);
        double M = 34 * k, gap = 25 * k;
        double CW = W - 2 * M;
        var doc = new PdfDoc { Title = $"{o.Get("callsign")} {o.Origin}-{o.Destination} brief" };

        List<Row> Rows(List<string[]> spec) => spec.Select(r =>
        {
            var big = r[0].StartsWith('!');
            var label = r[0].TrimStart('!');
            var val = Profiles.Fill(r[1], o);
            var write = val.Trim() == "____";
            return new Row(label, val.Replace("____", "_______"), big, write);
        }).ToList();

        string ac = !string.IsNullOrEmpty(prof.Short) ? prof.Short : o.Get("type");
        string subtitle = $"{Title(o.Get("dep_name"))} ({o.Origin})  →  {Title(o.Get("arr_name"))} ({o.Destination})  |  " +
                          $"{o.Get("date")}  |  {o.Get("callsign")}  |  {o.Get("reg")}";

        void Header(PdfPage p, string title)
        {
            p.TextCentered(W / 2, M + 18 * baseScale, title, Font.HelvB, 20 * baseScale, Ink);
            p.TextCentered(W / 2, M + 34 * baseScale, subtitle, Font.Helv, 10.5 * baseScale, Muted);
            var f = 7 * baseScale;
            p.Text(M, H - 14 * k, "Pocket Dispatch  ·  simulator use only - generated from SimBrief. Verify entries in the aircraft FMS.", Font.Helv, f, Muted);
            p.TextRight(W - M, H - 14 * k, $"{prof.Name}  ·  AIRAC {o.Get("airac")}  ·  generated {o.Generated:HH:mm}Z", Font.Helv, f, Muted);
        }

        double bodyTop = M + 50 * baseScale, bodyH = H - 24 * k - bodyTop;
        double colW = (CW - 2 * gap) / 3;

        // ===================== PAGE 1: SET-UP =====================
        var p1 = doc.AddPage(W, H);
        Header(p1, $"{ac} FMS SETUP");

        var c1 = new List<Block>
        {
            new Heading("1. INIT"), new FieldTable(Rows(prof.Init)),
            new Heading("2. ROUTE"), new FieldTable(Rows(Profiles.Route)),
        };
        var c2 = new List<Block>
        {
            new Heading("3. FUEL / WEIGHT"), new FieldTable(Rows(prof.Fuel)),
            new Heading("4. TAKE-OFF PERFORMANCE"), new FieldTable(Rows(prof.Takeoff)),
        };
        if (o.Get("to_oat") != "-")
            c2.Add(new Note($"Planned {o.Get("to_oat")}°C, wind {o.Get("to_wind")}, QNH {o.Get("to_qnh")}" +
                            (o.Get("to_ils") != "-" ? $". Departure ILS {o.Get("to_ils")}." : ".")));
        var c3 = new List<Block>
        {
            new Heading($"5. ARRIVAL  {o.Destination} {o.Get("ld_rwy")}"), new FieldTable(Rows(prof.Arrival)),
        };
        if (o.Get("ld_surface") != "-")
            c3.Add(new Note($"{o.Get("ld_surface")} RUNWAY planned, wind {o.Get("ld_wind")}, QNH {o.Get("ld_qnh")}"));
        c3.Add(new Heading("6. CLEARANCE"));
        c3.Add(new FieldTable(Rows(Profiles.Clearance)));
        var metars = o.Weather.Where(w => w.Metar.Length > 0).Select(w => (w.Icao, StripMetar(w.Metar))).ToList();
        if (metars.Count > 0) { c3.Add(new Heading("7. WEATHER NOW")); c3.Add(new CodeTable(metars)); }

        Column(p1, c1, M, bodyTop, colW, bodyH, baseScale);
        Column(p1, c2, M + colW + gap, bodyTop, colW, bodyH, baseScale);
        Column(p1, c3, M + 2 * (colW + gap), bodyTop, colW, bodyH, baseScale);

        // ===================== PAGE 2: IN-FLIGHT =====================
        var p2 = doc.AddPage(W, H);
        Header(p2, "IN-FLIGHT");
        double navW = CW * 0.60;
        NavLog(p2, o, M, bodyTop, navW, bodyH, baseScale);

        var side = new List<Block>
        {
            new Heading("KEY NUMBERS"),
            new FieldTable(new List<Row>
            {
                new("Landing fuel (plan)", $"{o.Get("ldg_fuel_f")} {o.Get("wu")}"),
                new("Reserve (FINRES+ALTN)", $"{o.Get("reserve_f")} {o.Get("wu")}"),
                new("Alternate", o.Get("altn_line") != "-" ? o.Get("altn_line") : o.Get("altn")),
                new("Avg wind / ISA", $"{o.Get("avg_wind")} ({o.Get("trip_wind")}) / {o.Get("isa")}"),
                new("SELCAL", o.Get("selcal")),
            }),
        };
        var tafs = o.Weather.Where(w => w.Taf.Length > 0).Select(w => (w.Icao, SplitTaf(w.Taf))).ToList();
        if (tafs.Count > 0) { side.Add(new Heading("FORECASTS (TAF)")); side.Add(new CodeTable(tafs, Font.Cour, 9)); }
        if (o.Notams.Count > 0)
        {
            side.Add(new Heading("AIRPORT NOTAMS"));
            side.Add(new CodeTable(o.Notams.Select(n => (n.Icao, n.Text)).ToList(), Font.Helv, 9));
            if (o.NotamsOmitted > 0)
                side.Add(new Note($"{o.NotamsOmitted} other NOTAMs (stands, cranes, displays, obstacles...) left out."));
        }
        Column(p2, side, M + navW + gap, bodyTop, CW - navW - gap, bodyH, baseScale);

        doc.Write(output);
    }

    static void NavLog(PdfPage p, OfpData o, double x, double y, double w, double h, double bs)
    {
        p.Text(x, y + 17 * bs, "NAV LOG", Font.HelvB, 12 * bs, Navy);
        y += 24 * bs;
        h -= 24 * bs + 14 * bs;
        string[] hdr = { "FIX", "AWY", "FL", "DIST", "TIME", "ETO", "ATO", "FUEL", "ACT", "MORA" };
        double[] rel = { 1.25, 1.25, 0.7, 0.75, 0.85, 0.95, 0.95, 1.0, 0.95, 0.9 };
        double sum = rel.Sum();
        var cw = rel.Select(r => w * r / sum).ToArray();
        int n = o.NavLog.Count + 1;
        double rh = Math.Min(19 * bs, h / n);
        double fs = Math.Min(9 * bs, rh * 0.55);
        double cx;

        void RowBg(double yy, Rgb c, int from, int to)
        {
            double xx = x + cw.Take(from).Sum();
            p.FillRect(xx, yy, cw.Skip(from).Take(to - from + 1).Sum(), rh, c);
        }

        p.FillRect(x, y, w, rh, Head);
        cx = x;
        for (int i = 0; i < hdr.Length; i++)
        {
            if (i < 2) p.Text(cx + 4 * bs, y + rh * 0.68, hdr[i], Font.HelvB, fs, Ink);
            else p.TextCentered(cx + cw[i] / 2, y + rh * 0.68, hdr[i], Font.HelvB, fs, Ink);
            cx += cw[i];
        }
        double top = y;
        y += rh;
        foreach (var f in o.NavLog)
        {
            RowBg(y, Write, 5, 6);
            RowBg(y, Write, 8, 8);
            if (f.IsTocTod) { RowBg(y, Key, 0, 4); RowBg(y, Key, 7, 7); RowBg(y, Key, 9, 9); }
            string[] v = { f.Ident, f.Airway, f.FL, f.RemDist, f.TimeTotal, "", "", f.FuelOnboard, "", f.Mora };
            cx = x;
            for (int i = 0; i < v.Length; i++)
            {
                var txt = Fit(v[i], i == 0 ? Font.HelvB : Font.Helv, fs, cw[i] - 6 * bs);
                if (i < 2) p.Text(cx + 4 * bs, y + rh * 0.68, txt, i == 0 ? Font.HelvB : Font.Helv, fs, Ink);
                else p.TextCentered(cx + cw[i] / 2, y + rh * 0.68, txt, Font.Helv, fs, Ink);
                cx += cw[i];
            }
            y += rh;
            p.Line(x, y, x + w, y, Rule, 0.4);
        }
        p.StrokeRect(x, top, w, y - top, Rule);
        cx = x;
        for (int i = 0; i < cw.Length - 1; i++) { cx += cw[i]; p.Line(cx, top, cx, y, Rule, 0.4); }
        p.Text(x, y + 11 * bs, $"DIST = remaining nm  ·  TIME = elapsed from take-off ({o.Get("t_off")})  ·  " +
                               $"FUEL = planned on board ({o.Get("wu")})  ·  MORA in ft", Font.Helv, 7.5 * bs, Muted);
    }

    // ------------------------------------------------------------ text utils
    public static List<string> Wrap(string text, Font f, double size, double maxW)
    {
        var lines = new List<string>();
        if (string.IsNullOrEmpty(text)) { lines.Add(""); return lines; }
        var cur = "";
        foreach (var word in text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            var w = word;
            var trial = cur.Length == 0 ? w : cur + " " + w;
            if (PdfDoc.Width(trial, f, size) <= maxW) { cur = trial; continue; }
            if (cur.Length > 0) lines.Add(cur);
            while (PdfDoc.Width(w, f, size) > maxW && w.Length > 1)
            {
                int i = w.Length - 1;
                while (i > 1 && PdfDoc.Width(w[..i], f, size) > maxW) i--;
                lines.Add(w[..i]);
                w = w[i..];
            }
            cur = w;
        }
        if (cur.Length > 0 || lines.Count == 0) lines.Add(cur);
        return lines;
    }

    static string Fit(string s, Font f, double size, double maxW)
    {
        if (PdfDoc.Width(s, f, size) <= maxW) return s;
        while (s.Length > 1 && PdfDoc.Width(s + ".", f, size) > maxW) s = s[..^1];
        return s + ".";
    }

    static string Title(string s) =>
        s == "-" ? "" : System.Globalization.CultureInfo.InvariantCulture.TextInfo.ToTitleCase(s.ToLowerInvariant()).ToUpperInvariant();

    static string StripMetar(string m)
    {
        var t = System.Text.RegularExpressions.Regex.Replace(m.Trim(), @"\s+", " ");
        return System.Text.RegularExpressions.Regex.Replace(t, @"^(METAR |SPECI )?[A-Z]{4} \d{6}Z? ", "");
    }

    static string SplitTaf(string taf)
    {
        var t = System.Text.RegularExpressions.Regex.Replace(taf.Trim(), @"\s+", " ");
        t = System.Text.RegularExpressions.Regex.Replace(t, @"^(TAF )?(AMD |COR )?[A-Z]{4} \d{6}Z? ", "");
        return System.Text.RegularExpressions.Regex.Replace(t, @" (?=(TEMPO|BECMG|PROB\d0|FM\d{6})\b)", "\n")
            .Replace("PROB30\nTEMPO", "PROB30 TEMPO").Replace("PROB40\nTEMPO", "PROB40 TEMPO");
    }
}
