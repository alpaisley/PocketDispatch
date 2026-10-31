using System.Text.Json;

namespace PocketDispatch.Core;

public sealed class AppSettings
{
    public string PilotId { get; set; } = "";
    public string Username { get; set; } = "";
    public string XPlaneFolder { get; set; } = "";
    /// <summary>Blank = &lt;X-Plane&gt;\Resources\plugins\AviTab\charts</summary>
    public string AviTabFolder { get; set; } = "";
    /// <summary>Blank = &lt;X-Plane&gt;\Output\FMS plans</summary>
    public string FmsFolder { get; set; } = "";
    /// <summary>SimBrief format codes, e.g. "xpe" (X-Plane 11/12).</summary>
    public List<string> FmsFormats { get; set; } = new() { "xpe" };
    public bool KeepOriginalPdf { get; set; }
    /// <summary>How many briefs to keep in AviTab: 0 = all, 1 = only the latest, 3 = last three.
    /// Missing (settings from before this option) = keep all.</summary>
    public int? KeepBriefs { get; set; }
    public int KeepBriefsResolved => KeepBriefs ?? 0;
    /// <summary>Blank = auto-detect from aircraft type.</summary>
    public string Aircraft { get; set; } = "";
    public bool A5 { get; set; }
    public bool AutoFetch { get; set; }
    public int WatchSeconds { get; set; } = 30;
    public bool StartMinimised { get; set; }

    static string? _folder;

    /// <summary>%APPDATA%\PocketDispatch (settings carried over from the old SimBriefBrief folder).</summary>
    public static string Folder
    {
        get
        {
            if (_folder != null) return _folder;
            var root = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var dir = Path.Combine(root, "PocketDispatch");
            var old = Path.Combine(root, "SimBriefBrief");
            try
            {
                if (!Directory.Exists(dir) && Directory.Exists(old))
                {
                    Directory.CreateDirectory(dir);
                    foreach (var f in Directory.GetFiles(old))
                        File.Copy(f, Path.Combine(dir, Path.GetFileName(f)), overwrite: false);
                }
            }
            catch { }
            return _folder = dir;
        }
    }

    static string FilePath => Path.Combine(Folder, "settings.json");
    static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public string ResolvedAviTab => !string.IsNullOrWhiteSpace(AviTabFolder) ? AviTabFolder
        : Path.Combine(XPlaneFolder, "Resources", "plugins", "AviTab", "charts");

    public string ResolvedFms => !string.IsNullOrWhiteSpace(FmsFolder) ? FmsFolder
        : Path.Combine(XPlaneFolder, "Output", "FMS plans");

    public bool IsConfigured => (!string.IsNullOrWhiteSpace(PilotId) || !string.IsNullOrWhiteSpace(Username))
                                && Directory.Exists(XPlaneFolder);

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath)) ?? new();
        }
        catch { }
        return new AppSettings { XPlaneFolder = GuessXPlane(), KeepBriefs = 1 };
    }

    public void Save()
    {
        Directory.CreateDirectory(Folder);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(this, Json));
    }

    public static string GuessXPlane()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        string[] c =
        {
            @"C:\X-Plane 12", @"D:\X-Plane 12", @"E:\X-Plane 12", @"F:\X-Plane 12",
            Path.Combine(home, "Desktop", "X-Plane 12"), Path.Combine(home, "X-Plane 12"),
            @"C:\X-Plane 11", @"D:\X-Plane 11", Path.Combine(home, "Desktop", "X-Plane 11"),
        };
        return c.FirstOrDefault(d => Directory.Exists(Path.Combine(d, "Resources"))) ?? "";
    }
}
