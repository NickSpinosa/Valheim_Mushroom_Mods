# 05 — Sounding the horn: input, cooldown, seated and swimming

**Goal:** pressing attack with the Signal Horn equipped makes a Horn Call.
The drink animation plays when the body is free. Seated on a boat, swimming
or riding, the call is made without standing up and without an animation.
The Horn Cooldown is enforced with a centre-screen message.

**Depends on:** 02, 04. **Unblocks:** 08.

## Facts you need (read from the decompiled `Player` and `Humanoid`)

- Input reaches the player through
  `Player.SetControls(Vector3 movedir, bool attack, bool attackHold, bool secondaryAttack, bool secondaryAttackHold, bool block, bool blockHold, bool jump, bool crouch, bool run, bool autoRun, bool dodge = false)`
  called every physics tick by the controller. `attack` is the button-down
  edge; `attackHold` is the held state.
- **First thing `SetControls` does:** if `IsAttached() || InEmote()` and any
  of movedir/attack/secondaryAttack/block/jump/crouch is set (and no doodad
  controller), it zeroes the attack flags, calls `StopEmote()` and
  `AttachStop()`. That is what stands a seated player up on attack. A prefix
  that clears `attack` before this runs prevents it.
- `Humanoid.StartAttack` refuses when `!CanMove()`, `InAttack()`,
  `InMinorAction()` etc. `Player.StartEmote` refuses when `IsAttached() ||
  IsAttachedToShip()`. So the vanilla animation path is unreachable while
  seated; that is fine, the design says sound-only there.
- When a player attaches to a ship seat with `hideWeapons` (rudder and
  benches do), `AttachStart` calls `HideHandItems()`, which moves
  `m_rightItem` into the **private** `m_hiddenRightItem`. Swimming also hides
  hand items (`Player.Update`: `ShowHandItems` only runs when not swimming).
  Therefore "horn equipped" must mean **either** `m_rightItem` **or**
  `m_hiddenRightItem` is the Signal Horn. Read the private field with
  `AccessTools.FieldRefAccess<Humanoid, ItemDrop.ItemData>("m_hiddenRightItem")`.
- Body-free test: `!player.IsAttached() && !player.IsRiding() &&
  !(player.IsSwimming() && !player.IsOnGround())`. `IsAttached()` covers
  rudder, benches, beds and chairs; `IsRiding()` the lox.
- `Player.Message(MessageHud.MessageType.Center, string)` shows the vanilla
  centre-screen hint.
- Ticket 02's `docs/DESIGN.md` section "TankardOdin as cloned" tells you the
  attack animation name and that stamina cost is 0, so a free-body attack never
  fails for want of stamina.

## Deliverables

### `src/HornBlower.cs`

```csharp
internal static class HornBlower
{
    internal const string CooldownMessage = "The horn still rings.";

    /// True when the local player holds a Signal Horn, visible or hidden.
    internal static bool HasHornEquipped(Player p);
    /// Attempts a Horn Call. Returns true if one was sent.
    internal static bool TryBlow(Player p);
    internal static void Reset();   // clears the cooldown clock on ZNet.Shutdown
}
```

`TryBlow`:

- If `Time.time - _lastCall < Plugin.Settings.Cooldown.Value`: `p.Message(Center, CooldownMessage)`
  and return false. Use `Time.time`, not the ZDO time; it is per client and
  monotonic.
- Else `_lastCall = Time.time; HornCall.Send(p); return true;`.
- A `Cooldown` of 0 must allow back-to-back calls (guard with `<`, not `<=`,
  and treat the very first call as always allowed).

### `src/Patches/SetControlsPatch.cs`

Harmony **prefix** on `Player.SetControls`, matched by full parameter type list
(12 parameters; use `HarmonyPatch(typeof(Player), nameof(Player.SetControls),
new[] { ... })` with the exact types so a future overload cannot hijack it).
Parameters by ref: `ref bool attack`. Body:

```
if (__instance != Player.m_localPlayer) return;     // remote players run SetControls too
if (!attack) return;                                 // edge only; ignore attackHold
if (!HornBlower.HasHornEquipped(__instance)) return;
bool bodyFree = <test above>;
bool sent = HornBlower.TryBlow(__instance);
if (!bodyFree) attack = false;   // sound-only: never let vanilla stand us up or start an attack
// bodyFree: leave attack = true so vanilla plays the drink animation, sent or not
```

Why the animation still plays during cooldown: the cooldown only gates the
sound; the player still visibly raises the horn, and the centre message says
why nothing was heard. That is the cheapest honest feedback.

Do **not** patch `Humanoid.StartAttack` or `Attack.Start`. They run after the
seated/swimming gates have already eaten the input.

`ZNet.Shutdown` postfix calls `HornBlower.Reset()`.

### Edge cases to handle explicitly

- Radial/emote menu open: vanilla already suppresses attack input; nothing
  extra.
- Attack while a Horn Call from the cooldown message is still on screen:
  message is re-shown, fine.
- Two horns (one hidden, one in hand): impossible, a Tool occupies the right
  hand; `HasHornEquipped` still checks both fields.
- Horn in hotbar but not equipped: no call. Pressing the hotbar key equips it
  via vanilla; the next attack blows it.
- Placing mode (`InPlaceMode()`): hammer is equipped, so the horn is not; no
  special case.

## Acceptance

Two clients on a server, default settings unless stated.

- Standing: attack with the horn plays the drink animation and both clients
  hear the call. Second press inside 10 s: animation plays, centre message
  "The horn still rings.", no sound anywhere.
- Host `Cooldown = 0`: rapid presses each produce a call.
- Seated at a boat rudder: attack makes the call, the player **stays seated**,
  no animation. Same on a bench. Same swimming (offshore, not touching
  bottom). Same riding a lox.
- On deck standing on a moving boat: animation plays; the other client hears
  the sound move with the boat (ticket 04's follow transform).
- Remote player sounding a horn does not trigger anything through this
  patch on the observing client (guarded by the `m_localPlayer` check); the
  observer hears it only via the RPC.
- Odin's Tankard and every other tool still behave as vanilla.
- `docs/DESIGN.md` gains "Why the attack is intercepted in SetControls".
