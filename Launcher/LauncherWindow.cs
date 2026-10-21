using System.Diagnostics;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;

namespace HikariForge.M313.Launcher
{
    /// <summary>The launcher window: key art, PLAY, Check for Updates, Settings, Support / Website, Exit.</summary>
    public sealed class LauncherWindow : Window
    {
        private static readonly Color Void = Color.FromRgb(7, 8, 11);
        private static readonly Color Emerald = Color.FromRgb(52, 211, 145);
        private static readonly Color Violet = Color.FromRgb(155, 109, 255);
        private static readonly Color LcdRed = Color.FromRgb(255, 52, 52);
        private static readonly Color Bone = Color.FromRgb(221, 211, 196);
        private static readonly Color Dim = Color.FromRgb(139, 145, 156);
        private const double W = 960, H = 560;

        private readonly string _baseDir = AppContext.BaseDirectory;
        private readonly LauncherConfig _config;
        private readonly LauncherSettings _settings = LauncherSettings.Load();
        private readonly Random _rng = new Random();
        private readonly List<Line> _rain = new List<Line>();
        private readonly TextBlock _status, _lcd;
        private readonly Rectangle _topGlow, _bottomGlow;
        private readonly Rectangle _blackCover;
        private readonly bool _studioIntroShown;
        private double _t;

        /// <param name="studioIntroShown">True when the studio cinematic played this run, so the game can skip its own studio credit.</param>
        public LauncherWindow(bool studioIntroShown)
        {
            _studioIntroShown = studioIntroShown;
            _config = LauncherConfig.Load(_baseDir);
            Title = "The 3:13 Machine";
            Width = W; Height = H;
            CanResize = false;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            Background = new SolidColorBrush(Void);
            try { Icon = new WindowIcon(AssetLoader.Open(new Uri("avares://The313MachineLauncher/Assets/launcher.ico"))); } catch { }

            var canvas = new Canvas { Width = W, Height = H };
            try
            {
                var art = new Bitmap(AssetLoader.Open(new Uri("avares://The313MachineLauncher/Assets/launcher_bg.jpg")));
                var img = new Image { Source = art, Width = 700, Height = H, Stretch = Stretch.UniformToFill };
                Canvas.SetLeft(img, 260);
                canvas.Children.Add(img);
            }
            catch { }
            canvas.Children.Add(new Rectangle
            {
                Width = 720, Height = H,
                Fill = new LinearGradientBrush
                {
                    StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(1, 0, RelativeUnit.Relative),
                    GradientStops =
                    {
                        new GradientStop(Void, 0), new GradientStop(Void, 0.45),
                        new GradientStop(Color.FromArgb(150, 7, 8, 11), 0.75), new GradientStop(Color.FromArgb(0, 7, 8, 11), 1)
                    }
                }
            });
            for (int i = 0; i < 80; i++)
            {
                var l = new Line { Stroke = new SolidColorBrush(Color.FromArgb(45, 190, 205, 230)), StrokeThickness = 1 };
                Place(l, _rng.Next(380, 980), _rng.Next(0, (int)H));
                _rain.Add(l);
                canvas.Children.Add(l);
            }
            _topGlow = new Rectangle { Width = W, Height = 3, Fill = new SolidColorBrush(Emerald) };
            _bottomGlow = new Rectangle { Width = W, Height = 3, Fill = new SolidColorBrush(Violet) };
            Canvas.SetTop(_bottomGlow, H - 3);
            canvas.Children.Add(_topGlow);
            canvas.Children.Add(_bottomGlow);

            // Studio credit: a short emerald-to-violet rule (the emblem's colours) and a quiet "presents" line.
            Add(canvas, new Rectangle
            {
                Width = 28, Height = 2,
                Fill = new LinearGradientBrush
                {
                    StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(1, 0, RelativeUnit.Relative),
                    GradientStops = { new GradientStop(Emerald, 0), new GradientStop(Violet, 1) }
                }
            }, 40, 33);
            var credit = new TextBlock { FontSize = 11, LetterSpacing = 4, FontFamily = new FontFamily("Bahnschrift, Segoe UI, Inter, sans-serif") };
            credit.Inlines!.Add(new Avalonia.Controls.Documents.Run("HIKARI FORGE STUDIOS") { Foreground = new SolidColorBrush(Bone), FontWeight = FontWeight.SemiBold });
            credit.Inlines.Add(new Avalonia.Controls.Documents.Run("   PRESENTS") { Foreground = new SolidColorBrush(Dim) });
            Add(canvas, credit, 80, 27);
            Add(canvas, Text("THE", 30, Bone, FontWeight.Normal, 6), 38, 46);
            _lcd = Text("3:13", 96, LcdRed, FontWeight.Bold, 0, "Consolas, Courier New, monospace");
            Add(canvas, _lcd, 30, 72);
            Add(canvas, Text("MACHINE", 52, Bone, FontWeight.Normal, 4), 36, 178);
            Add(canvas, Text($"Version {InstalledVersion()}", 12, Dim), 800, 528);

            double y = 262;
            Add(canvas, MakeButton("PLAY", true, Play), 40, y); y += 64;
            Add(canvas, MakeButton("CHECK FOR UPDATES", false, CheckForUpdates), 40, y); y += 48;
            Add(canvas, MakeButton("SETTINGS", false, ShowSettings), 40, y); y += 48;
            Add(canvas, MakeButton("SUPPORT / WEBSITE", false, () => OpenUrl(_config.SupportUrl)), 40, y); y += 48;
            Add(canvas, MakeButton("EXIT", false, Close), 40, y);
            _status = Text(GameExists() ? "Ready." : "Game files not found next to the launcher. Reinstall, or check launcher.json.", 12, Dim);
            _status.Width = 560;
            _status.TextWrapping = TextWrapping.Wrap;
            Add(canvas, _status, 40, 520);
            // Covers everything while coming out of the studio intro; see FadeInFromBlack.
            _blackCover = new Rectangle { Width = W, Height = H, Fill = Brushes.Black, IsVisible = studioIntroShown, IsHitTestVisible = false };
            canvas.Children.Add(_blackCover);
            Content = canvas;

            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
            timer.Tick += (s, e) => Animate();
            timer.Start();
            Closed += (s, e) => timer.Stop();
        }

