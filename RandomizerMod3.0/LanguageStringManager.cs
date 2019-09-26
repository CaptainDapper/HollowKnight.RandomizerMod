using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Xml;
using System.Linq;
using Language;
using static RandomizerMod.LogHelper;
using RandomizerMod.Randomization;

namespace RandomizerMod
{
    internal static class LanguageStringManager
    {
        private static readonly Dictionary<string, Dictionary<string, string>> LanguageStrings =
            new Dictionary<string, Dictionary<string, string>>();

        public static void LoadLanguageXML(Stream xmlStream)
        {
            // Load XmlDocument from resource stream
            XmlDocument xml = new XmlDocument();
            xml.Load(xmlStream);
            xmlStream.Dispose();

            XmlNodeList nodes = xml.SelectNodes("Language/entry");
            if (nodes == null)
            {
                LogWarn("Malformatted language xml, no nodes that match Language/entry");
                return;
            }

            foreach (XmlNode node in nodes)
            {
                string sheet = node.Attributes?["sheet"]?.Value;
                string key = node.Attributes?["key"]?.Value;

                if (sheet == null || key == null)
                {
                    LogWarn("Malformatted language xml, missing sheet or key on node");
                    continue;
                }

                SetString(sheet, key, node.InnerText.Replace("\\n", "\n"));
            }

            Log("Language xml processed");
        }

        public static void SetString(string sheetName, string key, string text)
        {
            if (string.IsNullOrEmpty(sheetName) || string.IsNullOrEmpty(key) || text == null)
            {
                return;
            }

            if (!LanguageStrings.TryGetValue(sheetName, out Dictionary<string, string> sheet))
            {
                sheet = new Dictionary<string, string>();
                LanguageStrings.Add(sheetName, sheet);
            }

            sheet[key] = text;
        }

        public static void ResetString(string sheetName, string key)
        {
            if (string.IsNullOrEmpty(sheetName) || string.IsNullOrEmpty(key))
            {
                return;
            }

            if (LanguageStrings.TryGetValue(sheetName, out Dictionary<string, string> sheet) && sheet.ContainsKey(key))
            {
                sheet.Remove(key);
            }
        }

        public static string GetLanguageString(string key, string sheetTitle)
        {
            if (sheetTitle == "Jiji" && key == "HIVE" && RandomizerMod.Instance.Settings.Jiji)
            {
                return NextJijiHint();
            }
            if (sheetTitle == "Quirrel" && RandomizerMod.Instance.Settings.Quirrel && RandomizerMod.Instance.Settings.QuirrerHintCounter < 3 &&
                new List<string> { "QUIRREL_MEET_TEMPLE_C", "QUIRREL_GREENPATH_1", "QUIRREL_QUEENSTATION_01", "QUIRREL_MANTIS_01", "QUIRREL_RUINS_1", "QUIRREL_SPA", "QUIRREL_MINES_2", "QUIRREL_FOGCANYON_A", "QUIRREL_EPILOGUE_A" }.Contains(key))
            {
                return GetQuirrelHint(key, sheetTitle);
            }
            if (string.IsNullOrEmpty(key) || string.IsNullOrEmpty(sheetTitle))
            {
                return string.Empty;
            }

            if (LanguageStrings.ContainsKey(sheetTitle) && LanguageStrings[sheetTitle].ContainsKey(key))
            {
                return LanguageStrings[sheetTitle][key];
            }

            return Language.Language.GetInternal(key, sheetTitle);
        }
        public static string NextJijiHint()
        {
            int hintMax = RandomizerMod.Instance.Settings.Hints.Length;
            string hintItemName = string.Empty;
            string hintItemSpot = string.Empty;
            string hint = string.Empty;
            while (RandomizerMod.Instance.Settings.JijiHintCounter < hintMax - 1)
            {
                string item = RandomizerMod.Instance.Settings.Hints[RandomizerMod.Instance.Settings.JijiHintCounter].Item1;
                string location = RandomizerMod.Instance.Settings.Hints[RandomizerMod.Instance.Settings.JijiHintCounter].Item2;
                hint = CreateJijiHint(item, location);
                RandoLogger.LogHintToTracker(hint);

                if (Actions.RandomizerAction.AdditiveBoolNames.TryGetValue(item, out string additiveBoolName))
                {
                    if (!RandomizerMod.Instance.Settings.GetBool(false, additiveBoolName))
                    {
                        hintItemName = item;
                        hintItemSpot = location;
                        RandomizerMod.Instance.Settings.JijiHintCounter++;
                        break;
                    }
                }
                else if (!PlayerData.instance.GetBool(LogicManager.GetItemDef(item).boolName))
                {
                    hintItemName = item;
                    hintItemSpot = location;
                    RandomizerMod.Instance.Settings.JijiHintCounter++;
                    break;
                }
                RandomizerMod.Instance.Settings.JijiHintCounter++;
            }
            if (hintItemName == string.Empty || hintItemSpot == string.Empty || hint == string.Empty) return "Oh! I guess I couldn't find any items you left behind. Since you're doing so well, though, I think I'll be keeping this meal.";

            return hint;
        }

