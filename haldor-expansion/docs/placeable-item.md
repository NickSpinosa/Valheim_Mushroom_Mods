# One prefab that is both an item and a piece

Why `SuperMistTorch` adds an `ItemDrop` to a build piece, and why that is the vanilla
shape rather than a workaround.

## The failure

On 1.0.7 every registration pass logged

```
[Error  :Haldor Expansion] Cannot build SuperMistTorch: 'piece_groundtorch_mist' has no ItemDrop.
```

and the torch existed nowhere — not in Haldor's stock, not in any build menu.

`BuildPrefab` cloned the vanilla Wisp Torch and required the clone to carry both a
`Piece` and an `ItemDrop`, because the same prefab has to be the thing Haldor sells *and*
the thing that gets placed. Read out of the 1.0.7 bundle, the source carries:

| Component | |
|---|---|
| `Transform`, `LODGroup` | |
| `Piece` | `$piece_groundtorchdemister`, category `Furniture` |
| `ZNetView` | persistent, not distant |
| `WearNTear` | |
| `HoverText` | |

No `ItemDrop`. It is a **pure build piece**, built with the hammer from 1 YggdrasilWood +
1 Wisp, both recoverable. There is no item form of the wisp torch in vanilla and no
evidence there ever was one — this looks like an assumption that was never true rather
than a 1.0.7 regression, and the first in-game run is what surfaced it.

## What vanilla actually does

The "one prefab, both halves" idea is sound; it is just not what the wisp torch is.
Scanning the bundle for GameObjects carrying **both** an `ItemDrop` and a `Piece` finds
**120** of them, and they are all food and mead:

```
Honey
  Rigidbody, ZNetView, ZSyncTransform
  ItemDrop   autoPickup=1 autoDestroy=1 type=Consumable maxStack=50
  Piece      $item_honey, category=Feasts, craftingStation=none
      requires Honey x1 recover=1
  WearNTear  health=50
```

`Piece.m_resources` is **the item itself, x1, recoverable** — exactly the recipe
`SuperMistTorch` already wrote. So the design was right and only the clone source was
wrong.

## The engine drives the handover

Nothing has to patch placement. Both ends are already in the shipped code:

```csharp
// Player.PlacePiece, on the object it just instantiated
gameObject.GetComponent<ItemDrop>()?.MakePiece(sendRPC: true);

// Player, on the placement ghost
m_placementGhost.gameObject.GetComponent<ItemDrop>()?.MakePiece();
```

`ItemDrop.Awake` prepares for it — `m_piece = GetComponent<Piece>()`, and any `WearNTear`
is **disabled on spawn** — and `MakePiece` is what turns the item into a piece: destroy
the `Rigidbody`, move the collider to the `piece` layer, re-enable `WearNTear`, and set
the `s_piece` flag on the ZDO so the object comes back as a piece after a reload
(`Awake` re-runs `MakePiece` when it sees that flag).

The consequence worth remembering: **an `ItemDrop` on a piece is not a hack the game
tolerates, it is a case the game handles.** What it does *not* handle is the reverse —
there is no path that gives a piece an item form.

## Where the item data comes from

`AddItemHalf` copies `ItemData` off a vanilla item (`Wood`) by instantiating it and
keeping the copy:

```csharp
GameObject scratch = Object.Instantiate(template, _prefabContainer.transform);
ItemDrop.ItemData data = scratch.GetComponent<ItemDrop>().m_itemData;
Object.Destroy(scratch);
```

`ItemData` and `SharedData` are plain `[Serializable]` classes, so `Instantiate`
deep-copies them and the scratch GameObject can be thrown away while the data lives on.

**Do not build the `SharedData` with `new` instead.** It is the same trap
[trade-item-1.0.7.md](trade-item-1.0.7.md) describes from the other side: Unity authors
every array and string on a serialized class non-null, C# does not, and the throw lands
somewhere far from the construction site. The concrete one here is
`ItemData.GetIcon()`:

```csharp
public Sprite GetIcon() => m_shared.m_icons[m_variant];
```

No length check. An empty `m_icons` is not a missing picture, it is an exception while the
shop list is being *rendered*. The torch takes `Piece.m_icon` — the hammer-menu icon,
which is already a picture of this torch — and falls back to the template item's icon.

## No Rigidbody, deliberately

Vanilla food carries a `Rigidbody` (and `ZSyncTransform`); the wisp torch piece does not,
and none is added. That single omission decides three behaviours, through one method:

```csharp
public bool IsPiece() => !m_body && m_piece && m_wnt;
```

With no `Rigidbody`, `IsPiece()` is **always** true, in the inventory-dropped state as
well as the placed one. What that changes:

