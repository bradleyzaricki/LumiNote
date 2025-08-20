using System;
using System.Diagnostics;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using SpotifyAPI.Web;
using System.Linq;
using System.Collections.Generic;
using Avalonia.Controls;

namespace LumikitApp
{
    public class SpotifyProvider : ISpotifyProvider
    {
        private readonly string _clientId;
        private readonly string _redirectUri;
        private Window _mainWindow;
        private SpotifyClient _spotify;

        /// <summary>
        /// Spotify API wrapper for Lumikit procedures
        /// </summary>
        /// <param name="mainWindow"></param>
        /// <param name="clientId"></param>
        /// <param name="redirectUri"></param>
        public SpotifyProvider(Window mainWindow, string clientId, string redirectUri)
        {
            _mainWindow = mainWindow;
            _clientId = clientId;
            _redirectUri = redirectUri;
        }
        
        /// <summary>
        /// Initialize a new spotify web API connection to be used 
        /// </summary>
        /// <returns></returns>
        public async Task<SpotifyClient> InitializeClient()
        {
            var (verifier, challenge) = PKCEUtil.GenerateCodes();

            var loginRequest = new LoginRequest(
                new Uri(_redirectUri),
                _clientId,
                LoginRequest.ResponseType.Code
            )
            {
                CodeChallengeMethod = "S256",
                CodeChallenge = challenge,
                Scope = new[] { Scopes.UserReadPlaybackState, Scopes.UserReadCurrentlyPlaying, Scopes.UserModifyPlaybackState, Scopes.UgcImageUpload }
            };

            var uri = loginRequest.ToUri();

            Process.Start(new ProcessStartInfo
            {
                FileName = uri.ToString(),
                UseShellExecute = true
            });

            var http = new HttpListener();
            http.Prefixes.Add("http://localhost:5000/callback/");
            http.Start();

            Console.WriteLine("Waiting for Spotify login...");

            var context = await http.GetContextAsync();
            var code = context.Request.QueryString["code"];

            string responseHtml = "<html><body>Login successful. You can close this window.</body></html>";
            byte[] buffer = Encoding.UTF8.GetBytes(responseHtml);
            context.Response.ContentLength64 = buffer.Length;
            await context.Response.OutputStream.WriteAsync(buffer, 0, buffer.Length);
            context.Response.OutputStream.Close();
            http.Stop();

            var tokenResponse = await new OAuthClient().RequestToken(
                new PKCETokenRequest(_clientId, code, new Uri(_redirectUri), verifier)
            );

            var config = SpotifyClientConfig.CreateDefault().WithToken(tokenResponse.AccessToken);
            _spotify = new SpotifyClient(config);

            return _spotify;
        }

        /// <summary>
        /// Get the current device 
        /// </summary>
        /// <returns></returns>
        private async Task<Device> GetCurrentDeviceAsync()
        {
            var devices = await _spotify.Player.GetAvailableDevices();
            return devices.Devices.FirstOrDefault(d => d.IsActive) ?? devices.Devices.FirstOrDefault();
            
        }

        public async Task<bool> IsPlayingAsync()
        {
            var playback = await _spotify.Player.GetCurrentPlayback();
            return playback?.IsPlaying ?? false;
        }

        /// <summary>
        /// Resume current playback as fast as possible to avoid any latency
        /// </summary>
        public async Task ResumePlaybackAsync()
        {
            try
            {
                await _spotify.Player.ResumePlayback(); 
            }
            catch (APIException ex)
            {
                Debug.WriteLine("ResumePlayback failed: " + ex.Message);

                if (ex.Response?.StatusCode == HttpStatusCode.Forbidden ||
                    ex.Response?.StatusCode == HttpStatusCode.NotFound)
                {
                    var device = await GetCurrentDeviceAsync(); 

                    if (device == null)
                    {
                        Debug.WriteLine("No available Spotify devices found.");
                        return;
                    }

                    try
                    {
                        await _spotify.Player.TransferPlayback(
                            new PlayerTransferPlaybackRequest(new List<string> { device.Id }) { Play = true }
                        );

                        await Task.Delay(300); // Spotify needs a second

                        await _spotify.Player.ResumePlayback(); // Retry
                        Debug.WriteLine("ResumePlayback retried after transfer.");
                    }
                    catch (APIException ex2)
                    {
                        Debug.WriteLine("Retry after transfer failed: " + ex2.Message);
                    }
                }
            }
        }

