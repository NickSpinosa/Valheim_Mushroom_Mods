using System;
using System.Collections.Generic;

namespace QuietNights
{
    /// <summary>
    /// The parsed form of the two rule settings: which creatures are covered and where
    /// each of them belongs, and which global keys count as a boss kill.
    /// </summary>
    internal sealed class Rules
    {
        internal sealed class Creature
        {
            internal string Pattern;
            internal Heightmap.Biome Native;
        }

        internal readonly List<Creature> Creatures = new List<Creature>();
        internal readonly List<string> BossKeys = new List<string>();

        /// <summary>
        /// Reads <c>Prefab:Biome</c> pairs and key patterns. A bad item is reported and
        /// dropped rather than failing the lot: one typo on the server should not turn
        /// every other rule off for every client.
        /// </summary>
        internal static Rules Parse(string creatures, string bossKeys)
        {
            var rules = new Rules();

            foreach (string item in Split(creatures))
            {
                int colon = item.LastIndexOf(':');
                if (colon <= 0 || colon == item.Length - 1)
                {
                    Plugin.Log.LogWarning("Rules.Creatures: '" + item + "' is not Prefab:Biome - ignored.");
                    continue;
                }

                if (!TryParseBiomes(item.Substring(colon + 1), out Heightmap.Biome native))
                {
                    Plugin.Log.LogWarning("Rules.Creatures: '" + item + "' names no biome the game knows - ignored. "
                        + "Valid: " + string.Join(", ", Enum.GetNames(typeof(Heightmap.Biome))));
                    continue;
                }

                rules.Creatures.Add(new Creature { Pattern = item.Substring(0, colon).Trim(), Native = native });
            }

            rules.BossKeys.AddRange(Split(bossKeys));
            return rules;
        }

        internal Creature Find(string prefabName)
        {
            foreach (Creature creature in Creatures)
            {
                if (Matches(creature.Pattern, prefabName))
                    return creature;
            }

            return null;
        }

        internal bool IsBossKey(string key)
        {
            foreach (string pattern in BossKeys)
            {
                if (Matches(pattern, key))
                    return true;
            }

            return false;
        }

        /// <summary>Exact, or a prefix when the pattern ends in <c>*</c>. Case-insensitive.</summary>
        private static bool Matches(string pattern, string value)
        {
            if (string.IsNullOrEmpty(value))
                return false;

            if (pattern.EndsWith("*", StringComparison.Ordinal))
                return value.StartsWith(pattern.Substring(0, pattern.Length - 1), StringComparison.OrdinalIgnoreCase);

            return string.Equals(pattern, value, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary><c>Plains</c>, or <c>Plains+Meadows</c> for a creature at home in more than one.</summary>
        private static bool TryParseBiomes(string text, out Heightmap.Biome biomes)
        {
            biomes = Heightmap.Biome.None;

            foreach (string name in text.Split('+'))
            {
                // Enum.TryParse accepts bare numbers; "7" is not a biome anyone meant.
                if (!Enum.TryParse(name.Trim(), true, out Heightmap.Biome biome)
                    || !Enum.IsDefined(typeof(Heightmap.Biome), biome)
                    || biome == Heightmap.Biome.None)
                    return false;

                biomes |= biome;
            }

            return true;
        }

        private static IEnumerable<string> Split(string list)
        {
            foreach (string item in (list ?? "").Split(','))
            {
                string trimmed = item.Trim();
                if (trimmed.Length > 0)
                    yield return trimmed;
            }
        }
    }
}
