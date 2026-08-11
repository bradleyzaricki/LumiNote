using System;
using System.Diagnostics;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace LumikitApp
{
    /// <summary>
    /// Collects the user's own API credentials for one music source. Driven entirely by
    /// <see cref="ProviderMetadata"/>, so a future provider that needs a key gets this window for
    /// free — declare its portal URL, redirect URI and instructions and nothing here changes.
    ///
    /// Follows the app's cross-window handoff idiom: await <see cref="Completed"/>, which reports
    /// whether the provider is configured after the window closed.
    /// </summary>
    public partial class ProviderCredentialsWindow : Window
    {
        private readonly TaskCompletionSource<bool> _completed = new();
        private readonly ProviderCredentialStore _store = null!;
        private readonly ProviderType _provider;

        /// <summary>True if the provider has complete credentials once this window closes.</summary>
        public Task<bool> Completed => _completed.Task;

        // Designer-only.
        public ProviderCredentialsWindow()
        {
            InitializeComponent();
        }

        public ProviderCredentialsWindow(ProviderType provider, ProviderCredentialStore store)
        {
            _provider = provider;
            _store = store;

            InitializeComponent();
            LoadProvider();
        }

        private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

        private void LoadProvider()
        {
            Title = $"Connect {_provider.DisplayName()}";
            HeadingText.Text = $"Connect {_provider.DisplayName()}";
            InstructionsText.Text = _provider.SetupInstructions();
            CredentialLabelText.Text = _provider.CredentialLabel();
            RedirectUriBox.Text = _provider.RedirectUri() ?? "";
            ExperimentalBadge.IsVisible = _provider.IsExperimental();
            OpenPortalButton.IsVisible = _provider.CredentialPortalUrl() != null;

            SaveButton.Background = new SolidColorBrush(_provider.BadgeColor());

            try
            {
                var icon = _provider.IconPath();
                if (!string.IsNullOrEmpty(icon))
                    ProviderIcon.Source = new Bitmap(AssetLoader.Open(new Uri(icon)));
            }
            catch
            {
                // A missing icon must not stop the user configuring the source.
            }

            var existing = _store.Get(_provider);
            if (existing != null)
            {
                ClientIdBox.Text = existing.ClientId;
                RemoveButton.IsVisible = true;
            }

            UpdateSaveState();
        }

        private void UpdateSaveState() =>
            SaveButton.IsEnabled = !string.IsNullOrWhiteSpace(ClientIdBox.Text);

        private void ClientId_TextChanged(object? sender, TextChangedEventArgs e)
        {
            ErrorText.IsVisible = false;
            UpdateSaveState();
        }

        private async void CopyRedirect_Click(object? sender, RoutedEventArgs e)
        {
            var clipboard = GetTopLevel(this)?.Clipboard;
            if (clipboard == null) return;

            await clipboard.SetTextAsync(RedirectUriBox.Text ?? "");
            CopyRedirectButton.Content = "Copied";
            await Task.Delay(1200);
            CopyRedirectButton.Content = "Copy";
        }

        private void OpenPortal_Click(object? sender, RoutedEventArgs e)
        {
            var url = _provider.CredentialPortalUrl();
            if (url == null) return;

            try
            {
                Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true });
            }
            catch (Exception ex)
            {
                ShowError($"Couldn't open the dashboard: {ex.Message}");
            }
        }

        private void Save_Click(object? sender, RoutedEventArgs e)
        {
            var clientId = (ClientIdBox.Text ?? "").Trim();
            if (string.IsNullOrWhiteSpace(clientId))
            {
                ShowError($"Enter your {_provider.CredentialLabel()}.");
                return;
            }

            // Catch the most common paste mistake: grabbing the client *secret* URL or the whole
            // dashboard line instead of the bare id.
            if (clientId.Contains(' ') || clientId.Contains('/'))
            {
                ShowError($"That doesn't look like a {_provider.CredentialLabel()} — paste just the id, with no spaces or URL.");
                return;
            }

            _store.Save(_provider, new ProviderCredentials { ClientId = clientId });
            _completed.TrySetResult(true);
            Close();
        }

        private void Remove_Click(object? sender, RoutedEventArgs e)
        {
            _store.Clear(_provider);
            _completed.TrySetResult(false);
            Close();
        }

        private void Cancel_Click(object? sender, RoutedEventArgs e)
        {
            _completed.TrySetResult(_store.IsConfigured(_provider));
            Close();
        }

        protected override void OnClosed(EventArgs e)
        {
            // Closing via the title bar still has to release anyone awaiting Completed.
            _completed.TrySetResult(_store?.IsConfigured(_provider) ?? false);
            base.OnClosed(e);
        }

        private void ShowError(string message)
        {
            ErrorText.Text = message;
            ErrorText.IsVisible = true;
        }
    }
}
