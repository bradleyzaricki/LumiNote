using System;
using System.Collections.Generic;
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

        private static IServiceProvider BuildServices(IReadOnlyList<ProviderType> selectedProviders)
        {
            var services = new ServiceCollection();

            string clientId = "7a3be16d49114bcb8317330636aa2647";
            string redirectUri = "http://127.0.0.1:5000/callback";

            // Build a concrete provider/handler pair for each source the user enabled.
            var pairs = new List<(IMusicProvider provider, IPlaybackHandler handler)>();
            foreach (var name in selectedProviders)
            {
                switch (name)
                {
                    case ProviderType.Spotify:
                        var sp = new SpotifyProvider(clientId, redirectUri);
                        pairs.Add((sp, new SpotifyPlaybackHandler(sp)));
                        break;
                    case ProviderType.LocalFiles:
                        var lf = new MusicFileProvider();
                        pairs.Add((lf, new LocalFilesPlaybackHandler(lf)));
                        break;
                }
            }

            // The router exposes both surfaces and switches between the enabled pairs at runtime.
            var router = new RoutingMusicSession(pairs);
            services.AddSingleton(router);
            services.AddSingleton<IMusicProvider>(router);
            services.AddSingleton<IPlaybackHandler>(router);
            services.AddSingleton<IAppLog, AppLog>();
            services.AddSingleton<JsonDataHandler>();
            services.AddSingleton<GoogleAuthService>();
            services.AddSingleton<DatabaseAccess>();
            services.AddTransient<BlockEditorViewModel>();
            services.AddSingleton<ISerialPanel, SerialPanel>();
            services.AddTransient<LumikitWindow>();
            services.AddTransient<ProviderPickerWindow>();
            // OffsetTapper is no longer a startup singleton — the main window creates one on
            // demand (Calibrate Sync button) per provider, so it isn't registered here.
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

                //Build DI constructor implementations based on provider choices
                Services = BuildServices(picker.SelectedProviders);

                //await login (also inits BASS for local files)
                var musicProvider = Services.GetRequiredService<IMusicProvider>();
                await musicProvider.InitializeClient();

                //run main window — sync offset is now per-provider, calibrated on demand from the
                //Calibrate Sync button and persisted, so there is no startup offset step.
                var mainWindow = Services.GetRequiredService<LumikitWindow>();
                desktop.MainWindow = mainWindow;
                mainWindow.Show();
                mainWindow.InitializeWindow();
                mainWindow.Activate();
            }

            base.OnFrameworkInitializationCompleted();
        }

    }
}
