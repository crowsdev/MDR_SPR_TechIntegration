using Sandbox.Game.EntityComponents;
using Sandbox.ModAPI.Ingame;
using Sandbox.ModAPI.Interfaces;
using SpaceEngineers.Game.ModAPI.Ingame;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using VRage;
using VRage.Collections;
using VRage.Game;
using VRage.Game.Components;
using VRage.Game.GUI.TextPanel;
using VRage.Game.ModAPI.Ingame;
using VRage.Game.ModAPI.Ingame.Utilities;
using VRage.Game.ObjectBuilders.Definitions;
using VRageMath;

namespace IngameScript
{
    partial class Program : MyGridProgram
    {
        #region Configuration

        private const int REFINERY_SPEED_MULTIPLIER = 3; // set to match refinery multiplier in game options (1, 3, 10)

        private const bool ENABLE_REFINERY_PRIORITIZATION = true; //set to false to disable refinery balancing
        private const bool ENABLE_ORE_TO_STORAGE = true;          //set to false to only move to storage if refineries full
        private const bool ENABLE_OUTPUT_TO_STORAGE = true;       //set to false to leave production output in refineries
        private const bool DISABLE_REFINERY_CONVEYORS = true;     //set to false to allow refinery self-adjustment between ticks

        //execution speed
        private const int SURVEY_FREQUENCY = 2; //delay between surveys. 6 = 1s
        private const int BALANCE_FREQUENCY = 3; //balance every x surveys. 1 = every survey. 2 = every 2 surveys, etc.

        //tags
        private const string LCD_TAG = "[REF]";
        private const string GROUP_TAG = "[REF]";

        //commands
        private const string CURSOR_UP = "UP";
        private const string CURSOR_DOWN = "DOWN";
        private const string SELECT = "SELECT";
        private const string SWITCH_SCREEN = "SWITCH";
        private const string RESET_PRIORITIES = "RESET";

        // commands to import and export priorities via the Custom Data field of your programmable block
        private const string EXPORT_PRIORITIES = "EXPORT";
        private const string IMPORT_PRIORITIES = "IMPORT";

        //balancing parameters
        private const float MIN_ORE_THRESHOLD = 10.0f; //minimum amount of ore per refinery
        private const float MAX_ORE_THRESHOLD = 1000000.0f; //maximum target ore
        private MyFixedPoint MIN_STORAGE_THRESHOLD = 100; //minimum amount of space necessary move secondary inventories

        //default group name, for if no groups found
        private const string DEFAULT_GROUP = "DEFAULT_GROUP"; //don't name your groups this

        private const string IGNORE_LINE = "--- IGNORE BELOW ---";

        //running icon
        private string[] RUNNING = { "|--", "-|-", "--|" };

        //set to true to echo script status to programming block. 
        // Works best with survey and balance frequency = 1
        private const bool ENABLE_DEBUG = false;

        /* End Configuration */

        #endregion

        #region MetaData

        //Metadata
        private const string ORE_ITEM_TYPE = "Ore";
        private const string SPEED_MODULE_NAME = "Productivity";

        private const string GRAVEL_TEXT = "Gravel";
        private const string GRAVEL_CRUSHER = "GravelRefinery";

        private MyFixedPoint LITER_PER_ORE = (MyFixedPoint)0.37f;

        static string LCD_TAG_REGEX = System.Text.RegularExpressions.Regex.Escape(LCD_TAG) + @"((\d+)(,(\d+))*)";
        System.Text.RegularExpressions.Regex powerDiagConfig = new System.Text.RegularExpressions.Regex(LCD_TAG_REGEX);

        static string STORAGE_PARSE_REGEX = @"\{([^}]+)\}([^{]+)";
        System.Text.RegularExpressions.Regex storageRegex = new System.Text.RegularExpressions.Regex(STORAGE_PARSE_REGEX);

        IDictionary<string, double> refineryTypes = new Dictionary<string, double>()
        {
            {"Blast Furnace", 0.65 },
            {"LargeRefinery", 1.3 },
            {"LargeRefineryIndustrial", 1.3 },
            {"LargeRefinery2x", 2.2750}, //tiered tech refineries
            {"LargeRefinery4x", 3.9813}, //tiered tech refineries
            {"LargeRefinery8x", 6.9672}, //tiered tech refineries
            {"LargeRefinery16x", 11.0081}, //SI Infinite Expanse
            {"LargeRefinery32x", 17.2827}, //SI Infinite Expanse
            {"LargeRefineryIndustrial2x", 2.2750}, //tiered tech refineries
            {"LargeRefineryIndustrial4x", 3.9813}, //tiered tech refineries
            {"LargeRefineryIndustrial8x", 6.9672}, //tiered tech refineries
            {"LargeRefineryIndustrial16x", 11.0081}, //SI Infinite Expanse
            {"LargeRefineryIndustrial32x", 17.2827}, //SI Infinite Expanse
            {"StoneIncinerator", (20 / .04) }, //tiered tech refineries
            {"StoneOblitrator16x", (40 / .06) }, //SI Infinite Expanse
            {"StoneOblitirator32x", (80 / .08) }, //SI Infinite Expanse
            {"Converter",  (0.025 / 6)}, //Mogwai Stone Squeezer
            {"LargeStoneCrusher", 0.05 / 3 }, //Stone Crusher
            {"GravelRefinery", 0.125 } //gravel crusher 
        
        };

        IDictionary<string, double> refiningSpeeds = new Dictionary<string, double>() {
            {"Scrap", 90000.0 },
            {"Stone", 1440000.0 }, //current bug, expect 1/10 in future
            {"Iron", 72000.0 },
            {"Silicon", 6000.0 },
            {"Nickel", 1800.0 },
            {"Cobalt", 1200.0 },
            {"Silver", 3600.0 },
            {"Gold", 9000.0 },
            {"Magnesium", 7200.0 },
            {"Uranium", 900.0 },
            {"Platinum", 1200.0 },
            {GRAVEL_TEXT, 72000.0 },
            // {"Carbon", 900 }, //daily needs
            // {"Phosphorus", 900 }, //daily needs
            // {"Trinium", 900 }, //stargate 
            // {"Neutronium", 900 }, //stargate 
            // {"Naquadah", 900 } //stargate
        };

