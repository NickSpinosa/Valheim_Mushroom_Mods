using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace VegvisirCompass
{
    /// <summary>Injects the compass prefab into the item database as it is built.</summary>
    [HarmonyPatch(typeof(ObjectDB))]
    internal static class ObjectDbPatches
    {
        // Bodies are wrapped so a failure here can never abort the postfix chain for
        // the other mods sharing these patch points.

        [HarmonyPostfix]
        [HarmonyPriority(Priority.First)]
        [HarmonyPatch("Awake")]
        internal static void AwakePostfix(ObjectDB __instance)
        {
            try { CompassItem.EnsureRegistered(__instance); }
            catch (System.Exception e) { Plugin.Log.LogError("ObjectDB.Awake registration failed: " + e); }
        }

        // Fires when the world ObjectDB is merged over the main-menu one, which is
        // where a prefab registered too early would otherwise be dropped.
        [HarmonyPostfix]
        [HarmonyPriority(Priority.First)]
        [HarmonyPatch(nameof(ObjectDB.CopyOtherDB))]
        internal static void CopyOtherDbPostfix(ObjectDB __instance)
        {
            try { CompassItem.EnsureRegistered(__instance); }
            catch (System.Exception e) { Plugin.Log.LogError("ObjectDB.CopyOtherDB registration failed: " + e); }
        }
    }

    /// <summary>
    /// Makes the compass a networkable object so it can be dropped on the ground.
    ///
    /// Runs at Priority.First because ZNetScene.Awake is a crowded patch point: a
    /// postfix from another mod that throws will abort the rest of the chain, and an
    /// outdated mod doing exactly that would otherwise silently skip our
    /// registration. Our own body is wrapped so we never do that to anyone else.
    /// </summary>
    [HarmonyPatch(typeof(ZNetScene), "Awake")]
    internal static class ZNetSceneAwakePatch
    {
        [HarmonyPostfix]
        [HarmonyPriority(Priority.First)]
        internal static void Postfix(ZNetScene __instance)
        {
            try
            {
                // ZNetScene.Awake can run before ObjectDB has been populated, so make
                // sure the prefab exists before registering it for networking.
                CompassItem.EnsureRegistered(ObjectDB.instance);
                CompassItem.EnsureNetworkRegistered(__instance);
            }
            catch (System.Exception e)
            {
                Plugin.Log.LogError("Failed to register the compass with ZNetScene: " + e);
            }
        }
    }

    /// <summary>Sets up the RPC handlers once networking exists.</summary>
    [HarmonyPatch(typeof(Game), "Start")]
    internal static class GameStartPatch
    {
        [HarmonyPostfix]
        [HarmonyPriority(Priority.First)]
        internal static void Postfix()
        {
            try
            {
                CompassRpc.Register();

                // Safety net. If a misbehaving mod aborted the ZNetScene.Awake postfix
                // chain before we got there, the prefab would not be networkable and
                // dropping a compass on the ground would fail. This is a separate
                // patch chain, so it still gets a chance to put things right.
                CompassItem.EnsureRegistered(ObjectDB.instance);
                CompassItem.EnsureNetworkRegistered(ZNetScene.instance);
            }
            catch (System.Exception e)
            {
                Plugin.Log.LogError("Failed to initialise the compass on game start: " + e);
            }
        }
    }

    /// <summary>Clears server state when the session ends.</summary>
    [HarmonyPatch(typeof(ZNet), nameof(ZNet.Shutdown))]
    internal static class ZNetShutdownPatch
    {
        [HarmonyPostfix]
        internal static void Postfix()
        {
            LootCooldownRegistry.Reset();
            CompassRpc.Reset();
            MerchantPlacement.Reset();

            // Belt and braces. The lockout expires on its own deadline anyway, but
            // disconnecting mid-pan should not carry it into the next session.
            CompassPan.Reset();
        }
    }

    /// <summary>
    /// Replaces what happens when a player interacts with a Vegvisir.
    ///
    /// The original is skipped entirely, so Game.DiscoverClosestLocation never runs
    /// and no map pin is ever written. Instead the player is granted a compass, via
    /// the server so the cooldown can be enforced for everyone.
    ///
    /// Skipping vanilla means inheriting everything else it did. Revealing the map is
    /// not all a stone does: it can also grant progression keys, which have nothing to
    /// do with the map and must still happen. Those are replicated below.
    /// </summary>
    [HarmonyPatch(typeof(Vegvisir), nameof(Vegvisir.Interact))]
    internal static class VegvisirInteractPatch
    {
        // There is deliberately no setting to turn this off. It was one, and it was the
        // mod's own kill switch: a client setting it false fell through to vanilla
        // Vegvisir.Interact, which writes a permanent boss pin - on a server whose whole
        // point is that no pin is ever written. Anyone wanting vanilla stones uninstalls
        // the mod; installing it is the consent.
        [HarmonyPrefix]
        internal static bool Prefix(Vegvisir __instance, Humanoid character, bool hold, ref bool __result)
        {
            if (hold)
            {
                __result = false;
                return false;
            }

            Player player = character as Player;
            if (player == null || player != Player.m_localPlayer)
            {
                __result = false;
                return false;
            }

            // Before any compass work, and so on every path out of here. Vanilla sets
            // these once the hold check passes, regardless of what the locations do, so
            // a player who already carries every compass from this stone still gets the
            // key.
            ApplyStoneKeys(__instance, player);

            // The carry rule filters rather than refuses. A stone naming several places
            // grants one compass each, so already holding one of them must drop just
            // that target and still hand over the rest - refusing the whole interaction
            // would make Hildir's three dungeons unobtainable after the first.
            //
            // Matched on location name rather than pin name, because pin names are
            // display labels that stones can share.
            Inventory inventory = player.GetInventory();
            List<string> wanted = new List<string>();
            List<string> alreadyHeld = new List<string>();

            foreach (Vegvisir.VegvisrLocation location in __instance.m_locations)
            {
                if (string.IsNullOrEmpty(location.m_locationName)) continue;

                List<string> single = new List<string> { location.m_locationName };
                if (CompassItem.FindCompassForAny(inventory, single) != null)
                {
                    alreadyHeld.Add(location.m_locationName);
                    continue;
                }

                wanted.Add(CompassRpc.Encode(location.m_locationName, location.m_pinName));
            }

            if (wanted.Count == 0)
            {
                // Nothing left to give, so say so rather than appearing to do nothing.
                player.Message(MessageHud.MessageType.Center,
                    alreadyHeld.Count > 1
                        ? "You already carry every " + CompassItem.DisplayName + " from this stone"
                        : "You already carry that " + CompassItem.DisplayName);
                Plugin.Debug($"All {alreadyHeld.Count} target(s) already carried; nothing requested.");
                __result = true;
                return false;
            }

            if (alreadyHeld.Count > 0)
            {
                Plugin.Debug($"Skipping {alreadyHeld.Count} target(s) already carried; requesting {wanted.Count}.");
            }

            CompassRpc.RequestCompass(__instance.transform.position, wanted, player);
            __result = true;
            return false;
        }

        /// <summary>
        /// Grants the progression keys a stone carries, mirroring the tail of vanilla
        /// Vegvisir.Interact.
        ///
        /// Both are idempotent, so replaying them on a re-read costs nothing.
        /// SetGlobalKey routes to the server and lands in the world save;
        /// AddUniqueKey is per-character. Wrapped because a stone's keys must never
        /// be able to cost the player their compass.
        /// </summary>
        private static void ApplyStoneKeys(Vegvisir stone, Player player)
        {
            try
            {
                if (!string.IsNullOrEmpty(stone.m_setsGlobalKey) && ZoneSystem.instance != null)
                {
                    ZoneSystem.instance.SetGlobalKey(stone.m_setsGlobalKey);
                    Plugin.Debug($"Stone set the global key '{stone.m_setsGlobalKey}'.");
                }

                if (!string.IsNullOrEmpty(stone.m_setsPlayerKey))
                {
                    player.AddUniqueKey(stone.m_setsPlayerKey);
                    Plugin.Debug($"Stone set the player key '{stone.m_setsPlayerKey}'.");
                }
            }
            catch (System.Exception e)
            {
                Plugin.Log.LogWarning("Could not apply the stone's keys: " + e.Message);
            }
        }
    }

    /// <summary>
    /// Holds vanilla back from deleting a merchant's other candidate sites.
    ///
    /// Vanilla treats merchant camps as unique: placing one wipes the rest, so the first
    /// candidate anyone walks past decides where the trader lives forever. Blocking that
    /// until the site is settled keeps the choice open.
    /// </summary>
    [HarmonyPatch(typeof(ZoneSystem), nameof(ZoneSystem.RemoveUnplacedLocations))]
    internal static class RemoveUnplacedLocationsPatch
    {
        [HarmonyPrefix]
        internal static bool Prefix(ZoneSystem.ZoneLocation location)
        {
            try
            {
                if (!MerchantPlacement.IsActive) return true;

                MerchantDef def = MerchantLocator.ForLocation(SafePrefabName(location));
                if (def == null) return true;

                // Let it through only once the merchant has settled.
                return MerchantPlacement.IsLocked(def);
            }
            catch (System.Exception e)
            {
                Plugin.Log.LogWarning("RemoveUnplacedLocations guard failed: " + e.Message);
                return true;
            }
        }

        internal static string SafePrefabName(ZoneSystem.ZoneLocation location)
        {
            if (location == null) return "";
            if (!string.IsNullOrEmpty(location.m_prefabName)) return location.m_prefabName;
            return location.m_name ?? "";
        }
    }

    // Deliberately no patch on ZoneSystem.PlaceLocations.
    //
    // Blocking it looks like the obvious way to hold a camp back, and it is a trap.
    // SpawnZone calls PlaceLocations only when the zone has never been generated, then
    // calls SetZoneGenerated regardless of what happened inside. A zone whose placement
    // we skipped is therefore marked generated with nothing in it, for good, and that
    // candidate site can never host the merchant again. Deferring is done by managing
    // the trader instead - see MerchantPlacement.

    /// <summary>
    /// Settles a merchant where they stand the moment their shop is opened.
    ///
    /// StoreGui.Show runs on the client, which owns none of the rival traders' ZDOs, so
    /// the work itself has to happen on the server. The client only asks.
    /// </summary>
    [HarmonyPatch(typeof(StoreGui), nameof(StoreGui.Show))]
    internal static class StoreGuiShowPatch
    {
        [HarmonyPostfix]
        internal static void Postfix(Trader trader)
        {
            if (trader == null || !MerchantPlacement.IsActive) return;

            try
            {
                MerchantDef def = MerchantCatalog.ResolveForTrader(trader);
                if (def == null || MerchantPlacement.IsLocked(def)) return;

                CompassRpc.RequestLockIn(def, trader.transform.position);
            }
            catch (System.Exception e)
            {
                Plugin.Log.LogWarning("Merchant lock-in failed: " + e.Message);
            }
        }
    }

    /// <summary>Drives the placement tick and keeps clients in step.</summary>
    [HarmonyPatch(typeof(ZoneSystem), "Update")]
    internal static class ZoneSystemUpdatePatch
    {
        [HarmonyPostfix]
        internal static void Postfix(ZoneSystem __instance)
        {
            try
            {
                MerchantPlacement.ServerTick(__instance);
                MerchantPlacement.SyncClientState(__instance);
            }
            catch (System.Exception e)
            {
                Plugin.Log.LogWarning("Merchant placement tick failed: " + e.Message);
            }
        }
    }

    /// <summary>
    /// Turns distant biome lorestones into merchant pointers.
    ///
    /// A postfix rather than a prefix: the stone should still show its lore text as
    /// normal, with the compass granted on top. Stones nearer the world centre than
    /// their merchant's gate are left entirely alone.
    /// </summary>
    [HarmonyPatch(typeof(RuneStone), nameof(RuneStone.Interact))]
    internal static class RuneStoneInteractPatch
    {
        // Deliberately does not test __result. RuneStone.Interact ends in an
        // unconditional "return false" even on a successful read, so gating on it
        // would skip every stone.
        // Also has no off switch, for the same reason as the Vegvisir patch above. The
        // distance gates already serve the one legitimate use - a server wanting
        // merchants to stay a discovery challenge keeps its stones inside them - and a
        // boolean only let a client overrule that, since the server never checked it.
        [HarmonyPostfix]
        internal static void Postfix(RuneStone __instance, Humanoid character, bool hold)
        {
            if (hold) return;

            Player player = character as Player;
            if (player == null || player != Player.m_localPlayer) return;

            try
            {
                Vector3 stonePosition = __instance.transform.position;

                MerchantDef merchant = MerchantCatalog.ResolveForStone(stonePosition);
                if (merchant == null) return;

                if (!MerchantCatalog.IsPastGuidanceGate(merchant, stonePosition))
                {
                    Plugin.Debug($"Lorestone for {merchant.DisplayName} is inside its gate; lore only.");
                    return;
                }

                // Same per-target rule as Vegvisirs: one compass per destination.
                List<string> targets = new List<string> { merchant.LocationName };
                ItemDrop.ItemData duplicate = CompassItem.FindCompassForAny(player.GetInventory(), targets);
                if (duplicate != null)
                {
                    player.Message(MessageHud.MessageType.Center,
                        "You already carry a " + CompassItem.DisplayName + " - " + merchant.DisplayName);
                    return;
                }

                CompassRpc.RequestCompass(
                    stonePosition,
                    new List<string> { CompassRpc.Encode(merchant.LocationName, merchant.DisplayName) },
                    player);

                Plugin.Debug($"Lorestone guidance requested for {merchant.DisplayName}.");
            }
            catch (System.Exception e)
            {
                Plugin.Log.LogWarning("Merchant lorestone handling failed: " + e.Message);
            }
        }
    }


    /// <summary>
    /// Shows which boss a compass points at, by appending the target to the name
    /// wherever the game displays it.
    ///
    /// Done at display time rather than by renaming the item, because m_shared is a
    /// single object shared by every instance of the prefab: writing a per-compass
    /// name into it would rename them all, and would be lost on reload anyway.
    /// </summary>
    [HarmonyPatch(typeof(InventoryGrid), "CreateItemTooltip")]
    internal static class InventoryTooltipNamePatch
    {
        /// <summary>
        /// Replaces the call rather than running after it. UITooltip.Set renders the
        /// tooltip inside the call, so assigning m_topic afterwards changed the field
        /// but not what was on screen - and the next refresh, comparing the base name
        /// against our modified field, simply overwrote it again. The name has to go
        /// in through Set itself.
        /// </summary>
        [HarmonyPrefix]
        internal static bool Prefix(InventoryGrid __instance, ItemDrop.ItemData item, UITooltip tooltip)
        {
            if (tooltip == null || !CompassItem.IsCompass(item)) return true;

            try
            {
                tooltip.Set(CompassItem.GetDisplayName(item), item.GetTooltip(), __instance.m_tooltipAnchor);
                return false;
            }
            catch (System.Exception e)
            {
                Plugin.Log.LogWarning("Could not label the compass tooltip: " + e.Message);
                return true;
            }
        }
    }

    /// <summary>The same label, for a compass lying on the ground.</summary>
    [HarmonyPatch(typeof(ItemDrop), nameof(ItemDrop.GetHoverName))]
    internal static class ItemDropHoverNamePatch
    {
        [HarmonyPostfix]
        internal static void Postfix(ItemDrop __instance, ref string __result)
        {
            if (__instance?.m_itemData == null || !CompassItem.IsCompass(__instance.m_itemData)) return;

            try
            {
                __result = CompassItem.GetDisplayName(__instance.m_itemData);
            }
            catch (System.Exception e)
            {
                Plugin.Log.LogWarning("Could not label the dropped compass: " + e.Message);
            }
        }
    }

    /// <summary>
    /// Applies the per-target carry rule to a compass picked up off the ground.
    ///
    /// The rule was only ever enforced where compasses are granted, against the
    /// player's inventory. Dropping one takes it out of that inventory, so a player
    /// could drop a compass, loot the stone again, and pick the first back up to end
    /// up holding two for the same target. Pickup is the remaining way an item enters
    /// an inventory, so the check belongs here too.
    ///
    /// Humanoid.Pickup is the single funnel for both walking over an item and
    /// pressing use on it, so one patch covers both.
    /// </summary>
    [HarmonyPatch(typeof(Humanoid), nameof(Humanoid.Pickup))]
    internal static class PickupPatch
    {
        /// <summary>
        /// Rate limit for the refusal. Pickup can be attempted repeatedly - a held
        /// use key, or another mod driving it - and an unthrottled message would
        /// flood the centre of the screen.
        /// </summary>
        private const float MessageInterval = 2f;

        private static float _lastMessageTime = -999f;

        [HarmonyPrefix]
        internal static bool Prefix(Humanoid __instance, GameObject go, ref bool __result)
        {
            try
            {
                Player player = __instance as Player;
                if (player == null || player != Player.m_localPlayer || go == null)
                {
                    return true;
                }

                ItemDrop drop = go.GetComponent<ItemDrop>();
                if (drop == null) return true;

                // Custom data lives in the ZDO until this runs, and vanilla only calls
                // it further down Pickup than we sit. Without it a dropped compass
                // reads as having no target and slips straight through the check.
                // Load is guarded on the ZDO revision internally, so calling it early
                // costs nothing.
                drop.Load();

                if (!CompassItem.IsCompass(drop.m_itemData)) return true;

                // No location recorded means no identity to compare - console-spawned,
                // or looted before locations were stored. Let those through rather
                // than making them impossible to pick up.
                string location = CompassItem.GetLocationName(drop.m_itemData);
                if (string.IsNullOrEmpty(location)) return true;

                if (CompassItem.FindCompassForAny(player.GetInventory(),
                                                  new List<string> { location }) == null)
                {
                    return true;
                }

                if (Time.time - _lastMessageTime >= MessageInterval)
                {
                    _lastMessageTime = Time.time;

                    string boss = CompassItem.GetLocalizedBossName(drop.m_itemData);
                    player.Message(MessageHud.MessageType.Center,
                        string.IsNullOrEmpty(boss)
                            ? "You already carry that " + CompassItem.DisplayName
                            : "You already carry a " + CompassItem.DisplayName + " - " + boss);
                }

                Plugin.Debug($"Refused pickup: already carrying a compass for {location}.");
                __result = false;
                return false;
            }
            catch (System.Exception e)
            {
                // Never let a fault here stop a player picking up their own items.
                Plugin.Log.LogWarning("Compass pickup guard failed: " + e.Message);
                return true;
            }
        }
    }

    /// <summary>
    /// Owns the camera pan a compass starts: holding look input off for its duration,
    /// and easing the pitch level alongside the yaw.
    ///
    /// Vanilla's eased turn does not steer the camera - SetLookDir with a transition
    /// time re-assigns m_lookYaw from a lerp once per frame, and Player.SetMouseLook
    /// writes that same field from mouse input every frame. Two writers, one field, no
    /// arbitration: whichever ran last won, so nudging the mouse cancelled the pan.
    /// Holding look input off for the transition makes the swing atomic.
    ///
    /// Pitch has to be eased here by hand. It lives in the separate field
    /// Player.m_lookPitch, which no vector passed to SetLookDir can reach, so vanilla's
    /// transition leaves it alone entirely.
    /// </summary>
    internal static class CompassPan
    {
        /// <summary>
        /// Hard ceiling on the lockout, whatever Compass.LookSmoothing is set to. Losing
        /// the camera for a moment is a pan; losing it for twenty seconds is a bug, and
        /// the config allows values that long.
        /// </summary>
        private const float MaxLockoutSeconds = 6f;

        /// <summary>Margin past the transition so the last frame is still covered.</summary>
        private const float LockoutGrace = 0.25f;

        private static bool _active;
        private static float _startPitch;
        private static float _expiresAt;

        /// <summary>
        /// Whether look input is currently held off. Deliberately also false once the
        /// deadline passes, so a pan interrupted by death, a teleport or a disconnect
        /// can never leave the player without camera control.
        /// </summary>
        internal static bool LookLocked => _active && Time.time < _expiresAt;

        internal static void Begin(Player player, float seconds)
        {
            _active = true;
            _startPitch = player.m_lookPitch;
            _expiresAt = Time.time + Mathf.Min(seconds, MaxLockoutSeconds) + LockoutGrace;

            Plugin.Debug($"Panning the camera over {seconds:0.#}s; look input held until then.");
        }

        internal static void Reset()
        {
            _active = false;
            _expiresAt = 0f;
        }

        /// <summary>
        /// Carries the pitch down to level in step with vanilla's yaw lerp, reading the
        /// same transition clock so the two axes land together.
        /// </summary>
        internal static void Advance(Player player)
        {
            if (!_active) return;

            float total = player.m_lookTransitionTimeTotal;
            float remaining = player.m_lookTransitionTime;

            if (remaining <= 0f || total <= 0f || Time.time >= _expiresAt)
            {
                player.m_lookPitch = 0f;
                Plugin.Debug($"Pan finished; pitch {_startPitch:0.#} -> 0.");
                Reset();
                return;
            }

            // Mirrors the form of vanilla's own yaw lerp, which runs from the target back
            // towards the start as the remaining time falls to zero.
            player.m_lookPitch = Mathf.Lerp(0f, _startPitch, Mathf.SmoothStep(0f, 1f, remaining / total));
        }
    }

    /// <summary>Eases the pitch alongside vanilla's yaw transition.</summary>
    [HarmonyPatch(typeof(Character), "UpdateLookTransition")]
    internal static class LookTransitionPatch
    {
        [HarmonyPostfix]
        internal static void Postfix(Character __instance)
        {
            if (!(__instance is Player player) || player != Player.m_localPlayer) return;

            try { CompassPan.Advance(player); }
            catch (System.Exception e)
            {
                // Never leave the camera mid-pan if this throws.
                CompassPan.Reset();
                Plugin.Log.LogWarning("Compass pan failed: " + e.Message);
            }
        }
    }

    /// <summary>Holds mouse look off while a compass is panning the camera.</summary>
    [HarmonyPatch(typeof(Player), nameof(Player.SetMouseLook))]
    internal static class MouseLookPatch
    {
        [HarmonyPrefix]
        internal static bool Prefix(Player __instance)
        {
            if (!CompassPan.LookLocked) return true;

            // Never take the camera off anyone but the player who read the compass.
            return __instance != Player.m_localPlayer;
        }
    }

    /// <summary>
    /// Handles using a compass: aim the camera at the stored target, spend a use,
    /// and destroy the item once it is spent.
    /// </summary>
    [HarmonyPatch(typeof(Humanoid), nameof(Humanoid.UseItem))]
    internal static class UseItemPatch
    {
        /// <summary>
        /// Minimum seconds between uses, so one keypress cannot burn two. An
        /// implementation detail rather than a setting: there is no reason to tune it,
        /// and nothing to gain from doing so - a use still costs a durability point
        /// either way.
        /// </summary>
        private const float SpamGuardSeconds = 1f;

        private static float _lastUseTime = -999f;

        [HarmonyPrefix]
        internal static bool Prefix(Humanoid __instance, Inventory inventory, ItemDrop.ItemData item)
        {
            if (!CompassItem.IsCompass(item))
            {
                return true;
            }

            Player player = __instance as Player;
            if (player == null || player != Player.m_localPlayer)
            {
                return false;
            }

            // Guards against a double-trigger burning two uses on one keypress.
            if (Time.time - _lastUseTime < SpamGuardSeconds)
            {
                Plugin.Debug("Compass use ignored - within the spam guard window.");
                return false;
            }

            _lastUseTime = Time.time;

            if (!CompassItem.TryGetTarget(item, out Vector3 target))
            {
                player.Message(MessageHud.MessageType.Center, "The runes are blank");
                Plugin.Log.LogWarning("A compass had no usable target stored.");
                return false;
            }

            // Out of range costs nothing: no use is spent and the camera does not
            // move. Compasses with no origin recorded - console-spawned, or looted
            // before ranges existed - are left unrestricted rather than bricked.
            float range = CompassItem.GetRange(item);
            if (range > 0f && CompassItem.TryGetOrigin(item, out Vector3 origin))
            {
                float distance = CompassItem.HorizontalDistance(origin, player.transform.position);
                if (distance > range)
                {
                    player.Message(MessageHud.MessageType.Center,
                        "Nothing happens, you are too far from the vegvisir");
                    Plugin.Debug($"Compass out of range: {distance:0}m from its stone, limit {range:0}m.");
                    return false;
                }

                Plugin.Debug($"Compass in range: {distance:0}m from its stone, limit {range:0}m.");
            }

            // Turn to face the boss, and level the view on the horizon.
            //
            // SetLookDir only moves a Player's yaw. Pitch lives in the separate
            // private field Player.m_lookPitch, which the eye rotation is rebuilt
            // from every frame:
            //
            //     m_eye.rotation = m_lookYaw * Quaternion.Euler(m_lookPitch, 0, 0)
            //
            // so no vector passed to SetLookDir can affect it. Levelling the view
            // means zeroing that field directly.
            Vector3 direction = target - player.transform.position;
            direction.y = 0f;

            // Guard on the flattened vector: a target directly overhead has no
            // horizontal component to point at.
            float smoothing = Mathf.Max(0f, Plugin.LookSmoothing.Value);

            if (direction.sqrMagnitude > 0.001f)
            {
                // Start the lockout before the pan, so no frame of the transition is
                // left open to mouse input.
                if (smoothing > 0f) CompassPan.Begin(player, smoothing);

                player.SetLookDir(direction.normalized, smoothing);
                Plugin.Debug($"Aimed at {target}, {direction.magnitude:0}m away on the horizontal.");
            }
            else
            {
                Plugin.Debug("Target is directly above or below; skipping the camera swing.");
            }

            // With a pan running the pitch is carried down to level alongside the yaw,
            // one axis per writer but a single curve. Without one there is nothing to
            // ride, so it levels at once.
            if (smoothing <= 0f)
            {
                float previousPitch = player.m_lookPitch;
                player.m_lookPitch = 0f;
                Plugin.Debug($"Levelled the view, pitch {previousPitch:0.#} -> 0.");
            }

            // Pin names come from the Vegvisir as localization tokens such as
            // "$enemy_dragon", so they have to be resolved before being shown.
            string bossName = Localization.instance.Localize(CompassItem.GetBossName(item));
            Inventory owning = inventory ?? player.GetInventory();

            item.m_durability -= 1f;
            bool spent = item.m_durability <= 0f;

            if (spent)
            {
                owning?.RemoveItem(item);
                player.Message(MessageHud.MessageType.Center,
                    string.IsNullOrEmpty(bossName)
                        ? "The compass crumbles to dust"
                        : bossName + " - the compass crumbles to dust");
                Plugin.Debug($"Compass spent on {bossName}; item destroyed.");
            }
            else
            {
                int remaining = Mathf.Max(0, Mathf.RoundToInt(item.m_durability));
                player.Message(MessageHud.MessageType.Center,
                    string.IsNullOrEmpty(bossName)
                        ? $"{remaining} use(s) remaining"
                        : $"{bossName} - {remaining} use(s) remaining");
                Plugin.Debug($"Compass used on {bossName}; {remaining} use(s) remaining.");
            }

            return false;
        }
    }
}
