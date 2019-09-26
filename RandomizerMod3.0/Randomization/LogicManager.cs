using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Xml;
using SeanprCore;
using static RandomizerMod.LogHelper;
using System.Text.RegularExpressions;

namespace RandomizerMod.Randomization
{
    internal enum ItemType
    {
        Big,
        Charm,
        Trinket,
        Shop,
        Spell,
        Geo
    }

    // ReSharper disable InconsistentNaming
#pragma warning disable 0649 // Assigned via reflection
    internal struct TransitionDef
    {
        public string sceneName;
        public string doorName;
        public string areaName;

        public string destinationScene;
        public string destinationGate;

        public string[] logic;
        public List<(int, int)> processedLogic;

        public bool isolated;
        public bool deadEnd;
        public int oneWay; // 0 == 2-way, 1 == can only go in, 2 == can only come out
    }
    internal struct ReqDef
    {
        // Control variables
        public string boolName;
        public string sceneName;
        public string objectName;
        public string shopName;
        public string altObjectName;
        public string fsmName;
        public bool replace;
        public string[] itemLogic;
        public List<(int, int)> processedItemLogic;
        public string[] areaLogic;
        public List<(int, int)> processedAreaLogic;
        public string[] roomLogic;
        public List<(int, int)> processedRoomLogic;

        public ItemType type;
        public string pool;
        public string areaName;

        public bool newShiny;
        public int x;
        public int y;

        // Big item variables
        public string bigSpriteKey;
        public string takeKey;
        public string nameKey;
        public string buttonKey;
        public string descOneKey;
        public string descTwoKey;

        // Shop variables
        public string shopDescKey;
        public string shopSpriteKey;
        public string notchCost;

        // Trinket variables
        public int trinketNum;

        // Item tier flags
        public bool progression;
        public bool itemCandidate; // Excludes progression items which are unlikely to open new locations in a pinch, such as charms that only kill baldurs or assist with spiketunnels
        public bool areaCandidate; // Only those items which are very likely to open new transitions
        public bool isGoodItem;
        public bool isFake;
        public int longItemTier; // For future use with tiered bonus items?

        // Geo flags
        public bool inChest;
        public int geo;

        public string chestName;
        public string chestFsmName;

        // For pricey items such as dash slash location
        public int cost;
        public int costType;
    }

    internal struct ShopDef
    {
        public string sceneName;
        public string objectName;

        public string[] itemLogic;
        public List<(int, int)> processedItemLogic;
        public string[] areaLogic;
        public List<(int, int)> processedAreaLogic;
        public string[] roomLogic;
        public List<(int, int)> processedRoomLogic;

        public string requiredPlayerDataBool;
        public bool dungDiscount;
    }
#pragma warning restore 0649
    // ReSharper restore InconsistentNaming

    internal static class LogicManager
    {
        private static Dictionary<string, TransitionDef> _areaTransitions;
        private static Dictionary<string, TransitionDef> _roomTransitions;
        private static Dictionary<string, ReqDef> _items;
        private static Dictionary<string, ShopDef> _shops;
        private static Dictionary<string, string[]> _additiveItems;
        private static Dictionary<string, string[]> _macros;
        private static Dictionary<string, HashSet<string>> _progressionIndexedItemsForItemRando;
        private static Dictionary<string, HashSet<string>> _progressionIndexedItemsForAreaRando;
        private static Dictionary<string, HashSet<string>> _progressionIndexedItemsForRoomRando;
        private static Dictionary<string, HashSet<string>> _progressionIndexedTransitionsForAreaRando;
        private static Dictionary<string, HashSet<string>> _progressionIndexedTransitionsForRoomRando;
        private static Dictionary<string, HashSet<string>> _poolIndexedItems;
        public static HashSet<string> grubProgression;
        public static HashSet<string> essenceProgression;

        public static Dictionary<string, (int, int)> progressionBitMask;
        public static int bitMaskMax;
        public static int essenceIndex;
        public static int grubIndex;
        public static Dictionary<string, (int, int)> itemCountsByPool = null;

        public static string[] ItemNames => _items.Keys.ToArray();

        public static string[] ShopNames => _shops.Keys.ToArray();

        public static string[] AdditiveItemNames => _additiveItems.Keys.ToArray();