        /// <summary>Reveals the launcher from black after the studio intro.</summary>
        public void FadeInFromBlack()
        {
            var started = DateTime.UtcNow;
            var fade = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
            fade.Tick += (s, e) =>
            {
                double k = Math.Min(1, (DateTime.UtcNow - started).TotalMilliseconds / 500);
                _blackCover.Opacity = 1 - k;
                if (k >= 1) { fade.Stop(); _blackCover.IsVisible = false; }
            };
            fade.Start();
        }

        private static void Place(Line l, double x, double y)
        {
            l.StartPoint = new Point(x, y);
            l.EndPoint = new Point(x - 3, y + 18);
        }

        private void Animate()
        {
            _t += 0.05;
            foreach (var l in _rain)
            {
                double x = l.StartPoint.X - 4, y = l.StartPoint.Y + 26;
                if (y > H) { y = -20; x = _rng.Next(380, 980); }
                Place(l, x, y);
            }
            double glow = 0.25 + 0.2 * Math.Sin(_t * 1.3);
            _topGlow.Opacity = glow;
            _bottomGlow.Opacity = glow;
            _lcd.Opacity = (_t % 6) > 5.85 ? 0.4 : 1.0;
        }

        private static void Add(Canvas c, Control child, double x, double y)
        {
            Canvas.SetLeft(child, x);
            Canvas.SetTop(child, y);
            c.Children.Add(child);
        }

        private static TextBlock Text(string s, double size, Color color, FontWeight weight = FontWeight.Normal, double spacing = 0, string family = null)
        {
            var t = new TextBlock { Text = s, FontSize = size, Foreground = new SolidColorBrush(color), FontWeight = weight, LetterSpacing = spacing };
            t.FontFamily = new FontFamily(family ?? "Bahnschrift, Segoe UI, Inter, sans-serif");
            return t;
        }

