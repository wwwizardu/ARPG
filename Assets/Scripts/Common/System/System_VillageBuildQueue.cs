#nullable enable
using ARPG.Component;
using ARPG.Map;
using ARPG.Village;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace ARPG.Systems
{
    /// <summary>
    /// 마을의 범용 오브젝트 배치 큐.
    /// - 마을당 ObjectPlacementTaskComponent 1개를 슬롯으로 사용 (큐 아님).
    /// - VillageNeedsEvaluator가 점수 정렬한 후보 리스트를 받아, affordable + placeable한 첫 후보 채택.
    /// - 자원 차감 + Task 부착 → 시간 누적 → 완료 시 PlaceObject + Task 제거.
    /// 우선순위 정책은 BUILD_PRIORITY_DESIGN.md §2 (4 Layer 점수) 참조.
    /// </summary>
    public class System_VillageBuildQueue : IFixedUpdateSystem
    {
        private const int DEFAULT_MAX_RADIUS = 3;

        // 도메인 대역 (CLAUDE.md): 65-69 Construction (Phase C에서 58→66 재할당)
        public int Priority => 66;
        public float UpdateInterval => 5.0f;

        public void OnCreate() { }

        public void OnFixedUpdate(float inFixedDeltaTime)
        {
            float now = AR.s.Time.CurrentGameTime;
            var villages = AR.s.Village.GetAllVillages();

            foreach (VillageData v in villages)
            {
                if (v.Population < 1) continue;
                if (v.EntityId < 0) continue;

                bool hasTask = AR.s.Component.TryGetComponent<ObjectPlacementTaskComponent>(v.EntityId, out var task);

                if (hasTask == false)
                {
                    TryStartNextTask(v, now);
                    continue;
                }

                float elapsed = now - task.StartedAt;
                if (elapsed < task.BuildDurationHours)
                    continue;

                TryFinishAsync(v, task).Forget();
            }
        }

        private void TryStartNextTask(VillageData v, float now)
        {
            // 다음 미완성 벽 세그먼트 조회 — Gate면 절대 우선, 일반 Palisade는 fallback
            WallSegmentSaveData? wallSeg = WallSegmentRegistry.GetNextUnbuiltSegment(v);
            bool wallIsGate = wallSeg != null && wallSeg.Orient == (int)WallOrientation.Gate;
            if (wallSeg != null && wallIsGate)
            {
                TryStartWallTask(v, wallSeg, now);
                return;
            }

            // BUILD_PRIORITY_DESIGN.md §3 — 점수 우선순위 절대 존중.
            //  • 1위가 자원 부족 → 다른 후보로 우회하지 않고 자원이 모일 때까지 대기
            //  • 1위가 자리 없음(영구 블로커) → 2위, 3위 순으로 시도
            //  • 모든 후보 자리 없음 → 벽 fallback
            var ranked = VillageNeedsEvaluator.GetRankedCandidates(v);
            for (int i = 0; i < ranked.Count; i++)
            {
                BuildAttemptResult result = TryStartGeneralBuild(v, ranked[i], now);
                if (result == BuildAttemptResult.Started)
                    return;
                if (result == BuildAttemptResult.WaitForResources)
                    return;  // 1위(이거든 후순위든 도달한 후보)의 자원 모일 때까지 대기 — fallback X
                // BuildAttemptResult.NoTileOrTableMissing → 다음 후보 시도
            }

            // 모든 일반 후보 자리 없음 / 테이블 누락 → 벽 천천히 진행
            if (wallSeg != null)
                TryStartWallTask(v, wallSeg, now);
        }

        private enum BuildAttemptResult
        {
            Started,                  // 빌드 시작 성공
            WaitForResources,         // 자원 부족 — 시간이 지나면 해소되므로 대기
            NoTileOrTableMissing,     // 자리 없음 / 테이블 누락 — 영구 블로커, 다음 후보 시도
        }

        /// <summary>
        /// 일반 건물 1개 빌드 시도.
        /// BuildHours는 BuildableItemTable.BuildHours에서 직접 읽음.
        /// </summary>
        private BuildAttemptResult TryStartGeneralBuild(VillageData v, int targetTableId, float now)
        {
            Tables.BuildableItemTable? table = AR.s.Data.GetBuildableItem(targetTableId);
            if (table == null) return BuildAttemptResult.NoTileOrTableMissing;

            int wood = AR.s.Village.GetResourceAmount(v.VillageId, GlobalEnum.ItemType.Wood);
            int stone = AR.s.Village.GetResourceAmount(v.VillageId, GlobalEnum.ItemType.Stone);
            if (wood < table.Cost_Wood || stone < table.Cost_Stone)
                return BuildAttemptResult.WaitForResources;

            // 빈 타일 탐색 — 광장/큰길 예약 + 카테고리 minSep + 거리 차등 클러스터
            Vector2Int center = new Vector2Int(
                Mathf.FloorToInt(v.PositionX),
                Mathf.FloorToInt(v.PositionY)
            );
            BuildableCategory category = (BuildableCategory)table.Category;
            int boundsRadius = VillageManager.GetBoundsRadius(v.Stage);
            int maxRadius = boundsRadius > 0
                ? Mathf.Max(DEFAULT_MAX_RADIUS, boundsRadius - VillageTileFinder.OUTSKIRT_MARGIN_TILES)
                : DEFAULT_MAX_RADIUS;

            (int roadRadius, int roadHalfWidth) = GetRoadReserve(v.Stage);
            VillageTileFinder.SetRoadReserve(roadRadius, roadHalfWidth);
            VillageTileFinder.SetPlazaRadius(GetPlazaRadius(v.Stage));

            Vector2Int? tile = VillageTileFinder.FindEmptyTileNearest(
                center, maxRadius, v.VillageId, category, boundsRadius, table.MinSeparation);
            if (tile.HasValue == false) return BuildAttemptResult.NoTileOrTableMissing;  // 자리 없음 → 다음 후보 시도

            // 자원 차감 (Wood/Stone 둘 중 하나라도 실패 시 환불). 동시성 race 시에만 발생.
            if (table.Cost_Wood > 0
                && AR.s.Village.ConsumeResource(v.VillageId, GlobalEnum.ItemType.Wood, table.Cost_Wood) == false)
                return BuildAttemptResult.WaitForResources;
            if (table.Cost_Stone > 0
                && AR.s.Village.ConsumeResource(v.VillageId, GlobalEnum.ItemType.Stone, table.Cost_Stone) == false)
            {
                if (table.Cost_Wood > 0)
                    AR.s.Village.ProduceResource(v.VillageId, GlobalEnum.ItemType.Wood, table.Cost_Wood);
                return BuildAttemptResult.WaitForResources;
            }

            float buildHours = table.BuildHours > 0f ? table.BuildHours : 2f;
            ObjectPlacementTaskComponent task = new ObjectPlacementTaskComponent
            {
                VillageId = v.VillageId,
                TargetTableId = targetTableId,
                TileX = tile.Value.x,
                TileY = tile.Value.y,
                StartedAt = now,
                BuildDurationHours = buildHours,
                ReservedWoodCost = table.Cost_Wood,
                ReservedStoneCost = table.Cost_Stone,
            };
            AR.s.Component.AddComponent(v.EntityId, task);

            Debug.Log($"[BuildQueue] v{v.VillageId} 착수 '{table.Name}': Wood -{table.Cost_Wood}, Stone -{table.Cost_Stone}, tile=({tile.Value.x},{tile.Value.y}), 완료 예정={now + buildHours:F1}h");
            return BuildAttemptResult.Started;
        }

        private async UniTask TryFinishAsync(VillageData v, ObjectPlacementTaskComponent task)
        {
            // 중복 방어: 즉시 컴포넌트 제거 (Phase A의 HasCampfire 잠금 패턴 일반화)
            AR.s.Component.RemoveComponent<ObjectPlacementTaskComponent>(v.EntityId);

            Tables.BuildableItemTable? table = AR.s.Data.GetBuildableItem(task.TargetTableId);
            if (table == null)
            {
                RefundReserved(v, task);
                return;
            }

            // Tile 경로면 사전 로드, Entity 경로는 BuildingFactory가 내부에서 처리
            if (table.SpawnType == GlobalEnum.BuildableSpawnType.Tile)
                await BuildableTileRegistry.EnsureLoadedAsync(task.TargetTableId);

            bool placed = AR.s.Map.PlaceObject(task.TileX, task.TileY, task.TargetTableId);
            if (placed == false)
            {
                RefundReserved(v, task);
                Debug.LogWarning($"[BuildQueue] v{v.VillageId} '{table.Name}' 배치 실패, Wood +{task.ReservedWoodCost} Stone +{task.ReservedStoneCost} 환불 후 재시도 대기");
                return;
            }

            // 성공 처리 — 벽이면 세그먼트 마킹, 그 외엔 누적 리스트 + 효과 콜백
            if (IsWallTask(task.TargetTableId))
            {
                OnWallSegmentCompleted(v, task);
                Debug.Log($"[BuildQueue] v{v.VillageId} 벽 '{table.Name}' 완성 at ({task.TileX},{task.TileY})");
                return;
            }

            v.PlacedObjectTypeIds.Add(task.TargetTableId);
            AR.s.Village.OnObjectPlaced(v.VillageId, task.TargetTableId, task.TileX, task.TileY);

            // Phase A 호환 플래그 (UI/타 시스템이 아직 참조할 수 있어 유지)
            if (task.TargetTableId == VillageNeedsEvaluator.CAMPFIRE_TABLE_ID)
                v.HasCampfire = true;

            Debug.Log($"[BuildQueue] v{v.VillageId} '{table.Name}' 완성 at ({task.TileX},{task.TileY})");
        }

        private static void RefundReserved(VillageData v, ObjectPlacementTaskComponent task)
        {
            if (task.ReservedWoodCost > 0)
                AR.s.Village.ProduceResource(v.VillageId, GlobalEnum.ItemType.Wood, task.ReservedWoodCost);
            if (task.ReservedStoneCost > 0)
                AR.s.Village.ProduceResource(v.VillageId, GlobalEnum.ItemType.Stone, task.ReservedStoneCost);
        }

        // ========== Phase C: 벽 세그먼트 처리 ==========

        // PHASE_C_DESIGN.md §15 결정 #2: 벽 1칸 건설 시간 0.5h (실시간 ~15초)
        private const float PALISADE_BUILD_HOURS = 0.5f;
        private const int PALISADE_TABLE_ID = 180;
        private const int PALISADE_GATE_TABLE_ID = 181;

        /// <summary>
        /// 미완성 벽 세그먼트를 ObjectPlacementTaskComponent로 큐잉.
        /// 자원/타일 체크 — Cost_Wood만 사용 (Palisade=8, Gate=40).
        /// </summary>
        private void TryStartWallTask(VillageData v, WallSegmentSaveData seg, float now)
        {
            int tableId = (seg.Orient == (int)WallOrientation.Gate) ? PALISADE_GATE_TABLE_ID : PALISADE_TABLE_ID;
            Tables.BuildableItemTable? table = AR.s.Data.GetBuildableItem(tableId);
            if (table == null) return;

            int wood = AR.s.Village.GetResourceAmount(v.VillageId, GlobalEnum.ItemType.Wood);
            if (wood < table.Cost_Wood) return;

            // 타일이 비어있는지 확인 (다른 오브젝트가 점유했으면 스킵)
            if (AR.s.Map.GetObjectIdAt(seg.TileX, seg.TileY) != 0)
            {
                // 이미 무언가 있음 — 벽 못 세움. SegmentId가 있으니 재시도 안 함, IsBuilt=true로 마킹
                Debug.LogWarning($"[WallPlanner] v{v.VillageId} seg{seg.SegmentId} 위치 ({seg.TileX},{seg.TileY}) 점유됨, 스킵");
                WallSegmentRegistry.MarkSegmentBuilt(v, seg.SegmentId);
                return;
            }

            // 자원 차감
            if (table.Cost_Wood > 0
                && AR.s.Village.ConsumeResource(v.VillageId, GlobalEnum.ItemType.Wood, table.Cost_Wood) == false)
                return;

            ObjectPlacementTaskComponent task = new ObjectPlacementTaskComponent
            {
                VillageId = v.VillageId,
                TargetTableId = tableId,
                TileX = seg.TileX,
                TileY = seg.TileY,
                StartedAt = now,
                BuildDurationHours = PALISADE_BUILD_HOURS,
                ReservedWoodCost = table.Cost_Wood,
                ReservedStoneCost = 0,
            };
            AR.s.Component.AddComponent(v.EntityId, task);

            Debug.Log($"[BuildQueue] v{v.VillageId} 벽 착수 seg{seg.SegmentId} '{table.Name}' tile=({seg.TileX},{seg.TileY})");
        }

        /// <summary>현재 task가 벽 세그먼트인지 판정 — TableId로 분기.</summary>
        private static bool IsWallTask(int tableId)
        {
            return tableId == PALISADE_TABLE_ID || tableId == PALISADE_GATE_TABLE_ID;
        }

        /// <summary>벽 완성 시 처리 — PlacedObjectTypeIds에 추가하지 않고 WallSegments 마킹.</summary>
        private static void OnWallSegmentCompleted(VillageData v, ObjectPlacementTaskComponent task)
        {
            // 좌표로 세그먼트 찾기 (SegmentId를 task에 안 박았으므로 좌표 매칭)
            if (v.WallSegments == null) return;
            for (int i = 0; i < v.WallSegments.Count; i++)
            {
                var seg = v.WallSegments[i];
                if (seg.IsBuilt == false && seg.TileX == task.TileX && seg.TileY == task.TileY)
                {
                    WallSegmentRegistry.MarkSegmentBuilt(v, seg.SegmentId);
                    UpdateVillageWallStats(v);
                    return;
                }
            }
        }

        private static void UpdateVillageWallStats(VillageData v)
        {
            if (AR.s.Component.TryGetComponent<VillageComponent>(v.EntityId, out var vc))
            {
                vc.CompletedWallSegments = v.CompletedWallSegments;
                AR.s.Component.SetComponent(v.EntityId, vc);
            }

            // 10% 단위 진행률 로그
            int total = v.WallSegmentCount;
            int built = v.CompletedWallSegments;
            if (total <= 0) return;
            int pct = built * 100 / total;
            int prevPct = (built - 1) * 100 / total;
            if (pct / 10 != prevPct / 10)
                Debug.Log($"[WallPlanner] v{v.VillageId} 벽 {built}/{total} ({pct}%)");
        }

        /// <summary>
        /// Phase E: Stage별 큰길 예약 (radius, halfWidth).
        ///  Settlement: 비활성 (좁은 마을)
        ///  Hamlet+: 폭 3타일(±1)로 시각적으로 통로처럼 인식되도록 확장 — B3
        /// </summary>
        private static (int radius, int halfWidth) GetRoadReserve(VillageStage stage)
        {
            return stage switch
            {
                VillageStage.Settlement => (0, 0),
                VillageStage.Hamlet     => (4, 1),
                VillageStage.Village    => (6, 1),
                VillageStage.Town       => (8, 1),
                VillageStage.City       => (10, 1),
                _ => (0, 0),
            };
        }

        /// <summary>
        /// Phase E (A4): Stage별 마을 중심 광장. Hamlet부터 3x3, Town부터 5x5로 확장.
        /// 광장은 큰길 예약과 겹치지만 시각/판정상 무해 (둘 다 "배치 불가" 표시일 뿐).
        /// </summary>
        private static int GetPlazaRadius(VillageStage stage)
        {
            return stage switch
            {
                VillageStage.Settlement => 0,
                VillageStage.Hamlet     => 1,   // 3x3
                VillageStage.Village    => 1,   // 3x3
                VillageStage.Town       => 2,   // 5x5
                VillageStage.City       => 2,   // 5x5
                _ => 0,
            };
        }

        public void OnReset() { }
    }
}
