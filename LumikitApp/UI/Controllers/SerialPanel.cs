using System;
using System.IO.Ports;
using System.Threading.Tasks;
using Avalonia.Media;
using Avalonia.Threading;

namespace LumikitApp;

/// <summary>
/// Owns the SerialHandler lifecycle, brightness scaling, and async frame delivery.
/// UI control reads (port name, LED count, current) are supplied by the caller so
/// this class stays free of Avalonia control references.
/// </summary>
public class SerialPanel
{
    private SerialHandler? _serialHandler;

    /// <summary>
    /// Brightness scale derived from hardware current and LED count.
    /// Read by the playback coordinator when ticking the timeline.
    /// </summary>
    public double BrightnessScale { get; private set; } = 1;

    private readonly Action<string> _onError;
    private readonly Action<string> _onConnectionStatus;

    public SerialPanel(Action<string> onError, Action<string> onConnectionStatus)
    {
        _onError = onError;
        _onConnectionStatus = onConnectionStatus;
    }

    /// <summary>
    /// Opens a new serial connection.
    /// Closes any existing connection first.
    /// </summary>
    public void Connect(string port, int ledCount, int hardwareCurrent)
    {
        if (_serialHandler != null)
        {
            try
            {
                _serialHandler.ClosePort();
                _onConnectionStatus("Serial Disconnected");
            }
            catch
            {
                _onError("Failed to close previous port, please retry");
                return;
            }
        }

        BrightnessScale = hardwareCurrent / (ledCount * 0.06);

        try
        {
            _serialHandler = new SerialHandler(ledCount,
                new SerialPort(port, 460800, Parity.None, 8, StopBits.One));
            _onConnectionStatus($"Connected to {port}");
        }
        catch (Exception ex)
        {
            _onError("Failed to create port connection, please retry");
            Console.WriteLine(ex);
        }
    }

    /// <summary>
    /// Sends a color frame to hardware. Clears the handler and reports an error on failure.
    /// </summary>
    public async Task TrySendFrameAsync(Color[]? colors)
    {
        try
        {
            if (_serialHandler == null) return;
            await Task.Run(() => _serialHandler.SendFrame(colors));
        }
        catch
        {
            Dispatcher.UIThread.Post(() =>
            {
                _onError("Serial Communication Disconnected, please reconnect");
                _onConnectionStatus("Serial Disconnected");
            });
            _serialHandler = null;
        }
    }
}