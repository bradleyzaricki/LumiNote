using System;
using System.IO.Ports;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Media;
using Avalonia.Threading;

namespace LumikitApp;

/// <summary>
/// Owns the SerialHandler lifecycle, brightness scaling, and async frame delivery.
/// UI control reads (port name, LED count, current) are supplied by the caller so
/// this class stays free of Avalonia control references.
/// </summary>
public class SerialPanel : ISerialPanel
{
    private SerialHandler? _serialHandler;

    /// <summary>1 while a frame is on the wire. Guards against overlapping writes — see TrySendFrameAsync.</summary>
    private int _sending;

    public double BrightnessScale { get; private set; } = 1;

    public event Action<string>? ErrorOccurred;
    public event Action<string>? ConnectionStatusChanged;

    /// <summary>
    /// Opens a new serial connection, closing any existing one first.
    /// </summary>
    public void Connect(string? port, int ledCount, int hardwareCurrent)
    {
        if (_serialHandler != null)
        {
            try
            {
                _serialHandler.ClosePort();
                ConnectionStatusChanged?.Invoke("Serial Disconnected");
            }
            catch
            {
                ErrorOccurred?.Invoke("Failed to close previous port, please retry");
                return;
            }
        }

        BrightnessScale = hardwareCurrent / (ledCount * 0.06);

        try
        {
            _serialHandler = new SerialHandler(ledCount,
                new SerialPort(port, 460800, Parity.None, 8, StopBits.One));
            ConnectionStatusChanged?.Invoke($"Connected to {port}");
        }
        catch (Exception ex)
        {
            ErrorOccurred?.Invoke("Failed to create port connection, please retry");
            Console.WriteLine(ex);
        }
    }

    /// <summary>
    /// Sends a color frame to hardware. Clears the handler and reports an error on failure.
    /// </summary>
    public async Task TrySendFrameAsync(Color[]? colors)
    {
        if (_serialHandler == null) return;

        // This is a realtime stream, so a frame that can't go out now is worthless — the next one
        // supersedes it. Dropping instead of queueing matters twice over:
        //
        //  - SendFrame fills one shared _frameBuffer and then writes it. Two overlapping calls
        //    corrupt each other's buffer and interleave on the wire, so the device sees malformed
        //    frames rather than merely late ones.
        //  - Without a drop, a device that drains slower than we write backs up into the 64 KB
        //    OS write buffer. That surfaces as the strip lagging further and further behind the
        //    music, not as an error, and eventually as writes blocking outright.
        if (Interlocked.CompareExchange(ref _sending, 1, 0) != 0) return;

        try
        {
            await Task.Run(() => _serialHandler.SendFrame(colors));
        }
        catch
        {
            Dispatcher.UIThread.Post(() =>
            {
                ErrorOccurred?.Invoke("Serial Communication Disconnected, please reconnect");
                ConnectionStatusChanged?.Invoke("Serial Disconnected");
            });
            _serialHandler = null;
        }
        finally
        {
            Interlocked.Exchange(ref _sending, 0);
        }
    }
}
