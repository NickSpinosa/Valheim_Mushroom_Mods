# Why only group portals are indestructible

A group portal and a portal a player crafts from the hammer are the same prefab
(`Game.m_portalPrefabs[0]`, the wood portal piece). The group tag is a ZDO
string (`separate_spawns_group`) written on the instance. An empty string, or a
`GroupPortalMarker` with no group name, is a crafted portal.

## Hammer dismantle is not damage

Swinging a weapon at a piece calls `WearNTear.ApplyDamage`. The hammer does not.
Middle-mouse remove and the build-menu remove piece both call
`Player.RemovePiece`. That method never calls `ApplyDamage`. It returns
immediately when `Piece.m_canBeRemoved` is false, then checks no-build, ward,
and workbench range, then `WearNTear.Remove()`.

A prefix on `ApplyDamage` therefore cannot make a crafted portal removable with
the hammer, and cannot keep a meadows group portal from being dismantled. Both
gates have to key off the group name:

- `ApplyDamage` skips only when `IsProtectedPortal` is true (non-empty group
  name on the marker or the ZDO). A marker component by itself is not enough.
- `Player.RemovePiece` does the same, and tells the player the group portal
  cannot be removed.
- The instance's `Piece.m_canBeRemoved` is set false for group portals and
  forced true for everything else, in `TeleportWorld.Awake` and again after
  `GroupPortalMarker.Initialize`. `Awake` runs before a freshly placed group
  portal writes its ZDO key, so `Initialize` is the one that locks that
  instance down.

## Do not edit the shared prefab

`m_portalPrefabs[0]` is also the piece on the hammer. Writing
`m_canBeRemoved = false` on that object (or adding `GroupPortalMarker` to it)
makes every portal the player places inherit the group-portal treatment: no hammer
remove, and the tag editor replaced with "this portal's tag is fixed". A marker
component's `GroupName` field is the same trap if it was copied from that
prefab — the name is not saved on the instance. The only thing that means
"this is a group portal" is the instance ZDO key `separate_spawns_group`.
Set `m_canBeRemoved` on the instance after it exists. Never on the prefab.
Lock `Interact` / `SetText` / `RPC_SetTag` the same way, and leave those
methods alone when the key is empty so a crafted portal still opens the
vanilla tag prompt.
