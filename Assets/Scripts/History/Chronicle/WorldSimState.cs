using System;

namespace ARPG.History
{
    /// <summary>
    /// 런타임 추상 층(runtime LOD) — 게임 중 계속 변동하는 정착지 라이브 상태.
    /// ChronicleSaveData.Settlements(정지된 New Game 스냅샷)와 분리된 정본으로, WorldData에 영속화된다.
    /// PROCEDURAL_HISTORY_DESIGN.md §0.3.
    ///
    /// 성능 규약(CLAUDE.md): 배열 + struct — 근거리 실체화되지 않은 정착지 수천 개를
    /// 엔티티 없이 정수/실수 몇 개로만 표현하고, System_SettlementSim이 커서 기반으로 조금씩 순회한다.
    /// (배열이라 tick 시 arr[i] 제자리 변경으로 값 복사·GC 없음 — List&lt;struct&gt;는 인덱서가 복사본을 반환하므로 불가)
    /// </summary>
    [Serializable]
    public class WorldSimState
    {
        public int Version = 1;
        public SettlementLiveState[] Settlements = Array.Empty<SettlementLiveState>();

        public bool IsInitialized => Settlements != null && Settlements.Length > 0;
    }

    /// <summary>
    /// 정착지 1개의 런타임 추상 상태. struct — 배열 제자리 tick으로 GC 회피.
    /// 실체화(근처)되면 <see cref="IsMaterialized"/>=true가 되어 추상 tick에서 제외되고,
    /// VillageData/ECS(VillageComponent)가 상세 시뮬을 이어받는다. 두 층은 <see cref="SettlementId"/> ↔ VillageId로 연결.
    /// </summary>
    [Serializable]
    public struct SettlementLiveState
    {
        public int SettlementId;             // = ChronicleSaveData.Settlements의 SettlementId (placement 인덱스)
        public GlobalEnum.RaceType Race;
        public GlobalEnum.BiomeType Biome;   // 인구 성장 모델(BiomeData.GrowthRate/Carry)이 사용
        public int WorldCoordX;              // materialize 위치 매칭용 (Phase 2)
        public int WorldCoordY;
        public float Pop;                    // 라이브 추상 인구. float — 하루 성장이 1 미만이어도 누적되도록(정수면 정체)
        public int KingdomId;
        public SettlementFate Fate;
        public int FoundedYear;

        // 시간 분할 tick의 핵심: 정착지마다 자기 시계를 갖고 "그동안 며칠 지났나"를 스스로 계산해 전진.
        // 단위는 게임-시간(hour, = AR.s.Time.CurrentGameTime).
        public float LastSimGameTime;

        public bool IsMaterialized;          // true = 추상 tick 제외 (근처 = ECS가 처리 중)
        public int VillageId;                // 실체화 링크 (-1 = 미실체화)
    }
}