        //default refining priority
        private IDictionary<int, string> defaultRefiningPriority = new Dictionary<int, string>() {
            {1, "Gold"},
            {2, "Uranium"},
            {3, "Platinum"},
            {4, "Cobalt"},
            {5, "Silicon"},
            {6, "Magnesium"},
            {7, "Silver"},
            {8, "Nickel"},
            {9, "Iron"},
            {10, "Scrap"},
            {11, "Stone"},
            {12, IGNORE_LINE },
            {13, GRAVEL_TEXT },
            // {14, "Trinium"},
            // {15, "Neutronium"},
            // {16, "Naquadah"},
            // {17, "Carbon"}, //daily needs
            // {18, "Phosphorus"} //daily needs
        };

        ISet<string> nonRefineryOres = new HashSet<string>() {
            "Ice",
            "Organic" //basic needs
        };

        string BASIC_REFINERY_NAME = "Blast Furnace";
        ISet<string> basicRefineryOres = new HashSet<string>()
        {
            "Scrap",
            "Stone",
            "Iron",
            "Silicon",
            "Nickel",
            "Cobalt",
            "Magnesium"
        };

        private const int EFFECTIVE_BALANCE_FREQUENCY = BALANCE_FREQUENCY * SURVEY_FREQUENCY;

        //End Metadata

        #endregion

        #region Data

        //Data
        private int currentScreen;
        private int runningIcon = 0;
        private int surveyCounter = 0;
        private int balanceCounter = 0;
        IDictionary<string, GroupData> groupData = new Dictionary<string, GroupData>();

        private static ByAvailableOre byAvailableOre = new ByAvailableOre();
        private static ByAvailableBasicOre byAvailableBasicOre = new ByAvailableBasicOre();
        private static ByAvailableStorage byAvailableStorage = new ByAvailableStorage();
        private static ByAvailableGravel byAvailableGravel = new ByAvailableGravel();
        private const string NON_ORE_STRING = "NON_ORE_STRING";

        private double totalTimeHours = 0.0;
        private double totalGravelTimeHours = 0.0;

        #endregion

        public Program()
        {
            Runtime.UpdateFrequency = UpdateFrequency.Update10;
        }

        public void Save()
        {
            Storage = GroupDataToString(groupData);
            Echo("Saving priorities: " + Storage);

            //reenable conveyors when Save() occurs
            if (DISABLE_REFINERY_CONVEYORS)
            {
                foreach (GroupData group in groupData.Values)
                {
                    foreach (IMyRefinery refinery in group.Refineries)
                    {
                        refinery.UseConveyorSystem = true;
                    }
                }
            }
        }

        public void Main(string argument, UpdateType updateSource)
        {
            //update counters
            surveyCounter = (surveyCounter + 1) % SURVEY_FREQUENCY;
            balanceCounter = (balanceCounter + 1) % EFFECTIVE_BALANCE_FREQUENCY;

            base.Echo(String.Format("Ticks to Survey:{0}\nTicks to Balance:{1}", surveyCounter, balanceCounter));
            base.Echo("Total Ore Time: " + HoursToTimeString(totalTimeHours));
            if (totalGravelTimeHours > 0.01)
            {
                base.Echo("Total Gravel Time: " + HoursToTimeString(totalGravelTimeHours));
            }

            argument = argument.ToUpper();

            if (argument.Equals(RESET_PRIORITIES))
            {
                Echo("Resetting priorities");
                groupData = new Dictionary<string, GroupData>();
                Storage = "";
            }
            //instantiates one groupData per blockGroup found on the grid, or a single groupData if none
            groupData = InitializeGroupData();

            //process arguments to adjust or reset priorities
            argument = argument.ToUpper();
            if (!String.IsNullOrEmpty(argument))
            {
                ProcessCommand(argument, groupData);
            }
            //override priorities with saved as long as not RESET
            if (!argument.Equals(RESET_PRIORITIES))
            {
                LoadPrioritiesFromStorage(groupData);
            }

            if (surveyCounter != 0)
            {
                return;
            }

            //Only update the running Icon if surveying, since it won't print otherwise
            runningIcon = (runningIcon + 1) % RUNNING.Length;

            // Echo(String.Format("Server Refining Multiplier: {0}x", REFINERY_SPEED_MULTIPLIER));

            foreach (KeyValuePair<string, GroupData> groupPair in groupData)
            {
                Echo("Processing " + groupPair.Key);
                GroupData group = groupPair.Value;

                //count all the ore in the group and identify how much of each priority ore is present
                group.OreGroups = CountOresInGroup(group.TerminalBlocks);
                group.PriorityOres = FindPriorityOres(group.RefiningPriority, group.OreGroups, (group.Refineries.Count * MIN_ORE_THRESHOLD));

                Echo("Surveying " + group.Refineries.Count() + " Refineries");
                SurveyRefineries(group);

                if (ENABLE_REFINERY_PRIORITIZATION)
                {
                    if (balanceCounter == 0)
                    {
                        BalanceRefineries(group);
                    }
                }
            }
            UpdatePanels(groupData.ElementAt(currentScreen).Value);
        }

        #region Custom Methods

        //Initialize one GroupData for each blockGroup found on the grid with default priorities
        //If no groups found, return a single, default group consisting of the whole grid
        private IDictionary<string, GroupData> InitializeGroupData()
        {
            IDictionary<string, GroupData> tempData = new Dictionary<string, GroupData>();
            List<IMyBlockGroup> blockGroups = new List<IMyBlockGroup>();
            GridTerminalSystem.GetBlockGroups(blockGroups, group => group.Name.Contains(GROUP_TAG));

            if (blockGroups.Count == 0)
            {
                Echo("No groups found, defaulting to whole grid.");
                GroupData group = new GroupData(DEFAULT_GROUP);

                List<IMyRefinery> refineries = new List<IMyRefinery>();
                List<IMyTerminalBlock> allGroupBlocks = new List<IMyTerminalBlock>();
                GridTerminalSystem.GetBlocksOfType<IMyRefinery>(refineries);
                GridTerminalSystem.GetBlocks(allGroupBlocks);

                group.Refineries = refineries;
                group.TerminalBlocks = allGroupBlocks;
                group.RefiningPriority = defaultRefiningPriority.ToDictionary(entry => entry.Key, entry => entry.Value);

                tempData.Add(DEFAULT_GROUP, group);
            }
            else
            {
                Echo("Found " + blockGroups.Count + " groups.");
                foreach (IMyBlockGroup bg in blockGroups)
                {
                    GroupData group = new GroupData(bg.Name);

                    List<IMyRefinery> refineries = new List<IMyRefinery>();
                    List<IMyTerminalBlock> allGroupBlocks = new List<IMyTerminalBlock>();
                    bg.GetBlocksOfType<IMyRefinery>(refineries);
                    bg.GetBlocks(allGroupBlocks);

                    group.Refineries = refineries;
                    group.TerminalBlocks = allGroupBlocks;
                    group.RefiningPriority = defaultRefiningPriority.ToDictionary(entry => entry.Key, entry => entry.Value);

                    tempData.Add(bg.Name, group);
                }
            }

            foreach (GroupData group in tempData.Values)
            {
                if (groupData.ContainsKey(group.GroupName))
                {
                    group.CursorLocation = groupData[group.GroupName].CursorLocation;
                    group.CursorSelected = groupData[group.GroupName].CursorSelected;
                    group.RefiningPriority = groupData[group.GroupName].RefiningPriority;
                }
            }

            return tempData;
        }

