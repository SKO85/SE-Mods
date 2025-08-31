namespace SKONanobotBuildAndRepairSystem
{
    using System.Collections.Generic;
    using Sandbox.ModAPI;
    using VRage;
    using VRage.Utils;

    public static class Texts
    {
        public static readonly MyStringId ModeSettings_Headline;
        public static readonly MyStringId SearchMode;
        public static readonly MyStringId SearchMode_Tooltip;
        public static readonly MyStringId SearchMode_Walk;
        public static readonly MyStringId SearchMode_Fly;

        public static readonly MyStringId WorkMode;
        public static readonly MyStringId WorkMode_Tooltip;
        public static readonly MyStringId WorkMode_WeldB4Grind;
        public static readonly MyStringId WorkMode_GrindB4Weld;
        public static readonly MyStringId WorkMode_GrindIfWeldStuck;
        public static readonly MyStringId WorkMode_WeldOnly;
        public static readonly MyStringId WorkMode_GrindOnly;

        public static readonly MyStringId WeldSettings_Headline;
        public static readonly MyStringId WeldUseIgnoreColor;
        public static readonly MyStringId WeldUseIgnoreColor_Tooltip;
        public static readonly MyStringId WeldBuildNew;
        public static readonly MyStringId WeldBuildNew_Tooltip;
        public static readonly MyStringId WeldToFuncOnly;
        public static readonly MyStringId WeldToFuncOnly_Tooltip;
        public static readonly MyStringId WeldPriority;
        public static readonly MyStringId WeldPriority_Tooltip;

        public static readonly MyStringId GrindSettings_Headline;
        public static readonly MyStringId GrindHeadline_Tooltip;
        public static readonly MyStringId GrindUseGrindColor;
        public static readonly MyStringId GrindUseGrindColor_Tooltip;
        public static readonly MyStringId GrindJanitorEnemy;
        public static readonly MyStringId GrindJanitorEnemy_Tooltip;
        public static readonly MyStringId GrindJanitorNotOwned;
        public static readonly MyStringId GrindJanitorNotOwned_Tooltip;
        public static readonly MyStringId GrindJanitorNeutrals;
        public static readonly MyStringId GrindJanitorNeutrals_Tooltip;
        public static readonly MyStringId GrindJanitorDisableOnly;
        public static readonly MyStringId GrindJanitorDisableOnly_Tooltip;
        public static readonly MyStringId GrindJanitorHackOnly;
        public static readonly MyStringId GrindJanitorHackOnly_Tooltip;
        public static readonly MyStringId GrindPriority;
        public static readonly MyStringId GrindPriority_Tooltip;
        public static readonly MyStringId GrindOrderNearest;
        public static readonly MyStringId GrindOrderNearest_Tooltip;
        public static readonly MyStringId GrindOrderFurthest;
        public static readonly MyStringId GrindOrderFurthest_Tooltip;
        public static readonly MyStringId GrindOrderSmallest;
        public static readonly MyStringId GrindOrderSmallest_Tooltip;

        public static readonly MyStringId CollectSettings_Headline;
        public static readonly MyStringId CollectPriority;
        public static readonly MyStringId CollectPriority_Tooltip;
        public static readonly MyStringId CollectOnlyIfIdle;
        public static readonly MyStringId CollectOnlyIfIdle_Tooltip;
        public static readonly MyStringId CollectPushOre;
        public static readonly MyStringId CollectPushOre_Tooltip;
        public static readonly MyStringId CollectPushItems;
        public static readonly MyStringId CollectPushItems_Tooltip;
        public static readonly MyStringId CollectPushComp;
        public static readonly MyStringId CollectPushComp_Tooltip;

        public static readonly MyStringId Color_PickCurrentColor;
        public static readonly MyStringId Color_SetCurrentColor;

        public static readonly MyStringId Priority_Enable;
        public static readonly MyStringId Priority_Disable;
        public static readonly MyStringId Priority_Up;
        public static readonly MyStringId Priority_Down;

        public static readonly MyStringId AreaShow;
        public static readonly MyStringId AreaShow_Tooltip;

        public static readonly MyStringId AreaWidth;
        public static readonly MyStringId AreaHeight;
        public static readonly MyStringId AreaDepth;

        //public readonly static MyStringId RemoteCtrlBy;
        //public readonly static MyStringId RemoteCtrlBy_Tooltip;
        //public readonly static MyStringId RemoteCtrlBy_None;
        //public readonly static MyStringId RemoteCtrlShowArea;
        //public readonly static MyStringId RemoteCtrlShowArea_Tooltip;
        //public readonly static MyStringId RemoteCtrlWorking;
        //public readonly static MyStringId RemoteCtrlWorking_Tooltip;

        public static readonly MyStringId SoundVolume;
        public static readonly MyStringId ScriptControlled;
        public static readonly MyStringId ScriptControlled_Tooltip;

        public static readonly MyStringId Info_CurentWeldEntity;
        public static readonly MyStringId Info_CurentGrindEntity;
        public static readonly MyStringId Info_InventoryFull;
        public static readonly MyStringId Info_LimitReached;

        //public readonly static MyStringId Info_DisabledByRemote;
        public static readonly MyStringId Info_BlocksToBuild;

        public static readonly MyStringId Info_BlocksToGrind;
        public static readonly MyStringId Info_ItemsToCollect;
        public static readonly MyStringId Info_More;
        public static readonly MyStringId Info_MissingItems;
        public static readonly MyStringId Info_BlockSwitchedOff;
        public static readonly MyStringId Info_BlockDamaged;
        public static readonly MyStringId Info_BlockUnpowered;

        public static readonly MyStringId Cmd_HelpClient;
        public static readonly MyStringId Cmd_HelpServer;

        static Texts()
        {
            var language = Constants.DisableLocalization ? MyLanguagesEnum.English : MyAPIGateway.Session.Config.Language;
            Logging.Instance?.Write(Logging.Level.Error, "Localization: Disabled={0} Language={1}", Constants.DisableLocalization, language);

            var texts = LocalizationHelper.GetTexts(language, GetDictionaries(), Logging.Instance);
            ModeSettings_Headline = LocalizationHelper.GetStringId(texts, "ModeSettings_Headline");
            SearchMode = LocalizationHelper.GetStringId(texts, "SearchMode");
            SearchMode_Tooltip = LocalizationHelper.GetStringId(texts, "SearchMode_Tooltip");
            SearchMode_Walk = LocalizationHelper.GetStringId(texts, "SearchMode_Walk");
            SearchMode_Fly = LocalizationHelper.GetStringId(texts, "SearchMode_Fly");

            WorkMode = LocalizationHelper.GetStringId(texts, "WorkMode");
            WorkMode_Tooltip = LocalizationHelper.GetStringId(texts, "WorkMode_Tooltip");
            WorkMode_WeldB4Grind = LocalizationHelper.GetStringId(texts, "WorkMode_WeldB4Grind");
            WorkMode_GrindB4Weld = LocalizationHelper.GetStringId(texts, "WorkMode_GrindB4Weld");
            WorkMode_GrindIfWeldStuck = LocalizationHelper.GetStringId(texts, "WorkMode_GrindIfWeldStuck");
            WorkMode_WeldOnly = LocalizationHelper.GetStringId(texts, "WorkMode_WeldOnly");
            WorkMode_GrindOnly = LocalizationHelper.GetStringId(texts, "WorkMode_GrindOnly");

            WeldSettings_Headline = LocalizationHelper.GetStringId(texts, "WeldSettings_Headline");
            WeldUseIgnoreColor = LocalizationHelper.GetStringId(texts, "WeldUseIgnoreColor");
            WeldUseIgnoreColor_Tooltip = LocalizationHelper.GetStringId(texts, "WeldUseIgnoreColor_Tooltip");
            WeldBuildNew = LocalizationHelper.GetStringId(texts, "WeldBuildNew");
            WeldBuildNew_Tooltip = LocalizationHelper.GetStringId(texts, "WeldBuildNew_Tooltip");
            WeldToFuncOnly = LocalizationHelper.GetStringId(texts, "WeldToFuncOnly");
            WeldToFuncOnly_Tooltip = LocalizationHelper.GetStringId(texts, "WeldToFuncOnly_Tooltip");
            WeldPriority = LocalizationHelper.GetStringId(texts, "WeldPriority");
            WeldPriority_Tooltip = LocalizationHelper.GetStringId(texts, "WeldPriority_Tooltip");

            GrindSettings_Headline = LocalizationHelper.GetStringId(texts, "GrindSettings_Headline");
            GrindUseGrindColor = LocalizationHelper.GetStringId(texts, "GrindUseGrindColor");
            GrindUseGrindColor_Tooltip = LocalizationHelper.GetStringId(texts, "GrindUseGrindColor_Tooltip");

            GrindJanitorEnemy = LocalizationHelper.GetStringId(texts, "GrindJanitorEnemy");
            GrindJanitorEnemy_Tooltip = LocalizationHelper.GetStringId(texts, "GrindJanitorEnemy_Tooltip");
            GrindJanitorNotOwned = LocalizationHelper.GetStringId(texts, "GrindJanitorNotOwned");
            GrindJanitorNotOwned_Tooltip = LocalizationHelper.GetStringId(texts, "GrindJanitorNotOwned_Tooltip");
            GrindJanitorNeutrals = LocalizationHelper.GetStringId(texts, "GrindJanitorNeutrals");
            GrindJanitorNeutrals_Tooltip = LocalizationHelper.GetStringId(texts, "GrindJanitorNeutrals_Tooltip");
            GrindJanitorDisableOnly = LocalizationHelper.GetStringId(texts, "GrindJanitorDisableOnly");
            GrindJanitorDisableOnly_Tooltip = LocalizationHelper.GetStringId(texts, "GrindJanitorDisableOnly_Tooltip");
            GrindJanitorHackOnly = LocalizationHelper.GetStringId(texts, "GrindJanitorHackOnly");
            GrindJanitorHackOnly_Tooltip = LocalizationHelper.GetStringId(texts, "GrindJanitorHackOnly_Tooltip");

            GrindPriority = LocalizationHelper.GetStringId(texts, "GrindPriority");
            GrindPriority_Tooltip = LocalizationHelper.GetStringId(texts, "GrindPriority_Tooltip");

            GrindOrderNearest = LocalizationHelper.GetStringId(texts, "GrindOrderNearest");
            GrindOrderNearest_Tooltip = LocalizationHelper.GetStringId(texts, "GrindOrderNearest_Tooltip");
            GrindOrderFurthest = LocalizationHelper.GetStringId(texts, "GrindOrderFurthest");
            GrindOrderFurthest_Tooltip = LocalizationHelper.GetStringId(texts, "GrindOrderFurthest_Tooltip");
            GrindOrderSmallest = LocalizationHelper.GetStringId(texts, "GrindOrderSmallest");
            GrindOrderSmallest_Tooltip = LocalizationHelper.GetStringId(texts, "GrindOrderSmallest_Tooltip");

            CollectSettings_Headline = LocalizationHelper.GetStringId(texts, "CollectSettings_Headline");
            CollectPriority = LocalizationHelper.GetStringId(texts, "CollectPriority");
            CollectPriority_Tooltip = LocalizationHelper.GetStringId(texts, "CollectPriority_Tooltip");
            CollectOnlyIfIdle = LocalizationHelper.GetStringId(texts, "CollectOnlyIfIdle");
            CollectOnlyIfIdle_Tooltip = LocalizationHelper.GetStringId(texts, "CollectOnlyIfIdle_Tooltip");
            CollectPushOre = LocalizationHelper.GetStringId(texts, "CollectPushOre");
            CollectPushOre_Tooltip = LocalizationHelper.GetStringId(texts, "CollectPushOre_Tooltip");
            CollectPushItems = LocalizationHelper.GetStringId(texts, "CollectPushItems");
            CollectPushItems_Tooltip = LocalizationHelper.GetStringId(texts, "CollectPushItems_Tooltip");
            CollectPushComp = LocalizationHelper.GetStringId(texts, "CollectPushComp");
            CollectPushComp_Tooltip = LocalizationHelper.GetStringId(texts, "CollectPushComp_Tooltip");

            Color_PickCurrentColor = LocalizationHelper.GetStringId(texts, "Color_PickCurrentColor");
            Color_SetCurrentColor = LocalizationHelper.GetStringId(texts, "Color_SetCurrentColor");

            Priority_Enable = LocalizationHelper.GetStringId(texts, "Priority_Enable");
            Priority_Disable = LocalizationHelper.GetStringId(texts, "Priority_Disable");
            Priority_Up = LocalizationHelper.GetStringId(texts, "Priority_Up");
            Priority_Down = LocalizationHelper.GetStringId(texts, "Priority_Down");

            AreaShow = LocalizationHelper.GetStringId(texts, "AreaShow");
            AreaShow_Tooltip = LocalizationHelper.GetStringId(texts, "AreaShow_Tooltip");
            AreaWidth = LocalizationHelper.GetStringId(texts, "AreaWidth");
            AreaHeight = LocalizationHelper.GetStringId(texts, "AreaHeight");
            AreaDepth = LocalizationHelper.GetStringId(texts, "AreaDepth");

            //RemoteCtrlBy = LocalizationHelper.GetStringId(texts, "RemoteCtrlBy");
            //RemoteCtrlBy_Tooltip = LocalizationHelper.GetStringId(texts, "RemoteCtrlBy_Tooltip");
            //RemoteCtrlBy_None = LocalizationHelper.GetStringId(texts, "RemoteCtrlBy_None");

            //RemoteCtrlShowArea = LocalizationHelper.GetStringId(texts, "RemoteCtrlShowArea");
            //RemoteCtrlShowArea_Tooltip = LocalizationHelper.GetStringId(texts, "RemoteCtrlShowArea_Tooltip");
            //RemoteCtrlWorking = LocalizationHelper.GetStringId(texts, "RemoteCtrlWorking");
            //RemoteCtrlWorking_Tooltip = LocalizationHelper.GetStringId(texts, "RemoteCtrlWorking_Tooltip");

            SoundVolume = LocalizationHelper.GetStringId(texts, "SoundVolume");
            ScriptControlled = LocalizationHelper.GetStringId(texts, "ScriptControlled");
            ScriptControlled_Tooltip = LocalizationHelper.GetStringId(texts, "ScriptControlled_Tooltip");

            Info_CurentWeldEntity = LocalizationHelper.GetStringId(texts, "Info_CurentWeldEntity");
            Info_CurentGrindEntity = LocalizationHelper.GetStringId(texts, "Info_CurentGrindEntity");
            Info_InventoryFull = LocalizationHelper.GetStringId(texts, "Info_InventoryFull");
            Info_LimitReached = LocalizationHelper.GetStringId(texts, "Info_LimitReached");
            //Info_DisabledByRemote = LocalizationHelper.GetStringId(texts, "Info_DisabledByRemote");
            Info_BlocksToBuild = LocalizationHelper.GetStringId(texts, "Info_BlocksToBuild");
            Info_BlocksToGrind = LocalizationHelper.GetStringId(texts, "Info_BlocksToGrind");
            Info_ItemsToCollect = LocalizationHelper.GetStringId(texts, "Info_ItemsToCollect");
            Info_More = LocalizationHelper.GetStringId(texts, "Info_More");
            Info_MissingItems = LocalizationHelper.GetStringId(texts, "Info_MissingItems");
            Info_BlockSwitchedOff = LocalizationHelper.GetStringId(texts, "Info_BlockSwitchedOff");
            Info_BlockDamaged = LocalizationHelper.GetStringId(texts, "Info_BlockDamaged");
            Info_BlockUnpowered = LocalizationHelper.GetStringId(texts, "Info_BlockUnpowered");

            Cmd_HelpClient = LocalizationHelper.GetStringId(texts, "Cmd_HelpClient");
            Cmd_HelpServer = LocalizationHelper.GetStringId(texts, "Cmd_HelpServer");
        }

        public static Dictionary<string, string> GetDictionary(MyLanguagesEnum language)
        {
            return LocalizationHelper.GetTexts(language, GetDictionaries(), null);
        }

        private static Dictionary<MyLanguagesEnum, Dictionary<string, string>> GetDictionaries()
        {
            var dicts = new Dictionary<MyLanguagesEnum, Dictionary<string, string>>
         {
            { MyLanguagesEnum.English, new Dictionary<string, string>
               {
                  {"ModeSettings_Headline",           "———————Mode Settings———————"},
                  {"SearchMode",                      "Mode"},
                  {"SearchMode_Tooltip",              "Select how the nanobots search and reach their targets."},
                  {"SearchMode_Walk",                 "Walk mode"},
                  {"SearchMode_Fly",                  "Fly mode"},
                  {"WorkMode",                        "WorkMode"},
                  {"WorkMode_Tooltip",                "Select how the nanobots decide what to do (weld or grind)."},
                  {"WorkMode_WeldB4Grind",            "Weld before grind"},
                  {"WorkMode_GrindB4Weld",            "Grind before weld"},
                  {"WorkMode_GrindIfWeldStuck",       "Grind if weld get stuck"},
                  {"WorkMode_WeldOnly",               "Welding only"},
                  {"WorkMode_GrindOnly",              "Grinding only"},
                  {"WeldSettings_Headline",           "———————Settings for Welding———————"},
                  {"WeldUseIgnoreColor",              "Use Ignore Color"},
                  {"WeldUseIgnoreColor_Tooltip",      "When checked, the system will ignore blocks with the color defined further down."},
                  {"WeldBuildNew",                    "Build new"},
                  {"WeldBuildNew_Tooltip",            "When checked, the System will also construct projected blocks."},
                  {"WeldToFuncOnly",                  "Weld to functional only"},
                  {"WeldToFuncOnly_Tooltip",          "When checked, bock only welded to functional state."},
                  {"WeldPriority",                    "Welding Priority"},
                  {"WeldPriority_Tooltip",            "Enable/Disable build-repair of selected items kinds"},

                  {"GrindSettings_Headline",          "———————Settings for Grinding———————"},
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
                  {"GrindOrderFurthest",              "Furthest first"},
                  {"GrindOrderFurthest_Tooltip",      "When checked, if blocks have the same priority, the furthest is grinded first."},
                  {"GrindOrderSmallest",              "Smallest grid first"},
                  {"GrindOrderSmallest_Tooltip",      "When checked, if blocks have the same priority, the smallest grid is grinded first."},

                  {"CollectSettings_Headline",        "———————Settings for Collecting———————"},
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
               }
            },
            { MyLanguagesEnum.Polish, new Dictionary<string, string>
               {
                  {"ModeSettings_Headline",           "——————Ustawienia Trybu——————"},
                  {"SearchMode",                      "Tryb"},
                  {"SearchMode_Tooltip",              "Wybierz, jak nanoboty mają szukać i docierać do celów."},
                  {"SearchMode_Walk",                 "Tryb chodzenia"},
                  {"SearchMode_Fly",                  "Tryb latania"},
                  {"WorkMode",                        "Tryb pracy"},
                  {"WorkMode_Tooltip",                "Wybierz, jak nanoboty mają decydować co robić (spawać czy rozbierać)."},
                  {"WorkMode_WeldB4Grind",            "Spawanie przed rozbiórką"},
                  {"WorkMode_GrindB4Weld",            "Rozbiórka przed spawaniem"},
                  {"WorkMode_GrindIfWeldStuck",       "Rozbiórka gdy nie ma spawania"},
                  {"WorkMode_WeldOnly",               "Tylko spawanie"},
                  {"WorkMode_GrindOnly",              "Tylko rozbiórka"},
                  {"WeldSettings_Headline",           "—————Ustawienia Spawania—————"},
                  {"WeldUseIgnoreColor",              "Użyj ignorowanie koloru"},
                  {"WeldUseIgnoreColor_Tooltip",      "Po zaznaczeniu, system zignoruje bloki w określonym kolorze."},
                  {"WeldBuildNew",                    "Buduj nowe"},
                  {"WeldBuildNew_Tooltip",            "Po zaznaczeniu, system będzie również budował bloki z projekcji."},
                  {"WeldToFuncOnly",                  "Spawaj tylko do funkcjonalności"},
                  {"WeldToFuncOnly_Tooltip",          "Po zaznaczeniu, bloki będą spawane tylko do stanu funkcjonalnego."},
                  {"WeldPriority",                    "Priorytet spawania"},
                  {"WeldPriority_Tooltip",            "Włącz/Wyłącz naprawę/budowę wybranych typów przedmiotów"},

                  {"GrindSettings_Headline",          "—————Ustawienia Rozbiórki—————"},
                  {"GrindUseGrindColor",              "Użyj koloru rozbiórki"},
                  {"GrindUseGrindColor_Tooltip",      "Po zaznaczeniu, system będzie rozbierał bloki w określonym kolorze."},
                  {"GrindJanitorEnemy",               "Rozbiera wrogie bloki"},
                  {"GrindJanitorEnemy_Tooltip",       "Po zaznaczeniu, wrogie bloki w zasięgu będą rozbierane."},
                  {"GrindJanitorNotOwned",            "Rozbiera nieprzypisane bloki"},
                  {"GrindJanitorNotOwned_Tooltip",    "Po zaznaczeniu, bloki bez właściciela w zasięgu będą rozbierane."},
                  {"GrindJanitorNeutrals",            "Rozbiera neutralne bloki"},
                  {"GrindJanitorNeutrals_Tooltip",    "Po zaznaczeniu, system będzie rozbierał również bloki należące do neutralnych frakcji (niebędących w stanie wojny)."},
                  {"GrindJanitorDisableOnly",         "Rozbiera tylko do wyłączenia"},
                  {"GrindJanitorDisableOnly_Tooltip", "Po zaznaczeniu, tylko funkcjonalne bloki będą rozbierane do momentu, aż przestaną działać."},
                  {"GrindJanitorHackOnly",            "Rozbiera tylko do przejęcia"},
                  {"GrindJanitorHackOnly_Tooltip",    "Po zaznaczeniu, tylko funkcjonalne bloki będą rozbierane do momentu, aż będzie można je zhakować."},
                  {"GrindPriority",                   "Priorytet rozbiórki"},
                  {"GrindPriority_Tooltip",           "Włącz/Wyłącz rozbiórkę wybranych przedmiotów i ustaw ich priorytet\n(Jeśli rozbiórka według koloru – priorytet i status są ignorowane)"},
                  {"GrindOrderNearest",               "Najpierw najbliższe"},
                  {"GrindOrderNearest_Tooltip",       "Po zaznaczeniu, jeśli bloki mają ten sam priorytet, najbliższy będzie rozbierany pierwszy."},
                  {"GrindOrderFurthest",              "Najpierw najdalsze"},
                  {"GrindOrderFurthest_Tooltip",      "Po zaznaczeniu, jeśli bloki mają ten sam priorytet, najdalszy będzie rozbierany pierwszy."},
                  {"GrindOrderSmallest",              "Najpierw najmniejsza siatka"},
                  {"GrindOrderSmallest_Tooltip",      "Po zaznaczeniu, jeśli bloki mają ten sam priorytet, najmniejsza siatka będzie rozbierana jako pierwsza."},

                  {"CollectSettings_Headline",        "———————Ustawienia Zbierania———————"},
                  {"CollectPriority",                 "Priorytet zbierania"},
                  {"CollectPriority_Tooltip",         "Włącz/Wyłącz zbieranie wybranych typów przedmiotów"},
                  {"CollectOnlyIfIdle",               "Zbieraj gdy bezczynny"},
                  {"CollectOnlyIfIdle_Tooltip",       "Jeśli ustawione, zbieranie przedmiotów odbywa się tylko, gdy nie ma potrzeby spawania/rozbierania."},
                  {"CollectPushOre",                  "Wpierw zbierz rudy/sztabki"},
                  {"CollectPushOre_Tooltip",          "Po zaznaczeniu, system natychmiast przesyła rudy/sztabki do pojemnika."},
                  {"CollectPushItems",                "Wpierw zbierz przedmioty"},
                  {"CollectPushItems_Tooltip",        "Po zaznaczeniu, system natychmiast przesyła przedmioty (narzędzia, broń, amunicję, butle itd.) do pojemnika."},
                  {"CollectPushComp",                 "Wpierw zbierz komponenty"},
                  {"CollectPushComp_Tooltip",         "Po zaznaczeniu, system natychmiast przesyła komponenty do pojemnika."},

                  {"Priority_Enable",                 "Wł"},
                  {"Priority_Disable",                "Wył"},
                  {"Priority_Up",                     "Zwiększ priorytet"},
                  {"Priority_Down",                   "Obniż priorytet"},

                  {"Color_PickCurrentColor",          "Pobierz bieżący kolor"},
                  {"Color_SetCurrentColor",           "Ustaw bieżący kolor"},

                  {"AreaShow",                        "Pokaż obszar"},
                  {"AreaShow_Tooltip",                "Po zaznaczeniu, pokaże obszar działania systemu"},
                  {"AreaWidth",                       "Szerokość obszaru"},
                  {"AreaHeight",                      "Wysokość obszaru"},
                  {"AreaDepth",                       "Głębokość obszaru"},
                  {"RemoteCtrlBy",                    "Zdalnie sterowany przez"},
                  {"RemoteCtrlBy_Tooltip",            "Wybierz, czy środek obszaru roboczego ma podążać za postacią (jeśli jest w zasięgu)."},
                  {"RemoteCtrlBy_None",               "-Brak-"},
                  {"RemoteCtrlShowArea",              "Kontroluj widoczny obszar"},
                  {"RemoteCtrlShowArea_Tooltip",      "Wybierz, czy 'pokaż obszar' działa tylko gdy postać ma wyposażony ręczny spawacz/rozbieracz."},
                  {"RemoteCtrlWorking",               "Zdalne działanie"},
                  {"RemoteCtrlWorking_Tooltip",       "Select if drill is only switched on as long as character is equipped with hand welder/grinder"},
                  {"SoundVolume",                     "Głośność efektow pracy"},
                  {"ScriptControlled",                "Kontrolowany przez skrypt"},
                  {"ScriptControlled_Tooltip",        "Po zaznaczeniu, system nie będzie automatycznie budował/naprawiał bloków. Każdy blok musi być wybrany przez skrypt."},
                  {"Info_CurentWeldEntity",           "Wybrany blok do spawania:"},
                  {"Info_CurentGrindEntity",          "Wybrany blok do rozbiórki:"},
                  {"Info_InventoryFull",              "Ekwipunek magazynu jest pełny!"},
                  {"Info_LimitReached",               "Osiągnięto limit PCU!"},
                  {"Info_DisabledByRemote",           "Wyłączone przez zdalne sterowanie!"},
                  {"Info_BlocksToBuild",              "Bloki do zbudowania:"},
                  {"Info_BlocksToGrind",              "Bloki do rozbiórki:"},
                  {"Info_ItemsToCollect",             "Unoszące się obiekty do zebrania:"},
                  {"Info_More",                       " -.."},
                  {"Info_MissingItems",               "Brakujące przedmioty:"},
                  {"Info_BlockSwitchedOff",           "Blok jest wyłączony"},
                  {"Info_BlockDamaged",               "Blok jest uszkodzony / niekompletny"},
                  {"Info_BlockUnpowered",             "Blok nie ma wystarczającej ilości energii"},
                  {"Cmd_HelpClient",                  "Wersja: {0}" +
                                                      "\nDostępne komendy:" +
                                                      "\n[{1};{2}]: Wyświetla te informacje" +
                                                      "\n[{3} {4};{5}]: Ustawia aktualny poziom logów. Uwaga: ustawienie poziomu na '{4}' może spowodować powstanie bardzo dużych plików logów" +
                                                      "\n[{6} {7}]: Eksportuje aktualne tłumaczenia dla wybranego języka do pliku znajdującego się w {8}"},
                  {"Cmd_HelpServer",                  "\n[{0}]: Tworzy plik konfiguracyjny wewnątrz bieżącego folderu świata. Po restarcie będą używane ustawienia z tego pliku zamiast globalnego pliku ustawień moda." +
                                                      "\n[{1}]: Tworzy globalny plik ustawień w folderze moda (zawierający wszystkie opcje)."}
               }
            },
            { MyLanguagesEnum.German,  new Dictionary<string, string>
               {
                  {"ModeSettings_Headline",           "—— Moduseinstellungen ——"},
                  {"SearchMode",                      "Mode"},
                  {"SearchMode_Tooltip",              "Wählen Sie aus, wie die Nanobots ihre Ziele suchen und erreichen."},
                  {"SearchMode_Walk",                 "Laufmodus"},
                  {"SearchMode_Fly",                  "Flugmodus"},
                  {"WorkMode",                        "Arbeitsmodus"},
                  {"WorkMode_Tooltip",                "Wählen Sie aus, wie die Nanobots entscheiden was zu tun ist (Schweißen oder Demontieren)."},
                  {"WorkMode_WeldB4Grind",            "Schweißen vor Demontieren"},
                  {"WorkMode_GrindB4Weld",            "Demontieren vor Schweißen"},
                  {"WorkMode_GrindIfWeldStuck",       "Demontieren wenn Schweißen blockiert ist"},
                  {"WorkMode_WeldOnly",               "Nur Schweißen"},
                  {"WorkMode_GrindOnly",              "Nur Demontieren"},
                  {"WeldSettings_Headline",           "——Einstellungen fürs Schweißen——"},
                  {"WeldUseIgnoreColor",              "Ignorierfarbe verwenden"},
                  {"WeldUseIgnoreColor_Tooltip",      "Wenn diese Option markiert ist, wird das System alle Blöcke, die die weiter unten definierte Farbe besitzen, ignorieren (nicht fertig schweißen)."},
                  {"WeldBuildNew",                    "Neue Blöcke erzeugen"},
                  {"WeldBuildNew_Tooltip",            "Wenn diese Option markiert ist, wird das System auch projizierte Blöcke erzeugen und schweißen."},
                  {"WeldToFuncOnly",                  "Nur bis Funktionsstufe schweißen"},
                  {"WeldToFuncOnly_Tooltip",          "Wenn diese Option markiert ist, werden Blöcke nur bis zur der Stufe geschweißt in der sie bereits arbeiten können."},
                  {"WeldPriority",                    "Schweiß Priorität"},
                  {"WeldPriority_Tooltip",            "Schaltet das Erzeugen/Reparieren der selektierten Typen von Blöcken ein/aus"},

                  {"GrindSettings_Headline",          "——Einstellungen fürs Demontieren——"},
                  {"GrindUseGrindColor",              "Demontierfarbe verwenden"},
                  {"GrindUseGrindColor_Tooltip",      "Wenn diese Option markiert ist, wird das System alle Blöcke, die die weiter unten definierte Farbe besitzen, demontieren."},
                  {"GrindJanitorEnemy",               "Aufräumen: Feindliche Blöcke demontieren"},
                  {"GrindJanitorEnemy_Tooltip",       "Wenn diese Option markiert ist, wird das System alle feindlichen Blöcke in Reichweite demontieren."},
                  {"GrindJanitorNotOwned",            "Aufräumen: Blöcke ohne Besitzer demontieren"},
                  {"GrindJanitorNotOwned_Tooltip",    "Wenn diese Option markiert ist, wird das System alle Blöcke ohne Besitzer in Reichweite demontieren."},
                  {"GrindJanitorNeutrals",            "Aufräumen: Blöcke von neutralen Besitzern demontieren"},
                  {"GrindJanitorNeutrals_Tooltip",    "Wenn diese Option markiert ist, wird das System alle Blöcke die neutralen Besitzern (Fraktionen die sich nicht im Krieg befinden) gehören in Reichweite demontieren."},
                  {"GrindJanitorDisableOnly",         "Aufräumen: Demontieren nur bis funktionslos"},
                  {"GrindJanitorDisableOnly_Tooltip", "Wenn diese Option markiert ist, wird das System Blöcke nur solange demontieren bis sie ausser Funktion sind."},
                  {"GrindJanitorHackOnly",            "Aufräumen: Demontieren nur bis übernehmbar"},
                  {"GrindJanitorHackOnly_Tooltip",    "Wenn diese Option markiert ist, wird das System Blöcke nur solange demontieren bis sie übernehmbar (Hackbar) sind."},
                  {"GrindPriority",                   "Zerlege Priorität"},
                  {"GrindPriority_Tooltip",           "Schlaltet das Demontieren des selektierten Blocktypes ein/aus und legt die Priorität fest.\n(Wenn das Demontieren per festgelegter Farbe erfolgt, wird die Priorät und die Freigabe ignorierd)"},
                  {"GrindOrderNearest",               "Nächstgelegen zurerst"},
                  {"GrindOrderNearest_Tooltip",       "Wenn diese Option markiert ist und Blöcke die gleiche Priorität besitzen, wird der nächgelegen Block zuerst demontiert."},
                  {"GrindOrderFurthest",              "Enferntester zuerst"},
                  {"GrindOrderFurthest_Tooltip",      "Wenn diese Option markiert ist und Blöcke die gleiche Priorität besitzen, wird der enfernteste Block zuerst demontiert."},
                  {"GrindOrderSmallest",              "Kleinster Verbund zuerst"},
                  {"GrindOrderSmallest_Tooltip",      "Wenn diese Option markiert ist und Blöcke die gleiche Priorität besitzen, werden die Blöcke im kleinsten Verbund zuerst demontiert."},

                  {"CollectSettings_Headline",        "—— Einstellungen zum Sammeln ——————"},
                  {"CollectPriority",                 "Sammelpriorität"},
                  {"CollectPriority_Tooltip",         "Sammeln von Objekten ein/ausschalten"},
                  {"CollectOnlyIfIdle",               "Nur im Leerlauf sammeln"},
                  {"CollectOnlyIfIdle_Tooltip",       "Wenn das Sammeln von freien Objekten eingestellt ist, erfolgt dies nur, wenn kein Schweißen / Demontieren erforderlich ist."},
                  {"CollectPushOre",                  "Erze sofort auslagern"},
                  {"CollectPushOre_Tooltip",          "Wenn diese Option markiert ist, wird das Sytem sofort versuchen Erz in angschlosse Container auszulagern."},
                  {"CollectPushItems",                "Objekte sofort auslagern"},
                  {"CollectPushItems_Tooltip",        "Wenn diese Option markiert ist, wird das System sofort versuchen Objekte (Werkzeuge, Waffen, Munition, Flaschen, ..) in angschlosse Container auszulagern."},
                  {"CollectPushComp",                 "Komponenten sofort auslagern"},
                  {"CollectPushComp_Tooltip",         "Wenn diese Option markiert ist, wird das System sofort versuchen Komponenten in angschlosse Container auszulagern."},

                  {"Priority_Enable",                 "Aktivieren"},
                  {"Priority_Disable",                "Deaktivieren"},
                  {"Priority_Up",                     "Priorität hoch"},
                  {"Priority_Down",                   "Priorität runter"},

                  {"Color_PickCurrentColor",          "Aktuelle Farbe übernehmen"},
                  {"Color_SetCurrentColor",           "Aktuelle Farbe setzen"},

                  {"AreaShow",                        "Bereich anzeigen"},
                  {"AreaShow_Tooltip",                "Wenn diese Option aktiviert ist, wird der Bereich angezeigt, den dieses System abdeckt."},
                  {"AreaWidth",                       "Bereichsbreite"},
                  {"AreaHeight",                      "Bereichshöhe"},
                  {"AreaDepth",                       "Bereichstiefe"},
                  {"RemoteCtrlBy",                    "Ferngesteuert von"},
                  {"RemoteCtrlBy_Tooltip",            "Wählen Sie aus, ob die Mitte des Arbeitsbereichs einem Charakter folgen soll. (Solange er sich innerhalb der maximalen Reichweite befindet) "},
                  {"RemoteCtrlBy_None",               "-Keinem-"},
                  {"RemoteCtrlShowArea",              "Bereichsanzeige steuern"},
                  {"RemoteCtrlShowArea_Tooltip",      "Wählen Sie, ob 'Bereich anzeigen' aktiv ist, solange der Charakter mit einem Schweißgerät oder Winkelschleifer ausgestattet ist."},
                  {"RemoteCtrlWorking",               "Block ein/aus steuern"},
                  {"RemoteCtrlWorking_Tooltip",       "Wählen Sie, ob der Block nur eingeschaltet ist, solange der Charakter mit einem Schweißgerät oder Winkelschleifer ausgestattet ist."},
                  {"SoundVolume",                     "Lautstärke"},
                  {"ScriptControlled",                "Vom Skript gesteuert"},
                  {"ScriptControlled_Tooltip",        "Wenn diese Option aktiviert ist, bohrt / füllt das System nicht automatisch. Jede Aktion muss durch Aufrufen von Skriptfunktionen ausgewählt werden."},

                  {"Info_CurentWeldEntity",           "Aktuell geschweißter Block:"},
                  {"Info_CurentGrindEntity",          "Aktuell demontierter Block:"},
                  {"Info_InventoryFull",              "Blockinventar ist voll!"},
                  {"Info_LimitReached",               "PCU Limit erreicht!"},
                  {"Info_DisabledByRemote",           "Durch Fernbedienung deaktiviert!"},
                  {"Info_BlocksToBuild",              "Zu schweißende Blöcke:"},
                  {"Info_BlocksToGrind",              "Zu demontierende Blöcke:"},
                  {"Info_ItemsToCollect",             "Zu sammelnde Objekte:"},
                  {"Info_More",                       " -.."},
                  {"Info_MissingItems",               "Fehlenden Komponenten:"},
                  {"Info_BlockSwitchedOff",           "Block ist ausgeschaltet"},
                  {"Info_BlockDamaged",               "Block ist beschädigt / unvollständig"},
                  {"Info_BlockUnpowered",             "Block hat nicht genug Energie"},
                  {"Cmd_HelpClient",                  "Version: {0}" +
                                                      "\nVerfügbare Befehle:" +
                                                      "\n[{1}; {2}]: Zeigt diese Info an" +
                                                      "\n[{3} {4}; {5}]: Legen Sie die aktuelle Protokollierungsstufe fest. Warnung: Das Setzen der Stufe auf '{4}' kann zu sehr großen Protokolldateien führen." +
                                                      "\n[{6} {7}]: Exportiert die aktuelle Übersetzung für dei gewählte Sprache in eine Datei im Ordner: {8}"},
                  {"Cmd_HelpServer",                  "\n[{0}]: Erstellt eine Einstellungsdatei in Ihrem aktuellen Weltordner. Nach dem Neustart werden die Einstellungen in dieser Datei anstelle der globalen Mod - Einstellungsdatei verwendet."+
                                                      "\n[{1}]: Erstellt eine globale Einstellungsdatei im Mod-Ordner (einschließlich aller Optionen)."}
               }
            },
            { MyLanguagesEnum.Russian,  new Dictionary<string, string>
               {
                  {"ModeSettings_Headline",           "——————— Настройки режима ———————"},
                  {"SearchMode",                      "Режим"},
                  {"SearchMode_Tooltip",              "Выбор способа поиска целей наноботами"},
                  {"SearchMode_Walk",                 "Только эта сетка в поле"},
                  {"SearchMode_Fly",                  "Все сетки в поле"},
                  {"WorkMode",                        "Режим работы"},
                  {"WorkMode_Tooltip",                "Выберите, как наноботы решают, что делать (сварка или распиливание)."},
                  {"WorkMode_WeldB4Grind",            "Сварка затем распиливание"},
                  {"WorkMode_GrindB4Weld",            "Распиливание затем сварка"},
                  {"WorkMode_GrindIfWeldStuck",       "Распиливать, если не сваривает"},
                  {"WorkMode_WeldOnly",               "Только сварка"},
                  {"WorkMode_GrindOnly",              "Только распиливание"},
                  {"WeldSettings_Headline",           "——————— Настройки для сварки ———————"},
                  {"WeldUseIgnoreColor",              "Игнорировать цвет"},
                  {"WeldUseIgnoreColor_Tooltip",      "Когда установлен этот флажок, система будет\nигнорировать блоки с цветом, определенным ниже."},
                  {"WeldBuildNew",                    "Построить новый"},
                  {"WeldBuildNew_Tooltip",            "Если этот флажок установлен, система также будет\nсоздавать проецируемые блоки."},
                  {"WeldToFuncOnly",                  "Перестал быть активным"},
                  {"WeldToFuncOnly_Tooltip",          "Сваривать только когда блок перестал функционировать\nиз за повреждений/распиливания."},
                  {"WeldPriority",                    "Приоритет сварки"},
                  {"WeldPriority_Tooltip",            "Включить/выключить сборку-ремонт выбранных видов предметов."},

                  {"GrindSettings_Headline",          "——————— Настройки для распиливания ———————"},
                  {"GrindUseGrindColor",              "Цвет для распиливания"},
                  {"GrindUseGrindColor_Tooltip",      "Когда установлен этот флажок, система будет\nраспиливать блоки с цветом, определенным ниже."},
                  {"GrindJanitorEnemy",               "Распиливать вражеские блоки"},
                  {"GrindJanitorEnemy_Tooltip",       "Если вражеские блоки в радиусе действия то они будут распилены."},
                  {"GrindJanitorNotOwned",            "Распиливать блоки без владельца"},
                  {"GrindJanitorNotOwned_Tooltip",    "Распиливать блоки без владельца в радиусе действия."},
                  {"GrindJanitorNeutrals",            "Распиливать нейтральные блоки"},
                  {"GrindJanitorNeutrals_Tooltip",    "Распиливать блоки принадлежащие нейтралам (не воюющим фракциям)."},
                  {"GrindJanitorDisableOnly",         "Распиливать до отключения"},
                  {"GrindJanitorDisableOnly_Tooltip", "Если этот флажок установлен, распиливаются только\nфункционирующие блоки, и только до тех пор, пока они не перестанут работать."},
                  {"GrindJanitorHackOnly",            "Распиливать до взлома"},
                  {"GrindJanitorHackOnly_Tooltip",    "Если этот флажок установлен, распиливаются только\nфункционирующие блоки, и только до тех пор, пока они не будут взломаны."},
                  {"GrindPriority",                   "Приоритет распиливания"},
                  {"GrindPriority_Tooltip",           "Включить/выключить распиливание выбранных видов\nэлементов и установить приоритет во время распиливания (Если распиливать по цвету распиливания, приоритет и статус игнорируются)."},
                  {"GrindOrderNearest",               "Сначала ближайший блок"},
                  {"GrindOrderNearest_Tooltip",       "Если блоки имеют одинаковый приоритет, ближайший распиливается первым."},
                  {"GrindOrderFurthest",              "Сначала самый дальний блок"},
                  {"GrindOrderFurthest_Tooltip",      "Если блоки имеют одинаковый приоритет, самый\nдальний распиливается первым."},
                  {"GrindOrderSmallest",              "Сначала самый маленький блок"},
                  {"GrindOrderSmallest_Tooltip",      "Если блоки имеют одинаковый приоритет, вначале\nраспиливается самый маленький блок."},

                  {"CollectSettings_Headline",        "——————— Настройки для сбора ———————"},
                  {"CollectPriority",                 "Приоритет сбора"},
                  {"CollectPriority_Tooltip",         "Включить/отключить сбор выбранного вида предметов."},
                  {"CollectOnlyIfIdle",               "Только в режиме ожидания"},
                  {"CollectOnlyIfIdle_Tooltip",       "Сбор плавающих объектов выполняется только\nесли не выполняется сварка/распил"},
                  {"CollectPushOre",                  "Отправлять слитки/руду"},
                  {"CollectPushOre_Tooltip",          "Если этот флажок установлен, система будет\nсразу отправлять слитки/руду в подключенный контейнер."},
                  {"CollectPushItems",                "Отправить предметы"},
                  {"CollectPushItems_Tooltip",        "Если этот флажок установлен, система будетотправлять предметы\n(инструменты, оружие, боеприпасы, бутылки и т.д.) в подключенный контейнер."},
                  {"CollectPushComp",                 "Отправить компоненты"},
                  {"CollectPushComp_Tooltip",         "Если этот флажок установлен, система немедленно\nотправит компоненты в подключенный контейнер."},

                  {"Priority_Enable",                 "Вкл"},
                  {"Priority_Disable",                "Откл"},
                  {"Priority_Up",                     "Приоритет вверх"},
                  {"Priority_Down",                   "Приоритет вниз"},

                  {"Color_PickCurrentColor",          "Выбрать текущий цвет"},
                  {"Color_SetCurrentColor",           "Установить цвет"},

                  {"AreaShow",                        "Показать область"},
                  {"AreaShow_Tooltip",                "Когда флажок установлен, он покажет область,\nкоторую охватывает эта система"},
                  {"AreaWidth",                       "Ширина области"},
                  {"AreaHeight",                      "Высота области"},
                  {"AreaDepth",                       "Глубина области"},
                  {"RemoteCtrlBy",                    "Кем управляется"},
                  {"RemoteCtrlBy_Tooltip",            "Если выбрано, центр рабочей области будет следовать\nза персонажем (Пока он находится в пределах максимальной дистанции)."},
                  {"RemoteCtrlBy_None",               "-Никто-"},
                  {"RemoteCtrlShowArea",              "Контроль показа области"},
                  {"RemoteCtrlShowArea_Tooltip",      "Если отмечено, область будет отображатся пока\nперсонаж оснащен ручным сварщиком/резаком."},
                  {"RemoteCtrlWorking",               "Контроль работы"},
                  {"RemoteCtrlWorking_Tooltip",       "Если отмечено, система будет включена пока\nперсонаж оснащен ручным сварщиком/резаком."},
                  {"SoundVolume",                     "Громкость"},
                  {"ScriptControlled",                "Контролируется скриптом"},
                  {"ScriptControlled_Tooltip",        "Если этот флажок установлен, система не будет автоматически\nсваривать/разрезать. Каждое действие нужно выбирать, вызывая скриптовые функции."},
                  {"Info_CurentWeldEntity",           "Выбран блок сварщика:"},
                  {"Info_CurentGrindEntity",          "Выбран блок резака:"},
                  {"Info_InventoryFull",              "Блок инвентаря полон!"},
                  {"Info_LimitReached",               "Достигнут предел PCU!"},
                  {"Info_DisabledByRemote",           "Отключено дистанционным управлением!"},
                  {"Info_BlocksToBuild",              "Блоки для сборки:"},
                  {"Info_BlocksToGrind",              "Блоки для демонтажа:"},
                  {"Info_ItemsToCollect",             "Предметы для сбора:"},
                  {"Info_More",                       "- .."},
                  {"Info_MissingItems",               "Недостающие предметы:"},
                  {"Info_BlockSwitchedOff",           "Блок выключен"},
                  {"Info_BlockDamaged",               "Блок поврежден/недостроен"},
                  {"Info_BlockUnpowered",             "Блоку не хватает мощности"},
                  {"Cmd_HelpClient",                  "Версия: {0}" +
                                                      "\nДоступные команды:" +
                                                      "\n[{1}; {2}]: показать эту информацию" +
                                                      "\n[{3} {4}; {5}]: установить текущий уровень ведения журнала. Предупреждение: установка уровня '{4}' может привести к очень большим лог-файлам"},
                  {"Cmd_HelpServer",                  "\n[{0}]: создает файл настроек в текущей папке мира. После перезагрузки будут использованы настройки в этом файле вместо файла глобальных мод-настроек. "+
                                                      "\n[{1}]: создает файл глобальных настроек в папке мода (включая все опции)."}
               }
            }
         };

            return dicts;
        }
    }
}
