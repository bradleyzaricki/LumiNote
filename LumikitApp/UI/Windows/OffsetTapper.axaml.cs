using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Threading;
using ManagedBass;

namespace LumikitApp.UI.Windows;

public partial class OffsetTapper : Window
{
    private readonly TaskCompletionSource _completedTcs = new();
    public Task Completed => _completedTcs.Task;

    public int ComputedOffsetMs { get; private set; }

    private readonly Stopwatch _offsetStopwatch = new();
    private readonly CancellationTokenSource _cts = new();
    private readonly List<int> _tapOffsets = new();
    private long _lastTickMs;
    private string _tempWavPath;
    private bool _ownsBass;

    public OffsetTapper()
    {
        InitializeComponent();
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        if (Design.IsDesignMode) return;

        // Bass.Init returns false when the device is already initialized (e.g. the app's local
        // provider brought BASS up at startup). Only free it on close if WE initialized it, so
        // launching this on demand never tears down the running app's shared audio device.
        _ownsBass = Bass.Init();

        _tempWavPath = Path.Combine(Path.GetTempPath(), "lumikit_ping.wav");
        if (!File.Exists(_tempWavPath))
        {
            using var asset = AssetLoader.Open(new Uri("avares://LumikitApp/ping.wav"));
            using var fs = File.Create(_tempWavPath);
            asset.CopyTo(fs);
        }

        _offsetStopwatch.Restart();
        Task.Run(() => RunTickLoop(_cts.Token));
    }

    private async Task RunTickLoop(CancellationToken ct)
    {
        long nextTick = 1000;

        while (!ct.IsCancellationRequested)
        {
            long delay = nextTick - _offsetStopwatch.ElapsedMilliseconds;

            if (delay > 0)
            {
                try { await Task.Delay((int)delay, ct); }
                catch (OperationCanceledException) { break; }
            }

            if (ct.IsCancellationRequested) break;

            _lastTickMs = _offsetStopwatch.ElapsedMilliseconds;
            PlayTick();
            FlashAfterOffset(ct);
            nextTick += 1000;
        }
    }

    private void PlayTick()
    {
        int stream = Bass.CreateStream(_tempWavPath, Flags: BassFlags.Default);
        if (stream == 0) return;
        Bass.ChannelPlay(stream);
    }

    private void FlashAfterOffset(CancellationToken ct)
    {
        int delayMs = ComputedOffsetMs;
        Task.Run(async () =>
        {
            if (delayMs > 0)
            {
                try { await Task.Delay(delayMs, ct); }
                catch (OperationCanceledException) { return; }
            }
            FlashRectangles();
        }, ct);
    }

    private void FlashRectangles()
    {
        Dispatcher.UIThread.Post(async () =>
        {
            LeftFlash.Fill = Brushes.White;
            RightFlash.Fill = Brushes.White;
            await Task.Delay(80);
            LeftFlash.Fill = Brushes.MediumPurple;
            RightFlash.Fill = Brushes.MediumPurple;
        });
    }

    private void TapButton_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        long tapMs = _offsetStopwatch.ElapsedMilliseconds;
        int offsetFromTick = (int)(tapMs - _lastTickMs);

        // Only record taps within a reasonable latency window (< 500 ms)
        if (offsetFromTick is >= 0 and < 500)
        {
            _tapOffsets.Add(offsetFromTick);
            int avg = (int)_tapOffsets.Average();
            ComputedOffsetMs = avg;
            OffsetLabel.Text = $"Offset: {avg} ms  ({_tapOffsets.Count} taps)";
        }
    }

    // Done/Escape close the window; callers await ShowDialog and then read ComputedOffsetMs.
    private void DoneButton_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => Close();

    private void Window_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            e.Handled = true;
            Close();
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        _cts.Cancel();
        if (_ownsBass) Bass.Free();
        _completedTcs.TrySetResult();
        base.OnClosed(e);
    }
}