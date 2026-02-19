using System.Collections.Generic;

namespace SKONanobotBuildAndRepairSystem.Localization
{
    public static class TextsPolish
    {
        public static Dictionary<string, string> Dictionary = new Dictionary<string, string>()
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
            {"GrindOrderFarthest",              "Najpierw najdalsze"},
            {"GrindOrderFarthest_Tooltip",      "Po zaznaczeniu, jeśli bloki mają ten sam priorytet, najdalszy będzie rozbierany pierwszy."},
            {"GrindOrderSmallest",              "Najpierw najmniejsza siatka"},
            {"GrindOrderSmallest_Tooltip",      "Po zaznaczeniu, jeśli bloki mają ten sam priorytet, najmniejsza siatka będzie rozbierana jako pierwsza."},
            {"GrindIgnorePriority",             "Ignoruj kolejność priorytetów"},
            {"GrindIgnorePriority_Tooltip",     "Po zaznaczeniu kolejność priorytetów jest ignorowana, a bloki są rozbierane tylko według odległości. Status włączenia/wyłączenia typów bloków jest nadal respektowany."},

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
            {"DisableTickingSound",             "Wyłącz dźwięk tykania"},
            {"DisableTickingSound_Tooltip",     "Po zaznaczeniu dźwięk tykania dla tego bloku jest wyłączony."},
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
        };
    }
}