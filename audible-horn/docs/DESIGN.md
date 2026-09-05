# Audible Horn — design notes

Things learned while building this mod that the code alone does not explain. The
vocabulary is in [CONTEXT.md](CONTEXT.md); the build plan is in
[tickets/](tickets/).

## Which reference-root property the csproj uses

Ticket 01 says to copy `haldor-expansion/HaldorExpansion.csproj`, but haldor
resolves the game through a `Local.props` import and errors out if `ValheimDir`
is unset. That fails the ticket's own acceptance criterion — a bare
`dotnet build` with no arguments — because `Local.props` is gitignored and does
not exist in a fresh checkout.

So the reference block here is MushroomSync's instead: `ValheimDir` defaults from
the Steam registry (app 892970), then `ValheimManaged` and `BepInExCore` default
from it, each guarded by `Condition="'$(X)' == ''"`. CI is unaffected either way
— `docs/devops.md` says the composite action passes *every* spelling of the
reference root as an MSBuild global property, and global properties beat anything
a project sets, so the `Condition` blocks simply never fire on a runner.

The practical rule: a new mod here should take its reference block from
MushroomSync, not from haldor.

## Why sync has no opt-out gate

`Plugin.Awake` calls neither `ConfigSync.GatedBy` nor `AcceptedWhen`, unlike
Combat Adjustments and Haldor Expansion. That is deliberate, not an oversight.

Hearing Range and Horn Cooldown are not preferences — they are a shared fiction
between two machines. If a Listener's Hearing Range is larger than the Blower's,
they hear a Horn Call the Blower's client believes was out of earshot; if a
client keeps a shorter Cooldown than the server, it sounds the horn more often
than the server allows. Either way the two players disagree about what happened,
which is the one failure this mod cannot tolerate, because hearing the call *is*
the whole feature.

Horn Volume is the opposite case and is `Exclude`d: it is a personal loudness
multiplier, it changes nothing another player observes, and a host overwriting it
would be an intrusion.

## TankardOdin as cloned

**Status: to be filled in from the first in-game run.** The values below cannot be
read from `assembly_valheim.dll` — they live in the prefab's serialised data inside
the game's asset bundles, so only a running game can report them.

To get them: set `DumpClonedAttack = true` in `src/SignalHornItem.cs`, start the
client into any world, and read `BepInEx/LogOutput.log`. The dump runs once, on the
first successful prefab build, and is bracketed by
`--- TankardOdin as cloned ---` / `--- end TankardOdin dump ---`. Then set the flag
back to `false` and paste the values here.

| Field | Value |
|---|---|
| `m_shared.m_itemType` | _to be filled in_ (expected `Tool`) |
| `m_shared.m_animationState` | _to be filled in_ |
| `m_shared.m_attachOverride` | _to be filled in_ |
| `m_shared.m_attack.m_attackAnimation` | _to be filled in_ — **ticket 05 needs this** |
| `m_shared.m_attack.m_attackType` | _to be filled in_ |
| `m_shared.m_attack.m_attackStamina` | _to be filled in_ — **ticket 05 needs this to be 0** |

If `m_attackStamina` comes back non-zero, ticket 05's assumption that a free-body
attack never fails for want of stamina is wrong, and the horn will need
`m_attack.m_attackStamina = 0f` set in `BuildPrefab` alongside the other overrides.

### There is no `m_holdAnimationState`

Ticket 02 asks for `m_shared.m_holdAnimationState` in both the keep-as-cloned list
and the diagnostic dump. That field does not exist on
`ItemDrop.ItemData.SharedData` in this build of the game — the full public field
list was dumped by reflection over `assembly_valheim.dll` and the only near
neighbours are `m_animationState` (the `AnimationState` enum that
`Humanoid.SetupEquipment` feeds to the animator) and `m_attachOverride` (the
`ItemType` that decides which attach point the model hangs on). Nothing is done to
either, so "keep as cloned" is satisfied either way; the dump reports
`m_attachOverride` in its place, because that is the field that actually governs
how the horn is held.

## Why the workbench recipe can be deferred

`EnsureRecipe` resolves the workbench by scanning `ObjectDB.m_recipes` for a vanilla
recipe that already points at a `CraftingStation` named `piece_workbench`, and
reuses that reference rather than building its own. Two reasons, and the second is
the one that bites: it works inside `ObjectDB.Awake`, before `ZNetScene` exists at
all; and the crafting UI groups recipes by station *object*, so a `CraftingStation`
fetched separately from the prefab would be a different reference and the horn would
sit under a workbench the player is not standing at.