        private List<OreGroup> FindPriorityOres(IDictionary<int, string> refiningPriority, IDictionary<string, OreGroup> oreGroups, float minOre)
        {
            //find our highest priority ore of the ores present
            Echo("Identifying priority ores");
            List<OreGroup> priorityOres = new List<OreGroup>();

            if (oreGroups.Count == 0)
            {
                Echo("No ores found");
                return priorityOres;
            }

            for (int i = 1; i < refiningPriority.Count; i++)
            {
                if (refiningPriority[i].Equals(IGNORE_LINE))
                {
                    break;
                }
                else if (refiningPriority[i].Equals(GRAVEL_TEXT))
                {
                    continue; //gravel can't be processed by normal refineries, so skip it
                }
                if (oreGroups.ContainsKey(refiningPriority[i]))
                {
                    if ((float)oreGroups[refiningPriority[i]].Amount < minOre)
                    {
                        Echo("Insufficient " + refiningPriority[i]);
                    }
                    else
                    {
                        Echo("Adding " + oreGroups[refiningPriority[i]].Amount + " " + refiningPriority[i]);
                        priorityOres.Add(oreGroups[refiningPriority[i]]);
                    }
                }
            }
            if (priorityOres.Count == 0)
            {
                Echo("No priority ores found");
                return priorityOres;
            }

            return priorityOres;
        }

        private double CalculateRefineryEffectiveness(IMyRefinery refinery)
        {
            Dictionary<string, float> upgrades = new Dictionary<string, float>();
            ((IMyUpgradableBlock)refinery).GetUpgrades(out upgrades);
            return (refineryTypes[refinery.BlockDefinition.SubtypeName] * ((upgrades[SPEED_MODULE_NAME] + 1) * 1.0));
        }

        /**
         * Updates refineriesPerOre to include the name and amount of items being processed by the specified refinery
         * */
        private void AddCurrentProductionItem(IMyRefinery refinery, IDictionary<string, int> refineriesPerOre)
        {
            List<MyProductionItem> items = new List<MyProductionItem>();
            refinery.GetQueue(items);
            if (items.Count <= 0)
            {
                Echo(refinery.Name + " Not currently producing");
                return;
            }
            string bluePrintName = items[0].BlueprintId.SubtypeName;
            int offSet = bluePrintName.IndexOf("OreToIngot");
            if (offSet < 1)
            {
                if (bluePrintName.IndexOf("Scrap") > -1)
                {
                    bluePrintName = "Scrap";
                }
                else if (bluePrintName.IndexOf("ToPlant") > -1)
                {
                    return; //basic needs mod, skip
                }
                else if (bluePrintName.IndexOf("mogstone") > -1)
                {
                    bluePrintName = "Stone"; //mogwais stone squeezer mod
                }
                else if (bluePrintName.IndexOf("StoneOreToOres") > -1)
                {
                    bluePrintName = "Stone"; //stone crusher mod
                }
                else if (bluePrintName.IndexOf("GravelToIngots") > -1)
                {
                    bluePrintName = "Gravel"; //gravel sifter mod
                }
                else
                {
                    throw new InvalidOperationException("Unknown Blueprint: " + bluePrintName);
                }
            }
            else
            {
                bluePrintName = bluePrintName.Substring(0, bluePrintName.IndexOf("OreToIngot"));
            }

            if (!refineriesPerOre.ContainsKey(bluePrintName))
            {
                Echo("New blueprint " + bluePrintName);
                refineriesPerOre.Add(bluePrintName, 1);
            }
            else
            {
                refineriesPerOre[bluePrintName]++;
                Echo("Additional " + bluePrintName + " found (" + refineriesPerOre[bluePrintName] + ")");
            }
        }

        bool SurveyRefineries(GroupData group)
        {
            bool refineriesCorrect = true;
            foreach (IMyRefinery refinery in group.Refineries)
            {
                string refineryType = refinery.BlockDefinition.SubtypeName;
                Echo("Surveying " + refinery.CustomName + ": " + refinery.BlockDefinition.SubtypeName);
                if (refineryTypes.ContainsKey(refineryType))
                {
                    if (!refineryType.Equals(GRAVEL_CRUSHER))
                    {
                        group.TotalRefiningCapability += CalculateRefineryEffectiveness(refinery);
                    }

                    if (refineryType.Equals(BASIC_REFINERY_NAME))
                    {
                        group.BasicRefineries++;
                    }
                    else if (refineryType.Equals(GRAVEL_CRUSHER))
                    {
                        group.GravelRefiningCapability += CalculateRefineryEffectiveness(refinery);
                        group.GravelRefineries++;
                    }
                    //check if refinery contains correct ore
                    //move correct ore to front if there
                    if (refinery.IsProducing)
                    {
                        group.ActiveRefineries++;
                        AddCurrentProductionItem(refinery, group.RefineriesPerOre);

                        if (refineryType.Equals(GRAVEL_CRUSHER))
                        {
                            continue; //no need to sort gravel crusher inventory
                        }

                        bool correctOreFound = false;
                        List<MyInventoryItem> invItems = new List<MyInventoryItem>();
                        refinery.GetInventory(0).GetItems(invItems);
                        for (int i = 0; i < invItems.Count; i++)
                        {
                            MyInventoryItem item = invItems[i];
                            if (group.PriorityOres.Count > 0 && item.Type.SubtypeId.Contains(group.PriorityOres[0].Name))
                            {
                                correctOreFound = true;
                                // move to front
                                if (i != 0)
                                {
                                    Echo("Moving " + group.PriorityOres[0].Name + " to the front of the queue");
                                    refinery.GetInventory(0).TransferItemTo(refinery.GetInventory(0), i, 0, true);
                                }
                            }
                        }
                        if (!correctOreFound && invItems.Count > 0 && group.PriorityOres.Count > 0)
                        {
                            Echo(refinery.CustomName + " processing " + invItems[0].Type.SubtypeId + " instead of " + group.PriorityOres[0].Name + ".");
                            refineriesCorrect = false;
                        }
                    }
                    else
                    {
                        Echo("Refinery not producing");
                        group.InactiveRefineries++;
                        refineriesCorrect = false;
                    }
                }
                else if (refineryType.Contains("WRS") || refineryType.Contains("Hydroponics"))
                {
                    //basic needs, skip
                    continue;
                }
                else
                {
                    throw new InvalidOperationException("Update refineryTypes meta data with " + refineryType);
                }

            }
            return refineriesCorrect;
        }

