# Vegvísir Compass — context

Why this mod is built the way it is. The user-facing design lives in the
[README](../README.md); this file is for the reasoning and the traps.

## Replacing a vanilla method makes you responsible for all of it

`VegvisirInteractPatch` is a prefix returning `false`, so vanilla
`Vegvisir.Interact` never runs. That is deliberate and cannot be softened: the
vanilla path ends in `Minimap.DiscoverLocation`, which calls `AddPin(save: true)`
**unconditionally** — even when the location's `m_showMap` is false. On a no-map
server that would write permanent boss pins into every player's save. There is no
flag that turns it off, so the only way to guarantee no pin is to not call it.

The cost is that everything else in that method becomes ours to reproduce, and
the map reveal is not all a stone does:

```csharp
if (!string.IsNullOrEmpty(m_setsGlobalKey))
    ZoneSystem.instance.SetGlobalKey(m_setsGlobalKey);
if (!string.IsNullOrEmpty(m_setsPlayerKey) && character is Player player)
    player.AddUniqueKey(m_setsPlayerKey);
```

Those are progression keys. They have nothing to do with the map, and skipping
vanilla silently swallowed them — issue #13, live from the first release until
2026-09-09. `ApplyStoneKeys` replicates them.

Two details worth keeping:

- **They run on every path out of the prefix**, including the one where the player
  already carries every compass this stone offers. Vanilla sets them once the
  `hold` check passes, regardless of what the locations do, so gating them on a
  successful compass grant would be a subtler version of the same bug.
- **Both are idempotent**, so re-reading a stone costs nothing. `SetGlobalKey`
  routes to the server and lands in the world save; `AddUniqueKey` is
  per-character.

The general lesson: when a prefix returns `false`, diff the whole vanilla method
against what the patch does, not just the part being replaced. Re-check it on
every game update — a new side effect added to a method we skip is invisible,
because nothing fails.

## Vanilla's reach is asked for, never reconstructed

`MerchantPlacement.IsWithinVanillaRange` calls `ZNetScene.InActiveArea` rather
than comparing zone coordinates. It used to do the arithmetic itself, mirroring
`ZoneSystem.CreateGhostZones` with `m_activeArea + m_activeDistantArea`.

Valheim 1.0 removed those fields. The reach now depends on a server-synced,
player-configurable `SimulationDistance` whose shape is not a square of zones —
at near distance 2 it clips the corners to a 1.75-zone radius. Any arithmetic of
our own would be a second copy of that, free to drift on the next update.

Related: zone keys are `Vector2s` in 1.0, not `Vector2i`.

## Proximity comes from peers, not from Player.GetAllPlayers

A dedicated server only loads zones around **its own** reference position. For
clients it merely generates ghost zones. So no `Player` object exists for a
remote client, `Player.GetAllPlayers()` is empty on a headless server, and
`IsZoneLoaded` is false for the area around a player.

An earlier version of the placement system used both. Every camp therefore read
as abandoned on every tick, which un-flagged `m_placed` while the merchant went
on standing there — the camp objects are ZDOs that outlive the zone unloading.
That let the next candidate place too, and the result was a merchant permanently
spawned at every candidate camp any player had walked past.

Proximity is now taken from `ZNet.instance.GetPeers()` and each peer's
`GetRefPos()`, plus the server's own reference position.

## Locations are placed exactly once, ever

The obvious way to defer a merchant camp is to hold `ZoneSystem.PlaceLocations`
back until the player has chosen. It is a trap, and an expensive one.

`SpawnZone` calls `PlaceLocations` only when a zone has never been generated, and
then calls `SetZoneGenerated` regardless of what happened inside. A zone whose
placement was skipped is marked generated with nothing in it, permanently, and
that candidate site can never host the merchant again.

`LocationInstance.m_placed` is not a spawn switch either — it is bookkeeping
written during generation. Clearing it removes nothing.

So placement is left alone entirely and the **trader** is managed instead: every
candidate camp places normally, and the merchant standing in it is spawned and
despawned by proximity. Opening the trade UI settles the site.

## Lock-in has to run on the server

`StoreGui.Show` is client-side UI, and a client owns none of the rival traders'
ZDOs — `ZDOMan.DestroyZDO` acts only on ZDOs the caller owns. So the client sends
`VC_LockMerchant` and the server does the work, claiming ownership of each ZDO
before destroying it.

## The config is deliberately small

Cut from fourteen settings to five in 1.6.0. The mod is server-authoritative by
nature, so every setting layered on top was a hole that then needed plugging.

Two were live exploits rather than options. `ReplaceVegvisirBehaviour` turned the
mod off — a client setting it `false` got vanilla map pins on a no-map server.
`MerchantLorestones` was read only on the client, so a server setting it `false`
was overruled by any client setting it `true`.

Neither needed enforcement or config sync; deleting them removed the exploit. The
three settings that remain and matter (`LootCooldownSeconds`, `UsesPerCompass`,
`RangeMeters`) are read **only** inside `OnServerRequest` behind an `IsServer()`
guard and baked into each compass as it is granted, so a client copy is never
consulted. This mod does not use MushroomSync because there is nothing left to
sync.
