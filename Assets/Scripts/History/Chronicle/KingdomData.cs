using System;
using System.Collections.Generic;

namespace ARPG.History
{
    /// <summary>
    /// L1 시뮬 결과로 정착지들이 묶인 정치 단위. 수도 정착지 1개 보유.
    /// FoundingRace(건국 종족)로 피정복 타종족과 대비 → 다종족 제국의 지배/피지배 구분.
    /// 설계: PROCEDURAL_HISTORY_DESIGN.md §3.4
    /// </summary>
    [Serializable]
    public class KingdomData
    {
        public int KingdomId;
        public string Name = string.Empty;          // v1은 "{종족} 왕국 N" 수준 절차 작명
        public GlobalEnum.RaceType FoundingRace;     // 건국 종족(= 수도 종족)
        public int CapitalSettlementId = -1;
        public List<int> MemberSettlementIds = new(); // 정복으로 타종족 정착지 포함 가능 → 다종족 제국

        public KingdomData() { }

        public KingdomData(int kingdomId, GlobalEnum.RaceType foundingRace, int capitalSettlementId)
        {
            KingdomId = kingdomId;
            FoundingRace = foundingRace;
            CapitalSettlementId = capitalSettlementId;
        }
    }
}
