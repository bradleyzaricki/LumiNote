using System;
using System.Threading.Tasks;
using Avalonia.Media;

namespace LumikitApp;

public interface ISerialPanel
{
    double BrightnessScale { get; }
    void Connect(string? port, int ledCount, int hardwareCurrent);
    Task TrySendFrameAsync(Color[]? colors);

    event Action<string> ErrorOccurred;
    event Action<string> ConnectionStatusChanged;

    /// <summary>
    /// Periodic frame-delivery stats (send cadence, dropped frames, write duration). Reports
    /// what the strip is actually receiving, which is what decides whether a timed effect like
    /// Strobe renders evenly — the computed waveform can be perfect and still look irregular if
    /// frames arrive unevenly or get dropped.
    /// </summary>
    event Action<string> DiagnosticsReported;
}