When neither the scan nor the `ZNetScene.instance.GetPrefab` fallback finds one, the
recipe is **not** added and a warning is logged once. Adding it with
`m_craftingStation = null` is the tempting alternative and is wrong: vanilla reads a
null station as "craftable with bare hands", so the failure mode would be a free
horn rather than a missing one. `EnsureRegistered` runs again from
`ZNetScene.Awake` and `Game.Start`, and the bench is resolved by then.

## Two private members, reached without the publicizer

`vegvisir-compass` calls `ObjectDB.UpdateRegisters()` and writes
`ZNetScene.m_namedPrefabs` directly, which reads as precedent that both are public.
They are not — that project adds `BepInEx.AssemblyPublicizer.MSBuild` and marks its
references `Publicize="true"`. This project deliberately does not (see the tickets'
conventions), so both go through `HarmonyLib.AccessTools`: a cached `MethodInfo` for
`UpdateRegisters` and a `FieldRef<ZNetScene, Dictionary<int, GameObject>>` for
`m_namedPrefabs`.

`UpdateRegisters` is not optional. `ObjectDB` keeps private `m_itemByHash` and
`m_itemByData` dictionaries and rebuilds them only there; an item appended to
`m_items` without it is invisible to `GetItemPrefab`, which is what the inventory,
the crafting UI and `Inventory.AddItem` all resolve through. The item would appear
to register successfully and then not exist.

## Audio settings row hierarchy

The Horn Volume slider is a **clone of the vanilla music row**, not a hand-built
widget: cloning inherits the panel's fonts, colours, slider art and row width for
free, and keeps inheriting them when the game restyles the panel.

### The type is namespaced, and the bare name is a trap

The Audio tab is `Valheim.SettingsGui.AudioSettings`, not the global
`AudioSettings` the ticket names. `UnityEngine.AudioSettings` is a real,
unrelated type, so an unqualified `AudioSettings` compiles cleanly and binds to
Unity's — the patch class would attach to the wrong type and simply never fire,
with no error anywhere. `AudioSettingsPatch.cs` aliases it once as
`VanillaAudioSettings` and never writes the bare name.

### What the four lifecycle methods actually do

Read off the IL, because the names mislead:

| Method | Signature | Actually does |
|---|---|---|
| `Initialize()` | — | Loads the three sliders from `PlatformPrefs` and stores `m_old*`. Does **not** call `OnAudioChanged`, so the value texts are updated by the prefab's persistent slider listener, not here. |
| `OnTabOpen(Button back, Button ok)` | 2 params | Only wires gamepad navigation: `GuiUtils.SetNavigationDown/Up` between `m_continousMusic` and the Back/OK buttons. Nothing about values. |
| `OnOkAsync(OkActionCompletedHandler cb)` | 1 param | Writes `PlatformPrefs`, then invokes the callback if non-null. |
| `OnBack()` | — | Restores `m_old*` straight into `AudioListener.volume`, `MusicMan.m_masterMusicVolume` and `AudioMan.SetSFXVolume`. |
| `OnAudioChanged()` | — | The sliders' persistent change listener. Formats every value text as `Mathf.Round(v * 100f).ToString() + "%"` — matched exactly by our row. |

Two consequences. First, gamepad navigation has to be spliced in from an
`OnTabOpen` postfix, not `Initialize`: the buttons that terminate the chain are
not known any earlier. Second, the vanilla chain is set **explicitly**, so
`Navigation.mode` is presumably `Explicit`; our patch reads the mode and only
rewrites links when it is, otherwise it copies Music's navigation and lets
Unity's automatic mode find the clone geometrically.

### Why the row is discovered, not addressed by path

The row layout is only knowable at runtime and this was written without the
ability to launch the game, so nothing is hard-coded to a transform path. The
only two anchors the game exposes are the private fields `m_musicVolumeSlider`
and `m_musicVolumeText` (`AccessTools.FieldRefAccess`, no publicizer). From
those:

1. **The row** is the nearest ancestor-or-self of the slider that also contains
   the value text (`Transform.IsChildOf` is true for the transform itself, so a
   label parented *under* the slider falls out of the same test).
