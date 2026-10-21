using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Themes.Fluent;
using Avalonia.Threading;

namespace HikariForge.M313.Launcher
{
    internal static class Program
    {
        /// <summary>The studio intro playing for this launcher run, or null when it is disabled or unavailable.</summary>
        internal static StudioIntroPlayer Intro;

        [STAThread]
        private static void Main(string[] args)
        {
            // One launcher at a time.
            using var mutex = new Mutex(true, "HikariForge.The313Machine.Launcher", out bool first);
            if (!first) return;
            // The cinematic plays once per launcher start, on its own thread, while the launcher UI starts up behind it.
            if (LauncherSettings.Load().PlayStudioIntro)
                Intro = StudioIntroPlayer.Start(Path.Combine(AppContext.BaseDirectory, StudioIntroPlayer.DefaultRelativePath), "The 3:13 Machine", LauncherLog.Write);
            AppBuilder.Configure<App>().UsePlatformDetect().WithInterFont().LogToTrace().StartWithClassicDesktopLifetime(args);
        }
    }

    public sealed class App : Application
    {
        public override void Initialize()
        {
            Styles.Add(new FluentTheme());
            RequestedThemeVariant = Avalonia.Styling.ThemeVariant.Dark;
        }

        public override void OnFrameworkInitializationCompleted()
        {
            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                var intro = Program.Intro;
                if (intro == null) desktop.MainWindow = new LauncherWindow(false);
                else
                {
                    desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
                    ShowAfterIntro(desktop, intro);
                }
            }
            base.OnFrameworkInitializationCompleted();
        }

        private static async void ShowAfterIntro(IClassicDesktopStyleApplicationLifetime desktop, StudioIntroPlayer intro)
        {
            // Never wait forever: the player has its own watchdogs, this is the last line of defence.
            var done = await Task.WhenAny(intro.Finished, Task.Delay(TimeSpan.FromSeconds(30)));
            if (done != intro.Finished) LauncherLog.Write("Studio intro did not report back in 30s; opening the launcher.");
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                var window = new LauncherWindow(true);
                window.Opened += (s, e) => { window.FadeInFromBlack(); intro.DismissBackdrop(); };
                window.Closed += (s, e) => desktop.Shutdown();
                desktop.MainWindow = window;
                window.Show();
                window.Activate();
            });
        }
    }
}
