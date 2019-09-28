using System;
using System.Diagnostics;
using System.Collections.Generic;
using System.Linq;
using RandomizerMod.Actions;
using static RandomizerMod.LogHelper;

namespace RandomizerMod.Randomization
{
    internal static class Randomizer
    {
        private static ItemManager im;
        private static TransitionManager tm;
        private static VanillaManager vm { get { return VanillaManager.Instance; } }

        private static bool overflow;
        private static bool randomizationError;
        private static Random rand;

        public static void Randomize()
        {
            rand = new Random(RandomizerMod.Instance.Settings.Seed);

            while (true)
            {
                RandomizerMod.Instance.Settings.ResetPlacements();
                RandomizeCosts();
                if (RandomizerMod.Instance.Settings.RandomizeTransitions) RandomizeTransitions();
                RandomizeItems();
                if (!randomizationError) break;
            }

            PostRandomizationTasks();
            RandomizerAction.CreateActions(RandomizerMod.Instance.Settings.ItemPlacements, RandomizerMod.Instance.Settings.Seed);
        }

        public static void RandomizeCosts()
        {
            foreach (string item in LogicManager.ItemNames)
            {
                if (LogicManager.GetItemDef(item).costType == 1) //essence cost
                {
                    ReqDef def = LogicManager.GetItemDef(item);
                    int cost = 1 + rand.Next(900);
                    def.cost = cost;
                    LogicManager.EditItemDef(item, def);
                    RandomizerMod.Instance.Settings.AddNewCost(item, cost);
                }

                if (LogicManager.GetItemDef(item).costType == 3) //grub cost
                {
                    ReqDef def = LogicManager.GetItemDef(item);
                    int cost = 1 + rand.Next(23);
                    def.cost = cost;
                    LogicManager.EditItemDef(item, def);
                    RandomizerMod.Instance.Settings.AddNewCost(item, cost);
                }
            }
        }

        private static void RandomizeTransitions()
        {
            Stopwatch watch = new Stopwatch();
            watch.Start();


            while (true)
            {
                Log("");
                Log("Beginning transition randomization...");
                SetupTransitionVariables();
                if (RandomizerMod.Instance.Settings.RandomizeAreas) BuildAreaSpanningTree();
                else if (RandomizerMod.Instance.Settings.RandomizeRooms && RandomizerMod.Instance.Settings.ConnectAreas) BuildCARSpanningTree();
                else if (RandomizerMod.Instance.Settings.RandomizeRooms && !RandomizerMod.Instance.Settings.ConnectAreas) BuildRoomSpanningTree();
                else
                {
                    LogError("Ambiguous settings passed to transition randomizer.");
                    throw new NotSupportedException();
                }

                ConnectStartToGraph();
                CompleteTransitionGraph();

                if (ValidateTransitionRandomization()) break;
                if (randomizationError)
                {
                    Log("Error encountered while randomizing transitions, attempting again...");
                }
            }
            watch.Stop();
            RandomizerMod.Instance.Log("Transition randomization finished in " + watch.Elapsed.TotalSeconds + " seconds.");
        }
        private static void RandomizeItems()
        {
            Stopwatch watch = new Stopwatch();
            watch.Start();

            Log("");
            Log("Beginning item randomization...");
            SetupItemVariables();
            if (RandomizerMod.Instance.Settings.RandomizeTransitions) PlaceFury();
            FirstPass();
            SecondPass();
            if (!ValidateItemRandomization())
            {
                randomizationError = true;
                return;
            }

            RandomizerMod.Instance.Log("Item randomization finished in " + watch.Elapsed.TotalSeconds + " seconds.");
        }

        private static void PostRandomizationTasks()
        {
            SaveAllPlacements();
            SaveItemHints();
            //No vanilla'd loctions in the spoiler log, please!
            (string, string)[] itemPairs = RandomizerMod.Instance.Settings.ItemPlacements.Except(VanillaManager.Instance.ItemPlacements).ToArray();
            if (RandomizerMod.Instance.Settings.CreateSpoilerLog) RandoLogger.LogAllToSpoiler(itemPairs, RandomizerMod.Instance.Settings._transitionPlacements.Select(kvp => (kvp.Key, kvp.Value)).ToArray());
        }

