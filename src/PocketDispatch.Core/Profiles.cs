using System.Text.Json;
using System.Text.RegularExpressions;

namespace PocketDispatch.Core;

/// <summary>
/// One aircraft layout. Each section is a list of [field, entry-template].
///   {key}         value from OfpData.Ctx  (see README for the list)
///   {key|text}    use "text" if the value is missing
///   ____          yellow write-in box
///   field "!..."  printed large (V-speeds)
/// </summary>
public sealed class AircraftProfile
{
    public string Name { get; set; } = "";
    public string Short { get; set; } = "";
    public List<string> Types { get; set; } = new();
    public List<string[]> Init { get; set; } = new();
    public List<string[]> Fuel { get; set; } = new();
    public List<string[]> Takeoff { get; set; } = new();
    public List<string[]> Arrival { get; set; } = new();
}

public static class Profiles
{
    static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    /// <summary>Indented JSON, but each [field, entry] pair kept on one line.</summary>
    static string Serialize(object o)
    {
        var json = JsonSerializer.Serialize(o, Json);
        json = Regex.Replace(json, @"\[\s*(""(?:[^""\\]|\\.)*"")\s*,\s*(""(?:[^""\\]|\\.)*"")\s*\]", "[$1, $2]");
        return Regex.Replace(json, @"\[\s*((?:""[A-Z0-9]{3,4}"",?\s*)+)\]", m => "[" + Regex.Replace(m.Groups[1].Value.Trim(), @",\s*", ", ") + "]");
    }

    static List<string[]> L(params string[] kv)
    {
        var l = new List<string[]>();
        for (int i = 0; i < kv.Length; i += 2) l.Add(new[] { kv[i], kv[i + 1] });
        return l;
    }

    static readonly string[] CommonInit =
    {
        "From / To", "{dep} / {arr}",
        "Alternate", "{altn}",
        "Flight number", "{callsign}",
        "Cruise altitude", "{cruise_fl}",
    };

    public static readonly List<string[]> Route = L(
        "Departure runway", "{to_rwy}",
        "SID", "{sid}",
        "En-route", "{enroute}",
        "STAR", "{star}",
        "Arrival runway", "{ld_rwy}");

    public static readonly List<string[]> Clearance = L(
        "ATIS", "____",
        "Cleared SID / alt", "____",
        "Squawk", "____");