        public static void ParseXML(Assembly randoDLL)
        {
            XmlDocument additiveXml;
            XmlDocument macroXml;
            XmlDocument areaXml;
            XmlDocument roomXml;
            XmlDocument itemXml;
            XmlDocument shopXml;

            Stopwatch watch = new Stopwatch();
            watch.Start();

            try
            {
                Stream additiveStream = randoDLL.GetManifestResourceStream("RandomizerMod.Resources.additive.xml");
                additiveXml = new XmlDocument();
                additiveXml.Load(additiveStream);
                additiveStream.Dispose();

                Stream macroStream = randoDLL.GetManifestResourceStream("RandomizerMod.Resources.macros.xml");
                macroXml = new XmlDocument();
                macroXml.Load(macroStream);
                macroStream.Dispose();

                Stream areaStream = randoDLL.GetManifestResourceStream("RandomizerMod.Resources.areas.xml");
                areaXml = new XmlDocument();
                areaXml.Load(areaStream);
                areaStream.Dispose();

                Stream roomStream = randoDLL.GetManifestResourceStream("RandomizerMod.Resources.rooms.xml");
                roomXml = new XmlDocument();
                roomXml.Load(roomStream);
                roomStream.Dispose();

                Stream itemStream = randoDLL.GetManifestResourceStream("RandomizerMod.Resources.items.xml");
                itemXml = new XmlDocument();
                itemXml.Load(itemStream);
                itemStream.Dispose();

                Stream shopStream = randoDLL.GetManifestResourceStream("RandomizerMod.Resources.shops.xml");
                shopXml = new XmlDocument();
                shopXml.Load(shopStream);
                shopStream.Dispose();
            }
            catch(Exception e)
            {
                LogError("Could not load xml streams:\n" + e);
                return;
            }
            try
            {
                _macros = new Dictionary<string, string[]>();
                _additiveItems = new Dictionary<string, string[]>();
                _areaTransitions = new Dictionary<string, TransitionDef>();
                _roomTransitions = new Dictionary<string, TransitionDef>();
                _items = new Dictionary<string, ReqDef>();
                _shops = new Dictionary<string, ShopDef>();

                ParseAdditiveItemXML(additiveXml.SelectNodes("randomizer/additiveItemSet"));
                ParseMacroXML(macroXml.SelectNodes("randomizer/macro"));
                ParseTransitionXML(areaXml.SelectNodes("randomizer/transition"), room: false);
                ParseTransitionXML(roomXml.SelectNodes("randomizer/transition"), room: true);
                ParseItemXML(itemXml.SelectNodes("randomizer/item"));
                ParseShopXML(shopXml.SelectNodes("randomizer/shop"));
                CreateShortcuts();
                ProcessLogic();
            }
            catch (Exception e)
            {
                LogError("Could not parse xml nodes:\n" + e);
            }

            watch.Stop();
            Log("Parsed items.xml in " + watch.Elapsed.TotalSeconds + " seconds");
        }

        public static string[] TransitionNames(bool area = false, bool room = false)
        {
            if (area) return _areaTransitions.Keys.ToArray();
            if (room) return _roomTransitions.Keys.ToArray();

            if (RandomizerMod.Instance.Settings.RandomizeAreas) return _areaTransitions.Keys.ToArray();
            else return _roomTransitions.Keys.ToArray();
        }
        public static TransitionDef GetTransitionDef(string name)
        {
            if (RandomizerMod.Instance.Settings.RandomizeAreas && _areaTransitions.TryGetValue(name, out TransitionDef def1))
            {
                return def1;
            }
            else if (RandomizerMod.Instance.Settings.RandomizeRooms && _roomTransitions.TryGetValue(name, out TransitionDef def2))
            {
                return def2;
            }
            else if (!RandomizerMod.Instance.Settings.RandomizeAreas && !RandomizerMod.Instance.Settings.RandomizeRooms)
            {
                LogWarn("Requested transition with ambiguous randomization settings.");
            }
            else
            {
                LogWarn($"Nonexistent transition \"{name}\" requested");
            }
            return new TransitionDef();
        }
        public static ReqDef GetItemDef(string name)
        {
            string newName = Regex.Replace(name, @"_\(\d+\)$", ""); // an item name ending in _(1) is processed as a duplicate
            if (!_items.TryGetValue(newName, out ReqDef def))
            {
                LogWarn($"Nonexistent item \"{name}\" requested");
            }
            if (newName != name) def.boolName += name.Substring(name.Length - 4); // duplicate items need distinct bools

            return def;
        }

        public static void EditItemDef(string item, ReqDef newDef)
        {
            if (!_items.ContainsKey(item))
            {
                LogWarn($"Nonexistent item \"{item}\" requested");
            }
            _items[item] = newDef;
        }

        public static HashSet<string> GetItemsByPool(string pool)
        {
            return new HashSet<string>(_poolIndexedItems[pool]);
        }