        private Button MakeButton(string label, bool primary, Action onClick)
        {
            var b = new Button
            {
                Content = label,
                Width = 300,
                Height = primary ? 52 : 40,
                FontSize = primary ? 22 : 13,
                FontWeight = FontWeight.Bold,
                HorizontalContentAlignment = HorizontalAlignment.Center,
                VerticalContentAlignment = VerticalAlignment.Center,
                Background = new SolidColorBrush(primary ? Color.FromRgb(18, 53, 39) : Color.FromRgb(27, 31, 40)),
                Foreground = new SolidColorBrush(primary ? Color.FromRgb(201, 255, 230) : Bone),
                BorderBrush = new SolidColorBrush(primary ? Emerald : Color.FromRgb(42, 48, 60)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(4),
                Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand)
            };
            b.Click += (s, e) => onClick();
            return b;
        }

        private void SetStatus(string s) => _status.Text = s;

        // ------------------------------------------------------------------ actions

        private string GamePath()
        {
            foreach (var candidate in new[] { _config.GameExe, "The313Machine.exe", System.IO.Path.Combine("Game", "The313Machine.exe") })
            {
                var full = System.IO.Path.GetFullPath(System.IO.Path.Combine(_baseDir, candidate.Replace('\\', System.IO.Path.DirectorySeparatorChar)));
                if (File.Exists(full)) return full;
            }
            return System.IO.Path.GetFullPath(System.IO.Path.Combine(_baseDir, _config.GameExe.Replace('\\', System.IO.Path.DirectorySeparatorChar)));
        }

        private bool GameExists() => File.Exists(GamePath());

        private string InstalledVersion()
        {
            try
            {
                var vfile = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(GamePath())!, "version.txt");
                if (File.Exists(vfile)) return File.ReadAllText(vfile).Trim();
            }
            catch { }
            return Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.0.0";
        }

