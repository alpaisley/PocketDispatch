using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace PocketDispatch.Core;

public sealed record NavFix(string Ident, string Airway, string FL, string RemDist, string TimeTotal,
                            string FuelOnboard, string Mora, string Wind, string Oat, bool IsTocTod, string Freq);

public sealed record Wx(string Icao, string Role, string Metar, string Taf);

public sealed record Notam(string Icao, string Id, string Text);

/// <summary>
/// Everything the brief needs, pulled from SimBrief's XML (xml.fetcher.php).
/// <see cref="Ctx"/> holds the string values used by profile templates.
/// </summary>
public sealed class OfpData
{
    public Dictionary<string, string> Ctx { get; } = new(StringComparer.OrdinalIgnoreCase);
    public List<NavFix> NavLog { get; } = new();
    public List<Wx> Weather { get; } = new();
    public List<Notam> Notams { get; } = new();
    public int NotamsOmitted { get; set; }

    public string RequestId { get; private set; } = "";
    public string PdfUrl { get; private set; } = "";
    public Dictionary<string, string> FmsUrls { get; } = new(StringComparer.OrdinalIgnoreCase);
    public string Origin => Get("dep");
    public string Destination => Get("arr");
    public DateTime Generated { get; private set; } = DateTime.UtcNow;

    public string Get(string key) => Ctx.TryGetValue(key, out var v) && !string.IsNullOrWhiteSpace(v) ? v : "-";

    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    public static OfpData FromXml(string xml) => FromXml(XDocument.Parse(xml).Root!);

