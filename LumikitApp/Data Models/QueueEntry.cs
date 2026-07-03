using Avalonia.Media;

namespace LumikitApp.Models;

/// <summary>
/// One row of the playback queue. Display-only snapshot of a library track; the same
/// track can appear multiple times as distinct entries.
/// </summary>
public class QueueEntry
{
    public string TrackId { get; init; }
    public string TrackName { get; init; }
    public string Subtitle { get; init; }
    public IBrush Color { get; init; }

    public static QueueEntry From(TrackItemUI item) => new()
    {
        TrackId   = item.TrackId,
        TrackName = item.TrackName,
        Subtitle  = item.Subtitle,
        Color     = item.Color,
    };
}
