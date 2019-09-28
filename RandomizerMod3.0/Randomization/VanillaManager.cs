using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace RandomizerMod.Randomization
{
    class VanillaManager
    {
        private static VanillaManager inst = null;
        public static VanillaManager Instance { get
            {
                if (inst == null)
                {
                    inst = new VanillaManager();
                }

                return inst;
            }
        }










        private ItemManager im;
        public List<string> locationsObtained;
        public List<string> progressionLocations;
        public Dictionary<string, List<string>> progressionShopItems;
        public Dictionary<string, string> progressionNonShopItems;

		public List<(string, string)> ItemPlacements { get; private set; }

		public VanillaManager()
        {
            // Pass
        }

        internal void Setup(ItemManager im)
        {
            this.im = im;

            ItemPlacements = new List<(string, string)>();

            progressionLocations = new List<string>();
            progressionShopItems = new Dictionary<string, List<string>>();
            progressionNonShopItems = new Dictionary<string, string>();

            //Set up vanillaLocations
            //    Not as cool as all the hashset union stuff :(
            foreach (string item in GetVanillaItems())
            {
                ReqDef itemDef = LogicManager.GetItemDef(item);
                if (itemDef.type == ItemType.Shop && LogicManager.ShopNames.Contains(itemDef.shopName))
                {
                    ItemPlacements.Add((item, itemDef.shopName));

                    //Add shop to locations
                    if (itemDef.progression && !progressionLocations.Contains(itemDef.shopName))
                        progressionLocations.Add(itemDef.shopName);

                    //Add items to the shop items
                    if (itemDef.progression)
                    {
                        if (progressionShopItems.ContainsKey(itemDef.shopName))
                            //Shop's here, but item's not.
                            progressionShopItems[itemDef.shopName].Add(item);
                        else
                            //Shop's not here, so add the shop and the item to it.
                            progressionShopItems.Add(itemDef.shopName, new List<string>() { item });
                    }

                    continue;
                } else
                {
                    ItemPlacements.Add((item, item));
                    //Not a shop!
                    if (itemDef.progression)
                        progressionNonShopItems.Add(item, item);
                }

                if (itemDef.progression)
                    progressionLocations.Add(item);
            }
        }

        internal void ResetReachableLocations(bool doUpdateQueue = true, ProgressionManager _pm = null)
        {
            if (_pm == null) _pm = im.pm;
            locationsObtained = new List<string>();
            if (!doUpdateQueue) return;

            foreach (string location in progressionLocations)
            {
                if (_pm.CanGet(location))
                    UpdateVanillaLocations(location, doUpdateQueue);
            }
            if (doUpdateQueue) im.UpdateReachableLocations();
        }

        internal void UpdateVanillaLocations(string location, bool doUpdateQueue = true, ProgressionManager _pm = null)
        {
            if (_pm == null) _pm = im.pm;
            if (locationsObtained.Contains(location))
            {
                return;
            }

            if (progressionShopItems.ContainsKey(location))
            { // shop in vanilla
                foreach (string shopItem in progressionShopItems[location])
                {
                    _pm.Add(shopItem);
                    if (doUpdateQueue) im.updateQueue.Enqueue(shopItem);
                }
            }
            else
            { // item in vanilla
                _pm.Add(location);
                if (doUpdateQueue) im.updateQueue.Enqueue(location);
            }

            locationsObtained.Add(location);
        }

        private HashSet<string> GetVanillaItems()
        {
            HashSet<string> unrandoItems = new HashSet<string>();

            if (!RandomizerMod.Instance.Settings.RandomizeDreamers) unrandoItems.UnionWith(LogicManager.GetItemsByPool("Dreamer"));
            if (!RandomizerMod.Instance.Settings.RandomizeSkills) unrandoItems.UnionWith(LogicManager.GetItemsByPool("Skill"));
            if (!RandomizerMod.Instance.Settings.RandomizeCharms) unrandoItems.UnionWith(LogicManager.GetItemsByPool("Charm"));
            if (!RandomizerMod.Instance.Settings.RandomizeKeys) unrandoItems.UnionWith(LogicManager.GetItemsByPool("Key"));

            return unrandoItems;
        }
    }
}
