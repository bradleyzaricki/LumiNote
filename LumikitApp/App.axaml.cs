using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using LumikitApp.UI.Windows;
using LumikitApp.ViewModels;
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
            services.AddSingleton<JsonDataHandler>();
            services.AddSingleton<DatabaseAccess>();
            services.AddTransient<BlockEditorViewModel>();
            services.AddSingleton<ISerialPanel, SerialPanel>();
            services.AddTransient<LumikitWindow>();
            services.AddTransient<ProviderPickerWindow>();
            services.AddSingleton<OffsetTapper>();
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

                //Show offset tapper before main window
                var offsetTapper = Services.GetRequiredService<OffsetTapper>();
                desktop.MainWindow = offsetTapper;

                offsetTapper.Show();
                await offsetTapper.Completed;

                //await login
                var musicProvider = Services.GetRequiredService<IMusicProvider>();
                await musicProvider.InitializeClient();

                //run main window with computed audio offset
                var mainWindow = Services.GetRequiredService<LumikitWindow>();
                mainWindow.AudioOffsetMs = offsetTapper.ComputedOffsetMs;
                desktop.MainWindow = mainWindow;
                mainWindow.Show();
                offsetTapper.Close();
                mainWindow.InitializeWindow();
                mainWindow.Activate();
            }

            base.OnFrameworkInitializationCompleted();
        }

    }
}
