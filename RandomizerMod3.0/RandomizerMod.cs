using System;
using Random = System.Random;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using Modding;
using RandomizerMod.Actions;
using RandomizerMod.Randomization;
using SeanprCore;
using UnityEngine;
using UnityEngine.SceneManagement;
using HutongGames.PlayMaker;
using HutongGames.PlayMaker.Actions;
using RandomizerMod.Extensions;
using RandomizerMod.FsmStateActions;

using Object = UnityEngine.Object;

namespace RandomizerMod
{
    public class RandomizerMod : Mod
    {
        private static Dictionary<string, Sprite> _sprites;
        private static Dictionary<string, string> _secondaryBools;

        private static Thread _logicParseThread;

        public static RandomizerMod Instance { get; private set; }

        public SaveSettings Settings { get; set; } = new SaveSettings();

        public override ModSettings SaveSettings
        {
            get => Settings = Settings ?? new SaveSettings();
            set => Settings = value is SaveSettings saveSettings ? saveSettings : Settings;
        }

        public override void Initialize(Dictionary<string, Dictionary<string, GameObject>> preloaded)
        {
            if (Instance != null)
            {
                LogWarn("Attempting to make multiple instances of mod, ignoring");
                return;
            }

            // Set instance for outside use
            Instance = this;

            // Make sure the play mode screen is always unlocked
            Ref.GM.EnablePermadeathMode();

            // Unlock godseeker too because idk why not
            Ref.GM.SetStatusRecordInt("RecBossRushMode", 1);

            // Load embedded resources
            _sprites = ResourceHelper.GetSprites("RandomizerMod.Resources.");

            Assembly randoDLL = GetType().Assembly;
            try
            {
                LanguageStringManager.LoadLanguageXML(
                    randoDLL.GetManifestResourceStream("RandomizerMod.Resources.language.xml"));
            }
            catch (Exception e)
            {
                LogError("Could not process language xml:\n" + e);
            }

            _logicParseThread = new Thread(() =>
            LogicManager.ParseXML(randoDLL));
            _logicParseThread.Start();

            // Add hooks
            UnityEngine.SceneManagement.SceneManager.activeSceneChanged += HandleSceneChanges;
            ModHooks.Instance.LanguageGetHook += LanguageStringManager.GetLanguageString;
            ModHooks.Instance.GetPlayerIntHook += IntOverride;
            ModHooks.Instance.GetPlayerBoolHook += BoolGetOverride;
            ModHooks.Instance.SetPlayerBoolHook += BoolSetOverride;
            On.PlayMakerFSM.OnEnable += FixVoidHeart;
            On.GameManager.BeginSceneTransition += EditTransition;

            RandomizerAction.Hook();
            BenchHandler.Hook();
            MiscSceneChanges.Hook();

            // Setup preloaded objects
            ObjectCache.GetPrefabs(preloaded[SceneNames.Tutorial_01]);
            ObjectCache.GetPrefabBench(preloaded[SceneNames.Crossroads_30]);

            // Some items have two bools for no reason, gotta deal with that
            _secondaryBools = new Dictionary<string, string>
            {
                {nameof(PlayerData.hasDash), nameof(PlayerData.canDash)},
                {nameof(PlayerData.hasShadowDash), nameof(PlayerData.canShadowDash)},
                {nameof(PlayerData.hasSuperDash), nameof(PlayerData.canSuperDash)},
                {nameof(PlayerData.hasWalljump), nameof(PlayerData.canWallJump)},
                {nameof(PlayerData.gotCharm_23), nameof(PlayerData.fragileHealth_unbreakable)},
                {nameof(PlayerData.gotCharm_24), nameof(PlayerData.fragileGreed_unbreakable)},
                {nameof(PlayerData.gotCharm_25), nameof(PlayerData.fragileStrength_unbreakable)}
            };

            MenuChanger.EditUI();
        }

        public override List<(string, string)> GetPreloadNames()
        {
            return new List<(string, string)>
            {
                (SceneNames.Tutorial_01, "_Props/Chest/Item/Shiny Item (1)"),
                (SceneNames.Tutorial_01, "_Enemies/Crawler 1"),
                (SceneNames.Tutorial_01, "_Props/Cave Spikes (1)"),
                (SceneNames.Crossroads_30, "RestBench")
            };
        }

