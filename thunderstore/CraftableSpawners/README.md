# Craftable Spawners

Build the natural creature spawners yourself. Five pieces appear under the
hammer's **Misc** tag once you have picked up the matching trophy:

| Piece | Spawns | Unlocked by | Default recipe |
|---|---|---|---|
| Evil bone pile | Skeletons | Skeleton trophy | 40 Bone fragments, 5 Skeleton trophies |
| Greydwarf nest | Greydwarves | Greydwarf trophy | 20 Greydwarf eyes, 10 Ancient seeds, 5 Greydwarf trophies |
| Body pile | Draugr | Draugr trophy | 40 Entrails, 5 Draugr trophies |
| Fire pillar | Surtlings | Surtling trophy | 20 Surtling cores, 20 Coal, 5 Surtling trophies |
| Bone pile | Tar blobs | Growth trophy | 40 Tar, 5 Growth trophies |

- No workbench needed: hammer and materials only. Ground placement, any biome.
- Spawn timers, caps and ranges are the vanilla spawner's. Fire pillar and Bone
  pile are rebuilt from the bone pile spawner and spawn every 20 seconds.
- Hammer-remove refunds the full recipe to your inventory. Destroying one in
  combat drops the refund on the ground instead.

## Configuration

`BepInEx/config/CraftableSpawners.cfg` on a client, or `config/bepinex/` on a
dedicated server. Each spawner can be turned off, and every recipe amount can
be changed. With `LockConfiguration` on, the server's values apply to every
connected client through Mushroom Sync.

## Requirements

Requires **Mushroom Sync**, installed on the server and every client.

Source and issues: https://github.com/NickSpinosa/Valheim_Mushroom_Mods
