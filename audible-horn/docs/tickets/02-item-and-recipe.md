# 02 — Signal Horn item and recipe

**Goal:** a craftable **Signal Horn** item that equips like a tankard and plays
the drink animation when the attack key is pressed. No sound yet.

**Depends on:** 01. **Unblocks:** 05.

## Facts you need

- The two vanilla horn-shaped items are `TankardOdin` (model `betahorn`) and
  `TankardAnniversary`. **Clone `TankardOdin`.** Its ItemDrop is an
  `ItemType.Tool` whose `m_attack` plays the drink emote. Keep all of that; we
  only rename and re-cost it.
- Item cloning precedent: `vegvisir-compass/src/CompassItem.cs`
  (`EnsureRegistered`, `EnsureNetworkRegistered`, `BuildPrefab`) and
  `vegvisir-compass/src/Patches.cs` (which hooks to register on). Copy the
  structure, not the compass-specific fields.
- ObjectDB exists in a stripped form on the main menu. The guard
  `odb.GetItemPrefab("Wood") == null → return` in the compass code is what
  avoids cloning from an incomplete database. Keep it.
- Recipes are `Recipe` ScriptableObjects in `ObjectDB.m_recipes`. Fields:
  `m_item` (ItemDrop), `m_amount`, `m_enabled`, `m_craftingStation`
  (CraftingStation), `m_minStationLevel`, `m_resources` (`Piece.Requirement[]`
  with `m_resItem`, `m_amount`, `m_amountPerLevel`, `m_recover`).
  `Piece.Requirement` construction precedent: `CraftableSpawners/SpawnerSetup.cs`
  near line 309.
- The workbench prefab is `piece_workbench`. Get its `CraftingStation` by
  scanning `odb.m_recipes` for a vanilla recipe whose
  `m_craftingStation != null && m_craftingStation.name == "piece_workbench"`
  and reusing that reference. This works inside `ObjectDB.Awake`, before
  ZNetScene exists. Fall back to `ZNetScene.instance?.GetPrefab("piece_workbench")`.

## Deliverables

### `src/SignalHornItem.cs`

```csharp
internal static class SignalHornItem
{
    internal const string PrefabName  = "SignalHorn";
    internal const string CloneSource = "TankardOdin";
    internal const string DisplayName = "Signal Horn";
    internal const string Description = "A horn with a carrying voice. Sound it and those nearby will know where you are.";

    internal static void EnsureRegistered(ObjectDB odb);        // item + recipe, idempotent
    internal static void EnsureNetworkRegistered(ZNetScene s);  // so a dropped horn is a network object
    internal static bool IsSignalHorn(ItemDrop.ItemData item);  // null-safe; match on m_shared.m_name == DisplayName
}
```

`BuildPrefab`:

- Instantiate `TankardOdin` under an inactive `DontDestroyOnLoad` container
  named `AudibleHornPrefabs`. The compass code comments why the container must
  be inactive; keep an equivalent comment.
- `clone.name = PrefabName`.
- On `m_shared`: set `m_name = DisplayName`, `m_description = Description`,
  `m_maxStackSize = 1`, `m_useDurability = false`, `m_canBeReparied = false`,
  `m_teleportable = true`, `m_value = 0`. Keep as cloned: `m_itemType` (Tool),
  `m_attack`, `m_icons`, `m_weight`, `m_equipEffect`, `m_holdAnimationState`,
  `m_animationState`. Clear food fields and `m_equipStatusEffect` defensively.
- Leave `drop.m_autoPickup` as cloned. The compass disables it for a reason
  that does not apply here.

`EnsureRegistered` also adds the recipe:

- One `Recipe` from `ScriptableObject.CreateInstance<Recipe>()`, `name =
  "Recipe_SignalHorn"`, `m_item` = the clone's ItemDrop, `m_amount = 1`,
  `m_enabled = true`, `m_minStationLevel = 1`, workbench station.
- Resources: `BoneFragments` ×4, `LeatherScraps` ×2, `Resin` ×1, each with
  `m_amountPerLevel = 0`, `m_recover = true`. Resolve with
  `odb.GetItemPrefab(name)?.GetComponent<ItemDrop>()`. If any is null, log an
  error naming it and do not add the recipe.
- Idempotent: skip when `odb.m_recipes` already holds a recipe whose `m_item`
  is our ItemDrop. `EnsureRegistered` runs several times per session.
- Call `odb.UpdateRegisters()` after adding the item, as the compass does.

### `src/Patches/RegistrationPatches.cs`

The same four hooks as `vegvisir-compass/src/Patches.cs`, each
`Priority.First`, each body in `try/catch`: `ObjectDB.Awake` postfix,
`ObjectDB.CopyOtherDB` postfix, `ZNetScene.Awake` postfix (item first, then
network), `Game.Start` postfix as the safety net. Read the comments in the
compass file for why each exists and keep equivalent ones.

### Diagnostics

Behind `private const bool DumpClonedAttack = false;`, on first successful
prefab build log at Info: `m_attack.m_attackAnimation`, `m_attack.m_attackType`,
`m_attack.m_attackStamina`, `m_holdAnimationState`, `m_animationState`,
`m_itemType`. Run it once with the flag on, then record the values in
`audible-horn/docs/DESIGN.md` under the heading "TankardOdin as cloned".
Ticket 05 needs the animation name and confirmation that stamina cost is 0.

## Acceptance

- In a single-player world with a workbench: Signal Horn appears in the
  workbench crafting list, costs 4 Bone Fragments, 2 Leather Scraps, 1 Resin,
  and needs no station level. Tooltip shows the description and no durability
  bar.
- Equipping puts the horn model in the right hand, looking like Odin's
  tankard. The attack key plays the drinking animation.
- Vanilla `TankardOdin` is unchanged: still craftable, still named Odin's
  Tankard.
- Dropping the horn on the ground and picking it up works. A dedicated server
  running the DLL logs no missing-prefab warnings for `SignalHorn`.
- `dotnet build` is clean.
- `docs/DESIGN.md` has the "TankardOdin as cloned" section.
