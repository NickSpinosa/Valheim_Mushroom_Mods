# Haldor Expansion

Private Valheim mod. Adds five gathering materials to Haldor's stock to relieve
resource scarcity on a long-lived dedicated server.

See [DESIGN.md](docs/DESIGN.md) for the full design and the reasoning behind each decision.

## Build

1. `cp Local.props.example Local.props` and set `ValheimDir` to your install.
2. `dotnet build`

The build copies `HaldorExpansion.dll` into `<ValheimDir>\BepInEx\plugins`.
Set `CopyToPlugins` to `false` in `Local.props` to skip that.

**Also needs `MushroomSync.dll`** in `BepInEx/plugins`, the shared
server-authoritative sync plugin. This mod declares a `[BepInDependency]` on it and
**will not load without it**. `dotnet build` builds it, but only `HaldorExpansion.dll`
is auto-copied — take `MushroomSync/bin/Release/MushroomSync.dll` across yourself, or
use the release's `MushroomMods-plugins.zip`, which carries both.

## Status: unverified

Prices, the Ashlands global key spelling, and the prefab IDs are **provisional**.
Launch the game once and load a world; the mod writes a `HALDOR EXPANSION ::
VERIFICATION DUMP` block to `BepInEx\LogOutput.log` the first time any trader's
stock is queried. That dump resolves all four open questions:

- the real global key list (the Ashlands key is not a string literal in the game
  assembly, so its spelling cannot be read out of the binary)
- vanilla Haldor's actual prices, to re-anchor the table
- correct prefab spellings for all five items
- each item's max stack size vs. our configured stack

Bake the real values into `src/TradeTable.cs` afterwards.

## Configuration

`BepInEx/config/nicks.haldorexpansion.cfg` (created on first launch):

```
[Server]
LockConfiguration = true

[Items.Wood]
Enabled = true
Cost = 1
UnlockBoss = Elder

[Items.Stone]
Enabled = true
Cost = 1
UnlockBoss = Elder

[Items.Grausten]
Enabled = true
Cost = 2
UnlockBoss = Queen

[Items.Blackwood]
Enabled = true
Cost = 2
UnlockBoss = Queen

[Items.SurtlingCore]
Enabled = true
Cost = 100
UnlockBoss = Bonemass
```

`Cost` is coins **per unit**. One purchase still delivers the baked stack (50 wood,
5 surtling cores, …), so the coins charged are `Cost × stack`. Disable an item with
`Enabled = false`.

`UnlockBoss` is the boss that must already be defeated on this world before the
item appears. Allowed values: `None`, `Eikthyr`, `Elder`, `Bonemass`, `Moder`,
`Yagluth`, `Queen`, `Fader`. `None` means always in stock.

When `LockConfiguration` is on (the default) and this plugin is also on the server,
joining clients use the server's Enabled / Cost / UnlockBoss values. Their local
`.cfg` is left alone and comes back into effect after disconnect. The plugin logs a
`Trade table hash` line at startup and again after a client sync; if two people see
different hashes after that, they are not looking at the same stock.
