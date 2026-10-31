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

    static string Safe(string n) => Regex.Replace(n, @"[<>:""/\\|?*]+", "_");
}
