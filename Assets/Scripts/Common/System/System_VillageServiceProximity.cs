#nullable enable
using ARPG.Component;
using ARPG.Village;
using UnityEngine;

namespace ARPG.Systems
{
    /// <summary>
    /// Phase D: 플레이어 근처에서 사용 가능한 서비스 집계.
    /// 0.3s마다 PlayerNearbyServicesComponent 갱신 — F키 입력 시 ServiceUIRouter가 이 컴포넌트만 보면 됨.
    ///
    /// 작동 방식:
    /// 1. 플레이어가 어느 마을 안인지 — VillageManager.FindVillageContaining (단 1개)
    /// 2. 마을 안이면 AR.s.Village.PlacedObjects.GetAllEntitiesInVillage 순회
    /// 3. 각 PlacedObjectComponent의 Service 비트 + 거리로 가장 가까운 Shop/Forge/Inn/Shrine 결정
    /// 4. Gate는 마을 밖에서도 열 수 있어야 하므로 실체화된 VillageComponent 풀에서 별도 탐색
    /// 5. 마을 밖 → Gate/NPC를 제외한 시설은 -1/None으로 유지
    ///
    /// "단일 마을 한정"은 §10.3 외곽 보호 마진과 자연스럽게 맞물림
    /// (벽 밖 플레이어는 시설이 잡히지 않지만 Gate는 상호작용 가능).
    /// </summary>
    public class System_VillageServiceProximity : IUpdateSystem
    {
        // 도메인 대역 (CLAUDE.md): 60-64 Lifecycle (IUpdate)
        public int Priority => 61;
        public float UpdateInterval => 0.3f;     // 매 프레임 X — 0.3s 충분

        // 서비스별 거리 임계 (체비셰프 거리, 타일 단위)
        private const float SHOP_RANGE_SQR    = 3f * 3f;
        private const float FORGE_RANGE_SQR   = 3f * 3f;
        private const float INN_RANGE_SQR     = 4f * 4f;
        private const float SHRINE_RANGE_SQR  = 5f * 5f;
        // V4: VillageHub(100) 거점 이용 / Gate 개폐 임계
        private const float HUB_RANGE_SQR     = 3f * 3f;
        private const float GATE_RANGE_SQR    = 2f * 2f;

        // NPC 클릭 임계 (NPC_EQUIPMENT_DESIGN.md UI 패스, Q2=1.5 tile)
        private const float NPC_RANGE        = 1.5f;

        private ComponentManager _componentManager = null!;
        private SparseSet<VillageComponent> _villagePool = null!;

        public void OnCreate()
        {
            _componentManager = AR.s.Component;
            _villagePool = _componentManager.GetComponentPool<VillageComponent>();
        }

        public void OnUpdate(float inDeltaTime)
        {
            if (AR.s.Data == null) return;
            int playerEntityId = AR.s.Data.CurrentPlayerEntityId;
            if (playerEntityId < 0) return;

            // 플레이어 위치
            if (_componentManager.TryGetComponent<TransformComponent>(playerEntityId, out var playerTr) == false)
                return;

            int playerTileX = Mathf.FloorToInt(playerTr.Position.x);
            int playerTileY = Mathf.FloorToInt(playerTr.Position.y);
            int villageId = AR.s.Village.FindVillageContaining(playerTileX, playerTileY);

            // 기존 컴포넌트 (없으면 부착)
            if (_componentManager.TryGetComponent<PlayerNearbyServicesComponent>(playerEntityId, out var nearby) == false)
            {
                nearby = new PlayerNearbyServicesComponent
                {
                    NearestShopEntityId = -1,
                    NearestForgeEntityId = -1,
                    NearestInnEntityId = -1,
                    NearestShrineEntityId = -1,
                    NearestCivicEntityId = -1,
                    NearestVillageId = -1,
                };
                _componentManager.AddComponent(playerEntityId, nearby);
            }

            // 마을 안/밖과 무관하게 NPC는 검색 (월드 어디서든 NPC 클릭 가능)
            ResetNearby(ref nearby);

            int npcId = AR.s.Npc.FindNearestActiveNpc(playerTr.Position, NPC_RANGE, out float npcSqr);
            nearby.NearestNpcEntityId = npcId;
            nearby.NearestNpcDistanceSqr = npcId >= 0 ? npcSqr : float.MaxValue;

            // Gate는 닫힌 벽의 바깥에서도 열 수 있어야 하므로 마을 내부 판정과 분리한다.
            // VillageComponent 풀에는 현재 실체화된 마을만 존재한다.
            CollectNearestGate(playerTr.Position, ref nearby);

            if (villageId < 0)
            {
                // 마을 밖 — 시설은 없지만 Gate/NPC는 위에서 처리됨
                _componentManager.SetComponent(playerEntityId, nearby);
                return;
            }

            nearby.NearestVillageId = villageId;

            var entities = AR.s.Village.PlacedObjects.GetAllEntitiesInVillage(villageId);
            for (int i = 0; i < entities.Count; i++)
            {
                int entityId = entities[i];
                if (_componentManager.TryGetComponent<PlacedObjectComponent>(entityId, out var po) == false) continue;

                float dx = po.TileX - playerTr.Position.x;
                float dy = po.TileY - playerTr.Position.y;
                float sqr = dx * dx + dy * dy;

                if ((po.Service & ProvidedService.Shop) != 0 && sqr <= SHOP_RANGE_SQR && sqr < nearby.NearestShopDistanceSqr)
                {
                    nearby.NearestShopDistanceSqr = sqr;
                    nearby.NearestShopEntityId = entityId;
                    nearby.AvailableServices |= ProvidedService.Shop;
                }
                if ((po.Service & ProvidedService.Forge) != 0 && sqr <= FORGE_RANGE_SQR && sqr < nearby.NearestForgeDistanceSqr)
                {
                    nearby.NearestForgeDistanceSqr = sqr;
                    nearby.NearestForgeEntityId = entityId;
                    nearby.AvailableServices |= ProvidedService.Forge;
                }
                if ((po.Service & ProvidedService.Inn) != 0 && sqr <= INN_RANGE_SQR && sqr < nearby.NearestInnDistanceSqr)
                {
                    nearby.NearestInnDistanceSqr = sqr;
                    nearby.NearestInnEntityId = entityId;
                    nearby.AvailableServices |= ProvidedService.Inn;
                }
                if ((po.Service & ProvidedService.Shrine) != 0 && sqr <= SHRINE_RANGE_SQR && sqr < nearby.NearestShrineDistanceSqr)
                {
                    nearby.NearestShrineDistanceSqr = sqr;
                    nearby.NearestShrineEntityId = entityId;
                    nearby.AvailableServices |= ProvidedService.Shrine;
                }
                if ((po.Service & ProvidedService.Civic) != 0 && sqr <= SHRINE_RANGE_SQR && sqr < nearby.NearestCivicDistanceSqr)
                {
                    nearby.NearestCivicDistanceSqr = sqr;
                    nearby.NearestCivicEntityId = entityId;
                    nearby.AvailableServices |= ProvidedService.Civic;
                }
                // V4: VillageHub는 테이블 ProvidedService가 0이라 TableId(100)로 식별한다.
                // 구 세이브의 Campfire도 같은 TableId라 자동으로 거점 상호작용을 얻는다 (V0 Convert 계약).
                if (po.TableId == VillageInitialFacilityCatalog.VILLAGE_HUB_TABLE_ID
                    && sqr <= HUB_RANGE_SQR && sqr < nearby.NearestHubDistanceSqr)
                {
                    nearby.NearestHubDistanceSqr = sqr;
                    nearby.NearestHubEntityId = entityId;
                }
            }

            _componentManager.SetComponent(playerEntityId, nearby);
        }

