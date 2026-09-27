namespace ARPG.Component
{
    /// <summary>
    /// 실전투 허용 NPC 태그 (NPC_COMBAT_DESIGN §3.4).
    /// 이 태그가 있는 NPC만 System_AI_Perception에서 적을 인식한다. 동행·결투 시작 때 붙이고 끝날 때 뗀다.
    /// NpcActivityPausedTag가 함께 있으면 인식에서 제외한다(사냥 결과는 계산 판정 소관).
    /// </summary>
    public struct NpcCombatEnabledTag
    {
    }
}
