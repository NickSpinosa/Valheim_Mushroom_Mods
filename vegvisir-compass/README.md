# Vegvísir Compass

A BepInEx mod for [Valheim](https://www.valheimgame.com/), built for **no-map
multiplayer playthroughs**.

Interacting with a Vegvísir no longer reveals the boss on your map. Instead you
loot a **Vegvísir Compass** — a limited-use item that swings your view toward
the boss that stone points at, then crumbles once it is spent.

## What it does

- Interacting with a Vegvísir grants a **Vegvísir Compass** instead of revealing
  a map location.
- Each compass is **named for its target** — *Vegvisir Compass - Moder* — so you
  can tell at a glance which boss it points at.
- Stones can optionally go on a **shared cooldown** afterwards, applying to every
  player on the server. Off by default.
- A player may carry **only one compass per target** — holding one for The Elder
  does not stop you looting one for The Queen, but a second Elder compass is
  refused. Targets are matched by location, not by the displayed name, so stones
  sharing a label (such as the Ashlands Dyrnwyn chain) never block each other.
  The rule applies to **picking one up off the ground** as well as looting, so a
  compass cannot be duplicated by dropping it first. Compasses are not swept up by
  auto-pickup; press use to take one.
- Using the compass points the camera at the boss. It has **a single use** by default,
  and is destroyed once spent.
- A compass only works **within 350m of the Vegvísir it came from**, measured on
  the X/Z plane so height is ignored. Out of range costs nothing: no use is
  spent and the camera does not move.
- Aiming turns you to face the boss and levels the view on the horizon, rather
  than tilting up a mountain or down into a valley.
- A **1 second guard** stops a double-keypress burning two uses at once.
- A stone naming several places grants **one compass for each** — Hildir's map
  table is a Vegvísir carrying all three of her quest dungeons. Targets you
  already carry are skipped rather than refusing the whole stone.
- **Merchant lorestones**: biome runestones far enough from the world centre also
  grant a compass — Black Forest → Haldor, Meadows and Plains → Hildir, Swamp →
  the Bog Witch. The stone still shows its lore text.
- **Hildir's map table** grants compasses for her three quest dungeons, named
  Brass, Silver and Bronze.
- Merchants **settle where you trade with them**, not at the first camp anyone
  walks past. New worlds only.
- Icons are **coloured by what they point at**, so a full pack stays readable at
  a glance: gold for bosses, grey for merchants, red for Ashlands Mysterious
  Locations, purple for Hildir's quest dungeons.

The map is never touched. See [No map, really](#no-map-really) for why that
needed care.

## Requirements

| | |
|---|---|
| Valheim | Unity `6000.0.61f1`, build `21981559` |
| BepInEx | `5.4.2333` ([BepInExPack_Valheim](https://thunderstore.io/c/valheim/p/denikson/BepInExPack_Valheim/)) |
| .NET SDK | 9.0 — to build only; players need nothing beyond BepInEx |

No Jotunn, no ServerSync, no other plugin dependencies.

**Everyone needs it.** The compass is a real item prefab, so the mod must be
installed on the dedicated server *and* on every connecting client.

## Installing

Releases ship every Mushroom mod together. Download
**`MushroomMods-plugins.zip`** from the
[latest release](../../releases/latest), take `VegvisirCompass.dll` out of its
`plugins/` folder, and drop it into `BepInEx/plugins/`:

```
Valheim/
└── BepInEx/
    └── plugins/
        └── VegvisirCompass.dll
```

Do this on **every client and on the dedicated server**. For the server that is
its own Valheim directory — the folder holding `valheim_server.exe` and its
`BepInEx/`.

Mod managers such as r2modman and Vortex will also take a bare DLL through their
*Import local mod* / manual-install flow.

### Afterwards

The config file appears on first launch at
`BepInEx/config/vegvisircompass.cfg`.

> **Coming from 1.4.1 or earlier?** The file used to be named after the plugin
> GUID, `com.dhobbs.vegvisircompass.cfg`. It is renamed automatically on first
> launch and your settings come with it — nothing to do. Both the client and the
> dedicated server have their own copy, so both get renamed.

> **Upgrading?** BepInEx never overwrites a setting already present in your
> config file, so new defaults do not reach an existing install. If a release
> changes a default you want, edit that line yourself or delete the file and let
> it regenerate.

Remember the mod has to be on **the server and every client** — the compass is a
real item prefab, so a client without it will not have the item.

## Configuration

Five settings, deliberately. Anything that only ever had one sensible value is a
constant in the code, and anything whose *off* setting merely disabled the mod is
gone — see [Why the config is small](#why-the-config-is-small).

| Setting | Default | Decided by | Notes |
|---|---|---|---|
| `Vegvisir.LootCooldownSeconds` | `0` | **Server** | Shared re-loot cooldown, in seconds. `0` disables it, and the server then tracks nothing at all |
| `Compass.UsesPerCompass` | `1` | **Server** | How many times a compass works before it crumbles |
| `Compass.RangeMeters` | `350` | **Server** | How far from its stone a compass still works, X/Z only. `0` removes the limit |
| `Compass.LookSmoothing` | `3.5` | Client | Seconds the camera takes to pan, matching the vanilla Vegvísir. Mouse look is held off for the pan — vanilla applies an eased turn and mouse input to the same field, so without that the pan is cancelled the moment the mouse moves. The lockout is capped at 6s and expires on a deadline, so an interrupted pan cannot leave you without camera control. `0` turns instantly, with no lockout |
| `Debug.VerboseLogging` | `false` | Either | Logs looting, RPCs, placement, aiming and use for the side it is set on |

The three server settings are read **only** inside `OnServerRequest`, behind an
`IsServer()` guard, and their values are baked into each compass as it is
granted. A client editing their own copy changes nothing — there is no config
sync because none of these are ever read on a client.

The other two cannot be abused. `LookSmoothing` is personal taste, and
`VerboseLogging` writes to a log.

## Building

```
dotnet build -c Release
```

Game assemblies are referenced directly from the local Steam install rather than
vendored, so no copyrighted binaries are committed. The path comes from
`ValheimPath` and defaults to
`C:\Program Files (x86)\Steam\steamapps\common\Valheim`; override it per-machine
by creating `Directory.Build.user.props` (gitignored):

```xml
<Project>
  <PropertyGroup>
    <ValheimPath>D:\Games\Valheim</ValheimPath>
  </PropertyGroup>
</Project>
```

Every build deploys the output into `BepInEx/plugins` so it is immediately
testable. Valheim holds the DLL open while running, so a mid-session rebuild
warns rather than failing — close the game and rebuild to deploy.

No built DLL is committed. Publishing a GitHub release runs
[`.github/workflows/release.yml`](../.github/workflows/release.yml), which builds
every mod in the repo against reference assemblies it fetches itself and attaches
the DLLs to that release — so a downloaded plugin always corresponds to a tagged
commit.

## How it works

### Why the config is small

The mod is server-authoritative by nature: the server resolves locations, decides
uses and range, bakes them into the item, and owns merchant placement. The client
asks and renders. Every setting exposed on top of that is a hole someone then has
to plug — so the config was cut from fourteen settings to five.

Two of them were live exploits rather than options:

- **`ReplaceVegvisirBehaviour`** turned the mod off. A client setting it `false`
  fell through to vanilla `Vegvisir.Interact`, which writes a permanent boss pin —
  on a server whose entire point is that no pin is ever written.
- **`MerchantLorestones`** was checked only on the client. A server that set it
  `false` was overruled by any client that set it `true`, because
  `OnServerRequest` never looked at it.

Neither needed enforcement or config sync. Deleting them removed the exploit, and
the one legitimate use of the second — keeping merchants a discovery challenge —
is already served better by the distance gates.

The rest were noise: three icon tints nobody retunes (and which only work if
everyone agrees on them), a spam-guard interval with nothing to gain from tuning,
and an `Item.*` trio that was actively hostile — `DisplayName` is what
`IsCompass` matches on, so changing it orphaned every compass a player held.
`Item.Description` used to state the range in metres and had to be hand-edited to
stay true; it no longer names a number, so it cannot go stale.

### No map, really

The obvious implementation is to re-fire the vanilla Vegvísir trigger, which
already aims the camera. It is a trap. That path ends in
`Minimap.DiscoverLocation`, which calls `AddPin(save: true)` **unconditionally**
— even when `showMap` is false. Boss pins would be written permanently into
every player's save, so a no-map character would quietly accumulate a full set of
boss markers, visible the moment the map was ever enabled.

So the mod uses its own RPC pair and never calls into `Minimap` at all.

### Why looting needs the server

`ZoneSystem.m_locationInstances` is only populated on the server, so clients
cannot resolve where a boss actually is. Vanilla round-trips for the same reason.

On loot, the client asks the server, which enforces the cooldown, resolves every
location the stone lists, and returns one grant for each. That position
is **baked into the item's custom data**, so *using* the compass later is
entirely local — no further server contact.

### Finding a merchant who has moved in

Vanilla treats merchant camps as **unique**: the moment one is placed it deletes
every other candidate for that trader. Since a camp is placed simply by walking
within range, where your merchant lives is decided by the first site anyone
happens to pass — usually long before it mattered to anyone.

The obvious way to defer that is to hold the camp back from being placed. It is a
trap, and worth writing down. `ZoneSystem.SpawnZone` calls `PlaceLocations` only
when a zone has never been generated, then calls `SetZoneGenerated` regardless of
what happened inside — so a zone whose placement was skipped is marked generated
and empty, **permanently**, and that candidate can never host the merchant again.
Nor is `LocationInstance.m_placed` a spawn switch: it is bookkeeping written
during generation, and clearing it removes nothing, because the camp's objects
already exist as ZDOs that outlive the zone unloading.

So placement is left completely alone and the **trader** is managed instead. Every
candidate camp places normally. The merchant standing in it is spawned when a
player comes within vanilla's own range and removed again when they leave, so
several provisional merchants can exist at once and none of them is a commitment.
**Opening the trader's shop settles the site**: every other trader is destroyed
for good and vanilla's cleanup is finally allowed to clear the spare candidates.

Proximity is asked of the game — `ZNetScene.InActiveArea(campPosition, refPos)`,
once for the host's reference position and once per peer — rather than measured in
metres, so "vanilla range" stays whatever vanilla says it is. It deliberately does
not use `IsZoneLoaded` or `Player.GetAllPlayers`: a dedicated server only loads
zones around its own reference position and never instantiates a Player for a
remote client, so both are blind to where anyone actually is.

Reconstructing that reach by hand is the trap, and Valheim 1.0 turned it into a
bug. This used to be zone arithmetic — the camp's zone against the player's zone,
within `ZoneSystem.m_activeArea + m_activeDistantArea`. Those two fields are gone.
The active area is now a **server-synced, player-configurable** `SimulationDistance`
(`ZNet.GetSyncedSimulationDistance()`), so there is no constant left to copy, and
its shape is not even a square of zones: at near distance 2, non-classic,
`ZNetScene.PointInsideActiveArea` intersects the 1.5-zone Chebyshev box with a
1.75-zone radius, clipping the corners. Any arithmetic of our own would be a second
copy of a rule the player can change under it, and provisional merchants would
spawn or vanish at a distance that no longer matches what the server actually
simulates. `ZNetScene.InActiveArea` is static and public — use it.

Settling runs on the server, over an RPC, because `StoreGui.Show` is client-side
and a client owns none of the rival traders' ZDOs. The settled flag rides on a
global key, so it persists in the world save and reaches clients without any sync
of the mod's own. Guidance prefers a settled merchant, then the nearest one
currently standing, and finally the nearest candidate site.

> **New worlds only.** On a world where vanilla has already placed a camp, the
> other candidates are gone and there is nothing left to defer. The system
> disables itself and says so once in the log; compasses carry on working.

### Cooldowns

Disabled by default (`LootCooldownSeconds = 0`), in which case the server tracks
nothing whatsoever — no timestamps are recorded and the lookup short-circuits.

When enabled, there are still no timers. A single timestamp per looted stone is
held in memory and compared lazily when someone interacts, so the cost is one
dictionary lookup per interaction and nothing per frame. The map is lazy — an
entry appears only when a stone is actually looted — and prunes expired entries
past a threshold, bounding the live set to stones looted within the cooldown
window.

That state is deliberately in-memory, so **cooldowns reset when the server
restarts.**

### Remaining uses

Uses ride on item durability, which gives a native tooltip count and persists
through the item's ZDO. `m_canBeReparied` is disabled — without it, a workbench
would refill the uses.

## Status

**1.6.0** — in use on a dedicated server.

Verified end to end against a real dedicated server: the plugin registers on
both sides, the loot exchange survives a genuine network hop, and looting,
naming, the range check, camera aiming, the use counter, the spam guard and
destruction on the final use all behave. The loot cooldown was verified
separately in single-player before being turned off by default. The carry rule
was confirmed to hold on pickup as well as on looting, so a compass cannot be
duplicated by dropping it first.

Not yet exercised:

- **The merchant placement rewrite.** It needs a world generated with the mod
  already installed, and has not yet been watched spawning, despawning and
  settling a trader in a live world.
- **The uninterruptible camera turn.** The cause is understood and the fix is
  small, but it has not been played since.
- **Two players connected at once**, so the shared cooldown has never been
  observed refusing one player because of another's loot. The path is
  server-side and identical either way, but it has not been watched happening.

## Credits

The merchant lorestone and Hildir quest compass behaviour is adapted from
**Find Haldor** by **Gonfreecss**, used with permission — the merchant catalog,
the distance gates and the Lock In model are all theirs.

The inventory icon is from **"Compass PBR(Unity) CC0"** by **Lucian Pavel**,
released under [CC0](https://creativecommons.org/publicdomain/zero/1.0/) and
available at
[opengameart.org/content/compass-pbrunity-cc0](https://opengameart.org/content/compass-pbrunity-cc0).
CC0 waives the attribution requirement — the credit is here because it is
deserved, not because it is owed.

The icon is embedded in the plugin assembly, so installing remains a single DLL.
Only the icon is used; the item still borrows its in-world model from a vanilla
prefab (`Item.CloneSourceItem`).

## License

Released under the [MIT License](../LICENSE).