        public void OnReset() { }

        /// <summary>
        /// 근접 Gate 세그먼트(건설 완료)를 찾아 컴포넌트에 기록한다.
        /// Gate는 Entity가 아니라 VillageData.WallSegments 정본이라 별도 수집 경로를 쓴다.
        /// </summary>
        private void CollectNearestGate(Vector2 playerPosition,
            ref PlayerNearbyServicesComponent nearby)
        {
            for (int villageIndex = 0; villageIndex < _villagePool.Count; villageIndex++)
            {
                VillageComponent villageComponent = _villagePool.GetByIndex(villageIndex);
                VillageData? village = AR.s.Village.GetVillage(villageComponent.VillageId);
                if (village == null || village.WallSegments == null)
                    continue;

                for (int segmentIndex = 0; segmentIndex < village.WallSegments.Count;
                    segmentIndex++)
                {
                    WallSegmentSaveData segment = village.WallSegments[segmentIndex];
                    if (segment.IsBuilt == false
                        || segment.Orient != (int)WallOrientation.Gate)
                        continue;

                    float dx = segment.TileX - playerPosition.x;
                    float dy = segment.TileY - playerPosition.y;
                    float sqr = dx * dx + dy * dy;
                    if (sqr > GATE_RANGE_SQR || sqr >= nearby.NearestGateDistanceSqr)
                        continue;

                    nearby.NearestGateVillageId = village.VillageId;
                    nearby.NearestGateSegmentId = segment.SegmentId;
                    nearby.NearestGateTileX = segment.TileX;
                    nearby.NearestGateTileY = segment.TileY;
                    nearby.NearestGateIsOpen = segment.IsOpen;
                    nearby.NearestGateDistanceSqr = sqr;
                }
            }
        }

        private static void ResetNearby(ref PlayerNearbyServicesComponent nearby)
        {
            nearby.AvailableServices = ProvidedService.None;
            nearby.NearestShopEntityId = -1;
            nearby.NearestShopDistanceSqr = float.MaxValue;
            nearby.NearestForgeEntityId = -1;
            nearby.NearestForgeDistanceSqr = float.MaxValue;
            nearby.NearestInnEntityId = -1;
            nearby.NearestInnDistanceSqr = float.MaxValue;
            nearby.NearestShrineEntityId = -1;
            nearby.NearestShrineDistanceSqr = float.MaxValue;
            nearby.NearestCivicEntityId = -1;
            nearby.NearestCivicDistanceSqr = float.MaxValue;
            nearby.NearestNpcEntityId = -1;
            nearby.NearestNpcDistanceSqr = float.MaxValue;
            nearby.NearestHubEntityId = -1;
            nearby.NearestHubDistanceSqr = float.MaxValue;
            nearby.NearestGateVillageId = -1;
            nearby.NearestGateSegmentId = -1;
            nearby.NearestGateTileX = 0;
            nearby.NearestGateTileY = 0;
            nearby.NearestGateIsOpen = false;
            nearby.NearestGateDistanceSqr = float.MaxValue;
            nearby.NearestVillageId = -1;
        }
    }
}
