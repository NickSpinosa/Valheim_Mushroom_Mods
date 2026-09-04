using System.Collections.Generic;
using BepInEx.Configuration;

namespace HaldorExpansion
{
    internal sealed class ItemConfig
    {
        public readonly ConfigEntry<bool> Enabled;
        public readonly ConfigEntry<int> Cost;
        public readonly ConfigEntry<UnlockBoss> UnlockBoss;

        public ItemConfig(ConfigEntry<bool> enabled, ConfigEntry<int> cost,
                          ConfigEntry<UnlockBoss> unlockBoss)
        {
            Enabled = enabled;
            Cost = cost;
            UnlockBoss = unlockBoss;
        }
    }

    /// <summary>
    /// Per-item Enabled / Cost / UnlockBoss settings. Cost is coins per unit; the
    /// trader's purchase price is Cost times the baked stack size. Synced values
    /// (when connected to a server that locks configuration) are read through
    /// MushroomSync and are never written back into the local .cfg. Reading an
    /// entry's Value is enough - the overlay returns the host's value when one is in
    /// force, so no call site here needs to know whether a sync is active.
    /// </summary>
    internal sealed class ModConfig
    {
        private readonly ConfigFile _configFile;
        private readonly ConfigEntry<bool> _lockConfiguration;
        private readonly Dictionary<string, ItemConfig> _items =
            new Dictionary<string, ItemConfig>();

        private ConfigEntry<T> Bind<T>(string group, string name, T value,
                                       ConfigDescription description,
                                       bool synchronizedSetting = true)
        {
            var entry = _configFile.Bind(group, name, value, description);
            if (synchronizedSetting)
                Plugin.Sync.Register(entry);
            return entry;
        }

        internal ModConfig(ConfigFile configFile)
        {
            _configFile = configFile;
            configFile.SaveOnConfigSet = false;

            _lockConfiguration = Bind(
                "Server",
                "LockConfiguration",
                true,
                new ConfigDescription(
                    "If on, connected clients use this machine's item settings instead of their local values."),
                synchronizedSetting: false);

            foreach (var entry in TradeTable.Haldor)
            {
                var section = "Items." + entry.PrefabName;

                var enabled = Bind(
                    section,
                    "Enabled",
                    true,
                    new ConfigDescription(
                        "If off, " + entry.PrefabName + " is not added to Haldor's stock."));

                var cost = Bind(
                    section,
                    "Cost",
                    entry.PricePerUnit,
                    new ConfigDescription(
                        "Coins charged per unit of " + entry.PrefabName
                        + ". One purchase delivers " + entry.Stack
                        + " for Cost x " + entry.Stack + " coins.",
                        new AcceptableValueRange<int>(0, 100000)));

                var unlockBoss = Bind(
                    section,
                    "UnlockBoss",
                    entry.DefaultUnlockBoss,
                    new ConfigDescription(
                        "Boss that must be defeated before " + entry.PrefabName
                        + " appears in Haldor's stock. None means always available."));

                _items[entry.PrefabName] = new ItemConfig(enabled, cost, unlockBoss);
            }

            configFile.Save();
            configFile.SaveOnConfigSet = true;
        }

        internal bool LockConfiguration => _lockConfiguration.Value;

        internal bool IsEnabled(TradeEntry entry)
        {
            ItemConfig item;
            if (!_items.TryGetValue(entry.PrefabName, out item)) return true;
            return item.Enabled.Value;
        }

        /// <summary>Effective coins per unit, honoring a live server sync.</summary>
        internal int GetCostPerUnit(TradeEntry entry)
        {
            ItemConfig item;
            if (!_items.TryGetValue(entry.PrefabName, out item)) return entry.PricePerUnit;

            var perUnit = item.Cost.Value;
            return perUnit < 0 ? 0 : perUnit;
        }

        /// <summary>Total coins charged for one purchase of <see cref="TradeEntry.Stack"/> units.</summary>
        internal int GetPurchasePrice(TradeEntry entry)
        {
            return GetCostPerUnit(entry) * entry.Stack;
        }

        /// <summary>Effective boss gate, honoring a live server sync.</summary>
        internal UnlockBoss GetUnlockBoss(TradeEntry entry)
        {
            ItemConfig item;
            if (!_items.TryGetValue(entry.PrefabName, out item)) return entry.DefaultUnlockBoss;
            return item.UnlockBoss.Value;
        }

        /// <summary>ZoneSystem key for the effective boss gate, or null if ungated.</summary>
        internal string GetRequiredGlobalKey(TradeEntry entry)
        {
            return UnlockBossKeys.Get(GetUnlockBoss(entry));
        }
    }
}
