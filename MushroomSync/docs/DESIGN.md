# Design

Why Mushroom Sync is built the way it is. The API itself is in the
[README](../README.md); this is the reasoning behind it.

## What was here before

Four mods each carried their own copy of the same server-authoritative sync:

| Mod | File | Lines |
|---|---|---|
| Combat Adjustments | `ConfigSync.cs` | 519 |
| Haldor Expansion | `TradeConfigSync.cs` | 320 |
| Craftable Spawners | `SpawnerConfigSync.cs` | 294 |
| Random Yggdrasil | `RotationSync.cs` | 284 |

They were copies, and said so — Haldor's opened with "Same pattern as Craftable
Spawners and Combat Adjustments". They had also drifted, which is the real cost:

- Only Combat Adjustments patched `ConfigEntry<T>.Value`, guarded `ConfigFile.Save`,
  compressed its payload, and sorted entries for a stable wire order.
- Craftable Spawners passed enums straight to `TomlTypeConverter`, which returns
  `null` for shapes it cannot convert — a silently ignored setting. Haldor had hit
  this and added explicit enum handling; Craftable Spawners had not.
- Haldor gated sending on `LockConfiguration`; Craftable Spawners had no gate.

So the bugs were fixed in one copy and not the others, and nothing made that visible.

## Two layers, because the payload was never the shared part

Random Yggdrasil syncs world rotations, not config. Yet its file was the same
transport as the other three, differing only in what went into the package. That is
the seam:

- **`SyncChannel`** — RPC pair registration, the deferred handshake, the
  protocol+version envelope, broadcast to ready peers, and clearing client state on
  disconnect. Knows nothing about what it carries.
- **`ConfigSync`** — turns `ConfigEntryBase` into a payload and back, on top of a
  channel.

Random Yggdrasil now uses the channel directly and keeps ~100 lines describing what a
rotation looks like on the wire. Everything else went.

## One plugin, not a merged library

MushroomSync is its own BepInEx plugin, and mods declare
`[BepInDependency(MushroomSyncPlugin.PluginGuid)]`.

The alternative — ILRepack it into each mod, which two of them already had wired up —
would have kept mods independently installable, but every copy patches the same
methods. Four mods meant `ZNet.OnNewConnection`, `RPC_PeerInfo`, `Disconnect` and
`OnDestroy` patched four times, and four postfixes on `ConfigEntry<T>.Value` — which
is a **global** patch affecting every mod in the process, not just ours.

The usual argument against a separate plugin is the extra install step. That does not
apply here: releases ship one `MushroomMods-plugins.zip` containing every DLL, so a
user extracting it gets MushroomSync whether they think about it or not.

`[BepInDependency]` also fixes load order, so a mod's `Awake` can rely on the shared
patches already being installed.

## Not wrapping login sockets

ServerSync — the widely used library this replaced — hooks the socket during login.
That breaks whenever the game changes its socket layer, and it is why these mods
stopped using it; `ILRepack.targets` in Craftable Spawners still records "ServerSync
was removed; nothing left to merge".

This uses ordinary peer RPCs registered in `ZNet.OnNewConnection`, and defers its own
network work by two frames out of `RPC_PeerInfo` so it never invokes an RPC while
another mod is still wrapping the socket. Nothing here depends on socket internals.

## The value overlay is read-only

`entry.Value` returning the host's value is a Harmony postfix on the
`ConfigEntry<T>.Value` getter. Two properties keep that from being a trap:

**Nothing is written back into the entry.** The synced values live in a side
dictionary. A client's `.cfg` is never edited, and its own settings return the instant
it disconnects — no cleanup pass, nothing to get wrong if the game exits badly.

**A re-entrancy depth suppresses the overlay** in the two places where the local value
is the one that matters:

- While the server serialises its own config to send. Without this, a client that
  later hosts would rebroadcast values it had received from somewhere else.
- While BepInEx runs `ConfigFile.Save`. This one is the subtle one: a synced client
  that changes *any* unrelated setting triggers a save, and without the guard the
  save writes the overlaid host values into the client's own file **permanently**.
  Combat Adjustments had this guard; the other three did not, so they had a latent
  config-corruption bug that this refactor removes.