        private static void SetupTransitionVariables()
        {
            randomizationError = false;

            tm = new TransitionManager(rand);
            im = new ItemManager(rand);
        }
        private static void SetupItemVariables()
        {
            im = new ItemManager(rand);

            overflow = false;
        }

        private static void BuildAreaSpanningTree()
        {
            List<string> areas = new List<string>();
            Dictionary<string, List<string>> areaTransitions = new Dictionary<string, List<string>>();

            foreach (string transition in LogicManager.TransitionNames())
            {
                TransitionDef def = LogicManager.GetTransitionDef(transition);
                string areaName = def.areaName;
                if (areaName == "Kings_Pass") continue;
                if (new List<string> { "Dirtmouth", "Forgotten_Crossroads", "Resting_Grounds" }.Contains(areaName)) areaName = "Kings_Station";
                if (new List<string> { "Ancient_Basin", "Kingdoms_Edge" }.Contains(areaName)) areaName = "Deepnest";

                if (!areas.Contains(areaName) && !def.deadEnd && !def.isolated)
                {
                    areas.Add(areaName);
                    areaTransitions.Add(areaName, new List<string>());
                }
            }

            foreach (string transition in LogicManager.TransitionNames())
            {
                TransitionDef def = LogicManager.GetTransitionDef(transition);
                string areaName = def.areaName;
                if (def.oneWay == 0 && areas.Contains(areaName)) areaTransitions[areaName].Add(transition);
            }

            BuildSpanningTree(areaTransitions);
            PlaceOneWayTransitions();
            PlaceIsolatedTransitions();
        }

        private static void BuildRoomSpanningTree()
        {
            List<string> rooms = new List<string>();
            Dictionary<string, List<string>> roomTransitions = new Dictionary<string, List<string>>();

            foreach (string transition in LogicManager.TransitionNames())
            {
                TransitionDef def = LogicManager.GetTransitionDef(transition);
                string roomName = def.sceneName;
                if (roomName == "Tutorial_01") continue;
                if (new List<string> { "Crossroads_46", "Crossroads_46b" }.Contains(roomName)) roomName = "Crossroads_46";
                if (new List<string> { "Abyss_03", "Abyss_03_b", "Abyss_03_c" }.Contains(roomName)) roomName = "Abyss_03";
                if (new List<string> { "Ruins2_10", "Ruins2_10b" }.Contains(roomName)) roomName = "Ruins2_10";

                if (!rooms.Contains(roomName) && !def.deadEnd && !def.isolated)
                {
                    rooms.Add(roomName);
                    roomTransitions.Add(roomName, new List<string>());
                }
            }

            foreach (string transition in LogicManager.TransitionNames())
            {
                TransitionDef def = LogicManager.GetTransitionDef(transition);
                string roomName = def.sceneName;
                if (def.oneWay == 0 && rooms.Contains(roomName)) roomTransitions[roomName].Add(transition);
            }

            BuildSpanningTree(roomTransitions);
            PlaceOneWayTransitions();
            PlaceIsolatedTransitions();
        }

        private static void BuildCARSpanningTree()
        {
            PlaceOneWayTransitions();
            List<string> areas = new List<string>();
            Dictionary<string, List<string>> rooms = new Dictionary<string, List<string>>();
            foreach (string t in tm.unplacedTransitions)
            {
                if (!LogicManager.GetTransitionDef(t).isolated || !LogicManager.GetTransitionDef(t).deadEnd)
                {
                    if (!areas.Contains(LogicManager.GetTransitionDef(t).areaName))
                    {
                        areas.Add(LogicManager.GetTransitionDef(t).areaName);
                        rooms.Add(LogicManager.GetTransitionDef(t).areaName, new List<string>());
                    }


                    if (!rooms[LogicManager.GetTransitionDef(t).areaName].Contains(LogicManager.GetTransitionDef(t).sceneName))
                        rooms[LogicManager.GetTransitionDef(t).areaName].Add(LogicManager.GetTransitionDef(t).sceneName);
                }
            }

            var areaTransitions = new Dictionary<string, Dictionary<string, List<string>>>(); // [area][scene][transition]
            foreach (string area in areas) areaTransitions.Add(area, new Dictionary<string, List<string>>());
            foreach (var kvp in rooms) foreach (string room in kvp.Value) areaTransitions[kvp.Key].Add(room, new List<string>());
            foreach (string t in tm.unplacedTransitions)
            {
                TransitionDef def = LogicManager.GetTransitionDef(t);
                if (!areas.Contains(def.areaName) || !areaTransitions[def.areaName].ContainsKey(def.sceneName)) continue;
                areaTransitions[def.areaName][def.sceneName].Add(t);
            }
            foreach (string area in areas) BuildSpanningTree(areaTransitions[area]);
            var worldTransitions = new Dictionary<string, List<string>>();
            foreach (string area in areas)
            {
                if (area == "Kings_Pass") continue;
                worldTransitions.Add(area, new List<string>());
            }
            foreach (string t in tm.unplacedTransitions)
            {
                if (t.StartsWith("Tut")) continue;
                if (areas.Contains(LogicManager.GetTransitionDef(t).areaName) && rooms[LogicManager.GetTransitionDef(t).areaName].Contains(LogicManager.GetTransitionDef(t).sceneName))
                {
                    worldTransitions[LogicManager.GetTransitionDef(t).areaName].Add(t);
                }
            }
            BuildSpanningTree(worldTransitions);
            PlaceIsolatedTransitions();
        }

