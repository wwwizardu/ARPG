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

        public int Priority => 58;
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
            RoadmapEntry? next = VillageBuildRoadmap.GetNextTarget(v);
            if (next.HasValue == false)
                return;

            RoadmapEntry target = next.Value;
            Tables.BuildableItemTable? table = AR.s.Data.GetBuildableItem(target.TableId);
            if (table == null) return;

            int wood = AR.s.Village.GetResourceAmount(v.VillageId, GlobalEnum.ItemType.Wood);
            int stone = AR.s.Village.GetResourceAmount(v.VillageId, GlobalEnum.ItemType.Stone);
            if (wood < table.Cost_Wood) return;
            if (stone < table.Cost_Stone) return;

            // 빈 타일 탐색
            Tables.VillageTable? villageTable = AR.s.Data.GetVillageTable(v.TableId);
            int maxRadius = villageTable != null ? Mathf.CeilToInt(villageTable.SpawnRadius) : DEFAULT_MAX_RADIUS;
            Vector2Int center = new Vector2Int(
                Mathf.FloorToInt(v.PositionX),
                Mathf.FloorToInt(v.PositionY)
            );
            Vector2Int? tile = VillageTileFinder.FindEmptyTileNearest(center, maxRadius);
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

            // 성공 — 누적 리스트 + 효과 콜백
            v.PlacedObjectTypeIds.Add(task.TargetTableId);
            AR.s.Village.OnObjectPlaced(v.VillageId, task.TargetTableId);

            // Phase A 호환 플래그 (UI/타 시스템이 아직 참조할 수 있어 유지)
            if (task.TargetTableId == VillageBuildRoadmap.CAMPFIRE_TABLE_ID)
                v.HasCampfire = true;

            Debug.Log($"[BuildQueue] v{v.VillageId} '{table.Name}' 완성 at ({task.TileX},{task.TileY})");

            // 로드맵 소진 감지 (1회 로그)
            if (VillageBuildRoadmap.GetNextTarget(v) == null)
                Debug.Log($"[BuildQueue] v{v.VillageId} Stage0 로드맵 완료 — Phase C 승격 대기");
        }

        private static void RefundReserved(VillageData v, ObjectPlacementTaskComponent task)
        {
            if (task.ReservedWoodCost > 0)
                AR.s.Village.ProduceResource(v.VillageId, GlobalEnum.ItemType.Wood, task.ReservedWoodCost);
            if (task.ReservedStoneCost > 0)
                AR.s.Village.ProduceResource(v.VillageId, GlobalEnum.ItemType.Stone, task.ReservedStoneCost);
        }

        public void OnReset() { }
    }
}