        /// <summary>
        /// Pause current playback as soon as possible to avoid latency
        /// </summary>
        public async Task PausePlaybackAsync()
        {
            try
            {
                await _spotify.Player.PausePlayback();
            }
            catch (APIException ex)
            {
                Debug.WriteLine("PausePlayback failed: " + ex.Message);

                // If the failure is due to no active device or playback context, recover
                if ((int?)ex.Response?.StatusCode == 403 || (int?)ex.Response?.StatusCode == 404)
                {
                    var device = await GetCurrentDeviceAsync(); // no .Result

                    if (device == null)
                    {
                        Debug.WriteLine("No available Spotify devices found.");
                        return;
                    }

                    try
                    {
                        await _spotify.Player.TransferPlayback(
                            new PlayerTransferPlaybackRequest(new List<string> { device.Id }) { Play = false }
                        );

                        await Task.Delay(300); // let Spotify settle

                        await _spotify.Player.PausePlayback(); // Retry pause
                        Debug.WriteLine("PausePlayback retried after transfer.");
                    }
                    catch (APIException ex2)
                    {
                        Debug.WriteLine("Retry after transfer failed: " + ex2.Message);
                    }
                }
            }
        }

        public async Task<FullTrack> GetCurrentlyPlayingTrackAsync()
        {
            try
            {
                var playback = await _spotify.Player.GetCurrentlyPlaying(new PlayerCurrentlyPlayingRequest());
                var track = playback?.Item as FullTrack;
                if (track != null) return track;
            }
            catch
            {
                Console.WriteLine("Error Updating Track: Could not get CurrentlyPlaying item");
            }
            return null;

        }

        /// <summary>
        /// GET integer value of playback progress in ms 
        /// </summary>
        /// <returns></returns>
        public async Task<int> GetPlaybackProgressMsAsync()
        {
            var playback = await _spotify.Player.GetCurrentlyPlaying(new PlayerCurrentlyPlayingRequest());
            return playback?.ProgressMs ?? 0;
        }

        public async Task SeekToPlaybackTime(int ms)
        {
            try
            {
                await _spotify.Player.SeekTo(new PlayerSeekToRequest(ms));
                return;
            }
            catch (APIException ex)
            {

                var device = await GetCurrentDeviceAsync();

                if (device == null)
                {
                    Debug.WriteLine("No available Spotify devices found.");
                    return;
                }

                await _spotify.Player.TransferPlayback(
                    new PlayerTransferPlaybackRequest(new List<string> { device.Id }) { Play = false } // force playback
                );

                await Task.Delay(300); // allow Spotify to catch up

                try
                {
                    await _spotify.Player.SeekTo(new PlayerSeekToRequest(ms));
                }
                catch (APIException ex2)
                {
                    Debug.WriteLine("Pause failed after transfer: " + ex2.Message);
                }
            }
        }

        /// <summary>
        /// Skip the current track and 
        /// </summary>
        public async Task SkipTrack()
        {
            var playback = await _spotify.Player.GetCurrentlyPlaying(new PlayerCurrentlyPlayingRequest());
            var oldId = (playback?.Item as FullTrack)?.Id;

            await _spotify.Player.SkipNext();
            await WaitForTrackChange(oldId);
        }

        private async Task WaitForTrackChange(string oldTrackId)
        {
            for (int i = 0; i < 1000; i++)//try to fetch track for 1 second, more than enough time for API
            {
                await Task.Delay(1);

                var playback = await _spotify.Player.GetCurrentlyPlaying(new PlayerCurrentlyPlayingRequest());
                var track = (playback?.Item as FullTrack);


                if (!string.IsNullOrEmpty(track.Id) && track.Id != oldTrackId)
                    return;

            }

            throw new TimeoutException("Track didn't change within expected time.");
        }

        public void SetMainWindow(Window mainWindow)
        {
            _mainWindow = mainWindow;
        }
    }
}
