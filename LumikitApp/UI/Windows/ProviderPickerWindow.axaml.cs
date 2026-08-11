using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace LumikitApp
{
    public partial class ProviderPickerWindow : Window
    {
        private readonly TaskCompletionSource _chosen = new();
        private readonly ProviderCredentialStore _credentials;

        // One tile per provider, built from ProviderMetadata.All.
        private readonly Dictionary<ProviderType, ToggleButton> _toggles = new();
        private readonly Dictionary<ProviderType, TextBlock> _statusLabels = new();
        private readonly Dictionary<ProviderType, Button> _setupButtons = new();

        public List<ProviderType> SelectedProviders { get; } = new();

        public Task Choice => _chosen.Task;

        // Designer-only.
        public ProviderPickerWindow() : this(new ProviderCredentialStore()) { }

        public ProviderPickerWindow(ProviderCredentialStore credentials)
        {
            _credentials = credentials;
            InitializeComponent();
            BuildProviderTiles();
            RefreshState();
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }

        /// <summary>
        /// Generates a tile per known provider. Providers needing user-supplied credentials get a
        /// setup button and a status line; everything else is inferred from ProviderMetadata.
        /// </summary>
        private void BuildProviderTiles()
        {
            var list = this.FindControl<ItemsControl>("ProviderList");
            if (list == null) return;

            var tiles = new List<Control>();

            foreach (var provider in ProviderMetadata.All)
            {
                var toggle = new ToggleButton
                {
                    FocusAdorner = null,
                    Content = BuildIcon(provider),
                    HorizontalAlignment = HorizontalAlignment.Center
                };
                toggle.IsCheckedChanged += (_, _) => RefreshState();
                _toggles[provider] = toggle;

                var name = new TextBlock
                {
                    Text = provider.DisplayName(),
                    Foreground = Brushes.White,
                    FontSize = 13,
                    HorizontalAlignment = HorizontalAlignment.Center
                };

                var column = new StackPanel
                {
                    Orientation = Orientation.Vertical,
                    Spacing = 6,
                    Width = 150,
                    Children = { toggle, name }
                };

                if (provider.IsExperimental())
                {
                    column.Children.Add(new Border
                    {
                        Background = new SolidColorBrush(Color.Parse("#7A5C00")),
                        CornerRadius = new Avalonia.CornerRadius(8),
                        Padding = new Avalonia.Thickness(8, 2, 8, 2),
                        HorizontalAlignment = HorizontalAlignment.Center,
                        Child = new TextBlock
                        {
                            Text = "EXPERIMENTAL",
                            Foreground = new SolidColorBrush(Color.Parse("#FFD86B")),
                            FontSize = 9,
                            FontWeight = FontWeight.Bold
                        }
                    });
                }

                if (provider.RequiresUserCredentials())
                {
                    var status = new TextBlock
                    {
                        FontSize = 10,
                        TextWrapping = TextWrapping.Wrap,
                        TextAlignment = TextAlignment.Center,
                        HorizontalAlignment = HorizontalAlignment.Center
                    };
                    _statusLabels[provider] = status;

                    var setup = new Button
                    {
                        FontSize = 11,
                        Padding = new Avalonia.Thickness(12, 3, 12, 3),
                        HorizontalAlignment = HorizontalAlignment.Center
                    };
                    var captured = provider;
                    setup.Click += async (_, _) => await OpenCredentialsAsync(captured);
                    _setupButtons[provider] = setup;

                    column.Children.Add(status);
                    column.Children.Add(setup);
                }

                tiles.Add(column);
            }

            list.ItemsSource = tiles;
        }

        private static Control BuildIcon(ProviderType provider)
        {
            try
            {
                var path = provider.IconPath();
                if (!string.IsNullOrEmpty(path))
                {
                    return new Image
                    {
                        Source = new Bitmap(AssetLoader.Open(new Uri(path))),
                        Width = 96,
                        Height = 96,
                        Stretch = Stretch.Uniform
                    };
                }
            }
            catch
            {
                // Fall through to the text tile below.
            }

            return new TextBlock
            {
                Text = provider.DisplayName(),
                Foreground = Brushes.White,
                Width = 96,
                Height = 96,
                TextAlignment = TextAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
        }

        private async Task OpenCredentialsAsync(ProviderType provider)
        {
            var window = new ProviderCredentialsWindow(provider, _credentials);
            await window.ShowDialog(this);
            await window.Completed;
            RefreshState();
        }

        /// <summary>
        /// Keeps setup status, the Continue gate and the hint line in sync. Continue stays
        /// disabled while any selected provider still needs credentials, so the app never
        /// reaches DI with a provider it can't construct.
        /// </summary>
        private void RefreshState()
        {
            foreach (var (provider, status) in _statusLabels)
            {
                bool configured = _credentials.IsConfigured(provider);
                status.Text = configured ? "Connected" : "Setup required";
                status.Foreground = configured
                    ? new SolidColorBrush(provider.BadgeColor())
                    : new SolidColorBrush(Color.Parse("#FFD86B"));

                if (_setupButtons.TryGetValue(provider, out var button))
                    button.Content = configured ? "Change key" : "Set up";
            }

            var selected = _toggles.Where(kv => kv.Value.IsChecked == true)
                                   .Select(kv => kv.Key)
                                   .ToList();

            var blocked = selected.Where(p => !_credentials.IsConfigured(p)).ToList();

            var hint = this.FindControl<TextBlock>("HintText");
            if (hint != null)
            {
                hint.IsVisible = blocked.Count > 0;
                if (blocked.Count > 0)
                {
                    var names = string.Join(", ", blocked.Select(p => p.DisplayName()));
                    hint.Text = $"{names} needs your own developer key before it can be used — press Set up.";
                }
            }

            var continueButton = this.FindControl<Button>("ContinueButton");
            if (continueButton != null)
                continueButton.IsEnabled = selected.Count > 0 && blocked.Count == 0;
        }

        private void Continue_Click(object? sender, RoutedEventArgs e)
        {
            SelectedProviders.Clear();

            // Preserve ProviderMetadata.All ordering so the first enabled source is deterministic.
            foreach (var provider in ProviderMetadata.All)
            {
                if (_toggles.TryGetValue(provider, out var toggle)
                    && toggle.IsChecked == true
                    && _credentials.IsConfigured(provider))
                {
                    SelectedProviders.Add(provider);
                }
            }

            if (SelectedProviders.Count == 0) return;

            _chosen.TrySetResult();
        }
    }
}
