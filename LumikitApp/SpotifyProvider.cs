using System;
using System.Diagnostics;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using SpotifyAPI.Web;
using System.Linq;
using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.Controls.Primitives;
namespace LumikitApp
{

    public class SpotifyProvider
    {
        static string clientId = "7a3be16d49114bcb8317330636aa2647"; // replace this
        static string redirectUri = "http://localhost:5000/callback";
        Window _LumikitWindow;
        SpotifyClient _spotify;
        public SpotifyProvider(Window mainWindow)
        {
            _LumikitWindow = mainWindow;
        }
        public async Task<SpotifyClient> InitializeClient()
        {
            var (verifier, challenge) = PKCEUtil.GenerateCodes();

            var loginRequest = new LoginRequest(
                new Uri(redirectUri),
                clientId,
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
                new PKCETokenRequest(clientId, code, new Uri(redirectUri), verifier)
            );

            var config = SpotifyClientConfig.CreateDefault().WithToken(tokenResponse.AccessToken);
            _spotify = new SpotifyClient(config);
            return _spotify;
        }
        /// <summary>
        /// Get list of active devices and returns the first in that list
        /// </summary>
        /// <returns></returns>
        private async Task<Device> GetCurrentDevices()
        {
            var devices = await _spotify.Player.GetAvailableDevices();
            var device = devices.Devices.FirstOrDefault(d => d.IsActive) ?? devices.Devices.FirstOrDefault();
            return device;
        }
        public bool isPlaying()
        {
            return _spotify.Player.GetCurrentPlayback().Result.IsPlaying;
        }
        /// <summary>
        /// Attemps to resume playback, first a force resume for minimum latency, if that fails then it will check if a device is in playback and attempt from there
        /// </summary>
        /// <returns></returns>

        public async Task ResumePlayback()
        {
            try
            {
                await _spotify.Player.ResumePlayback(); // Fast path
            }
            catch (APIException)
            {
                var device = GetCurrentDevices().Result;

                if (device == null)
                {
                    Console.WriteLine("No available Spotify devices found.");
                    return;
                }

                // This actually starts playback
                await _spotify.Player.TransferPlayback(
                    new PlayerTransferPlaybackRequest(new List<string> { device.Id }) { Play = true }
                );

            }
        }
        public async Task<FullTrack> GetCurrentlyPlayingTrack()
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
        public int GetPlaybackProgressMs()
        {
            var playback =  _spotify.Player.GetCurrentlyPlaying(new PlayerCurrentlyPlayingRequest()).Result;
            var progress = playback.ProgressMs;
            if (progress != null) return progress.Value;
            return 0;
        }
        /// <summary>
        /// Attemps to pause playback, first a force pause for minimum latency, if that fails then it will check if a device is in playback and attempt from there
        /// </summary>
        /// <returns></returns>
        public async Task PausePlayback()
        {
            try
            {
                await _spotify.Player.PausePlayback(); // Try fast pause
            }
            catch (APIException)
            {
                var device = GetCurrentDevices().Result;

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
                    await _spotify.Player.PausePlayback();
                }
                catch (APIException ex2)
                {
                    Debug.WriteLine("Pause failed after transfer: " + ex2.Message);
                }
            }
        }
        public async Task SeekToPlaybackTime(int ms)
        {
            try
            {
                await _spotify.Player.SeekTo(new PlayerSeekToRequest(ms));
                return;
            }
            catch (APIException)
            {
                var device = GetCurrentDevices().Result;

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
        public async Task SkipTrack()
        {
            var playback = await _spotify.Player.GetCurrentlyPlaying(new PlayerCurrentlyPlayingRequest());
            var oldId = (playback?.Item as FullTrack)?.Id;

            await _spotify.Player.SkipNext();
            await WaitForTrackChange(oldId);
        }
        /// <summary>
        /// When a track is expected to change, this will watch for that change and update UI accordingly
        /// </summary>
        /// <param name="oldTrackId"></param>
        /// <returns></returns>
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
        
    }
}
