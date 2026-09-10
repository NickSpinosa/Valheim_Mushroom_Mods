# An item you can place, and the piece it places

Why `SuperMistTorch` is **two** prefabs — `SuperMistTorch` (the item Haldor sells) and
`SuperMistTorchPiece` (the thing that gets placed) — after being one for three
revisions.

> **If you are here to change something, read [Why one prefab cannot be
> both](#why-one-prefab-cannot-be-both) first.** For three revisions this file argued
> that one prefab should be both halves, because that is a genuine vanilla shape. It
> is a genuine vanilla shape, and it still could not work here — it took a live bug
> (#42) to find out why. Everything before that section is reasoning that is still
> true and still load-bearing: how the item reaches the right hand, where `ItemData`
> comes from, and how to read prefabs without launching the game.

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

`BuildItemPrefab` clones a vanilla item (`Wood`) whole and edits the copy. `ItemData`
and `SharedData` are plain `[Serializable]` classes, so `Instantiate` deep-copies them
and the edits cannot leak back into Wood.

*(Earlier revisions kept only the data — instantiating `Wood`, lifting its `ItemData`
onto the piece, and destroying the scratch GameObject. Cloning the whole prefab is what
gives the item its own `Rigidbody` and `ZSyncTransform`, so a dropped torch falls and
can be walked over. The cost is cosmetic and confined to that state: a dropped torch
wears Wood's model until picked up.)*

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
| `Player.UpdatePlacement` | **the one this table missed.** See below — it is what broke hammer removal |

The first two read as what we want. The fourth is the one that was not enumerated, and
it is the reason this whole design had to be undone.

*(Historical note: this section originally concluded that a `Rigidbody` was not worth
adding back, because it would put physics on a 2× scaled torch whose colliders were
authored static. That trade-off is real, and it is moot now — the item and the piece
are separate prefabs, so the item can carry a Rigidbody and the piece never does.)*

## Why one prefab cannot be both

`IsPiece()` has a fourth caller, and it is the gate that decides whether the hammer
will even attempt a removal (`Player.UpdatePlacement`, 1.0.7):

```csharp
bool flag = (rightItem.m_shared.m_buildPieces.m_canRemovePieces
             && (!hoveringPiece || (!feast && (!itemDrop || !itemDrop.IsPiece()))))
         || (rightItem.m_shared.m_buildPieces.m_canRemoveFeasts
             && (!hoveringPiece || (bool)feast || ((bool)itemDrop && itemDrop.IsPiece())));
```

`itemDrop` is the `ItemDrop` on the object under the cursor. Hovering a placed torch
with a hammer: `m_canRemovePieces` is true, `hoveringPiece` is true, `feast` is null,
and `IsPiece()` is true — so the first clause is false. The second requires the
**Hammer's** `m_canRemoveFeasts`, which is false; that flag is the Feaster's.

So `flag` is false, `RemovePiece()` is never called, and none of its own checks
(`m_canBeRemoved`, `CheckCanRemovePiece`, …) are ever reached. The torch could not be
removed with a hammer at all. Its resources could not be refunded, and a misplaced one
was permanent.

This is not a bug in the gate. Vanilla is deliberately classifying "a placed thing that
is also an item" as feast-shaped and routing its removal to the tool that removes
feasts. Food and mead work precisely because they *are* that. Our torch is not, and
saying it was made the game treat it as one.

**There is no field that fixes this while one prefab is both halves.** Read the
predicate again:

```csharp
public bool IsPiece() => !m_body && m_piece && m_wnt;
```

A placed torch cannot have a `Rigidbody` — `MakePiece()` destroys it. It must keep its
`Piece`, and it must keep its `WearNTear` or it cannot be damaged or removed at all.
All three terms are forced, so `IsPiece()` is necessarily true, so the hammer is
necessarily diverted. The only way out is for the placed object not to be an item.

### What replaced it

Two prefabs, wired to each other:

- **`SuperMistTorch`** — cloned from a vanilla *item*. `ItemDrop`, `Rigidbody`, no
  `Piece`. Type `Tool` so it can reach the right hand, carrying its own one-entry
  `PieceTable` on `m_buildPieces` so equipping it enters build mode. Everything the
  ["Which tool opens the build menu"](#which-tool-opens-the-build-menu) section
  established still applies, unchanged — that reasoning was never the problem.
- **`SuperMistTorchPiece`** — the scaled Wisp Torch clone, with no `ItemDrop`.
  `m_resources` is the item ×1 with `m_recover = true`, so placing consumes the
  purchase and hammer-removal refunds it.

With no `ItemDrop` on the placed object, `itemDrop` at the gate is null, the first
clause is true, and the hammer behaves exactly as it does for every other piece in the
game. The refund still works because `m_recover` is a `Piece` mechanism and never
needed the item and the piece to be the same object.

The item keeps the name `SuperMistTorch` because the trade table and saved configs
refer to it, and because the item is what Haldor sells.

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

Removal stays with the hammer, and dropping the torch from the Hammer's table costs
nothing. `Player.RemovePiece` raycasts, checks `m_canBeRemoved`, and never consults the
piece table you happen to be holding.

> **That last sentence is true and was not enough.** `RemovePiece` really is
> table-agnostic — but its *caller* is not, and for three revisions this file cited the
> callee as though it settled the question. It gates on the hovered object's
> `ItemDrop.IsPiece()` before `RemovePiece` is reached, which is how a placed torch
> ended up unremovable while this paragraph said it could not be. See [Why one prefab
> cannot be both](#why-one-prefab-cannot-be-both). When a claim is about whether some
> vanilla behaviour happens, check the call site as well as the method.

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
