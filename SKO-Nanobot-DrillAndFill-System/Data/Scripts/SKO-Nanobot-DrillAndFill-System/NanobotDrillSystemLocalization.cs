namespace SpaceEquipmentLtd.NanobotDrillSystem
{
   using System.Collections.Generic;
   using VRage;
   using VRage.Utils;
   using Sandbox.ModAPI;
   using SpaceEquipmentLtd.Localization;
   using SpaceEquipmentLtd.Utils;

   
   public static class Texts
   {
      public readonly static MyStringId ModeSettings_Headline;
      public readonly static MyStringId WorkMode;
      public readonly static MyStringId WorkMode_Tooltip;
      public readonly static MyStringId WorkMode_Collect;
      public readonly static MyStringId WorkMode_Drill;
      public readonly static MyStringId WorkMode_Fill;
      public readonly static MyStringId DrillSettings_Headline;
      public readonly static MyStringId DrillPriority;
      public readonly static MyStringId DrillPriority_Tooltip;

      public readonly static MyStringId FillSettings_Headline;
      public readonly static MyStringId FillMaterial;
      public readonly static MyStringId FillMaterial_Tooltip;

      public readonly static MyStringId CollectSettings_Headline;
      public readonly static MyStringId CollectPriority;
      public readonly static MyStringId CollectPriority_Tooltip;
      public readonly static MyStringId CollectOnlyIfIdle;
      public readonly static MyStringId CollectOnlyIfIdle_Tooltip;

      public readonly static MyStringId Priority_Enable;
      public readonly static MyStringId Priority_Disable;
      public readonly static MyStringId Priority_Up;
      public readonly static MyStringId Priority_Down;

      public readonly static MyStringId GeneralSettings_Headline;

      public readonly static MyStringId AreaShow;
      public readonly static MyStringId AreaShow_Tooltip;

      public readonly static MyStringId AreaWidth;
      public readonly static MyStringId AreaHeight;
      public readonly static MyStringId AreaDepth;

      public readonly static MyStringId RemoteCtrlBy;
      public readonly static MyStringId RemoteCtrlBy_Tooltip;
      public readonly static MyStringId RemoteCtrlBy_None;
      public readonly static MyStringId RemoteCtrlShowArea;
      public readonly static MyStringId RemoteCtrlShowArea_Tooltip;
      public readonly static MyStringId RemoteCtrlWorking;
      public readonly static MyStringId RemoteCtrlWorking_Tooltip;

      public readonly static MyStringId Parent;
      public readonly static MyStringId Parent_Tooltip;

      public readonly static MyStringId SoundVolume;
      public readonly static MyStringId ScriptControlled;
      public readonly static MyStringId ScriptControlled_Tooltip;

      public readonly static MyStringId Info_CurentDrillEntity;
      public readonly static MyStringId Info_CurentFillEntity;
      public readonly static MyStringId Info_InventoryFull;
      public readonly static MyStringId Info_DisabledByRemote;
      public readonly static MyStringId Info_ItemsToDrill;
      public readonly static MyStringId Info_ItemsToFill;
      public readonly static MyStringId Info_ItemsToCollect;
      public readonly static MyStringId Info_More;
      public readonly static MyStringId Info_MissingMaterial;
      public readonly static MyStringId Info_ObjectInWorkarea;
      public readonly static MyStringId Info_BlockSwitchedOff;
      public readonly static MyStringId Info_BlockDamaged;
      public readonly static MyStringId Info_BlockUnpowered;

      public readonly static MyStringId Cmd_HelpClient;
      public readonly static MyStringId Cmd_HelpServer;

      static Texts()
      {
         var language = Mod.DisableLocalization ? MyLanguagesEnum.English : MyAPIGateway.Session.Config.Language;
         Mod.Log.Write(Logging.Level.Error, "Localization: Disabled={0} Language={1}", Mod.DisableLocalization, language);

         var texts = LocalizationHelper.GetTexts(language, GetDictionaries(), Mod.Log);
         ModeSettings_Headline = LocalizationHelper.GetStringId(texts, "ModeSettings_Headline");
         WorkMode = LocalizationHelper.GetStringId(texts, "WorkMode");
         WorkMode_Tooltip = LocalizationHelper.GetStringId(texts, "WorkMode_Tooltip");
         WorkMode_Collect = LocalizationHelper.GetStringId(texts, "WorkMode_Collect");
         WorkMode_Drill = LocalizationHelper.GetStringId(texts, "WorkMode_Drill");
         WorkMode_Fill = LocalizationHelper.GetStringId(texts, "WorkMode_Fill");

         DrillSettings_Headline = LocalizationHelper.GetStringId(texts, "DrillSettings_Headline");
         DrillPriority = LocalizationHelper.GetStringId(texts, "DrillPriority");
         DrillPriority_Tooltip = LocalizationHelper.GetStringId(texts, "DrillPriority_Tooltip");

         FillSettings_Headline = LocalizationHelper.GetStringId(texts, "FillSettings_Headline");
         FillMaterial = LocalizationHelper.GetStringId(texts, "FillMaterial");
         FillMaterial_Tooltip = LocalizationHelper.GetStringId(texts, "FillMaterial_Tooltip");

         CollectSettings_Headline = LocalizationHelper.GetStringId(texts, "CollectSettings_Headline");
         CollectPriority = LocalizationHelper.GetStringId(texts, "CollectPriority");
         CollectPriority_Tooltip = LocalizationHelper.GetStringId(texts, "CollectPriority_Tooltip");
         CollectOnlyIfIdle = LocalizationHelper.GetStringId(texts, "CollectOnlyIfIdle");
         CollectOnlyIfIdle_Tooltip = LocalizationHelper.GetStringId(texts, "CollectOnlyIfIdle_Tooltip");

         Priority_Enable = LocalizationHelper.GetStringId(texts, "Priority_Enable");
         Priority_Disable = LocalizationHelper.GetStringId(texts, "Priority_Disable");
         Priority_Up = LocalizationHelper.GetStringId(texts, "Priority_Up");
         Priority_Down = LocalizationHelper.GetStringId(texts, "Priority_Down");

         GeneralSettings_Headline = LocalizationHelper.GetStringId(texts, "GeneralSettings_Headline");

         AreaShow = LocalizationHelper.GetStringId(texts, "AreaShow");
         AreaShow_Tooltip = LocalizationHelper.GetStringId(texts, "AreaShow_Tooltip");
         AreaWidth = LocalizationHelper.GetStringId(texts, "AreaWidth");
         AreaHeight = LocalizationHelper.GetStringId(texts, "AreaHeight");
         AreaDepth = LocalizationHelper.GetStringId(texts, "AreaDepth");

         Parent = LocalizationHelper.GetStringId(texts, "Parent");
         Parent_Tooltip = LocalizationHelper.GetStringId(texts, "Parent_Tooltip");

         RemoteCtrlBy = LocalizationHelper.GetStringId(texts, "RemoteCtrlBy");
         RemoteCtrlBy_Tooltip = LocalizationHelper.GetStringId(texts, "RemoteCtrlBy_Tooltip");
         RemoteCtrlBy_None = LocalizationHelper.GetStringId(texts, "RemoteCtrlBy_None");

         RemoteCtrlShowArea = LocalizationHelper.GetStringId(texts, "RemoteCtrlShowArea");
         RemoteCtrlShowArea_Tooltip = LocalizationHelper.GetStringId(texts, "RemoteCtrlShowArea_Tooltip");
         RemoteCtrlWorking = LocalizationHelper.GetStringId(texts, "RemoteCtrlWorking");
         RemoteCtrlWorking_Tooltip = LocalizationHelper.GetStringId(texts, "RemoteCtrlWorking_Tooltip");

         SoundVolume = LocalizationHelper.GetStringId(texts, "SoundVolume");
         ScriptControlled = LocalizationHelper.GetStringId(texts, "ScriptControlled");
         ScriptControlled_Tooltip = LocalizationHelper.GetStringId(texts, "ScriptControlled_Tooltip");

         Info_CurentDrillEntity = LocalizationHelper.GetStringId(texts, "Info_CurentDrillEntity");
         Info_CurentFillEntity = LocalizationHelper.GetStringId(texts, "Info_CurentFillEntity");
         Info_InventoryFull = LocalizationHelper.GetStringId(texts, "Info_InventoryFull");
         Info_DisabledByRemote = LocalizationHelper.GetStringId(texts, "Info_DisabledByRemote");
         Info_ItemsToDrill = LocalizationHelper.GetStringId(texts, "Info_ItemsToDrill");
         Info_ItemsToFill = LocalizationHelper.GetStringId(texts, "Info_ItemsToFill");
         Info_ItemsToCollect = LocalizationHelper.GetStringId(texts, "Info_ItemsToCollect");
         Info_More = LocalizationHelper.GetStringId(texts, "Info_More");
         Info_MissingMaterial = LocalizationHelper.GetStringId(texts, "Info_MissingMaterial");
         Info_ObjectInWorkarea = LocalizationHelper.GetStringId(texts, "Info_ObjectInWorkarea");
         Info_BlockSwitchedOff = LocalizationHelper.GetStringId(texts, "Info_BlockSwitchedOff");
         Info_BlockDamaged = LocalizationHelper.GetStringId(texts, "Info_BlockDamaged");
         Info_BlockUnpowered = LocalizationHelper.GetStringId(texts, "Info_BlockUnpowered");

         Cmd_HelpClient = LocalizationHelper.GetStringId(texts, "Cmd_HelpClient");
         Cmd_HelpServer = LocalizationHelper.GetStringId(texts, "Cmd_HelpServer");
      }

      static Dictionary<MyLanguagesEnum, Dictionary<string, string>> GetDictionaries()
      {
         var dicts = new Dictionary<MyLanguagesEnum, Dictionary<string, string>>
         {
            { MyLanguagesEnum.English, new Dictionary<string, string>
               {
                  {"ModeSettings_Headline",      "———————Mode Settings———————"},
                  {"WorkMode",                   "Work mode"},
                  {"WorkMode_Tooltip",           "Select what the nanobots should do and how."},
                  {"WorkMode_Collect",           "Collect"},
                  {"WorkMode_Drill",             "Drill"},
                  {"WorkMode_Fill",              "Fill"},
                  {"DrillSettings_Headline",     "———————Settings for Drilling———————"},
                  {"DrillPriority",              "Drill Priority"},
                  {"DrillPriority_Tooltip",      "Enable/Disable drill of ore"},
                  {"FillSettings_Headline",      "———————Settings for Filling———————"},
                  {"FillMaterial",               "Fill Material"},
                  {"FillMaterial_Tooltip",       "Select material for filling"},
                  {"CollectSettings_Headline",   "———————Settings for Collecting———————"},
                  {"CollectPriority",            "Collect Priority"},
                  {"CollectPriority_Tooltip",    "Enable/Disable collecting of ore"},
                  {"CollectOnlyIfIdle",          "Collect only if idle"},
                  {"CollectOnlyIfIdle_Tooltip",  "if set collecting floating objects is done only if no drilling/filling is needed."},
                  {"Priority_Enable",            "Enable"},
                  {"Priority_Disable",           "Disable"},
                  {"Priority_Up",                "Priority Up"},
                  {"Priority_Down",              "Priority Down"},
                  {"GeneralSettings_Headline",   "———————General Settings ———————"},
                  {"AreaShow",                   "Show Area"},
                  {"AreaShow_Tooltip",           "When checked, it will show you the area this system covers"},
                  {"AreaWidth",                  "Area Width"},
                  {"AreaHeight",                 "Area Height"},
                  {"AreaDepth",                  "Area Depth"},
                  {"Parent",                     "Parent"},
                  {"Parent_Tooltip",             "Settings applied from this parent"},
                  {"RemoteCtrlBy",               "Remote controlled by"},
                  {"RemoteCtrlBy_Tooltip",       "Select if center of working area should follow a character. (As long as he is inside the maximum range)"},
                  {"RemoteCtrlBy_None",          "-None-"},
                  {"RemoteCtrlShowArea",         "Control Show Area"},
                  {"RemoteCtrlShowArea_Tooltip", "Select if 'Show area' is active as long as character is equipped with hand drill"},
                  {"RemoteCtrlWorking",          "Control Working"},
                  {"RemoteCtrlWorking_Tooltip",  "Select if drill is only switched on as long as character is equipped with hand drill"},
                  {"SoundVolume",                "Sound Volume"},
                  {"ScriptControlled",           "Controlled by Script"},
                  {"ScriptControlled_Tooltip",   "When checked, the system will not drill/fill automatically. Each action has to be picked by calling scripting functions."},
                  {"Info_CurentDrillEntity",     "Picked Drilling Entity:"},
                  {"Info_CurentFillEntity",      "Picked Filling Entity:"},
                  {"Info_InventoryFull",         "Block inventory is full!"},
                  {"Info_DisabledByRemote",      "Disabled by remote control!"},
                  {"Info_ItemsToDrill",          "Items to drill:"},
                  {"Info_ItemsToFill",           "Items to fill:"},
                  {"Info_ItemsToCollect",        "Floatings to collect:"},
                  {"Info_More",                  " -.."},
                  {"Info_MissingMaterial",       "Missing material for filling! {0}"},
                  {"Info_ObjectInWorkarea",      "Life form in working area detected!"},
                  {"Info_BlockSwitchedOff",      "Block is switched off"},
                  {"Info_BlockDamaged",          "Block is damaged / incomplete"},
                  {"Info_BlockUnpowered",        "Block has not enough power"},
                  {"Cmd_HelpClient",             "Version: {0}" +
                                                 "\nAvailable commands:" +
                                                 "\n[{1};{2}]: Shows this info" +
                                                 "\n[{3} {4};{5}]: Set the current logging level. Warning: Setting level to '{4}' could produce very large log-files"},
                  {"Cmd_HelpServer",             "\n[{0}]: Creates a settings file inside your current world folder. After restart the settings in this file will be used, instead of the global mod-settings file." +
                                                 "\n[{1}]: Creates a global settings file inside mod folder (including all options)."}
               }
            },
            { MyLanguagesEnum.German,  new Dictionary<string, string>
               {
                  {"ModeSettings_Headline",      "—— Moduseinstellungen ——"},
                  {"WorkMode",                   "Arbeitsmodus"},
                  {"WorkMode_Tooltip",           "Wählen Sie aus, was und wie es die Nanobots tun sollen."},
                  {"WorkMode_Collect",           "Sammeln"},
                  {"WorkMode_Drill",             "Bohren"},
                  {"WorkMode_Fill",              "Füllen"},
                  {"DrillSettings_Headline",     "—— Einstellungen für das Bohren ———"},
                  {"DrillPriority",              "Bohr-Priorität"},
                  {"DrillPriority_Tooltip",      "Bohren diesen Erzen aktivieren / deaktivieren"},
                  {"FillSettings_Headline",      "—— Einstellungen zum Befüllen —————"},
                  {"FillMaterial",               "Füllmaterial"},
                  {"FillMaterial_Tooltip",       "Material zum Füllen auswählen"},
                  {"CollectSettings_Headline",   "—— Einstellungen zum Sammeln ——————"},
                  {"CollectPriority",            "Sammelpriorität"},
                  {"CollectPriority_Tooltip",    "Erz sammeln aktivieren / deaktivieren"},
                  {"CollectOnlyIfIdle",          "Nur im Leerlauf sammeln"},
                  {"CollectOnlyIfIdle_Tooltip",  "Wenn das Sammeln von freien Objekten eingestellt ist, erfolgt dies nur, wenn kein Bohren / Füllen erforderlich ist."},
                  {"Priority_Enable",            "Aktivieren"},
                  {"Priority_Disable",           "Deaktivieren"},
                  {"Priority_Up",                "Priorität hoch"},
                  {"Priority_Down",              "Priorität runter"},
                  {"GeneralSettings_Headline",   "—— Generelle Einstellungen ——————"},
                  {"AreaShow",                   "Bereich anzeigen"},
                  {"AreaShow_Tooltip",           "Wenn diese Option aktiviert ist, wird der Bereich angezeigt, den dieses System abdeckt."},
                  {"AreaWidth",                  "Bereichsbreite"},
                  {"AreaHeight",                 "Bereichshöhe"},
                  {"AreaDepth",                  "Bereichstiefe"},
                  {"Parent",                     "Kontroller"},
                  {"Parent_Tooltip",             "Einstellungen werden von diesem Kontroller übernommen"},
                  {"RemoteCtrlBy",               "Ferngesteuert von"},
                  {"RemoteCtrlBy_Tooltip",       "Wählen Sie aus, ob die Mitte des Arbeitsbereichs einem Charakter folgen soll. (Solange er sich innerhalb der maximalen Reichweite befindet) "},
                  {"RemoteCtrlBy_None",          "-Keinem-"},
                  {"RemoteCtrlShowArea",         "Bereichsanzeige steuern"},
                  {"RemoteCtrlShowArea_Tooltip", "Wählen Sie, ob 'Bereich anzeigen' aktiv ist, solange der Charakter mit einer Handbohrmaschine ausgestattet ist."},
                  {"RemoteCtrlWorking",          "Block ein/aus steuern"},
                  {"RemoteCtrlWorking_Tooltip",  "Wählen Sie, ob der Block nur eingeschaltet ist, solange der Charakter mit einer Handbohrmaschine ausgestattet ist."},
                  {"SoundVolume",                "Lautstärke"},
                  {"ScriptControlled",           "Vom Skript gesteuert"},
                  {"ScriptControlled_Tooltip",   "Wenn diese Option aktiviert ist, bohrt / füllt das System nicht automatisch. Jede Aktion muss durch Aufrufen von Skriptfunktionen ausgewählt werden."},
                  {"Info_CurentDrillEntity",     "Ausgewählte Bohrstelle:"},
                  {"Info_CurentFillEntity",      "Ausgewählte Füllstelle:"},
                  {"Info_InventoryFull",         "Blockinventar ist voll!"},
                  {"Info_DisabledByRemote",      "Durch Fernbedienung deaktiviert!"},
                  {"Info_ItemsToDrill",          "Zu bohrende Elemente:"},
                  {"Info_ItemsToFill",           "Zu füllende Elemente:"},
                  {"Info_ItemsToCollect",        "Zu sammelnde Erze:"},
                  {"Info_More",                  "- ..."},
                  {"Info_MissingMaterial",       "Fehlendes Material zum Füllen! {0} "},
                  {"Info_ObjectInWorkarea",      "Lebensform im Arbeitsbereich erkannt!"},
                  {"Info_BlockSwitchedOff",      "Block ist ausgeschaltet"},
                  {"Info_BlockDamaged",          "Block ist beschädigt / unvollständig"},
                  {"Info_BlockUnpowered",        "Block hat nicht genug Energie"},
                  {"Cmd_HelpClient",             "Version: {0}" +
                                                 "\nVerfügbare Befehle:" +
                                                 "\n[{1}; {2}]: Zeigt diese Info an" +
                                                 "\n[{3} {4}; {5}]: Legen Sie die aktuelle Protokollierungsstufe fest. Warnung: Das Setzen der Stufe auf '{4}' kann zu sehr großen Protokolldateien führen."},
                  {"Cmd_HelpServer",             "\n[{0}]: Erstellt eine Einstellungsdatei in Ihrem aktuellen Weltordner. Nach dem Neustart werden die Einstellungen in dieser Datei anstelle der globalen Mod - Einstellungsdatei verwendet."+
                                                 "\n[{1}]: Erstellt eine globale Einstellungsdatei im Mod-Ordner (einschließlich aller Optionen)."}
               }
            },
            { MyLanguagesEnum.Russian,  new Dictionary<string, string>
               {
                  {"ModeSettings_Headline",      "———————Настройки———————"},
                  {"WorkMode",                   "Режим работы"},
                  {"WorkMode_Tooltip",           "Выберите в каком режиме наноботы должны работать."},
                  {"WorkMode_Collect",           "Сбор"},
                  {"WorkMode_Drill",             "Бурение"},
                  {"WorkMode_Fill",              "Заполнение"},
                  {"DrillSettings_Headline",     "———————Настройки бурения———————"},
                  {"DrillPriority",              "Приоритет бурения"},
                  {"DrillPriority_Tooltip",      "Вкл/выкл бурение руды"},
                  {"FillSettings_Headline",      "———————Настройки заполнения———————"},
                  {"FillMaterial",               "Материал заполнения"},
                  {"FillMaterial_Tooltip",       "Выберите материал для заполнения"},
                  {"CollectSettings_Headline",   "———————Настройки сбора———————"},
                  {"CollectPriority",            "Приоритет сбора"},
                  {"CollectPriority_Tooltip",    "Вкл/выкл сбор руды"},
                  {"CollectOnlyIfIdle",          "Собирать только если свободно"},
                  {"CollectOnlyIfIdle_Tooltip",  "если отмечено сбор объектов будет происходить только если нет бурения/заполнения в процессе."},
                  {"Priority_Enable",            "Включить"},
                  {"Priority_Disable",           "Выключить"},
                  {"Priority_Up",                "Повысить приоритет"},
                  {"Priority_Down",              "Понизить приоритет"},
                  {"GeneralSettings_Headline",   "——Общие настройки ——————"},
                  {"AreaShow",                   "Показать область"},
                  {"AreaShow_Tooltip",           "Когда отмечено, будет показываться рабочая область"},
                  {"AreaWidth",                  "Ширина области"},
                  {"AreaHeight",                 "Высота области"},
                  {"AreaDepth",                  "Глубина области"},
                  {"Parent",                     "Parent"},
                  {"Parent_Tooltip",             "Settings applied from this parent"},
                  {"RemoteCtrlBy",               "Кем управляется дистанционно"},
                  {"RemoteCtrlBy_Tooltip",       "Если отмечено, центр рабочей зоны будет следовать за персонажем. (Пока он находится в пределах максимальной дистанции)"},
                  {"RemoteCtrlBy_None",          "-Никем-"},
                  {"RemoteCtrlShowArea",         "Управлять отображением области"},
                  {"RemoteCtrlShowArea_Tooltip", "Если отмечено, область будет показываться тогда, когда ручной бур будет находиться в руках"},
                  {"RemoteCtrlWorking",          "Управлять работой"},
                  {"RemoteCtrlWorking_Tooltip",  "Если отмечено, бурение будет происходить тогда, когда ручной бур будет находиться в руках"},
                  {"SoundVolume",                "Уровень громкости"},
                  {"ScriptControlled",           "Управляется ли скриптом"},
                  {"ScriptControlled_Tooltip",   "Когда отмечено, система не будет бурить/заполнять автоматически. Каждое действие будет управляться функциями скрипта."},
                  {"Info_CurentDrillEntity",     "Текущий объект бурения:"},
                  {"Info_CurentFillEntity",      "Текущий объект заполнения:"},
                  {"Info_InventoryFull",         "Инвентарь блока заполнен!"},
                  {"Info_DisabledByRemote",      "Отключено дистанционным управлением!"},
                  {"Info_ItemsToDrill",          "Предметы для бурения:"},
                  {"Info_ItemsToFill",           "Предметы для заполнения:"},
                  {"Info_ItemsToCollect",        "Объекты для подбора:"},
                  {"Info_More",                  " -.."},
                  {"Info_MissingMaterial",       "Отсутствует материал для заполнения! {0}"},
                  {"Info_ObjectInWorkarea",      "Живой организм находится в рабочей области!"},
                  {"Info_BlockSwitchedOff",      "Блок выключен"},
                  {"Info_BlockDamaged",          "Блок поврежден / не завершено строительство"},
                  {"Info_BlockUnpowered",        "Блоку недостаточно энергии"},
                  {"Cmd_HelpClient",             "Версия: {0}" +
                                                 "\nДоступные команды:" +
                                                 "\n[{1};{2}]: Показать эту информацию" + 
                                                 "\n[{3} {4};{5}]: Установить текущий уровень записи логов. Предупреждение: Установка уровня на '{4}' может создавать огромные лог-файлы"},
                  {"Cmd_HelpServer",             "\n[{0}]: Создает файл с настройками внутри папки текущего мира. После рестарта настройки в этом файле будут использованы вместо глобальных настроек." +
                                                 "\n[{1}]: Создает файл с глобальными настройками внутри папки мода (включает все опции)."}
               }
            },
            { MyLanguagesEnum.ChineseChina,  new Dictionary<string, string>
               {
                  {"ModeSettings_Headline",      " ————————模式设置————————"},
                  {"WorkMode",                   "工作模式"},
                  {"WorkMode_Tooltip",           "选择纳米机器人应该做什么以及如何做。"},
                  {"WorkMode_Collect",           "收藏"},
                  {"WorkMode_Drill",             "钻头"},
                  {"WorkMode_Fill",              "填"},
                  {"DrillSettings_Headline",     " ————————钻井设置————————"},
                  {"DrillPriority",              "钻探优先级"},
                  {"DrillPriority_Tooltip",      "启用/禁用矿石钻探"},
                  {"FillSettings_Headline",      " ————————填充设置————————"},
                  {"FillMaterial",               "填充材料"},
                  {"FillMaterial_Tooltip",       "选择填充材料"},
                  {"CollectSettings_Headline",   " ————————收集设置————————"},
                  {"CollectPriority",            "收集优先级"},
                  {"CollectPriority_Tooltip",    "启用/禁用矿石收集"},
                  {"CollectOnlyIfIdle",          "仅在空闲时收集"},
                  {"CollectOnlyIfIdle_Tooltip",  "如果仅在不需要钻孔/填充的情况下进行收集浮动物体的设置，"},
                  {"Priority_Enable",            "启用"},
                  {"Priority_Disable",           "禁用"},
                  {"Priority_Up",                "优先"},
                  {"Priority_Down",              "优先级降低"},
                  {"AreaShow",                   "显示区域"},
                  {"AreaShow_Tooltip",           "选中后，它将向您显示系统覆盖的区域"},
                  {"AreaWidth",                  "区域宽度"},
                  {"AreaHeight",                 "区域高度"},
                  {"AreaDepth",                  "区域深度"},
                  {"RemoteCtrlBy",               "远程控制"},
                  {"RemoteCtrlBy_Tooltip",       "选择工作区域的中心是否应跟随字符。（只要他在最大范围内）"},
                  {"RemoteCtrlBy_None",          "-没有-"},
                  {"RemoteCtrlShowArea",         "控制展区"},
                  {"RemoteCtrlShowArea_Tooltip", "选择，只要角色配备了手钻，'显示区域'是否处于活动状态"},
                  {"RemoteCtrlWorking",          "控制工作"},
                  {"RemoteCtrlWorking_Tooltip",  "选择是否仅在角色配备手动钻机的情况下才打开钻机"},
                  {"SoundVolume",                "音量"},
                  {"ScriptControlled",           "由脚本控制"},
                  {"ScriptControlled_Tooltip",   "选中后，系统将不会自动进行钻取/填充。每个动作都必须通过调用脚本函数来选择。"},
                  {"Info_CurentDrillEntity",     "拾取的钻井实体："},
                  {"Info_CurentFillEntity",      "选择的填充实体："},
                  {"Info_InventoryFull",         "大量库存已满！"},
                  {"Info_DisabledByRemote",      "被远程控制禁用！"},
                  {"Info_ItemsToDrill",          "要钻的项目："},
                  {"Info_ItemsToFill",           "要填充的项目："},
                  {"Info_ItemsToCollect",        "浮标收集："},
                  {"Info_More",                  "-.."},
                  {"Info_MissingMaterial",       "缺少填充材料！{0}"},
                  {"Info_ObjectInWorkarea",      "发现工作区的生命形式！"},
                  {"Info_BlockSwitchedOff",      "块已关闭"},
                  {"Info_BlockDamaged",          "块已损坏/不完整"},
                  {"Info_BlockUnpowered",        "块没有足够的力量"},
                  {"Cmd_HelpClient",             "版本：{0}" +
                                                 "\n可用命令：" +
                                                 "\n[{1}; {2}]：显示此信息" +
                                                 "\n[{3} {4}; {5}]：设置当前日志记录级别。警告：将级别设置为'{4}'可能会产生非常大的日志文件"},
                  {"Cmd_HelpServer",             "\n[{0}]：在当前世界文件夹内创建一个设置文件。重新启动后，将使用该文件中的设置，而不是全局mod-settings文件。" +
                                                 "\n[{1}]：在mod文件夹内创建一个全局设置文件（包括所有选项）。"}
               }
            },
            { MyLanguagesEnum.Spanish_Spain,  new Dictionary<string, string>
               {
                  {"ModeSettings_Headline",      "——————— Configuración de modo ———————"},
                  {"WorkMode",                   "Modo de trabajo"},
                  {"WorkMode_Tooltip",           "Seleccione qué deben hacer los nanobots y cómo"},
                  {"WorkMode_Collect",           "Recoger"},
                  {"WorkMode_Drill",             "Perforar"},
                  {"WorkMode_Fill",              "Llenar"},
                  {"DrillSettings_Headline",     "——————— Configuración para perforación ———————"},
                  {"DrillPriority",              "Prioridad de perforación"},
                  {"DrillPriority_Tooltip",      "Activar / Desactivar perforación de mineral"},
                  {"FillSettings_Headline",      "——————— Configuración para el llenado ———————"},
                  {"FillMaterial",               "Material de relleno"},
                  {"FillMaterial_Tooltip",       "Seleccionar material para relleno"},
                  {"CollectSettings_Headline",   "——————— Configuración para la recopilación ———————"},
                  {"CollectPriority",            "Recoger prioridad"},
                  {"CollectPriority_Tooltip",    "Habilitar / deshabilitar la recolección de mineral"},
                  {"CollectOnlyIfIdle",          "Recoger solo si está inactivo"},
                  {"CollectOnlyIfIdle_Tooltip",  "si el conjunto de recolección de objetos flotantes se realiza solo si no se necesita perforación / relleno"},
                  {"Priority_Enable",            "Habilitar"},
                  {"Priority_Disable",           "Inhabilitar"},
                  {"Priority_Up",                "Prioridad arriba"},
                  {"Priority_Down",              "Prioridad hacia abajo"},
                  {"AreaShow",                   "Mostrar área"},
                  {"AreaShow_Tooltip",           "Cuando se marca, le mostrará el área que cubre este sistema"},
                  {"AreaWidth",                  "Ancho del área"},
                  {"AreaHeight",                 "Altura del área"},
                  {"AreaDepth",                  "Profundidad del área"},
                  {"RemoteCtrlBy",               "Control remoto por"},
                  {"RemoteCtrlBy_Tooltip",       "Seleccione si el centro del área de trabajo debe seguir a un personaje. (Mientras esté dentro del rango máximo) "},
                  {"RemoteCtrlBy_None",          "-Ninguna-"},
                  {"RemoteCtrlShowArea",         "Área de demostración de control"},
                  {"RemoteCtrlShowArea_Tooltip", "Seleccione si 'Mostrar área' está activo siempre que el personaje esté equipado con taladro manual"},
                  {"RemoteCtrlWorking",          "Control de trabajo"},
                  {"RemoteCtrlWorking_Tooltip",  "Seleccione si el ejercicio solo está activado siempre que el personaje esté equipado con ejercicio manual"},
                  {"SoundVolume",                "Volumen de sonido"},
                  {"ScriptControlled",           "Controlado por script"},
                  {"ScriptControlled_Tooltip",   "Cuando está marcado, el sistema no perforará / rellenará automáticamente. Cada acción tiene que elegirse llamando a las funciones de secuencias de comandos "},
                  {"Info_CurentDrillEntity",     "Entidad de perforación elegida:"},
                  {"Info_CurentFillEntity",      "Entidad de llenado elegida:"},
                  {"Info_InventoryFull",         "¡El inventario de bloques está lleno!"},
                  {"Info_DisabledByRemote",      "Deshabilitado por control remoto!"},
                  {"Info_ItemsToDrill",          "Artículos para perforar:"},
                  {"Info_ItemsToFill",           "Elementos para llenar:"},
                  {"Info_ItemsToCollect",        "Flotadores para recoger:"},
                  {"Info_More",                  "- .."},
                  {"Info_MissingMaterial",       "¡Falta material para llenar! {0} "},
                  {"Info_ObjectInWorkarea",      "¡Forma de vida en el área de trabajo detectada!"},
                  {"Info_BlockSwitchedOff",      "El bloque está apagado"},
                  {"Info_BlockDamaged",          "El bloque está dañado / incompleto"},
                  {"Info_BlockUnpowered",        "El bloque no tiene suficiente poder"},
                  {"Cmd_HelpClient",             "Versión: {0}" +
                                                 "\nComandos disponibles:" +
                                                 "\n[{1}; {2}]: muestra esta información" +
                                                 "\n[{3} {4}; {5}]: establece el nivel de registro actual. Advertencia: establecer el nivel en '{4}' podría producir archivos de registro muy grandes"},
                  {"Cmd_HelpServer",             "\n[{0}]: crea un archivo de configuración dentro de su carpeta mundial actual. Después de reiniciar, se usará la configuración de este archivo, en lugar del archivo de configuración de mod global."+
                                                 "\n[{1}]: crea un archivo de configuración global dentro de la carpeta de modulación (incluidas todas las opciones)"}
               }
            },
            { MyLanguagesEnum.French,  new Dictionary<string, string>
               {
                  {"ModeSettings_Headline",      "——————— Paramètres du mode ———————"},
                  {"WorkMode",                   "En mode travail"},
                  {"WorkMode_Tooltip",           "Sélectionnez ce que les nanobots doivent faire et comment."},
                  {"WorkMode_Collect",           "Collecte"},
                  {"WorkMode_Drill",             "Percer"},
                  {"WorkMode_Fill",              "Remplir"},
                  {"DrillSettings_Headline",     "——————— Paramètres de perçage ———————"},
                  {"DrillPriority",              "Priorité de forage"},
                  {"DrillPriority_Tooltip",      "Activer / désactiver le forage du minerai"},
                  {"FillSettings_Headline",      "——————— Paramètres de remplissage ———————"},
                  {"FillMaterial",               "Matériau de remplissage"},
                  {"FillMaterial_Tooltip",       "Choisir le matériau à remplir"},
                  {"CollectSettings_Headline",   "——————— Paramètres pour la collecte ———————"},
                  {"CollectPriority",            "Priorité de collecte"},
                  {"CollectPriority_Tooltip",    "Activer / désactiver la collecte de minerai"},
                  {"CollectOnlyIfIdle",          "Récupérer uniquement si inactif"},
                  {"CollectOnlyIfIdle_Tooltip",  "si la collecte des objets flottants est configurée uniquement si aucun perçage / remplissage n'est nécessaire."},
                  {"Priority_Enable",            "Activer"},
                  {"Priority_Disable",           "Désactiver"},
                  {"Priority_Up",                "Priorité vers le haut"},
                  {"Priority_Down",              "Priorité vers le bas"},
                  {"AreaShow",                   "Zone d'exposition"},
                  {"AreaShow_Tooltip",           "Lorsque coché, il vous montrera la zone couverte par ce système"},
                  {"AreaWidth",                  "Largeur de la zone"},
                  {"AreaHeight",                 "Hauteur de la zone"},
                  {"AreaDepth",                  "Profondeur de la zone"},
                  {"RemoteCtrlBy",               "Télécommandé par"},
                  {"RemoteCtrlBy_Tooltip",       "Sélectionnez si le centre de la zone de travail doit suivre un caractère. (Tant qu'il est dans la plage maximale)"},
                  {"RemoteCtrlBy_None",          "-Aucun-"},
                  {"RemoteCtrlShowArea",         "Zone d'exposition de contrôle"},
                  {"RemoteCtrlShowArea_Tooltip", "Sélectionnez si 'Afficher la zone' est actif tant que le personnage est équipé d'une perceuse à main"},
                  {"RemoteCtrlWorking",          "Contrôle de travail"},
                  {"RemoteCtrlWorking_Tooltip",  "Sélectionnez si l'exercice est activé uniquement si le personnage est équipé d'une perceuse à main"},
                  {"SoundVolume",                "Volume sonore"},
                  {"ScriptControlled",           "Contrôlé par script"},
                  {"ScriptControlled_Tooltip",   "Lorsque cette case est cochée, le système ne perde pas / ne remplit pas automatiquement. Chaque action doit être sélectionnée en appelant des fonctions de script."},
                  {"Info_CurentDrillEntity",     "Entité de forage choisie:"},
                  {"Info_CurentFillEntity",      "Entité de remplissage choisie:"},
                  {"Info_InventoryFull",         "L'inventaire en bloc est plein!"},
                  {"Info_DisabledByRemote",      "Désactivé par la télécommande!"},
                  {"Info_ItemsToDrill",          "Articles à percer:"},
                  {"Info_ItemsToFill",           "Articles à remplir:"},
                  {"Info_ItemsToCollect",        "Flottants à collecter:"},
                  {"Info_More",                  "- .."},
                  {"Info_MissingMaterial",       "Matière manquante pour le remplissage! {0}"},
                  {"Info_ObjectInWorkarea",      "Forme de vie détectée dans la zone de travail!"},
                  {"Info_BlockSwitchedOff",      "Le bloc est désactivé"},
                  {"Info_BlockDamaged",          "Le bloc est endommagé / incomplet"},
                  {"Info_BlockUnpowered",        "Block n'a pas assez de pouvoir"},
                  {"Cmd_HelpClient",             "Version: {0}" +
                                                "\nCommandes disponibles:" +
                                                "\n[{1}; {2}]: affiche cette information" +
                                                "\n[{3} {4}; {5}]: définissez le niveau de journalisation actuel. Avertissement: définir le niveau sur '{4}' peut générer des fichiers journaux très volumineux"},
                  {"Cmd_HelpServer",            "\n[{0}]: crée un fichier de paramètres dans votre dossier mondial actuel. Après le redémarrage, les paramètres de ce fichier seront utilisés à la place du fichier global de paramètres de mod. "+
                                                "\n[{1}]: crée un fichier de paramètres globaux dans le dossier mod (avec toutes les options)."}
               }
            }
         };

         dicts.Add(MyLanguagesEnum.Spanish_HispanicAmerica, dicts[MyLanguagesEnum.Spanish_Spain]);
         return dicts;
      }
   }
}
