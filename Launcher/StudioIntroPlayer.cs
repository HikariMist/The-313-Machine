using System.Runtime.InteropServices;
using System.Windows.Interop;
using Wpf = System.Windows;
using WpfControls = System.Windows.Controls;
using WpfInput = System.Windows.Input;
using WpfMedia = System.Windows.Media;
using WpfAnim = System.Windows.Media.Animation;
using WpfThreading = System.Windows.Threading;

namespace HikariForge.M313.Launcher
{
    public enum IntroResult { Completed, Skipped, Failed }

    /// <summary>
    /// Plays the Hikari Forge Studios startup cinematic in a borderless black window, then fades to black.
    /// Reusable for any Hikari Forge launcher: it only needs a video path and a log callback.
    ///
    /// Uses WPF's MediaElement (Windows Media Foundation, part of the .NET desktop runtime bundled with the
    /// launcher) on its own STA thread, so the caller's UI framework can start up while the video plays.
    /// Every failure path (missing file, codec missing, hang) completes <see cref="Finished"/> so the
    /// player is never left on a black screen.
    /// </summary>
    public sealed class StudioIntroPlayer
    {
        /// <summary>Location of the cinematic relative to the launcher executable.</summary>
        public const string DefaultRelativePath = "Intro\\Hikari_Forge_Studios_Startup_Intro.mp4";

        private static readonly TimeSpan OpenTimeout = TimeSpan.FromSeconds(6);
        private static readonly TimeSpan EndFade = TimeSpan.FromMilliseconds(700);
        private static readonly TimeSpan SkipFade = TimeSpan.FromMilliseconds(220);
        private static readonly TimeSpan BackdropFade = TimeSpan.FromMilliseconds(350);

        private readonly string _videoPath;
        private readonly string _title;
        private readonly double _volume;
        private readonly Action<string> _log;
        private readonly TaskCompletionSource<IntroResult> _finished = new TaskCompletionSource<IntroResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        private WpfThreading.Dispatcher _dispatcher;
        private Wpf.Window _window;
        private WpfControls.MediaElement _media;
        private WpfControls.TextBlock _hint;
        private WpfThreading.DispatcherTimer _watchdog;
        private int _finishing;
        private bool _backdropDismissed;

        /// <summary>Completes once the video has ended, been skipped or failed, and its resources are released.</summary>
        public Task<IntroResult> Finished => _finished.Task;

        private StudioIntroPlayer(string videoPath, string title, double volume, Action<string> log)
        {
            _videoPath = videoPath;
            _title = title;
            _volume = volume;
            _log = log ?? (_ => { });
        }

        /// <summary>Starts the intro on its own UI thread. Returns null (and logs why) when the video file is missing.</summary>
        public static StudioIntroPlayer Start(string videoPath, string windowTitle, Action<string> log, double volume = 1.0)
        {
            if (!File.Exists(videoPath))
            {
                log?.Invoke($"Studio intro skipped: video not found at {videoPath}");
                return null;
            }
            var player = new StudioIntroPlayer(videoPath, windowTitle, volume, log);
            var thread = new Thread(player.Run) { IsBackground = true, Name = "Studio intro" };
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            return player;
        }

        /// <summary>
        /// Fades out and closes the black backdrop that stays up after the video, then ends the intro thread.
        /// Call once the next window is on screen so the desktop never flashes in between.
        /// </summary>
        public void DismissBackdrop()
        {
            var d = _dispatcher;
            if (d == null || d.HasShutdownStarted) return;
            d.BeginInvoke(new Action(FadeOutBackdrop));
        }

        // ------------------------------------------------------------------ intro thread

