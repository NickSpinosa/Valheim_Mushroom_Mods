# Spawn priority trap

Vanilla `Game.FindSpawnPoint` order:

1. Logout point — only when **not** respawning after death, while `HaveLogoutPoint()`
2. Bed — while `HaveCustomSpawnPoint()`; if the bed object is missing within 5m, vanilla clears the custom point and retries
3. Sacrificial stones (`StartTemple`)

`usedLogoutPoint` is set **only after** a successful logout spawn. While the logout zone is still streaming, vanilla returns false with `usedLogoutPoint == false` and `HaveLogoutPoint()` still true.

## What went wrong

The FindSpawnPoint postfix treated "not `usedLogoutPoint` and no bed" as "force Group Spawn". During the logout wait that condition is true, so the mod called `SetReferencePosition` on the Group Spawn, diverted streaming away from the logout point, and placed the player at Group Spawn on reconnect — including after destroying a bed (logout still applies; bed is irrelevant).

Group Spawn must only replace step 3 (stones), never interrupt steps 1–2. Decision helper: `SpawnOverrideDecision.ShouldLeaveVanillaAlone`. Regression check: `scripts/verify-spawn-override.ps1`.
