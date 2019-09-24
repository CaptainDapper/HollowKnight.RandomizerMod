using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Diagnostics;
using static RandomizerMod.LogHelper;

namespace RandomizerMod.Randomization
{
    class ItemManager
    {
        public ProgressionManager pm;

        public static Dictionary<string, string> nonShopItems;
        public static Dictionary<string, List<string>> shopItems;

        private List<string> unplacedLocations;
        private List<string> unplacedItems;
        private List<string> unplacedProgression;
        private List<string> standbyLocations;
        private List<string> standbyItems;
        private List<string> standbyProgression;

        private Queue<bool> progressionFlag;
        private Queue<string> updateQueue;

        private HashSet<string> reachableLocations;
        public HashSet<string> randomizedLocations;

        public int availableCount => reachableLocations.Intersect(unplacedLocations).Count();

        public bool anyLocations => unplacedLocations.Any();
        public bool anyItems => unplacedItems.Any();
        public bool canGuess => unplacedProgression.Any(i => LogicManager.GetItemDef(i).itemCandidate);
        public ItemManager(Random rnd)
        {
            // takes approximately .004s to construct

            pm = new ProgressionManager();

            nonShopItems = new Dictionary<string, string>();
            shopItems = new Dictionary<string, List<string>>();

            unplacedLocations = new List<string>();
            unplacedItems = new List<string>();
            unplacedProgression = new List<string>();
            standbyLocations = new List<string>();
            standbyItems = new List<string>();
            standbyProgression = new List<string>();

            progressionFlag = new Queue<bool>();
            updateQueue = new Queue<string>();

            foreach (string shopName in LogicManager.ShopNames)
            {
                shopItems.Add(shopName, new List<string>());
            }

            List<string> items = GetRandomizedItems().ToList();
            List<string> locations = GetRandomizedLocations().ToList();
            randomizedLocations = new HashSet<string>(locations);
            if (RandomizerMod.Instance.Settings.RandomizeTransitions) locations.Remove("Fury_of_the_Fallen");
            if (RandomizerMod.Instance.Settings.RandomizeRooms)
            {
                items.Remove("Dream_Nail");
                items.Remove("Dream_Gate");
            }

            while (locations.Any())
            {
                string l = locations[rnd.Next(locations.Count)];
                unplacedLocations.Add(l);
                locations.Remove(l);
            }

            while (items.Any())
            {
                string i = items[rnd.Next(items.Count)];

                if (RandomizerMod.Instance.Settings.Cursed)
                {
                    if (LogicManager.GetItemDef(i).pool == "Dreamer" || LogicManager.GetItemDef(i).pool == "Charm" || i == "Mantis_Claw" || i == "Monarch_Wings") i = items[rnd.Next(items.Count)];
                }

                if (!LogicManager.GetItemDef(i).progression)
                {
                    unplacedItems.Add(i);
                    progressionFlag.Enqueue(false);
                }
                else
                {
                    unplacedProgression.Add(i);
                    progressionFlag.Enqueue(true);
                }
                items.Remove(i);
            }

            reachableLocations = new HashSet<string>();
        }

