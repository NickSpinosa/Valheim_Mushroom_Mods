# `GenerateLocationsCompleted` does not fire for a saved world

Found 2026-09-18 on Valheim 1.0.15, testing a local dedicated server. Read this
before trusting any `ZoneSystem` event to tell you a world is ready.

## What happened

A client joined a dedicated server and sat on the loading screen for three
minutes, then spawned at the vanilla stones. The server log said, once per
request and for ever:

```
Direct layout sync skipped for peer ...; server layout unavailable.
```

The server had no layout because it had never bootstrapped. Its log had
`Separate Spawns subscribed to location generation.` and then none of what
should follow: no `GenerateLocationsCompleted fired.`, no `Bootstrapping
Separate Spawns for world`.

## Why

The event is raised by a property setter:

```csharp
public bool LocationsGenerated {
    private set {
        m_locationsGenerated = value;
        if (m_locationsGenerated) { m_generateLocationsCompleted?.Invoke(); ... }
    }
}
```

`ZoneSystem.Load`, the 1.0 "DB2" save loader, does not use it:

```csharp
m_locationsGenerated = zPackage.ReadBool();   // the field, not the property
```

The legacy `LoadOld` path does use the property, and so does the end of
`GenerateLocationsTimeSliced`. So the event fires for a world in the old save
format and for a world generating its locations for the first time, and **never
for a 1.0-format world that already has them**. That is every restart of an
established server.

`WorldBootstrap.Initialize` runs at `ZoneSystem.Awake`, before the save is
read, so its own "already generated?" check saw `false` too. The flag flipped a
moment later with nobody watching.

## Why production looked fine

It had the same gap. The production log never shows a bootstrap after a restart
either. It kept working because `DirectPeerSync.SendToPeer` and
`LayoutSync.OnRequestLayout` both fall back to `WorldLayoutStore.Load` when the
cache is empty, and production had a layout file from the day the world was
made. What production silently lost on every restart was the rest of
`BootstrapServer`: `PlacePortals` (which repairs as well as places),
`TryApplySpawnDifficulties`, and the startup broadcast.

A world with no layout file had nothing to fall back to. That was the test
server, and it would be any world the mod is added to after creation.

## The fix

`WorldBootstrap.WatchForLoadedLocations` polls `LocationsGenerated` twice a
second on the server until it is true, then runs the same handler the event
does. `HandleLocationsReady` is latched, so whichever notices first wins and
the other is a no-op. The subscription stays: on a new world the event is still
the prompt signal, and the watcher simply finds the work done.

The watcher is tied to the `ZoneSystem` it was started for and exits when that
instance is gone, so it needs no teardown of its own; `Shutdown` only clears the
latch. It stops at once on a client, which never generates locations and gets
its layout through `ClientSyncHelper`.

`PlacePortals` is safe to run again on a live world: it skips any group whose
two portal ZDOs already exist. It ran on every start before 1.0 changed the
loader, which is what it was written for.

## Two smaller things the same session turned up

**The spawn wait clock was per-process.** `GameFindSpawnPointPatch` timed its
300 s wait with a static that nothing cleared. The player above gave up after
three minutes, rejoined, and was dropped at the vanilla spawn seventeen seconds
later by a timeout that had been running since the first session. It is reset on
world teardown now, next to `WorldBootstrap.Shutdown`. This is the rule in
[world-lifecycle.md](world-lifecycle.md) again; that file's list of per-world
state should be the first place to look when adding a static.

**The retry loop was most of both logs.** While the layout was outstanding the
client asked every two seconds, three times per pass (the roster and layout
requests each sent a direct request of their own on top of the loop's), and
printed every member of every group for each of the two roster replies. The
loop now sends one direct request per pass, backs off from 2 s to 30 s, logs its
requests once and a "still waiting" line per pass at the cap, and ignores a
roster payload identical to the one it already applied. The server logs "sent
roster" and "layout unavailable" once per peer per world.

The 300 s timeout itself is unchanged. A brand-new world really can take
minutes to generate locations and a layout, and a client that gives up early
spawns in the wrong place for good.
