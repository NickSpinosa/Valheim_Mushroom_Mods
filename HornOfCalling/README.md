# Horn of Calling

Adds a craftable **Horn of Calling** to the Workbench. Equip it and left click to sound
a blast.

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

## Not done yet

From the design note, still open:

- **10 stamina** per use — `m_attack.m_attackStamina`, currently `0`.
- The **roar emote** instead of the inherited `emote_drink`.
- The viking should **appear to hold nothing**.
- Reaching **other players beyond the ZDO active area** (~64 m). Other players *do*
  hear the blast today, out to 64 m — see [Range](#range) — but the design note's 200 m
  is past what the zone grid delivers and needs a `ZRoutedRpc` broadcast.
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
| ZDO replication | ~64 m guaranteed | `ZoneSystem.m_activeArea` (1) × `m_zoneSize` (64 m) |

The falloff is a plateau curve, tuned in the `Falloff` table:

| Distance | Volume |
|---|---|
| 0 – 15 m | 100% |
| 15 – 25 m | 82% |
| 25 – 35 m | 64% |
| 35 – 45 m | 46% |
| 45 – 64 m | 28% |

Raising the audible range past ~64 m does nothing on its own: peers outside their active
area never receive the object, so no volume setting reaches them.

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

The prefab name is case-sensitive and is *not* the display name: `spawn "Horn of Calling"`
will not work.

The log names each registration step as it happens, so a partial failure is visible:

```bash
grep -i hornofcalling "$VALHEIM/BepInEx/LogOutput.log"
```

Unity-side stack traces land in `~/.config/unity3d/IronGate/Valheim/Player.log`.

## Notes

See [docs/CONTEXT.md](docs/CONTEXT.md) for the API traps behind the design above.
