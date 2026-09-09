# Valheim 1.0 build UI, and what it costs this mod

Written for issue #15, checked against 1.0.7 on 2026-09-09. Read this before
touching `ConfigurePiece`, `EnsurePiecesInHammerTables`, or anything in
`Patches.cs`.

## The one thing that actually needed fixing: `Piece.m_usage`

1.0 replaced the old category grid with `BuildUi`, which shows four tabs built
in this order (`BuildUi.Awake`):

| Tab | Class | Tags come from |
|---|---|---|
| `$hud_byusage` (opens here) | `ByUsagePieceList` | `Piece.m_usage` |
| `$hud_bymaterial` | `ByMaterialPieceList` | each piece's `m_resources` |
| `$hud_recent` | `RecentPieceList` | player history |
| `$hud_favorites` | `FavoritePieceList` | player-defined |

Every tab lists `PieceTable.m_availablePieces` and narrows it by the selected
tag, with tag id `-1` meaning "All". `ByUsagePieceList.GetAvailablePiecesWithTag`
keeps a piece only when `piece.m_usage.HasFlag(tag)`.

`Piece.UsageTagFlags` starts at `Misc = 1`. **There is no zero member.** A piece
left at the default `m_usage == 0` therefore matches no tag at all, and is
visible only while "All" is selected — which is where the menu happens to open,
so the pieces do not vanish outright, they just fall out of the list the moment
the player touches a tag. That is the failure this mod had: it set
`m_category` and never `m_usage`. `ConfigurePiece` now sets
`UsageTagFlags.Misc`, matching its `PieceCategory.Misc`.

`m_usage` is read in exactly two other places, both harmless here:
`Piece.CheckClusteredBuildPieceStats` counts placed pieces per flag into
`PlayerStatType` 171+, which feeds the lenient build achievements. Tagging the
spawners `Misc` makes them count as misc builds, which is what a vanilla piece
in that category would do.

## What did not need fixing

- **Adding to `PieceTable.m_pieces` still works.** `PieceTable.UpdateAvailable`
  still walks `m_pieces` and fills `m_enabledPieces`, `m_availablePieces` and
  `m_availablePiecesByCategory` from it. The prefix on
  `Player.UpdateAvailablePiecesList` still lands before that walk, and `BuildUi.Update`
  rebuilds its buttons whenever `m_availablePieces.Count` changes, so a piece
  added mid-session appears without any extra poke.
- **`m_category` still matters**, even though the new UI does not group by it.
  `PieceTable.GetPiece(category, Vector2Int)` and everything built on it
  (selection, gamepad navigation) still index `m_availablePiecesByCategory`.
  Keep setting both fields.
- **The "by material" tab needs nothing.** It derives its tags from
  `m_resources`, which `ApplyRecipeAndIcon` already fills, so the spawners show
  up under their own ingredients. A spawner whose recipe ends up empty (every
  amount configured to 0, or every ingredient prefab missing) is the one case
  that falls back to "All" only.
- **`Piece.SetCreator(long, PlatformUserID)`** gained its second parameter, but
  the patch is name-only with a postfix that takes just `__instance`, so Harmony
  still binds it. `Player` calls it in one place on placement, as before.

## Patch targets, resolved against 1.0.7

All eleven still resolve, each to exactly one method — no overload was added
that would make a name-only `[HarmonyPatch]` ambiguous.

| Target | 1.0.7 signature |
|---|---|
| `ZNetScene.Awake` | `void ()` |
| `ObjectDB.Awake` | `void ()` |
| `ObjectDB.CopyOtherDB` | `void (ObjectDB)` |
| `Player.OnSpawned` | `void (bool)` |
| `Player.AddKnownItem` | `void (ItemDrop.ItemData)` |
| `Player.UpdateAvailablePiecesList` | `void ()` |
| `Player.RemovePiece` | `bool ()` |
| `Piece.SetCreator` | `void (long, PlatformUserID)` |
| `SpawnArea.SpawnOne` | `bool ()` |
| `WearNTear.Destroy` | `void (HitData, bool)` |
| `Destructible.Destroy` | `void (HitData)` |

To redo this after the next update, read the `[HarmonyPatch]` attributes out of
the built DLL with Mono.Cecil and look each one up in `assembly_valheim.dll`;
the check is worth more than reading `Patches.cs` by eye, because it catches a
*new overload* as well as a removal.

## Checking prefab names without launching the game

1.0 moved prefabs into SoftRef bundles, but the index beside them is plain text:

```bash
D=~/.local/share/Steam/steamapps/common/Valheim/valheim_Data/StreamingAssets/SoftRef
grep -c "/BonePileSpawner\.prefab$" $D/manifest_extended
```

Anchor the pattern on `/` and `.prefab$` or `BonePileSpawner` also matches
`BonePileSpawner_swamp`. Every name this mod resolves — the three clone sources,
`Spawner_imp_respawn`, `Spawner_BlobTar_respawn_30`, `Surtling`, `BlobTar`, the
five trophies, `Hammer`, and the `lox_ribs` / `bonfire` / `fire_pit` /
`piece_groundtorch*` / `hearth` visuals — is present in 1.0.7.

The exception is `wood_wall`, the first entry in `FindPlaceEffectSource`'s
fallback chain: no such prefab exists (1.0 has `wood_wall_quarter`,
`piece_dvergr_wood_wall`, and so on, but not the bare name). It has always been
a dead lookup. `ZNetScene.GetPrefab` returns null silently, the chain falls
through to `wood_floor`, and the place effect is found — so this costs one
dictionary miss at init and nothing else. Left in place rather than churned.

**Caveat on this technique:** presence in the manifest proves the asset ships,
not that `ZNetScene.GetPrefab` will return it. `m_namedPrefabs` is built in
`ZNetScene.Awake` from `m_prefabs` and `m_nonNetViewPrefabs`, which are a
curated subset. Absence from the manifest is conclusive; presence is strong
evidence that still wants the in-game confirmation below.

## Still needs a human on a 1.0.7 client

Nothing above touches runtime behaviour, which is the rest of #15:

- All five pieces appear under the hammer, and stay visible after clicking the
  "misc" usage tag and an ingredient tag.
- Each is unlocked by its trophy, both on pickup and retroactively on load.
- They place, and spawn on the 20 s cadence.
- Hammer-remove refunds to inventory; combat destruction drops the refund as
  world pickups.
- No `Could not find source prefab` in the log at startup.
