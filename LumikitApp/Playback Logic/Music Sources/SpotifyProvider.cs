using System;
using System.Diagnostics;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using SpotifyAPI.Web;
using System.Linq;
using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.Media;

namespace LumikitApp
{
    /// <summary>
    /// This class will handle all spotify endpoints
    /// </summary>
    public class SpotifyProvider : IMusicProvider
    {
        /// <summary>
        /// Spotify Green Color
        /// </summary>
        public Color ProviderColor {get; set;}

        public bool IsProviderLocal { get; set; }

        public ProviderType providerName => ProviderType.Spotify;
        private readonly string _clientId;
        private readonly string _redirectUri;
        private SpotifyClient _spotify;
        
        //not for spotify
        public TrackPOCO currentTrack { get; set; }
        public string currentlyPlayingPath {get; set;}

        /// <summary>
        /// Spotify API wrapper for Lumikit procedures
        /// </summary>
        /// <param name="mainWindow"></param>
        /// <param name="clientId"></param>
        /// <param name="redirectUri"></param>
        public SpotifyProvider(string clientId, string redirectUri)
        {
            ProviderColor = new Color(255, 30, 215, 96);
            _clientId = clientId;
            _redirectUri = redirectUri;
        }
        
        /// <summary>
        /// Initialize a new spotify web API connection to be used 
        /// </summary>
        /// <returns></returns>
        public async Task InitializeClient()
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
                // Request only what the app actually calls. Spotify's app review treats
                // unused scopes as a compliance failure, and UgcImageUpload (playlist cover
                // upload) was never used by anything here.
                Scope = new[]
                {
                    Scopes.UserReadPlaybackState,
                    Scopes.UserReadCurrentlyPlaying,
                    Scopes.UserModifyPlaybackState
                }
            };

            var uri = loginRequest.ToUri();

            Process.Start(new ProcessStartInfo
            {
                FileName = uri.ToString(),
                UseShellExecute = true
            });

            var http = new HttpListener();
            http.Prefixes.Add("http://127.0.0.1:5000/callback/");
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

            return;
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

        public async Task PlayTrackAsync()
        {

            var request = new PlayerResumePlaybackRequest
            {
                Uris = new List<string>
                {
                    $"spotify:track:{currentlyPlayingPath}"
                }
            };
            Console.WriteLine("Switching to " + currentlyPlayingPath);
            await _spotify.Player.ResumePlayback(request);
            //await WaitForTrackChange(oldId);
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

                        await Task.Delay(500); // Spotify needs a second

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

                        await Task.Delay(500); // let Spotify settle

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

        /// <summary>
        ///  
        /// </summary>
        /// <returns>Returns the track as a C# object,</returns>
        public async Task<TrackPOCO> GetCurrentlyPlayingTrackAsync()
        {
            try
            {
                var playback = await _spotify.Player.GetCurrentlyPlaying(new PlayerCurrentlyPlayingRequest());
                var track = playback?.Item as FullTrack;
                if (track?.Id == currentlyPlayingPath)
                {
                    
                    // Images are ordered widest-first; index 1 is the medium (300px) art.
                    // Guard the index — some albums expose fewer than three sizes.
                    var cover = track.Album.Images.Count > 1
                        ? track.Album.Images[1].Url
                        : track.Album.Images.FirstOrDefault()?.Url ?? "";

                    // external_urls.spotify is the canonical open.spotify.com link; fall back to
                    // building it from the id so the required attribution link is never missing.
                    var url = track.ExternalUrls != null
                              && track.ExternalUrls.TryGetValue("spotify", out var ext)
                              && !string.IsNullOrWhiteSpace(ext)
                        ? ext
                        : $"https://open.spotify.com/track/{track.Id}";

                    currentTrack = new TrackPOCO(Guid.Empty, track.Name, track.Artists[0].Name, cover, url);
                    return currentTrack;
                }

            }
            catch
            {
                Console.WriteLine("Error Updating Track: Could not get CurrentlyPlaying item");
            }
            return null;

        }

        public string GetCurrentlyPlayingTrackIdAsync()
        {
            var playback =  _spotify.Player.GetCurrentlyPlaying(new PlayerCurrentlyPlayingRequest()).Result;
            var track = playback.Item as FullTrack;
            return track.Id;
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

        /// <summary>
        /// Total duration of the loaded track via the Spotify track endpoint (0 if unknown).
        /// </summary>
        public async Task<int> GetTrackDurationMsAsync()
        {
            try
            {
                if (string.IsNullOrEmpty(currentlyPlayingPath)) return 0;
                var track = await _spotify.Tracks.Get(currentlyPlayingPath);
                return track?.DurationMs ?? 0;
            }
            catch (Exception ex)
            {
                Debug.WriteLine("GetTrackDurationMs failed: " + ex.Message);
                return 0;
            }
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
        public IPlaybackHandler GetPlaybackHandler()
        {
            return new SpotifyPlaybackHandler(this);
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
        
    }
}
