using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;

namespace LumikitApp;

public partial class NewTrackPopup : Window
{
    private TextBox _lightmapBox = null!;
    private TextBox _titleBox = null!;
    private TextBox _artistBox = null!;

    public string LightmapText => _lightmapBox.Text ?? "";
    public string TitleText => _titleBox.Text ?? "";
    public string ArtistText => _artistBox.Text ?? "";

    public NewTrackPopup()
    {
        InitializeComponent();

        _lightmapBox = this.FindControl<TextBox>("LightmapBox");
        _titleBox = this.FindControl<TextBox>("TitleBox");
        _artistBox = this.FindControl<TextBox>("ArtistBox");
    }

    /// <summary>
    /// Prefill the inputs before showing (e.g. track name/author from Spotify metadata).
    /// Null leaves a field empty for the user to type (the local-files case).
    /// </summary>
    public void Prefill(string? lightmapName, string? trackName, string? artists)
    {
        if (!string.IsNullOrWhiteSpace(lightmapName)) _lightmapBox.Text = lightmapName;
        if (!string.IsNullOrWhiteSpace(trackName)) _titleBox.Text = trackName;
        if (!string.IsNullOrWhiteSpace(artists)) _artistBox.Text = artists;
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    private void Ok_Click(object? sender, RoutedEventArgs e) => Close(true);
    private void Cancel_Click(object? sender, RoutedEventArgs e) => Close(false);
}