        private static void BuildSpanningTree(Dictionary<string, List<string>> sortedTransitions, string first = null)
        {
            List<string> remaining = sortedTransitions.Keys.ToList();
            while (first == null)
            {
                first = remaining[rand.Next(remaining.Count)];
                if (!sortedTransitions[first].Any(t => !LogicManager.GetTransitionDef(t).isolated)) first = null;
            }
            remaining.Remove(first);
            List<DirectedTransitions> directed = new List<DirectedTransitions>();
            directed.Add(new DirectedTransitions(rand));
            directed[0].Add(sortedTransitions[first].Where(t => !LogicManager.GetTransitionDef(t).isolated).ToList());
            int failsafe = 0;

            while (remaining.Any())
            {
                bool placed = false;
                failsafe++;
                if (failsafe > 500 || !directed[0].AnyCompatible())
                {
                    Log("Triggered failsafe on round " + failsafe + " in BuildSpanningTree, where first transition set was: " + first + " with count: " + sortedTransitions[first].Count);
                    randomizationError = true;
                    return;
                }

                string nextRoom = remaining[rand.Next(remaining.Count)];

                foreach (DirectedTransitions dt in directed)
                {
                    List<string> nextAreaTransitions = sortedTransitions[nextRoom].Where(transition => !LogicManager.GetTransitionDef(transition).deadEnd && dt.Test(transition)).ToList();
                    List<string> newTransitions = sortedTransitions[nextRoom].Where(transition => !LogicManager.GetTransitionDef(transition).isolated).ToList();

                    if (!nextAreaTransitions.Any())
                    {
                        continue;
                    }

                    string transitionTarget = nextAreaTransitions[rand.Next(nextAreaTransitions.Count)];
                    string transitionSource = dt.GetNextTransition(transitionTarget);

                    tm.PlaceTransitionPair(transitionSource, transitionTarget);
                    remaining.Remove(nextRoom);

                    dt.Add(newTransitions);
                    dt.Remove(transitionTarget, transitionSource);
                    placed = true;
                    break;
                }
                if (placed) continue;
                else
                {
                    DirectedTransitions dt = new DirectedTransitions(rand);
                    dt.Add(sortedTransitions[nextRoom].Where(transition => !LogicManager.GetTransitionDef(transition).isolated).ToList());
                    directed.Add(dt);
                    remaining.Remove(nextRoom);
                }
            }
            //Log("Completed first pass of BuildSpanningTree with " + directed.Count + " connected component(s).");
            for (int i = 0; i < directed.Count; i++)
            {
                DirectedTransitions dt = directed[i];
                DirectedTransitions dt1 = null;
                string transition1 = null;
                string transition2 = null;

                foreach (var dt2 in directed)
                {
                    if (dt == dt2) continue;

                    if (dt.left && dt2.right)
                    {
                        transition1 = dt.leftTransitions[rand.Next(dt.leftTransitions.Count)];
                        transition2 = dt2.rightTransitions[rand.Next(dt2.rightTransitions.Count)];
                        dt1 = dt2;
                        break;
                    }
                    else if (dt.right && dt2.left)
                    {
                        transition1 = dt.rightTransitions[rand.Next(dt.rightTransitions.Count)];
                        transition2 = dt2.leftTransitions[rand.Next(dt2.leftTransitions.Count)];
                        dt1 = dt2;
                        break;
                    }
                    else if (dt.top && dt2.bot)
                    {
                        transition1 = dt.topTransitions[rand.Next(dt.topTransitions.Count)];
                        transition2 = dt2.botTransitions[rand.Next(dt2.botTransitions.Count)];
                        dt1 = dt2;
                        break;
                    }
                    else if (dt.bot && dt2.top)
                    {
                        transition1 = dt.botTransitions[rand.Next(dt.botTransitions.Count)];
                        transition2 = dt2.topTransitions[rand.Next(dt2.topTransitions.Count)];
                        dt1 = dt2;
                        break;
                    }
                }
                if (!string.IsNullOrEmpty(transition1))
                {
                    tm.PlaceTransitionPair(transition1, transition2);
                    dt1.Add(dt.AllTransitions);
                    dt1.Remove(transition1, transition2);
                    directed.Remove(dt);
                    i = -1;
                }
            }
            //Log("Exited BuildSpanningTree with " + directed.Count + " connected component(s).");
        }

