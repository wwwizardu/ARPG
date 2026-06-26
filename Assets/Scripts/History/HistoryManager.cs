using System.Collections.Generic;
using UnityEngine;
using ARPG.Map;

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

        public bool IsGenerated => _generated;
        public int WorldSeed => _worldSeed;

        public void Initialize()
        {
            Debug.Log("HistoryManager Initialized");
            GenerateSettlementChronicleIfNeeded();
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
        /// 청크 활성화 시 호출. Phase 4 이후 AmbientTag → VFX 트리거, Lore 아이템 spawn 등을 처리.
        /// </summary>
        public void OnChunkActivated(Vector2Int chunkCoord)
        {
            // Phase 0: no-op
        }
    }
}
