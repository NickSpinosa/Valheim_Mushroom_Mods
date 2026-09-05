# 03 — Embedded horn sound and local playback

**Goal:** the mod can load a horn recording embedded in its own DLL and play
it as a spatialised 3D sound at a world position, following a transform,
fading linearly to silence at Hearing Range, scaled by Horn Volume and by the
game's own SFX slider. Nothing triggers it yet except a debug console command.

**Depends on:** 01. **Unblocks:** 04.

## Facts you need

- The game ships **no horn sound**. The nearest vanilla clips are the Gjall
  blowout, wolf howls and the offering bell, none of which will do. The
  recording comes from ticket 07; until it lands, this ticket ships a
  synthesised placeholder so the pipeline is testable.
- Unity cannot decode Ogg or MP3 from a byte array without `UnityWebRequest`
  modules the project does not reference. **WAV is the format**: parse the
  RIFF container yourself and build the clip with `AudioClip.Create` +
  `SetData`. Support only 16-bit PCM, mono or stereo, any sample rate. Reject
  anything else with a clear `LogError` naming what was found.
- `AudioMan` (assembly_valheim) owns the mixer: public fields
  `m_masterMixer` (AudioMixer), `m_ambientMixer` and `m_guiMixer`
  (AudioMixerGroup). The SFX group is not a public field. The mixer's exposed
  parameter for effects volume is named `SfxVol`, and
  `AudioMan.GetSFXVolume()` / `SetSFXVolume(float)` are public static. Every
  vanilla `ZSFX` has an `AudioSource` whose `outputAudioMixerGroup` is the SFX
  group, which is how you find it (below).
- `ZSFX` (public fields `m_audioClips`, `m_minVol`, `m_maxVol`, `m_maxDelay`,
  etc.) is the game's sound component and handles concurrency, reverb by
  distance, and fade. It is built around prefabs and a hash registry, not
  runtime clips, so **do not use it**. A plain `AudioSource` on a temporary
  GameObject is enough here. Say so in `docs/DESIGN.md` so nobody retries it.

## Deliverables

### `Assets/horn.wav` (placeholder)

Generate a placeholder with a short script (PowerShell or C#, kept under
`audible-horn/tools/`, and gitignored per `docs/devops.md` which skips
`tools/` projects): a 2.5 s, 44.1 kHz, 16-bit mono clip of a 220 Hz
fundamental with 2nd and 3rd harmonics, 100 ms attack, 600 ms release. It must
sound obviously synthetic so nobody mistakes it for the final asset. Commit the
resulting `Assets/horn.wav`; ticket 07 replaces it. Keep it under 300 KB.

### `src/Audio/WavLoader.cs`

`internal static AudioClip Load(string resourceName)` — reads the embedded
resource `AudibleHorn.Assets.horn.wav` (the manifest name is
`<RootNamespace>.<path with dots>`; verify with
`Assembly.GetExecutingAssembly().GetManifestResourceNames()` and log the list
at Info once if the expected name is missing), parses `fmt ` and `data` chunks
(walk chunks by id and size, do not assume `data` follows `fmt ` immediately),
converts int16 to float in [-1, 1], and returns
`AudioClip.Create("SignalHorn", frames, channels, sampleRate, false)` with
`SetData`. Cache the clip in a static; it is loaded once per process.

### `src/Audio/HornAudio.cs`

```csharp
internal static class HornAudio
{
    /// Plays a Horn Call at pos. If follow is non-null the sound rides with it.
    internal static void Play(Vector3 pos, Transform follow, float hearingRange, float volume);
    internal static bool IsReady { get; }   // clip loaded and mixer group found
}
```

Behaviour of `Play`:

- No-op with a single Warning (once) when `AudioClip` is null, and silently
  when there is no `AudioListener` in the scene (dedicated server).
- Create `new GameObject("SignalHornCall")`, parent to `follow` if given
  (`worldPositionStays: false`, local position zero), else place at `pos`.
- `AudioSource`: `clip`, `spatialBlend = 1f`, `rolloffMode =
  AudioRolloffMode.Linear`, `minDistance = 2f`, `maxDistance = hearingRange`,
  `dopplerLevel = 0f`, `spread = 0f`, `volume = Mathf.Clamp01(volume)`,
  `outputAudioMixerGroup = SfxGroup`, `playOnAwake = false`, then `Play()`.
  Linear rolloff to `maxDistance` is the glossary's "falls off linearly to
  silence at the edge"; do not switch to logarithmic.
- `Object.Destroy(go, clip.length + 0.1f)`.

`SfxGroup` resolution, lazily on first `Play`: iterate
`ZNetScene.instance.m_prefabs`, find the first with a `ZSFX` component whose
`GetComponent<AudioSource>().outputAudioMixerGroup != null`, cache that group,
and log its name once. If ZNetScene is not up or nothing matches, leave the
group null (the sound still plays, just outside the SFX slider) and log a
Warning once. Record the group name you observed in `docs/DESIGN.md`.

### Debug command

Register a console command `hornsound` (see how `Terminal.ConsoleCommand` is
constructed; any existing mod in the repo that adds one, or the decompiled
`Terminal.decompiled.cs`, shows the constructor) that calls
`HornAudio.Play(localPlayer.position + forward*5, null, Settings.HearingRange.Value, Settings.HornVolume.Value)`.
Cheat-only (`isCheat: true`). It stays in the shipped mod; it is harmless.

## Acceptance

- Build clean; `AudibleHorn.dll` contains the resource (check with
  `GetManifestResourceNames` logging or a decompiler).
- In game, `hornsound` in the console plays the placeholder 5 m ahead. Walking
  away it fades and is silent at `HearingRange` metres; turning the camera
  pans it left and right.
- Setting the game's Effects slider to 0 silences it; setting `HornVolume` to
  0 in the `.cfg` silences it; 0.5 is audibly quieter than 1.
- Parent-following works: from the console with a `follow` transform of the
  local player (add a `hornsoundfollow` variant if needed for the test), the
  sound moves with the player.
- No errors on a dedicated server when the same DLL loads (nothing calls
  `Play` there, but `WavLoader` must not touch anything Unity-audio at load).
- `docs/DESIGN.md` has "Why not ZSFX" and "Finding the SFX mixer group".