        private static void PlaceOneWayTransitions()
        {
            if (randomizationError) return;
            List<string> oneWayEntrances = LogicManager.TransitionNames().Where(transition => LogicManager.GetTransitionDef(transition).oneWay == 1).ToList();
            List<string> oneWayExits = LogicManager.TransitionNames().Where(transition => LogicManager.GetTransitionDef(transition).oneWay == 2).ToList();
            List<string> horizontalOneWays = oneWayEntrances.Where(t => !LogicManager.GetTransitionDef(t).doorName.StartsWith("b")).ToList();

            while (horizontalOneWays.Any())
            {
                string horizontalEntrance = horizontalOneWays.First();
                string downExit = oneWayExits[rand.Next(oneWayExits.Count)];
                tm.PlaceOneWayPair(horizontalEntrance, downExit);
                oneWayEntrances.Remove(horizontalEntrance);
                horizontalOneWays.Remove(horizontalEntrance);
                oneWayExits.Remove(downExit);
            }

            DirectedTransitions directed = new DirectedTransitions(rand);
            directed.Add(oneWayExits);
            while (oneWayEntrances.Any())
            {
                string entrance = oneWayEntrances[rand.Next(oneWayEntrances.Count)];
                string exit = directed.GetNextTransition(entrance);
                tm.PlaceOneWayPair(entrance, exit);
                oneWayEntrances.Remove(entrance);
                oneWayExits.Remove(exit);
                directed.Remove(exit);
            }
        }

        private static void PlaceIsolatedTransitions()
        {
            if (randomizationError) return;

            List<string> isolatedTransitions = tm.unplacedTransitions.Where(transition => LogicManager.GetTransitionDef(transition).isolated).ToList();
            List<string> nonisolatedTransitions = tm.unplacedTransitions.Where(transition => !LogicManager.GetTransitionDef(transition).isolated).ToList();
            DirectedTransitions directed = new DirectedTransitions(rand);
            directed.Add(nonisolatedTransitions);
            directed.Remove("Tutorial_01[right1]", "Tutorial_01[top2]");
            while (isolatedTransitions.Any())
            {
                string transition1 = isolatedTransitions[rand.Next(isolatedTransitions.Count)];
                string transition2 = directed.GetNextTransition(transition1);
                if (transition2 is null)
                {
                    Log("Ran out of nonisolated transitions during preplacement!");
                    randomizationError = true;
                    return;
                }
                tm.PlaceStandbyPair(transition1, transition2);
                isolatedTransitions.Remove(transition1);
                directed.Remove(transition2);
            }
        }

