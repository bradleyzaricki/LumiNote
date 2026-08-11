# LumiNote (LumikitApp)

Avalonia desktop app (.NET 8, C#) that designs timeline-based LED light shows, syncs them to
Spotify or local audio, and drives WS2812B strips on an ESP32 over USB serial. See
`../README.md` for the product pitch.

## Build & run

```bash
dotnet build
dotnet run
```

- **After editing any `.axaml`/`.xaml`, do a clean build:** `dotnet clean && dotnet build`.
  Rider's incremental build sometimes skips Avalonia's XamlX precompiler, causing a runtime
  crash: *"No precompiled XAML found for ..."*. The CLI clean build always reruns it.
- **`bin/` and `obj/` are committed to git** even though `.gitignore` lists them (they were
  tracked before the ignore). The running app locks those DLLs, so builds and **git
  operations** (`stash`, `reset`, `checkout`) fail with *"file is being used by another
  process"* / *"unable to unlink"*. Fix: close the app first, or
  `Get-Process dotnet,LumikitApp | Stop-Process -Force` (PowerShell), then retry.
- Shell here is PowerShell (Windows). A Bash tool is also available for POSIX scripts.
- Native deps: **ManagedBass** (local audio playback/decoding), **System.IO.Ports** (serial).
  Targets Windows primarily but is meant to stay cross-platform (Mac/Linux) — don't add
  Windows-only dependencies. Spotify's embedded Web Playback SDK was rejected for this reason
  (Widevine DRM is effectively Windows-only).

## Startup flow (`App.axaml.cs` → `OnFrameworkInitializationCompleted`)

1. `ProviderPickerWindow` — multi-select of which providers to enable this session
   (returns `SelectedProviders`).
2. `BuildServices(selected)` — builds the DI container based on that selection.
3. `OffsetTapper` — user taps to a metronome to measure `ComputedOffsetMs` (audio→light
   latency calibration).
4. `LumikitWindow` — main window; gets `AudioOffsetMs` and calls `InitializeWindow()`.

## Architecture

**Two-interface strategy for audio**, split by responsibility (this split is intentional —
don't merge them):
- `IMusicProvider` — the **source/transport** (Spotify vs local file): play/pause/seek
  primitives, `currentlyPlayingPath`, `providerName`, `ProviderColor`.
- `IPlaybackHandler` — the **timing/sync** strategy: owns the lightshow clock, raises
  `ProgressUpdated(ms)` and `PlaybackStopped`.
- Concrete pairs: `SpotifyProvider`+`SpotifyPlaybackHandler`, `MusicFileProvider`+
  `LocalFilesPlaybackHandler`.

**`RoutingMusicSession`** (`Playback Logic/RoutingMusicSession.cs`) implements **both**
interfaces as a delegating composite. It holds every enabled pair and forwards to the active
one; `SwitchToAsync(name)` **pauses the current handler before switching**. It's registered in
DI as `IMusicProvider`, `IPlaybackHandler`, **and** its concrete type (all the same instance),
so the window holds stable references while the active source changes at runtime. To add a new
source, implement the two interfaces and register the pair in `BuildServices` — nothing else
changes.

**Playback clock model:** handlers don't poll the source per frame. They anchor a local
`Stopwatch` once at (re)start and extrapolate `_anchorMs + elapsed`, firing `ProgressUpdated`
every ~10 ms. So per-frame cost is zero network. `LumikitWindow` subscribes once to
`ProgressUpdated` → `Timeline.Tick(ms + AudioOffsetMs, ...)` → `LightEffectsComputer` → serial
frame + on-screen color bars.

### Spotify sync constraints (important)
The Spotify Web API is a **remote control**: it only honors **whole-second** seeks and reports
a **coarse, stale** position. Exact sync is architecturally impossible over it. Current design:
`SpotifyPlaybackHandler.ReanchorAndPlay` floors the target to the previous second and runs
**[load] → PAUSE → SEEK → PLAY**, then a small fixed `PlayLatencyMs` (~50 ms) delay before
anchoring the local `Stopwatch`, so the clock starts when audio actually starts. No mid-playback
re-sync. `PlayLatencyMs` (top of the handler) is the single knob for start latency; the global
`AudioOffsetMs` is the perceptual trim. Local files are sample-exact; playback **prefers local
when available**.

## Lighting

- **`LightEffectsComputer.ComputeBlockEffects` is the single stateless function** used by
  *both* the on-screen preview and the hardware output, so they're guaranteed identical. It
  computes a 1000-element virtual strip; `SerialHandler` downsamples to the real LED count.
  Add new effects here.
- `LightBlock.BlockEffects` is `List<EffectData>` — **each effect owns its params**
  (`Dictionary<string,double>`, e.g. Combine `TargetWidth`, Repeat `Count`). Don't add
  per-effect fields to `LightBlock`. `EffectDataListConverter` migrates old saves (bare int
  lists, and old `Seperate`→`Combine`+`Direction=-1`).
- **`EffectCatalog`** (`Data Models/EffectCatalog.cs`) is the schema for all effects: category
  (**Shape** = mutually exclusive Travel/Combine/Seperate; **Texture** = mutually exclusive
  per-pixel modulation, e.g. Twinkle; **Modifier** = stackable), editor title, and param
  definitions (key, UI title, control type — `NumberBox`/`CheckBox` — default, min/max). The
  block editor **generates its param inputs from this** (`EffectParams` ItemsControl); to give
  an effect a new param, declare it in the catalog and read it in `LightEffectsComputer` — the
  sidebar UI follows automatically. Shape/texture are stored in `BlockEffects` like any effect
  (JSON format unchanged); use `LightBlock.GetShape()/SetShape()/GetTexture()/SetTexture()`.
  Textures run **after** the FillColor pass; their `AffectFill` param (default on) controls
  whether fill pixels are modulated too.
- **Shapes:** Static, Travel, Combine, Seperate, **Scanner** (a fixed-width bar sweeping the
  span — `Wrap` param picks bounce vs wrap-around, `Width`/`Cycles` size and speed it).
  **Textures:** Twinkle, **Shimmer** (scrolling brightness sine), **Sparkle** (clustered
  white glints). **Modifier `Comet`** paints a fading tail into pixels a moving shape is
  vacating: shapes emit "tail requests" (trailing edge + direction, derived from edge
  velocity signs) during their switch case, and a single pass after the switch renders them —
  so Comet works for any moving shape (Travel/Combine/Seperate/Scanner) without per-shape code.
- **`TimelineView`** (`UI/Controls/`) owns the light blocks, selection, drag/resize, and the
  **undo history** (`CaptureState`/`PushUndo`/`Undo`; Ctrl+Z in `LumikitWindow.OnKeyDown`).
  `MsPerSlot = 50`.
- **`BlockEditorPanel`** auto-applies editor changes onto selected blocks via
  `BlockEditorViewModel.PropertyChanged` (guarded by `_isLoading`) — there is no "Apply"
  button.

## Data & persistence

- `DirectoryPaths` → `%LocalAppData%/LumiNote/{Audio,TrackInfo,Settings}` (override via
  `LUMINOTE_*` env vars; `.env` loaded by `DotNetEnv` in `Program.Main`).
- `JsonDataHandler` — one JSON file per track in `TrackInfo`. `TrackData.Sources` is a
  `Dictionary<provider→id/path>`; `MigrateLegacyFields()` upgrades old flat `filePath`/
  `provider` and runs on every load.
- `DatabaseAccess` — shared cloud library via a Cloudflare Worker REST API. **Note: `BaseUrl`
  and `ApiKey` are hardcoded** in the source.
- `ProviderCredentialStore` (`Settings/provider_credentials.json`) — **the app ships no Spotify
  client id**. Spotify's Developer Terms forbid disclosing our Security Codes to third parties,
  so each user registers their own developer app and enters its client id
  (`ProviderCredentialsWindow`). `ProviderMetadata` declares per-provider setup requirements and
  the picker/DI wiring are data-driven off it — a new source needs an enum case, cases in the
  `ProviderMetadata` switches, and a factory case in `App.BuildServices`; no UI edits.

## Hardware output

- `SerialPanel` (`ISerialPanel`) owns the `SerialHandler` lifecycle and `BrightnessScale`
  (derived from input current ÷ LED power budget). `TrySendFrameAsync` runs the blocking write
  off the UI thread and reports errors via events.
- `SerialHandler` speaks a custom binary protocol to an ESP32 (`0xAA 0x55` header, seq byte,
  length, RGB payload, CRC16), 460800 baud. Alpha is premultiplied into RGB before sending.

## Conventions / gotchas

- Folders are nested (`UI/Controls`, `Playback Logic/...`) but most types live in the flat
  `LumikitApp` namespace; sub-namespaces (e.g. `LumikitApp.Controls`) resolve `LumikitApp`
  types without an explicit `using`.
- `AudioOffsetMs` is a **single global** calibration applied in `Timeline.Tick`. It can't be
  right for both Spotify and local simultaneously (very different latencies) — a per-provider
  offset is a known gap if sync precision matters.
- When editing `LumikitWindow.axaml`, watch line 1: a stray prefix has corrupted it before
  (`ost<Window ...`), which fails the XAML build with *"Data at the root level is invalid"*.
- Most cross-window handoffs use `TaskCompletionSource` exposed as a `Task` (e.g.
  `ProviderPickerWindow.Choice`, `OffsetTapper.Completed`).
