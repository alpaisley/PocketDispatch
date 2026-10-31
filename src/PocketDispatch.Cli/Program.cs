using PocketDispatch.Core;

// Pocket Dispatch command line (pdispatch)
//   pdispatch                  fetch latest OFP once (uses the same settings as the Windows app)
//   pdispatch --watch          keep checking and fetch each new OFP
//   pdispatch --setup          enter Pilot ID / X-Plane folder
//   pdispatch --xml file.xml [-o out.pdf] [--aircraft erj] [--a5]   render a saved SimBrief XML

var argv = args.ToList();
string? Arg(string name)
{
    var i = argv.IndexOf(name);
    return i >= 0 && i + 1 < argv.Count ? argv[i + 1] : null;
}
bool Flag(string name) => argv.Contains(name);

if (Arg("--api") is { } api) SimBriefService.ApiUrl = api;   // testing hook

if (Arg("--xml") is { } xmlPath)
{
    var o = OfpData.FromXml(File.ReadAllText(xmlPath));
    var profiles = Profiles.Load(AppSettings.Folder);
    var pid = Arg("--aircraft") ?? Profiles.Detect(o, profiles);
    var outPath = Arg("-o") ?? Path.ChangeExtension(xmlPath, null) + "_BRIEF.pdf";
    using (var fs = File.Create(outPath))
        BriefRenderer.Render(o, profiles[pid], new BriefOptions { A5 = Flag("--a5") }, fs);
    Console.WriteLine($"[{profiles[pid].Name}] -> {outPath}");
    return 0;
}

var s = AppSettings.Load();
if (Flag("--setup") || !s.IsConfigured)
{
    Console.WriteLine("=== Pocket Dispatch set-up ===");
    Console.Write($"SimBrief Pilot ID (blank to use username) [{s.PilotId}]: ");
    var pid = Console.ReadLine()?.Trim();
    if (!string.IsNullOrEmpty(pid)) s.PilotId = pid;
    if (string.IsNullOrEmpty(s.PilotId))
    {
        Console.Write($"SimBrief username [{s.Username}]: ");
        var u = Console.ReadLine()?.Trim();
        if (!string.IsNullOrEmpty(u)) s.Username = u;
    }
    while (true)
    {
        Console.Write($"X-Plane folder [{s.XPlaneFolder}]: ");
        var xp = Console.ReadLine()?.Trim().Trim('"');
        if (!string.IsNullOrEmpty(xp)) s.XPlaneFolder = xp;
        if (Directory.Exists(Path.Combine(s.XPlaneFolder, "Resources"))) break;
        Console.WriteLine("  That doesn't look like an X-Plane folder (no Resources folder).");
    }
    s.Save();
    Console.WriteLine($"Saved to {AppSettings.Folder}");
}

var svc = new SimBriefService();
svc.Log += Console.WriteLine;

if (!Flag("--watch"))
{
    try
    {
        await svc.ProcessAsync(await svc.FetchLatestAsync(s), s);
        return 0;
    }
    catch (Exception ex)
    {
        Console.WriteLine("! " + ex.Message);
        return 1;
    }
}

Console.WriteLine($"Watching SimBrief every {s.WatchSeconds}s. Ctrl+C to stop.");
string? last = null;
while (true)
{
    try
    {
        var o = await svc.FetchLatestAsync(s);
        if (last != null && o.RequestId != last) await svc.ProcessAsync(o, s);
        last = o.RequestId;
    }
    catch (Exception ex) { Console.WriteLine("! " + ex.Message); }
    await Task.Delay(TimeSpan.FromSeconds(Math.Max(15, s.WatchSeconds)));
}
