# Finding the local Platform User ID on Valheim 1.0

Why `PlatformIdHelper` looks the way it does after 1.0.7, and the way it failed
before. Read before changing how the mod decides who the local player is.

## The failure this fixed (issue #12)

On the first 1.0.7 run a client hosting its own world generated the world, ran
the bootstrap, and then sat on the loading screen with
`Cannot resolve group spawn: local platform user id was empty`. The spawn
override treats an empty id as "still waiting for sync" and holds vanilla off
for up to five minutes. Two independent things had gone wrong:

1. **The platform layer moved into a namespace.** `PlatformManager` is now
   `Splatform.PlatformManager` in `Splatform.dll`. `Assembly.GetType` takes a
   full name, so the bare `"PlatformManager"` returned null on every assembly and
   the primary lookup silently produced nothing. The property chain below it
   (`DistributionPlatform` → `LocalUser` → `PlatformUserID`) kept its names.
2. **The headless check was keyed on the local player.** It answered "headless"
   whenever `ZNet.IsServer()` and `Player.m_localPlayer == null`. A hosting
   client is exactly that until its player spawns, and the spawn is what was
   waiting on the id. That check gated the Steamworks fallback too, so the
   fallback that should have carried a Steam client never ran.

Neither leg logged anything, which is why the same symptom read as
"Steam not ready yet". The lookup now logs once which path produced the id.

## What decides headless now

`SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null`. That is the test
the game itself uses to know whether there is a local player at all
(`ZNet.UpdatePlayerList`), it is true from process start on a dedicated server,
and it never changes during a session.

Rejected: `ZNet.IsDedicated()`. It exists, it is public, and in the client build
it is a stub that returns `false` unconditionally. Do not gate anything on it.

## Consequence for the wait in `GameFindSpawnPointPatch`

The 300 s timeout was sized for "roster not synced yet", a condition that
resolves on its own. An id lookup that has definitively failed does not, so the
wait is pure delay in that case. Left alone in this change to keep the fix to
the two broken legs; a shorter fallback for "both id paths failed" is a
reasonable follow-up.