        private async void Play()
        {
            string exe = GamePath();
            if (!File.Exists(exe))
            {
                await Dialog.Show(this, "Game not found", $"The game could not be found at:\n{exe}\n\nPlease reinstall The 3:13 Machine.");
                return;
            }
            if (Process.GetProcessesByName(_config.GameProcessName).Length > 0)
            {
                SetStatus("The game is already running.");
                return;
            }
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = exe,
                    // No screen arguments: the game applies its saved display mode itself (GameDisplaySettings).
                    // The game shows its own studio credit on direct launches; skip it when the cinematic just played.
                    Arguments = _studioIntroShown ? "-hf-studio-intro-shown" : "",
                    WorkingDirectory = System.IO.Path.GetDirectoryName(exe)!,
                    UseShellExecute = false
                });
                SetStatus("Starting the night shift…");
                if (_settings.CloseAfterLaunch) Close();
            }
            catch (Exception ex)
            {
                await Dialog.Show(this, "Launch failed", "The game could not be started:\n" + ex.Message);
            }
        }

        private async void CheckForUpdates()
        {
            string url = _config.UpdateManifestUrl;
            if (string.IsNullOrWhiteSpace(url) || !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                if (await Dialog.Show(this, "Check for updates", "Automatic update checks are not set up for this copy.\n\nUpdates are published on the store page. Open it now?", "Open store page"))
                    OpenUrl(_config.StorePageUrl);
                return;
            }
            SetStatus("Checking for updates…");
            try
            {
                using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
                using var doc = JsonDocument.Parse(await http.GetStringAsync(url));
                string latest = doc.RootElement.GetProperty("version").GetString() ?? "0.0.0";
                string page = doc.RootElement.TryGetProperty("page", out var p) ? p.GetString() ?? _config.StorePageUrl : _config.StorePageUrl;
                if (Version.TryParse(latest, out var lv) && Version.TryParse(InstalledVersion(), out var iv) && lv > iv)
                {
                    SetStatus($"Version {latest} is available.");
                    if (await Dialog.Show(this, "Update available", $"Version {latest} is available (you have {InstalledVersion()}).\n\nOpen the download page?", "Open download page"))
                        OpenUrl(page);
                }
                else SetStatus("You have the latest version.");
            }
            catch (Exception)
            {
                SetStatus("Could not check for updates (offline?). The game still works without internet.");
            }
        }

        private async void ShowSettings()
        {
            if (await new SettingsWindow(_settings).ShowDialog<bool>(this))
            {
                _settings.Save();
                SetStatus("Settings saved.");
            }
        }

        private void OpenUrl(string url)
        {
            if (string.IsNullOrWhiteSpace(url) || !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) return;
            try { Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true }); }
            catch (Exception) { SetStatus("Could not open the browser. Visit: " + url); }
        }
    }

    /// <summary>Minimal modal message box (Avalonia has none built in).</summary>
    internal static class Dialog
    {
        public static Task<bool> Show(Window owner, string title, string message, string confirm = null)
        {
            var w = new Window
            {
                Title = title, Width = 440, SizeToContent = SizeToContent.Height, CanResize = false,
                WindowStartupLocation = WindowStartupLocation.CenterOwner, Background = new SolidColorBrush(Color.FromRgb(16, 19, 26))
            };
            var ok = new Button { Content = confirm ?? "OK", Margin = new Thickness(0, 0, 8, 0) };
            var cancel = new Button { Content = "Cancel", IsVisible = confirm != null };
            ok.Click += (s, e) => w.Close(true);
            cancel.Click += (s, e) => w.Close(false);
            w.Content = new StackPanel
            {
                Margin = new Thickness(20), Spacing = 16,
                Children =
                {
                    new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap },
                    new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Children = { ok, cancel } }
                }
            };
            return w.ShowDialog<bool>(owner);
        }
    }

    internal sealed class SettingsWindow : Window
    {
        public SettingsWindow(LauncherSettings s)
        {
            Title = "Launcher settings";
            Width = 400; SizeToContent = SizeToContent.Height; CanResize = false;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            Background = new SolidColorBrush(Color.FromRgb(16, 19, 26));
            var (mode, winW, winH) = GameDisplaySettings.Load();
            var display = new ComboBox { ItemsSource = GameDisplaySettings.Modes, SelectedIndex = mode, Width = 220 };
            var res = new ComboBox { ItemsSource = GameDisplaySettings.WindowSizes, Width = 220 };
            res.SelectedItem = $"{winW}x{winH}";
            if (res.SelectedIndex < 0) res.SelectedIndex = 1;
            res.IsEnabled = mode == 3;
            display.SelectionChanged += (o, e) => res.IsEnabled = display.SelectedIndex == 3;
            var close = new CheckBox { Content = "Close the launcher when the game starts", IsChecked = s.CloseAfterLaunch };
            var intro = new CheckBox { Content = "Play Studio Intro on Startup", IsChecked = s.PlayStudioIntro };
            var ok = new Button { Content = "Save", Margin = new Thickness(0, 0, 8, 0) };
            var cancel = new Button { Content = "Cancel" };
            ok.Click += (o, e) =>
            {
                s.CloseAfterLaunch = close.IsChecked == true;
                s.PlayStudioIntro = intro.IsChecked == true;
                var parts = ((string)res.SelectedItem!).Split('x');
                GameDisplaySettings.Save(display.SelectedIndex, int.Parse(parts[0]), int.Parse(parts[1]));
                Close(true);
            };
            cancel.Click += (o, e) => Close(false);
            Content = new StackPanel
            {
                Margin = new Thickness(20), Spacing = 14,
                Children =
                {
                    new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12, Children = { new TextBlock { Text = "Display mode", Width = 110, VerticalAlignment = VerticalAlignment.Center }, display } },
                    new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12, Children = { new TextBlock { Text = "Window size", Width = 110, VerticalAlignment = VerticalAlignment.Center }, res } },
                    close,
                    intro,
                    new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Children = { ok, cancel } }
                }
            };
        }
    }
}
