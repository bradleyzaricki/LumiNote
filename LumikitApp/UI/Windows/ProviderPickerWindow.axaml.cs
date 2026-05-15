using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
namespace LumikitApp
{
    public partial class ProviderPickerWindow : Window
    {
        private readonly TaskCompletionSource _chosen = new();
        public bool UseSpotify { get; private set; }
        public Task Choice => _chosen.Task;
        public event EventHandler? ProviderChosen;
        public ProviderPickerWindow()
        {
            InitializeComponent();
            //When providerchoses is invoked, give the choice a result of complete to advance the lumanite startup
            ProviderChosen += (_, _) => _chosen.TrySetResult();
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
            
        }

        private void Spotify_Click(object? sender, RoutedEventArgs e)
        {
            UseSpotify = true;
            ProviderChosen?.Invoke(this, EventArgs.Empty);
        }

        private void Local_Click(object? sender, RoutedEventArgs e)
        {
            UseSpotify = false;
            ProviderChosen?.Invoke(this, EventArgs.Empty);
        }
    }
}