        private HashSet<string> GetRandomizedItems()
        {
            HashSet<string> items = new HashSet<string>();
            if (RandomizerMod.Instance.Settings.RandomizeDreamers) items.UnionWith(LogicManager.GetItemsByPool("Dreamer"));
            if (RandomizerMod.Instance.Settings.RandomizeSkills) items.UnionWith(LogicManager.GetItemsByPool("Skill"));
            if (RandomizerMod.Instance.Settings.RandomizeCharms) items.UnionWith(LogicManager.GetItemsByPool("Charm"));
            if (RandomizerMod.Instance.Settings.RandomizeKeys) items.UnionWith(LogicManager.GetItemsByPool("Key"));
            if (RandomizerMod.Instance.Settings.RandomizeMaskShards) items.UnionWith(LogicManager.GetItemsByPool("Mask"));
            if (RandomizerMod.Instance.Settings.RandomizeVesselFragments) items.UnionWith(LogicManager.GetItemsByPool("Vessel"));
            if (RandomizerMod.Instance.Settings.RandomizePaleOre) items.UnionWith(LogicManager.GetItemsByPool("Ore"));
            if (RandomizerMod.Instance.Settings.RandomizeCharmNotches) items.UnionWith(LogicManager.GetItemsByPool("Notch"));
            if (RandomizerMod.Instance.Settings.RandomizeGeoChests) items.UnionWith(LogicManager.GetItemsByPool("Geo"));
            if (RandomizerMod.Instance.Settings.RandomizeRancidEggs) items.UnionWith(LogicManager.GetItemsByPool("Egg"));
            if (RandomizerMod.Instance.Settings.RandomizeRelics) items.UnionWith(LogicManager.GetItemsByPool("Relic"));

            if (RandomizerMod.Instance.Settings.Cursed)
            {
                items.Remove("Shade_Soul");
                items.Remove("Descending_Dark");
                items.Remove("Abyss_Shriek");

                int i = 0;

                List<string> iterate = items.ToList();
                foreach (string item in iterate)
                {
                    switch (LogicManager.GetItemDef(item).pool)
                    {
                        case "Mask":
                        case "Vessel":
                        case "Ore":
                        case "Notch":
                        case "Geo":
                        case "Egg":
                        case "Relic":
                            items.Remove(item);
                            items.Add("1_Geo_(" + i + ")");
                            i++;
                            break;
                    }
                }
            }

            return items;
        }

        private HashSet<string> GetRandomizedLocations()
        {
            HashSet<string> locations = new HashSet<string>();
            if (RandomizerMod.Instance.Settings.RandomizeDreamers) locations.UnionWith(LogicManager.GetItemsByPool("Dreamer"));
            if (RandomizerMod.Instance.Settings.RandomizeSkills) locations.UnionWith(LogicManager.GetItemsByPool("Skill"));
            if (RandomizerMod.Instance.Settings.RandomizeCharms) locations.UnionWith(LogicManager.GetItemsByPool("Charm"));
            if (RandomizerMod.Instance.Settings.RandomizeKeys) locations.UnionWith(LogicManager.GetItemsByPool("Key"));
            if (RandomizerMod.Instance.Settings.RandomizeMaskShards) locations.UnionWith(LogicManager.GetItemsByPool("Mask"));
            if (RandomizerMod.Instance.Settings.RandomizeVesselFragments) locations.UnionWith(LogicManager.GetItemsByPool("Vessel"));
            if (RandomizerMod.Instance.Settings.RandomizePaleOre) locations.UnionWith(LogicManager.GetItemsByPool("Ore"));
            if (RandomizerMod.Instance.Settings.RandomizeCharmNotches) locations.UnionWith(LogicManager.GetItemsByPool("Notch"));
            if (RandomizerMod.Instance.Settings.RandomizeGeoChests) locations.UnionWith(LogicManager.GetItemsByPool("Geo"));
            if (RandomizerMod.Instance.Settings.RandomizeRancidEggs) locations.UnionWith(LogicManager.GetItemsByPool("Egg"));
            if (RandomizerMod.Instance.Settings.RandomizeRelics) locations.UnionWith(LogicManager.GetItemsByPool("Relic"));

            locations = new HashSet<string>(locations.Where(item => LogicManager.GetItemDef(item).type != ItemType.Shop));
            locations.UnionWith(LogicManager.ShopNames);
            return locations;
        }

        public void QueueUpdate(string newThing, ProgressionManager _pm = null)
        {
            if (_pm != null) pm = _pm;

            pm.Add(newThing);
            updateQueue.Enqueue(newThing);
        }

