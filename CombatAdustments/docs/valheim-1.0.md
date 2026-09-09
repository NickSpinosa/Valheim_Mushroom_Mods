# Valheim 1.0.7 notes

## Harmony type lists break on *added optional parameters*

Both tooltip patches (`ItemData_GetTooltip_Patch` in `Patches.cs`,
`ItemData_GetTooltip_TwoHanded_Patch` in `TwoHandedCombat.cs`) pin their target
with an explicit `typeof(...)` list. 1.0.7 appended one optional parameter to the
static overload — `GetTooltip(ItemData, int, bool, float, int stackOverride = -1,
bool appending = false)` — and both attributes went stale.

The lesson worth keeping: an explicit type list is an *exact* match. C# will
happily let you call the new method with the old five arguments, so nothing in
our source looks wrong and the build stays green; the mismatch only exists at
runtime, where Harmony resolves the attribute against the real method and finds
nothing. Optional parameters are the worst version of this, because adding one is
a source-compatible change from the game's point of view and a breaking one from
ours. Any Valheim update is a reason to re-check every attribute in this mod that
names types, not just the ones whose *behaviour* changed.

## Why one stale attribute took the whole plugin down

`Harmony.PatchAll` does not skip a patch class it cannot resolve — it throws
`ArgumentException: Undefined target method for patch method ...`. That call sits
in the middle of `ShieldReworkPlugin.Awake`, so the throw skipped everything
after it: `Sync.Register`, the feast and ocean config hooks,
`Sync.WatchForChanges`, `OceanWeather.Apply()` and the "loaded" log line. Every
patch class compiled after the bad one (`SailingWind`, `StaggerDebugHud`, all of
`TwoHandedCombat`) was never applied either.

What made it expensive to read: the failure looked *partial*, not total. Shield
stats still applied in game, because the `ObjectDB` patches come earlier in file
order and had already been installed when the throw happened. A log that shows
correct shield numbers and no "loaded" line is the signature of this class of
bug — check for the `PatchAll` exception before believing any half-working
symptom.

The structural fix is patch isolation: patch classes applied individually inside
try/catch so one bad target costs one feature instead of the plugin. That is
tracked separately in issue #18 and deliberately not done here.
