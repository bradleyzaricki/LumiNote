using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;

namespace LumikitApp
{
    public partial class ProviderPickerWindow : Window
    {
        public bool UseSpotify { get; private set; }

        public ProviderPickerWindow()
        {
            InitializeComponent();
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }

        private void Spotify_Click(object? sender, RoutedEventArgs e)
        {
            UseSpotify = true;
            Close();
        }

        private void Local_Click(object? sender, RoutedEventArgs e)
        {
            UseSpotify = false;
            Close();
        }
    }
}