        public static HashSet<string> GetItemsByProgression(string newProgression)
        {
            if (RandomizerMod.Instance.Settings.RandomizeRooms) return new HashSet<string>(_progressionIndexedItemsForRoomRando[newProgression]);
            else if (RandomizerMod.Instance.Settings.RandomizeAreas) return new HashSet<string>(_progressionIndexedItemsForAreaRando[newProgression]);
            else return new HashSet<string>(_progressionIndexedItemsForItemRando[newProgression]);
        }
        public static HashSet<string> GetTransitionsByProgression(string newProgression)
        {
            if (RandomizerMod.Instance.Settings.RandomizeRooms) return new HashSet<string>(_progressionIndexedTransitionsForRoomRando[newProgression]);
            else return new HashSet<string>(_progressionIndexedTransitionsForAreaRando[newProgression]);
        }

        public static ShopDef GetShopDef(string name)
        {
            if (!_shops.TryGetValue(name, out ShopDef def))
            {
                LogWarn($"Nonexistent shop \"{name}\" requested");
            }

            return def;
        }

        public static (int, int) GetPoolCount(string poolName)
        {
            //Lazy init
            if (itemCountsByPool == null)
            {
                itemCountsByPool = new Dictionary<string, (int, int)>();
                foreach (string pool in _poolIndexedItems.Keys)
                {
                    int poolCount = _poolIndexedItems[pool].Count();
                    int progCount = _poolIndexedItems[pool].Where(val => GetItemDef(val).progression).Count();
                    itemCountsByPool.Add(pool, (progCount, poolCount));
                    Log($"Pool '{pool}' has {progCount} progression out of {poolCount} total items.");
                }
            }

            //Actual Function
            return itemCountsByPool[poolName];
        }

        public static bool ParseProcessedLogic(string item, int[] obtained)
        {
            List<(int, int)> logic;
            int cost = 0;

            if (_items.TryGetValue(item, out ReqDef reqDef))
            {
                if (RandomizerMod.Instance.Settings.RandomizeAreas) logic = reqDef.processedAreaLogic;
                else if (RandomizerMod.Instance.Settings.RandomizeRooms) logic = reqDef.processedRoomLogic;
                else logic = reqDef.processedItemLogic;
                cost = reqDef.cost;
            }
            else if (_shops.TryGetValue(item, out ShopDef shopDef))
            {
                if (RandomizerMod.Instance.Settings.RandomizeAreas) logic = shopDef.processedAreaLogic;
                else if (RandomizerMod.Instance.Settings.RandomizeRooms) logic = shopDef.processedRoomLogic;
                else logic = shopDef.processedItemLogic;
            }
            else if (RandomizerMod.Instance.Settings.RandomizeAreas && _areaTransitions.TryGetValue(item, out TransitionDef areaTransition))
            {
                logic = areaTransition.processedLogic;
            }
            else if (RandomizerMod.Instance.Settings.RandomizeRooms && _roomTransitions.TryGetValue(item, out TransitionDef roomTransition))
            {
                logic = roomTransition.processedLogic;
            }
            else
            {
                RandomizerMod.Instance.LogWarn($"ParseProcessedLogic called for non-existent item/shop \"{item}\"");
                return false;
            }

            if (logic == null || logic.Count == 0)
            {
                return true;
            }

            Stack<bool> stack = new Stack<bool>();

            for (int i = 0; i < logic.Count; i++)
            {
                switch (logic[i].Item1)
                {
                    //AND
                    case -2:
                        if (stack.Count < 2)
                        {
                            RandomizerMod.Instance.LogWarn($"Could not parse logic for \"{item}\": Found + when stack contained less than 2 items");
                            return false;
                        }

                        stack.Push(stack.Pop() & stack.Pop());
                        break;
                    //OR
                    case -1:
                        if (stack.Count < 2)
                        {
                            RandomizerMod.Instance.LogWarn($"Could not parse logic for \"{item}\": Found | when stack contained less than 2 items");
                            return false;
                        }
                        stack.Push(stack.Pop() | stack.Pop());
                        break;
                    //EVERYTHING - DO NOT USE, WILL BREAK THE RANDOMIZER
                    case 0:
                        stack.Push(false);
                        break;
                    // ESSENCECOUNT
                    case -3:
                        stack.Push(obtained[essenceIndex] >= cost + 25);
                        break;
                    // GRUBCOUNT
                    case -4:
                        stack.Push(obtained[grubIndex] >= cost);
                        break;
                    default:
                        stack.Push((logic[i].Item1 & obtained[logic[i].Item2]) == logic[i].Item1);
                        break;
                }
            }

            if (stack.Count == 0)
            {
                LogWarn($"Could not parse logic for \"{item}\": Stack empty after parsing");
                return false;
            }

            if (stack.Count != 1)
            {
                LogWarn($"Extra items in stack after parsing logic for \"{item}\"");
            }

            return stack.Pop();
        }

        public static string[] GetAdditiveItems(string name)
        {
            if (!_additiveItems.TryGetValue(name, out string[] items))
            {
                LogWarn($"Nonexistent additive item set \"{name}\" requested");
                return null;
            }

            return (string[]) items.Clone();
        }

