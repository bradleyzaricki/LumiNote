using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;

namespace LumikitApp
{
    public partial class ProviderPickerWindow : Window
    {
        private readonly TaskCompletionSource _chosen = new();

        /// <summary>Provider names the user enabled, e.g. "Spotify", "LocalFiles".</summary>
        public List<string> SelectedProviders { get; } = new();

        public Task Choice => _chosen.Task;

        public ProviderPickerWindow()
        {
            InitializeComponent();
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }

        // Enable Continue only once at least one source is selected.
        private void Toggle_Changed(object? sender, RoutedEventArgs e)
        {
            var continueButton = this.FindControl<Button>("ContinueButton");
            if (continueButton == null) return;

            bool any = (this.FindControl<ToggleButton>("SpotifyToggle")?.IsChecked == true)
                    || (this.FindControl<ToggleButton>("LocalToggle")?.IsChecked == true);
            continueButton.IsEnabled = any;
        }

        private void Continue_Click(object? sender, RoutedEventArgs e)
        {
            SelectedProviders.Clear();
            if (this.FindControl<ToggleButton>("SpotifyToggle")?.IsChecked == true)
                SelectedProviders.Add("Spotify");
            if (this.FindControl<ToggleButton>("LocalToggle")?.IsChecked == true)
                SelectedProviders.Add("LocalFiles");

            if (SelectedProviders.Count == 0) return;

            _chosen.TrySetResult();
        }
    }
}
