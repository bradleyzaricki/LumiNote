using Avalonia.Controls;
using Avalonia.Interactivity;

namespace LumikitApp;

/// <summary>Delete / Cancel confirmation shown before permanently removing a lightmap.</summary>
public partial class ConfirmDeleteDialog : Window
{
    public ConfirmDeleteDialog()
    {
        InitializeComponent();
    }

    public ConfirmDeleteDialog(string itemName) : this()
    {
        MessageText.Text = $"\"{itemName}\" will be permanently deleted. This can't be undone.";
    }

    private void Delete_Click(object? sender, RoutedEventArgs e) => Close(true);
    private void Cancel_Click(object? sender, RoutedEventArgs e) => Close(false);
}
