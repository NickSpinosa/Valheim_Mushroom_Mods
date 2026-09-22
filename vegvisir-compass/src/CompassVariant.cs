using UnityEngine;

namespace VegvisirCompass
{
    /// <summary>
    /// Which icon a compass wears, chosen by what it points at.
    ///
    /// Valheim resolves an item's icon as m_shared.m_icons[m_variant]. The icon array
    /// is shared across the prefab but m_variant is per item and is ZDO-persisted, so
    /// one prefab can carry every colour and each compass picks its own - surviving
    /// drops, chests and reloads.
    ///
    /// Indices are stored on items, so their meaning must not be reshuffled.
    /// </summary>
    internal static class CompassVariant
    {
        internal const int Boss = 0;
        internal const int Merchant = 1;
        internal const int MysteriousLocation = 2;
        internal const int HildirQuest = 3;
        internal const int ForgeOfPotential = 4;

        /// <summary>Number of icons the prefab is given.</summary>
        internal const int Count = 5;

        // Tint per variant. Boss wears the original artwork untinted, so it has no
        // entry. Deliberately fixed rather than configurable: a compass pack is only
        // readable at a glance if the colours mean the same thing to everyone, and
        // colour knobs were ways to break that for no gain.
        internal static readonly Color MerchantTint = new Color(0.80f, 0.84f, 0.90f);       // #CCD6E6
        internal static readonly Color MysteryTint = new Color(0.88f, 0.27f, 0.20f);        // #E04533
        internal static readonly Color HildirQuestTint = new Color(0.64f, 0.38f, 0.92f);    // #A361EB
        internal static readonly Color ForgeTint = new Color(0.55f, 0.78f, 0.92f);          // #8CC7EB mountain ice

        /// <summary>
        /// Ashlands Mysterious Locations, the Dyrnwyn chain. Matched by prefix so all
        /// three steps are covered without naming each.
        /// </summary>
        private const string MysteriousLocationPrefix = "PlaceofMystery";

        /// <summary>
        /// Picks a variant from the target's location name.
        ///
        /// Anything unrecognised falls back to the default icon rather than guessing,
        /// so a location name that turns out to be wrong costs nothing but colour.
        /// </summary>
        internal static int ForLocation(string locationName)
        {
            if (string.IsNullOrEmpty(locationName)) return Boss;

            if (locationName.StartsWith(MysteriousLocationPrefix, System.StringComparison.OrdinalIgnoreCase))
            {
                return MysteriousLocation;
            }

            foreach (MerchantDef merchant in MerchantCatalog.All)
            {
                if (!string.Equals(locationName, merchant.LocationName, System.StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                return merchant.IsUpgrader ? ForgeOfPotential : Merchant;
            }

            foreach (HildirQuestDef quest in HildirQuestCatalog.All)
            {
                if (string.Equals(locationName, quest.LocationName, System.StringComparison.OrdinalIgnoreCase))
                {
                    return HildirQuest;
                }
            }

            return Boss;
        }

        /// <summary>
        /// Recolours the base icon by flattening it to luminance and multiplying by a
        /// tint. Multiplying the brass artwork directly would only ever produce dirtier
        /// brass; going through greyscale first gives a clean colour.
        ///
        /// The brightness lift compensates for luminance being darker than the original
        /// at full saturation.
        /// </summary>
        internal static Texture2D Tint(Texture2D source, Color tint, float brightness = 1.15f)
        {
            Texture2D result = new Texture2D(source.width, source.height, TextureFormat.RGBA32, mipChain: false);
            Color[] pixels = source.GetPixels();

            for (int i = 0; i < pixels.Length; i++)
            {
                Color p = pixels[i];
                float luminance = (p.r * 0.299f + p.g * 0.587f + p.b * 0.114f) * brightness;

                pixels[i] = new Color(
                    Mathf.Clamp01(luminance * tint.r),
                    Mathf.Clamp01(luminance * tint.g),
                    Mathf.Clamp01(luminance * tint.b),
                    p.a);
            }

            result.SetPixels(pixels);
            result.Apply(updateMipmaps: false);
            result.name = "VegvisirCompassIcon_Tinted";
            result.hideFlags = HideFlags.HideAndDontSave;
            return result;
        }
    }
}
