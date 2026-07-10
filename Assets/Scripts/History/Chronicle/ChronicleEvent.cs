using System;

namespace ARPG.History
{
    /// <summary>
    /// 정착지 연대기의 단일 사건. append-only (기존 사건은 수정하지 않고 추가만).
    /// L1 시뮬(New Game) + 게임 중 Live append로 누적된다.
    /// 설계: PROCEDURAL_HISTORY_DESIGN.md §3.3
    /// </summary>
    [Serializable]
    public class ChronicleEvent
    {
        public int EventId;                 // 발급 순번 (append-only)
        public int Year;
        public ChronicleEventType Type;
        public int SubjectSettlementId;
        public int ObjectSettlementId = -1; // 단독 사건이면 -1
        public int KingdomId = -1;          // 관련 없으면 -1

        public ChronicleEvent() { }

        public ChronicleEvent(int eventId, int year, ChronicleEventType type,
            int subjectSettlementId, int objectSettlementId = -1, int kingdomId = -1)
        {
            EventId = eventId;
            Year = year;
            Type = type;
            SubjectSettlementId = subjectSettlementId;
            ObjectSettlementId = objectSettlementId;
            KingdomId = kingdomId;
        }
    }

    public enum ChronicleEventType
    {
        Trade = 0,
        Skirmish = 1,
        War = 2,
        Conquer = 3,        // Tier 2 (정복-병합)
        Alliance = 4,
        Merge = 5,
        Ruin = 6,
        KingdomFound = 7,
        KingdomJoin = 8,
        Promotion = 9,      // VillageStage 승격 (Live append)
        Fell = 10,          // NPC 오프스크린 활동 중 영구 사망 (Live append)
    }
}