        private void BalanceRefineries(GroupData group)
        {
            Echo("Balancing " + group.GroupName);
            MyFixedPoint oreAmount = 0;
            MyFixedPoint basicOreAmount = 0;
            MyFixedPoint gravelAmount = 0;
            string oreName = NON_ORE_STRING;
            string basicOreName = NON_ORE_STRING;
            bool enableBasicProcessing = false;
            if (group.PriorityOres.Count() > 0)
            {
                //check if we need a separate ore for basic refineries
                if (group.BasicRefineries > 0 && !basicRefineryOres.Contains(group.PriorityOres[0].Name))
                {
                    foreach (OreGroup priorityOre in group.PriorityOres)
                    {
                        if (basicRefineryOres.Contains(priorityOre.Name))
                        {
                            enableBasicProcessing = true;
                            basicOreName = priorityOre.Name;
                            basicOreAmount = (MyFixedPoint)Math.Min(((float)priorityOre.Amount / (float)group.BasicRefineries), MAX_ORE_THRESHOLD);
                            Echo("Setting BasicRefining ore: " + basicOreName + " min: " + basicOreAmount);
                            break;
                        }
                    }
                }

                //determine how much should be in each refinery
                int numRefineries = enableBasicProcessing ? group.Refineries.Count - group.BasicRefineries : group.Refineries.Count;
                numRefineries -= group.GravelRefineries;
                oreAmount = (MyFixedPoint)Math.Min(((float)group.PriorityOres[0].Amount / (float)numRefineries), MAX_ORE_THRESHOLD);
                oreName = group.PriorityOres[0].Name;
                Echo("Target: " + oreAmount + " " + oreName + " per Refinery");
                if (enableBasicProcessing)
                {
                    Echo("Target: " + basicOreAmount + " " + basicOreName + " per Basic Refinery");
                }
                //check if we're processing gravel
                if (group.GravelRefineries > 0)
                {
                    gravelAmount = (MyFixedPoint)Math.Min(((float)group.OreGroups[GRAVEL_TEXT].Amount / (float)group.GravelRefineries), MAX_ORE_THRESHOLD);
                    Echo("Gravel Target: " + gravelAmount + " per Gravel Sifter");
                }
            }

            //calculate available space and ore
            List<Container> storageContainers = new List<Container>();
            IDictionary<long, Container> containerMap = new Dictionary<long, Container>();

            Echo("Cataloging group containers");
            foreach (IMyTerminalBlock block in group.TerminalBlocks)
            {
                bool isRefinery = block is IMyRefinery;
                bool isStorage = block is IMyCargoContainer;
                if (!isStorage && !isRefinery)
                {
                    continue;
                }
                //Echo(block.CustomName + ": Refinery(" + isRefinery + ") Cargo(" + isStorage + ")");
                Container c = new Container(block.CustomName, block.GetInventory(0), oreName, basicOreName, isRefinery);
                //Echo(c.CustomName + " catalogued. " + c.Ore.Amount + " " + c.PriorityOre + " " + c.BasicOre.Amount + " " + c.PriorityBasicOre);
                storageContainers.Add(c);
                containerMap.Add(block.EntityId, c);
            }

            //all refineries should already have priorityOre in inv slot 1 if present
            foreach (IMyRefinery refinery in group.Refineries)
            {
                Echo("Balancing " + refinery.CustomName);
                if (DISABLE_REFINERY_CONVEYORS)
                {
                    refinery.UseConveyorSystem = false;
                }

                bool isBasic = refinery.BlockDefinition.SubtypeName.Equals(BASIC_REFINERY_NAME) && enableBasicProcessing;
                bool isGravel = refinery.BlockDefinition.SubtypeName.Equals(GRAVEL_CRUSHER);

                //figure out which ore we're using for this refinery and how much we need of it
                string keepOre = isBasic ? basicOreName : isGravel ? GRAVEL_TEXT : oreName;
                MyFixedPoint keepOreAmount = (MyFixedPoint)Math.Max((float)(isBasic ? basicOreAmount : isGravel ? gravelAmount : oreAmount), MIN_ORE_THRESHOLD);

                //simple first step - dump all non priority ores into non-refinery containers if found
                bool storageComplete = false;
                if (ENABLE_ORE_TO_STORAGE && !isGravel)
                {
                    storageComplete = MoveNonPriorityOresToStorage(refinery, containerMap[refinery.EntityId], storageContainers, keepOre);
                }
                if (ENABLE_OUTPUT_TO_STORAGE)
                {
                    MoveSecondaryInventoryToStorage(refinery, containerMap[refinery.EntityId], storageContainers);
                }

                if (group.PriorityOres.Count == 0 && !isGravel)
                {
                    continue; //if we have no priority ore, no further balancing needed
                }

                IMyInventory inv = refinery.GetInventory(0);
                if (!inv.GetItemAt(0).HasValue ||
                    !inv.GetItemAt(0).Value.Type.SubtypeId.Contains(keepOre) ||
                    (isGravel && !IsGravel(inv.GetItemAt(0).Value)))
                {
                    if (!storageComplete && !isGravel)
                    {
                        Echo("Insufficient space to store non-priority ores, attempting to balance in place");

                        //if container doesn't have enough room, move some ore into another container
                        while (((inv.CurrentVolume * 1000) + (keepOreAmount * LITER_PER_ORE)) > (inv.MaxVolume * 1000))
                        {
                            Echo("Insufficient room in " + refinery.CustomName + ".");
                            Echo("CurrentVol" + (inv.CurrentVolume * 1000) + " target ore amount: " + keepOreAmount + " maxVol: " + (inv.MaxVolume * 1000));
                            storageContainers.Sort(byAvailableStorage);
                            Container storage = storageContainers.First();

                            if (inv.GetItemAt(0).HasValue)
                            {
                                TransferAllowableOre(storage, inv, inv.GetItemAt(0).Value);
                            }
                            else
                            {
                                Echo("Unable to find item to transfer from " + refinery.CustomName + ", skipping");
                                break;
                            }

                            storage.Refresh();
                            containerMap[refinery.EntityId].Refresh();

                        }
                    }
                    //at this point, we have a refinery that has no oreName ore in it
                    MyFixedPoint itemAmount = GetItemAmount(inv, keepOre);
                    if (itemAmount < keepOreAmount)
                    //while(itemAmount < oreAmount)
                    {
                        Echo("Insufficient Ore. Expected: " + keepOreAmount + " Found: " + itemAmount);
                        if (isBasic)
                        {
                            storageContainers.Sort(byAvailableBasicOre);
                        }
                        else if (isGravel)
                        {
                            storageContainers.Sort(byAvailableGravel);
                        }
                        else
                        {
                            storageContainers.Sort(byAvailableOre);
                        }
                        //foreach (Container c in storageContainers)
                        //{
                        //   Echo(c.CustomName + ": " + (isBasic ? c.BasicOre.Amount : c.Ore.Amount));
                        //}
                        Container storage = null;
                        for (int i = storageContainers.Count - 1; i >= 0; i--)
                        {
                            if (!storageContainers[i].Inventory.Equals(refinery.GetInventory(0)))
                            {
                                storage = storageContainers[i];
                                break;
                            }
                        }
                        if (storage == null)
                        {
                            Echo("Unable to find ore to move to " + refinery.CustomName);
                            break;
                        }
                        //Container storage = storageContainers.Last();

                        MyFixedPoint neededOre = keepOreAmount - itemAmount;
                        MyFixedPoint storageOre = isBasic ? storage.BasicOre.Amount : isGravel ? storage.Gravel.Amount : storage.Ore.Amount;
                        MyFixedPoint transferAmount = (MyFixedPoint)(Math.Min((float)(neededOre + 1), (float)storageOre));
                        Echo("Transferring " + transferAmount + " " + (isBasic ? storage.BasicOre.Type.SubtypeId : isGravel ? GRAVEL_TEXT : storage.Ore.Type.SubtypeId) + " from " + storage.CustomName + " to " + refinery.CustomName);
                        storage.Inventory.TransferItemTo(inv, (isBasic ? storage.BasicOre : isGravel ? storage.Gravel : storage.Ore), transferAmount);

                        storage.Refresh();
                        containerMap[refinery.EntityId].Refresh();
                        itemAmount = GetItemAmount(inv, keepOre);
                    }
                }
            }
        }

