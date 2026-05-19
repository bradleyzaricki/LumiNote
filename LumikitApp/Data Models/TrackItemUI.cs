using System;
using Avalonia.Media;

namespace LumikitApp.Models;

public class TrackItemUI
{
    public string TrackId { get; set; }
    public string TrackName { get; set; }
    public string Subtitle { get; set; }
    public string Provider { get; set; }
    public IBrush Color { get; set; }
}
