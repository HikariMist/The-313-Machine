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
        public bool Fullscreen { get; set; } = true;
        public int Width { get; set; } = 1920;
        public int Height { get; set; } = 1080;
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

        /// <summary>Unity standalone player command-line arguments.</summary>
        public string UnityArgs() => $"-screen-fullscreen {(Fullscreen ? 1 : 0)} -screen-width {Width} -screen-height {Height}";
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