        //moves non-priority ore out of the provided refinery and into non-refinery storage containers, if present
        private bool MoveNonPriorityOresToStorage(IMyRefinery refinery, Container refineryContainer, List<Container> storageContainers, string oreName)
        {
            IMyInventory inv = refinery.InputInventory;
            if (inv.ItemCount == 0)
            {
                return true; //Nothing to move, bail
            }
            Echo("Moving surplus ore from " + refinery.CustomName + " to storage.");

            for (int i = inv.ItemCount - 1; i >= 0; i--)
            {
                while (inv.GetItemAt(i).HasValue && !inv.GetItemAt(i).Value.Type.SubtypeId.Contains(oreName))
                {
                    storageContainers.Sort(byAvailableStorage);
                    if (!storageContainers.First().IsRefinery && storageContainers.First().GetSpace() > 0)
                    {
                        TransferAllowableOre(storageContainers.First(), inv, inv.GetItemAt(i).Value);
                        storageContainers.First().Refresh();
                        refineryContainer.Refresh();
                    }
                    else
                    {
                        return false; //ran out of room to store stuff in non-refineries
                    }
                }
            }
            return true;
        }

        //move all items out of secondary inventory into storage
        private void MoveSecondaryInventoryToStorage(IMyRefinery refinery, Container refineryContainer, List<Container> storageContainers)
        {
            IMyInventory inv = refinery.OutputInventory;
            if (!inv.GetItemAt(0).HasValue)
            {
                return;
            }
            Echo("Moving " + refinery.CustomName + " output to storage");
            while (inv.GetItemAt(0).HasValue)
            {
                //Echo("Moving " + inv.GetItemAt(0).Value.Type.SubtypeId);
                storageContainers.Sort(byAvailableStorage);

                if (storageContainers.First().GetSpace() < MIN_STORAGE_THRESHOLD)
                {
                    Echo("Insufficient storage available in " + storageContainers.First().CustomName
                        + ", unable to relocate secondary inventory");
                    break;
                }

                if (!storageContainers.First().IsRefinery && storageContainers.First().GetSpace() > 0)
                {
                    TransferAllowableOre(storageContainers.First(), inv, inv.GetItemAt(0).Value);
                    storageContainers.First().Refresh();
                    refineryContainer.Refresh();
                }
            }
        }

        //transfers the maximum amount of the specified item from the inv into the container that will fit
        private void TransferAllowableOre(Container storage, IMyInventory inv, MyInventoryItem item)
        {
            MyFixedPoint allowedVolume = (storage.GetSpace()) * 1000;
            float volumeOfFirstItem = (float)item.Amount / (float)LITER_PER_ORE;
            MyFixedPoint transferAmount = (MyFixedPoint)(Math.Min((float)allowedVolume, volumeOfFirstItem));
            //Echo("Moving " + transferAmount + " " + item.Type.SubtypeId + " to " + storage.CustomName);
            inv.TransferItemTo(storage.Inventory, item, transferAmount);
        }

