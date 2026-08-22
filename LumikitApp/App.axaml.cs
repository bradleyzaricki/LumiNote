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

        private static IServiceProvider BuildServices(
            IReadOnlyList<ProviderType> selectedProviders,
            ProviderCredentialStore credentials)
        {
            var services = new ServiceCollection();

            // Built up front (rather than resolved from the container) because the provider/
            // handler pairs below are constructed before the container exists, and they need
            // to log through the same instance the rest of the app uses.
            var log = new AppLog();

            // Build a concrete provider/handler pair for each source the user enabled. Providers
            // needing user-supplied credentials are skipped when unconfigured — the picker
            // already blocks that combination, this is the backstop.
            var pairs = new List<(IMusicProvider provider, IPlaybackHandler handler)>();
            foreach (var name in selectedProviders)
            {
                if (!credentials.IsConfigured(name)) continue;

                switch (name)
                {
                    case ProviderType.Spotify:
                        // No client id ships with LumiNote — this is the user's own, from their
                        // own Spotify developer app. See ProviderCredentialStore.
                        var sp = new SpotifyProvider(
                            credentials.Get(ProviderType.Spotify)!.ClientId,
                            ProviderType.Spotify.RedirectUri()!,
                            log);
                        pairs.Add((sp, new SpotifyPlaybackHandler(sp, log)));
                        break;
                    case ProviderType.LocalFiles:
                        var lf = new MusicFileProvider(log);
                        pairs.Add((lf, new LocalFilesPlaybackHandler(lf, log)));
                        break;
                }
            }

            // The router exposes both surfaces and switches between the enabled pairs at runtime.
            var router = new RoutingMusicSession(pairs);
            services.AddSingleton(router);
            // Same instance the picker used, so credential edits from the main window and from
            // startup share one view of the store.
            services.AddSingleton(credentials);
            services.AddSingleton<IMusicProvider>(router);
            services.AddSingleton<IPlaybackHandler>(router);
            services.AddSingleton<IAppLog>(log);
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
                // Loaded before the picker so it can show which sources are already set up, and
                // reused for the DI container so both see the same store.
                var credentials = new ProviderCredentialStore();

                //Create provider service picker window and await choice
                var picker = new ProviderPickerWindow(credentials);
                desktop.MainWindow = picker;
                picker.Show();

                await picker.Choice;

                //Build DI constructor implementations based on provider choices
                Services = BuildServices(picker.SelectedProviders, credentials);

                //await login (also inits BASS for local files)
                var musicProvider = Services.GetRequiredService<IMusicProvider>();
                try
                {
                    await musicProvider.InitializeClient();
                }
                catch (AggregateException ex)
                {
                    // A user-supplied key that's wrong, revoked, or missing its redirect URI fails
                    // here. Every other source still initialized, so carry on into the main window
                    // with the reason in the console rather than dying on a blank screen — the
                    // user can fix the key from the source-key button and relaunch.
                    var log = Services.GetRequiredService<IAppLog>();
                    foreach (var failure in ex.InnerExceptions)
                        log.Error($"Music source sign-in failed — {failure.Message}", "Startup");
                }

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