        private static void ConnectStartToGraph()
        {
            if (randomizationError) return;
            Log("Attaching King's Pass to graph...");

            tm.pm = new ProgressionManager();
            im.ResetReachableLocations();
            tm.ResetReachableTransitions();

            {   // keeping local variables out of the way
                // We place a random item at fotf, and then force transitions which unlock new transitions until we reach a place with more item locations

                string placeItem = im.SpecialGuessItem();
                tm.pm.Add(placeItem);
                im.PlaceItem(placeItem, "Fury_of_the_Fallen");
                tm.firstItem = placeItem;

                string transition1 = "Tutorial_01[right1]";

                DirectedTransitions d = new DirectedTransitions(rand);
                d.Add(transition1);
                string transition2 = tm.ForceTransition(d);
                if (transition2 is null) // this should happen extremely rarely, but it has to be handled
                {
                    Log("No way out of King's Pass?!?");
                    randomizationError = true;
                    return;
                }
                tm.PlaceTransitionPair(transition1, transition2);
            }

            while (true)
            {
                if (im.FindNextLocation(tm.pm) != null) return;

                tm.UnloadReachableStandby();
                List<string> placeableTransitions = tm.reachableTransitions.Intersect(tm.unplacedTransitions.Union(tm.standbyTransitions.Keys)).ToList();
                if (!placeableTransitions.Any())
                {
                    Log("Could not connect King's Pass to map--ran out of placeable transitions.");
                    foreach (string t in tm.reachableTransitions) Log(t);
                    randomizationError = true;
                    return;
                }

                DirectedTransitions directed = new DirectedTransitions(rand);
                directed.Add(placeableTransitions);

                if (tm.ForceTransition(directed) is string transition1)
                {
                    string transition2 = directed.GetNextTransition(transition1);
                    tm.PlaceTransitionPair(transition1, transition2);
                }
                else
                {
                    Log("Could not connect King's Pass to map--ran out of progression transitions.");
                    randomizationError = true;
                    return;
                }
            }
        }

        private static void CompleteTransitionGraph()
        {
            if (randomizationError) return;
            int failsafe = 0;
            Log("Beginning full placement of transitions...");

            while (tm.unplacedTransitions.Any())
            {
                failsafe++;
                if (failsafe > 120)
                {
                    Log("Aborted randomization on too many passes. At the time, there were:");
                    Log("Unplaced transitions: " + tm.unplacedTransitions.Count);
                    Log("Reachable transitions: " + tm.reachableTransitions.Count);
                    Log("Reachable unplaced transitions, directionally compatible: " + tm.placeableCount);
                    Log("Reachable item locations: " + im.availableCount);
                    foreach (string t in tm.unplacedTransitions) Log(t);
                    randomizationError = true;
                    return;
                }

                if (im.canGuess)
                {
                    if (im.FindNextLocation(tm.pm) is string placeLocation)
                    {
                        string placeItem = im.GuessItem();
                        im.PlaceItem(placeItem, placeLocation);
                        tm.UpdateReachableTransitions(placeItem, true);
                    }
                }

                int placeableCount = tm.placeableCount;
                if (placeableCount < 4) tm.UpdateReachableTransitions();
                if (placeableCount == 0 && im.availableCount == 0)
                {
                    Log("Ran out of locations?!?");
                    randomizationError = true;
                    return;
                }
                else if (placeableCount > 2)
                {
                    tm.UnloadReachableStandby();
                    string transition1 = tm.NextTransition();
                    string transition2 = tm.dt.GetNextTransition(transition1);
                    tm.PlaceTransitionPair(transition1, transition2);
                    continue;
                }
                else if (tm.unplacedTransitions.Count == 2)
                {
                    string transition1 = tm.unplacedTransitions[0];
                    string transition2 = tm.unplacedTransitions[1];
                    tm.PlaceTransitionPair(transition1, transition2);
                    continue;
                }
                else if (placeableCount != 0)
                {
                    if (tm.ForceTransition() is string transition1)
                    {
                        string transition2 = tm.dt.GetNextTransition(transition1);
                        tm.PlaceTransitionPair(transition1, transition2);
                        continue;
                    }
                }
                // Last ditch effort to save the seed. The list is ordered by which items are heuristically likely to unlock transitions at this point.
                if (im.FindNextLocation(tm.pm) is string lastLocation)
                {
                    foreach (string item in new List<string> { "Mantis_Claw", "Monarch_Wings", "Desolate_Dive", "Isma's_Tear", "Crystal_Heart", "Mothwing_Cloak", "Shade_Cloak" })
                    {
                        if (!tm.pm.Has(item))
                        {
                            im.PlaceItem(item, lastLocation);
                            tm.UpdateReachableTransitions(item, true);
                            break;
                        }
                    }
                    continue;
                }
            }
            Log("Placing last reserved transitions...");
            tm.UnloadStandby();
            Log("All transitions placed? " + (TransitionManager.transitionPlacements.Count == LogicManager.TransitionNames().Count(t => LogicManager.GetTransitionDef(t).oneWay != 2)));
        }