        //count the amount of specified ore in an inventory
        MyFixedPoint GetItemAmount(IMyInventory inv, string oreName)
        {
            Echo("Counting " + oreName);
            MyFixedPoint amount = 0;
            for (int i = 0; i < inv.ItemCount; i++)
            {
                MyInventoryItem item = inv.GetItemAt(i).Value;
                if ((item.Type.TypeId.EndsWith(ORE_ITEM_TYPE) && item.Type.SubtypeId.Contains(oreName)) ||
                    (oreName.Equals(GRAVEL_TEXT) && IsGravel(item)))
                {
                    amount += item.Amount;
                }
            }
            return amount;
        }

        private void UpdatePanels(GroupData group)
        {
            totalTimeHours = 0.0;
            totalGravelTimeHours = 0.0;

            List<IMyTerminalBlock> panels = new List<IMyTerminalBlock>();
            GridTerminalSystem.SearchBlocksOfName(LCD_TAG, panels);
            List<PanelMessage> text = new List<PanelMessage>();
            foreach (KeyValuePair<int, string> priority in group.RefiningPriority)
            {
                string oreListing = priority.Key == group.CursorLocation ? group.CursorSelected ? "[" + priority.Value + "]" : ">" + priority.Value : priority.Value;
                int numRefineries = group.RefineriesPerOre.ContainsKey(priority.Value) ? group.RefineriesPerOre[priority.Value] : 0;
                if (numRefineries > 0)
                {
                    oreListing += " (" + numRefineries + " active)";
                }
                string status = BuildOreStatus(group, priority.Value);
                text.Add(new PanelMessage(priority.Key + ". " + oreListing, status));
            }
            foreach (IMyTerminalBlock panel in panels)
            {
                WritePanelText(panel, text, group.CursorLocation, RUNNING[runningIcon] + " " + group.GroupName + ": " +
                    Math.Round(Convert.ToDecimal(group.TotalRefiningCapability), 2) + " capability");
            }
        }

        private string BuildOreStatus(GroupData group, string ore)
        {
            if (!group.OreGroups.ContainsKey(ore) || (group.TotalRefiningCapability <= 0 && group.GravelRefiningCapability <= 0))
            {
                //Echo("Cannot find key " + ore + " or capability = " + group.TotalRefiningCapability);
                return "";
            }
            double oreAmount = (double)group.OreGroups[ore].Amount;
            double effectiveRefining = ore.Equals(GRAVEL_TEXT) ? group.GravelRefiningCapability : group.TotalRefiningCapability;
            double currentHours = effectiveRefining == 0 ? 0.0 : oreAmount / (refiningSpeeds[ore] * effectiveRefining * REFINERY_SPEED_MULTIPLIER);

            if (ore.Equals(GRAVEL_TEXT))
            {
                totalGravelTimeHours += currentHours;
            }
            else
            {
                totalTimeHours += currentHours;
            }

            string displayCount = OreToCountString(oreAmount);
            string displayTime = HoursToTimeString(currentHours);

            return String.Format(" {0} / {1}", displayTime, displayCount);
        }

        //Maybe there's an easier way to do this, but there aren't that many cases so just manually build time string
        string HoursToTimeString(double hours)
        {
            string time = "";
            try
            {
                TimeSpan t = TimeSpan.FromHours(hours);
                if (t.TotalHours >= 1)
                {
                    time = (int)t.TotalHours + "h " + t.Minutes + "m";
                }
                else if (t.Minutes > 0)
                {
                    time = t.Minutes + "m " + t.Seconds + "s";
                }
                else
                {
                    time = t.Seconds + "s";
                }
            }
            catch (Exception e)
            {
                Echo("Failed to convert " + hours + " hours to string");
            }
            return time;
        }

        //format Ore counts
        string OreToCountString(double oreCount)
        {
            string ore;
            if (oreCount >= 1000000)
            {
                ore = "" + Math.Round(Convert.ToDecimal((oreCount / 1000000)), 1) + "M";
            }
            else if (oreCount >= 1000)
            {
                ore = "" + Math.Round(Convert.ToDecimal((oreCount / 1000)), 1) + "k";
            }
            else
            {
                ore = "" + Math.Round(oreCount, 1);
            }
            return ore;
        }

        /**
         * Returns a map containing all the ore in the provided group of blocks
         */
        private IDictionary<string, OreGroup> CountOresInGroup(List<IMyTerminalBlock> terminalBlocks)
        {
            Echo("Counting Ore");
            IDictionary<string, OreGroup> oreGroups = new Dictionary<string, OreGroup>();
            foreach (IMyTerminalBlock block in terminalBlocks)
            {
                for (int i = 0; i < block.InventoryCount; i++)
                {
                    IMyInventory inv = block.GetInventory(i);
                    for (int j = 0; j < inv.ItemCount; j++)
                    {
                        MyInventoryItem item = inv.GetItemAt(j).Value;
                        if (item.Type.TypeId.ToString().EndsWith(ORE_ITEM_TYPE))
                        {
                            AddItem(item.Type.SubtypeId, item, oreGroups);
                        }
                        else if (IsGravel(item))
                        {
                            AddItem(GRAVEL_TEXT, item, oreGroups);
                        }
                        else
                        {
                            //Echo("Skipping non-ore Item: " + item.Type.TypeId.ToString() + " " + item.Type.SubtypeId.ToString());
                        }
                    }
                }
            }
            return oreGroups;
        }

        private static bool IsGravel(MyInventoryItem item)
        {
            //stone ingots are gravel
            return item.Type.TypeId.EndsWith("Ingot") && item.Type.SubtypeId.Equals("Stone");
        }

        //Add the current stack to the oreCounts Map
        void AddItem(string itemName, MyInventoryItem item, IDictionary<string, OreGroup> oreGroups)
        {
            Echo("Adding " + item.Amount + " " + itemName);
            if (!oreGroups.ContainsKey(itemName))
            {
                oreGroups.Add(itemName, new OreGroup(item));
            }
            else
            {
                oreGroups[itemName].Add(item);
            }
        }