        public void ResetReachableLocations()
        {
            reachableLocations = new HashSet<string>();
            foreach (string location in randomizedLocations) if (pm.CanGet(location)) reachableLocations.Add(location);
        }
        public void UpdateReachableLocations(string newThing = null, ProgressionManager _pm = null)
        {
            if (_pm != null) pm = _pm;
            //if (_pm == null && RandomizerMod.Instance.Settings.RandomizeTransitions) GetReachableTransitions();

            if (newThing != null)
            {
                pm.Add(newThing);
                updateQueue.Enqueue(newThing);
            }

            while (updateQueue.Any())
            {
                string item = updateQueue.Dequeue();
                foreach (string location in LogicManager.GetItemsByProgression(item))
                {
                    if (pm.CanGet(location))
                    {
                        reachableLocations.Add(location);
                    }
                }
                if (RandomizerMod.Instance.Settings.RandomizeTransitions)
                {
                    if (TransitionManager.transitionPlacements.TryGetValue(item, out string transition1) && !pm.Has(transition1))
                    {
                        pm.Add(transition1);
                        updateQueue.Enqueue(transition1);
                    }
                    foreach (string transition in LogicManager.GetTransitionsByProgression(item))
                    {
                        if (!pm.Has(transition) && pm.CanGet(transition))
                        {
                            pm.Add(transition);
                            updateQueue.Enqueue(transition);
                            if (TransitionManager.transitionPlacements.TryGetValue(transition, out string transition2) && !pm.Has(transition2))
                            {
                                pm.Add(transition2);
                                updateQueue.Enqueue(transition2);
                            }
                        }
                    }
                }
            }
        }

        private List<string> GetReachableTransitions(ProgressionManager _pm = null) // essentially the same as the method in transitionManager, using that class's static placement dictionary
        {
            if (_pm != null) pm = _pm;
            bool done = false;
            bool updated = false;
            List<string> reachableTransitions = new List<string>();
            List<string> unreachableTransitions = LogicManager.TransitionNames().ToList();

            while (!done)
            {
                foreach (string transition in unreachableTransitions)
                {
                    if (pm.Has(transition))
                    {
                        reachableTransitions.Add(transition);
                    }
                    else if (LogicManager.GetTransitionDef(transition).oneWay == 2)
                    {
                        string entrance = TransitionManager.transitionPlacements.FirstOrDefault(exit => exit.Value == transition).Key;

                        if (entrance != null && pm.CanGet(entrance))
                        {
                            reachableTransitions.Add(transition);
                            updated = true;
                        }
                    }
                    else if (!LogicManager.GetTransitionDef(transition).isolated && pm.CanGet(transition))
                    {
                        reachableTransitions.Add(transition);
                        updated = true;
                    }

                    else if (TransitionManager.transitionPlacements.TryGetValue(transition, out string altTransition) && LogicManager.GetTransitionDef(altTransition).oneWay != 2
                        && !LogicManager.GetTransitionDef(altTransition).isolated && pm.CanGet(altTransition))
                    {
                        reachableTransitions.Add(transition);
                        updated = true;
                    }
                }
                foreach (string transition in reachableTransitions)
                {
                    unreachableTransitions.Remove(transition);
                    pm.Add(transition);
                }
                done = !updated;
                updated = false;
            }
            return reachableTransitions;
        }

