using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;

namespace LumikitApp;

public partial class NewTrackPopup : Window
{
    private TextBox _titleBox = null!;
    private TextBox _authorBox = null!;

    public string TitleText => _titleBox.Text ?? "";
    public string AuthorText => _authorBox.Text ?? "";

    public NewTrackPopup()
    {
        InitializeComponent();

        _titleBox = this.FindControl<TextBox>("TitleBox");
        _authorBox = this.FindControl<TextBox>("AuthorBox");
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    private void Ok_Click(object? sender, RoutedEventArgs e) => Close(true);
    private void Cancel_Click(object? sender, RoutedEventArgs e) => Close(false);
}