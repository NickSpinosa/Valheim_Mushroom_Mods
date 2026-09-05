# 04 — Horn Call over the network

**Goal:** a Horn Call made on one client is heard on every client whose local
player is within Hearing Range of the Blower, including the Blower, and on no
other. The Blower's own horn plays exactly once.

**Depends on:** 03. **Unblocks:** 05.

## Facts you need

- Valheim only loads objects a few zones around each player. A Listener at
  the edge of a large Hearing Range may not have the Blower's `Player` object
  loaded, so the call cannot be a sound attached to the Blower's prefab. It is
  a **routed RPC broadcast** carrying the Blower's position, and each receiving
  client decides audibility from its own local player's position.
- Routed RPC precedent in this repo: `Separate Spawns/SeparateSpawns/LayoutSync.cs`
  (`Register` guarding against re-registration when `ZRoutedRpc.instance` is
  replaced, `InvokeRoutedRPC(ZRoutedRpc.Everybody, ...)`). Handler signature is
  `(long sender, <params>)`. Registration must happen after `ZRoutedRpc`
  exists; `Game.Start` postfix is the hook the compass uses for its RPCs.
- Whether a broadcast to `ZRoutedRpc.Everybody` is also delivered to the
  sender's own handler is **not confirmed in this repo**. Determine it at
  runtime (below) rather than assuming.
- A dedicated server has no local player and relays routed RPCs without
  needing a handler, but this DLL runs there too, so the handler must return
  early when `Player.m_localPlayer == null`.
- `ZDOID` and `Vector3` are both supported routed-RPC parameter types.

## Deliverables

### `src/HornCall.cs`

```csharp
internal static class HornCall
{
    internal const string RpcName = "AudibleHorn.HornCall";

    internal static void Register();                 // idempotent, per ZRoutedRpc instance
    internal static void Reset();                    // on ZNet.Shutdown
    internal static void Send(Player blower);        // called by ticket 05
}
```

`Send(blower)`:

- `var zdoid = blower.GetZDOID(); var pos = blower.transform.position;`
- `ZRoutedRpc.instance.InvokeRoutedRPC(ZRoutedRpc.Everybody, RpcName, zdoid, pos);`
- Then, **only if** the runtime check below shows the sender does not receive
  its own broadcast, call `Receive` locally with the same arguments. Implement
  this as a `static bool SelfEchoConfirmed` decided once per session: on the
  first `Send`, set a flag `_awaitingEcho = true` and record the time; if the
  handler fires for our own zdoid within the same frame or the next, the echo
  exists. If after one second no echo arrived, play locally and remember to do
  so for the rest of the session. Log which case was observed, once, and
  record it in `docs/DESIGN.md` under "Does Everybody include the sender?".
  Simplify to the confirmed behaviour once known; leave the detection code out
  of the final commit if the answer is deterministic (it will be).

`Receive(long sender, ZDOID blower, Vector3 pos)` — the handler:

- Return if `Player.m_localPlayer == null` (server) or `!HornAudio.IsReady`.
- `float range = Plugin.Settings.HearingRange.Value;` (already the host's
  value when synced).
- `if (Vector3.Distance(Player.m_localPlayer.transform.position, pos) > range) return;`
  Straight-line 3D distance. This also keeps dungeons apart from the surface,
  since interiors sit thousands of metres up.
- Resolve `follow`: `ZNetScene.instance.FindInstance(blower)?.transform`, null
  if the Blower is not loaded here. A far Listener then hears a fixed
  position, which is the documented fallback.
- `HornAudio.Play(pos, follow, range, Plugin.Settings.HornVolume.Value);`

### Hooks

- `Game.Start` postfix (in the same `RegistrationPatches` file from ticket 02,
  or a sibling) calls `HornCall.Register()`.
- `ZNet.Shutdown` postfix calls `HornCall.Reset()` so the next session
  re-registers against the new `ZRoutedRpc` instance.

### Debug command

Extend ticket 03's console command with `horncall`, cheat-only, that calls
`HornCall.Send(Player.m_localPlayer)`. Ticket 05 wires the real trigger.

## Acceptance

Test with a dedicated server plus two clients (or host-as-server plus one
client; both should be tried before ticket 08 signs off).

- `horncall` on client A: A hears it once, not twice, not zero times.
- Client B standing 50 m from A hears it, quieter than A, panned toward A.
- Client B at 150 m (default range 100) hears nothing.
- Server console shows no errors when the RPC passes through.
- Host `HearingRange = 300`: B at 150 m now hears it. B's own `.cfg` still
  says 100 and is not rewritten.
- If the "self echo" answer turned out to be "no", the local play path is
  present and the doc section explains it. If "yes", there is no local play
  path and the doc section says why.
- `ZNet.Shutdown` then rejoin: `horncall` still works.
