using System.Collections.Generic;

namespace SKONanobotBuildAndRepairSystem.Localization
{
    // Korean translation contributed by najeong2 (GitHub #145). Space Engineers has no
    // Korean MyLanguagesEnum entry, so this dictionary is selected via the
    // LanguageOverride mod setting (FEAT-261006.1), never by the game language.
    public static class TextsKorean
    {
        public static Dictionary<string, string> Dictionary = new Dictionary<string, string>()
        {
            {"ModeSettings_Headline",           "—————— 작동 모드 설정 ——————"},
            {"SearchMode",                      "나노봇 이동 방식"},
            {"SearchMode_Tooltip",              "나노봇이 작업 대상을 탐색하고 접근하는 방식을 선택합니다."},
            {"SearchMode_Walk",                 "보행 모드"},
            {"SearchMode_Fly",                  "비행 모드"},
            {"WorkMode",                        "작업 방식"},
            {"WorkMode_Tooltip",                "나노봇이 용접과 분해 중 어떤 작업을 우선할지 선택합니다."},
            {"WorkMode_WeldB4Grind",            "용접 우선"},
            {"WorkMode_GrindB4Weld",            "분해 우선"},
            {"WorkMode_GrindIfWeldStuck",       "용접이 막히면 분해"},
            {"WorkMode_WeldOnly",               "용접만"},
            {"WorkMode_GrindOnly",              "분해만"},

            {"WeldSettings_Headline",           "—————— 용접 설정 ——————"},
            {"WeldUseIgnoreColor",              "제외 색상 사용"},
            {"WeldUseIgnoreColor_Tooltip",      "활성화하면 아래에서 지정한 색상의 블록은 용접 대상에서 제외합니다."},
            {"WeldBuildNew",                    "프로젝터 투영 건설"},
            {"WeldBuildNew_Tooltip",            "활성화하면 프로젝터가 투영한 블록도 자동으로 건설합니다."},
            {"WeldMode",                        "용접 수준"},
            {"WeldMode_Tooltip",                "나노봇이 블록을 어느 상태까지 용접할지 선택합니다."},
            {"WeldMode_Full",                   "완전히 용접"},
            {"WeldMode_Functional",             "기능 작동 상태까지"},
            {"WeldMode_Skeleton",               "골격만"},
            {"WeldPriority",                    "용접 우선순위"},
            {"WeldPriority_Tooltip",            "선택한 블록 종류의 건설/수리를 활성화하거나 비활성화하고 우선순위를 정합니다."},

            {"GrindSettings_Headline",          "—————— 분해 설정 ——————"},
            {"GrindUseGrindColor",              "분해 색상 사용"},
            {"GrindUseGrindColor_Tooltip",      "활성화하면 아래에서 지정한 색상의 블록을 분해합니다."},
            {"GrindJanitorEnemy",               "자동 정리: 적 블록 분해"},
            {"GrindJanitorEnemy_Tooltip",       "활성화하면 작업 범위 안의 적 소유 블록을 자동으로 분해합니다."},
            {"GrindJanitorNotOwned",            "자동 정리: 무소유 블록 분해"},
            {"GrindJanitorNotOwned_Tooltip",    "활성화하면 작업 범위 안의 소유자가 없는 블록을 자동으로 분해합니다."},
            {"GrindJanitorNeutrals",            "자동 정리: 중립 블록 분해"},
            {"GrindJanitorNeutrals_Tooltip",    "활성화하면 전쟁 중이 아닌 중립 세력이 소유한 블록도 자동으로 분해합니다."},
            {"GrindJanitorDisableOnly",         "자동 정리: 작동 중지까지만 분해"},
            {"GrindJanitorDisableOnly_Tooltip", "활성화하면 기능 블록만 분해하며, 블록이 작동을 멈추는 상태까지만 분해합니다."},
            {"GrindJanitorHackOnly",            "자동 정리: 해킹 가능 상태까지만 분해"},
            {"GrindJanitorHackOnly_Tooltip",    "활성화하면 기능 블록만 분해하며, 블록을 해킹할 수 있는 상태까지만 분해합니다."},
            {"GrindPriority",                   "분해 우선순위"},
            {"GrindPriority_Tooltip",           "선택한 블록 종류의 분해 여부와 우선순위를 설정합니다.\n(분해 색상으로 지정된 블록은 우선순위와 해제 상태를 무시합니다.)"},
            {"GrindOrderNearest",               "가까운 블록 우선"},
            {"GrindOrderNearest_Tooltip",       "우선순위가 같을 경우 가장 가까운 블록부터 분해합니다."},
            {"GrindOrderFarthest",              "먼 블록 우선"},
            {"GrindOrderFarthest_Tooltip",      "우선순위가 같을 경우 가장 먼 블록부터 분해합니다."},
            {"GrindOrderSmallest",              "작은 그리드 우선"},
            {"GrindOrderSmallest_Tooltip",      "우선순위가 같을 경우 가장 작은 그리드의 블록부터 분해합니다."},
            {"GrindIgnorePriority",             "우선순위 순서 무시"},
            {"GrindIgnorePriority_Tooltip",     "활성화하면 우선순위 순서를 무시하고 거리만 기준으로 분해합니다. 블록 종류별 활성/비활성 설정은 그대로 적용됩니다."},

            {"CollectSettings_Headline",        "—————— 수집 설정 ——————"},
            {"CollectPriority",                 "수집 우선순위"},
            {"CollectPriority_Tooltip",         "선택한 아이템 종류의 수집 여부와 우선순위를 설정합니다."},
            {"CollectOnlyIfIdle",               "유휴 상태에서만 수집"},
            {"CollectOnlyIfIdle_Tooltip",       "활성화하면 용접이나 분해할 작업이 없을 때만 부유 아이템을 수집합니다."},
            {"CollectPushOre",                  "주괴/광석 즉시 이동"},
            {"CollectPushOre_Tooltip",          "활성화하면 수집한 주괴와 광석을 연결된 컨테이너로 즉시 이동합니다."},
            {"CollectPushItems",                "아이템 즉시 이동"},
            {"CollectPushItems_Tooltip",        "활성화하면 도구, 무기, 탄약, 가스통 등의 아이템을 연결된 컨테이너로 즉시 이동합니다."},
            {"CollectPushComp",                 "부품 즉시 이동"},
            {"CollectPushComp_Tooltip",         "활성화하면 수집한 부품을 연결된 컨테이너로 즉시 이동합니다."},

            {"Priority_Enable",                 "활성화"},
            {"Priority_Disable",                "비활성화"},
            {"Priority_Up",                     "우선순위 올리기"},
            {"Priority_Down",                   "우선순위 내리기"},
            {"Priority_EnableAll",              "모두 활성화"},
            {"Priority_DisableAll",             "모두 비활성화"},

            {"Color_PickCurrentColor",          "현재 건설 색상 가져오기"},
            {"Color_SetCurrentColor",           "현재 건설 색상으로 설정"},

            {"AreaShow",                        "작업 영역 표시"},
            {"AreaShow_Tooltip",                "활성화하면 이 시스템이 작업할 수 있는 영역을 표시합니다."},
            {"AreaWidth",                       "작업 영역 너비"},
            {"AreaHeight",                      "작업 영역 높이"},
            {"AreaDepth",                       "작업 영역 깊이"},
            {"RemoteCtrlBy",                    "원격 제어 대상"},
            {"RemoteCtrlBy_Tooltip",            "작업 영역의 중심이 선택한 캐릭터를 따라가도록 설정합니다. 캐릭터가 최대 범위 안에 있을 때만 적용됩니다."},
            {"RemoteCtrlBy_None",               "-없음-"},
            {"RemoteCtrlShowArea",              "작업 영역 표시 연동"},
            {"RemoteCtrlShowArea_Tooltip",      "캐릭터가 휴대용 용접기 또는 그라인더를 장착한 동안 '작업 영역 표시'를 활성화할지 선택합니다."},
            {"RemoteCtrlWorking",               "작업 동작 연동"},
            {"RemoteCtrlWorking_Tooltip",       "캐릭터가 휴대용 용접기 또는 그라인더를 장착한 동안에만 시스템이 작동하도록 설정합니다."},
            {"SoundVolume",                     "효과음 볼륨"},
            {"DisableTickingSound",             "틱/작동 불가 소리 끄기"},
            {"DisableTickingSound_Tooltip",     "활성화하면 이 블록의 틱 소리 및 작동 불가 알림음을 끕니다."},
            {"DisableParticleEffects",          "비행 나노봇 효과 끄기"},
            {"DisableParticleEffects_Tooltip",  "활성화하면 이 블록의 나노봇 이동 궤적 효과(수거/운반)를 끕니다. 용접 및 분해 불꽃 효과에는 영향을 주지 않습니다."},
            {"ResetAllSettings",                "모든 설정 초기화"},
            {"ResetAllSettings_Tooltip",        "우선순위 목록 상태를 포함하여 이 블록의 모든 설정을 기본값으로 되돌립니다."},
            {"ScriptControlled",                "스크립트로 제어"},
            {"ScriptControlled_Tooltip",        "활성화하면 시스템이 블록을 자동으로 건설/수리하지 않습니다. 스크립트 함수로 각 작업 대상을 직접 지정해야 합니다."},

            {"Info_CurrentWeldEntity",           "현재 용접 대상:"},
            {"Info_CurrentGrindEntity",          "현재 분해 대상:"},
            {"Info_InventoryFull",              "블록 인벤토리가 가득 찼습니다!"},
            {"Info_LimitReached",               "PCU 한도에 도달했습니다!"},
            {"Info_DisabledByRemote",           "원격 제어로 비활성화되었습니다!"},
            {"Info_BlocksToBuild",              "건설할 블록:"},
            {"Info_BlocksToGrind",              "분해할 블록:"},
            {"Info_ItemsToCollect",             "수집할 부유 아이템:"},
            {"Info_More",                       " -.."},
            {"Info_MissingItems",               "부족한 부품:"},
            {"Info_BlockSwitchedOff",           "블록이 꺼져 있습니다"},
            {"Info_BlockDamaged",               "블록이 손상되었거나 미완성 상태입니다"},
            {"Info_BlockUnpowered",             "블록에 전력이 부족합니다"},
            {"Cmd_HelpClient",                  "버전: {0}" +
                                                "\n사용 가능한 명령어:" +
                                                "\n[{1};{2}]: 이 도움말을 표시합니다" +
                                                "\n[{3} {4};{5}]: 현재 로그 수준을 설정합니다. 주의: 로그 수준을 '{4}'(으)로 설정하면 매우 큰 로그 파일이 생성될 수 있습니다" +
                                                "\n[{6} {7}]: 선택한 언어의 현재 번역을 {8} 위치의 파일로 내보냅니다"},
            {"Cmd_HelpServer",                  "\n[{0}]: 현재 월드 폴더 안에 설정 파일을 생성합니다. 재시작 후에는 전역 모드 설정 파일 대신 이 파일의 설정을 사용합니다." +
                                                "\n[{1}]: 모든 옵션을 포함한 전역 설정 파일을 모드 폴더 안에 생성합니다."}
        };
    }
}
