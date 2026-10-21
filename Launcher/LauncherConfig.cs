using System.Text.Json;

namespace HikariForge.M313.Launcher
{
    /// <summary>launcher.json, shipped next to the launcher. Edit to point at a real update feed or store page.</summary>
    public sealed class LauncherConfig
    {
        public string GameExe { get; set; } = "Game\\The313Machine.exe";
        public string GameProcessName { get; set; } = "The313Machine";
        /// <summary>HTTPS URL of a JSON manifest {"version":"1.0.1","page":"https://..."}; empty = no feed configured.</summary>
        public string UpdateManifestUrl { get; set; } = "";
        public string StorePageUrl { get; set; } = "https://hikariforgestudios.itch.io/the-313-machine";
        public string SupportUrl { get; set; } = "https://hikariforgestudios.itch.io/the-313-machine";

        public static LauncherConfig Load(string dir)
        {
            var path = Path.Combine(dir, "launcher.json");
            try
            {
                if (File.Exists(path))
                    return JsonSerializer.Deserialize<LauncherConfig>(File.ReadAllText(path), new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new LauncherConfig();
            }
            catch (Exception) { /* fall back to defaults on a damaged config */ }
            return new LauncherConfig();
        }
    }

    /// <summary>Per-user launcher preferences in %APPDATA%.</summary>
    public sealed class LauncherSettings
    {
        public bool CloseAfterLaunch { get; set; } = true;
        public bool PlayStudioIntro { get; set; } = true;

        internal static string Folder => System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Hikari Forge Studios", "The 313 Machine");
        private static string Path => System.IO.Path.Combine(Folder, "launcher_settings.json");

        public static LauncherSettings Load()
        {
            try { if (File.Exists(Path)) return JsonSerializer.Deserialize<LauncherSettings>(File.ReadAllText(Path)) ?? new LauncherSettings(); }
            catch (Exception) { }
            return new LauncherSettings();
        }

        public void Save()
        {
            try
            {
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
                File.WriteAllText(Path, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
            }
            catch (Exception) { }
        }

    }

    /// <summary>
    /// The game's own display setting, edited in place so the launcher and the in-game Settings screen share one value.
    /// File: %USERPROFILE%\AppData\LocalLow\Hikari Forge Studios\The 313 Machine\Saves\settings.json (written by Unity).
    /// The game applies it on every start; the launcher passes no screen arguments.
    /// </summary>
    public static class GameDisplaySettings
    {
        public static readonly string[] Modes = { "Fullscreen", "Borderless Fullscreen", "Windowed Fullscreen", "Windowed" };
        public static readonly string[] WindowSizes = { "1280x720", "1600x900", "1920x1080", "2560x1440" };

        private static string FilePath => System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "AppData", "LocalLow", "Hikari Forge Studios", "The 313 Machine", "Saves", "settings.json");

        public static (int mode, int width, int height) Load()
        {
            try
            {
                if (File.Exists(FilePath))
                {
                    var o = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(FilePath))!.AsObject();
                    int mode = o.TryGetPropertyValue("display", out var m) && m != null ? (int)m.GetValue<double>() : 1;
                    int w = o.TryGetPropertyValue("winW", out var ww) && ww != null ? (int)ww.GetValue<double>() : 1600;
                    int h = o.TryGetPropertyValue("winH", out var hh) && hh != null ? (int)hh.GetValue<double>() : 900;
                    return (Math.Clamp(mode, 0, Modes.Length - 1), w, h);
                }
            }
            catch (Exception ex) { LauncherLog.Write("Could not read game display settings: " + ex.Message); }
            return (1, 1600, 900);
        }

        public static void Save(int mode, int width, int height)
        {
            try
            {
                var o = File.Exists(FilePath)
                    ? System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(FilePath))!.AsObject()
                    : new System.Text.Json.Nodes.JsonObject();
                o["display"] = mode; o["winW"] = width; o["winH"] = height;
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(FilePath)!);
                File.WriteAllText(FilePath, o.ToJsonString());
            }
            catch (Exception ex) { LauncherLog.Write("Could not save game display settings: " + ex.Message); }
        }
    }

    /// <summary>launcher.log next to the settings file; kept small, never throws.</summary>
    public static class LauncherLog
    {
        private static readonly object Gate = new object();

        public static void Write(string message)
        {
            try
            {
                lock (Gate)
                {
                    Directory.CreateDirectory(LauncherSettings.Folder);
                    var path = System.IO.Path.Combine(LauncherSettings.Folder, "launcher.log");
                    if (File.Exists(path) && new FileInfo(path).Length > 256 * 1024) File.Delete(path);
                    File.AppendAllText(path, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} {message}{Environment.NewLine}");
                }
            }
            catch (Exception) { }
        }
    }
}
