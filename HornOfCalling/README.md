# Horn of Calling

Adds a craftable **Horn of Calling** to the Workbench. Equip it and left click to sound
a blast. Its volume is a slider on the Audio tab of the game's settings menu.

> **Status: in progress.** The horn looks and sounds right, costs **1 Bronze + 1 Deer
> Hide** at a level 1 Workbench, and is heard by other players out to 64 m. What is left
> is the behaviour: see [Not done yet](#not-done-yet).

Plain BepInEx + Harmony, no extra runtime dependency — install `HornOfCalling.dll` into
`BepInEx/plugins/` and nothing else.

The mod adds an item and a recipe, so it needs to be on **every client and on the
dedicated server**.

## Building

```bash
dotnet build HornOfCalling/HornOfCalling.csproj -c Release
```

Defaults to the Linux Steam install (`~/.local/share/Steam/steamapps/common/Valheim`),
falling back to the default Windows path. Override it by creating
`Directory.Build.user.props` next to `Directory.Build.props` (gitignored):

```xml
<Project>
  <PropertyGroup>
    <ValheimPath>D:\Games\Valheim</ValheimPath>
  </PropertyGroup>
</Project>
```

A successful build copies the DLL straight into `BepInEx/plugins/`. There is no hot
reload — restart the game after each build.

The build errors early with a named message if `assembly_valheim.dll` or `BepInEx.dll`
can't be found, rather than emitting a wall of `CS0246`.

## How it works

Three registration steps, all idempotent, driven from Harmony postfixes in
[`src/Patches.cs`](src/Patches.cs):

| Step | Where | Why there |
|---|---|---|
| Clone the prefab, add to `ObjectDB.m_items` | `ObjectDB.Awake`, `ObjectDB.CopyOtherDB` | ObjectDB is built twice — a stripped main-menu copy, then the real world one |
| Add the `Recipe` | any of the three patches | Needs the Workbench, which is a *piece* and may not exist at the first `ObjectDB.Awake` |
| Add to `ZNetScene.m_prefabs` / `m_namedPrefabs` | `ZNetScene.Awake` | Lets the item and its sound effect exist as networked objects |

The item is cloned from the vanilla **Horn of Celebration** (prefab `TankardAnniversary`),
which supplies the horn model, the icon and the hold pose without an AssetBundle.

The clone needs three things undone. Its `m_ammoType` is `"mead"` — vanilla, the horn
drinks a mead from your inventory when you sound it, and refuses to sound at all without
one — so that is cleared. Its inherited weapon stats (knockback, backstab, block, parry)
are zeroed so the tooltip is just the flavour text. And its start effects, a mead splash
and a burp, are replaced by the blast.

The blast is not played by a patch. The clip — a 16-bit PCM WAV embedded in the
assembly, decoded in [`src/HornSound.cs`](src/HornSound.cs) — is hung off the item's
`m_startEffect`, which the game fires once per attack after the stamina check. That
also routes it through the SFX mixer group, so the player's volume slider applies.

The clip is peak-normalised to −0.3 dBFS, and that is the whole of the horn's loudness at
100%: Unity clamps `AudioSource.volume` at 1, so once the mod asks for full volume the
only headroom left is inside the recording.

The volume row in [`src/VolumeSlider.cs`](src/VolumeSlider.cs) is the vanilla SFX row
cloned: the `Slider` sits on the row GameObject with its caption and percentage as
children, so one `Instantiate` copies the whole thing. It is then retitled, spliced into
the tab's navigation chain, and pointed at the config entry.

## Settings

**Settings → Audio → Horn of Calling**, sitting under the three vanilla volume sliders.
It moves live while you drag it, saves on **OK** and is discarded on **Back**, the same as
the sliders above it.

The value is stored in `BepInEx/config/com.greg.hornofcalling.cfg`:

```ini
[Audio]
## How loud the horn blast is, as a fraction of the recorded level. [...]
# Setting type: Single
# Default value: 1
# Acceptable value range: From 0 to 1
BlastVolume = 1
```

Editing the file, or the F1 ConfigurationManager overlay if you have it, applies without
a restart — the settings row is a front end for this entry, not a separate setting.

It multiplies Valheim's own sound-effects volume rather than replacing it, and it is
**per player**: it changes every blast *you* hear, whoever sounded it, and never what
anyone else hears. That is why it is not synced through MushroomSync — see
[docs/CONTEXT.md](docs/CONTEXT.md).

The horn sounds once as a preview when you stop moving the slider — on releasing the
handle, or a third of a second after the last arrow-key or stick nudge — at the level
you have just chosen, through the same mixer group as the real blast. Moving it again
restarts the preview rather than layering a second copy over it, and closing the menu
stops it.

Opened from the **main menu** the slider still works, but is silent: the blast prefab is
built from the world's `ObjectDB`, so there is nothing to preview until a world is
loaded.

## Not done yet

From the design note, still open:

- **10 stamina** per use — `m_attack.m_attackStamina`, currently `0`.
- The **roar emote** instead of the inherited `emote_drink`.
- The viking should **appear to hold nothing**.
- Reaching **other players beyond the audible falloff** (64 m). Other players *do* hear
  the blast today, out to 64 m — see [Range](#range) — but the design note asks for
  200 m. Since Valheim 1.0.7 the zone grid delivers the blast further than the curve
  carries it (128 m at the default simulation distance), so part of that gap is now a
  curve-tuning change rather than networking work; the rest still needs a `ZRoutedRpc`
  broadcast.
- **Hold** left click to sustain the sound, release to stop. It is one-shot per click.

## Range

Other players hear the horn. The effect prefab carries a `ZNetView`, so spawning it
creates a ZDO that replicates to nearby peers, whose `ZNetScene` resolves it by hash and
plays it locally — which is why the effect prefab is registered with `ZNetScene`
alongside the item.

Two independent limits apply, and the smaller one wins:

| Limit | Value | Set by |
|---|---|---|
| Audible falloff | 64 m | the custom rolloff curve in [`src/HornSound.cs`](src/HornSound.cs) |
| ZDO replication | 64 m guaranteed, 128 m at the default setting | the listener's **simulation distance** |

The falloff is a plateau curve, tuned in the `Falloff` table:

| Distance | Volume |
|---|---|
| 0 – 15 m | 100% |
| 15 – 25 m | 82% |
| 25 – 35 m | 64% |
| 35 – 45 m | 46% |
| 45 – 64 m | 28% |

Replication is the listener's own **Simulation distance** graphics setting, clamped by
the server's, not a fixed radius. Its lowest level (`0`) is the pre-1.0.7 one-ring
square and reaches 64 m; the default level `2` reaches 128 m, and the levels above it
further still. The falloff curve is tuned to 64 m because that is the floor — the only
reach every listener is guaranteed to have.

So raising the audible range past a listener's replication radius does nothing: they
never receive the object, and no volume setting reaches them. Raising it *within* that
radius would work, and since 1.0.7 there is room to — see
[`docs/CONTEXT.md`](docs/CONTEXT.md) for the mechanism and why the constant was left at
the floor.

## Local setup

BepInEx must be installed into the game folder before the plugin can load. On the
**native Linux** build:

1. Extract [BepInExPack_Valheim](https://thunderstore.io/c/valheim/p/denikson/BepInExPack_Valheim/)
   (5.4.2333) into the Valheim directory, then `chmod u+x start_game_bepinex.sh`.
2. Steam → Valheim → Properties → Compatibility: **uncheck** "Force the use of a
   specific Steam Play compatibility tool". Proton breaks doorstop injection on the
   native build.
3. Steam → Valheim → Properties → Launch Options: `./start_game_bepinex.sh %command%`

BepInEx is up if `BepInEx/LogOutput.log` appears after a launch. If it never does, run
`start_game_bepinex.sh` from a terminal — the Steam container runtime hides the error.

## Testing

New world → `F5` → `devcommands` → `spawn HornOfCalling 1 1 p`. Then stand at a
Workbench and confirm the recipe appears for 1 Bronze + 1 Deer Hide. Equip the horn and
left click — the blast is ~4.9 s.

`spawn Bronze 1` and `spawn DeerHide 1` put the materials in reach for a quick check.

For the volume slider: **Esc → Settings → Audio**, check the row reads "Horn of Calling"
and shows a percentage, drag it, press OK, and confirm `BlastVolume` in the config file
changed. Releasing the handle should sound the horn once at the new level, and dragging
again should cut the previous preview off rather than stack on it. Walking the list with
the arrow keys or a gamepad should stop on the row and preview a third of a second after
you stop nudging — if the row is skipped entirely, the navigation splice is what to look
at. Pressing Back after a drag should leave the config untouched and the next blast at the
old level.

The prefab name is case-sensitive and is *not* the display name: `spawn "Horn of Calling"`
will not work.

The log names each registration step as it happens, so a partial failure is visible:

```bash
grep -i hornofcalling "$VALHEIM/BepInEx/LogOutput.log"
```

Unity-side stack traces land in `~/.config/unity3d/IronGate/Valheim/Player.log`.

## Notes

See [docs/CONTEXT.md](docs/CONTEXT.md) for the API traps behind the design above.
