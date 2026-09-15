# ZNetScene is rebuilt on every world join

Written after the 2026-09-15 "nests despawn after a server restart" report.
Read this before touching `EnsureInitialized` or anything that registers a
prefab with `ZNetScene`.

## What happened

Players reported placed Greydwarf nests vanishing "after some time", and all
of them at once after a server restart. Nothing was deleted. The server log
had no `Destroyed invalid prefab ZDO` line, the ZDO count kept climbing, and
players who relaunched the game saw every nest again.

The client log told the story. One game launch, nine world joins, and:

| Line | Count |
|---|---|
| `Craftable spawners initialized.` | 1 |
| Haldor Expansion `Registered SuperMistTorch with ZNetScene.` | 9 |
| `Missing prefab hash: -925308856` | 2.69 million |

`-925308856` is `"CS_GreydwarfNest".GetStableHashCode()`.

## Why

Leaving a world destroys the `ZNetScene` object. The next join builds a new
one, and its `m_namedPrefabs` table is filled in `Awake` from the game's own
prefab lists only. Our clones survive that (they hang off a
`DontDestroyOnLoad` root), but the new instance has never heard of them.

`EnsureInitialized` used to guard everything behind one static `initialized`
flag, so the clones were registered with the first `ZNetScene` of the game
session and never again. From the second join on, that client could still
*place* a spawner (placement instantiates the piece prefab directly) but could
not *create* one from a ZDO when it came into range. The spawner was invisible
and inert for that client only, which is why the report never reproduced for
anyone who had relaunched the game.

A server restart kicks every client, and everyone rejoins without relaunching,
so every client hit it in the same minute.

## What the fix does

`RegisterAllPrefabs` runs on every `ZNetScene.Awake` and re-adds each built
clone to the new instance. The clone build itself stays one-time. It logs
`Registered N spawner prefab(s) with ZNetScene.` once per join, on purpose: the
count of that line against the count of `Valheim version:` lines in a client
log is the fastest way to see whether registration ran.

The dedicated server never rebuilds `ZNetScene`, so it was never affected.
Server logs will not show this bug; ask for a client `LogOutput.log`.

## Two things worth knowing

- **1.0.12 clients do not delete unknown-prefab ZDOs.** Only the server does,
  in `ZNetScene.CreateObjectsSorted` and `CreateDistantObjects`, and it logs
  when it does. A client just logs `Missing prefab hash` thirty times a second
  and moves on. That is why nothing was lost.
- **`Missing prefab hash` prints a number, not a name**, when the hash is not
  in the current scene's table. Compute the stable hash of your prefab names
  (the algorithm is `StringExtensionMethods.GetStableHashCode`) and compare.

## Repro

Launch the game, join, place a nest, log out to the main menu, rejoin, walk to
the nest. Before the fix it is gone and the client log fills with the missing
hash. Relaunch the game and rejoin: it is back.