        private void Run()
        {
            try
            {
                _dispatcher = WpfThreading.Dispatcher.CurrentDispatcher;
                _media = new WpfControls.MediaElement
                {
                    LoadedBehavior = WpfControls.MediaState.Manual,
                    UnloadedBehavior = WpfControls.MediaState.Manual,
                    Stretch = WpfMedia.Stretch.Uniform,   // keep 16:9, letterbox on other aspect ratios
                    Volume = _volume,
                    Opacity = 0
                };
                _hint = new WpfControls.TextBlock
                {
                    Text = "Press any key to skip",
                    FontSize = 14,
                    FontFamily = new WpfMedia.FontFamily("Bahnschrift, Segoe UI"),
                    Foreground = new WpfMedia.SolidColorBrush(WpfMedia.Color.FromArgb(120, 255, 255, 255)),
                    HorizontalAlignment = Wpf.HorizontalAlignment.Right,
                    VerticalAlignment = Wpf.VerticalAlignment.Bottom,
                    Margin = new Wpf.Thickness(0, 0, 32, 24),
                    Opacity = 0
                };
                var root = new WpfControls.Grid { Background = WpfMedia.Brushes.Black };
                root.Children.Add(_media);
                root.Children.Add(_hint);
                _window = new Wpf.Window
                {
                    Title = _title,
                    WindowStyle = Wpf.WindowStyle.None,
                    ResizeMode = Wpf.ResizeMode.NoResize,
                    WindowState = Wpf.WindowState.Maximized,   // borderless + maximized covers the taskbar on the current monitor
                    Background = WpfMedia.Brushes.Black,
                    Cursor = WpfInput.Cursors.None,
                    Content = root
                };

                _media.MediaOpened += (s, e) =>
                {
                    var length = _media.NaturalDuration.HasTimeSpan ? _media.NaturalDuration.TimeSpan : TimeSpan.FromSeconds(10);
                    _log($"Studio intro playing ({length.TotalSeconds:0.0}s, {_media.NaturalVideoWidth}x{_media.NaturalVideoHeight}, audio {(_media.HasAudio ? "on" : "none")}).");
                    RestartWatchdog(length + TimeSpan.FromSeconds(3), "playback did not finish in time");
                    // Ease the first frame in from black instead of popping onto the screen.
                    _media.BeginAnimation(Wpf.UIElement.OpacityProperty, new WpfAnim.DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(250)));
                    _hint.BeginAnimation(Wpf.UIElement.OpacityProperty, new WpfAnim.DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(600)) { BeginTime = TimeSpan.FromSeconds(1) });
                };
                _media.MediaEnded += (s, e) => Finish(IntroResult.Completed, "video finished");
                _media.MediaFailed += (s, e) => Finish(IntroResult.Failed, "playback error: " + e.ErrorException?.Message);
                _window.PreviewKeyDown += (s, e) =>
                {
                    // Alt/Win combos (Alt+Tab, Win key) switch away; they must not skip.
                    if (e.Key == WpfInput.Key.System || e.Key == WpfInput.Key.LWin || e.Key == WpfInput.Key.RWin) return;
                    e.Handled = true;
                    Finish(IntroResult.Skipped, "skipped with " + e.Key);
                };
                _window.PreviewMouseDown += (s, e) => { e.Handled = true; Finish(IntroResult.Skipped, "skipped with mouse"); };
                _window.Closed += (s, e) =>
                {
                    _backdropDismissed = true;
                    Finish(IntroResult.Skipped, "intro window closed");
                    _dispatcher.BeginInvokeShutdown(WpfThreading.DispatcherPriority.Background);
                };
                _window.Loaded += (s, e) =>
                {
                    _media.Source = new Uri(Path.GetFullPath(_videoPath), UriKind.Absolute);
                    _media.Play();
                    RestartWatchdog(OpenTimeout, "video did not open");
                };

                _window.Show();
                _window.Activate();
                WpfThreading.Dispatcher.Run();
            }
            catch (Exception ex)
            {
                _log("Studio intro could not start: " + ex);
                _backdropDismissed = true;
                try { _window?.Close(); } catch (Exception) { }
                Release(IntroResult.Failed);
            }
        }

        private void RestartWatchdog(TimeSpan after, string reason)
        {
            _watchdog?.Stop();
            _watchdog = new WpfThreading.DispatcherTimer { Interval = after };
            _watchdog.Tick += (s, e) => Finish(IntroResult.Failed, reason);
            _watchdog.Start();
        }

        /// <summary>Single exit point: whichever of end / skip / error / timeout happens first wins.</summary>
        private void Finish(IntroResult result, string reason)
        {
            if (Interlocked.Exchange(ref _finishing, 1) == 1) return;
            _watchdog?.Stop();
            _log($"Studio intro {result.ToString().ToLowerInvariant()}: {reason}.");
            if (result == IntroResult.Failed || _backdropDismissed)
            {
                Release(result);
                return;
            }
            // Fade picture and sound to black together, then free the video.
            var duration = result == IntroResult.Completed ? EndFade : SkipFade;
            _hint.BeginAnimation(Wpf.UIElement.OpacityProperty, new WpfAnim.DoubleAnimation(0, duration));
            _media.BeginAnimation(WpfControls.MediaElement.VolumeProperty, new WpfAnim.DoubleAnimation(0, duration));
            var fade = new WpfAnim.DoubleAnimation(0, duration);
            fade.Completed += (s, e) => Release(result);
            _media.BeginAnimation(Wpf.UIElement.OpacityProperty, fade);
        }

        private void Release(IntroResult result)
        {
            try
            {
                _media?.Stop();
                _media?.Close();
                if (_media != null) _media.Source = null;
            }
            catch (Exception ex) { _log("Studio intro release error: " + ex.Message); }
            _finished.TrySetResult(result);
        }

        private void FadeOutBackdrop()
        {
            if (_backdropDismissed || _window == null) { _dispatcher.BeginInvokeShutdown(WpfThreading.DispatcherPriority.Background); return; }
            _backdropDismissed = true;
            IntPtr hwnd = IntPtr.Zero;
            try
            {
                hwnd = new WindowInteropHelper(_window).Handle;
                SetWindowLong(hwnd, GWL_EXSTYLE, GetWindowLong(hwnd, GWL_EXSTYLE) | WS_EX_LAYERED);
            }
            catch (Exception) { hwnd = IntPtr.Zero; }
            if (hwnd == IntPtr.Zero) { _window.Close(); return; }
            var started = DateTime.UtcNow;
            var timer = new WpfThreading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(15) };
            timer.Tick += (s, e) =>
            {
                double k = Math.Min(1, (DateTime.UtcNow - started).TotalMilliseconds / BackdropFade.TotalMilliseconds);
                SetLayeredWindowAttributes(hwnd, 0, (byte)(255 * (1 - k)), LWA_ALPHA);
                if (k >= 1) { timer.Stop(); _window.Close(); }
            };
            timer.Start();
        }

        private const int GWL_EXSTYLE = -20;
        private const int WS_EX_LAYERED = 0x80000;
        private const uint LWA_ALPHA = 0x2;
        [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr hWnd, int nIndex);
        [DllImport("user32.dll")] private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);
        [DllImport("user32.dll")] private static extern bool SetLayeredWindowAttributes(IntPtr hwnd, uint crKey, byte bAlpha, uint dwFlags);
    }
}