    public static Dictionary<string, AircraftProfile> Defaults() => new(StringComparer.OrdinalIgnoreCase)
    {
        ["erj"] = new()
        {
            Name = "X-Crafts ERJ Family", Short = "E145",
            Types = new() { "E135", "E35L", "E140", "E145", "E45X" },
            Init = L(CommonInit.Concat(new[] { "Cruise speed", "{cruise_speed}" }).ToArray()),
            Fuel = L("ZFW", "{zfw_f} {wu}",
                     "Block fuel", "{block_f} {wu}",
                     "Reserve (FINRES+ALTN)", "{reserve_f} {wu}",
                     "TOW / LAW", "{tow_f} / {law_f} {wu}"),
            Takeoff = L("Runway", "{to_rwy}  ·  {to_len} {len_unit}",
                        "Flaps", "{to_flap}",
                        "Thrust", "{to_thrust}",
                        "!V1 / VR / V2", "{to_v1} / {to_vr} / {to_v2} kt",
                        "Max temp (MT)", "{to_maxtemp}°C",
                        "VFS", "{to_vother|____}"),
            Arrival = L("ILS frequency", "{ld_ils}",
                        "Flaps", "{ld_flap}",
                        "!VREF", "{ld_vref} kt",
                        "Landing dist. dry / wet", "{ld_dry} / {ld_wet} {len_unit}",
                        "Approach", "____"),
        },
        ["toliss"] = new()
        {
            Name = "ToLiSS Airbus", Short = "",
            Types = new() { "A318", "A319", "A320", "A321", "A19N", "A20N", "A21N", "A339", "A332", "A333", "A343", "A346" },
            Init = L(CommonInit.Concat(new[] { "Cost index", "{cost_index}", "TROPO", "{tropo}" }).ToArray()),
            Fuel = L("ZFW / ZFWCG", "{zfw_f} {wu}  /  ____",
                     "Block fuel", "{block_f} {wu}",
                     "RTE RSV", "{cont_f} {wu}",
                     "ALTN / FINAL", "{altn_f} / {finres_f} {wu}",
                     "TRIP WIND", "{trip_wind}"),
            Takeoff = L("Runway", "{to_rwy}  ·  {to_len} {len_unit}",
                        "CONF / FLEX", "{to_flap}  /  {to_flex|-}",
                        "!V1 / VR / V2", "{to_v1} / {to_vr} / {to_v2} kt",
                        "THS", "____",
                        "THR RED / ACC", "____"),
            Arrival = L("ILS frequency", "{ld_ils}",
                        "LDG CONF", "{ld_flap}",
                        "!VAPP", "____",
                        "Landing dist. dry / wet", "{ld_dry} / {ld_wet} {len_unit}",
                        "Approach", "____"),
        },
        ["q400"] = new()
        {
            Name = "FlyJSim Q4XP", Short = "Q400",
            Types = new() { "DH8D" },
            Init = L(CommonInit),
            Fuel = L("ZFW", "{zfw_f} {wu}",
                     "Block fuel", "{block_f} {wu}",
                     "Reserve (FINRES+ALTN)", "{reserve_f} {wu}",
                     "TOW / LAW", "{tow_f} / {law_f} {wu}"),
            Takeoff = L("Runway", "{to_rwy}  ·  {to_len} {len_unit}",
                        "Flaps / power", "{to_flap}  /  {to_thrust}",
                        "!V1 / VR / V2", "{to_v1} / {to_vr} / {to_v2} kt",
                        "VFRI / VCLIMB", "____"),
            Arrival = L("ILS frequency", "{ld_ils}",
                        "Flaps", "{ld_flap}",
                        "!VREF", "{ld_vref} kt",
                        "Landing dist. dry / wet", "{ld_dry} / {ld_wet} {len_unit}",
                        "Approach", "____"),
        },
        ["generic"] = new()
        {
            Name = "Generic", Short = "",
            Types = new(),
            Init = L(CommonInit.Concat(new[] { "Cruise speed / CI", "{cruise_speed}" }).ToArray()),
            Fuel = L("ZFW", "{zfw_f} {wu}",
                     "Block fuel", "{block_f} {wu}",
                     "Reserve (FINRES+ALTN)", "{reserve_f} {wu}"),
            Takeoff = L("Runway", "{to_rwy}  ·  {to_len} {len_unit}",
                        "Flaps / thrust", "{to_flap}  /  {to_thrust}",
                        "!V1 / VR / V2", "{to_v1} / {to_vr} / {to_v2} kt"),
            Arrival = L("ILS frequency", "{ld_ils}",
                        "Flaps", "{ld_flap}",
                        "!VREF", "{ld_vref} kt",
                        "Approach", "____"),
        },
    };

    /// <summary>Load profiles.json from <paramref name="folder"/>, writing the defaults the first time.</summary>
    public static Dictionary<string, AircraftProfile> Load(string folder)
    {
        var path = Path.Combine(folder, "profiles.json");
        try
        {
            if (File.Exists(path))
            {
                var d = JsonSerializer.Deserialize<Dictionary<string, AircraftProfile>>(File.ReadAllText(path), Json);
                if (d != null && d.Count > 0) return new(d, StringComparer.OrdinalIgnoreCase);
            }
            Directory.CreateDirectory(folder);
            File.WriteAllText(path, Serialize(Defaults()));
        }
        catch { /* fall back to built-in defaults */ }
        return Defaults();
    }

    public static string Detect(OfpData o, Dictionary<string, AircraftProfile> profiles)
    {
        var t = (o.Get("type") + " " + o.Get("type_name")).ToUpperInvariant();
        foreach (var (id, p) in profiles)
            if (p.Types.Any(x => t.Contains(x.ToUpperInvariant()))) return id;
        return profiles.ContainsKey("generic") ? "generic" : profiles.Keys.First();
    }

    static readonly Regex Key = new(@"\{(\w+)(?:\|([^}]*))?\}");

    public static string Fill(string template, OfpData o) =>
        Key.Replace(template, m =>
        {
            var v = o.Get(m.Groups[1].Value);
            return v == "-" && m.Groups[2].Success ? m.Groups[2].Value : v;
        });
}