| Call site | Effect |
|---|---|
| `ItemDrop.TimedDestruction` | guarded on `!IsPiece()` — a bought torch is never auto-despawned after an hour |
| `Player.AutoPickup` | skips pieces — a placed torch is not vacuumed up by walking past it |
| `ItemDrop.Interact` | **not** guarded — manual pickup still works on a dropped one |

The first two are what we want and are the reason the Rigidbody is not worth adding back.
The cost is cosmetic and confined to a state the normal flow never reaches: a torch
*dropped* from the inventory rather than placed does not fall, and has to be picked up
with a keypress instead of by walking over it. Purchases go straight to the inventory, so
reaching that state means deliberately dropping a 100-coin placeable.

Adding a `Rigidbody` to buy back those two details would put physics on a 2× scaled torch
whose colliders were authored static, which is a worse trade than the one it fixes.

## Which tool opens the build menu

The item+piece pairing above says what the torch *is*. It says nothing about how a
player gets a placement ghost, and that was assumed to be the hammer for the first two
revisions of this file — the piece was injected into the Hammer's `PieceTable`, so a
bought item turned up in the same list as free-to-build structures and could not be
placed from the inventory at all (issue #39).

There is exactly one mechanism in the game for entering build mode, and it runs through
the right hand:

```csharp
// Humanoid.SetupEquipment, on every equip
if (m_rightItem != null && (bool)m_rightItem.m_shared.m_buildPieces)
{
    SetPlaceMode(m_rightItem.m_shared.m_buildPieces);
}
else
{
    SetPlaceMode(null);
}
```

So "placeable from the inventory" means two things, and both are required:

- **The item must be able to reach the right hand.** `Humanoid.EquipItem` routes only
  `Tool` (and weapon types) there. The torch was `ItemType.Material`, copied wholesale
  from `Wood`, and a Material is never `m_rightItem` — no amount of piece-table wiring
  would have helped while that was true.
- **The item must carry its own `PieceTable`** on `m_shared.m_buildPieces`. A
  `PieceTable` is a MonoBehaviour, so it needs a GameObject; the torch's lives on a child
  of the inactive prefab container. One piece, `m_hideAdvancedMenu = true` because tags
  and favourites over a single entry are noise.

The Hammer, Hoe and Cultivator are all this shape. 1.0 also ships `Feaster.prefab`
alongside `_FeasterPieceTable.prefab`, which is a non-tool precedent — though note the
Feaster is *reusable* and its pieces cost other items, so it is a precedent for the
mechanism and not for what follows.

### Being both the tool and the material is the part vanilla never does

The torch's `Piece.m_resources` is itself ×1, so placing the last one deletes the item
currently in the player's hand. Nothing in the game unequips an item that has left the
inventory — `Inventory.RemoveItem` does not, `Humanoid.UpdateEquipment` only drains
durability, and `Player.OnInventoryChanged` only registers what was gained. The case
simply does not arise in vanilla, because a hammer is never one of its own ingredients.

Left alone, the player keeps a phantom held item and an unplaceable ghost until they
switch tools. `SuperMistTorch.UnequipIfDepleted`, hung off a `Player.PlacePiece`
postfix, closes it: if the right hand is a torch that is no longer in the inventory,
unequip it. It returns immediately for every other placement in the game.

This is also why the stack size is 1. Vanilla stacks nothing equippable, and an item
that is consumed out of the inventory *while equipped* is already one unusual thing;
stacking it would have been two at once.

Removal is unaffected and stays with the hammer. `Player.RemovePiece` raycasts, checks
`m_canBeRemoved`, and never consults the piece table you happen to be holding — so
dropping the torch from the Hammer's table costs nothing.

## Reading prefab components without launching the game

The component lists above came out of the shipped bundle with
[UnityPy](https://github.com/K0lb3/UnityPy), the same way the Horn of Calling's item
fields were read. `piece_groundtorch_mist` lives in bundle `c4210710`:

```bash
strings valheim_Data/StreamingAssets/SoftRef/manifest_extended \
  | grep -B8 'piece_groundtorch_mist\.prefab'      # -> bundle: c4210710
```

Two things that will otherwise waste an afternoon:

- **A `MonoBehaviour`'s `m_Script` PPtr does not resolve inside the bundle** — the
  MonoScript lives in another file, so `m_ClassName` comes back empty. Identify components
  by the *field names* their typetree exposes instead: `m_primaryTarget` + `m_comfortGroup`
  is a `Piece`, `m_itemData` + `m_autoPickup` is an `ItemDrop`.
- **A `MonoBehaviour` has no `m_Name`.** A `Piece.m_resources[].m_resItem` points at an
  `ItemDrop` *component*, so resolving it to a readable name means walking up to its
  `m_GameObject` and reading the name there.
