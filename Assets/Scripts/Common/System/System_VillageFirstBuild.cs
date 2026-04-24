#nullable enable
using ARPG.Map;
using ARPG.Village;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace ARPG.Systems
{
    /// <summary>
    /// Phase A MVP: 마을의 첫 Campfire 제작 루프.
    /// - NPC ≥ 1명 + Wood ≥ 3 + 빈 타일 존재 시 착수 (Wood 3 차감)
    /// - 2h 게임시간 경과 시 Campfire 타일 배치, 실패 시 자원 환불
    /// - 완료 후 HasCampfire=true → 같은 마을에서 재제작 안 함
    /// Phase B에서 범용 ObjectPlacementTaskComponent 기반으로 일반화 예정.
    /// </summary>
    public class System_VillageFirstBuild : IFixedUpdateSystem
    {
        // 기획 §4.4
        private const int CAMPFIRE_WOOD_COST = 3;
        private const float CAMPFIRE_BUILD_HOURS = 2f;
        private const int DEFAULT_MAX_RADIUS = 3;

        // BuildableItemTable의 Campfire 엔트리 Id
        private const int CAMPFIRE_BUILDABLE_ID = 100;

        public int Priority => 58;
        public float UpdateInterval => 5.0f;

        public void OnCreate() { }

        public void OnFixedUpdate(float inFixedDeltaTime)
        {
            float now = AR.s.Time.CurrentGameTime;
            var villages = AR.s.Village.GetAllVillages();

            foreach (VillageData v in villages)
            {
                if (v.HasCampfire)
                    continue;
                if (v.Population < 1)
                    continue;

                if (v.FirstBuildStartedAt < 0f)
                {
                    TryStart(v, now);
                    continue;
                }

                float elapsed = now - v.FirstBuildStartedAt;
                if (elapsed < CAMPFIRE_BUILD_HOURS)
                    continue;

                TryFinishAsync(v).Forget();
            }
        }

        private void TryStart(VillageData v, float now)
        {
            int wood = AR.s.Village.GetResourceAmount(v.VillageId, GlobalEnum.ItemType.Wood);
            if (wood < CAMPFIRE_WOOD_COST)
                return;

            Tables.VillageTable? table = AR.s.Data.GetVillageTable(v.TableId);
            int maxRadius = table != null ? Mathf.CeilToInt(table.SpawnRadius) : DEFAULT_MAX_RADIUS;

            Vector2Int center = new Vector2Int(
                Mathf.FloorToInt(v.PositionX),
                Mathf.FloorToInt(v.PositionY)
            );
            Vector2Int? target = VillageTileFinder.FindEmptyTileNearest(center, maxRadius);
            if (target.HasValue == false)
                return;

            if (AR.s.Village.ConsumeResource(v.VillageId, GlobalEnum.ItemType.Wood, CAMPFIRE_WOOD_COST) == false)
                return;

            v.FirstBuildStartedAt = now;
            v.FirstBuildTileX = target.Value.x;
            v.FirstBuildTileY = target.Value.y;

            Debug.Log($"[FirstBuild] v{v.VillageId} 착수: Wood -{CAMPFIRE_WOOD_COST}, tile=({target.Value.x},{target.Value.y}), 완료 예정={now + CAMPFIRE_BUILD_HOURS:F1}h");
        }

        // HasCampfire를 먼저 true로 잠가 중복 Forget 호출 방어
        private async UniTask TryFinishAsync(VillageData v)
        {
            if (v.HasCampfire) return;
            v.HasCampfire = true;

            // Addressable 타일 사전 로드 (lazy, 이미 로드돼 있으면 즉시 반환)
            await BuildableTileRegistry.EnsureLoadedAsync(CAMPFIRE_BUILDABLE_ID);

            bool placed = AR.s.Map.PlaceObject(v.FirstBuildTileX, v.FirstBuildTileY, CAMPFIRE_BUILDABLE_ID);
            if (placed)
            {
                v.FirstBuildStartedAt = -1f;
                Debug.Log($"[FirstBuild] v{v.VillageId} Campfire 완성 at ({v.FirstBuildTileX},{v.FirstBuildTileY})");
                return;
            }

            // 자리가 중간에 막힘 → 자원 환불 + 재착수 대기 + HasCampfire 원복
            v.HasCampfire = false;
            AR.s.Village.ProduceResource(v.VillageId, GlobalEnum.ItemType.Wood, CAMPFIRE_WOOD_COST);
            v.FirstBuildStartedAt = -1f;
            Debug.LogWarning($"[FirstBuild] v{v.VillageId} 배치 실패, Wood +{CAMPFIRE_WOOD_COST} 환불 후 재시도 대기");
        }

        public void OnReset() { }
    }
}
