using System;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Microsoft.Extensions.DependencyInjection;

namespace LumikitApp
{
    public partial class App : Application
    {
        public static IServiceProvider Services { get; private set; } = null!;

        public override void Initialize()
        {
            AvaloniaXamlLoader.Load(this);
        }

        private static IServiceProvider BuildServices(bool useSpotify)
        {
            var services = new ServiceCollection();

            string clientId = "7a3be16d49114bcb8317330636aa2647";
            string redirectUri = "http://127.0.0.1:5000/callback";

            services.AddSingleton<SpotifyProvider>(_ => new SpotifyProvider(null, clientId, redirectUri));
            services.AddSingleton<MusicFileProvider>(_ => new MusicFileProvider(null));

            services.AddSingleton<IMusicProvider>(sp =>
                useSpotify
                    ? sp.GetRequiredService<SpotifyProvider>()
                    : sp.GetRequiredService<MusicFileProvider>());

            return services.BuildServiceProvider();
        }

        public override async void OnFrameworkInitializationCompleted()
        {
            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                var mainWindow = new LumikitWindow();
                desktop.MainWindow = mainWindow;
                mainWindow.Show();



                var picker = new ProviderPickerWindow();
                await picker.ShowDialog(mainWindow);


                Services = BuildServices(picker.UseSpotify);

                var musicProvider = Services.GetRequiredService<IMusicProvider>();

                await musicProvider.InitializeClient();

                mainWindow.InitializeWindow(musicProvider);
                mainWindow.Activate();
            }

            base.OnFrameworkInitializationCompleted();
        }

    }
}