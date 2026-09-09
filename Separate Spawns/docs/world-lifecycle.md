# A plugin outlives a world

Read this before adding any `static` field that holds world-derived data, and before
adding a guard flag that stops work happening twice.

## The bug this file exists because of

Issue #38. Load world A, return to the main menu, load world B without quitting the
game — players spawned at **world A's group spawn coordinates inside world B**. Under a
different seed those coordinates are arbitrary terrain: the observed case was a swamp,
with no Group Portal and none of the placement guarantees the mod is for.

Nothing crashed and nothing logged an error. The mod reported a normal resolve:

```
Resolved Steam_7656... -> groupB spawn (-975, 2300).
Spawning local player at group spawn (-975, 32.9, 2300).
```

Two separate pieces of state caused it, and both looked reasonable in isolation.

**The bootstrap guards.** `WorldBootstrap._subscribed` and `_bootstrapStarted` were
cleared only by `Shutdown()`, which was called from `Plugin.OnDestroy` — *plugin*
teardown, not world teardown. `Initialize` ran once, from `Plugin.Start`. So on world B
`BootstrapWhenReady` returned immediately, and the `GenerateLocationsCompleted`
subscription was still attached to world A's destroyed `ZoneSystem`, so it never fired
again. No bootstrap, no layout, no report.

**The layout cache.** `WorldLayoutCache` held a bare `Current` with no world identity
and no invalidation, so `GroupSpawnResolver` cheerfully answered world B's query with
world A's data.

Either one alone would have been survivable. Together they produced a silent wrong
answer, which is the worst shape a failure can take in this mod — a player who spawns
somewhere plausible has no reason to suspect anything.

## The rule

**Per-world state gets cleared on world teardown, and per-world setup runs on world
load — not on plugin load.** `Plugin.Awake` and `Plugin.Start` fire once per *process*.
A world is not a process.

The two hooks are in `Patches/WorldLifecyclePatches.cs`:

- `ZoneSystem.Awake` postfix → `WorldBootstrap.Initialize`. Every world builds a new
  `ZoneSystem`, and `GenerateLocationsCompleted` lives on that instance, so the
  subscription must be made again each time. `Awake` assigns `s_instance` first thing,
  so the postfix already sees `ZoneSystem.instance`.
- `ZNet.OnDestroy` postfix → `WorldBootstrap.Shutdown`, which clears both flags and the
  layout cache. `OnDestroy` rather than `Shutdown`, because `ShutdownWithoutSave` is a
  second exit path that never calls the first and both end in `OnDestroy`.

`Plugin.Start` still calls `Initialize` as well. It is redundant while the patch is
applied and harmless if it runs twice, but patches are applied per class now
(`Shared/PatchIsolation.cs`), so a skipped patch class should not silently take the
first world's bootstrap with it.

## The backstop

Resetting on teardown is the fix; it is not the guarantee. `WorldLayoutCache` records
the world UID when a layout is set and checks it on every read of `Current`, returning
null when it does not match and logging once. A missed reset therefore degrades to
**vanilla spawning plus a loud error**, not to another world's coordinates.

This is the same fail-closed shape used elsewhere in the repo — Haldor's `IsUnlocked`
treats an unknown global key as "not unlocked" rather than assuming the best. When the
mod cannot be sure it has the right answer for this world, doing nothing is correct.

`ZNet.GetWorldUID()` dereferences `m_world` without a null check, so the helper goes
through `GetWorldName()` (which does null-check) before asking for the UID. Clients get
`m_world` from `RPC_PeerInfo`, so the UID is available on both sides once connected.

`separatespawns` prints both UIDs — the loaded world's and the held layout's — so a
mismatch is one command away rather than an inference from spawn coordinates.

## What was already correct

Worth knowing so it does not get "fixed" twice: `RosterSync`, `LayoutSync` and
`PortalActivationSync` guard registration with
`ReferenceEquals(_registeredInstance, ZRoutedRpc.instance)` rather than a bare bool, so
they re-register against a new world's `ZRoutedRpc` on their own. `ClientSyncHelper`'s
retry loop watches for disconnect and calls `RosterSync.ResetClientState`. The gap was
only in the bootstrap flags and the layout cache.

The group roster itself is deliberately **not** per-world: `SeparateSpawns.groups.json`
is global membership and should survive a world change. Only the spawn *difficulty*
values written into it are world-derived, and those are recalculated per world from the
frozen spawn positions.
