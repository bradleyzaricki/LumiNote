using Avalonia.Controls;
using Avalonia.Threading;
using SpotifyAPI.Web;
using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;

namespace LumikitApp;

public partial class LumikitWindow : Window
{
    private SpotifyClient _spotify;
    private SpotifyProvider _spotifyProvider;
    private Stopwatch playbackTime = new Stopwatch();
    private long playbackTimeElapsedBuffer = 0;
    private long msDelay = 0;
    public LumikitWindow()
    {
        InitializeComponent();

        // UpdateTrack();

    }
    public void InitializeWindow(SpotifyProvider provider, SpotifyClient client)
    {

        this.FindControl<Button>("PauseTrackButton").Click += async (_, _) =>
        {
            PausePlaybackTimer();
            await _spotifyProvider.PausePlayback();
        };
        this.FindControl<Button>("ResumeTrackButton").Click += async (_, _) =>
        {
            ResumePlaybackTimer();
            await _spotifyProvider.ResumePlayback();

        };
        this.FindControl<Button>("NextTrackButton").Click += async (_, _) =>
        {
            await _spotifyProvider.SkipTrack();
            UpdateTrackText(startNewLightShow: true);
            StartPlaybackTimer();
            FindLatency();
        };

        _spotifyProvider = provider;
        _spotify = client;
        return;
    }
    public async void StartPlaybackTimer()
    {

        _ = Task.Run(async () =>
        {
            while (true)
            {
                await Task.Delay(1);

                var ms = playbackTime.ElapsedMilliseconds + playbackTimeElapsedBuffer;

                Dispatcher.UIThread.Post(() =>
                {
                    StopwatchLabel.Text = (ms/1000).ToString();
                });
            }
        });
        
    }
    public async Task FindLatency()
    {
        await _spotifyProvider.PausePlayback();
        await _spotifyProvider.SeekToPlaybackTime(0);

        // Prime the stopwatch
        playbackTime.Reset();

        var sw = Stopwatch.StartNew();
        await _spotifyProvider.ResumePlayback();

        int progress = 0;
        for (int i = 0; i < 50; i++)
        {
            await Task.Delay(5);
            progress = _spotifyProvider.GetPlaybackProgressMs();
            if (progress > 0)
                break;
        }

        sw.Stop();

        long localElapsed = sw.ElapsedMilliseconds;
        long actualPlayback = progress;

        msDelay = localElapsed - actualPlayback;
        Debug.WriteLine($"Calculated latency: {msDelay}ms");

        ResumePlaybackTimer();
    }

    public async void PausePlaybackTimer()
    {
        playbackTime.Stop();

        playbackTimeElapsedBuffer = _spotifyProvider.GetPlaybackProgressMs() - playbackTime.ElapsedMilliseconds;
    }
    public async void ResumePlaybackTimer()
    {
        playbackTimeElapsedBuffer = _spotifyProvider.GetPlaybackProgressMs() - playbackTime.ElapsedMilliseconds;
        await Task.Delay((int)msDelay);

        playbackTime.Start();

    }
    public async void UpdateTrackText(bool startNewLightShow)
    {

        var track = await _spotifyProvider.GetCurrentlyPlayingTrack();
        var trackText = this.FindControl<TextBlock>("NowPlayingText");
        trackText.Text = track.Name;

        var albumImages = track.Album.Images;
        if (albumImages != null && albumImages.Count > 0)
        {
            var imageUrl = albumImages[1].Url;
            Console.WriteLine("Album cover URL: " + imageUrl);
            await SetAlbumCover(imageUrl);
        }
      
    }
    private async Task SetAlbumCover(string url)
    {
        try
        {
            using var client = new HttpClient();
            var data = await client.GetByteArrayAsync(url);

            using var stream = new MemoryStream(data);
            var bitmap = new Avalonia.Media.Imaging.Bitmap(stream); // ✅ disambiguate here

            var imageControl = this.FindControl<Avalonia.Controls.Image>("AlbumArt");
            imageControl.Source = bitmap;
        }
        catch (Exception ex)
        {
            Console.WriteLine("Failed to set album cover: " + ex.Message);
        }
    }

}
