**LumiNote Software Kit** <br>
LumiNote turns cheap WS2812B LED strips into festival-grade light shows. Design custom light sequences on a timeline, sync them to Spotify or local audio, and drive real hardware over USB serial in real time.<br>
<br>
Features:<br>
-Timeline based light show editor with 10+ optional effects per block<br>
-Spotify playback integration via PKCE OAuth<br>
-Local audio file support (MP3, WAV, FLAC)<br>
-Cloud database for sharing and downloading light shows, powered by Cloudflare Workers + D1 + R2 databases<br>
-Real-time LED output over a custom binary serial protocol to an ESP32<br>
<br>
Development choices I'm proud of:<br>
-Audio source and playback timer are separate interfaces so adding Apple Music or any other source only requires implementing IMusicProvider, nothing else changes<br>
-The backend data storage runs entirely on Cloudflare's network, no server to maintain<br>
-All lighting effects run through a single stateless function so the desktop visualizer and the ESP32 see identical output. This also makes it easy to add future light effects<br>




(OUTDATED)

Here is an example of a simple lightshow made using Luminotes lightshow editor:<br/>
<video src="https://github.com/user-attachments/assets/81bd91fd-5eb8-4992-8b31-bdfcae89c7d6"
       controls
       autoplay
       loop
       muted
       width="800">
</video>

Current Luminote UI:<br/>
<img width="2930" height="1688" alt="image" src="https://github.com/user-attachments/assets/01dea13c-714e-405b-ada7-3b5693fce3d2" />

