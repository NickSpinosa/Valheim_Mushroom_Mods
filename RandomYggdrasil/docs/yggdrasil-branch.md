# The one scene object this mod depends on

Everything here hangs on a single `GameObject.Find`. This file records what that
object is, that it survived Valheim 1.0, and how to check the next time someone
suspects it has not.

## Where the branch lives

`_GameMain/_Environment/YggdrasilBranch`, active, in the `main` scene. Its
siblings under `_Environment` are `CloudCylinder`, `Clouds`,
`Directional Light`, `Distant_fog_planes`, `FollowPlayer`, `OceanMist`, `Rain`,
`Thunder` and `WaterPlane` — the branch is scenery bolted to the environment
root, not a networked prefab, which is why it is found by name and rotated
directly rather than going anywhere near `ZNetScene`.

`GameObject.Find` searches by name across the whole active scene, so the path
above is documentation, not a lookup key. It matters only when the name stops
working and someone needs to know where to look.

## 1.0.7 status: unchanged (issue #14)

Checked 2026-09-09 against a 1.0.7 install. Deep North reworked the world's
northern edge but did not touch this object: it is still there, still spelled
`YggdrasilBranch`, still active. **No rename was needed.** The mod's own
`Applied rotation` log line on a live 1.0.7 world is the last confirmation, and
is the only part of #14 that a human still has to do.

## How that was checked without launching the game

1.0 moved prefabs and scenes into SoftRef bundles under
`valheim_Data/StreamingAssets/SoftRef/Bundles/`, named by hash. `manifest_extended`
beside them is plain text and maps every asset path to its bundle, so the main
scene's bundle is whichever one holds `Assets/Scenes/main/LightingData.asset`
— `d59cfac` in 1.0.7, but look it up rather than trusting that.

A grep for the raw string is a useful smoke test — the bundle is LZ4-compressed
but names often survive as literals — though it cannot tell a GameObject from a
mesh or a material. `Assets/world/Props/Yggdrasil/` ships `yggdrasil_branch.obj`
and `yggdrasil_branch.mat`, both lowercase, and those are the assets the object
uses, not the object.

To answer it properly, walk the bundle with
[UnityPy](https://pypi.org/project/UnityPy/) (`pip install UnityPy`), read every
`GameObject`, and filter on the name. That is what produced the hierarchy above.
Two `YggdrasilBranch` objects come back, both under an `_Environment` with an
identical child list: one is the standalone `Assets/Systems/_Environment.prefab`,
the other is the copy nested inside `Assets/Systems/_GameMain.prefab`. Only
`_GameMain` gets instantiated, so exactly one branch is live and
`GameObject.Find` is unambiguous. Do not read "two results" as "two in the
scene".

## What happens now when it is missing

Before this, a missing branch meant the mod silently did nothing — the failure
that made #14 hard to rule out from a log. `FindYggdrasilBranch` now logs three
lines, once per process, on the first miss:

- the name it looked for, the vanilla path, the active scene, and whether this
  is a client or a dedicated server;
- the scene's root objects;
- every object *in the active scene* whose name contains `ygg`, with its full
  path, inactive ones flagged.

That third line is the one that pays off. It includes inactive objects on
purpose, because `GameObject.Find` skips those and "someone deactivated it" and
"someone renamed it" produce the same silence otherwise. It is restricted to the
active scene because `Resources.FindObjectsOfTypeAll` otherwise drags in
thousands of loaded prefab assets. If it comes back empty, the branch was
removed rather than renamed, and this mod needs a different approach, not a new
string.

The warning also fires on a dedicated server, where the pre-existing code has
always allowed for the object being absent (`no scene object on this instance`).
If a 1.0.7 server turns out to genuinely lack it, drop that case to `Debug.Log`
rather than leaving a warning on every server start — but confirm it first, as
`_Environment` also carries `WaterPlane`, which a headless server does need.