        public static Sprite GetSprite(string name)
        {
            if (_sprites != null && _sprites.TryGetValue(name, out Sprite sprite))
            {
                return sprite;
            }
            return null;
        }

        public static bool LoadComplete()
        {
            return _logicParseThread == null || !_logicParseThread.IsAlive;
        }

        public void StartNewGame()
        {
            // Charm tutorial popup is annoying, get rid of it
            Ref.PD.hasCharm = true;

            //Lantern start for easy mode
            if (!RandomizerMod.Instance.Settings.DarkRooms && !RandomizerMod.Instance.Settings.RandomizeKeys)
            {
                PlayerData.instance.hasLantern = true;
            }

            if (RandomizerMod.Instance.Settings.RandomizeRooms)
            {
                PlayerData.instance.hasDreamNail = true;
                PlayerData.instance.hasDreamGate = true;
                PlayerData.instance.dreamOrbs = 10;
            }

            if (RandomizerMod.Instance.Settings.EarlyGeo)
            {
                PlayerData.instance.AddGeo(300);
            }

            // Fast boss intros
            Ref.PD.unchainedHollowKnight = true;
            Ref.PD.encounteredMimicSpider = true;
            Ref.PD.infectedKnightEncountered = true;
            Ref.PD.mageLordEncountered = true;
            Ref.PD.mageLordEncountered_2 = true;

            if (!Settings.Randomizer)
            {
                return;
            }

            if (!LoadComplete())
            {
                _logicParseThread.Join();
            }

            RandoLogger.InitializeTracker();
            RandoLogger.InitializeSpoiler();

            try
            {
                Randomizer.Randomize();

                RandoLogger.UpdateHelperLog();
            }
            catch (Exception e)
            {
                LogError("Error in randomization:\n" + e);
            }
        }

        public override string GetVersion()
        {
            string ver = "3.01";
            int minAPI = 51;

            bool apiTooLow = Convert.ToInt32(ModHooks.Instance.ModVersion.Split('-')[1]) < minAPI;
            if (apiTooLow)
            {
                return ver + " (Update API)";
            }

            return ver;
        }

        private void UpdateCharmNotches(PlayerData pd)
        {
            // Update charm notches
            if (Settings.CharmNotch)
            {
                if (pd == null)
                {
                    return;
                }

                pd.CountCharms();
                int charms = pd.charmsOwned;
                int notches = pd.charmSlots;

                if (!pd.salubraNotch1 && charms >= 5)
                {
                    pd.SetBool(nameof(PlayerData.salubraNotch1), true);
                    notches++;
                }

                if (!pd.salubraNotch2 && charms >= 10)
                {
                    pd.SetBool(nameof(PlayerData.salubraNotch2), true);
                    notches++;
                }

                if (!pd.salubraNotch3 && charms >= 18)
                {
                    pd.SetBool(nameof(PlayerData.salubraNotch3), true);
                    notches++;
                }

                if (!pd.salubraNotch4 && charms >= 25)
                {
                    pd.SetBool(nameof(PlayerData.salubraNotch4), true);
                    notches++;
                }

                pd.SetInt(nameof(PlayerData.charmSlots), notches);
                Ref.GM.RefreshOvercharm();
            }
        }

        private bool BoolGetOverride(string boolName)
        {
            // Fake spell bools
            if (boolName == "hasVengefulSpirit")
            {
                return Ref.PD.fireballLevel > 0;
            }

            if (boolName == "hasShadeSoul")
            {
                return Ref.PD.fireballLevel > 1;
            }

            if (boolName == "hasDesolateDive")
            {
                return Ref.PD.quakeLevel > 0;
            }

            if (boolName == "hasDescendingDark")
            {
                return Ref.PD.quakeLevel > 1;
            }

            if (boolName == "hasHowlingWraiths")
            {
                return Ref.PD.screamLevel > 0;
            }

            if (boolName == "hasAbyssShriek")
            {
                return Ref.PD.screamLevel > 1;
            }

            // This variable is incredibly stubborn, not worth the effort to make it cooperate
            // Just override it completely
            if (boolName == nameof(PlayerData.gotSlyCharm) && Settings.Randomizer)
            {
                return Settings.SlyCharm;
            }

            if (boolName.StartsWith("RandomizerMod."))
            {
                return Settings.GetBool(false, boolName.Substring(14));
            }
            
            if (RandomizerMod.Instance.Settings.RandomizeRooms && (boolName == "troupeInTown" || boolName == "divineInTown")) return false;
            if (boolName == "crossroadsInfected" && RandomizerMod.Instance.Settings.RandomizeRooms
                && new List<string> { SceneNames.Crossroads_03, SceneNames.Crossroads_06, SceneNames.Crossroads_10, SceneNames.Crossroads_19 }.Contains(GameManager.instance.sceneName)) return false;

            return Ref.PD.GetBoolInternal(boolName);
        }

