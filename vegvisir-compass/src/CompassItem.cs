using System.Collections.Generic;
using System.IO;
using System.Globalization;
using UnityEngine;

namespace VegvisirCompass
{
    /// <summary>
    /// Owns the compass item prefab: building it, registering it with ObjectDB and
    /// ZNetScene, and reading/writing the per-item state that rides along in the
    /// item custom data.
    /// </summary>
    internal static class CompassItem
    {
        internal const string PrefabName = "VegvisirCompass";

        /// <summary>
        /// Name shown in the inventory, and how the mod recognises its own item.
        ///
        /// Fixed rather than configurable. It was a setting, and an actively dangerous
        /// one: because IsCompass matches on it, changing it orphaned every compass a
        /// player already held, and two installs disagreeing left each side confidently
        /// right about a different name.
        /// </summary>
        internal const string DisplayName = "Vegvisir Compass";

        /// <summary>
        /// Tooltip text. Deliberately does not state the range in metres. It used to,
        /// which meant it had to be hand-edited to stay true whenever Compass.RangeMeters
        /// changed - and the server owns that value, so a client's copy could not be
        /// relied on to name it correctly anyway.
        /// </summary>
        internal const string Description =
            "A shard of rune-carved stone, warm to the touch. Read it and the runes will " +
            "turn you toward what you seek, then burn away. Only has power close to the " +
            "vegvisir that yielded it.";

        /// <summary>
        /// Vanilla item the compass borrows its in-world model from. Only the model:
        /// the icon is our own, embedded in the assembly. Fixed rather than
        /// configurable, since changing it was a way to break the item rather than to
        /// configure it.
        /// </summary>
        private const string CloneSourceItem = "SurtlingCore";

        /// <summary>World position of the boss this particular compass points at.</summary>
        private const string TargetKey = "vc_target";

        /// <summary>Display name of the target, purely for player-facing messages.</summary>
        private const string BossKey = "vc_boss";

        /// <summary>
        /// The target's location name, e.g. "PlaceofMystery2". This is the compass's
        /// real identity: pin names are labels and several stones can share one, while
        /// location names are distinct per target.
        /// </summary>
        private const string LocationKey = "vc_loc";

        /// <summary>Position of the Vegvisir this compass was looted from.</summary>
        private const string OriginKey = "vc_origin";

        /// <summary>
        /// How far from that stone the compass still works. Stored per item and set
        /// by the server, so it cannot be widened by editing a local config.
        /// </summary>
        private const string RangeKey = "vc_range";

        /// <summary>
        /// Embedded icon, matching the LogicalName set in the csproj.
        /// "Compass PBR(Unity) CC0" by Lucian Pavel, CC0.
        /// </summary>
        private const string IconResourceName = "VegvisirCompass.compass_icon.png";

        private static GameObject _prefab;
        private static GameObject _prefabContainer;
        private static Sprite _icon;
        private static bool _variantsInstalled;

        /// <summary>
        /// True when the item is one of ours. Compared by shared name, which is unique
        /// to this mod and survives the item being dropped, stored and picked back up.
        /// </summary>
        internal static bool IsCompass(ItemDrop.ItemData item)
        {
            return item?.m_shared != null
                && item.m_shared.m_name == DisplayName;
        }

        /// <summary>
        /// Finds a compass already pointing at one of the given locations, or null.
        ///
        /// The carry limit is per target rather than global: holding a compass for
        /// The Elder should not stop you looting one for The Queen. Duplicates of the
        /// same target are still refused, which is what the limit was for.
        ///
        /// Matching is on location name rather than pin name. Pin names are display
        /// labels and nothing stops several stones sharing one - the Ashlands Dyrnwyn
        /// chain runs through three separate stones - so deduping on them could refuse
        /// a genuinely different target and stall that chain. Location names are
        /// distinct per target, so they are the reliable identity.
        /// </summary>
        internal static ItemDrop.ItemData FindCompassForAny(Inventory inventory, List<string> locationNames)
        {
            if (inventory == null || locationNames == null || locationNames.Count == 0) return null;

            foreach (ItemDrop.ItemData item in inventory.GetAllItems())
            {
                if (!IsCompass(item)) continue;

                string held = GetLocationName(item);
                if (string.IsNullOrEmpty(held)) continue;

                foreach (string name in locationNames)
                {
                    if (!string.IsNullOrEmpty(name) && held == name)
                    {
                        return item;
                    }
                }
            }
            return null;
        }

