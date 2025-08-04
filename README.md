Melolux is a C# software application that acts as a music player by utilising spotify web services.<br/>
Ontop of being a music player, the user can drag and drop light configurations to play alongside music tracks.<br/>
These light configurations will be parsed and sent to an ESP32 microcontroller which will parse that data into<br/>
  live light sequences onto a WS2812 LED light strip (utilising the FastLED library)<br/>
<br/>
Currently the project includes:<br/>
<br/>
-Pause/Play/Skip playback features<br/>
-A live visual of the track playback including optional BPM timestamps for more efficient editing<br/>
-A drag n drop color pallet that the user can use to "paint" the track playback<br/>
-Saving track colormaps and bpm data via a local json file<br/>
<br/>
TODO:<br/>
-add and send arduino serial protocol to be received<br/>
-add more playback features (visual queue, song searching, prev track button...)<br/>
-add more lighting effects and parameters (fade in, fade out, start light & end light, strobe mode)<br/>
-add various quality of life features<br/>
<br/>
Screenshots:<br/>
<img width="802" height="482" alt="image" src="https://github.com/user-attachments/assets/0da375f2-340d-4fa6-b40e-905e1f4ab324" />

<img width="802" height="482" alt="image" src="https://github.com/user-attachments/assets/84f3e303-4f0c-4e3d-914c-feffefa87585" />
<img width="802" height="482" alt="image" src="https://github.com/user-attachments/assets/1f4b1086-03e9-4852-928d-6597f7f203d6" />
