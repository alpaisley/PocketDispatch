using System.Text.RegularExpressions;

namespace PocketDispatch.Core;

/// <summary>Fetches the latest OFP from SimBrief and writes the brief + FMS plan.</summary>
public sealed class SimBriefService
{
    public static string ApiUrl { get; set; } = "https://www.simbrief.com/api/xml.fetcher.php";

    static readonly HttpClient Http = CreateClient();

    static HttpClient CreateClient()
    {
        var c = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
        c.DefaultRequestHeaders.UserAgent.ParseAdd("PocketDispatch/1.0 (personal X-Plane downloader)");
        return c;
    }

    public event Action<string>? Log;
    void Say(string s) => Log?.Invoke(s);

    public async Task<OfpData> FetchLatestAsync(AppSettings s, CancellationToken ct = default)
    {
        var q = !string.IsNullOrWhiteSpace(s.PilotId)
            ? "userid=" + Uri.EscapeDataString(s.PilotId.Trim())
            : "username=" + Uri.EscapeDataString(s.Username.Trim());
        using var resp = await Http.GetAsync(ApiUrl + "?" + q, ct);
        var body = await resp.Content.ReadAsStringAsync(ct);
        if (!resp.IsSuccessStatusCode)
        {
            var m = Regex.Match(body, "<status>(.*?)</status>", RegexOptions.Singleline);
            throw new InvalidOperationException("SimBrief: " + (m.Success ? m.Groups[1].Value.Trim() : resp.StatusCode.ToString()));
        }
        return OfpData.FromXml(body);
    }

    /// <summary>Writes the brief to AviTab and the FMS plan(s) to X-Plane. Returns the brief path.</summary>
    public async Task<string> ProcessAsync(OfpData o, AppSettings s, CancellationToken ct = default)
    {
        var profiles = Profiles.Load(AppSettings.Folder);
        var pid = !string.IsNullOrWhiteSpace(s.Aircraft) && profiles.ContainsKey(s.Aircraft) ? s.Aircraft : Profiles.Detect(o, profiles);
        var prof = profiles[pid];
        var stamp = o.Generated.ToString("ddMMMyy_HHmm", System.Globalization.CultureInfo.InvariantCulture);
        var baseName = Safe($"{o.Origin}{o.Destination}_{stamp}");
        Say($"Latest OFP: {o.Get("callsign")} {o.Origin}-{o.Destination} ({o.Get("type")}), generated {o.Generated:dd MMM HH:mm}Z");

        var avitab = s.ResolvedAviTab;
        Directory.CreateDirectory(avitab);
        var brief = Path.Combine(avitab, baseName + "_BRIEF.pdf");
        try
        {
            using (var fs = File.Create(brief))
                BriefRenderer.Render(o, prof, new BriefOptions { A5 = s.A5 }, fs);
            Say($"  Brief  -> {brief}   [{prof.Name}]");
        }
        catch (Exception ex)
        {
            Say($"  ! Couldn't build the brief: {ex.Message}");
            try { File.Delete(brief); } catch { }
            brief = "";
        }

        if ((s.KeepOriginalPdf || brief == "") && o.PdfUrl.Length > 0)
        {
            var full = Path.Combine(avitab, baseName + "_OFP.pdf");
            await File.WriteAllBytesAsync(full, await Http.GetByteArrayAsync(o.PdfUrl, ct), ct);
            Say($"  Full OFP -> {full}");
        }

        if (brief != "" && s.KeepBriefsResolved > 0)
            RemoveOldBriefs(avitab, s.KeepBriefsResolved);

        var fmsDir = s.ResolvedFms;
        Directory.CreateDirectory(fmsDir);
        foreach (var code in s.FmsFormats.Where(c => !string.IsNullOrWhiteSpace(c)))
        {
            if (!o.FmsUrls.TryGetValue(code.Trim(), out var url))
            {
                Say($"  ! No '{code}' flight plan in the SimBrief data");
                continue;
            }
            var ext = Path.GetExtension(new Uri(url).AbsolutePath);
            if (string.IsNullOrEmpty(ext)) ext = ".fms";
            var dst = Path.Combine(fmsDir, Safe($"{o.Origin}{o.Destination}{ext}"));
            await File.WriteAllBytesAsync(dst, await Http.GetByteArrayAsync(url, ct), ct);
            Say($"  Flight plan -> {dst}");
        }
        return brief;
    }

    // Only files Pocket Dispatch makes: EGPHEGKK_31Oct26_0623_BRIEF.pdf / _OFP.pdf
    static readonly Regex OurFile = new(@"^(?<base>[A-Z0-9]{3,4}[A-Z0-9]{3,4}_(?<stamp>\d{2}[A-Za-z]{3}\d{2}_\d{4}))_(BRIEF|OFP)\.pdf$",
                                        RegexOptions.IgnoreCase);

    /// <summary>Keep the newest <paramref name="keep"/> flights' briefs (and full OFPs); recycle the rest.</summary>
    void RemoveOldBriefs(string folder, int keep)
    {
        try
        {
            var flights = Directory.GetFiles(folder, "*.pdf", SearchOption.TopDirectoryOnly)
                .Select(f => (path: f, m: OurFile.Match(Path.GetFileName(f))))
                .Where(x => x.m.Success)
                .GroupBy(x => x.m.Groups["base"].Value, StringComparer.OrdinalIgnoreCase)
                .Select(g => (files: g.Select(x => x.path).ToList(), when: Stamp(g.First().m.Groups["stamp"].Value, g.First().path)))
                .OrderByDescending(x => x.when)
                .ToList();
            var old = flights.Skip(keep).SelectMany(x => x.files).ToList();
            if (old.Count == 0) return;
            int done = 0;
            foreach (var f in old)
                if (Recycle.Delete(f)) done++;
                else Say($"  ! Couldn't remove {Path.GetFileName(f)}");
            if (done > 0)
                Say($"  Tidied AviTab: {done} old file{(done == 1 ? "" : "s")} moved to the Recycle Bin");
        }
        catch (Exception ex) { Say("  ! Tidy-up skipped: " + ex.Message); }
    }

    static DateTime Stamp(string stamp, string path) =>
        DateTime.TryParseExact(stamp, "ddMMMyy_HHmm", System.Globalization.CultureInfo.InvariantCulture,
                               System.Globalization.DateTimeStyles.None, out var d) ? d : File.GetLastWriteTimeUtc(path);

    static string Safe(string n) => Regex.Replace(n, @"[<>:""/\\|?*]+", "_");
}