        public static string CreateJijiHint(string hintItemName, string hintItemSpot)
        {
            ReqDef hintItem = LogicManager.GetItemDef(hintItemName);
            ReqDef hintSpot = LogicManager.GetItemDef(hintItemSpot);
            bool good = false;
            int useful = 0;
            foreach ((string, string) p in RandomizerMod.Instance.Settings.Hints)
            {
                ReqDef item = LogicManager.GetItemDef(p.Item1);
                ReqDef location = LogicManager.GetItemDef(p.Item2);
                if (location.areaName == hintSpot.areaName)
                {
                    if (item.isGoodItem) good = true;
                    if (item.progression) useful++;
                }
            }
            string secondMessage;
            if (good) secondMessage = " The items there... just thinking about them is getting me excited.";
            else if (useful >= 2) secondMessage = " There are a few useful things waiting for you there.";
            else if (useful == 1) secondMessage = " I can't say whether it would be worth your time though.";
            else secondMessage = " Although it does seem awfully out of the way...";

            hintItemName = GetLanguageString(hintItem.nameKey, "UI");
            string hintItemArea = hintSpot.areaName;
            string firstMessage;

            if (hintItemArea == "Greenpath") firstMessage = "Yes, I can see the items you've left behind. " + hintItemName + " in a lush, green land.";
            else if (hintItemArea == "Fungal_Wastes") firstMessage = "Yes, I can see the items you've left behind. " + hintItemName + " nestled amongst strange fungus and bubbling lakes.";
            else if (hintItemArea == "Crystal_Peak") firstMessage = "Yes, I can see the items you've left behind. " + hintItemName + " almost hidden by the glow of shimmering crystals around it.";
            else if (hintItemArea == "Abyss") firstMessage = "Yes, I can see the items you've left behind. Only faintly though... " + hintItemName + " deep below the world, surrounded by darkness. Almost a part of it...";
            else if (hintItemArea == "Royal_Waterways") firstMessage = "Yes, I can see the items you've left behind. " + hintItemName + " surrounded by pipes and running water. It can not be washed away, though...";
            else if (hintItemArea == "Resting_Grounds") firstMessage = "Yes, I can see the items you've left behind. " + hintItemName + " in a holy place of repose.";
            else if (hintItemArea == "Ancestral_Mound") firstMessage = "Yes, I can see the items you've left behind. " + hintItemName + " in an ancestral mound... a place of strange worships.";
            else if (hintItemArea == "City_of_Tears") firstMessage = "Yes, I can see the items you've left behind. " + hintItemName + " in the heart of the kingdom's capital. Rain can not wash it away, though...";
            else if (hintItemArea == "Fog_Canyon") firstMessage = "Yes, I can see the items you've left behind. " + hintItemName + " lost in the fog of a strange land.";
            else if (hintItemArea == "Howling_Cliffs") firstMessage = "Yes, I can see the items you've left behind. " + hintItemName + " high above us, surrounded by howling winds.";
            else if (hintItemArea == "Kingdoms_Edge") firstMessage = "Yes, I can see the items you've left behind. " + hintItemName + " far away at the very edge of the world.";
            else if (hintItemArea == "Forgotten_Crossroads") firstMessage = "Yes, I can see the items you've left behind. " + hintItemName + " just below us, lost amongst the kingdom's twisting roads and highways.";
            else if (hintItemArea == "Kings_Pass") firstMessage = "Yes, I can see the items you've left behind. " + hintItemName + " nearby, right at the entrance to this kingdom.";
            else if (hintItemArea == "Deepnest") firstMessage = "Yes, I can see the items you've left behind. " + hintItemName + ", barely visible in the tunnels of a nest deep below this kingdom.";
            else if (hintItemArea == "Dirtmouth") firstMessage = "Yes, I can see the items you've left behind. " + hintItemName + " just outside, in a town quietly fading away.";
            else if (hintItemArea == "Hive") firstMessage = "Yes, I can see the items you've left behind. " + hintItemName + " surrounded by golden light, in a hive far away from here.";
            else if (hintItemArea == "Queens_Gardens") firstMessage = "Yes, I can see the items you've left behind. " + hintItemName + ", marring a garden's beauty.";
            else if (hintItemArea == "Colosseum") firstMessage = "Yes, I can see the items you've left behind. " + hintItemName + " surrounded by warriors and fools.";
            else if (hintItemArea == "Ancient_Basin") firstMessage = "Yes, I can see the items you've left behind. " + hintItemName + ", lying just outside the ruins of the king's palace.";
            else firstMessage = hintItemName + " is in " + hintItemArea + ".";

            return firstMessage + secondMessage;
        }