        void WritePanelText(IMyTerminalBlock panel, List<PanelMessage> messages, int cursorLocation, string optionalHeader = "")
        {
            IList<IMyTextSurface> surfaces = new List<IMyTextSurface>();
            if (panel is IMyTextSurface)
            {
                surfaces.Add((IMyTextSurface)panel);
            }
            else if (panel is IMyTextSurfaceProvider)
            {
                //multi-surfaces possible, check CustomData for config
                HashSet<int> surfacesToUpdate = new HashSet<int>();
                string customData = panel.CustomData;
                System.Text.RegularExpressions.Match m = powerDiagConfig.Match(customData);
                if (m.Success)
                {
                    foreach (string s in m.Groups[1].Value.Split(','))
                    {
                        surfacesToUpdate.Add(Convert.ToInt32(s));
                    }
                }

                IMyTextSurfaceProvider provider = (IMyTextSurfaceProvider)panel;
                for (int i = 0; i < provider.SurfaceCount; i++)
                {
                    if (surfacesToUpdate.Contains(i))
                    {
                        surfaces.Add(provider.GetSurface(i));
                    }
                }
            }

            foreach (IMyTextSurface surface in surfaces)
            {
                surface.ContentType = VRage.Game.GUI.TextPanel.ContentType.TEXT_AND_IMAGE;
                StringBuilder sb = new StringBuilder();

                //figure out how many rows can fit. 
                float height = surface.SurfaceSize.Y;
                float lineHeight = surface.MeasureStringInPixels(new StringBuilder(messages[0].LeftSide), surface.Font, surface.FontSize).Y;
                int numLines = (int)(height / lineHeight);
                if (!String.IsNullOrEmpty(optionalHeader))
                {
                    numLines--; //leave room for header
                    sb.Append(optionalHeader).AppendLine();
                }

                int startingLine = Math.Max((cursorLocation - numLines) + 1, 0);

                float spaceWidth = surface.MeasureStringInPixels(new StringBuilder(" "), surface.Font, surface.FontSize).X;
                float surfaceWidth = surface.SurfaceSize.X;
                float padding = surface.TextPadding;
                surfaceWidth = (surfaceWidth * ((100 - padding) / 100)) - spaceWidth;
                for (int i = startingLine; i < messages.Count; i++)
                {
                    string rowString = "";
                    if (messages[i].RightSide.Equals(""))
                    {
                        sb.Append(messages[i].LeftSide).AppendLine();
                    }
                    else
                    {
                        float lineWidth = surface.MeasureStringInPixels(new StringBuilder(messages[i].LeftSide + messages[i].RightSide), surface.Font, surface.FontSize).X;
                        int spaces = Math.Max((int)((surfaceWidth - lineWidth) / spaceWidth), 1);
                        for (int j = spaces; j > 0; j--)
                        {
                            rowString = messages[i].LeftSide + new string(' ', j) + messages[i].RightSide;
                            if (surface.MeasureStringInPixels(new StringBuilder(rowString), surface.Font, surface.FontSize).X < surfaceWidth)
                            {
                                break;
                            }
                        }
                        sb.Append(rowString).AppendLine();
                    }
                }
                surface.WriteText(sb.ToString());
            }
        }

        private void LoadPrioritiesFromStorage(IDictionary<string, GroupData> groupData)
        {
            foreach (System.Text.RegularExpressions.Match match in storageRegex.Matches(Storage))
            {
                string groupName = match.Groups[1].Value;
                string priorityString = match.Groups[2].Value;

                if (!groupData.ContainsKey(groupName))
                {
                    Echo("Group " + groupName + " no longer found, discarding priorities.");
                    continue;
                }

                string[] priorities = priorityString.Split('|');
                if (priorities.Count() > 1)
                {
                    Echo(groupName + ": " + priorities.Count() + " priorities loading...");
                    groupData[groupName].RefiningPriority = new Dictionary<int, string>();
                    foreach (string priority in priorities)
                    {
                        string[] p = priority.Split(':');
                        groupData[groupName].RefiningPriority.Add(Convert.ToInt32(p[0]), p[1]);
                    }
                    Storage = "";
                }

            }
        }

        private string GroupDataToString(IDictionary<string, GroupData> groupData)
        {
            string ret = "";
            foreach (GroupData group in groupData.Values)
            {
                ret += ("{" + group.GroupName + "}" +
                    group.RefiningPriority.Select(kvp => kvp.Key + ":" + kvp.Value).Aggregate((a, b) => a + "|" + b));
            }
            return ret;
        }

        //update the priority list and cursor location based on the command
        private void ProcessCommand(string argument, IDictionary<string, GroupData> groupData)
        {
            GroupData currentGroup = groupData.ElementAt(currentScreen).Value;
            IDictionary<int, string> currentPriority = currentGroup.RefiningPriority;
            int cursorLocation = currentGroup.CursorLocation;

            switch (argument)
            {
                case CURSOR_UP:
                    if (currentGroup.CursorSelected)
                    {
                        if (cursorLocation > 1)
                        {
                            string temp = currentPriority[cursorLocation];
                            currentPriority[cursorLocation] = currentPriority[cursorLocation - 1];
                            currentPriority[cursorLocation - 1] = temp;
                        }
                        else
                        {
                            break; //if unable to move item, also don't move cursor
                        }
                    }
                    currentGroup.CursorLocation = ((currentGroup.CursorLocation - 2 + currentPriority.Count) % currentPriority.Count) + 1;
                    break;
                case CURSOR_DOWN:
                    if (currentGroup.CursorSelected)
                    {
                        if (cursorLocation < defaultRefiningPriority.Count)
                        {
                            string temp = currentPriority[cursorLocation];
                            currentPriority[cursorLocation] = currentPriority[cursorLocation + 1];
                            currentPriority[cursorLocation + 1] = temp;
                        }
                        else
                        {
                            break;
                        }
                    }
                    currentGroup.CursorLocation = (currentGroup.CursorLocation % currentPriority.Count) + 1;
                    break;
                case SELECT:
                    currentGroup.CursorSelected = !currentGroup.CursorSelected;
                    break;
                case SWITCH_SCREEN:
                    currentScreen = (currentScreen + 1) % groupData.Count;
                    break;
                case EXPORT_PRIORITIES:
                    Me.CustomData = GroupDataToString(groupData);
                    break;
                case IMPORT_PRIORITIES:
                    Storage = Me.CustomData;
                    break;
            }
        }

        #endregion

        #region SubClasses

        //Bundles all the inventory items for a given ore type
        class OreGroup
        {
            public string Name;
            public MyFixedPoint Amount;
            public List<MyInventoryItem> Items;

            public OreGroup(MyInventoryItem item)
            {
                this.Name = item.Type.SubtypeId;
                this.Amount = item.Amount;
                Items = new List<MyInventoryItem>();
                Items.Add(item);
            }

            public void Add(MyInventoryItem item)
            {
                Items.Add(item);
                Amount += item.Amount;
            }
        }

