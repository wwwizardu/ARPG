using System;
using System.Collections.Generic;

namespace ARPG.History
{
    /// <summary>
    /// 연대기 + 정치 스냅샷의 직렬화 루트. WorldData.Chronicle에 보관.
    /// **저장이 정본** — 한 번 생성된 연대기는 시드로 재생성하지 않는다 (설계 §3.4).
    /// 설계: PROCEDURAL_HISTORY_DESIGN.md §3.4
    /// </summary>
    [Serializable]
    public class ChronicleSaveData
    {
        public int Version = 1;
        public int SimulatedYears;
        public List<ChronicleEvent> Events = new();          // append-only, 수백 건 수준
        public List<SettlementSnapshot> Settlements = new(); // 최종 상태 스냅샷
        public List<KingdomData> Kingdoms = new();
    }
}
