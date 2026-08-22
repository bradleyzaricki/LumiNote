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
    public event Action<string>? DiagnosticsReported;

    // ── Frame delivery stats ──────────────────────────────────────────────────
    // Measures what the strip actually receives rather than what we intended to send. A timed
    // effect renders evenly only if frames leave evenly, so an uneven-looking Strobe is either
    // a bad waveform or bad delivery — these numbers say which.
    private readonly System.Diagnostics.Stopwatch _statsWatch = System.Diagnostics.Stopwatch.StartNew();
    private double _lastSendAtMs = -1;
    private int _sent, _dropped;
    private double _gapSumMs, _gapMinMs = double.MaxValue, _gapMaxMs;
    private double _writeSumMs, _writeMaxMs;
    private double _lastReportAtMs;

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

        // Clamp to at least 1 LED — a zero/negative count would divide by zero and leave
        // BrightnessScale as NaN/Infinity, corrupting every subsequent frame's intensity math.
        BrightnessScale = hardwareCurrent / (Math.Max(1, ledCount) * 0.06);

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
        if (Interlocked.CompareExchange(ref _sending, 1, 0) != 0)
        {
            Interlocked.Increment(ref _dropped);
            return;
        }

        try
        {
            double startedAt = _statsWatch.Elapsed.TotalMilliseconds;
            await Task.Run(() => _serialHandler.SendFrame(colors));
            RecordSend(startedAt, _statsWatch.Elapsed.TotalMilliseconds);
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

    /// <summary>
    /// Folds one completed send into the running stats and reports a summary every 2 s.
    /// Gap = time between consecutive frames reaching the wire; write = how long the blocking
    /// write itself took, which is the number that says whether the device is keeping up.
    /// </summary>
    private void RecordSend(double startedAtMs, double finishedAtMs)
    {
        _sent++;
        double writeMs = finishedAtMs - startedAtMs;
        _writeSumMs += writeMs;
        if (writeMs > _writeMaxMs) _writeMaxMs = writeMs;

        if (_lastSendAtMs >= 0)
        {
            double gap = finishedAtMs - _lastSendAtMs;
            _gapSumMs += gap;
            if (gap < _gapMinMs) _gapMinMs = gap;
            if (gap > _gapMaxMs) _gapMaxMs = gap;
        }
        _lastSendAtMs = finishedAtMs;

        if (finishedAtMs - _lastReportAtMs < 2000) return;
        _lastReportAtMs = finishedAtMs;

        int gaps = Math.Max(1, _sent - 1);
        DiagnosticsReported?.Invoke(
            $"frames {_sent} sent / {_dropped} dropped · " +
            $"gap avg {_gapSumMs / gaps:F0}ms (min {(_gapMinMs == double.MaxValue ? 0 : _gapMinMs):F0} / max {_gapMaxMs:F0}) · " +
            $"write avg {_writeSumMs / _sent:F1}ms (max {_writeMaxMs:F1})");

        _sent = 0; _dropped = 0;
        _gapSumMs = 0; _gapMinMs = double.MaxValue; _gapMaxMs = 0;
        _writeSumMs = 0; _writeMaxMs = 0;
    }
}