        public static string GetQuirrelHint(string key, string sheetTitle)
        {
            RandomizerMod.Instance.Settings.QuirrerHintCounter++;
            string hint = string.Empty;
            List<string> areas = new List<string>();
            List<string> locations = new List<string>();

            switch (key)
            {
                case "QUIRREL_MEET_TEMPLE_C":
                    locations = RandomizerMod.Instance.Settings.ItemPlacements.Where(pair => new List<string> { "Dream_Nail", "Dream_Gate", "Awoken_Dream_Nail" }.Contains(pair.Item1)).Select(pair => pair.Item2).ToList();
                    foreach (string location in locations)
                    {
                        if (LogicManager.ShopNames.Contains(location))
                        {
                            areas.Add("a shop");
                        }
                        else
                        {
                            areas.Add(LogicManager.GetItemDef(location).areaName.Replace('_',' '));
                        }
                    }
                    hint = "A great stone egg, lying in the corpse of an ancient kingdom. And this egg...Is it warm? It certainly gives off a unique air." +
                        "<page>" + "Can it be opened? There are strange marks all over it..." + 
                        "<page>" + "Perhaps...I've heard rumors of artifacts in this kingdom which can open the mind and recover even the darkest of secrets." +
                        "<page>" + "The first was said to be stowed away in " + areas[0] + "." +
                        "<page>" + "The second kept safe in " + areas[1] + "." +
                        "<page>" + "And the last... somewhere in " + areas[2] + "." +
                        "<page>" + "Perhaps with one of those you might solve the riddle of this temple...";
                    break;
                case "QUIRREL_GREENPATH_1":
                    locations.Add(RandomizerMod.Instance.Settings.ItemPlacements.First(pair => pair.Item1 == "Mantis_Claw" || pair.Item1 == "Monarch_Wings").Item2);
                    foreach (string location in locations)
                    {
                        if (LogicManager.ShopNames.Contains(location))
                        {
                            areas.Add("a shop");
                        }
                        else
                        {
                            areas.Add(LogicManager.GetItemDef(location).areaName.Replace('_', ' '));
                        }
                    }
                    hint = "Oh, hello there! Seems we both tread far from the path." +
                        "<page>" + "I can hardly believe those dusty old highways led to such a lush and lively place!" +
                        "<page>" + "This building suggests some form of worship, though its idol has clearly been long forgotten. Doubles equally well for a moment's respite."
                        +"<page>" + "Oh, I picked up a good tip for you too. If you're looking to get to higher places, try checking " + areas[0] + "."
                        +"<page>" + "You should be able to find something useful there.";
                    break;
                case "QUIRREL_QUEENSTATION_01":
                    locations.Add(RandomizerMod.Instance.Settings.ItemPlacements.First(pair => pair.Item1 == "Mothwing_Cloak" || pair.Item1 == "Shade_Cloak").Item2);
                    foreach (string location in locations)
                    {
                        if (LogicManager.ShopNames.Contains(location))
                        {
                            areas.Add("a shop");
                        }
                        else
                        {
                            areas.Add(LogicManager.GetItemDef(location).areaName.Replace('_', ' '));
                        }
                    }
                    hint = "Isn't this something. I'd not expected to discover so huge a Stag Station after that foggy descent." +
                        "<page>" + "The bugs of Hallownest must've been an impressive lot, building such grand structures so far into these wilds." +
                        "<page>" + "Seems the dangerous creatures about haven't yet made their way in here. It's the perfect place for a quick rest." +
                        "<page>" + "From this point on, you're going to need to move a bit faster. I've heard tell of a special cloak that confers such a power." +
                        "<page>" + "I couldn't find it myself, but you might. Try looking in " + areas[0] + ".";
                    break;
                case "QUIRREL_MANTIS_01":
                    locations.Add(RandomizerMod.Instance.Settings.ItemPlacements.First(pair => pair.Item1 == "Lumafly_Lantern").Item2);
                    foreach (string location in locations)
                    {
                        if (LogicManager.ShopNames.Contains(location))
                        {
                            areas.Add("a shop");
                        }
                        else
                        {
                            areas.Add(LogicManager.GetItemDef(location).areaName.Replace('_', ' '));
                        }
                    }
                    hint = "Hello again! I suppose you've already met with the tribe of this village, hmm? They seem a little distrustful of strangers... to put it lightly."+
                        "<page>"+"They're not brutes though, no. The sickness in the air that clouds the mind of lesser beasts... they resist it. They retain their intellect and their honour, though also their lethal traditions."+
                        "<page>"+"I've some words of advice, my friend. If you plan to head down below, past the lords of this village, you'll find it far more difficult without a tool to light your way."+
                        "<page>"+"I saw an extra one while I was passing through " + areas[0] + ".";
                    break;
                case "QUIRREL_RUINS_1":
                    locations = RandomizerMod.Instance.Settings.ItemPlacements.Where(pair => new List<string> { "Desolate_Dive", "Descending_Dark" }.Contains(pair.Item1)).Select(pair => pair.Item2).ToList();
                    foreach (string location in locations)
                    {
                        if (LogicManager.ShopNames.Contains(location))
                        {
                            areas.Add("a shop");
                        }
                        else
                        {
                            areas.Add(LogicManager.GetItemDef(location).areaName.Replace('_', ' '));
                        }
                    }
                    hint = "The capital lies before us my friend. What a sombre place it seems and one that holds the answers to many a mystery."+
                        "<page>"+"I too have felt the pull of this place, though now I sit before it I find myself hesitant to descend."+
                        "<page>"+"Is it fear I wonder, or something else that holds me back?"+
                        "<page>"+"The stories of the terrible research done in the sanctum before us are too terrible to repeat. Supposedly they developed a spell to smash through the ground, but the art was lost when the city fell."+
                        "<page>"+"If you need that kind of power, try searching " + areas[0] + ". That seems like the most likely place to find it. You might also find it in " + areas[1] + " but that will take some digging.";
                    break;
                case "QUIRREL_SPA":
                    locations.Add(RandomizerMod.Instance.Settings.ItemPlacements.Last(pair => pair.Item1 == "Mothwing_Cloak" || pair.Item1 == "Shade_Cloak").Item2);
                    foreach (string location in locations)
                    {
                        if (LogicManager.ShopNames.Contains(location))
                        {
                            areas.Add("a shop");
                        }
                        else
                        {
                            areas.Add(LogicManager.GetItemDef(location).areaName.Replace('_', ' '));
                        }
                    }
                    hint = "Hello, hello! What a thrill this is, to find such warm comfort amidst the den of beasts."+
                        "<page>"+"This is a ferocious place no doubt. Supposedly, there's a village deep in the warren. Its inhabitants never accepted Hallownest's King."+
                        "<page>"+"By the way, have you noticed those strange black barriers strewn about?"+
                        "<page>"+"Supposedly the tool to get past them was sealed away in " + areas[0] +", but who can say for sure?";
                    break;
                case "QUIRREL_MINES_2":
                    locations.Add(RandomizerMod.Instance.Settings.ItemPlacements.Last(pair => pair.Item1 == "Crystal_Heart").Item2);
                    foreach (string location in locations)
                    {
                        if (LogicManager.ShopNames.Contains(location))
                        {
                            areas.Add("a shop");
                        }
                        else
                        {
                            areas.Add(LogicManager.GetItemDef(location).areaName.Replace('_', ' '));
                        }
                    }
                    hint = "Did it sadden you to see those miners below, still labouring at their endless task?" +
                        "<page>" + "Even overcome, strong purpose has been imprinted upon their husks." +
                        "<page>" + "The crystal ore is said to contain a sort of energy, not as powerful as the soul the city dwellers harnessed but far less lethal." +
                        "<page>" + "Now, the cores they use to power their tunneling machines must've had incredible power, but not many remain. The only one I know of was moved to " + areas[0] + ".";
                    break;
                case "QUIRREL_FOGCANYON_A":
                    locations.Add(RandomizerMod.Instance.Settings.ItemPlacements.Last(pair => pair.Item1 == "Isma's_Tear").Item2);
                    foreach (string location in locations)
                    {
                        if (LogicManager.ShopNames.Contains(location))
                        {
                            areas.Add("a shop");
                        }
                        else
                        {
                            areas.Add(LogicManager.GetItemDef(location).areaName.Replace('_', ' '));
                        }
                    }
                    hint = "Doesn't this kingdom just abound with surprises? A building atop an acid lake." +
                        "<page>" + "Speaking of, I'm impressed you were able to make it this far. The acid is quite threatening to our kind, without the proper protection." +
                        "<page>" + "If you still need that ability, I'd suggest searching around " + areas[0] +
                        "<page>"+"Despite the sight, I can't help but feel... familiarity? Something stirs in my mind, though I can't yet tell what.."+
                        "<page>"+"I'd thought it my lust for discovery that led me here but now there seems something else."+
                        "<page>"+"This building beckons me.";
                    break;
                case "QUIRREL_EPILOGUE_A":
                    locations.Add(RandomizerMod.Instance.Settings.ItemPlacements.First(pair => pair.Item1 == "Mantis_Claw" || pair.Item1 == "Monarch_Wings").Item2);
                    foreach (string location in locations)
                    {
                        if (LogicManager.ShopNames.Contains(location))
                        {
                            areas.Add("a shop");
                        }
                        else
                        {
                            areas.Add(LogicManager.GetItemDef(location).areaName.Replace('_', ' '));
                        }
                    }
                    hint = "Again we meet, my short friend. Here at last, I feel at peace."+
                        "<page>"+"I suppose your quest is still far from over though. If you still seek greater heights, go to " + areas[0] + ". You should find it there, I hope..."+
                        "<page>"+"Twice I've seen this world and though my service may have stripped the first experience from me, I'm thankful I could witness its beauty again."+
                        "<page>"+"Hallownest is a vast and wondrous thing, but with as many wonders as it holds, I've seen none quite so intriguing as you."+
                        "<page>"+"Ha. My flattery returns only silent stoicism. I like that."+
                        "<page>"+"I like that very much.";
                    break;
                default:
                    LogWarn("Unknown key passed to GetQuirrelHint");
                    break;
            }
            if (hint == string.Empty)
                return Language.Language.GetInternal(key, sheetTitle);

            RandoLogger.LogHintToTracker(hint, jiji: false, quirrel: true);
            return hint;
        }
    }
}