        private void BoolSetOverride(string boolName, bool value)
        {
            PlayerData pd = Ref.PD;
            if (value && Actions.RandomizerAction.ShopItemBoolNames.ContainsValue(boolName))
            {
                (string, string) pair = Actions.RandomizerAction.ShopItemBoolNames.FirstOrDefault(kvp => kvp.Value == boolName).Key;
                Instance.Settings.UpdateObtainedProgression(pair.Item1);
                RandoLogger.LogItemToTracker(pair.Item1, pair.Item2);
                RandoLogger.UpdateHelperLog();
            }

            // It's just way easier if I can treat spells as bools
            if (boolName == "hasVengefulSpirit" && value && pd.fireballLevel <= 0)
            {
                pd.SetInt("fireballLevel", 1);
            }
            else if (boolName == "hasVengefulSpirit" && !value)
            {
                pd.SetInt("fireballLevel", 0);
            }
            else if (boolName == "hasShadeSoul" && value)
            {
                pd.SetInt("fireballLevel", 2);
            }
            else if (boolName == "hasShadeSoul" && !value && pd.fireballLevel >= 2)
            {
                pd.SetInt("fireballLevel", 1);
            }
            else if (boolName == "hasDesolateDive" && value && pd.quakeLevel <= 0)
            {
                pd.SetInt("quakeLevel", 1);
            }
            else if (boolName == "hasDesolateDive" && !value)
            {
                pd.SetInt("quakeLevel", 0);
            }
            else if (boolName == "hasDescendingDark" && value)
            {
                pd.SetInt("quakeLevel", 2);
            }
            else if (boolName == "hasDescendingDark" && !value && pd.quakeLevel >= 2)
            {
                pd.SetInt("quakeLevel", 1);
            }
            else if (boolName == "hasHowlingWraiths" && value && pd.screamLevel <= 0)
            {
                pd.SetInt("screamLevel", 1);
            }
            else if (boolName == "hasHowlingWraiths" && !value)
            {
                pd.SetInt("screamLevel", 0);
            }
            else if (boolName == "hasAbyssShriek" && value)
            {
                pd.SetInt("screamLevel", 2);
            }
            else if (boolName == "hasAbyssShriek" && !value && pd.screamLevel >= 2)
            {
                pd.SetInt("screamLevel", 1);
            }
            else if (boolName.StartsWith("RandomizerMod."))
            {
                boolName = boolName.Substring(14);
                if (boolName.StartsWith("ShopFireball"))
                {
                    pd.IncrementInt("fireballLevel");
                }
                else if (boolName.StartsWith("ShopQuake"))
                {
                    pd.IncrementInt("quakeLevel");
                }
                else if (boolName.StartsWith("ShopScream"))
                {
                    pd.IncrementInt("screamLevel");
                }
                else if (boolName.StartsWith("ShopDash"))
                {
                    pd.SetBool(pd.hasDash ? "hasShadowDash" : "hasDash", true);
                }
                else if (boolName.StartsWith("ShopDreamNail"))
                {
                    if (!pd.hasDreamNail) pd.SetBool(nameof(pd.hasDreamNail), true);
                    else if (!pd.hasDreamGate) pd.SetBool(nameof(pd.hasDreamGate), true);
                    else if (!pd.dreamNailUpgraded) pd.SetBool(nameof(pd.dreamNailUpgraded), true);
                }
                else if (boolName.StartsWith("ShopKingsoul") || boolName.StartsWith("QueenFragment") || boolName.StartsWith("KingFragment") || boolName.StartsWith("VoidHeart"))
                {
                    pd.SetBoolInternal("gotCharm_36", true);
                    if (pd.royalCharmState == 1) pd.SetInt("royalCharmState", 3);
                    else pd.IncrementInt("royalCharmState");
                    if (pd.royalCharmState == 4)
                    {
                        pd.SetBoolInternal("gotShadeCharm", true);
                        pd.SetInt(nameof(pd.charmCost_36), 0);
                    }
                }
                else if (boolName.StartsWith("ShopGeo"))
                {
                    HeroController.instance.AddGeo(int.Parse(boolName.Substring(7)));
                }
                else if (boolName.StartsWith("ShoponeGeo"))
                {
                    HeroController.instance.AddGeo(1);
                }
                else if (boolName.StartsWith("Lurien"))
                {
                    pd.SetBoolInternal("lurienDefeated", true);
                    pd.SetBoolInternal("maskBrokenLurien", true);
                    pd.IncrementInt("guardiansDefeated");
                    if (pd.guardiansDefeated == 1)
                    {
                        pd.SetBoolInternal("hornetFountainEncounter", true);
                        pd.SetBoolInternal("marmOutside", true);
                        pd.SetBoolInternal("crossroadsInfected", true);
                    }
                    if (pd.lurienDefeated && pd.hegemolDefeated && pd.monomonDefeated)
                    {
                        pd.SetBoolInternal("dungDefenderSleeping", true);
                        pd.SetInt("mrMushroomState", 1);
                        pd.IncrementInt("brettaState");
                    }
                }
                else if (boolName.StartsWith("Monomon"))
                {
                    pd.SetBoolInternal("monomonDefeated", true);
                    pd.SetBoolInternal("maskBrokenMonomon", true);
                    pd.IncrementInt("guardiansDefeated");
                    if (pd.guardiansDefeated == 1)
                    {
                        pd.SetBoolInternal("hornetFountainEncounter", true);
                        pd.SetBoolInternal("marmOutside", true);
                        pd.SetBoolInternal("crossroadsInfected", true);
                    }
                    if (pd.lurienDefeated && pd.hegemolDefeated && pd.monomonDefeated)
                    {
                        pd.SetBoolInternal("dungDefenderSleeping", true);
                        pd.SetInt("mrMushroomState", 1);
                        pd.IncrementInt("brettaState");
                    }
                }
                else if (boolName.StartsWith("Herrah"))
                {
                    pd.SetBoolInternal("hegemolDefeated", true);
                    pd.SetBoolInternal("maskBrokenHegemol", true);
                    pd.IncrementInt("guardiansDefeated");
                    if (pd.guardiansDefeated == 1)
                    {
                        pd.SetBoolInternal("hornetFountainEncounter", true);
                        pd.SetBoolInternal("marmOutside", true);
                        pd.SetBoolInternal("crossroadsInfected", true);
                    }
                    if (pd.lurienDefeated && pd.hegemolDefeated && pd.monomonDefeated)
                    {
                        pd.SetBoolInternal("dungDefenderSleeping", true);
                        pd.SetInt("mrMushroomState", 1);
                        pd.IncrementInt("brettaState");
                    }
                }
                else if (boolName.StartsWith("BasinSimpleKey") || boolName.StartsWith("CitySimpleKey") || boolName.StartsWith("SlySimpleKey") || boolName.StartsWith("LurkerSimpleKey"))
                {
                    pd.IncrementInt("simpleKeys");
                }
                else if (boolName.StartsWith("hasWhite") || boolName.StartsWith("hasLove") || boolName.StartsWith("hasSly")) pd.SetBoolInternal(boolName, true);
                else if (boolName.StartsWith("MaskShard"))
                {
                    pd.SetBoolInternal("heartPieceCollected", true);
                    if (PlayerData.instance.heartPieces < 3) GameManager.instance.IncrementPlayerDataInt("heartPieces");
                    else
                    {
                        HeroController.instance.AddToMaxHealth(1);
                        if (PlayerData.instance.maxHealthBase < PlayerData.instance.maxHealthCap) PlayerData.instance.SetIntInternal("heartPieces", 0);
                        PlayMakerFSM.BroadcastEvent("MAX HP UP");
                    }
                }
                else if (boolName.StartsWith("VesselFragment"))
                {
                    pd.SetBoolInternal("vesselFragmentCollected", true);
                    if (PlayerData.instance.vesselFragments < 2) GameManager.instance.IncrementPlayerDataInt("vesselFragments");
                    else
                    {
                        HeroController.instance.AddToMaxMPReserve(33);
                        if (PlayerData.instance.MPReserveMax < PlayerData.instance.MPReserveCap) PlayerData.instance.SetIntInternal("vesselFragments", 0);
                        PlayMakerFSM.BroadcastEvent("NEW SOUL ORB");
                    }
                }
                else if (boolName.StartsWith("PaleOre"))
                {
                    pd.IncrementInt("ore");
                }
                else if (boolName.StartsWith("CharmNotch"))
                {
                    pd.IncrementInt("charmSlots");
                }
                else if (boolName.StartsWith("RancidEgg"))
                {
                    pd.IncrementInt("rancidEggs");
                }
                else if (boolName.StartsWith("WanderersJournal"))
                {
                    pd.IncrementInt("trinket1");
                    pd.SetBoolInternal("foundTrinket1", true);
                }
                else if (boolName.StartsWith("HallownestSeal"))
                {
                    pd.IncrementInt("trinket2");
                    pd.SetBoolInternal("foundTrinket2", true);
                }
                else if (boolName.StartsWith("KingsIdol"))
                {
                    pd.IncrementInt("trinket3");
                    pd.SetBoolInternal("foundTrinket3", true);
                }
                else if (boolName.StartsWith("ArcaneEgg"))
                {
                    pd.IncrementInt("trinket4");
                    pd.SetBoolInternal("foundTrinket4", true);
                }
                else if (boolName.StartsWith("WhisperingRoot"))
                {
                    PlayerData.instance.dreamOrbs += LogicManager.GetItemDef(Instance.Settings.ItemPlacements.First(pair => LogicManager.GetItemDef(pair.Item1).boolName == boolName).Item1).geo;
                }
                Settings.SetBool(value, boolName);
                return;
            }
            // Send the set through to the actual set
            pd.SetBoolInternal(boolName, value);

            // Check if there is a secondary bool for this item
            if (_secondaryBools.TryGetValue(boolName, out string secondaryBoolName))
            {
                pd.SetBool(secondaryBoolName, value);
            }

            if (boolName == nameof(PlayerData.gotCharm_40))
            {
                PlayerData.instance.SetBoolInternal(nameof(PlayerData.nightmareLanternAppeared), true);
                PlayerData.instance.SetBoolInternal(nameof(PlayerData.nightmareLanternLit), true);
                PlayerData.instance.SetBoolInternal(nameof(PlayerData.troupeInTown), true);
                PlayerData.instance.SetBoolInternal(nameof(PlayerData.divineInTown), true);
                PlayerData.instance.SetBoolInternal(nameof(PlayerData.metGrimm), true);
                PlayerData.instance.SetInt(nameof(PlayerData.flamesRequired), 3);
                PlayerData.instance.SetInt(nameof(PlayerData.flamesCollected), 3);
                PlayerData.instance.SetBoolInternal(nameof(PlayerData.killedFlameBearerSmall), true);
                PlayerData.instance.SetBoolInternal(nameof(PlayerData.killedFlameBearerMed), true);
                PlayerData.instance.SetInt(nameof(PlayerData.killsFlameBearerSmall), 3);
                PlayerData.instance.SetInt(nameof(PlayerData.killsFlameBearerMed), 3);
                PlayerData.instance.SetInt(nameof(PlayerData.grimmChildLevel), 2);

                GameManager.instance.sceneData.SaveMyState(new PersistentBoolData
                {
                    sceneName = "Mines_10",
                    id = "Flamebearer Spawn",
                    activated = true,
                    semiPersistent = false
                });
                GameManager.instance.sceneData.SaveMyState(new PersistentBoolData
                {
                    sceneName = "Ruins1_28",
                    id = "Flamebearer Spawn",
                    activated = true,
                    semiPersistent = false
                });
                GameManager.instance.sceneData.SaveMyState(new PersistentBoolData
                {
                    sceneName = "Fungus1_10",
                    id = "Flamebearer Spawn",
                    activated = true,
                    semiPersistent = false
                });
                GameManager.instance.sceneData.SaveMyState(new PersistentBoolData
                {
                    sceneName = "Tutorial_01",
                    id = "Flamebearer Spawn",
                    activated = true,
                    semiPersistent = false
                });
                GameManager.instance.sceneData.SaveMyState(new PersistentBoolData
                {
                    sceneName = "RestingGrounds_06",
                    id = "Flamebearer Spawn",
                    activated = true,
                    semiPersistent = false
                });
                GameManager.instance.sceneData.SaveMyState(new PersistentBoolData
                {
                    sceneName = "Deepnest_East_03",
                    id = "Flamebearer Spawn",
                    activated = true,
                    semiPersistent = false
                });
            }

            if ((boolName == nameof(PlayerData.divineInTown) || boolName == nameof(PlayerData.troupeInTown)) && !value)
            {
                return;
            }

            if (boolName == nameof(PlayerData.hasCyclone) || boolName == nameof(PlayerData.hasUpwardSlash) ||
                boolName == nameof(PlayerData.hasDashSlash))
            {
                // Make nail arts work
                bool hasCyclone = pd.GetBool(nameof(PlayerData.hasCyclone));
                bool hasUpwardSlash = pd.GetBool(nameof(PlayerData.hasUpwardSlash));
                bool hasDashSlash = pd.GetBool(nameof(PlayerData.hasDashSlash));

                pd.SetBool(nameof(PlayerData.hasNailArt), hasCyclone || hasUpwardSlash || hasDashSlash);
                pd.SetBool(nameof(PlayerData.hasAllNailArts), hasCyclone && hasUpwardSlash && hasDashSlash);
            }
            else if (boolName == nameof(PlayerData.hasDreamGate) && value)
            {
                // Make sure the player can actually use dream gate after getting it
                FSMUtility.LocateFSM(Ref.Hero.gameObject, "Dream Nail").FsmVariables
                    .GetFsmBool("Dream Warp Allowed").Value = true;
            }
            else if (boolName == nameof(PlayerData.hasAcidArmour) && value)
            {
                // Gotta update the acid pools after getting this
                PlayMakerFSM.BroadcastEvent("GET ACID ARMOUR");
            }
            else if (boolName.StartsWith("gotCharm_"))
            {
                // Check for Salubra notches if it's a charm
                UpdateCharmNotches(pd);
            }
        }

