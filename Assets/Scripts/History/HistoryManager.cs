using System.Collections.Generic;
using UnityEngine;
using ARPG.Map;
using ARPG.Village;

namespace ARPG.History
{
    /// <summary>
    /// 절차적 역사 시스템의 진입점. AR.s.History로 접근.
    /// Phase 0: 골격만 — Initialize/Reset 호출 시 로그만 출력.
    /// Phase 1 이후: GenerateOrLoad(worldSeed)에서 카탈로그 로드 + 백본 생성.
    /// </summary>
    public class HistoryManager : MonoBehaviour
    {
        private bool _generated = false;
        private int _worldSeed = 0;

        // PROCEDURAL §0 런타임 LOD — 추상 인구 tick 시간 분할용 커서 (런타임 전용, 세이브 아님)
        private int _abstractCursor = 0;
        private const float DAY_HOURS = 24f;   // 게임 1일 = 24 게임-시간 (AR.s.Time.CurrentGameTime 단위)

        public bool IsGenerated => _generated;
        public int WorldSeed => _worldSeed;

        public void Initialize()
        {
            Debug.Log("HistoryManager Initialized");
            GenerateSettlementChronicleIfNeeded();
            EnsureWorldSimInitialized();
        }

        /// <summary>
        /// R1: 정착지 연대기를 New Game 시 1회 생성한다. 이미 연대기가 있으면(로드) 건너뛴다 — 저장이 정본.
        /// AR.Initialize 마지막 단계에서 호출되므로 Map/WorldData가 준비된 상태다.
        /// 비용은 New Game 1회뿐(로드 시 가드로 skip).
        /// </summary>
        private void GenerateSettlementChronicleIfNeeded()
        {
            ChronicleSaveData existing = AR.s.Data.Chronicle;
            if (existing != null && existing.Events != null && existing.Events.Count > 0)
            {
                _worldSeed = AR.s.Map.mapSeed;
                _generated = true;
                Debug.Log($"[HistoryManager] Chronicle already present ({existing.Events.Count} events) — skip generation.");
                return;
            }

            int mapSeed = AR.s.Map.mapSeed;
            List<RaceSettlementPlacer.SettlementPlacement> placements = RaceSettlementPlacer.ComputeAll(
                mapSeed, AR.s.Map.chunkSize,
                AR.s.Map.minChunkX, AR.s.Map.maxChunkX, AR.s.Map.minChunkY, AR.s.Map.maxChunkY);

            ChronicleSaveData chronicle = SettlementChronicleSimulator.Simulate(mapSeed, placements);
            AR.s.Data.Chronicle = chronicle;
            _worldSeed = mapSeed;
            _generated = true;

            Debug.Log($"[HistoryManager] Settlement chronicle generated: seed={mapSeed}, " +
                      $"{placements.Count} settlements, {chronicle.Events.Count} events, {chronicle.Kingdoms.Count} kingdoms.");
        }

        public void Reset()
        {
            _generated = false;
            _worldSeed = 0;
            _abstractCursor = 0;
            HistoryTagRegistry.Reset();
            Debug.Log("HistoryManager Reset");
        }

        /// <summary>
        /// 월드 시드를 기반으로 Past Backbone을 생성하거나 세이브에서 로드한다.
        /// Phase 0: 골격 — 호출은 받지만 실제 생성은 Phase 1 이후.
        /// </summary>
        public void GenerateOrLoad(int worldSeed)
        {
            _worldSeed = worldSeed;
            _generated = true;
            Debug.Log($"[HistoryManager] GenerateOrLoad(seed={worldSeed}) — Phase 0 stub");
        }

