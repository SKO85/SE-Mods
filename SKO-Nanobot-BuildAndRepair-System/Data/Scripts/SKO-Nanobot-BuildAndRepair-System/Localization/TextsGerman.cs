using System.Collections.Generic;

namespace SKONanobotBuildAndRepairSystem.Localization
{
    public static class TextsGerman
    {
        public static Dictionary<string, string> Dictionary = new Dictionary<string, string>()
        {
            {"ModeSettings_Headline",           "—— Moduseinstellungen ——"},
            {"SearchMode",                      "Modus"},
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
            {"GrindOrderFarthest",              "Enferntester zuerst"},
            {"GrindOrderFarthest_Tooltip",      "Wenn diese Option markiert ist und Blöcke die gleiche Priorität besitzen, wird der enfernteste Block zuerst demontiert."},
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
        };
    }
}