2. **Sanity gate.** If that ancestor also contains the SFX slider, the master
   slider or the Continuous Music toggle, the walk over-reached and the "row" is
   really the whole list — cloning it would duplicate every audio control. The
   patch logs an error and adds no slider rather than wrecking the panel.
3. **Parts inside the clone** are resolved by the *child-index path* the
   original occupies in the source row, not by name. `Instantiate` preserves
   child order exactly, and index paths stay unambiguous where names do not: two
   rows can both contain a `Text (TMP)`. The resolved object's name is compared
   to the original's and a mismatch is logged, so a surprising hierarchy shows up
   in the log instead of silently relabelling the wrong object. Name matching is
   the fallback if the index walk fails.
4. **The label** is whichever `TMP_Text` in the row is not the value text.
5. **Placement.** If the row's parent has a `LayoutGroup`, `SetSiblingIndex` is
   the whole story. If not, the rows are absolutely positioned and the clone is
   offset by `musicRow.anchoredPosition - sfxRow.anchoredPosition` — the gap the
   panel already uses between two rows. Controls *below* Music are deliberately
   not moved; if that turns out to be needed the log says so loudly.

`Localize` components are destroyed throughout the clone. `Localize.Start` runs
`Localization.Localize` over its subtree and repeats it on every language change;
our label is a literal with no `$token` so it would survive, but removing the
component makes that a fact rather than a bet.

### Observed structure

**To be filled in from the first in-game run.** The patch logs one Info block,
once per session, from the first `Initialize` — the slider and value-text paths,
the chosen row, the row's parent and its components, the parent's children in
order, the full row subtree with each object's components and text, the slider's
`Navigation.mode` and current up/down links, and the SFX/music anchored positions
with the derived step. Open Settings → Audio once, then paste that block here and
replace this paragraph. Until then, treat everything above as the *strategy*, not
a description of what is there.

What to check in that block:

- Is the chosen row one row, or did the walk over-reach? (An error line right
  after it says so.)
- Does the row's parent carry a `LayoutGroup`? The next Info line names it, or
  reports the absolute-position offset it computed instead.
- Is `Navigation.mode` `Explicit`? If it is `Automatic`, the navigation splice is
  skipped by design and the Info line says so.

### The ticket's note about `WatchForChanges` is wrong

Ticket 06 justifies the `OnTabOpen` refresh with "MushroomSync's
`WatchForChanges` reloads it". It does not: `ConfigSync.WatchForChanges`
subscribes to `ConfigFile.SettingChanged` and *rebroadcasts* registered settings
from the server. It never re-reads the file, and Horn Volume is `Exclude`d so it
is never broadcast either. Usefully, that also means our slider cannot start a
write/reload feedback loop. The refresh is kept anyway — it is cheap, and it
covers a `.cfg` reloaded by any other means.

Writing `ConfigEntry.Value` both applies live and persists (BepInEx saves on set
by default), which is what makes the change listener one line. It does mean a
slider drag rewrites the `.cfg` per changed frame; the listener skips writes when
the value did not actually move, and the file is small.
## Why not ZSFX

`ZSFX` is the game's own sound component, and reaching for it is the obvious move:
it already handles concurrency limits, reverb by distance, randomised pitch and
volume, and fade-out. It was rejected, and it should not be retried.

ZSFX is built around *prefabs*, not around runtime clips. Its clips come from a
`m_audioClips` array populated in the editor, and its instances are expected to be
spawned from a prefab that `ZNetScene` knows about — the component's whole
lifecycle assumes a registered prefab and the hash registry that goes with it. A
Horn Call has neither: the clip is decoded out of this DLL's own resources at
runtime, and the sound is a transient one-shot with no networked object behind it.
Using ZSFX would mean fabricating a prefab at load, registering it, and then
overwriting `m_audioClips` on each instance — a lot of machinery to end up with
the same `AudioSource` that `HornAudio.Play` creates in nine lines.

What ZSFX offers over a plain `AudioSource` is also mostly not wanted here. Its
concurrency cap and randomised pitch are right for a hundred overlapping combat
sounds and wrong for a signal: a Horn Call is rare (there is a Horn Cooldown), and
it must sound the same every time, because a Listener judges distance from its
loudness. Randomising that would defeat the feature.

The one thing ZSFX is still needed for is finding the mixer group — see below.

## Finding the SFX mixer group

