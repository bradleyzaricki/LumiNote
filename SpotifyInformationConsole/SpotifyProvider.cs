using System;
using System.Diagnostics;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using SpotifyAPI.Web;
using System.Windows.Forms;

namespace SpotifyInformationConsole
{
    public class SpotifyProvider
    {
        static string clientId = "7a3be16d49114bcb8317330636aa2647"; // replace this
        static string redirectUri = "http://localhost:5000/callback";
        static string refreshToken = null;
        SpotifyClient _spotify;
        Form _spotifyForm;

        public SpotifyProvider()
        {
            ///
        }


        public async Task InitializeClient()
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
                Scope = new[] { Scopes.UserReadPlaybackState, Scopes.UserReadCurrentlyPlaying, Scopes.UserModifyPlaybackState, Scopes.UgcImageUpload}
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

            var playback = await _spotify.Player.GetCurrentlyPlaying(new PlayerCurrentlyPlayingRequest());
            if (playback?.Item is FullTrack track)
            {
                Console.WriteLine($"Now playing: {track.Name} by {track.Artists[0].Name}");
            }
            else
            {
                Console.WriteLine("Nothing currently playing.");
            }
            InitializeForm();
        }
        private void InitializeForm()
        {
            // Save tokenResponse.RefreshToken somewhere safe
            _spotifyForm = new BaseUI(_spotify);
            Application.Run(_spotifyForm);
        }
    }
}
