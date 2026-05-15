using System;
using Avalonia;
using Avalonia.Controls;
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

            IMusicProvider provider = useSpotify
                ? new SpotifyProvider(clientId, redirectUri)
                : new MusicFileProvider();
            IPlaybackHandler playbackHandler = useSpotify
                ? new SpotifyPlaybackHandler(provider)
                : new LocalFilesPlaybackHandler(provider);
            //Adds IMusicProvider and IPlaybackHandler constructor connections to any connected class
            services.AddSingleton(provider);
            services.AddSingleton(playbackHandler);
            services.AddTransient<LumikitWindow>();
            return services.BuildServiceProvider();
        }

        public override async void OnFrameworkInitializationCompleted()
        {
            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                //Create provider service picker window and await choice
                var picker = new ProviderPickerWindow();
                desktop.MainWindow = picker;
                picker.Show();

                await picker.Choice;

                //Build DI constructor implementations based on provider choice
                Services = BuildServices(picker.UseSpotify);
                var mainWindow = Services.GetRequiredService<LumikitWindow>();
                
                //await login
                var musicProvider = Services.GetRequiredService<IMusicProvider>();
                await musicProvider.InitializeClient();

                //run main window
                desktop.MainWindow = mainWindow;
                mainWindow.Show();
                mainWindow.InitializeWindow();
                mainWindow.Activate();
                picker.Close();
            }

            base.OnFrameworkInitializationCompleted();
        }

    }
}