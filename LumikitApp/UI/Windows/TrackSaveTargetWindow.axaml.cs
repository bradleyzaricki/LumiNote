using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using LumikitApp.Models;

namespace LumikitApp;

/// <summary>
/// Lets the user pick which existing local lightmap (for the same song) to overwrite, or
/// save this edit as a brand new lightmap instead.
/// </summary>
public partial class TrackSaveTargetWindow : Window
{
    private ListBox _candidatesListBox = null!;
    private Button _overwriteButton = null!;

    public TrackSaveTargetWindow()
    {
        InitializeComponent();

        _candidatesListBox = this.FindControl<ListBox>("CandidatesListBox")!;
        _overwriteButton = this.FindControl<Button>("OverwriteButton")!;
    }

    public TrackSaveTargetWindow(List<TrackItemUI> candidates, string? preselectTrackGuid) : this()
    {
        _candidatesListBox.ItemsSource = candidates;

        var match = candidates.FirstOrDefault(t => t.TrackId == preselectTrackGuid);
        if (match != null)
        {
            _candidatesListBox.SelectedItem = match;
            _overwriteButton.IsEnabled = true;
        }
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    private void CandidatesListBox_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        _overwriteButton.IsEnabled = _candidatesListBox.SelectedItem != null;
    }

    private void Cancel_Click(object? sender, RoutedEventArgs e) => Close(null);

    // Guid.Empty is the "save as new" sentinel — a real lightmap's trackGUID is never empty.
    private void SaveAsNew_Click(object? sender, RoutedEventArgs e) => Close((Guid?)Guid.Empty);

    private void Overwrite_Click(object? sender, RoutedEventArgs e)
    {
        if (_candidatesListBox.SelectedItem is TrackItemUI item && Guid.TryParse(item.TrackId, out var guid))
            Close((Guid?)guid);
    }
}
