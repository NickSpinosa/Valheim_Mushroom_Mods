# Mushroom Mods

[![CI](https://github.com/NickSpinosa/Valheim_Mushroom_Mods/actions/workflows/ci.yml/badge.svg?branch=main)](https://github.com/NickSpinosa/Valheim_Mushroom_Mods/actions/workflows/ci.yml)

Mushroom mods are a small collection of utility and quality of life mods designed for large nomap servers

## Mods

| Mod | What it does |
|---|---|
| [Vegvísir Compass](vegvisir-compass/README.md) | Loot a limited-use compass from a Vegvísir that points you at its boss, instead of revealing it on the map |
| [Separate Spawns](Separate%20Spawns/README.md) | Splits players into groups at world creation, each with its own starting area and a private portal back to the world centre |
| [Haldor Expansion](haldor-expansion/README.md) | Adds five gathering materials to Haldor's stock, to relieve resource scarcity on a long-lived server |
| [Combat Adjustments](CombatAdustments/src/CombatAdjustments.ShieldRework/README.md) | Shield stagger, durability and tower block-armor rework |
| [Craftable Spawners](CraftableSpawners) | Craftable natural spawners |
| [Random Yggdrasil](RandomYggdrasil) | Randomises the Yggdrasil branch rotation per world, synced across the server |
| [Horn of Calling](HornOfCalling/README.md) | Craft a horn at the Workbench that sounds a blast other players hear out to 64 m |
| [Mushroom Sync](MushroomSync/README.md) | No gameplay of its own — shared server-authoritative sync, required by Combat Adjustments, Craftable Spawners, Haldor Expansion and Random Yggdrasil |

## Installing

Download **`MushroomMods-plugins.zip`** from the
[latest release](../../releases/latest) and extract it into your Valheim
`BepInEx/` directory. The zip holds a `plugins/` folder, so every mod lands in
`BepInEx/plugins/` in one step:

```
Valheim/
└── BepInEx/
    └── plugins/
        ├── CombatAdjustments.ShieldRework.dll
        ├── CraftableSpawners.dll
        ├── HaldorExpansion.dll
        ├── HornOfCalling.dll
        ├── MushroomSync.dll
        ├── RandomYggdrasil.dll
        ├── SeparateSpawns.dll
        └── VegvisirCompass.dll
```

Want only some of them? Extract the zip and delete the DLLs you do not want.
Keep `MushroomSync.dll` if you keep Combat Adjustments, Craftable Spawners,
Haldor Expansion or Random Yggdrasil — they depend on it and will not load
without it. Horn of Calling, Separate Spawns and Vegvísir Compass are
independent.

Most of these mods are server-authoritative, so install them on **every client
and on the dedicated server**; check each mod's own README.

### On Linux

Valheim's native Linux build runs these mods fine, but Steam does not launch
BepInEx for you — three things have to be right.

**1. Find the game folder.** Steam's default library puts it at:

```
~/.local/share/Steam/steamapps/common/Valheim
```

`~/.steam/steam/` is a symlink to the same place, so either path works. If the
game lives in a second library (an external drive, another partition), let Steam
tell you: **Steam → Valheim → Manage → Browse local files**. The folder is the
right one if it contains `valheim.x86_64` and, once BepInEx is extracted,
`start_game_bepinex.sh`.

Extract [BepInExPack_Valheim](https://thunderstore.io/c/valheim/p/denikson/BepInExPack_Valheim/)
into that folder, then unzip `MushroomMods-plugins.zip` and drop its `plugins`
folder into the `BepInEx/` directory the pack just created.

**2. Make the launch script executable.** The BepInEx zip does not preserve
the execute bit, and without it Steam's launch option fails silently:

```bash
cd ~/.local/share/Steam/steamapps/common/Valheim
chmod u+x start_game_bepinex.sh
```

**3. Add the script to Valheim's launch options.** **Steam → Valheim →
Properties → General → Launch Options**, and set:

```
./start_game_bepinex.sh %command%
```

While you are in Properties, go to **Compatibility** and **uncheck** "Force the
use of a specific Steam Play compatibility tool". Proton breaks doorstop
injection on the native build — the game starts, but no plugin loads.

**Check it worked.** `BepInEx/LogOutput.log` appears in the game folder after a
launch, and names each plugin it loads:

```bash
grep -iE "mushroom|vegvisir|spawns|haldor|shieldrework|hornofcalling|yggdrasil" \
  ~/.local/share/Steam/steamapps/common/Valheim/BepInEx/LogOutput.log
```

If the log never appears at all, run `./start_game_bepinex.sh` from a terminal —
Steam's container runtime swallows the error message.

No built DLL is committed to this repo; releases carry the artifacts.

### Turning the mods off

To play vanilla without uninstalling anything, rename the plugins folder:

```bash
cd ~/.local/share/Steam/steamapps/common/Valheim/BepInEx
mv plugins plugins_disabled
```

BepInEx only loads what it finds in `plugins/`, so anything under another name
is invisible to it — the launch option, the execute bit and BepInEx itself can
all stay exactly as they are. Any name works; `plugins_disabled` is just the
obvious one. Rename it back to turn the mods on again.

On Windows it is the same rename, in `BepInEx\plugins`.

None of this touches the server. These mods are mostly server-authoritative, so
a dedicated server that still has them keeps applying its own rules — disabling
locally only stops *your* client from loading them.

## Building and releasing

See [docs/devops.md](docs/devops.md).