        private static string[] ShuntingYard(string infix)
        {
            int i = 0;
            Stack<string> stack = new Stack<string>();
            List<string> postfix = new List<string>();

            while (i < infix.Length)
            {
                string op = GetNextOperator(infix, ref i);

                // Easiest way to deal with whitespace between operators
                if (op.Trim(' ') == string.Empty)
                {
                    continue;
                }

                if (op == "+" || op == "|")
                {
                    while (stack.Count != 0 && (op == "|" || op == "+" && stack.Peek() != "|") && stack.Peek() != "(")
                    {
                        postfix.Add(stack.Pop());
                    }

                    stack.Push(op);
                }
                else if (op == "(")
                {
                    stack.Push(op);
                }
                else if (op == ")")
                {
                    while (stack.Peek() != "(")
                    {
                        postfix.Add(stack.Pop());
                    }

                    stack.Pop();
                }
                else
                {
                    // Parse macros
                    if (_macros.TryGetValue(op, out string[] macro))
                    {
                        postfix.AddRange(macro);
                    }
                    else
                    {
                        postfix.Add(op);
                    }
                }
            }

            while (stack.Count != 0)
            {
                postfix.Add(stack.Pop());
            }

            return postfix.ToArray();
        }

        private static void CreateShortcuts()
        {
            _progressionIndexedItemsForItemRando = new Dictionary<string, HashSet<string>>();
            _progressionIndexedItemsForAreaRando = new Dictionary<string, HashSet<string>>();
            _progressionIndexedItemsForRoomRando = new Dictionary<string, HashSet<string>>();

            _progressionIndexedTransitionsForAreaRando = new Dictionary<string, HashSet<string>>();
            _progressionIndexedTransitionsForRoomRando = new Dictionary<string, HashSet<string>>();

            _poolIndexedItems = new Dictionary<string, HashSet<string>>();
            grubProgression = new HashSet<string>();
            essenceProgression = new HashSet<string>();

            foreach (string item in ItemNames)
            {
                if (_items[item].progression)
                {
                    _progressionIndexedItemsForItemRando.Add(item, new HashSet<string>());
                    _progressionIndexedItemsForAreaRando.Add(item, new HashSet<string>());
                    _progressionIndexedItemsForRoomRando.Add(item, new HashSet<string>());

                    _progressionIndexedTransitionsForAreaRando.Add(item, new HashSet<string>());
                    _progressionIndexedTransitionsForRoomRando.Add(item, new HashSet<string>());
                }
                if (!_poolIndexedItems.ContainsKey(_items[item].pool)) _poolIndexedItems.Add(_items[item].pool, new HashSet<string>());
                _poolIndexedItems[_items[item].pool].Add(item);
            }
            foreach (string transition in _roomTransitions.Keys)
            {
                _progressionIndexedItemsForAreaRando.Add(transition, new HashSet<string>());
                _progressionIndexedItemsForRoomRando.Add(transition, new HashSet<string>());
                _progressionIndexedTransitionsForAreaRando.Add(transition, new HashSet<string>());
                _progressionIndexedTransitionsForRoomRando.Add(transition, new HashSet<string>());
            }

            foreach (string item in ItemNames)
            {
                foreach (string i in _items[item].itemLogic) if (_progressionIndexedItemsForItemRando.ContainsKey(i)) _progressionIndexedItemsForItemRando[i].Add(item);
                foreach (string i in _items[item].areaLogic) if (_progressionIndexedItemsForAreaRando.ContainsKey(i)) _progressionIndexedItemsForAreaRando[i].Add(item);
                foreach (string i in _items[item].roomLogic) if (_progressionIndexedItemsForRoomRando.ContainsKey(i)) _progressionIndexedItemsForRoomRando[i].Add(item);

                if (_items[item].pool == "Essence")
                {
                    foreach (string i in _items[item].itemLogic) essenceProgression.Add(i);
                    foreach (string i in _items[item].areaLogic) essenceProgression.Add(i);
                    foreach (string i in _items[item].roomLogic) essenceProgression.Add(i);
                }
                else if (_items[item].pool == "Grub")
                {
                    foreach (string i in _items[item].itemLogic) grubProgression.Add(i);
                    foreach (string i in _items[item].areaLogic) grubProgression.Add(i);
                    foreach (string i in _items[item].roomLogic) grubProgression.Add(i);
                }
            }
            foreach (string shop in ShopNames)
            {
                foreach (string i in _shops[shop].itemLogic) if (_progressionIndexedItemsForItemRando.ContainsKey(i)) _progressionIndexedItemsForItemRando[i].Add(shop);
                foreach (string i in _shops[shop].areaLogic) if (_progressionIndexedItemsForAreaRando.ContainsKey(i)) _progressionIndexedItemsForAreaRando[i].Add(shop);
                foreach (string i in _shops[shop].roomLogic) if (_progressionIndexedItemsForRoomRando.ContainsKey(i)) _progressionIndexedItemsForRoomRando[i].Add(shop);
            }
            foreach (string transition in _areaTransitions.Keys)
            {
                if (_areaTransitions[transition].isolated || _areaTransitions[transition].oneWay == 2) continue;
                foreach (string i in _areaTransitions[transition].logic) if (_progressionIndexedTransitionsForAreaRando.ContainsKey(i)) _progressionIndexedTransitionsForAreaRando[i].Add(transition);
            }
            foreach (string transition in _roomTransitions.Keys)
            {
                if (_roomTransitions[transition].isolated || _roomTransitions[transition].oneWay == 2) continue;
                foreach (string i in _roomTransitions[transition].logic) if (_progressionIndexedTransitionsForRoomRando.ContainsKey(i)) _progressionIndexedTransitionsForRoomRando[i].Add(transition);
            }
        }