        //Collected information about each group to monitor separately
        class GroupData
        {
            public string GroupName;
            public IDictionary<int, string> RefiningPriority;
            public IDictionary<string, OreGroup> OreGroups;
            public IDictionary<string, int> RefineriesPerOre = new Dictionary<string, int>();
            //public List<IMyTerminalBlock> Lcds;
            public List<IMyRefinery> Refineries;
            public int BasicRefineries = 0;
            public int GravelRefineries = 0;
            public List<IMyTerminalBlock> TerminalBlocks;
            public List<OreGroup> PriorityOres;
            public int CursorLocation = 1;
            public bool CursorSelected = false;
            public double TotalRefiningCapability = 0.0;
            public double GravelRefiningCapability = 0.0;
            public int InactiveRefineries = 0;
            public int ActiveRefineries = 0;

            public GroupData(string groupName)
            {
                this.GroupName = groupName;
            }
        }

        class PanelMessage
        {
            public string LeftSide { get; set; }
            public string RightSide { get; set; }
            public PanelMessage(string leftSide, string rightSide)
            {
                this.LeftSide = leftSide;
                this.RightSide = rightSide;
            }
        }

        //each refinery or storage in the group counts as container and can act as either
        //storage for or provider of the priority ore
        class Container
        {
            public string CustomName { get; set; }
            public string PriorityOre { get; set; }
            public string PriorityBasicOre { get; set; }
            public IMyInventory Inventory { get; set; }
            public MyInventoryItem Ore { get; set; }
            public MyInventoryItem BasicOre { get; set; }
            public MyInventoryItem Gravel { get; set; }
            public bool IsRefinery { get; set; }

            public Container(string customName, IMyInventory inventory, string priorityOre, string priorityBasicOre, bool isRefinery)
            {
                this.CustomName = customName;
                this.Inventory = inventory;
                this.PriorityOre = priorityOre;
                this.PriorityBasicOre = priorityBasicOre;
                this.IsRefinery = isRefinery;
                Refresh();
            }

            public void Refresh()
            {
                List<MyInventoryItem> items = new List<MyInventoryItem>();
                Inventory.GetItems(items);
                foreach (MyInventoryItem item in items)
                {
                    if (item.Type.TypeId.EndsWith(ORE_ITEM_TYPE) && item.Type.SubtypeId.Contains(PriorityOre))
                    {
                        Ore = item;
                    }
                    else if (item.Type.TypeId.EndsWith(ORE_ITEM_TYPE) && item.Type.SubtypeId.Contains(PriorityBasicOre))
                    {
                        BasicOre = item;
                    }
                    else if (item.Type.TypeId.EndsWith("Ingot") && item.Type.SubtypeId.Equals("Stone"))
                    {
                        Gravel = item;
                    }
                }
            }
            public MyFixedPoint GetSpace()
            {
                return Inventory.MaxVolume - Inventory.CurrentVolume;
            }
        }

        //sorts a set of containers by how much empty space they have
        public class ByAvailableStorage : IComparer<Container>
        {
            int IComparer<Container>.Compare(Container c1, Container c2)
            {
                MyFixedPoint c1Space = c1.GetSpace();
                MyFixedPoint c2Space = c2.GetSpace();

                //prioritize storing in storage containers
                if (c1.IsRefinery != c2.IsRefinery)
                {
                    if (c2.IsRefinery && c1Space > 0)
                    {
                        return -1;
                    }
                    else if (c1.IsRefinery && c2Space > 0)
                    {
                        return 1;
                    }
                }
                int retVal = (int)(c2Space - c1Space);
                if (retVal == 0)
                {
                    return c1.CustomName.CompareTo(c2.CustomName);
                }
                else
                {
                    return retVal;
                }
            }
        }

        //sorts a set of containers by how much of the priority ore they contain
        public class ByAvailableOre : IComparer<Container>
        {
            int IComparer<Container>.Compare(Container c1, Container c2)
            {
                if (c1.IsRefinery != c2.IsRefinery)
                {
                    if (c1.IsRefinery && (float)c2.Ore.Amount > MIN_ORE_THRESHOLD)
                    {
                        return -1;
                    }
                    else if (c2.IsRefinery && (float)c1.Ore.Amount > MIN_ORE_THRESHOLD)
                    {
                        return 1;
                    }
                }
                int retVal = (int)(c1.Ore.Amount - c2.Ore.Amount);
                if (retVal == 0)
                {
                    return c1.CustomName.CompareTo(c2.CustomName);
                }
                else
                {
                    return retVal;
                }
            }
        }


        //sorts a set of containers by how much of the priority basic refinery ore they contain
        public class ByAvailableBasicOre : IComparer<Container>
        {
            int IComparer<Container>.Compare(Container c1, Container c2)
            {
                if (c1.IsRefinery != c2.IsRefinery)
                {
                    if (c1.IsRefinery && (float)c2.BasicOre.Amount > MIN_ORE_THRESHOLD)
                    {
                        return -1;
                    }
                    else if (c2.IsRefinery && (float)c1.BasicOre.Amount > MIN_ORE_THRESHOLD)
                    {
                        return 1;
                    }
                }
                int retVal = (int)(c1.BasicOre.Amount - c2.BasicOre.Amount);
                if (retVal == 0)
                {
                    return c1.CustomName.CompareTo(c2.CustomName);
                }
                else
                {
                    return retVal;
                }
            }
        }

        //sorts a set of containers by how much gravel they contain
        public class ByAvailableGravel : IComparer<Container>
        {
            int IComparer<Container>.Compare(Container c1, Container c2)
            {
                if (c1.IsRefinery != c2.IsRefinery)
                {
                    if (c1.IsRefinery && (float)c2.Gravel.Amount > MIN_ORE_THRESHOLD)
                    {
                        return -1;
                    }
                    else if (c2.IsRefinery && (float)c1.Gravel.Amount > MIN_ORE_THRESHOLD)
                    {
                        return 1;
                    }
                }
                int retVal = (int)(c1.Gravel.Amount - c2.Gravel.Amount);
                if (retVal == 0)
                {
                    return c1.CustomName.CompareTo(c2.CustomName);
                }
                else
                {
                    return retVal;
                }
            }
        }

        private void Echo(string message)
        {
            if (ENABLE_DEBUG)
            {
                base.Echo(message);
            }
        }

        #endregion
    }
}