        public string FindNextLocation(ProgressionManager _pm = null)
        {
            if (_pm != null) pm = _pm;
            return unplacedLocations.FirstOrDefault(location => pm.CanGet(location));
        }
        public string NextLocation(bool checkLogic = true)
        {
            return unplacedLocations.First(location => !checkLogic || reachableLocations.Contains(location));
        }
        public string NextItem(bool checkFlag = true)
        {
            if (checkFlag && progressionFlag.Dequeue() && unplacedProgression.Any()) return unplacedProgression.First();
            if (unplacedItems.Any()) return unplacedItems.First();
            if (unplacedProgression.Any()) return unplacedProgression.First();
            if (standbyItems.Any()) return standbyItems.First();
            if (standbyProgression.Any()) return standbyProgression.First();
            throw new IndexOutOfRangeException();
        }
        public string GuessItem()
        {
            return unplacedProgression.First(item => LogicManager.GetItemDef(item).itemCandidate);
        }
        public string ForceItem()
        {
            Queue<string> progressionQueue = new Queue<string>();
            List<string> tempProgression = new List<string>();

            void UpdateTransitions(string item)
            {
                foreach (string transition in LogicManager.GetTransitionsByProgression(item))
                {
                    if (!pm.Has(transition) && pm.CanGet(transition))
                    {
                        tempProgression.Add(transition);
                        progressionQueue.Enqueue(transition);
                        pm.Add(transition);
                        if (TransitionManager.transitionPlacements.TryGetValue(transition, out string transition2))
                        {
                            tempProgression.Add(transition2);
                            progressionQueue.Enqueue(transition2);
                            pm.Add(transition2);
                        }
                    }
                }
            }
            bool CheckForNewLocations(string item)
            {
                foreach (string location in LogicManager.GetItemsByProgression(item))
                {
                    if (!randomizedLocations.Contains(location)) continue;
                    if (!reachableLocations.Contains(location) && pm.CanGet(location))
                    {
                        return true;
                    }
                }
                return false;
            }

            for (int i = 0; i < unplacedProgression.Count; i++)
            {
                string item = unplacedProgression[i];
                pm.Add(item);
                if (CheckForNewLocations(item)) return item;
                else if (RandomizerMod.Instance.Settings.RandomizeTransitions)
                {
                    bool found = false;
                    UpdateTransitions(item);
                    while (progressionQueue.Any())
                    {
                        string t = progressionQueue.Dequeue();
                        UpdateTransitions(t);
                        found = found || CheckForNewLocations(t);
                    }
                    if (found) return item;
                    foreach (string transition in tempProgression) pm.Remove(transition);
                }

                pm.Remove(item);
            }
            return null;
        }
        public string SpecialGuessItem()
        {
            return unplacedProgression.First(item => LogicManager.GetItemDef(item).itemCandidate);
        }

        public void TransferStandby()
        {
            standbyItems.AddRange(unplacedItems);
            unplacedItems = new List<string>();
            unplacedItems.AddRange(standbyProgression);
            unplacedItems.AddRange(unplacedProgression);
            unplacedItems.AddRange(standbyItems);

            standbyLocations.AddRange(unplacedLocations);
            unplacedLocations = standbyLocations;
        }

        public void PlaceItem(string item, string location)
        {
            if (shopItems.ContainsKey(location)) shopItems[location].Add(item);
            else nonShopItems.Add(location, item);

            unplacedLocations.Remove(location);
            if (LogicManager.GetItemDef(item).progression)
            {
                unplacedProgression.Remove(item);
                UpdateReachableLocations(item);
            }
            else unplacedItems.Remove(item);
        }

        public void PlaceItemFromStandby(string item, string location)
        {
            if (shopItems.ContainsKey(location)) shopItems[location].Add(item);
            else nonShopItems.Add(location, item);
            unplacedLocations.Remove(location);
            unplacedItems.Remove(item);
        }

        public void PlaceProgressionToStandby(string item)
        {
            unplacedProgression.Remove(item);
            standbyProgression.Add(item);
            UpdateReachableLocations(item);
        }

        public void PlaceJunkItemToStandby(string item, string location)
        {
            standbyItems.Add(item);
            standbyLocations.Add(location);
            unplacedItems.Remove(item);
            unplacedLocations.Remove(location);
        }

        private void LogDataConflicts()
        {
            string stuff = pm.ListObtainedProgression();
            foreach (string _item in stuff.Split(','))
            {
                string item = _item.Trim();
                if (string.IsNullOrEmpty(item)) continue;
                if (!nonShopItems.ContainsValue(item) && !standbyProgression.Contains(item))
                {
                    if (LogicManager.ShopNames.All(shop => !shopItems[shop].Contains(item)))
                    {
                        LogWarn("Found " + item + " in inventory, unable to trace origin.");
                    }
                }
            }
        }
    }
}