        private static void ProcessLogic()
        {
            List<string> roomTransitions = _roomTransitions.Keys.ToList();
            List<string> areaTransitions = _areaTransitions.Keys.ToList();

            progressionBitMask = new Dictionary<string, (int, int)>();
            progressionBitMask.Add("SHADESKIPS", (1, 0));
            progressionBitMask.Add("ACIDSKIPS", (2, 0));
            progressionBitMask.Add("SPIKETUNNELS", (4, 0));
            progressionBitMask.Add("SPICYSKIPS", (8, 0));
            progressionBitMask.Add("FIREBALLSKIPS", (16, 0));
            progressionBitMask.Add("DARKROOMS", (32, 0));
            progressionBitMask.Add("MILDSKIPS", (64, 0));

            int i = 7;

            foreach (string itemName in ItemNames)
            {
                if (_items[itemName].progression)
                {
                    progressionBitMask.Add(itemName, ((int)Math.Pow(2, i), bitMaskMax));
                    i++;
                    if (i == 31)
                    {
                        i = 0;
                        bitMaskMax++;
                    }
                }
            }
            foreach (string transitionName in roomTransitions)
            {
                progressionBitMask.Add(transitionName, ((int)Math.Pow(2, i), bitMaskMax));
                i++;
                if (i == 31)
                {
                    i = 0;
                    bitMaskMax++;
                }
            }

            essenceIndex = bitMaskMax + 1;
            grubIndex = bitMaskMax + 2;
            bitMaskMax = grubIndex;

            foreach (string itemName in ItemNames)
            {
                ReqDef def = _items[itemName];
                string[] infix = def.itemLogic;
                List<(int, int)> postfix = new List<(int, int)>();
                i = 0;
                while (i < infix.Length)
                {
                    if (infix[i] == "|") postfix.Add((-1, 0));
                    else if (infix[i] == "+") postfix.Add((-2, 0));
                    else if (infix[i] == "ESSENCECOUNT") postfix.Add((-3, 0));
                    else if (infix[i] == "GRUBCOUNT") postfix.Add((-4, 0));
                    else
                    {
                        if (!progressionBitMask.TryGetValue(infix[i], out (int, int) pair)) RandomizerMod.Instance.LogWarn("Could not find progression value for: " + infix[i]);
                        postfix.Add(pair);
                    }
                    i++;
                }

                def.processedItemLogic = postfix;

                infix = def.areaLogic;
                postfix = new List<(int, int)>();
                i = 0;
                while (i < infix.Length)
                {
                    if (infix[i] == "|") postfix.Add((-1, 0));
                    else if (infix[i] == "+") postfix.Add((-2, 0));
                    else if (infix[i] == "ESSENCECOUNT") postfix.Add((-3, 0));
                    else if (infix[i] == "GRUBCOUNT") postfix.Add((-4, 0));
                    else
                    {
                        if (!progressionBitMask.TryGetValue(infix[i], out (int, int) pair)) RandomizerMod.Instance.LogWarn("Could not find progression value for: " + infix[i]);
                        postfix.Add(pair);
                    }
                    i++;
                }

                def.processedAreaLogic = postfix;

                infix = def.roomLogic;
                postfix = new List<(int, int)>();
                i = 0;
                while (i < infix.Length)
                {
                    if (infix[i] == "|") postfix.Add((-1, 0));
                    else if (infix[i] == "+") postfix.Add((-2, 0));
                    else if (infix[i] == "ESSENCECOUNT") postfix.Add((-3, 0));
                    else if (infix[i] == "GRUBCOUNT") postfix.Add((-4, 0));
                    else
                    {
                        if (!progressionBitMask.TryGetValue(infix[i], out (int, int) pair)) RandomizerMod.Instance.LogWarn("Could not find progression value for: " + infix[i]);
                        postfix.Add(pair);
                    }
                    i++;
                }

                def.processedRoomLogic = postfix;
                _items[itemName] = def;
            }

            foreach (string shopName in ShopNames)
            {
                ShopDef def = _shops[shopName];
                string[] infix = def.itemLogic;
                List<(int, int)> postfix = new List<(int, int)>();
                i = 0;
                while (i < infix.Length)
                {
                    if (infix[i] == "|") postfix.Add((-1, 0));
                    else if (infix[i] == "+") postfix.Add((-2, 0));
                    else if (infix[i] == "ESSENCECOUNT") postfix.Add((-3, 0));
                    else if (infix[i] == "GRUBCOUNT") postfix.Add((-4, 0));
                    else
                    {
                        if (!progressionBitMask.TryGetValue(infix[i], out (int, int) pair)) RandomizerMod.Instance.LogWarn("Could not find progression value for: " + infix[i]);
                        postfix.Add(pair);
                    }
                    i++;
                }

                def.processedItemLogic = postfix;

                infix = def.areaLogic;
                postfix = new List<(int, int)>();
                i = 0;
                while (i < infix.Length)
                {
                    if (infix[i] == "|") postfix.Add((-1, 0));
                    else if (infix[i] == "+") postfix.Add((-2, 0));
                    else if (infix[i] == "ESSENCECOUNT") postfix.Add((-3, 0));
                    else if (infix[i] == "GRUBCOUNT") postfix.Add((-4, 0));
                    else
                    {
                        if (!progressionBitMask.TryGetValue(infix[i], out (int, int) pair)) RandomizerMod.Instance.LogWarn("Could not find progression value for: " + infix[i]);
                        postfix.Add(pair);
                    }
                    i++;
                }

                def.processedAreaLogic = postfix;

                infix = def.roomLogic;
                postfix = new List<(int, int)>();
                i = 0;
                while (i < infix.Length)
                {
                    if (infix[i] == "|") postfix.Add((-1, 0));
                    else if (infix[i] == "+") postfix.Add((-2, 0));
                    else if (infix[i] == "ESSENCECOUNT") postfix.Add((-3, 0));
                    else if (infix[i] == "GRUBCOUNT") postfix.Add((-4, 0));
                    else
                    {
                        if (!progressionBitMask.TryGetValue(infix[i], out (int, int) pair)) RandomizerMod.Instance.LogWarn("Could not find progression value for: " + infix[i]);
                        postfix.Add(pair);
                    }
                    i++;
                }

                def.processedRoomLogic = postfix;
                _shops[shopName] = def;
            }

            foreach (string transitionName in areaTransitions)
            {
                TransitionDef def = _areaTransitions[transitionName];
                if ((def.oneWay == 2) || def.isolated) continue;
                string[] infix = def.logic;
                List<(int, int)> postfix = new List<(int, int)>();
                i = 0;
                while (i < infix.Length)
                {
                    if (infix[i] == "|") postfix.Add((-1, 0));
                    else if (infix[i] == "+") postfix.Add((-2, 0));
                    else if (infix[i] == "ESSENCECOUNT") postfix.Add((-3, 0));
                    else if (infix[i] == "GRUBCOUNT") postfix.Add((-4, 0));
                    else
                    {
                        if (!progressionBitMask.TryGetValue(infix[i], out (int, int) pair)) RandomizerMod.Instance.LogWarn("Could not find progression value for: " + infix[i]);
                        postfix.Add(pair);
                    }
                    i++;
                }

                def.processedLogic = postfix;
                _areaTransitions[transitionName] = def;
            }
            
            foreach (string transitionName in roomTransitions)
            {
                TransitionDef def = _roomTransitions[transitionName];

                if ((def.oneWay == 2) || def.isolated) continue;
                string[] infix = def.logic;
                List<(int, int)> postfix = new List<(int, int)>();
                i = 0;
                while (i < infix.Length)
                {
                    if (infix[i] == "|") postfix.Add((-1, 0));
                    else if (infix[i] == "+") postfix.Add((-2, 0));
                    else if (infix[i] == "ESSENCECOUNT") postfix.Add((-3, 0));
                    else if (infix[i] == "GRUBCOUNT") postfix.Add((-4, 0));
                    else
                    {
                        if (!progressionBitMask.TryGetValue(infix[i], out (int, int) pair)) RandomizerMod.Instance.LogWarn("Could not find progression value for: " + infix[i]);
                        postfix.Add(pair);
                    }
                    i++;
                }

                def.processedLogic = postfix;
                _roomTransitions[transitionName] = def;
            }
        }

