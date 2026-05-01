#nullable enable
using ARPG.Component;
using ARPG.Map;
using ARPG.Village;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace ARPG.Systems
{
    /// <summary>
    /// Phase B: 마을의 범용 오브젝트 배치 큐.
    /// - 마을당 ObjectPlacementTaskComponent 1개를 슬롯으로 사용 (큐 아님).
    /// - VillageBuildRoadmap이 다음 타깃 결정.
    /// - 자원/빈 타일 대기 → 자원 차감 + Task 부착 → 시간 누적 → 완료 시 PlaceObject + Task 제거.
    /// Phase A의 System_VillageFirstBuild를 일반화해 대체.
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
            // Phase C: 다음 미완성 벽 세그먼트 조회
            WallSegmentSaveData? wallSeg = WallSegmentRegistry.GetNextUnbuiltSegment(v);
            bool wallIsGate = wallSeg != null && wallSeg.Orient == (int)WallOrientation.Gate;

            // Gate는 항상 우선 (방어 핵심 → 로드맵 차단 허용)
            if (wallSeg != null && wallIsGate)
            {
                TryStartWallTask(v, wallSeg, now);
                return;
            }

            // Phase D: NeedsEvaluation 후보 1순위 채택 — 없으면 로드맵 fallback
            RoadmapEntry? next = VillageNeedsCache.GetNextTarget(v);
            if (next.HasValue == false)
                next = VillageBuildRoadmap.GetNextTarget(v);

            // 로드맵 소진 → 남은 Palisade 진행 (잉여 체크 없이)
            if (next.HasValue == false)
            {
                if (wallSeg != null)
                    TryStartWallTask(v, wallSeg, now);
                return;
            }

            RoadmapEntry target = next.Value;
            Tables.BuildableItemTable? table = AR.s.Data.GetBuildableItem(target.TableId);
            if (table == null) return;

            int wood = AR.s.Village.GetResourceAmount(v.VillageId, GlobalEnum.ItemType.Wood);
            int stone = AR.s.Village.GetResourceAmount(v.VillageId, GlobalEnum.ItemType.Stone);

            // Phase D 정책 변경 (이전 Phase C "잉여 우선" 룰 폐기):
            // 일반 건물 자원 충분 → 항상 일반 건물 우선. 자원 부족 시에만 벽 fallback.
            // 이전 룰("Wood 잉여 시 벽 먼저")은 일반 건물 비용이 낮을 때 벽이 무한 도배되는 부작용 발생.
            bool canBuildRegular = wood >= table.Cost_Wood && stone >= table.Cost_Stone;

            if (canBuildRegular == false)
            {
                // 자원 부족 → 시간 낭비하지 말고 Palisade로 fallback (있으면)
                if (wallSeg != null)
                    TryStartWallTask(v, wallSeg, now);
                return;
            }

            // 빈 타일 탐색 — Phase E: 광장/큰길 예약 + 카테고리 minSep + 거리 차등 클러스터
            Vector2Int center = new Vector2Int(
                Mathf.FloorToInt(v.PositionX),
                Mathf.FloorToInt(v.PositionY)
            );
            BuildableCategory category = (BuildableCategory)table.Category;
            int boundsRadius = VillageManager.GetBoundsRadius(v.Stage);

            // Phase E: 탐색 반경을 boundsRadius 기반으로 확대 — Stage가 커질수록 분산 가능
            // (이전엔 villageTable.SpawnRadius=3 고정이라 City에서도 3타일 안에 다 박힘)
            int maxRadius = boundsRadius > 0
                ? Mathf.Max(DEFAULT_MAX_RADIUS, boundsRadius - VillageTileFinder.OUTSKIRT_MARGIN_TILES)
                : DEFAULT_MAX_RADIUS;

            // Phase E: 큰길 폭(B3) + 광장(A4) Stage별 옵션
            (int roadRadius, int roadHalfWidth) = GetRoadReserve(v.Stage);
            VillageTileFinder.SetRoadReserve(roadRadius, roadHalfWidth);
            VillageTileFinder.SetPlazaRadius(GetPlazaRadius(v.Stage));

            // B1: 테이블에 MinSeparation > 0이면 카테고리 기본값을 오버라이드
            Vector2Int? tile = VillageTileFinder.FindEmptyTileNearest(
                center, maxRadius, v.VillageId, category, boundsRadius, table.MinSeparation);
            if (tile.HasValue == false) return;

            // 자원 차감 (실패 시 abort)
            if (table.Cost_Wood > 0)
            {
                if (AR.s.Village.ConsumeResource(v.VillageId, GlobalEnum.ItemType.Wood, table.Cost_Wood) == false)
                    return;
            }
            if (table.Cost_Stone > 0)
            {
                if (AR.s.Village.ConsumeResource(v.VillageId, GlobalEnum.ItemType.Stone, table.Cost_Stone) == false)
                {
                    // Stone 차감 실패 → Wood 환불
                    if (table.Cost_Wood > 0)
                        AR.s.Village.ProduceResource(v.VillageId, GlobalEnum.ItemType.Wood, table.Cost_Wood);
                    return;
                }
            }

            // Task 부착
            ObjectPlacementTaskComponent task = new ObjectPlacementTaskComponent
            {
                VillageId = v.VillageId,
                TargetTableId = target.TableId,
                TileX = tile.Value.x,
                TileY = tile.Value.y,
                StartedAt = now,
                BuildDurationHours = target.BuildHours,
                ReservedWoodCost = table.Cost_Wood,
                ReservedStoneCost = table.Cost_Stone,
            };
            AR.s.Component.AddComponent(v.EntityId, task);

            Debug.Log($"[BuildQueue] v{v.VillageId} 착수 '{table.Name}': Wood -{table.Cost_Wood}, Stone -{table.Cost_Stone}, tile=({tile.Value.x},{tile.Value.y}), 완료 예정={now + target.BuildHours:F1}h");
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
            if (task.TargetTableId == VillageBuildRoadmap.CAMPFIRE_TABLE_ID)
                v.HasCampfire = true;

            Debug.Log($"[BuildQueue] v{v.VillageId} '{table.Name}' 완성 at ({task.TileX},{task.TileY})");

            // 로드맵 소진 감지 (1회 로그) — 벽이 없을 때만 의미 있음
            if (VillageBuildRoadmap.GetNextTarget(v) == null && WallSegmentRegistry.CountUnbuilt(v) == 0)
                Debug.Log($"[BuildQueue] v{v.VillageId} 로드맵 + 벽 완료 — Phase C+ 승격 대기");
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