        /// <summary>
        /// R3: 게임 중 발생한 사건을 연대기에 append (append-only, EventId 이어서 발급).
        /// 예: 마을 전멸(Ruin), VillageStage 승격(Promotion), 플레이어 개입.
        /// 연대기가 없으면(미생성) 무시. Year는 생성 시뮬 기간(현재 = SimulatedYears)으로 표기.
        /// </summary>
        public void AppendLiveEvent(ChronicleEventType type, int subjectSettlementId,
            int objectSettlementId = -1, int kingdomId = -1)
        {
            ChronicleSaveData c = AR.s.Data.Chronicle;
            if (c == null)
                return;
            if (c.Events == null)
                c.Events = new List<ChronicleEvent>();

            int nextId = c.Events.Count > 0 ? c.Events[c.Events.Count - 1].EventId + 1 : 0;
            c.Events.Add(new ChronicleEvent(nextId, c.SimulatedYears, type, subjectSettlementId, objectSettlementId, kingdomId));
        }

        /// <summary>
        /// settlementId의 현재 인구를 반환한다.
        /// PROCEDURAL §0 런타임 LOD: 라이브 추상 값(WorldSim, 시간에 따라 변동)을 우선 조회하고,
        /// 미초기화 시 New Game 스냅샷(Chronicle)으로 폴백. 링크 없음/미생성이면 0.
        /// 용도: 마을 첫 population 시 NPC 스폰 수를 인구에 비례시키기 위한 조회.
        /// </summary>
        public int GetSettlementPopulation(int settlementId)
        {
            if (settlementId < 0)
                return 0;

            // 1) 런타임 라이브 층 우선
            if (TryGetLiveIndex(settlementId, out int idx))
                return (int)AR.s.Data.WorldSim.Settlements[idx].Pop;

            // 2) 폴백: New Game 스냅샷
            ChronicleSaveData c = AR.s.Data.Chronicle;
            if (c == null || c.Settlements == null)
                return 0;

            for (int i = 0; i < c.Settlements.Count; i++)
            {
                if (c.Settlements[i].SettlementId == settlementId)
                    return c.Settlements[i].Population;
            }
            return 0;
        }