        private static string GetNextOperator(string infix, ref int i)
        {
            int start = i;

            if (infix[i] == '(' || infix[i] == ')' || infix[i] == '+' || infix[i] == '|')
            {
                i++;
                return infix[i - 1].ToString();
            }

            while (i < infix.Length && infix[i] != '(' && infix[i] != ')' && infix[i] != '+' && infix[i] != '|')
            {
                i++;
            }

            return infix.Substring(start, i - start).Trim(' ');
        }

        private static void ParseAdditiveItemXML(XmlNodeList nodes)
        {
            foreach (XmlNode setNode in nodes)
            {
                XmlAttribute nameAttr = setNode.Attributes?["name"];
                if (nameAttr == null)
                {
                    LogWarn("Node in items.xml has no name attribute");
                    continue;
                }

                string[] additiveSet = new string[setNode.ChildNodes.Count];
                for (int i = 0; i < additiveSet.Length; i++)
                {
                    additiveSet[i] = setNode.ChildNodes[i].InnerText;
                }

                LogDebug($"Parsed XML for item set \"{nameAttr.InnerText}\"");
                _additiveItems.Add(nameAttr.InnerText, additiveSet);
                _macros.Add(nameAttr.InnerText, ShuntingYard(string.Join(" | ", additiveSet)));
            }
        }

