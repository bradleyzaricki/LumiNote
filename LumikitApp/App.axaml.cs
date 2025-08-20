using System;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Microsoft.Extensions.DependencyInjection;

namespace LumikitApp
{
    public partial class App : Application
    {
        public static IServiceProvider Services { get; private set; }

        public override void Initialize()
        {
            AvaloniaXamlLoader.Load(this);

            var serviceCollection = new ServiceCollection();
            serviceCollection.AddSingleton<SpotifyProvider>(provider =>
            {
                 string clientId = "7a3be16d49114bcb8317330636aa2647"; // replace this
                 string redirectUri = "http://localhost:5000/callback";
                // Window will be set later
                return new SpotifyProvider(null, clientId, redirectUri);
            });
            // Optionally, also register the interface if needed elsewhere
            serviceCollection.AddSingleton<ISpotifyProvider>(sp => sp.GetRequiredService<SpotifyProvider>());

            Services = serviceCollection.BuildServiceProvider();
        }

        public override async void OnFrameworkInitializationCompleted()
        {
            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                var spotifyProvider = Services.GetRequiredService<SpotifyProvider>();
                var mainWindow = new LumikitWindow();

                // Set the main window reference in the provider (add a method for this)
                spotifyProvider.SetMainWindow(mainWindow);

                // Await login before using the provider
                var spot = await spotifyProvider.InitializeClient();

                mainWindow.InitializeWindow(spotifyProvider);

                mainWindow.Show();
                desktop.MainWindow = mainWindow;
            }

            base.OnFrameworkInitializationCompleted();
        }
    }
}