        private int IntOverride(string intName)
        {
            if (intName == "RandomizerMod.Zero")
            {
                return 0;
            }

            return Ref.PD.GetIntInternal(intName);
        }

        private void FixVoidHeart(On.PlayMakerFSM.orig_OnEnable orig, PlayMakerFSM self)
        {
            orig(self);
            // Normal shade and sibling AI
            if ((self.FsmName == "Control" && self.gameObject.name.StartsWith("Shade Sibling")) || (self.FsmName == "Shade Control" && self.gameObject.name.StartsWith("Hollow Shade")))
            {
                self.FsmVariables.FindFsmBool("Friendly").Value = false;
                self.GetState("Pause").ClearTransitions();
                self.GetState("Pause").AddTransition("FINISHED", "Init");
            }
            // Make Void Heart equippable
            else if (self.FsmName == "UI Charms" && self.gameObject.name == "Charms")
            {
                self.GetState("Equipped?").RemoveTransitionsTo("Black Charm? 2");
                self.GetState("Equipped?").AddTransition("EQUIPPED", "Return Points");
                self.GetState("Set Current Item Num").RemoveTransitionsTo("Black Charm?");
                self.GetState("Set Current Item Num").AddTransition("FINISHED", "Return Points");
            }
        }

        private static void EditTransition(On.GameManager.orig_BeginSceneTransition orig, GameManager self, GameManager.SceneLoadInfo info)
        {
            if (string.IsNullOrEmpty(info.EntryGateName) || string.IsNullOrEmpty(info.SceneName))
            {
                orig(self, info);
                return;
            }
            else if (RandomizerMod.Instance.Settings.RandomizeTransitions)
            {
                TransitionPoint tp = Object.FindObjectsOfType<TransitionPoint>().FirstOrDefault(x => x.entryPoint == info.EntryGateName && x.targetScene == info.SceneName);
                string transitionName = string.Empty;

                if (tp == null)
                {
                    if (self.sceneName == SceneNames.Fungus3_44 && info.EntryGateName == "left1") transitionName = self.sceneName + "[door1]";
                    else if (self.sceneName == SceneNames.Crossroads_02 && info.EntryGateName == "left1") transitionName = self.sceneName + "[door1]";
                    else if (self.sceneName == SceneNames.Crossroads_06 && info.EntryGateName == "left1") transitionName = self.sceneName + "[door1]";
                    else if (self.sceneName == SceneNames.Deepnest_10 && info.EntryGateName == "left1") transitionName = self.sceneName + "[door1]";
                    else if (self.sceneName == SceneNames.Town && info.SceneName == SceneNames.Room_shop) transitionName = self.sceneName + "[door_sly]";
                    else if (self.sceneName == SceneNames.Town && info.SceneName == SceneNames.Room_Town_Stag_Station) transitionName = self.sceneName + "[door_station]";
                    else if (self.sceneName == SceneNames.Town && info.SceneName == SceneNames.Room_Bretta) transitionName = self.sceneName + "[door_bretta]";
                    else if (self.sceneName == SceneNames.Crossroads_04 && info.SceneName == SceneNames.Room_Charm_Shop) transitionName = self.sceneName + "[door_charmshop]";
                    else if (self.sceneName == SceneNames.Crossroads_04 && info.SceneName == SceneNames.Room_Mender_House) transitionName = self.sceneName + "[door_Mender_House]";
                    else if (self.sceneName == SceneNames.Ruins1_04 && info.SceneName == SceneNames.Room_nailsmith) transitionName = self.sceneName + "[door1]";
                    else if (self.sceneName == SceneNames.Fungus3_48 && info.SceneName == SceneNames.Room_Queen) transitionName = self.sceneName + "[door1]";
                    else
                    {
                        orig(self, info);
                        return;
                    }
                }
                else
                {
                    string name = tp.name.Split(null).First(); // some transitions have duplicates named left1 (1) and so on

                    if (RandomizerMod.Instance.Settings.RandomizeRooms)
                    {
                        // It's simplest to treat the three transitions connecting Mantis Lords and Mantis Village as one
                        if (self.sceneName == SceneNames.Fungus2_14 && name.StartsWith("bot")) name = "bot3";
                        else if (self.sceneName == SceneNames.Fungus2_15 && name.StartsWith("top")) name = "top3";
                    }

                    transitionName = self.sceneName + "[" + name + "]";
                }

                if (Instance.Settings._transitionPlacements.TryGetValue(transitionName, out string destination))
                {
                    try
                    {
                        if (!Instance.Settings.HasObtainedProgression(transitionName))
                        {
                            RandoLogger.LogTransitionToTracker(transitionName, destination);
                            Instance.Settings.UpdateObtainedProgression(transitionName);
                            Instance.Settings.UpdateObtainedProgression(destination);
                            RandoLogger.UpdateHelperLog();
                        }
                    }
                    catch (Exception e)
                    {
                        Instance.LogError("Error in modifying obtained progression settings: " + e);
                    }
                    info.SceneName = LogicManager.GetTransitionDef(destination).sceneName.Split('-').First();
                    info.EntryGateName = LogicManager.GetTransitionDef(destination).doorName;
                }
            }
            MiscSceneChanges.ApplySaveDataChanges(info.SceneName, info.EntryGateName);
            orig(self, info);
        }

        private void HandleSceneChanges(Scene from, Scene to)
        {
            if (Ref.GM.GetSceneNameString() == SceneNames.Menu_Title)
            {
                // Reset settings on menu load
                Settings = new SaveSettings();
                RandomizerAction.ClearActions();

                try
                {
                    MenuChanger.EditUI();
                }
                catch (Exception e)
                {
                    LogError("Error editing menu:\n" + e);
                }
            }

            if (Ref.GM.IsGameplayScene())
            {
                try
                {
                    // In rare cases, this is called before the previous scene has unloaded
                    // Deleting old randomizer shinies to prevent issues
                    GameObject oldShiny = GameObject.Find("Randomizer Shiny");
                    if (oldShiny != null)
                    {
                        Object.DestroyImmediate(oldShiny);
                    }

                    RandomizerAction.EditShinies();
                }
                catch (Exception e)
                {
                    LogError($"Error applying RandomizerActions to scene {to.name}:\n" + e);
                }
            }

            try
            {
                RestrictionManager.SceneChanged(to);
                MiscSceneChanges.SceneChanged(to);
            }
            catch (Exception e)
            {
                LogError($"Error applying changes to scene {to.name}:\n" + e);
            }
        }
    }
}
