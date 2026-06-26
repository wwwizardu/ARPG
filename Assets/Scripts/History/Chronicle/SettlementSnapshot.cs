using System;
using Newtonsoft.Json;
using UnityEngine;

namespace ARPG.History
{
    /// <summary>
    /// L1 시뮬 최종 상태의 직렬화 스냅샷 (= SettlementSimState에서 시뮬-전용 필드 제외).
    /// 런타임 추상 상태(틱마다 변동)는 별도 SettlementLiveState(런타임 LOD 트랙, PROCEDURAL §0.3).
    /// 설계: PROCEDURAL_HISTORY_DESIGN.md §3.1/§3.4
    /// </summary>
    [Serializable]
    public class SettlementSnapshot
    {
        public int SettlementId;             // placement 인덱스 기반 결정론 ID
        public GlobalEnum.RaceType Race;
        // 프로젝트 직렬화 규약: Unity 벡터 타입을 저장하지 않고 int X/Y로 분해 (NpcSaveData.Position과 동일 패턴)
        public int RegionCoordX;
        public int RegionCoordY;
        public int Population;
        public int KingdomId = -1;           // 무소속 = -1
        public SettlementFate Fate;
        public int MergedIntoId = -1;        // Fate == Merged일 때
        public int ConqueredByKingdomId = -1; // Fate == Conquered일 때 (정복국 KingdomId). 정착지는 생존
        public int FoundedYear;

        [JsonIgnore]
        public Vector2Int RegionCoord
        {
            get => new Vector2Int(RegionCoordX, RegionCoordY);
            set { RegionCoordX = value.x; RegionCoordY = value.y; }
        }
    }

    public enum SettlementFate
    {
        Alive = 0,
        Ruined = 1,
        Merged = 2,
        Conquered = 3,
    }
}