The depth is `[ThreadStatic]` — config can be read off the main thread, and a
suppression leaking across threads would silently disable the overlay.

### The active flag goes up before the payload is read

`SyncChannel` sets `ClientSyncActive` *before* calling `ReadPayload`, not after.

This looks like a detail and is not. Callers apply values from inside `ReadPayload` —
Combat Adjustments bakes shield, weapon and feast stats into `ObjectDB`, Craftable
Spawners rebuilds spawner drop tables, Random Yggdrasil rotates the tree. All of them
read `entry.Value`, and both the overlay and `TryGetSyncedValue` gate on that flag.

Set the flag afterwards and the first payload after a join is applied while
`entry.Value` still returns the client's own config. Later reads are correct, so it
looks fine — but whatever was baked during that first apply stays wrong until some
later broadcast happens to redo it. A client would join a server and quietly play with
its own shield numbers.

The cost of the earlier flag is that a throw part-way through `ReadPayload` leaves a
half-applied state, so the catch clears unconditionally rather than only when a sync
was already established.

There is a second, quieter cost, and it caught this code once already: **nothing
inside `ReadPayload` can ask the channel whether this is the first payload**, because
by then the answer is always "no". `ConfigSync` therefore tracks `_hasHostValues`
itself, set after a successful apply and cleared when the sync drops. Sampling
`ClientSyncActive` instead makes "first activation" permanently false, which costs no
correctness — the overlay and the ObjectDB bake are unaffected — but silently loses
the player-facing toast on joining a server, and makes the log always say "updated"
even the first time. A read-only symptom of a state-ordering bug is exactly the kind
that survives review.

### Patched per type, on demand

The original patched `ConfigEntry<T>.Value` for a fixed four: `bool`, `int`, `float`,
`string`. Anything else — notably enums — could not be overlaid. Haldor's
`ConfigEntry<UnlockBoss>` is exactly that case.

`Register` now patches the getter for whatever type the entry actually is, once per
type. Enums and any future type work without touching this file.

## Wire format

```
envelope: [int protocol][string modVersion][compressed payload]
```

The mod version is compared on both sides. A mismatch is logged and the client falls
back to local values rather than applying a payload it may not understand — the mods
sync gameplay-affecting settings, so applying a half-understood payload is worse than
not syncing.

`ProtocolVersion` covers the envelope only. A mod changing its own payload shape does
not need to bump it, because its own version string already differs.

Payloads are compressed. Config payloads are highly repetitive strings and this runs
during login, when the connection is busiest.

### This is not a silent upgrade

The RPC names changed (`<id>.Sync`, `<id>.SyncRequest`) and the protocol went to 2, so
**a pre-MushroomSync build and a post-MushroomSync build of the same mod will not talk
to each other.** The old build never registered the new RPC names, so it does not
receive anything — there is no handshake to fail and no error to see. It looks exactly
like "sync stopped working".

That is acceptable because the mods ship together in one zip: extract
`MushroomMods-plugins.zip` on the server and on every client and everything matches.
It is worth knowing about for a *staged* rollout, where a server updates before its
players do. Update both ends together.

The mod-version handshake catches the narrower case of two different post-MushroomSync
builds, and logs it.

Config entries are written in a deterministic order, so an unchanged config produces
an identical payload and packet dumps are comparable between runs.

## Targeting net472

The consumers are `net472` and `net48`, so `net472` is the lowest target all of them
can reference.

`netstandard2.0` would have been more portable and was tried first. It fails: the
game's `UnityEngine` assemblies are themselves built against netstandard 2.1, and
referencing them from a 2.0 target is `CS1705`. `netstandard2.1` compiles but then
cannot be referenced by the `net47x`/`net48` mods.

The consequence to know about: **vegvisir-compass targets `netstandard2.1` and cannot
reference this as-is.** It has no config to sync today. If that changes, retarget it
to `net472` alongside the others.

## Channel ids must be unique

A channel id becomes its RPC names (`<id>.Sync`, `<id>.SyncRequest`). Two mods
choosing the same id would register RPCs over each other. `ChannelRegistry` refuses a
duplicate id and logs it rather than letting that happen quietly.

`ConfigSync.Create(PluginGuid, ...)` suffixes `.Config`, so passing the plugin GUID is
enough to be unique.
