using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Platform;
using ManagedBass;

namespace LumikitApp.UI.Windows;

public partial class OffsetTapper : Window
{
    private readonly Stopwatch _offsetStopwatch = new();
    private readonly CancellationTokenSource _cts = new();

    private string _tempWavPath;
    private int _stream;

    public OffsetTapper()
    {
        InitializeComponent();
        
        Bass.Init();

        _tempWavPath = Path.Combine(Path.GetTempPath(), "lumikit_powerup.wav");

        if (File.Exists(_tempWavPath))
        {
            File.Delete(_tempWavPath);
        }

        using (var asset = AssetLoader.Open(new Uri("avares://LumikitApp/ping.wav")))
        using (var fs = File.Create(_tempWavPath))
        {
            asset.CopyTo(fs);
        }
        _stream = Bass.CreateStream(_tempWavPath, Flags: BassFlags.Default);

        _offsetStopwatch.Start();

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
                try
                {
                    await Task.Delay((int)delay, ct);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }

            if (ct.IsCancellationRequested)
                break;

            PlayTick();

            nextTick += 1000;
        }
    }

    private void PlayTick()
    {
        if (_stream == 0)
            return;

        Bass.ChannelStop(_stream);
        Bass.ChannelSetPosition(_stream, 0);
        Bass.ChannelPlay(_stream);
    }

    protected override void OnClosed(EventArgs e)
    {
        _cts.Cancel();

        if (_stream != 0)
        {
            Bass.StreamFree(_stream);
        }

        if (File.Exists(_tempWavPath))
        {
            File.Delete(_tempWavPath);
        }

        Bass.Free();

        base.OnClosed(e);
    }
}