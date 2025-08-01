using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;

namespace LumikitApp;

public partial class App : Application
{
    public SpotifyProvider Spotify { get; private set; }

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override async void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // In App.xaml.cs
            var splash = new StartupWindow(); // small and centered
            desktop.MainWindow = splash;
            splash.Show();

            var lumikit = new LumikitWindow();
            Spotify = new SpotifyProvider(lumikit);
            var provider = await Spotify.InitializeClient();

            lumikit.InitializeWindow(Spotify, provider);
            lumikit.Show();
            desktop.MainWindow = lumikit;

            splash.Close();

        }

        base.OnFrameworkInitializationCompleted();
    }
}