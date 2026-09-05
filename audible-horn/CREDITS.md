**Pending: conversion not yet run** — the recording below is approved but has not
been downloaded and converted yet, so `Assets/horn.wav` is still the synthesised
placeholder. Everything else on this page describes the file that lands the
moment the [command](#conversion) at the bottom is run; delete this paragraph
then.

# Credits

Audible Horn's own code is MIT, like the rest of this repo. The horn recording it
ships is not: it is third-party audio under a licence that requires attribution,
and this page is that attribution.

## The horn recording

| | |
|---|---|
| **Title** | Hunting Horn.wav |
| **Author** | Benboncan |
| **Source** | https://freesound.org/people/Benboncan/sounds/72753/ |
| **Licence** | Creative Commons Attribution 4.0 International (CC BY 4.0) |
| **Licence text** | https://creativecommons.org/licenses/by/4.0/ |

The original is 4.674 s, WAV, 44.1 kHz, 16-bit, stereo, about 805 KB.

CC BY 4.0 permits commercial use and derivative works, and requires credit to the
author, a link to the licence, and a note of the changes made. The changes are
listed below; this file travels with the mod, and the README links to it, so the
credit is carried wherever the DLL goes.

## What was changed

`Assets/horn.wav` is a modified version of that recording, produced by
[`tools/convert-horn.ps1`](tools/convert-horn.ps1):

- **Mixed down to mono.** The original is stereo. A Horn Call is spatialised as a
  point source, so its own stereo image would only fight the game's panning — and
  mono halves the size of an embedded resource.
- **No resampling.** The original is already 44.1 kHz, which is the target rate,
  so the converter's resampler does not run.
- **Silence trimmed from the head and the tail**, at a −50 dBFS threshold, with
  20 ms kept in front of the onset. The horn has to start on the key press, not a
  moment after it.
- **15 ms fade-in and 150 ms fade-out**, so neither end of the clip starts or
  stops on a non-zero sample and clicks.
- **Peak normalised to −1 dBFS.** A Listener judges distance from loudness, so
  the clip's own level has to be predictable; the headroom keeps it from clipping
  once the mixer and the Horn Volume multiplier have had their turn.
- **Capped at 4.0 s.** The trimmed clip is cut at `-MaxSeconds` if it is longer,
  with the fade-out landing on the cut.

The result is a canonical 44-byte-header 16-bit PCM WAV, mono, 44.1 kHz — the one
format `src/Audio/WavLoader.cs` accepts.

## Conversion

`ffmpeg` is not installed on the build machine and the ticket's ffmpeg one-liner
was therefore not used. `tools/convert-horn.ps1` does the same work in pure .NET
so the conversion is reproducible from the untouched download. Run from the
repository root, with the download saved as
`audible-horn/Assets/hunting-horn-source.wav`:

```
powershell -NoProfile -ExecutionPolicy Bypass -File audible-horn/tools/convert-horn.ps1 -Source audible-horn/Assets/hunting-horn-source.wav
```

`-Destination` defaults to `audible-horn/Assets/horn.wav`, overwriting the
placeholder.

**The source file is deleted afterwards and is never committed.** Only the
converted `horn.wav` ships: `AudibleHorn.csproj` embeds `Assets\**\*.wav`, so a
stray original left in that folder would be compiled into the DLL as a second,
unused, 805 KB resource.

## The placeholder it replaced

The synthesised tone that shipped before this recording came from
[`tools/make-placeholder-horn.ps1`](tools/make-placeholder-horn.ps1), which stays
in the repo. It is original to this project, needs no attribution, and is still
the fastest way to get a valid `horn.wav` back if one is ever needed.
