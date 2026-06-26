using System;

namespace ARPG.Npc
{
    /// <summary>
    /// NPC → 정착지 단위 감정 (피정복 반감, 왕국 자부심 등).
    /// 상대 마을 NPC가 아직 미스폰이어도 표현 가능하도록 개인 → 정착지 태그로 NpcSaveData에 둔다.
    /// 설계: NPC_RELATION_HISTORY_DESIGN.md §4.2
    /// </summary>
    [Serializable]
    public class NpcSentiment
    {
        public NpcSentimentType Type;
        public int TargetSettlementId = -1;   // 감정의 대상 정착지 (-1 = 자기 마을)
        public int OriginEventId = -1;

        public NpcSentiment() { }

        public NpcSentiment(NpcSentimentType type, int targetSettlementId = -1, int originEventId = -1)
        {
            Type = type;
            TargetSettlementId = targetSettlementId;
            OriginEventId = originEventId;
        }
    }

    public enum NpcSentimentType
    {
        LostFamilyToWar = 0,
        ProudOfKingdom = 1,
        ResentsMerge = 2,
        ResentsKingdom = 3,
    }
}