        private static void PlaceFury()
        {
            im.UpdateReachableLocations("Tutorial_01[right1]");
            im.PlaceItem(tm.firstItem, "Fury_of_the_Fallen");
        }

        private static void FirstPass()
        {
            Log("Beginning first pass of item placement...");
            if (!RandomizerMod.Instance.Settings.RandomizeTransitions)
            {
                im.ResetReachableLocations();
                vm.ResetReachableLocations();
            }

            while (true)
            {
                string placeItem;
                string placeLocation;

                switch (im.availableCount)
                {
                    case 0:
                        if (im.anyLocations)
                        {
                            if (im.canGuess)
                            {
                                if (!overflow) Log("Entered overflow state with 0 reachable locations after placing " + ItemManager.nonShopItems.Count + " locations");
                                overflow = true;
                                placeItem = im.GuessItem();
                                im.PlaceProgressionToStandby(placeItem);
                                continue;
                            }
                        }
                        return;
                    case 1:
                        placeItem = im.ForceItem();
                        if (placeItem is null)
                        {
                            if (im.canGuess)
                            {
                                if (!overflow) Log("Entered overflow state with 1 reachable location after placing " + ItemManager.nonShopItems.Count + " locations");
                                overflow = true;
                                placeItem = im.GuessItem();
                                im.PlaceProgressionToStandby(placeItem);
                                continue;
                            }
                            else placeItem = im.NextItem();
                        }
                        placeLocation = im.NextLocation();
                        break;
                    default:
                        placeItem = im.NextItem();
                        placeLocation = im.NextLocation();
                        break;
                }

                //Log($"i: {placeItem}, l: {placeLocation}, o: {overflow}, p: {LogicManager.GetItemDef(placeItem).progression}");

                if (!overflow && !LogicManager.GetItemDef(placeItem).progression)
                {
                    im.PlaceJunkItemToStandby(placeItem, placeLocation);
                }
                else
                {
                    im.PlaceItem(placeItem, placeLocation);
                }
            }
        }

        private static void SecondPass()
        {
            Log("Beginning second pass of item placement...");
            im.TransferStandby();

            // We fill the remaining locations and shops with the leftover junk
            while (im.anyItems)
            {
                string placeItem = im.NextItem(checkFlag: false);
                string placeLocation;

                if (im.anyLocations) placeLocation = im.NextLocation(checkLogic: false);
                else placeLocation = LogicManager.ShopNames[rand.Next(5)];

                im.PlaceItemFromStandby(placeItem, placeLocation);
            }
        }

        private static bool ValidateTransitionRandomization()
        {
            if (randomizationError) return false;
            Log("Beginning transition placement validation...");

            ProgressionManager pm = new ProgressionManager();
            foreach (string item in LogicManager.ItemNames.Where(i => LogicManager.GetItemDef(i).progression)) pm.Add(item);
            tm.ResetReachableTransitions();
            tm.UpdateReachableTransitions(_pm: pm);
            tm.UpdateReachableTransitions("Tutorial_01[top2]", _pm: pm);
            bool validated = tm.reachableTransitions.SetEquals(LogicManager.TransitionNames());

            if (!validated)
            {
                Log("Transition placements failed to validate!");
                foreach (string t in LogicManager.TransitionNames().Except(tm.reachableTransitions)) Log(t);
            }
            else Log("Validation successful.");
            return validated;
        }

