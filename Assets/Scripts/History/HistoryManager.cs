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
    public partial class HistoryManager : MonoBehaviour
    {
        private bool _generated = false;
        private int _worldSeed = 0;

        // PROCEDURAL §0 런타임 LOD — 추상 인구 tick 시간 분할용 커서 (런타임 전용, 세이브 아님)
        private int _abstractCursor = 0;
        private const int MATERIALIZE_GENERAL_EVENT_BUDGET = 128;
        private const int MATERIALIZE_WALL_EVENT_BUDGET = 64;

        public bool IsGenerated => _generated;
        public int WorldSeed => _worldSeed;

        public void Initialize()
        {
            Debug.Log("HistoryManager Initialized");
            bool generatedNewGameHistory = GenerateSettlementChronicleIfNeeded();
            EnsureWorldSimInitialized();
            AR.s.Village.EnsureVillageProfiles();
            if (generatedNewGameHistory == true)
            {
                BootstrapNewGameVillageDevelopment();
                EnsureNewGameMinimumForges();
            }
            RebuildSaveSnapshot();
        }

        /// <summary>
        /// R1: 정착지 연대기를 New Game 시 1회 생성한다. 이미 연대기가 있으면(로드) 건너뛴다 — 저장이 정본.
        /// AR.Initialize 마지막 단계에서 호출되므로 Map/WorldData가 준비된 상태다.
        /// 비용은 New Game 1회뿐(로드 시 가드로 skip).
        /// </summary>
        private bool GenerateSettlementChronicleIfNeeded()
        {
            ChronicleSaveData existing = AR.s.Data.Chronicle;
            if (existing != null && existing.Events != null && existing.Events.Count > 0)
            {
                _worldSeed = AR.s.Map.mapSeed;
                _generated = true;
                Debug.Log($"[HistoryManager] Chronicle already present ({existing.Events.Count} events) — skip generation.");
                return false;
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
            return true;
        }

        public void Reset()
        {
            _settlementSaveSnapshot.Reset();
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
        public int AppendLiveEvent(ChronicleEventType type, int subjectSettlementId,
            int objectSettlementId = -1, int kingdomId = -1)
        {
            ChronicleSaveData c = AR.s.Data.Chronicle;
            if (c == null)
                return -1;
            if (c.Events == null)
                c.Events = new List<ChronicleEvent>();

            int nextId = c.Events.Count > 0 ? c.Events[c.Events.Count - 1].EventId + 1 : 0;
            c.Events.Add(new ChronicleEvent(nextId, c.SimulatedYears, type, subjectSettlementId, objectSettlementId, kingdomId));
            return nextId;
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

        public bool TryGetLiveSettlement(int settlementId,
            out SettlementLiveState settlement)
        {
            if (TryGetLiveIndex(settlementId, out int index) == true)
            {
                settlement = AR.s.Data.WorldSim.Settlements[index];
                return true;
            }
            settlement = default;
            return false;
        }

        /// <summary>
        /// Village Profile resolver 입력을 저장된 History 정본에서 조회한다.
        /// WorldSim이 있으면 라이브 스냅샷을, 구 세이브처럼 비어 있으면 Chronicle을 사용한다.
        /// </summary>
        public bool TryGetVillageProfileContext(int settlementId,
            out VillageProfileContext context)
        {
            if (TryGetLiveSettlement(settlementId,
                    out SettlementLiveState liveSettlement) == true)
            {
                context = new VillageProfileContext(liveSettlement.Race,
                    liveSettlement.Biome,
                    ResolveVillageSettlementRole(settlementId, liveSettlement.Fate));
                return true;
            }

            ChronicleSaveData chronicle = AR.s.Data.Chronicle;
            if (chronicle != null && chronicle.Settlements != null)
            {
                for (int i = 0; i < chronicle.Settlements.Count; i++)
                {
                    SettlementSnapshot snapshot = chronicle.Settlements[i];
                    if (snapshot.SettlementId != settlementId)
                        continue;

                    context = new VillageProfileContext(snapshot.Race,
                        snapshot.Biome,
                        ResolveVillageSettlementRole(settlementId, snapshot.Fate));
                    return true;
                }
            }

            context = default;
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

        private VillageSettlementRole ResolveVillageSettlementRole(int settlementId,
            SettlementFate fate)
        {
            if (fate == SettlementFate.Conquered)
                return VillageSettlementRole.Conquered;

            ChronicleSaveData chronicle = AR.s.Data.Chronicle;
            if (chronicle != null && chronicle.Kingdoms != null)
            {
                for (int i = 0; i < chronicle.Kingdoms.Count; i++)
                {
                    KingdomData kingdom = chronicle.Kingdoms[i];
                    if (kingdom != null
                        && kingdom.CapitalSettlementId == settlementId)
                    {
                        return VillageSettlementRole.Capital;
                    }
                }
            }
            return VillageSettlementRole.Standard;
        }

        /// <summary>
        /// V2 (VILLAGE_REWORK_PLAN §3.2/§3.3): New Game 1회, 고정 역사 완료 인구로 초기 Stage를
        /// 직접 확정한다. synthetic 시간 진행·자동 건설을 실행하지 않으므로 진행 task가 남지 않는다.
        /// 초기 Stage 입력은 원본 역사 인구(raw)이며, 경제 인구(1~25)는 생산량 계산 전용이다.
        /// Stage별 누적 시설·상점·Cap 생성은 V3에서 이 지점에 이어 붙인다.
        /// </summary>
        private void BootstrapNewGameVillageDevelopment()
        {
            WorldSimState sim = AR.s.Data.WorldSim;
            if (sim == null || sim.Settlements == null || sim.Settlements.Length == 0)
                return;

            float runtimeGameTime = AR.s.Time.CurrentGameTime;
            int applied = 0;
            int skippedNotOperational = 0;
            int milestonesApplied = 0;
            int facilitiesCreated = 0;
            int pendingPlacements = 0;
            int initialWallVillages = 0;
            for (int i = 0; i < sim.Settlements.Length; i++)
            {
                SettlementLiveState settlement = sim.Settlements[i];
                if (VillageDevelopmentBootstrap.IsOperationalFate(settlement.Fate)
                    == false)
                {
                    skippedNotOperational++;
                    continue;
                }

                VillageData village = AR.s.Village.GetVillageBySettlement(
                    settlement.SettlementId);
                if (village == null || village.InitialDevelopmentApplied == true)
                    continue;

                // 경제 인구는 고정 raw 인구의 정량화 값 — 생산량 계산에만 쓰인다.
                // GetSettlementPopulation과 같은 (int) 절사를 사용해 이후 tick 동기화와 일치시킨다.
                AR.s.Village.MarkSaveDirty(village.VillageId);
                village.Population = VillageManager.QuantizeAbstractPopulation(
                    (int)settlement.Pop);

                VillageStage maxStage = VillageStage.City;
                VillageProfile resolvedProfile = null;
                if (AR.s.Village.TryGetVillageProfile(village.VillageId,
                        out VillageProfile profile) == true)
                {
                    resolvedProfile = profile;
                    maxStage = profile.MaxStage;
                }
                else
                {
                    Debug.LogWarning($"[VillageBootstrap] v{village.VillageId} profile " +
                        "미확정 — MaxStage 상한 없이 초기 Stage를 계산합니다.");
                }

                VillageStage initialStage = VillageProgressionRules.CalculateInitialStage(
                    Mathf.RoundToInt(settlement.Pop), maxStage);

                // V4 (VILLAGE_REWORK_PLAN §7.3/§7.4): 초기 Bounds는 LayoutProfile의 콤팩트
                // 기능 구역 반경을 우선 사용하고, 레이아웃이 정의하지 않은 Profile은
                // 기존 Stage 테이블 반경을 유지한다.
                VillageLayoutProfile layout = VillageLayoutCatalog.Resolve(
                    resolvedProfile != null ? resolvedProfile.LayoutProfileId : null);
                int boundsRadius = layout.GetInitialBoundsRadius(initialStage);
                if (boundsRadius <= 0)
                    boundsRadius = VillageManager.GetBoundsRadius(initialStage);
                VillageDevelopmentBootstrap.ApplyFixedInitialState(village, initialStage,
                    boundsRadius, runtimeGameTime);
                applied++;

                // V4 (§7.5 1안): 기능성 벽 레이아웃은 Hamlet 이상 초기 Stage에서 Bounds 둘레
                // 완공 Palisade/Gate 세그먼트를 자원·task 없이 생성한다. 타일 배치는 첫
                // materialize(RestoreWallTilesFromData)가 담당한다. Dwarf 청사진의 장식 경계
                // Id 1은 로드 시 제거되므로 청사진 경계와 런타임 벽이 중복되지 않는다.
                if (layout.HasFunctionalWalls == true
                    && VillageWallRules.ApplyInitialCompletedWalls(village,
                        initialStage) == true)
                    initialWallVillages++;

                // V3 (VILLAGE_REWORK_PLAN §3.5): Settlement→InitialStage 누적 마일스톤을
                // 자원·건설시간·진행 task 없이 완공 원장으로 1회 적용한다. 시설은 좌표 미할당
                // 완공 레코드(PlacementPending)로 저장되고 첫 materialize에서 배치된다.
                VillageGrowthPlan plan = VillageInitialDevelopment.ResolvePlan(
                    resolvedProfile, out string planWarning);
                if (string.IsNullOrEmpty(planWarning) == false)
                    Debug.LogWarning($"[VillageBootstrap] v{village.VillageId} {planWarning}");

                AR.s.Village.MarkSaveDirty(village.VillageId);
                VillageInitialDevelopmentResult milestones =
                    VillageInitialDevelopment.ApplyInitialMilestones(village, plan,
                        initialStage, AR.s.Data.GetBuildableItem);
                for (int e = 0; e < milestones.Errors.Count; e++)
                    Debug.LogError($"[VillageBootstrap] v{village.VillageId} 초기 시설 " +
                        $"데이터 검증 실패: {milestones.Errors[e]}");
                for (int w = 0; w < milestones.Warnings.Count; w++)
                    Debug.LogWarning($"[VillageBootstrap] v{village.VillageId} " +
                        $"{milestones.Warnings[w]}");
                milestonesApplied += milestones.MilestonesApplied;
                facilitiesCreated += milestones.FacilitiesCreated;
                pendingPlacements +=
                    VillageInitialDevelopment.CountPendingInitialPlacements(village);
            }

            Debug.Log($"[VillageBootstrap] New Game 초기 Stage 확정: applied={applied}, " +
                $"notOperational={skippedNotOperational}, settlements={sim.Settlements.Length}, " +
                $"milestones={milestonesApplied}, facilities={facilitiesCreated}, " +
                $"pendingPlacements={pendingPlacements}, walledVillages={initialWallVillages}");
        }

        /// <summary>
        /// 신규 게임에만 적용하는 공통 Forge 후조건. 종족별 초기 마일스톤이 모두 끝난 뒤
        /// 실행해 Dwarf Tier 2/3 승격을 보존하고, 연대기 미연결 BaseTown도 함께 포함한다.
        /// </summary>
        private void EnsureNewGameMinimumForges()
        {
            IReadOnlyList<VillageData> villages = AR.s.Village.GetAllVillages();
            int created = 0;
            int normalized = 0;
            int unchanged = 0;
            int errors = 0;

            for (int i = 0; i < villages.Count; i++)
            {
                VillageData village = villages[i];
                AR.s.Village.MarkSaveDirty(village.VillageId);
                MinimumForgeEnsureResult result =
                    VillageInitialDevelopment.EnsureMinimumForge(village,
                        AR.s.Data.GetBuildableItem, out string error);

                if (result == MinimumForgeEnsureResult.Created)
                {
                    created++;
                    AR.s.Village.RetryPendingPlacements(village.VillageId);
                }
                else if (result == MinimumForgeEnsureResult.TierNormalized)
                {
                    normalized++;
                    AR.s.Village.RetryPendingPlacements(village.VillageId);
                }
                else if (result == MinimumForgeEnsureResult.Unchanged)
                {
                    unchanged++;
                }
                else
                {
                    errors++;
                    Debug.LogError($"[VillageBootstrap] 공통 Forge 보장 실패: {error}");
                }
            }

            Debug.Log($"[VillageBootstrap] New Game 공통 Forge 최소 보장: " +
                $"villages={villages.Count}, created={created}, normalized={normalized}, " +
                $"unchanged={unchanged}, errors={errors}");
        }

        /// <summary>
        /// PROCEDURAL §0 런타임 추상 정착지 tick — 시간 분할(라운드로빈).
        /// V2 (VILLAGE_REWORK_PLAN §3.1): 절차적 역사 완료 인구가 런타임 정본으로 고정되어
        /// <see cref="SettlementPopulationModel.Advance"/>를 더 이상 호출하지 않는다(모델은 후속 설계용으로 보존).
        /// 대신 LastSimGameTime만 현재 시각으로 정렬해, 후속 설계에서 인구 tick을 재활성화할 때
        /// 고정 기간 전체가 backlog로 소급 적용되지 않게 한다. 구 세이브의 과거 시각도 이 경로로 정렬된다.
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

                if (sim.Settlements[i].LastSimGameTime < now)
                {
                    MarkSaveDirty(i);
                    sim.Settlements[i].LastSimGameTime = now;   // 인구 고정 — 시계만 정렬
                }
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

            IReadOnlyList<VillageData> villages = AR.s.Village.GetAllVillages();
            for (int i = 0; i < villages.Count; i++)
            {
                VillageData v = villages[i];
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
        public bool MaterializeSettlement(int settlementId, int villageId,
            int generalEventBudget = MATERIALIZE_GENERAL_EVENT_BUDGET,
            int wallEventBudget = MATERIALIZE_WALL_EVENT_BUDGET)
        {
            if (TryGetLiveIndex(settlementId, out int idx) == false)
                return false;

            WorldSimState sim = AR.s.Data.WorldSim;
            float now = AR.s.Time.CurrentGameTime;

            // V2: 인구는 역사 완료 값으로 고정 — materialize 직전 catch-up에서도 전진시키지 않고
            // 시계만 현재 시각으로 정렬한다(재활성화 시 backlog 방지).
            MarkSaveDirty(idx);
            sim.Settlements[idx].LastSimGameTime = now;
            VillageData village = AR.s.Village.GetVillage(villageId);
            if (village == null)
                return false;

            // 전역 population refresh보다 먼저 catch-up하므로 여기서 최신 추상 인구를 직접 반영한다.
            AR.s.Village.MarkSaveDirty(village.VillageId);
            village.Population = VillageManager.QuantizeAbstractPopulation(
                (int)sim.Settlements[idx].Pop);
            bool isOperational = VillageDevelopmentBootstrap.IsOperationalFate(
                sim.Settlements[idx].Fate);
            VillageAbstractAdvanceResult construction =
                VillageAbstractConstruction.AdvanceAbstractConstruction(village, now,
                    new VillageAbstractAdvanceOptions
                    {
                        GeneralEventBudget = generalEventBudget,
                        WallEventBudget = wallEventBudget,
                        IsOperational = isOperational,
                        // V2: 원거리 catch-up은 자원 생산·clock 정렬만 수행하고 발전 이벤트는 실행하지 않는다.
                        AllowDevelopment = VillageProgressionRules.RuntimeProgressionEnabled,
                    });

            // 예산 소진/비운영/blocked 상태에서는 clock과 task 진행을 그대로 두고 다음 tick에 재개한다.
            if (construction.IsCaughtUp == false)
                return false;

            sim.Settlements[idx].IsMaterialized = true;
            sim.Settlements[idx].VillageId = villageId;

            // 런타임 LOD: 마을 ECS 엔티티(스토리지/컴포넌트 + 진행중 task + 배치 오브젝트) 실체화.
            AR.s.Village.MaterializeVillage(villageId);

            Debug.Log($"[HistoryManager] Materialize settlement {settlementId} → village {villageId} " +
                      $"(fixed pop={(int)sim.Settlements[idx].Pop})");
            return true;
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

            MarkSaveDirty(idx);
            WorldSimState sim = AR.s.Data.WorldSim;
            float now = AR.s.Time.CurrentGameTime;

            // 런타임 LOD: VillageId가 유효한 동안 마을 ECS 엔티티를 추상화(상태 flush + 엔티티 파괴).
            int villageId = sim.Settlements[idx].VillageId;
            if (villageId >= 0)
            {
                AR.s.Village.DematerializeVillage(villageId);
                VillageData village = AR.s.Village.GetVillage(villageId);
                if (village != null)
                {
                    AR.s.Village.MarkSaveDirty(village.VillageId);
                    village.LastConstructionSimGameTime = now;
                    village.LastConstructionTierCheckGameTime = now;
                }
            }

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