        private static void ParseMacroXML(XmlNodeList nodes)
        {
            foreach (XmlNode macroNode in nodes)
            {
                XmlAttribute nameAttr = macroNode.Attributes?["name"];
                if (nameAttr == null)
                {
                    LogWarn("Node in items.xml has no name attribute");
                    continue;
                }

                LogDebug($"Parsed XML for macro \"{nameAttr.InnerText}\"");
                _macros.Add(nameAttr.InnerText, ShuntingYard(macroNode.InnerText));
            }
        }

        private static void ParseTransitionXML(XmlNodeList nodes, bool room = false)
        {
            Dictionary<string, FieldInfo> transitionFields = new Dictionary<string, FieldInfo>();
            typeof(TransitionDef).GetFields().ToList().ForEach(f => transitionFields.Add(f.Name, f));

            foreach (XmlNode transitionNode in nodes)
            {
                XmlAttribute nameAttr = transitionNode.Attributes?["name"];
                if (nameAttr == null)
                {
                    LogWarn("Node in items.xml has no name attribute");
                    continue;
                }
                
                // Setting as object prevents boxing in FieldInfo.SetValue calls
                object def = new TransitionDef();

                foreach (XmlNode fieldNode in transitionNode.ChildNodes)
                {
                    if (!transitionFields.TryGetValue(fieldNode.Name, out FieldInfo field))
                    {
                        LogWarn(
                            $"Xml node \"{fieldNode.Name}\" does not map to a field in struct TransitionDef");
                        continue;
                    }

                    if (field.FieldType == typeof(string))
                    {
                        field.SetValue(def, fieldNode.InnerText);
                    }
                    else if (field.FieldType == typeof(string[]))
                    {
                        if (field.Name.EndsWith("ogic"))
                        {
                            field.SetValue(def, ShuntingYard(fieldNode.InnerText));
                        }
                        else
                        {
                            LogWarn(
                                "string[] field not ending in \"ogic\" found in TransitionDef, ignoring");
                        }
                    }
                    else if (field.FieldType == typeof(bool))
                    {
                        if (bool.TryParse(fieldNode.InnerText, out bool xmlBool))
                        {
                            field.SetValue(def, xmlBool);
                        }
                        else
                        {
                            LogWarn($"Could not parse \"{fieldNode.InnerText}\" to bool");
                        }
                    }
                    else if (field.FieldType == typeof(ItemType))
                    {
                        if (fieldNode.InnerText.TryToEnum(out ItemType type))
                        {
                            field.SetValue(def, type);
                        }
                        else
                        {
                            LogWarn($"Could not parse \"{fieldNode.InnerText}\" to ItemType");
                        }
                    }
                    else if (field.FieldType == typeof(int))
                    {
                        if (int.TryParse(fieldNode.InnerText, out int xmlInt))
                        {
                            field.SetValue(def, xmlInt);
                        }
                        else
                        {
                            LogWarn($"Could not parse \"{fieldNode.InnerText}\" to int");
                        }
                    }
                    else
                    {
                        LogWarn("Unsupported type in TransitionDef: " + field.FieldType.Name);
                    }
                }

                LogDebug($"Parsed XML for transition \"{nameAttr.InnerText}\"");
                if (!room) _areaTransitions.Add(nameAttr.InnerText, (TransitionDef)def);
                else _roomTransitions.Add(nameAttr.InnerText, (TransitionDef)def);
            }
        }

