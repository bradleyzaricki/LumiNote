using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;

namespace LumikitApp;

/// <summary>What the user chose when prompted about unsaved lightmap edits before a track switch.</summary>
public enum UnsavedChoice
{
    Save,
    DontSave,
    Cancel
}

/// <summary>Save / Don't Save / Cancel prompt shown before switching away from an edited lightmap.</summary>
public partial class UnsavedChangesPrompt : Window
{
    public UnsavedChangesPrompt()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    private void Save_Click(object? sender, RoutedEventArgs e) => Close(UnsavedChoice.Save);
    private void DontSave_Click(object? sender, RoutedEventArgs e) => Close(UnsavedChoice.DontSave);
    private void Cancel_Click(object? sender, RoutedEventArgs e) => Close(UnsavedChoice.Cancel);
}