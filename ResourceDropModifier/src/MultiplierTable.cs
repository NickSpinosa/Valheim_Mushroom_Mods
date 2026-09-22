using System;
using System.Collections.Generic;

namespace ResourceDropModifier
{
    /// <summary>
    /// The multipliers in force, keyed by item prefab name. Immutable once built so a
    /// table can be swapped atomically when the file is reloaded or a sync arrives.
    /// <para>
    /// The biome sections in the config are presentation only; at runtime a drop
    /// is looked up by the item alone. See "Grouping" in docs/DESIGN.md for why.
    /// </para>
    /// </summary>
    internal sealed class MultiplierTable
    {
        internal static readonly MultiplierTable Empty = new MultiplierTable(new Dictionary<string, float>());

        private readonly Dictionary<string, float> _byPrefab;

        internal MultiplierTable(Dictionary<string, float> byPrefab)
        {
            _byPrefab = new Dictionary<string, float>(byPrefab, StringComparer.Ordinal);
        }

        internal int Count => _byPrefab.Count;

        /// <summary>Every entry, for writing the file or the sync payload.</summary>
        internal IEnumerable<KeyValuePair<string, float>> Entries => _byPrefab;

        /// <summary>The multiplier for an item prefab, or 1 for anything not listed.</summary>
        internal float Get(string prefabName)
        {
            if (prefabName != null && _byPrefab.TryGetValue(prefabName, out float value))
                return value;
            return 1f;
        }

        internal bool TryGet(string prefabName, out float multiplier)
        {
            multiplier = 1f;
            return prefabName != null && _byPrefab.TryGetValue(prefabName, out multiplier);
        }

        /// <summary>
        /// Applies a multiplier to a drop count. Rounds stochastically: 3 x 0.5 is 1
        /// half the time and 2 the other half, so fractional multipliers average out
        /// instead of a 0.5 on a single-item drop always rounding the same way.
        /// A multiplier of 0 removes the drop; the result is never negative.
        /// </summary>
        internal static int Scale(int amount, float multiplier)
        {
            if (multiplier == 1f || amount <= 0)
                return amount;
            if (multiplier <= 0f)
                return 0;

            double scaled = amount * (double)multiplier;
            int whole = (int)Math.Floor(scaled);
            double fraction = scaled - whole;
            if (fraction > 0 && UnityEngine.Random.value < fraction)
                whole++;
            return whole;
        }
    }
}