Horn Calls must follow the game's own Effects slider, which means routing the
`AudioSource` through the mixer group vanilla sound effects use. There is no
public handle on it. `AudioMan` exposes `m_masterMixer` (the `AudioMixer` itself),
`m_ambientMixer` and `m_guiMixer` (both `AudioMixerGroup`) — everything except the
one that is wanted. `AudioMan.GetSFXVolume()` / `SetSFXVolume(float)` are public
and static, and the mixer's exposed parameter is named `SfxVol`, but a parameter
value is not a group and cannot be assigned to `outputAudioMixerGroup`.

Two approaches were considered:

- **Read `SfxVol` and fold it into the source's `volume`.** Rejected: it
  duplicates the mixer's own maths, it needs re-reading whenever the player moves
  the slider mid-call, and it silently diverges the moment the game changes how
  that parameter maps to gain.
- **Borrow the group from something already routed to it.** Taken. Every vanilla
  `ZSFX` prefab carries an `AudioSource` whose `outputAudioMixerGroup` *is* the SFX
  group, so `HornAudio` walks `ZNetScene.instance.m_prefabs` on the first Horn
  Call, takes the first prefab with a `ZSFX` whose `AudioSource` has a non-null
  group, and caches it.

The scan is lazy because `ZNetScene.instance` does not exist at plugin load, and
it settles permanently once `ZNetScene` is up: the prefab list does not change
after `Awake`, so a full scan that found nothing will not find anything later
either, and re-walking a few thousand prefabs per Horn Call would be a waste.
Before `ZNetScene` exists the scan stays unsettled and is retried on the next
call.

**Group name observed: to be filled in from the first in-game run.** The mod logs
it once, at Info, as `SFX mixer group '<name>' taken from prefab '<prefab>'`.

A missing group is a degradation, not a failure. The call still plays; it just
sits outside the Effects slider, and a Warning says so once. This is why
`HornAudio.IsReady` deliberately does **not** include the group in its answer,
despite ticket 03's inline comment saying "clip loaded and mixer group found":
ticket 04 gates playback on `IsReady`, so folding the group into it would turn a
cosmetic fallback into total silence — the opposite of what the same ticket
specifies two paragraphs earlier. `IsReady` means "this process has an
`AudioListener` and the clip decoded", which is the question ticket 04 is actually
asking.

## Referencing UnityEngine.AudioModule from net472 costs two workarounds

Audible Horn is the first net472 project in this repo to use types out of
`UnityEngine.AudioModule`. Merely referencing the assembly is fine — the scaffold
did that from ticket 01 and nothing complained. Touching a type inside it is not,
and the two failures that follow look unrelated but are the same root cause.

1. **CS1705.** `UnityEngine.AudioModule` is built against netstandard **2.1**;
   a net472 target supplies the 2.0 facade, and the compiler refuses the moment it
   has to load a type from the assembly. The fix is a `<Reference
   Include="netstandard">` pointing at the game's own
   `valheim_Data/Managed/netstandard.dll`. Retargeting is not an option in either
   direction: netstandard2.1 cannot reference MushroomSync's net472, and net48 is
   still capped at netstandard 2.0.

2. **CS0518 on `AudioClip.SetData`**, caused by the fix for the first. With
   netstandard 2.1 in the compilation the compiler now sees Unity's
   `SetData(ReadOnlySpan<float>, int)` overload, and binding the call requires
   resolving `ReadOnlySpan<T>` — which net472 does not define and Unity's
   netstandard 2.1 only *forwards* to a Mono `mscorlib` this project does not
   reference. The array overload is an exact match and is still unreachable,
   because overload resolution has to type every candidate first. `WavLoader`
   therefore picks `SetData(float[], int)` by signature through reflection, once
   per process.

The general shape to expect: any Unity API with both an array and a
`Span`/`ReadOnlySpan` overload is unbindable from this project. Reach for
reflection at that one call site rather than restructuring the reference set —
pulling in the game's whole Mono BCL with `NoStdLib` was the alternative, and it
would make this mod the only one here that does not build like the others.

## net472 is forced, not chosen

`MushroomSync` targets `net472` and cannot be netstandard (its csproj carries the
reasoning: the game's UnityEngine assemblies are built against netstandard 2.1,
so a 2.0 target fails with CS1705, and a 2.1 target could not be referenced by the
net47x mods). Anything that references MushroomSync inherits that constraint.
vegvisir-compass is the mod that does not, and it is the mod that cannot use
sync.
