using System;
using System.Collections.Generic;
using UnityEngine;

namespace VegvisirCompass
{
    /// <summary>
    /// The compass that finds a Jotun invasion, dropped in the hall that started it.
    ///
    /// Breaking the Malicious Ice at the bottom of Mörkhalla calls
    /// PersistentEventSystem.TriggerEvent, which places one invasion somewhere in
    /// the older biomes. The compass has to exist before that ice is broken, or
    /// there is nothing to follow to it. It is spawned where the hall ice stood
    /// after vanilla has tried to place the invasion. Three already being active
    /// places nothing new, and the compass is dropped anyway so those can be found.
    ///
    /// The aim target is not baked in. "Nearest" depends on where the player is
    /// when they read the compass, and invasions come and go, so the position is
    /// taken from the live event list at use time. That list is already on the
    /// client: the server pushes it for the no-map particle trail.
    /// </summary>
    internal static class InvasionCompass
    {
        private const string NoteHallRpc = "VC_NoteInvasionHall";

        /// <summary>Vanilla's placeholder when an event was given no map token.</summary>
        private const string UnknownEventToken = "$unknown_event";

        /// <summary>Shown when the event itself has no usable name.</summary>
        internal const string FallbackLabel = "Jotun Invasion";

        private struct PendingDrop
        {
            internal Vector3 Position;
            internal bool Cheated;
            internal string EventName;
        }

        private static bool _registered;
        private static int _eventsBeforeStart;
        private static readonly Dictionary<long, Queue<PendingDrop>> Pending = new Dictionary<long, Queue<PendingDrop>>();

        internal static void Register()
        {
            if (_registered || ZRoutedRpc.instance == null) return;

            ZRoutedRpc.instance.Register<Vector3, bool, string>(NoteHallRpc, RPC_NoteHall);
            _registered = true;
            Plugin.Debug("Registered invasion compass RPCs.");
        }

        internal static void Reset()
        {
            _registered = false;
            _eventsBeforeStart = 0;
            Pending.Clear();
        }

        /// <summary>
        /// Tells the server where the hall ice stood. Called before vanilla
        /// TriggerEvent, from the owner of that ice. The compass is dropped
        /// whether or not a new invasion was placed.
        /// </summary>
        internal static void NoteHall(Vector3 position, bool cheated, string eventName)
        {
            if (ZRoutedRpc.instance == null) return;
            ZRoutedRpc.instance.InvokeRoutedRPC(NoteHallRpc, position, cheated, eventName ?? "");
        }

        /// <summary>Runs on the server, before an invasion is placed.</summary>
        internal static void BeforeStart()
        {
            if (!IsServer()) return;
            _eventsBeforeStart = ActiveEvents()?.Count ?? 0;
        }

        /// <summary>
        /// Runs on the server after the start RPC, including when it throws.
        /// Drops the compass in the hall even when vanilla refused another
        /// invasion because some are already active.
        /// </summary>
        internal static void AfterStart(long sender)
        {
            if (!IsServer()) return;

            if (!TryDequeue(sender, out PendingDrop drop))
            {
                bool created = (ActiveEvents()?.Count ?? 0) > _eventsBeforeStart;
                if (created)
                {
                    Plugin.Log.LogWarning("An invasion started with no hall position recorded.");
                }
                return;
            }

            if (string.IsNullOrEmpty(drop.EventName))
            {
                Plugin.Log.LogWarning("Hall ice had no event name; no compass dropped.");
                return;
            }

            CompassItem.SpawnLive(
                drop.Position, drop.EventName, LabelFor(drop.EventName), drop.Cheated, snapToGround: false);
        }

        /// <summary>
        /// Nearest active event of this compass's kind, measured on the X/Z plane.
        ///
        /// Vanilla's no-map trail picks a nearest event by comparing distance to
        /// the candidate position's magnitude, which is how far that point is from
        /// the world origin. That is not distance from the player. This measures
        /// the player.
        /// </summary>
        internal static bool TryGetNearest(string eventName, Vector3 from, out Vector3 target)
        {
            target = Vector3.zero;
            if (string.IsNullOrEmpty(eventName)) return false;

            List<PersistentEventSystem.ActivePersistentEvent> list = ActiveEvents();
            if (list == null) return false;

            float best = float.MaxValue;
            bool found = false;

            foreach (PersistentEventSystem.ActivePersistentEvent active in list)
            {
                string name;
                try
                {
                    name = active.internalName;
                }
                catch (Exception)
                {
                    continue;
                }

                if (!string.Equals(name, eventName, StringComparison.OrdinalIgnoreCase)) continue;

                float distance = CompassItem.HorizontalDistance(from, active.position);
                if (distance >= best) continue;

                best = distance;
                target = active.position;
                found = true;
            }

            return found;
        }

        private static void RPC_NoteHall(long sender, Vector3 position, bool cheated, string eventName)
        {
            if (!IsServer()) return;

            if (!Pending.TryGetValue(sender, out Queue<PendingDrop> queue))
            {
                queue = new Queue<PendingDrop>();
                Pending[sender] = queue;
            }

            queue.Enqueue(new PendingDrop
            {
                Position = position,
                Cheated = cheated,
                EventName = eventName ?? ""
            });
            Plugin.Debug($"Hall noted for peer {sender} at {position}.");
        }

        private static string LabelFor(string eventName)
        {
            PersistentEventSystem system = PersistentEventSystem.instance;
            if (system?.m_possibleEvents == null) return FallbackLabel;

            foreach (PersistentEventSystem.PersistentEvent possible in system.m_possibleEvents)
            {
                if (!string.Equals(possible.internalName, eventName, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (!string.IsNullOrEmpty(possible.mapTokenString)
                    && possible.mapTokenString != UnknownEventToken)
                {
                    return possible.mapTokenString;
                }

                break;
            }

            return FallbackLabel;
        }

        private static List<PersistentEventSystem.ActivePersistentEvent> ActiveEvents()
        {
            return PersistentEventSystem.instance?.m_activePersistentEvents?.list;
        }

        private static bool IsServer()
        {
            return ZNet.instance != null && ZNet.instance.IsServer();
        }

        private static bool TryDequeue(long sender, out PendingDrop value)
        {
            value = default;
            if (!Pending.TryGetValue(sender, out Queue<PendingDrop> queue) || queue.Count == 0) return false;
            value = queue.Dequeue();
            return true;
        }
    }
}
