# 06 — Horn Volume slider in the vanilla audio settings

**Goal:** the Settings → Audio tab gains a "Signal Horn" slider beneath the
Effects and Music sliders. Moving it changes `HornVolume`; OK saves it, Back
reverts it. It looks like it belongs there.

**Depends on:** 01. **Unblocks:** 08.

## Facts you need (from `assembly_valheim.dll`)

`AudioSettings : MonoBehaviour` is the Audio tab. Private fields:

| Field | Type |
|---|---|
| `m_volumeSlider`, `m_volumeText` | `Slider`, `TMP_Text` (master) |
| `m_sfxVolumeSlider`, `m_sfxVolumeText` | `Slider`, `TMP_Text` |
| `m_musicVolumeSlider`, `m_musicVolumeText` | `Slider`, `TMP_Text` |
| `m_continousMusic` | `Toggle` |
| `m_masterMixer` | `AudioMixer` |
| `m_oldVolume`, `m_oldSfxVolume`, `m_oldMusicVolume` | `float` (the revert values) |

Public methods: `Initialize()`, `OnTabOpen(...)` (2 params), `OnOkAsync(...)`
(1 param), `OnBack()`, `OnAudioChanged()`. The pattern is: `Initialize`
loads the current values into the sliders and stores `m_old*`,
`OnAudioChanged` is the sliders' change callback that applies live,
`OnOkAsync` persists, `OnBack` restores `m_old*`. Mirror that lifecycle
exactly for the new slider so it feels native.

The row layout (which parent, whether the label is a sibling or a child of
the slider) is only knowable at runtime. **Inspect it first**: in a postfix
on `Initialize`, log `m_musicVolumeSlider.transform.parent` and its children
names once, then design the clone around what you see. Write what you found
in `docs/DESIGN.md` under "Audio settings row hierarchy" so the next person
does not have to.

Read private fields with `AccessTools.FieldRefAccess<AudioSettings, Slider>("m_musicVolumeSlider")`
and friends. No publicizer.

## Deliverables

### `src/Patches/AudioSettingsPatch.cs`

- **`Initialize` postfix.** If our slider already exists on this instance
  (keep a per-instance reference in a `ConditionalWeakTable` or find by name
  `"SignalHornVolumeRow"`), just refresh its value. Otherwise:
  1. Clone the music slider's row (the smallest ancestor that contains both
     the slider and its label text, per your inspection) with
     `Object.Instantiate(row, row.parent)`, name it `SignalHornVolumeRow`, and
     place it immediately after the music row with `SetSiblingIndex`.
  2. In the clone, set the label `TMP_Text` to `"Signal Horn"` and the value
     text to the percentage the vanilla rows use (check how `m_musicVolumeText`
     is formatted in `OnAudioChanged`; match it).
  3. Set the slider's `minValue = 0`, `maxValue = 1`, `wholeNumbers = false`,
     `value = Plugin.Settings.HornVolume.Value`. Remove the cloned
     `onValueChanged` persistent listeners (`RemoveAllListeners` clears only
     runtime listeners; for persistent ones set
     `slider.onValueChanged = new Slider.SliderEvent()`), then add ours.
  4. Store the revert value `_oldHornVolume = HornVolume.Value`.
  5. If the tab uses a layout group, nothing else is needed; if rows are
     absolutely positioned, shift the clone by the music row's offset from the
     SFX row. Your inspection decides.
- **Our change listener:** `HornVolume.Value = v` and update the value text.
  Setting `.Value` on a BepInEx `ConfigEntry` writes through to the file when
  `SaveOnConfigSet` is on (default), so live apply and persist are one step.
- **`OnBack` postfix:** `HornVolume.Value = _oldHornVolume`.
- **`OnOkAsync` postfix:** `_oldHornVolume = HornVolume.Value` (so a later
  Back inside the same open panel does not revert past the OK).
- **`OnTabOpen` postfix:** refresh the slider from `HornVolume.Value`, in case
  the `.cfg` was edited while the game ran (MushroomSync's `WatchForChanges`
  reloads it).
- Every body in `try/catch` with `LogError`; a UI failure must never break the
  settings panel.
- Guard for the dedicated server: `AudioSettings` never exists there, so the
  patch simply never fires. No special code, but do not touch UI types in any
  static initialiser that runs on load.

### Gamepad

The Settings panel is navigable with a controller. After cloning, the slider
needs to be in the navigation chain: set `Navigation` on the cloned slider to
`Automatic` if the vanilla ones use it, or explicitly link `selectOnUp` /
`selectOnDown` between music → horn → whatever followed music. Check the
vanilla `Navigation.mode` during inspection.

## Acceptance

- Settings → Audio shows a fourth slider labelled "Signal Horn" under Music,
  visually matching the others (same width, same value-text style).
- Dragging it and pressing OK: `HornVolume` in
  `BepInEx/config/mushroom.audiblehorn.cfg` changes to the new value. Firing
  the `hornsound` debug command afterwards is louder or quieter accordingly.
- Dragging it and pressing Back: the value on screen and in the `.cfg` return
  to what they were.
- Open the panel twice in one session: exactly one Signal Horn row, not two.
- Main-menu Settings and in-game Settings both show it (both instantiate
  `AudioSettings`).
- Gamepad navigation reaches the slider and leaves it downward.
- No log errors; no change to Effects or Music behaviour.
- `docs/DESIGN.md` has "Audio settings row hierarchy".
