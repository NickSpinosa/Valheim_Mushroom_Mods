using System.Collections.Generic;
using UnityEngine;

namespace ResourceDropModifier
{
    /// <summary>
    /// Resolves an <see cref="ItemDrop.ItemData"/> to its prefab name at the patch
    /// point, where the name is not reliably on the data itself.
    /// <para>
    /// <c>DropTable.AddItemToList</c> sets <c>m_dropPrefab</c> before it calls
    /// <c>ScaleDrops</c>, but <c>Beehive</c>, <c>SapCollector</c> and
    /// <c>PickableItem</c> pass the prefab's own <c>m_itemData</c>, whose
    /// <c>m_dropPrefab</c> is whatever Unity serialised, usually null. So the lookup
    /// goes <c>m_dropPrefab</c> first, then a map from <c>SharedData</c> to prefab
    /// name. <c>ItemData.Clone()</c> is a <c>MemberwiseClone</c>, so every clone shares
    /// the prefab's <c>SharedData</c> instance by reference and the map resolves any of
    /// them without a string compare.
    /// </para>
    /// <para>
    /// Rebuilt with the catalog on every world load, because <c>ObjectDB.CopyOtherDB</c>
    /// replaces the item list and its <c>SharedData</c> instances with it.
    /// </para>
    /// </summary>
    internal static class ItemIndex
    {
        private static Dictionary<ItemDrop.ItemData.SharedData, string> _bySharedData =
            new Dictionary<ItemDrop.ItemData.SharedData, string>();

        internal static int Count => _bySharedData.Count;

        internal static void Rebuild(ObjectDB db)
        {
            var map = new Dictionary<ItemDrop.ItemData.SharedData, string>();
            if (db != null)
            {
                foreach (GameObject prefab in db.m_items)
                {
                    if (prefab == null)
                        continue;

                    ItemDrop item = prefab.GetComponent<ItemDrop>();
                    if (item == null || item.m_itemData == null || item.m_itemData.m_shared == null)
                        continue;

                    if (!map.ContainsKey(item.m_itemData.m_shared))
                        map[item.m_itemData.m_shared] = prefab.name;
                }
            }

            _bySharedData = map;
        }

        /// <summary>The prefab name for an item's data, or null when it cannot be resolved.</summary>
        internal static string NameOf(ItemDrop.ItemData data)
        {
            if (data == null)
                return null;

            if (data.m_dropPrefab != null)
                return data.m_dropPrefab.name;

            string name;
            if (data.m_shared != null && _bySharedData.TryGetValue(data.m_shared, out name))
                return name;

            return null;
        }
    }
}
