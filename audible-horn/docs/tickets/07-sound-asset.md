# 07 — Source the horn recording

**Goal:** replace the synthesised placeholder from ticket 03 with a real horn
recording that the mod may legally ship, credited in the README.

**Depends on:** nothing to start; lands on top of 03. **Needs maintainer
approval** of the chosen clip before it is committed. Present two or three
candidates with links and let the maintainer pick.

## Requirements for the clip

- **Licence:** CC0 / public domain, or CC-BY with attribution acceptable to
  put in the README. Nothing non-commercial-only, nothing share-alike, nothing
  from a game or film rip. Record the exact licence text link.
- **Sound:** a single sustained blast of a natural horn, lur, or bugle-like
  instrument. One note or a short rising call. No reverb tail longer than
  about a second (the game adds its own space). No music, no crowd, no
  Viking-movie foley. It has to read as "a person blew a horn over there",
  not as a monster.
- **Length:** 1.5 to 4 seconds including release.
- **Format on disk:** WAV, 16-bit PCM, **mono**, 44.1 kHz (ticket 03's loader
  handles stereo, but mono halves the size and spatialisation wants a point
  source anyway). Under 400 KB. Normalise peaks to about -1 dBFS; trim silence
  from the head so the sound starts on the key press.
- **Path:** `audible-horn/Assets/horn.wav`, overwriting the placeholder.

## Where to look

Search freesound.org (filter licence to CC0), the Internet Archive, and
Wikimedia Commons for terms like "horn blast", "lur", "bugle call",
"hunting horn", "viking horn". Skip anything whose licence page you cannot
open and read.

## Deliverables

1. The WAV at `Assets/horn.wav`.
2. `audible-horn/CREDITS.md` with: title, author, source URL, licence name,
   licence URL, and what you changed (trim, mono mix-down, normalise).
3. A "Credits" section in `audible-horn/README.md` that links to it (ticket 08
   writes the README; if 07 lands first, add the section to the stub).
4. Conversion done with `ffmpeg` if it is on the machine
   (`ffmpeg -i in.ext -ac 1 -ar 44100 -sample_fmt s16 -af "silenceremove=start_periods=1:start_threshold=-50dB" horn.wav`);
   otherwise any tool that produces plain PCM WAV. Note the command used in
   `CREDITS.md`.

## Acceptance

- `hornsound` debug command plays the real clip with no click at the start and
  no truncation at the end.
- `WavLoader` logs no warnings for the file.
- The maintainer has approved the clip in chat, and `CREDITS.md` names the
  licence.
- Git: the file is committed (WAV is not blocked by `.gitignore`; check that
  it really is tracked with `git ls-files audible-horn/Assets`).
