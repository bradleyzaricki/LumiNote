using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using ManagedBass;

namespace LumikitApp
{
    /// <summary>
    /// This class will handle all spotify endpoints
    /// </summary>
    public class MusicFileProvider : IMusicProvider
    {
        public string providerName => "LocalFiles";
        public TrackPOCO currentTrack { get; set; }
        private int _stream;
        private bool _init;
        private Window _mainWindow;

        public string currentlyPlayingPath {get; set;}
        /// <summary>
        /// Spotify API wrapper for Lumikit procedures
        /// </summary>
        /// <param name="mainWindow"></param>
        /// <param name="clientId"></param>
        /// <param name="redirectUri"></param>
        public MusicFileProvider(Window mainWindow)
        {

            _mainWindow = mainWindow;

        }
        

        public Task<bool> IsPlayingAsync()
        {
            if (_stream == 0) return Task.FromResult(false);
            return Task.FromResult(Bass.ChannelIsActive(_stream) == PlaybackState.Playing);
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
        /// Skip the current track and 
        /// </summary>
        public async Task SkipTrack(TrackPOCO track, string filePath)
        {
            await InitializeClient();
            currentTrack = track;
            var path = filePath;

            if (_stream != 0)
            {
                Bass.ChannelStop(_stream);
                Bass.StreamFree(_stream);
                _stream = 0;
            }

            _stream = Bass.CreateStream(path, Flags: BassFlags.Default);
            Bass.ChannelPlay(_stream);

            System.Console.WriteLine("Playing");
        }
        
        public async Task SkipTrack()
        {
            await InitializeClient();

            if (_stream != 0)
            {
                Bass.ChannelStop(_stream);
                Bass.StreamFree(_stream);
                _stream = 0;
            }

            _stream = Bass.CreateStream(currentlyPlayingPath, Flags: BassFlags.Default);
            Bass.ChannelPlay(_stream);

            System.Console.WriteLine("Playing");
        }
        
        public Task InitializeClient()
        {
            if (_init) return Task.CompletedTask;
            _init = Bass.Init();
            return Task.CompletedTask;
        }


        public void SetMainWindow(Window mainWindow)
        {
            _mainWindow = mainWindow;
        }
    }
}