        // --- Per-item state -------------------------------------------------

        internal static void SetTarget(ItemDrop.ItemData item, Vector3 target, string bossName, string locationName)
        {
            item.m_customData[TargetKey] = string.Format(
                CultureInfo.InvariantCulture, "{0};{1};{2}", target.x, target.y, target.z);
            item.m_customData[BossKey] = bossName ?? "";
            item.m_customData[LocationKey] = locationName ?? "";
        }

        /// <summary>
        /// The target's location name. Empty for compasses granted before locations
        /// were recorded, which simply means they take part in no dedupe.
        /// </summary>
        internal static string GetLocationName(ItemDrop.ItemData item)
        {
            if (item?.m_customData != null && item.m_customData.TryGetValue(LocationKey, out string name))
            {
                return name;
            }
            return "";
        }

        internal static bool TryGetTarget(ItemDrop.ItemData item, out Vector3 target)
        {
            target = Vector3.zero;
            if (item?.m_customData == null) return false;
            if (!item.m_customData.TryGetValue(TargetKey, out string raw)) return false;

            string[] parts = raw.Split(';');
            if (parts.Length != 3) return false;

            if (!float.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out float x)) return false;
            if (!float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float y)) return false;
            if (!float.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out float z)) return false;

            target = new Vector3(x, y, z);
            return true;
        }

        internal static string GetBossName(ItemDrop.ItemData item)
        {
            if (item?.m_customData != null && item.m_customData.TryGetValue(BossKey, out string name))
            {
                return name;
            }
            return "";
        }

        /// <summary>
        /// Localized boss name, or empty when the compass carries no target.
        /// The stored value is a token such as "$enemy_dragon".
        /// </summary>
        internal static string GetLocalizedBossName(ItemDrop.ItemData item)
        {
            string token = GetBossName(item);
            if (string.IsNullOrEmpty(token)) return "";
            return Localization.instance != null ? Localization.instance.Localize(token) : token;
        }

        /// <summary>
        /// Name to show the player, with the target appended: "Vegvisir Compass - Moder".
        ///
        /// Built for display only. The underlying m_shared.m_name is left alone
        /// because SharedData is one object shared by every instance of the prefab -
        /// writing to it would rename every compass at once, break the name check in
        /// IsCompass, and be discarded anyway when items are reloaded from the prefab.
        /// </summary>
        internal static string GetDisplayName(ItemDrop.ItemData item)
        {
            string baseName = item?.m_shared?.m_name ?? "";
            if (Localization.instance != null)
            {
                baseName = Localization.instance.Localize(baseName);
            }

            string boss = GetLocalizedBossName(item);
            return string.IsNullOrEmpty(boss) ? baseName : baseName + " - " + boss;
        }

        internal static void SetOrigin(ItemDrop.ItemData item, Vector3 origin, float range)
        {
            item.m_customData[OriginKey] = string.Format(
                CultureInfo.InvariantCulture, "{0};{1};{2}", origin.x, origin.y, origin.z);
            item.m_customData[RangeKey] = range.ToString(CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// Where the compass was looted from. False for compasses created before
        /// ranges existed, or spawned straight from the console; callers treat that
        /// as "no restriction" rather than bricking the item.
        /// </summary>
        internal static bool TryGetOrigin(ItemDrop.ItemData item, out Vector3 origin)
        {
            origin = Vector3.zero;
            if (item?.m_customData == null) return false;
            if (!item.m_customData.TryGetValue(OriginKey, out string raw)) return false;

            string[] parts = raw.Split(';');
            if (parts.Length != 3) return false;

            if (!float.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out float x)) return false;
            if (!float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float y)) return false;
            if (!float.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out float z)) return false;

            origin = new Vector3(x, y, z);
            return true;
        }

        /// <summary>Range stored on the item, or zero when it carries none.</summary>
        internal static float GetRange(ItemDrop.ItemData item)
        {
            if (item?.m_customData != null
                && item.m_customData.TryGetValue(RangeKey, out string raw)
                && float.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out float range))
            {
                return range;
            }
            return 0f;
        }

        /// <summary>
        /// Distance between two points on the X/Z plane.
        /// Deliberately ignores height: a stone at the foot of a mountain should not
        /// read as out of range just because the player climbed it.
        /// </summary>
        internal static float HorizontalDistance(Vector3 a, Vector3 b)
        {
            float dx = b.x - a.x;
            float dz = b.z - a.z;
            return Mathf.Sqrt(dx * dx + dz * dz);
        }

        // --- Registration ---------------------------------------------------

        /// <summary>
        /// Adds the compass to the given ObjectDB, building the prefab on first call.
        /// Safe to call repeatedly; ObjectDB.Awake and CopyOtherDB both fire more than once.
        /// </summary>
        internal static void EnsureRegistered(ObjectDB odb)
        {
            if (odb == null || odb.m_items == null) return;

            // ObjectDB also exists in the main menu in a stripped-down form. Anything
            // cloned from it there would be incomplete, so wait for the real one.
            if (odb.GetItemPrefab("Wood") == null) return;

            if (_prefab == null)
            {
                _prefab = BuildPrefab(odb);
                if (_prefab == null) return;
            }

            if (!odb.m_items.Contains(_prefab))
            {
                odb.m_items.Add(_prefab);
                odb.UpdateRegisters();
                Plugin.Log.LogInfo("Registered " + PrefabName + " with ObjectDB.");
            }
        }

        /// <summary>
        /// Registers the prefab with ZNetScene so the item can exist as a networked
        /// object once dropped on the ground.
        /// </summary>
        internal static void EnsureNetworkRegistered(ZNetScene scene)
        {
            if (scene == null || _prefab == null) return;

            if (!scene.m_prefabs.Contains(_prefab))
            {
                scene.m_prefabs.Add(_prefab);
            }

            int hash = PrefabName.GetStableHashCode();
            if (!scene.m_namedPrefabs.ContainsKey(hash))
            {
                scene.m_namedPrefabs[hash] = _prefab;
                Plugin.Log.LogInfo("Registered " + PrefabName + " with ZNetScene.");
            }
        }

        /// <summary>
        /// Builds the inventory icon from the PNG embedded in this assembly.
        /// Returns null on any failure, in which case the donor item's own icon is
        /// kept - a missing icon should not cost us a working item.
        /// </summary>
        private static Sprite LoadEmbeddedIcon()
        {
            if (_icon != null) return _icon;

            try
            {
                using (Stream stream = typeof(CompassItem).Assembly.GetManifestResourceStream(IconResourceName))
                {
                    if (stream == null)
                    {
                        Plugin.Log.LogWarning("Embedded icon " + IconResourceName + " not found.");
                        return null;
                    }

                    byte[] data = new byte[stream.Length];
                    int read = 0;
                    while (read < data.Length)
                    {
                        int chunk = stream.Read(data, read, data.Length - read);
                        if (chunk <= 0) break;
                        read += chunk;
                    }

                    // Size is irrelevant here: LoadImage replaces the texture wholesale
                    // with the PNG's own dimensions.
                    Texture2D texture = new Texture2D(2, 2, TextureFormat.RGBA32, mipChain: false);
                    if (!texture.LoadImage(data))
                    {
                        Plugin.Log.LogWarning("Embedded icon could not be decoded.");
                        Object.Destroy(texture);
                        return null;
                    }

                    texture.name = "VegvisirCompassIcon";
                    // Created at runtime and referenced only from our prefab, so keep it
                    // out of Unity's scene bookkeeping and away from unload sweeps.
                    texture.hideFlags = HideFlags.HideAndDontSave;

                    _icon = Sprite.Create(
                        texture,
                        new Rect(0f, 0f, texture.width, texture.height),
                        new Vector2(0.5f, 0.5f));
                    _icon.name = "VegvisirCompassIcon";
                    _icon.hideFlags = HideFlags.HideAndDontSave;

                    Plugin.Log.LogInfo($"Loaded the embedded icon ({texture.width}x{texture.height}).");
                    return _icon;
                }
            }
            catch (System.Exception e)
            {
                Plugin.Log.LogWarning("Failed to load the embedded icon: " + e.Message);
                return null;
            }
        }

        /// <summary>
        /// Builds one icon per variant: the original artwork for bosses, then a tinted
        /// copy for each other category. Returns null if the base icon is unavailable,
        /// in which case the donor item's icon stays and every compass looks alike.
        /// </summary>
        private static Sprite[] BuildIconSet()
        {
            Sprite baseIcon = LoadEmbeddedIcon();
            if (baseIcon == null) return null;

            Sprite[] icons = new Sprite[CompassVariant.Count];
            icons[CompassVariant.Boss] = baseIcon;

            // Tints are fixed; see CompassVariant.
            icons[CompassVariant.Merchant] = TintedSprite(baseIcon, CompassVariant.MerchantTint);
            icons[CompassVariant.MysteriousLocation] = TintedSprite(baseIcon, CompassVariant.MysteryTint);
            icons[CompassVariant.HildirQuest] = TintedSprite(baseIcon, CompassVariant.HildirQuestTint);
            icons[CompassVariant.ForgeOfPotential] = TintedSprite(baseIcon, CompassVariant.ForgeTint);

            // A missing tint would leave a null in the array and throw when the icon is
            // drawn, so fall back to the untinted original for any that failed.
            for (int i = 0; i < icons.Length; i++)
            {
                if (icons[i] == null) icons[i] = baseIcon;
            }

            Plugin.Log.LogInfo($"Built {icons.Length} compass icon variants.");
            return icons;
        }

        private static Sprite TintedSprite(Sprite source, Color tint)
        {
            try
            {
                Texture2D tinted = CompassVariant.Tint(source.texture, tint);
                Sprite sprite = Sprite.Create(
                    tinted, new Rect(0f, 0f, tinted.width, tinted.height), new Vector2(0.5f, 0.5f));
                sprite.name = "VegvisirCompassIcon_" + ColorUtility.ToHtmlStringRGB(tint);
                sprite.hideFlags = HideFlags.HideAndDontSave;
                return sprite;
            }
            catch (System.Exception e)
            {
                Plugin.Log.LogWarning("Could not tint the compass icon: " + e.Message);
                return null;
            }
        }

        private static GameObject BuildPrefab(ObjectDB odb)
        {
            string sourceName = CloneSourceItem;
            GameObject source = odb.GetItemPrefab(sourceName);
            if (source == null)
            {
                Plugin.Log.LogError(
                    "Cannot build the compass: no item prefab named " + sourceName +
                    " exists. Set Item/CloneSourceItem to a valid vanilla item.");
                return null;
            }

            // Instantiating into an inactive parent keeps Unity from running Awake on
            // the clone, so it behaves as a prefab rather than a live scene object.
            if (_prefabContainer == null)
            {
                _prefabContainer = new GameObject("VegvisirCompassPrefabs");
                _prefabContainer.SetActive(false);
                Object.DontDestroyOnLoad(_prefabContainer);
            }

            GameObject clone = Object.Instantiate(source, _prefabContainer.transform);
            clone.name = PrefabName;

            ItemDrop drop = clone.GetComponent<ItemDrop>();
            if (drop == null)
            {
                Plugin.Log.LogError("Item prefab " + sourceName + " has no ItemDrop component.");
                Object.Destroy(clone);
                return null;
            }

            // SharedData is a plain [Serializable] class, so Instantiate deep-copied it
            // and these edits cannot leak back into the item we cloned from.
            ItemDrop.ItemData.SharedData shared = drop.m_itemData.m_shared;
            shared.m_name = DisplayName;
            shared.m_description = Description;

            // Consumable is what makes the item usable from the hotbar as well as by
            // right-clicking it in the inventory. It is never actually eaten - the
            // UseItem patch intercepts before the vanilla consumable branch runs.
            shared.m_itemType = ItemDrop.ItemData.ItemType.Consumable;
            shared.m_maxStackSize = 1;

            Sprite[] icons = BuildIconSet();
            if (icons != null)
            {
                shared.m_icons = icons;
                shared.m_variants = icons.Length;
                _variantsInstalled = true;
            }
            shared.m_weight = 0.5f;
            shared.m_value = 0;
            shared.m_teleportable = true;
            shared.m_questItem = false;
            shared.m_centerCamera = false;

            // Remaining uses are tracked as durability so the count shows up in the
            // vanilla tooltip. Repair must be off, or a workbench would refill it.
            shared.m_useDurability = true;
            shared.m_maxDurability = Plugin.UsesPerCompass.Value;
            shared.m_durabilityPerLevel = 0f;
            shared.m_useDurabilityDrain = 0f;
            shared.m_durabilityDrain = 0f;
            shared.m_canBeReparied = false;

            // Strip anything inherited from the donor item that would misbehave here.
            shared.m_food = 0f;
            shared.m_foodStamina = 0f;
            shared.m_foodEitr = 0f;
            shared.m_foodBurnTime = 0f;
            shared.m_foodRegen = 0f;
            shared.m_consumeStatusEffect = null;
            shared.m_equipStatusEffect = null;

            // Compasses are never hoovered up by walking over them. The carry rule
            // can refuse a pickup, and auto-pickup reacts to a refusal by dragging the
            // item to the player's feet and trying again every frame - so a compass the
            // player may not hold would jitter around them indefinitely. Requiring the
            // use key also matches how the compass was obtained in the first place.
            drop.m_autoPickup = false;

            drop.m_itemData.m_stack = 1;
            drop.m_itemData.m_durability = Plugin.UsesPerCompass.Value;

            Plugin.Log.LogInfo("Built " + PrefabName + " from " + sourceName + ".");
            return clone;
        }

        /// <summary>
        /// Creates a compass in the player inventory, aimed at the given target.
        /// The use count comes from the server rather than local config, so a player
        /// cannot hand themselves extra uses by editing their own settings.
        /// </summary>
        internal static bool Grant(Player player, Vector3 target, string bossName, int uses,
                                   Vector3 origin, float range, string locationName)
        {
            Inventory inventory = player?.GetInventory();
            if (inventory == null) return false;

            ItemDrop.ItemData item = inventory.AddItem(
                PrefabName, 1, 1, 0, player.GetPlayerID(), player.GetPlayerName(), true);

            if (item == null)
            {
                Plugin.Log.LogWarning("Could not add the compass - the inventory is probably full.");
                return false;
            }

            item.m_durability = Mathf.Max(1, uses);
            SetTarget(item, target, bossName, locationName);
            SetOrigin(item, origin, range);

            // Only index past the first icon once the full set is actually installed.
            // GetIcon does m_icons[m_variant] with no bounds check, so a variant beyond
            // the array would throw every frame the inventory is drawn.
            if (_variantsInstalled)
            {
                item.m_variant = CompassVariant.ForLocation(locationName);
                Plugin.Debug($"Compass for {locationName} uses icon variant {item.m_variant}.");
            }

            return true;
        }
    }
}
