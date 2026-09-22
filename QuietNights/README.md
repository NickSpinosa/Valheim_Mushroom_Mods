# Quiet Nights

Killing a boss in Valheim does more than unlock the next biome: it switches on
extra creature spawns at night in the biomes you already cleared. Quiet Nights
turns those off for fulings, seekers and the charred, so a long-lived server's
starting areas do not get steadily more hostile as the group progresses.

Only *away from home*. Fulings still roam the Plains, seekers the Mistlands and
charred the Ashlands exactly as before, day and night. What stops is those
creatures arriving after dark in biomes you had already made safe, because
someone killed a boss.

Raids and the fixed spawners in camps and dungeons are not touched. How it works,
and why it is built the way it is, is in [docs/DESIGN.md](docs/DESIGN.md).

## Installing

Needs **[Mushroom Sync](../MushroomSync/README.md)**. Install both on **every
client and on the dedicated server**.

Every client matters more than usual here. Valheim rolls a zone's spawns on the
client that owns the zone, not on the dedicated server, so a player without the
mod still spawns the vanilla night creatures for everyone near them.

## Configuration

Settings are read from the server's `BepInEx/config/mushroom.quietnights.cfg`
and followed by connected clients; a client's own file is left untouched.

| Setting | Default | |
|---|---|---|
| `General.Enabled` | `true` | Master switch. Off means vanilla spawns |
| `Rules.Creatures` | `Goblin*:Plains, Seeker*:Mistlands, Charred*:AshLands` | `Prefab:HomeBiome` pairs. A trailing `*` matches a family of prefabs; join several home biomes with `+` |
| `Rules.BossKeys` | `defeated_*` | Which global keys count as a boss kill. Only spawns that require one of these are touched |

Changes apply live, without a restart or a relog. Creatures already in the world
are left alone; night spawns walk off at dawn on their own.

### Seeing what it does

```
quietnights dump
```

in the F5 console (no `devcommands` needed) writes every boss-gated spawn entry,
and what Quiet Nights does to each, to `BepInEx/config/QuietNights.dump.txt`.
Use it to find the real prefab name of a creature you want to add, and to check
a rule catches what you meant: suppressed entries are marked `APPLIED`.

## Building

```bash
dotnet build -c Release
```

See [docs/devops.md](../docs/devops.md) if the game is not where the Steam
registry says it is.