        private static void ParseItemXML(XmlNodeList nodes)
        {
            Dictionary<string, FieldInfo> reqFields = new Dictionary<string, FieldInfo>();
            typeof(ReqDef).GetFields().ToList().ForEach(f => reqFields.Add(f.Name, f));

            foreach (XmlNode itemNode in nodes)
            {
                XmlAttribute nameAttr = itemNode.Attributes?["name"];
                if (nameAttr == null)
                {
                    LogWarn("Node in items.xml has no name attribute");
                    continue;
                }

                // Setting as object prevents boxing in FieldInfo.SetValue calls
                object def = new ReqDef();

                foreach (XmlNode fieldNode in itemNode.ChildNodes)
                {
                    if (!reqFields.TryGetValue(fieldNode.Name, out FieldInfo field))
                    {
                        LogWarn(
                            $"Xml node \"{fieldNode.Name}\" does not map to a field in struct ReqDef");
                        continue;
                    }

                    if (field.FieldType == typeof(string))
                    {
                        field.SetValue(def, fieldNode.InnerText);
                    }
                    else if (field.FieldType == typeof(string[]))
                    {
                        if (field.Name.EndsWith("ogic"))
                        {
                            field.SetValue(def, ShuntingYard(fieldNode.InnerText));
                        }
                        else
                        {
                            LogWarn(
                                "string[] field not ending in \"ogic\" found in ReqDef, ignoring");
                        }
                    }
                    else if (field.FieldType == typeof(bool))
                    {
                        if (bool.TryParse(fieldNode.InnerText, out bool xmlBool))
                        {
                            field.SetValue(def, xmlBool);
                        }
                        else
                        {
                            LogWarn($"Could not parse \"{fieldNode.InnerText}\" to bool");
                        }
                    }
                    else if (field.FieldType == typeof(ItemType))
                    {
                        if (fieldNode.InnerText.TryToEnum(out ItemType type))
                        {
                            field.SetValue(def, type);
                        }
                        else
                        {
                            LogWarn($"Could not parse \"{fieldNode.InnerText}\" to ItemType");
                        }
                    }
                    else if (field.FieldType == typeof(int))
                    {
                        if (int.TryParse(fieldNode.InnerText, out int xmlInt))
                        {
                            field.SetValue(def, xmlInt);
                        }
                        else
                        {
                            LogWarn($"Could not parse \"{fieldNode.InnerText}\" to int");
                        }
                    }
                    else
                    {
                        LogWarn("Unsupported type in ReqDef: " + field.FieldType.Name);
                    }
                }

                LogDebug($"Parsed XML for item \"{nameAttr.InnerText}\"");
                _items.Add(nameAttr.InnerText, (ReqDef) def);
            }
        }

        private static void ParseShopXML(XmlNodeList nodes)
        {
            Dictionary<string, FieldInfo> shopFields = new Dictionary<string, FieldInfo>();
            typeof(ShopDef).GetFields().ToList().ForEach(f => shopFields.Add(f.Name, f));

            foreach (XmlNode shopNode in nodes)
            {
                XmlAttribute nameAttr = shopNode.Attributes?["name"];
                if (nameAttr == null)
                {
                    LogWarn("Node in items.xml has no name attribute");
                    continue;
                }

                // Setting as object prevents boxing in FieldInfo.SetValue calls
                object def = new ShopDef();

                foreach (XmlNode fieldNode in shopNode.ChildNodes)
                {
                    if (!shopFields.TryGetValue(fieldNode.Name, out FieldInfo field))
                    {
                        LogWarn(
                            $"Xml node \"{fieldNode.Name}\" does not map to a field in struct ReqDef");
                        continue;
                    }

                    if (field.FieldType == typeof(string))
                    {
                        field.SetValue(def, fieldNode.InnerText);
                    }
                    else if (field.FieldType == typeof(string[]))
                    {
                        if (field.Name.EndsWith("ogic"))
                        {
                            field.SetValue(def, ShuntingYard(fieldNode.InnerText));
                        }
                        else
                        {
                            LogWarn(
                                "string[] field not ending in \"ogic\" found in ShopDef, ignoring");
                        }
                    }
                    else if (field.FieldType == typeof(bool))
                    {
                        if (bool.TryParse(fieldNode.InnerText, out bool xmlBool))
                        {
                            field.SetValue(def, xmlBool);
                        }
                        else
                        {
                            LogWarn($"Could not parse \"{fieldNode.InnerText}\" to bool");
                        }
                    }
                    else
                    {
                        LogWarn("Unsupported type in ShopDef: " + field.FieldType.Name);
                    }
                }

                LogDebug($"Parsed XML for shop \"{nameAttr.InnerText}\"");
                _shops.Add(nameAttr.InnerText, (ShopDef) def);
            }
        }
    }
}