        private static bool ValidateItemRandomization()
        {
            RandomizerMod.Instance.Log("Beginning item placement validation...");

            if (im.randomizedLocations.Except(ItemManager.nonShopItems.Keys).Except(ItemManager.shopItems.Keys).Any())
            {
                Log("Unable to validate!");
                string m = "The following locations were not filled: ";
                foreach (string l in im.randomizedLocations.Except(ItemManager.nonShopItems.Keys).Except(ItemManager.shopItems.Keys)) m += l + ", ";
                Log(m);
                return false;
            }

            ProgressionManager pm = new ProgressionManager();

            HashSet<string> everything = new HashSet<string>(im.randomizedLocations.Union(vm.progressionLocations));

            if (RandomizerMod.Instance.Settings.RandomizeTransitions)
            {
                everything.UnionWith(LogicManager.TransitionNames());
                tm.ResetReachableTransitions();
                tm.UpdateReachableTransitions(_pm: pm);
            } else
            {
                vm.ResetReachableLocations(false, pm);
            }

            int passes = 0;
            while (everything.Any())
            {
                if (RandomizerMod.Instance.Settings.RandomizeTransitions) everything.ExceptWith(tm.reachableTransitions);

                foreach (string location in im.randomizedLocations.Union(vm.progressionLocations).Where(loc => everything.Contains(loc) && pm.CanGet(loc)))
                {
                    everything.Remove(location);
                    if (vm.progressionLocations.Contains(location)) vm.UpdateVanillaLocations(location, false, pm);
                    else if (LogicManager.ShopNames.Contains(location))
                    {
                        foreach (string newItem in ItemManager.shopItems[location])
                        {
                            if (LogicManager.GetItemDef(newItem).progression)
                            {
                                pm.Add(newItem);
                                if (RandomizerMod.Instance.Settings.RandomizeTransitions) tm.UpdateReachableTransitions(newItem, true, pm);
                            }
                        }
                    }
                    else if (LogicManager.GetItemDef(ItemManager.nonShopItems[location]).progression)
                    {
                        pm.Add(ItemManager.nonShopItems[location]);
                        if (RandomizerMod.Instance.Settings.RandomizeTransitions) tm.UpdateReachableTransitions(ItemManager.nonShopItems[location], true, pm);
                    }
                }

                passes++;
                if (passes > 400)
                {
                    Log("Unable to validate!");
                    Log("Able to get: " + pm.ListObtainedProgression() + Environment.NewLine + "Grubs: " + pm.obtained[LogicManager.grubIndex] + Environment.NewLine + "Essence: " + pm.obtained[LogicManager.essenceIndex]);
                    string m = string.Empty;
                    foreach (string s in everything) m += s + ", ";
                    Log("Unable to get: " + m);
                    return false;
                }
            }
            Log("Validation successful.");
            return true;
        }

        private static void SaveAllPlacements()
        {
            if (RandomizerMod.Instance.Settings.RandomizeTransitions)
            {
                foreach (KeyValuePair<string, string> kvp in TransitionManager.transitionPlacements)
                {
                    RandomizerMod.Instance.Settings.AddTransitionPlacement(kvp.Key, kvp.Value);
                }
            }

            foreach (KeyValuePair<string, List<string>> kvp in ItemManager.shopItems)
            {
                foreach (string item in kvp.Value)
                {
                    RandomizerMod.Instance.Settings.AddItemPlacement(item, kvp.Key);
                }
            }
            foreach (KeyValuePair<string, string> kvp in ItemManager.nonShopItems)
            {
                RandomizerMod.Instance.Settings.AddItemPlacement(kvp.Value, kvp.Key);
            }

            //Vanilla Item Placements (for RandomizerActions, Hints, Logs, etc)
            foreach ((string, string) pair in vm.ItemPlacements)
            {
                RandomizerMod.Instance.Settings.AddItemPlacement(pair.Item1, pair.Item2);
            }
        }

        private static void SaveItemHints()
        {
            //Create a randomly ordered list of all major items in nonshop locations
            List<string> goodPools = new List<string> { "Dreamer", "Skill", "Charm", "Key" };
            List<string> possibleHintItems = ItemManager.nonShopItems.Values.Where(val => goodPools.Contains(LogicManager.GetItemDef(val).pool)).ToList();
            Dictionary<string, string> inverseNonShopItems = ItemManager.nonShopItems.ToDictionary(x => x.Value, x => x.Key); // There can't be two items at the same location, so this inversion is safe
            while (possibleHintItems.Count > 0)
            {
                string item = possibleHintItems[rand.Next(possibleHintItems.Count)];
                RandomizerMod.Instance.Settings.AddNewHint(item, inverseNonShopItems[item]);
                possibleHintItems.Remove(item);
            }
        }
    }
}
