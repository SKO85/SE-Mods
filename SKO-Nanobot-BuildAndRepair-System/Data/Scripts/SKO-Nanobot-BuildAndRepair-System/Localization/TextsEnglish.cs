using System.Collections.Generic;

namespace SKONanobotBuildAndRepairSystem.Localization
{
    public static class TextsEnglish
    {
        public static Dictionary<string, string> Dictionary = new Dictionary<string, string>()
        {
            {"ModeSettings_Headline",           "—————— Mode Settings ——————"},
            {"SearchMode",                      "Mode"},
            {"SearchMode_Tooltip",              "Select how the nanobots search and reach their targets."},
            {"SearchMode_Walk",                 "Walk mode"},
            {"SearchMode_Fly",                  "Fly mode"},
            {"WorkMode",                        "Work mode"},
            {"WorkMode_Tooltip",                "Select how the nanobots decide what to do (weld or grind)."},
            {"WorkMode_WeldB4Grind",            "Weld before grind"},
            {"WorkMode_GrindB4Weld",            "Grind before weld"},
            {"WorkMode_GrindIfWeldStuck",       "Grind if weld get stuck"},
            {"WorkMode_WeldOnly",               "Welding only"},
            {"WorkMode_GrindOnly",              "Grinding only"},
            {"WeldSettings_Headline",           "—————— Settings for Welding ——————"},
            {"WeldUseIgnoreColor",              "Use Ignore Color"},
            {"WeldUseIgnoreColor_Tooltip",      "When checked, the system will ignore blocks with the color defined further down."},
            {"WeldBuildNew",                    "Build new"},
            {"WeldBuildNew_Tooltip",            "When checked, the System will also construct projected blocks."},
            {"WeldToFuncOnly",                  "Weld to functional only"},
            {"WeldToFuncOnly_Tooltip",          "When checked, bock only welded to functional state."},
            {"WeldPriority",                    "Welding Priority"},
            {"WeldPriority_Tooltip",            "Enable/Disable build-repair of selected items kinds"},

            {"GrindSettings_Headline",          "—————— Settings for Grinding ——————"},
            {"GrindUseGrindColor",              "Use Grind Color"},
            {"GrindUseGrindColor_Tooltip",      "When checked, the system will grind blocks with the color defined further down."},
            {"GrindJanitorEnemy",               "Janitor grinds enemy blocks"},
            {"GrindJanitorEnemy_Tooltip",       "When checked, enemy blocks in range will be grinded."},
            {"GrindJanitorNotOwned",            "Janitor grinds not owned blocks"},
            {"GrindJanitorNotOwned_Tooltip",    "When checked, blocks without owner in range will be grinded."},
            {"GrindJanitorNeutrals",            "Janitor grinds neutral blocks"},
            {"GrindJanitorNeutrals_Tooltip",    "When checked, the system will grind also blocks owned by neutrals (factions not at war)."},
            {"GrindJanitorDisableOnly",         "Janitor grind to disable only"},
            {"GrindJanitorDisableOnly_Tooltip", "When checked, only functional blocks are grinded and these only until they stop working."},
            {"GrindJanitorHackOnly",            "Janitor grind to hack only"},
            {"GrindJanitorHackOnly_Tooltip",    "When checked, only functional blocks are grinded and these only until they could be hacked."},
            {"GrindPriority",                   "Grind Priority"},
            {"GrindPriority_Tooltip",           "Enable/Disable grinding of selected items kinds and set the priority while grinding\n(If grinded by grind color the priority and release status is ignored)"},
            {"GrindOrderNearest",               "Nearest First"},
            {"GrindOrderNearest_Tooltip",       "When checked, if blocks have the same priority, the nearest is grinded first."},
            {"GrindOrderFarthest",              "Farthest first"},
            {"GrindOrderFarthest_Tooltip",      "When checked, if blocks have the same priority, the farthest is grinded first."},
            {"GrindOrderSmallest",              "Smallest grid first"},
            {"GrindOrderSmallest_Tooltip",      "When checked, if blocks have the same priority, the smallest grid is grinded first."},
            {"GrindIgnorePriority",             "Ignore priority order"},
            {"GrindIgnorePriority_Tooltip",     "When checked, the priority order is ignored and blocks are grinded by distance only. Enabled/disabled state of block types is still respected."},

            {"CollectSettings_Headline",        "—————— Settings for Collecting ——————"},
            {"CollectPriority",                 "Collect Priority"},
            {"CollectPriority_Tooltip",         "Enable/Disable collecting of selected items kind"},
            {"CollectOnlyIfIdle",               "Collect only if idle"},
            {"CollectOnlyIfIdle_Tooltip",       "if set collecting floating objects is done only if no welding/grinding is needed."},
            {"CollectPushOre",                  "Push ingot/ore immediately"},
            {"CollectPushOre_Tooltip",          "When checked, the system will push ingot/ore immediately into connected container."},
            {"CollectPushItems",                "Push items immediately"},
            {"CollectPushItems_Tooltip",        "When checked, the system will push items (tools,weapons,ammo,bottles, ..) immediately into connected container."},
            {"CollectPushComp",                 "Push components immediately"},
            {"CollectPushComp_Tooltip",         "When checked, the system will push components immediately into connected container."},

            {"Priority_Enable",                 "Enable"},
            {"Priority_Disable",                "Disable"},
            {"Priority_Up",                     "Priority Up"},
            {"Priority_Down",                   "Priority Down"},

            {"Color_PickCurrentColor",          "Pick current build color"},
            {"Color_SetCurrentColor",           "Set current build color"},

            {"AreaShow",                        "Show Area"},
            {"AreaShow_Tooltip",                "When checked, it will show you the area this system covers"},
            {"AreaWidth",                       "Area Width"},
            {"AreaHeight",                      "Area Height"},
            {"AreaDepth",                       "Area Depth"},
            {"RemoteCtrlBy",                    "Remote controlled by"},
            {"RemoteCtrlBy_Tooltip",            "Select if center of working area should follow a character. (As long as he is inside the maximum range)"},
            {"RemoteCtrlBy_None",               "-None-"},
            {"RemoteCtrlShowArea",              "Control Show Area"},
            {"RemoteCtrlShowArea_Tooltip",      "Select if 'Show area' is active as long as character is equipped with hand welder/grinder"},
            {"RemoteCtrlWorking",               "Control Working"},
            {"RemoteCtrlWorking_Tooltip",       "Select if drill is only switched on as long as character is equipped with hand welder/grinder"},
            {"SoundVolume",                     "Sound Volume"},
            {"ScriptControlled",                "Controlled by Script"},
            {"ScriptControlled_Tooltip",        "When checked, the system will not build/repair blocks automatically. Each block has to be picked by calling scripting functions."},
            {"Info_CurentWeldEntity",           "Picked Welding Block:"},
            {"Info_CurentGrindEntity",          "Picked Grinding Block:"},
            {"Info_InventoryFull",              "Block inventory is full!"},
            {"Info_LimitReached",               "PCU limit reached!"},
            {"Info_DisabledByRemote",           "Disabled by remote control!"},
            {"Info_BlocksToBuild",              "Blocks to build:"},
            {"Info_BlocksToGrind",              "Blocks to dismantle:"},
            {"Info_ItemsToCollect",             "Floatings to collect:"},
            {"Info_More",                       " -.."},
            {"Info_MissingItems",               "Missing items:"},
            {"Info_BlockSwitchedOff",           "Block is switched off"},
            {"Info_BlockDamaged",               "Block is damaged / incomplete"},
            {"Info_BlockUnpowered",             "Block has not enough power"},
            {"Cmd_HelpClient",                  "Version: {0}" +
                                                "\nAvailable commands:" +
                                                "\n[{1};{2}]: Shows this info" +
                                                "\n[{3} {4};{5}]: Set the current logging level. Warning: Setting level to '{4}' could produce very large log-files" +
                                                "\n[{6} {7}]: Export the current translations for the selected language into a file located in {8}"},
            {"Cmd_HelpServer",                  "\n[{0}]: Creates a settings file inside your current world folder. After restart the settings in this file will be used, instead of the global mod-settings file." +
                                                "\n[{1}]: Creates a global settings file inside mod folder (including all options)."}
        };
    }
}