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
}
