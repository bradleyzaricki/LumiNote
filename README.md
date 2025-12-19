LumiNote is a C# software application that acts as a music player by utilising either local files or spotify web services.<br/>
Ontop of being a music player, the user can drag and drop light configurations to play alongside music tracks.<br/>
These light configurations will be parsed and sent to an ESP32 microcontroller which will parse that data into<br/>
a custom lightshow fully synchronized to the users track<br/>

LumiNote Software has a built in lightshow editor to easily stitch together organized reactive lightshows to your favorite songs<br/>

Here is an example of a simple lightshow made using Luminotes lightshow editor:<br/>
https://github.com/user-attachments/assets/81bd91fd-5eb8-4992-8b31-bdfcae89c7d6

Current Luminote UI:<br/>
<img width="2930" height="1688" alt="image" src="https://github.com/user-attachments/assets/01dea13c-714e-405b-ada7-3b5693fce3d2" />

In Development:<br/>
* More lighting effects (currently 8, want 25-30)
* Mode with customizable ambient lights for when a nonmapped or no track is playing
* Full database support with proper safeuguards in place
* More diverse color system
* Optimization
