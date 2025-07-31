using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using System;
using System.Diagnostics;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using SpotifyAPI.Web;
using System;
using System.Drawing;          
using System.IO;                 
using System.Net.Http;            
using System.Threading.Tasks;    
using System.Windows.Forms;       
namespace SpotifyInformationConsole
{
    
    public partial class BaseUI : Form
    {
        SpotifyClient _spotify;
        public BaseUI(SpotifyClient spotify)
        {
            InitializeComponent();
            _spotify = spotify;
            updateTrack();


        }
        private async void updateTrack()
        {
            try
            {
                var playback = await _spotify.Player.GetCurrentlyPlaying(new PlayerCurrentlyPlayingRequest());
                var track = playback?.Item as FullTrack;
                if (!(track is FullTrack)) return;

                currentlyPlayingTrackLabel.Text = track.Name;
                var albumImages = track.Album.Images;

                if (albumImages != null && albumImages.Count > 0)
                {
                    var imageUrl = albumImages[1].Url; // the medium sized one
                    Console.WriteLine("Album cover URL: " + imageUrl);
                    await SetAlbumCover(imageUrl);
                }
            }
            catch
            {
                Console.WriteLine("Error Updating Track: Could not get CurrentlyPlaying item");
                return;
            }

        }

        private async Task<Device> GetCurrentDevices()
        {
            var devices = await _spotify.Player.GetAvailableDevices();
            var device = devices.Devices.FirstOrDefault(d => d.IsActive) ?? devices.Devices.FirstOrDefault();
            return device;
        }

        private async void Button_PauseTrack_Click(object sender, EventArgs e)
        {
            try
            {
                await _spotify.Player.PausePlayback(); // try pause immediately
            }
            catch (APIException ex)
            {
                // fallback: no active device or device doesn't support pause
                var device = GetCurrentDevices().Result;
                if (device == null)
                {
                    Console.WriteLine("No available Spotify devices found.");
                    return;
                }

                    await _spotify.Player.TransferPlayback(
                    new PlayerTransferPlaybackRequest(new List<string> { device.Id }) { Play = null }
                );

                await Task.Delay(500);

                var playback = await _spotify.Player.GetCurrentPlayback();
                if (playback?.IsPlaying == true)
                {
                    await _spotify.Player.PausePlayback();
                }
                else
                {
                    Console.WriteLine("Playback is not playing, therefore cannot pause");
                }
            }

        }



        private async void Button_NextTrack_Click (object sender, EventArgs e)
        {
            try
            {
                await SkipTrackAndWait();
                updateTrack();
            }
            catch(APIException ex)
            {
                Console.WriteLine(ex);
            }

        }


        private async Task SkipTrackAndWait()
        {
            var playback = await _spotify.Player.GetCurrentlyPlaying(new PlayerCurrentlyPlayingRequest());
            var oldId = (playback?.Item as FullTrack)?.Id;

            await _spotify.Player.SkipNext();
            await WaitForTrackChange(oldId);
        }

        private async Task WaitForTrackChange(string oldTrackId)
        {
            for (int i = 0; i < 1000; i++)
            {
                await Task.Delay(1);

                var playback = await _spotify.Player.GetCurrentlyPlaying(new PlayerCurrentlyPlayingRequest());
                var track = (playback?.Item as FullTrack);


                if (!string.IsNullOrEmpty(track.Id) && track.Id != oldTrackId)
                    return;
                updateTrack();

            }

            throw new TimeoutException("Track didn't change within expected time.");
        }
        private async Task SetAlbumCover(string url)
        {
            using (var client = new HttpClient())
            {
                var data = await client.GetByteArrayAsync(url);
                using (var ms = new MemoryStream(data))
                {
                    var albumCover = ImageFunctionality.ResizeImage(System.Drawing.Image.FromStream(ms), 128, 128);
                    pictureBox1.Image = albumCover;
                    currentlyPlayingTrackLabel.BackColor = ImageFunctionality.GetMostCommonColor(albumCover);
                    double luminance = (0.299 * currentlyPlayingTrackLabel.BackColor.R + 0.587 * currentlyPlayingTrackLabel.BackColor.G + 0.114 * currentlyPlayingTrackLabel.BackColor.B);
                    if (luminance < 100) currentlyPlayingTrackLabel.ForeColor = Color.White;
                    else currentlyPlayingTrackLabel.ForeColor = Color.Black;
                }
            }
        }


        private async void Button_ResumeTrack_Click(object sender, EventArgs e)
        {
            try
            {
                await _spotify.Player.ResumePlayback(); // try pause immediately
            }
            catch (APIException ex)
            {
                // fallback: no active device or device doesn't support pause
                var device = GetCurrentDevices().Result;
                if (device == null)
                {
                    Console.WriteLine("No available Spotify devices found.");
                    return;
                }

                await _spotify.Player.TransferPlayback(
                new PlayerTransferPlaybackRequest(new List<string> { device.Id }) { Play = true }
            );

                await Task.Delay(500);

                var playback = await _spotify.Player.GetCurrentPlayback();
                if (playback?.IsPlaying == true)
                {
                    await _spotify.Player.ResumePlayback();
                }
                else
                {
                    Console.WriteLine("Playback is not playing, therefore cannot pause");
                }
            }
        }
    }
}
