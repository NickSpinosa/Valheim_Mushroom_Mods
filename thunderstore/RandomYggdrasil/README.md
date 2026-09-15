# Random Yggdrasil

On a no-map server the Yggdrasil branch across the sky is a free compass: it
always points the same way. This mod gives each world its own random rotation
for the branch, so the sky stops telling you which way is north.

- The rotation is chosen once per world and stored in the config, keyed by
  world, so it never changes for an existing world.
- With `LockConfiguration` on, the server owns each world's rotation and every
  connected client uses the server's value through Mushroom Sync. All players
  see the same sky.
- Purely visual. Nothing about world generation or navigation changes.

## Configuration

`BepInEx/config/RandomYggdrasil.cfg` on a client, or `config/bepinex/` on a
dedicated server. A world's rotation can be set by hand if you want a specific
one.

## Requirements

Requires **Mushroom Sync**, installed on the server and every client.

Source and issues: https://github.com/NickSpinosa/Valheim_Mushroom_Mods
