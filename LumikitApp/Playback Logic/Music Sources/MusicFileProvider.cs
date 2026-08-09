using System;
using System.Diagnostics;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Media;
using ManagedBass;

namespace LumikitApp
{
    /// <summary>
    /// This class will handle all spotify endpoints
    /// </summary>
    public class MusicFileProvider : IMusicProvider
    {
        public string GetCurrentlyPlayingTrackIdAsync()
        {
            return null;
        }

        public Color ProviderColor {get; set;}
        public bool IsProviderLocal { get; set; } = true;
        public ProviderType providerName => ProviderType.LocalFiles;
        public TrackPOCO currentTrack { get; set; }
        private int _stream;
        private bool _init;

        public string currentlyPlayingPath {get; set;}
        /// <summary>
        /// Spotify API wrapper for Lumikit procedures
        /// </summary>
        /// <param name="clientId"></param>
        /// <param name="redirectUri"></param>
        public MusicFileProvider()
        {
            ProviderColor = Brushes.BlueViolet.Color;
        }
        

        public Task<bool> IsPlayingAsync()
        {
            if (_stream == 0) return Task.FromResult(false);
            return Task.FromResult(Bass.ChannelIsActive(_stream) == PlaybackState.Playing);
        }

        public IPlaybackHandler GetPlaybackHandler()
        {
            return new LocalFilesPlaybackHandler(this);
        }
        /// <summary>
        /// Resume current playback as fast as possible to avoid any latency
        /// </summary>
        public Task ResumePlaybackAsync()
        {
            if (_stream != 0)
                Bass.ChannelPlay(_stream, false);
            return Task.CompletedTask;
        }

        public Task PausePlaybackAsync()
        {
            if (_stream != 0)
                Bass.ChannelPause(_stream);
            return Task.CompletedTask;
        }

        /// <summary>
        /// Pause current playback as soon as possible to avoid latency
        /// </summary>
        public async Task<TrackPOCO> GetCurrentlyPlayingTrackAsync()
        {
            return currentTrack;
        }

        /// <summary>
        /// GET integer value of playback progress in ms 
        /// </summary>
        /// <returns></returns>
        public Task<int> GetPlaybackProgressMsAsync()
        {
            if (_stream == 0) return Task.FromResult(0);

            long pos = Bass.ChannelGetPosition(_stream);
            double sec = Bass.ChannelBytes2Seconds(_stream, pos);
            return Task.FromResult((int)(sec * 1000));
        }

        public Task SeekToPlaybackTime(int ms)
        {
            if (_stream == 0) return Task.CompletedTask;

            long bytes = Bass.ChannelSeconds2Bytes(_stream, ms / 1000.0);
            Bass.ChannelSetPosition(_stream, bytes);
            return Task.CompletedTask;
        }

        /// <summary>
        /// Total duration of the loaded stream in ms (0 if nothing is loaded).
        /// </summary>
        public Task<int> GetTrackDurationMsAsync()
        {
            if (_stream == 0) return Task.FromResult(0);

            long len = Bass.ChannelGetLength(_stream);
            double sec = Bass.ChannelBytes2Seconds(_stream, len);
            return Task.FromResult(sec > 0 ? (int)(sec * 1000) : 0);
        }

        /// <summary>
        /// Skip the current track and 
        /// </summary>
        public async Task PlayTrackAsync()
        {
            await InitializeClient();

            if (_stream != 0)
            {
                Bass.ChannelStop(_stream);
                Bass.StreamFree(_stream);
                _stream = 0;
            }

            _stream = Bass.CreateStream(currentlyPlayingPath, Flags: BassFlags.Default);
            Bass.ChannelSetPosition(_stream, 0);
            Bass.ChannelPlay(_stream);
        }
        
        public Task InitializeClient()
        {
            if (_init) return Task.CompletedTask;

            // Tighter update cadence and buffer than the defaults (100ms/500ms) so
            // ChannelGetPosition tracks the audible position closely — the playback
            // handler steers its clock off that read.
            Bass.Configure(Configuration.UpdatePeriod, 10);
            Bass.Configure(Configuration.PlaybackBufferLength, 200);

            _init = Bass.Init(Flags: DeviceInitFlags.Latency);
            if (_init)
                Debug.WriteLine($"BASS initialized, output latency ~{Bass.Info.Latency}ms");
            return Task.CompletedTask;
        }
        
    }
}