    public static OfpData FromXml(XElement root)
    {
        var o = new OfpData();
        string T(string path) => Text(root, path);
        var c = o.Ctx;

        var status = T("fetch/status");
        if (status.Length > 0 && !status.Equals("Success", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("SimBrief: " + status);

        // ---- ids & links ------------------------------------------------
        o.RequestId = FirstNonEmpty(T("params/request_id"), T("params/time_generated"));
        if (long.TryParse(T("params/time_generated"), out var gen))
            o.Generated = DateTimeOffset.FromUnixTimeSeconds(gen).UtcDateTime;
        if (T("files/directory").Length > 0 && T("files/pdf/link").Length > 0)
            o.PdfUrl = T("files/directory") + T("files/pdf/link");
        var fdir = T("fms_downloads/directory");
        foreach (var e in root.Element("fms_downloads")?.Elements() ?? Enumerable.Empty<XElement>())
        {
            var link = e.Element("link")?.Value.Trim();
            if (!string.IsNullOrEmpty(link)) o.FmsUrls[e.Name.LocalName] = fdir + link;
        }

        // ---- general ------------------------------------------------------
        bool kg = !T("params/units").StartsWith("lb", StringComparison.OrdinalIgnoreCase);
        c["wu"] = kg ? "kg" : "lb";
        c["dep"] = T("origin/icao_code");
        c["arr"] = T("destination/icao_code");
        c["dep_name"] = T("origin/name");
        c["arr_name"] = T("destination/name");
        c["flight"] = T("general/icao_airline") + T("general/flight_number");
        c["callsign"] = FirstNonEmpty(T("atc/callsign"), c["flight"]);
        c["reg"] = T("aircraft/reg");
        c["type"] = FirstNonEmpty(T("aircraft/icaocode"), T("aircraft/icao_code"));
        c["type_name"] = T("aircraft/name");
        // SimBrief "Aircraft Name" - for a saved airframe this is the name you gave it
        c["ac_name"] = c["type_name"];
        c["airframe"] = string.Join(" · ", new[] { c["reg"], c["ac_name"] }.Where(v => !string.IsNullOrWhiteSpace(v)));
        c["selcal"] = T("aircraft/selcal");
        c["airac"] = T("params/airac");
        c["date"] = o.Generated.ToString("dd MMM yyyy", Inv).ToUpperInvariant();

        // cruise
        var initAlt = Num(T("general/initial_altitude"));
        c["cruise_fl"] = initAlt > 0 ? $"FL{initAlt / 100:000}" : "-";
        var steps = T("general/stepclimb_string");
        var stepFls = Regex.Matches(steps, @"/(\d{4})").Select(m => "FL" + m.Groups[1].Value.TrimStart('0')).Distinct().ToList();
        if (stepFls.Count > 1) c["cruise_fl"] += "  (steps " + string.Join(" ", stepFls.Skip(1)) + ")";
        var profile = T("general/cruise_profile");
        var ci = T("general/costindex");
        var mach = T("general/cruise_mach");
        c["cost_index"] = Regex.IsMatch(ci, @"^\d+$") ? "CI " + ci : (profile.Length > 0 ? profile : "-");
        if (Regex.IsMatch(profile, @"^M?\.?\d{2,3}$") && !profile.StartsWith("CI"))
            c["cruise_speed"] = "M0." + Regex.Match(profile, @"\d{2}").Value;
        else if (mach.Length > 0 && double.TryParse(mach, NumberStyles.Float, Inv, out var mval))
            c["cruise_speed"] = $"M{mval:0.00}" + (Regex.IsMatch(ci, @"^\d+$") ? $"  (CI {ci})" : "");
        else c["cruise_speed"] = c["cost_index"];

        var isa = Num(T("general/avg_temp_dev"));
        c["isa"] = T("general/avg_temp_dev").Length > 0 ? $"ISA {(isa >= 0 ? "+" : "")}{isa}" : "-";
        var wcomp = Num(T("general/avg_wind_comp"));
        c["trip_wind"] = T("general/avg_wind_comp").Length > 0 ? (wcomp < 0 ? "HD" : "TL") + Math.Abs(wcomp).ToString("000") : "-";
        c["avg_wind"] = T("general/avg_wind_dir").Length > 0 ? $"{Num(T("general/avg_wind_dir")):000}/{Num(T("general/avg_wind_spd")):000}" : "-";
        var tropo = Num(T("general/avg_tropopause"));
        c["tropo"] = tropo > 0 ? $"{tropo} ft" : "-";
        c["route_dist"] = T("general/route_distance");
        c["altn"] = FirstNonEmpty(T("alternate/icao_code"), "-");

        // ---- times --------------------------------------------------------
        string Z(string p) => long.TryParse(T(p), out var s) && s > 100000
            ? DateTimeOffset.FromUnixTimeSeconds(s).UtcDateTime.ToString("HH:mm", Inv) + "Z" : "-";
        string Dur(string p) => long.TryParse(T(p), out var s) ? $"{s / 3600:00}:{s % 3600 / 60:00}" : "-";
        c["t_out"] = Z("times/est_out"); c["t_off"] = Z("times/est_off");
        c["t_on"] = Z("times/est_on"); c["t_in"] = Z("times/est_in");
        c["t_block"] = Dur("times/est_block"); c["t_air"] = Dur("times/est_time_enroute");

        // ---- fuel & weights ------------------------------------------------
        int F(string p) => Num(T(p));
        string Fmt(int v) => v.ToString("N0", Inv);
        int block = F("fuel/plan_ramp"), taxi = F("fuel/taxi"), trip = F("fuel/enroute_burn");
        int cont = F("fuel/contingency"), altnF = F("fuel/alternate_burn"), finres = F("fuel/reserve");
        c["block"] = block.ToString(); c["taxi"] = taxi.ToString(); c["trip"] = trip.ToString();
        c["cont"] = cont.ToString(); c["altn_fuel"] = altnF.ToString(); c["finres"] = finres.ToString();
        c["fmc_reserve"] = (altnF + finres).ToString();
        c["block_f"] = Fmt(block); c["taxi_f"] = Fmt(taxi); c["trip_f"] = Fmt(trip); c["cont_f"] = Fmt(cont);
        c["altn_f"] = Fmt(altnF); c["finres_f"] = Fmt(finres); c["reserve_f"] = Fmt(altnF + finres);
        c["tof_f"] = Fmt(F("fuel/plan_takeoff"));
        c["ldg_fuel_f"] = Fmt(F("fuel/plan_landing"));
        c["extra_f"] = Fmt(F("fuel/extra"));
        c["finres_time"] = Dur("times/reserve_time");
        c["zfw"] = T("weights/est_zfw");
        c["zfw_f"] = Fmt(F("weights/est_zfw")); c["tow_f"] = Fmt(F("weights/est_tow")); c["law_f"] = Fmt(F("weights/est_ldw"));
        c["max_zfw_f"] = Fmt(F("weights/max_zfw")); c["max_tow_f"] = Fmt(F("weights/max_tow")); c["max_law_f"] = Fmt(F("weights/max_ldw"));
        c["pax"] = T("weights/pax_count");
        c["cargo_f"] = Fmt(F("weights/cargo"));

        // ---- route --------------------------------------------------------
        var route = T("general/route");
        var toks = route.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList();
        if (toks.Count > 0 && toks[0].Contains('/')) toks.RemoveAt(0);
        if (toks.Count > 0 && toks[^1].Contains('/')) toks.RemoveAt(toks.Count - 1);
        if (toks.Count > 0 && (toks[0] == c["dep"])) toks.RemoveAt(0);
        if (toks.Count > 0 && (toks[^1] == c["arr"])) toks.RemoveAt(toks.Count - 1);
        var proc = new Regex(@"^[A-Z]{2,5}\d[A-Z]?$");
        c["sid"] = toks.Count > 0 && proc.IsMatch(toks[0]) ? Pop(toks, 0) : "-";
        c["star"] = toks.Count > 0 && proc.IsMatch(toks[^1]) ? Pop(toks, toks.Count - 1) : "-";
        c["enroute"] = toks.Count > 0 ? string.Join(" ", toks) : "-";
        c["route"] = route;

        // alternate
        var alt = root.Element("alternate");
        if (alt != null)
        {
            var aFl = Num(Text(alt, "cruise_altitude"));
            c["altn_line"] = $"{Text(alt, "icao_code")} · {Text(alt, "distance")} nm · FL{aFl / 100:000} · " +
                             $"{(long.TryParse(Text(alt, "ete"), out var ete) ? $"{ete / 3600:00}:{ete % 3600 / 60:00}" : "-")} · " +
                             $"{Fmt(Num(Text(alt, "burn")))} {c["wu"]}";
            c["altn_route"] = Text(alt, "route");
        }

        // ---- TLR ----------------------------------------------------------
        var toC = root.Element("tlr")?.Element("takeoff")?.Element("conditions");
        var toRwyId = toC != null ? Text(toC, "planned_runway") : T("origin/plan_rwy");
        c["to_rwy"] = FirstNonEmpty(toRwyId, T("origin/plan_rwy"), "-");
        var toRwy = root.Element("tlr")?.Element("takeoff")?.Elements("runway")
            .FirstOrDefault(r => Text(r, "identifier") == c["to_rwy"]);
        if (toC != null)
        {
            c["to_oat"] = Text(toC, "temperature");
            c["to_wind"] = $"{Num(Text(toC, "wind_direction")):000}/{Num(Text(toC, "wind_speed")):00}";
            c["to_qnh"] = Text(toC, "altimeter");
            c["to_surface"] = Text(toC, "surface_condition");
        }
        if (toRwy != null)
        {
            string R(string n) => Text(toRwy, n);
            c["to_len"] = LenFmt(R("length"), out var unit); c["len_unit"] = unit;
            c["to_ils"] = R("ils_frequency");
            c["to_flap"] = R("flap_setting");
            c["to_thrust"] = R("thrust_setting");
            c["to_bleed"] = R("bleed_setting");
            c["to_antiice"] = R("anti_ice_setting");
            c["to_flex"] = R("flex_temperature");
            c["to_maxtemp"] = R("max_temperature");
            c["to_temp_val"] = FirstNonEmpty(R("flex_temperature"), R("max_temperature"), "-");
            c["to_v1"] = R("speeds_v1"); c["to_vr"] = R("speeds_vr"); c["to_v2"] = R("speeds_v2");
            c["to_vother"] = R("speeds_other");
            c["to_vother_id"] = R("speeds_other_id");
            c["to_limit"] = R("limit_code");
        }
        var land = root.Element("tlr")?.Element("landing");
        var ldC = land?.Element("conditions");
        c["ld_rwy"] = FirstNonEmpty(ldC != null ? Text(ldC, "planned_runway") : "", T("destination/plan_rwy"), "-");
        if (ldC != null)
        {
            c["ld_flap"] = Text(ldC, "flap_setting");
            c["ld_oat"] = Text(ldC, "temperature");
            c["ld_wind"] = $"{Num(Text(ldC, "wind_direction")):000}/{Num(Text(ldC, "wind_speed")):00}";
            c["ld_qnh"] = Text(ldC, "altimeter");
            c["ld_surface"] = Text(ldC, "surface_condition").ToUpperInvariant();
        }
        var dd = land?.Element("distance_dry");
        var dw = land?.Element("distance_wet");
        if (dd != null)
        {
            c["ld_vref"] = Text(dd, "speeds_vref");
            c["ld_dry"] = LenFmt(Text(dd, "actual_distance"), out _);
            c["ld_dry_fact"] = LenFmt(Text(dd, "factored_distance"), out _);
            if (string.IsNullOrEmpty(c.GetValueOrDefault("ld_flap"))) c["ld_flap"] = Text(dd, "flap_setting");
        }
        if (dw != null)
        {
            c["ld_vref_wet"] = Text(dw, "speeds_vref");
            c["ld_wet"] = LenFmt(Text(dw, "actual_distance"), out _);
            c["ld_wet_fact"] = LenFmt(Text(dw, "factored_distance"), out _);
        }
        var ldRwy = land?.Elements("runway").FirstOrDefault(r => Text(r, "identifier") == c["ld_rwy"]);
        if (ldRwy != null)
        {
            c["ld_len"] = LenFmt(Text(ldRwy, "length"), out var u2);
            c.TryAdd("len_unit", u2);
            c["ld_ils"] = Text(ldRwy, "ils_frequency");
        }
        c.TryAdd("len_unit", "ft");

        // transition altitudes (from airport data)
        c["dep_ta"] = T("origin/trans_alt");
        c["arr_tl"] = T("destination/trans_level");

        // ---- nav log --------------------------------------------------------
        var fixes = root.Element("navlog")?.Elements("fix").ToList() ?? new();
        double total = fixes.Sum(f => Dbl(Text(f, "distance")));
        if (total <= 0) total = Num(c["route_dist"]);
        double cum = 0;
        o.NavLog.Add(new NavFix(c["dep"], "", "", ((int)Math.Round(total)).ToString(), "00:00",
            Fmt(F("fuel/plan_takeoff")), Mora(T("origin/elevation"), true), "", "", false, ""));
        foreach (var f in fixes)
        {
            string X(string n) => Text(f, n);
            cum += Dbl(X("distance"));
            var ident = X("ident");
            var type = X("type").ToLowerInvariant();
            bool tt = ident is "TOC" or "TOD" || type is "toc" or "tod";
            var altFt = Num(X("altitude_feet"));
            var fl = altFt > 0 ? (altFt / 100).ToString("000") : "";
            long.TryParse(X("time_total"), out var ts);
            var wind = X("wind_dir").Length > 0 ? $"{Num(X("wind_dir")):000}/{Num(X("wind_spd")):000}" : "";
            var freq = X("frequency");
            if (freq.Length > 0 && double.TryParse(freq, NumberStyles.Float, Inv, out var fq) && fq <= 0) freq = "";
            o.NavLog.Add(new NavFix(ident, X("via_airway"), ident == c["arr"] ? "" : fl,
                Math.Max(0, (int)Math.Round(total - cum)).ToString(),
                $"{ts / 3600:00}:{ts % 3600 / 60:00}", Fmt(Num(X("fuel_plan_onboard"))),
                Mora(X("mora"), false), wind, X("oat"), tt, freq));
        }

        // ---- weather ------------------------------------------------------
        void AddWx(string icao, string role, string metar, string taf)
        {
            if (string.IsNullOrEmpty(icao) || o.Weather.Any(w => w.Icao == icao)) return;
            o.Weather.Add(new Wx(icao, role, metar, taf));
        }
        AddWx(c["dep"], "Departure", FirstNonEmpty(T("origin/metar"), T("weather/orig_metar")), FirstNonEmpty(T("origin/taf"), T("weather/orig_taf")));
        AddWx(c["arr"], "Destination", FirstNonEmpty(T("destination/metar"), T("weather/dest_metar")), FirstNonEmpty(T("destination/taf"), T("weather/dest_taf")));
        AddWx(T("alternate/icao_code"), "Alternate", FirstNonEmpty(T("alternate/metar"), T("weather/altn_metar")), FirstNonEmpty(T("alternate/taf"), T("weather/altn_taf")));

        // ---- NOTAMs -------------------------------------------------------
        var all = new List<Notam>();
        foreach (var (tag, icao) in new[] { ("origin", c["dep"]), ("destination", c["arr"]), ("alternate", T("alternate/icao_code")) })
            foreach (var n in root.Element(tag)?.Elements("notam") ?? Enumerable.Empty<XElement>())
                all.Add(new Notam(icao, FirstNonEmpty(Text(n, "notam_id"), Text(n, "id")),
                    FirstNonEmpty(Text(n, "notam_text"), Text(n, "text"), Text(n, "notam_raw"))));
        if (all.Count == 0)
            foreach (var n in root.Element("notams")?.Elements("notamdrec") ?? Enumerable.Empty<XElement>())
            {
                var icao = Text(n, "icao_id");
                if (icao == c["dep"] || icao == c["arr"] || icao == T("alternate/icao_code"))
                    all.Add(new Notam(icao, Text(n, "notam_id"), FirstNonEmpty(Text(n, "notam_text"), Text(n, "notam_report"))));
            }
        var seen = new HashSet<string>();
        foreach (var n in all)
        {
            if (!seen.Add(n.Icao + n.Id + n.Text)) continue;
            if (NotamFilter.Wanted(n.Text)) o.Notams.Add(n with { Text = NotamFilter.Shorten(n.Text) });
            else o.NotamsOmitted++;
        }
        return o;
    }

    // ---------------------------------------------------------------- helpers
    static string Pop(List<string> l, int i) { var v = l[i]; l.RemoveAt(i); return v; }

    static string Text(XElement e, string path)
    {
        XElement? cur = e;
        foreach (var part in path.Split('/'))
        {
            cur = cur?.Element(part);
            if (cur == null) return "";
        }
        return cur.HasElements ? "" : cur.Value.Trim();
    }

    static string FirstNonEmpty(params string[] v) => v.FirstOrDefault(s => !string.IsNullOrWhiteSpace(s)) ?? "";

    static int Num(string s) =>
        double.TryParse(s, NumberStyles.Float, Inv, out var d) ? (int)Math.Round(d) : 0;

    static double Dbl(string s) => double.TryParse(s, NumberStyles.Float, Inv, out var d) ? d : 0;

    static string Mora(string feet, bool elevation)
    {
        var v = Num(feet);
        if (v <= 0) return "";
        return elevation ? "" : v.ToString("N0", Inv);
    }

    /// <summary>TLR lengths come in feet or metres depending on the user's
    /// settings; runways shorter than 4,600 are almost certainly metres.</summary>
    static string LenFmt(string v, out string unit)
    {
        var n = Num(v);
        unit = n > 0 && n < 4600 ? "m" : "ft";
        return n > 0 ? n.ToString("N0", Inv) : "-";
    }
}

public static class NotamFilter
{
    static readonly Regex Drop = new(
        @"\bSTANDS?\b|STEEL PLATE|\bCRANE|LASER|FIREWORK|SEARCHLIGHT|FERRIS|BALLOON|" +
        @"TRIGGER NOTAM|AIP SUP|AIP AMDT|BIRD|DRONE|\bUAS\b|UNMANNED|MARSHALL|" +
        @"GRASS|AD HR|CUSTOMS|IMMIGRATION|\bFUEL\b|DE-?ICING|" +
        @"OBST|\bMAST\b|WIND TURBINE|CHIMNEY|TETHERED|PYROTECHNIC|LIGHT SHOW|PARACHUT",
        RegexOptions.Compiled);

    static readonly Regex Rescue = new(
        @"\bRWY\b|\bILS\b|\bLOC\b|\bGP\b|\bAPCH\b|\bPAPI\b|\bTORA\b|\bLDA\b|\bTWY\b.*\bCLSD\b",
        RegexOptions.Compiled);

    public static bool Wanted(string text)
    {
        var t = text.ToUpperInvariant();
        return !Drop.IsMatch(t) || Rescue.IsMatch(t);
    }

    public static string Shorten(string text)
    {
        var t = Regex.Replace(text, @"\s+", " ").Trim();
        // strip ICAO "Q) ... A) ... B) ... C) ..." header, keep E) line
        var e = Regex.Match(t, @"\bE\)\s*(.+?)(?:\s+[FG]\)\s|$)");
        if (e.Success) t = e.Groups[1].Value;
        t = Regex.Replace(t, @"\.?\s*RWY CONDITION CODES REPORTED VIA.*$", ".");
        return t.Length > 220 ? t[..217].TrimEnd() + "..." : t;
    }
}