        /// <summary>
        /// settlementId → WorldSim.Settlements 배열 인덱스. 없으면 false.
        /// SettlementId == 배열 인덱스(placement 순서)가 일반적이라 가드 후 직접 인덱스, 아니면 선형 스캔.
        /// </summary>
        private bool TryGetLiveIndex(int settlementId, out int index)
        {
            index = -1;
            if (settlementId < 0)
                return false;

            WorldSimState sim = AR.s.Data.WorldSim;
            if (sim == null || sim.Settlements == null || sim.Settlements.Length == 0)
                return false;

            SettlementLiveState[] arr = sim.Settlements;
            if (settlementId < arr.Length && arr[settlementId].SettlementId == settlementId)
            {
                index = settlementId;
                return true;
            }
            for (int i = 0; i < arr.Length; i++)
            {
                if (arr[i].SettlementId == settlementId)
                {
                    index = i;
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// PROCEDURAL §0: WorldSim(라이브 추상 층)을 Chronicle 스냅샷에서 초기화한다.
        /// New Game 직후(방금 생성) 또는 구세이브(Chronicle은 있으나 WorldSim 없음)에서 1회 채운다.
        /// 이미 라이브 상태가 있으면(세이브 복원) 보존하고 skip.
        /// </summary>
        private void EnsureWorldSimInitialized()
        {
            WorldSimState sim = AR.s.Data.WorldSim;
            if (sim == null)
                return;
            if (sim.IsInitialized)
                return;   // 세이브에서 복원된 라이브 상태 보존

            ChronicleSaveData c = AR.s.Data.Chronicle;
            if (c == null || c.Settlements == null || c.Settlements.Count == 0)
                return;   // 연대기 없음 → 초기화할 것 없음

            float now = AR.s.Time != null ? AR.s.Time.CurrentGameTime : 0f;
            SettlementLiveState[] arr = new SettlementLiveState[c.Settlements.Count];
            for (int i = 0; i < c.Settlements.Count; i++)
            {
                SettlementSnapshot snap = c.Settlements[i];
                arr[i] = new SettlementLiveState
                {
                    SettlementId = snap.SettlementId,
                    Race = snap.Race,
                    Biome = snap.Biome,
                    WorldCoordX = snap.WorldCoordX,
                    WorldCoordY = snap.WorldCoordY,
                    Pop = snap.Population,
                    KingdomId = snap.KingdomId,
                    Fate = snap.Fate,
                    FoundedYear = snap.FoundedYear,
                    LastSimGameTime = now,
                    IsMaterialized = false,
                    VillageId = -1,
                };
            }
            sim.Settlements = arr;
            Debug.Log($"[HistoryManager] WorldSim initialized from chronicle: {arr.Length} settlements @ t={now:F1}h");
        }

        /// <summary>
        /// PROCEDURAL §0 런타임 추상 인구 tick — 시간 분할(라운드로빈).
        /// 매 호출마다 커서에서 budget개만 처리하여 프레임당 비용을 상수로 유지(액션 게임 스파이크 방지).
        /// 각 정착지는 자기 LastSimGameTime 기준 경과 일수만큼 O(1)로 전진(<see cref="SettlementPopulationModel"/>).
        /// 실체화(근처)된 정착지는 ECS/VillageData가 처리하므로 제외.
        /// </summary>
        public void AdvanceAbstractSlice(int budget)
        {
            WorldSimState sim = AR.s.Data.WorldSim;
            if (sim == null || sim.Settlements == null)
                return;

            int count = sim.Settlements.Length;
            if (count == 0 || budget <= 0)
                return;

            float now = AR.s.Time.CurrentGameTime;
            int steps = budget < count ? budget : count;   // 한 번에 전체 이상 돌 필요 없음

            for (int n = 0; n < steps; n++)
            {
                if (_abstractCursor >= count)
                    _abstractCursor = 0;
                int i = _abstractCursor++;

                if (sim.Settlements[i].IsMaterialized)
                    continue;   // 근처 = ECS가 처리 중

                int days = (int)((now - sim.Settlements[i].LastSimGameTime) / DAY_HOURS);
                if (days <= 0)
                    continue;   // 아직 하루 안 지남 → 자동 스로틀(early-out)

                SettlementPopulationModel.Advance(ref sim.Settlements[i], days);
                sim.Settlements[i].LastSimGameTime += days * DAY_HOURS;
            }
        }

        /// <summary>
        /// PROCEDURAL §0 materialize/fold-back 전환 구동 — 매 tick 호출(System_SettlementSim).
        /// 배치된 마을(연대기 연결)의 중심 청크 활성 여부와 라이브 상태(IsMaterialized)를 비교해:
        ///   상승 에지(근처가 됨 + 미실체화)  → Materialize (추상→상세, 인구 flush + 동결)
        ///   하강 에지(멀어짐 + 실체화됨)      → FoldBack   (상세→추상, 동결 해제 + tick 재개)
        /// 상태는 LiveState.IsMaterialized가 보관 — 이 메서드/시스템은 무상태.
        /// 중심 청크를 근접 프록시로 사용(마을이 load 반경보다 작아 충분).
        /// </summary>
        public void UpdateMaterialization()
        {
            WorldSimState sim = AR.s.Data.WorldSim;
            if (sim == null || sim.Settlements == null || sim.Settlements.Length == 0)
                return;
            if (AR.s.Map == null || AR.s.Village == null)
                return;

            int chunkSize = AR.s.Map.chunkSize;
            if (chunkSize <= 0)
                return;

            foreach (VillageData v in AR.s.Village.GetAllVillages())
            {
                if (v.SettlementId < 0)
                    continue;   // 수제작 마을(연대기 미연결) — 추상 층 미참여
                if (TryGetLiveIndex(v.SettlementId, out int idx) == false)
                    continue;

                Vector2Int chunk = new Vector2Int(
                    Mathf.FloorToInt(v.PositionX / chunkSize),
                    Mathf.FloorToInt(v.PositionY / chunkSize));
                bool near = AR.s.Map.IsChunkActive(chunk);
                bool materialized = sim.Settlements[idx].IsMaterialized;

                if (near && materialized == false)
                    MaterializeSettlement(v.SettlementId, v.VillageId);
                else if (near == false && materialized)
                    FoldBackSettlement(v.SettlementId);
            }
        }

        /// <summary>
        /// PROCEDURAL §0.2 materialize (추상→상세): 정착지가 플레이어 근처가 되어 실체화될 때 1회 호출.
        /// 라이브 인구를 현재 시각으로 flush(catch-up)한 뒤 추상 tick에서 제외(IsMaterialized)하고 VillageId를 링크한다.
        /// 이후 인구는 VillageData/ECS가 상세 시뮬하며(NPC 스폰 수는 flush된 최신 Pop 기준),
        /// 이탈 시 FoldBack(Phase 3)이 라이브 상태로 되돌린다.
        /// </summary>
        public void MaterializeSettlement(int settlementId, int villageId)
        {
            if (TryGetLiveIndex(settlementId, out int idx) == false)
                return;

            WorldSimState sim = AR.s.Data.WorldSim;
            float now = AR.s.Time.CurrentGameTime;

            // 커서가 아직 못 들른 경우 대비 — 사용 직전 현재 시각으로 인구 flush
            int days = (int)((now - sim.Settlements[idx].LastSimGameTime) / DAY_HOURS);
            if (days > 0)
                SettlementPopulationModel.Advance(ref sim.Settlements[idx], days);

            sim.Settlements[idx].LastSimGameTime = now;
            sim.Settlements[idx].IsMaterialized = true;
            sim.Settlements[idx].VillageId = villageId;

            Debug.Log($"[HistoryManager] Materialize settlement {settlementId} → village {villageId} " +
                      $"(pop={(int)sim.Settlements[idx].Pop}, +{days}d catch-up)");
        }

        /// <summary>
        /// PROCEDURAL §0.2 fold-back (상세→추상): 실체화된 정착지가 플레이어에게서 멀어질 때 1회 호출.
        /// 동결(IsMaterialized)을 해제하고 추상 tick을 현재 시각부터 재개한다 → 떠난 마을이 다시 살아 변동.
        ///
        /// ★ v1은 물리 상태를 Pop에 역환산하지 않는다(=Pop 유지). 물리 NPC 수는 상한(NPC_POP_CAP) 걸린
        ///   '표본'이라, 계획서의 "생존 NPC → Pop 역함수"는 큰 정착지에서 정보 손실이 크다(saturate).
        ///   플레이어가 일으킨 인구 변화(부분 사상자/전멸)의 추상 반영은 전용 메커니즘으로 분리(후속).
        ///   단 전멸은 기존 Ruin 연대기 이벤트(NpcManager)로 이미 기록되므로 사건 자체는 유실되지 않는다.
        ///
        /// LastSimGameTime=now로 재설정 → 방문 구간은 물리 계층이 담당했으므로 추상 성장으로 소급하지 않고
        /// (동결 값에서의 불연속 점프 방지) now부터 이어서 굴린다.
        /// </summary>
        public void FoldBackSettlement(int settlementId)
        {
            if (TryGetLiveIndex(settlementId, out int idx) == false)
                return;

            WorldSimState sim = AR.s.Data.WorldSim;
            float now = AR.s.Time.CurrentGameTime;

            sim.Settlements[idx].IsMaterialized = false;
            sim.Settlements[idx].VillageId = -1;
            sim.Settlements[idx].LastSimGameTime = now;

            Debug.Log($"[HistoryManager] FoldBack settlement {settlementId} " +
                      $"(pop={(int)sim.Settlements[idx].Pop}) — 추상 tick 재개");
        }

        /// <summary>
        /// 청크 활성화 시 호출. Phase 4 이후 AmbientTag → VFX 트리거, Lore 아이템 spawn 등을 처리.
        /// </summary>
        public void OnChunkActivated(Vector2Int chunkCoord)
        {
            // Phase 0: no-op
        }
    }
}
