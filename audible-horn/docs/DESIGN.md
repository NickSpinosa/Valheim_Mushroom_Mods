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

## net472 is forced, not chosen

`MushroomSync` targets `net472` and cannot be netstandard (its csproj carries the
reasoning: the game's UnityEngine assemblies are built against netstandard 2.1,
so a 2.0 target fails with CS1705, and a 2.1 target could not be referenced by the
net47x mods). Anything that references MushroomSync inherits that constraint.
vegvisir-compass is the mod that does not, and it is the mod that cannot use
sync.
