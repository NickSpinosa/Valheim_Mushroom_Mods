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
| [Mushroom Sync](MushroomSync/README.md) | No gameplay of its own — shared server-authoritative sync, required by the four mods above |

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
        ├── MushroomSync.dll          <- required by the four above
        ├── RandomYggdrasil.dll
        ├── SeparateSpawns.dll
        └── VegvisirCompass.dll
```

Want only some of them? Extract the zip and delete the DLLs you do not want.
Keep `MushroomSync.dll` if you keep Combat Adjustments, Craftable Spawners,
Haldor Expansion or Random Yggdrasil — they depend on it and will not load
without it. Separate Spawns and Vegvísir Compass are independent.

Most of these mods are server-authoritative, so install them on **every client
and on the dedicated server**; check each mod's own README.

No built DLL is committed to this repo; releases carry the artifacts.

## Building and releasing

See [docs/devops.md](docs/devops.md).
