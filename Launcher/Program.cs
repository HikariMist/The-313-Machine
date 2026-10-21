using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Themes.Fluent;

namespace HikariForge.M313.Launcher
{
    internal static class Program
    {
        [STAThread]
        private static void Main(string[] args)
        {
            // One launcher at a time.
            using var mutex = new Mutex(true, "HikariForge.The313Machine.Launcher", out bool first);
            if (!first) return;
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
                desktop.MainWindow = new LauncherWindow();
            base.OnFrameworkInitializationCompleted();
        }
    }
}
