using System.Text;

namespace HaldorExpansion
{
    /// <summary>
    /// Boss that must be dead before a row appears in Haldor's stock.
    /// Serialized into the cfg by name so it is readable and syncs as a string.
    /// </summary>
    internal enum UnlockBoss
    {
        None,
        Eikthyr,
        Elder,
        Bonemass,
        Moder,
        Yagluth,
        Queen,
        Fader,
    }

    internal static class UnlockBossKeys
    {
        /// <summary>
        /// ZoneSystem global key for <paramref name="boss"/>, or null if ungated.
        /// Elder/Bonemass/Eikthyr/Moder/Yagluth spellings are string literals in
        /// assembly_valheim.dll. Queen and Fader are data-driven; diagnostics dumps
        /// the live key list so a wrong spelling fails closed (item never appears).
        /// </summary>
        public static string Get(UnlockBoss boss)
        {
            switch (boss)
            {
                case UnlockBoss.Eikthyr: return "defeated_eikthyr";
                case UnlockBoss.Elder: return "defeated_gdking";
                case UnlockBoss.Bonemass: return "defeated_bonemass";
                case UnlockBoss.Moder: return "defeated_dragon";
                case UnlockBoss.Yagluth: return "defeated_goblin";
                case UnlockBoss.Queen: return "defeated_queen";
                case UnlockBoss.Fader: return "defeated_fader";
                default: return null;
            }
        }
    }

    /// <summary>
    /// One purchasable row added to a trader's stock.
    /// </summary>
    internal sealed class TradeEntry
    {
        /// <summary>Prefab name as it appears in ObjectDB. Resolved at runtime, never assumed.</summary>
        public readonly string PrefabName;

        /// <summary>Units delivered per purchase click.</summary>
        public readonly int Stack;

        /// <summary>Total coin cost for one purchase of <see cref="Stack"/> units.</summary>
        public readonly int Price;

        /// <summary>Default boss gate; overlaid at runtime by the UnlockBoss config entry.</summary>
        public readonly UnlockBoss DefaultUnlockBoss;

        public TradeEntry(string prefabName, int stack, int price,
                          UnlockBoss defaultUnlockBoss = UnlockBoss.None)
        {
            PrefabName = prefabName;
            Stack = stack;
            Price = price;
            DefaultUnlockBoss = defaultUnlockBoss;
        }

        public int PricePerUnit => Stack > 0 ? Price / Stack : Price;
    }

    internal static class TradeTable
    {
        public const string HaldorPrefab = "Haldor";

        /// <summary>
        /// Haldor's added stock.
        ///
        /// Prices are anchored to vanilla (Megingjord ~950) and are PROVISIONAL until the
        /// diagnostics dump gives us Haldor's real price list. The ratios are the intent.
        ///
        /// Stone and wood are priced as a sustainable faucet rather than a one-time drawdown:
        /// they are the everyday anti-tedium items and must outlive the legacy coin pile.
        /// </summary>
        public static readonly TradeEntry[] Haldor =
        {
            new TradeEntry("Wood",  50, 50, UnlockBoss.Elder),
            new TradeEntry("Stone", 50, 50, UnlockBoss.Elder),

            // Blackwood is the Ashlands wood; there is no "Ashwood" prefab.
            new TradeEntry("Grausten", 50, 100, UnlockBoss.Queen),
            new TradeEntry("Blackwood", 50, 100, UnlockBoss.Queen),

            // Burial Chambers never regenerate.
            new TradeEntry("SurtlingCore", 5, 500, UnlockBoss.Bonemass),

            // Custom piece (cloned from piece_groundtorch_mist). See SuperMistTorch.cs.
            new TradeEntry(SuperMistTorch.PrefabName, 1, 100, UnlockBoss.Queen),
        };

        /// <summary>
        /// Fingerprint of the effective table (baked rows + live config, including a
        /// server sync when one is active). Logged at startup and again when a client
        /// receives host settings. Comparing this line across logs is the fast check
        /// that everyone is looking at the same stock and prices.
        ///
        /// FNV-1a rather than string.GetHashCode, which is not guaranteed stable across runs.
        /// </summary>
        public static string Hash
        {
            get
            {
                var sb = new StringBuilder();
                var settings = Plugin.Settings;
                foreach (var e in Haldor)
                {
                    var enabled = settings == null || settings.IsEnabled(e);
                    var price = settings == null ? e.Price : settings.GetPurchasePrice(e);
                    var boss = settings == null ? e.DefaultUnlockBoss : settings.GetUnlockBoss(e);
                    sb.Append(e.PrefabName).Append('|')
                      .Append(enabled ? "1" : "0").Append('|')
                      .Append(e.Stack).Append('|')
                      .Append(price).Append('|')
                      .Append(boss).Append(';');
                }

                ulong h = 14695981039346656037UL;
                foreach (var c in sb.ToString())
                {
                    h ^= c;
                    h *= 1099511628211UL;
                }

                return h.ToString("x16");
            }
        }
    }
}
