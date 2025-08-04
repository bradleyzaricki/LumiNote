Lumikit is a C# software application that acts as a music player by utilising spotify web services.
Ontop of being a music player, the user can drag and drop light configurations to play alongside music tracks.
These light configurations will be parsed and sent to an ESP32 microcontroller which will parse that data into
  live light sequences onto a WS2812 LED light strip (utilising the FastLED library)

Currently the project includes

-Pause/Play/Skip playback features
-A live visual of the track playback including optional BPM timestamps for more efficient editing
-A drag n drop color pallet that the user can use to "paint" the track playback
-Saving track colormaps and bpm data via a local json file

TODO:
-add and send arduino serial protocol to be received
-add more playback features (visual queue, song searching, prev track button...)
-add more lighting effects and parameters (fade in, fade out, start light & end light, strobe mode)

Screenshots:
<img width="802" height="482" alt="image" src="https://github.com/user-attachments/assets/7ad015fc-4d69-461e-a52c-2363a653c074" />
