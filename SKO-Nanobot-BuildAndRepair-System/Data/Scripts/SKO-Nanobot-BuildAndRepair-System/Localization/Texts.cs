namespace SKONanobotBuildAndRepairSystem.Localization
{
    using Sandbox.ModAPI;
    using SKONanobotBuildAndRepairSystem.Utils;
    using System.Collections.Generic;
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
        public static readonly MyStringId GrindOrderFarthest;
        public static readonly MyStringId GrindOrderFarthest_Tooltip;
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
            var language = Mod.DisableLocalization ? MyLanguagesEnum.English : MyAPIGateway.Session.Config.Language;
            Logging.Instance.Write(Logging.Level.Error, "Localization: Disabled={0} Language={1}", Mod.DisableLocalization, language);

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
            GrindOrderFarthest = LocalizationHelper.GetStringId(texts, "GrindOrderFarthest");
            GrindOrderFarthest_Tooltip = LocalizationHelper.GetStringId(texts, "GrindOrderFarthest_Tooltip");
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
            var dicts = new Dictionary<MyLanguagesEnum, Dictionary<string, string>> {
                { MyLanguagesEnum.English, TextsEnglish.Dictionary },
                { MyLanguagesEnum.German,  TextsGerman.Dictionary },
                { MyLanguagesEnum.Russian,  TextsRussian.Dictionary },
                { MyLanguagesEnum.Polish,  TextsPolish.Dictionary }
            };

            return dicts;
        }
    }
}