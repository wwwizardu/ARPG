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
    /// 2. 마을 안이면 PlacedObjectRegistry.GetAllEntitiesInVillage 순회
    /// 3. 각 PlacedObjectComponent의 Service 비트 + 거리로 가장 가까운 Shop/Forge/Inn/Shrine 결정
    /// 4. 마을 밖 → 모두 -1/None으로 클리어
    ///
    /// "단일 마을 한정"은 §10.3 외곽 보호 마진과 자연스럽게 맞물림 (벽 밖 플레이어는 서비스 잡힘 X).
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

        public void OnCreate() { }

        public void OnUpdate(float inDeltaTime)
        {
            if (AR.s.Data == null) return;
            int playerEntityId = AR.s.Data.CurrentPlayerEntityId;
            if (playerEntityId < 0) return;

            // 플레이어 위치
            if (AR.s.Component.TryGetComponent<TransformComponent>(playerEntityId, out var playerTr) == false)
                return;

            int playerTileX = Mathf.FloorToInt(playerTr.Position.x);
            int playerTileY = Mathf.FloorToInt(playerTr.Position.y);
            int villageId = AR.s.Village.FindVillageContaining(playerTileX, playerTileY);

            // 기존 컴포넌트 (없으면 부착)
            if (AR.s.Component.TryGetComponent<PlayerNearbyServicesComponent>(playerEntityId, out var nearby) == false)
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
                AR.s.Component.AddComponent(playerEntityId, nearby);
            }

            if (villageId < 0)
            {
                // 마을 밖 → 클리어
                ResetNearby(ref nearby);
                AR.s.Component.SetComponent(playerEntityId, nearby);
                return;
            }

            // 마을 안 → 검색 시작
            ResetNearby(ref nearby);
            nearby.NearestVillageId = villageId;

            float bestShopDist = float.MaxValue;
            float bestForgeDist = float.MaxValue;
            float bestInnDist = float.MaxValue;
            float bestShrineDist = float.MaxValue;
            float bestCivicDist = float.MaxValue;

            var entities = PlacedObjectRegistry.GetAllEntitiesInVillage(villageId);
            for (int i = 0; i < entities.Count; i++)
            {
                int entityId = entities[i];
                if (AR.s.Component.TryGetComponent<PlacedObjectComponent>(entityId, out var po) == false) continue;

                float dx = po.TileX - playerTr.Position.x;
                float dy = po.TileY - playerTr.Position.y;
                float sqr = dx * dx + dy * dy;

                if ((po.Service & ProvidedService.Shop) != 0 && sqr <= SHOP_RANGE_SQR && sqr < bestShopDist)
                {
                    bestShopDist = sqr;
                    nearby.NearestShopEntityId = entityId;
                    nearby.AvailableServices |= ProvidedService.Shop;
                }
                if ((po.Service & ProvidedService.Forge) != 0 && sqr <= FORGE_RANGE_SQR && sqr < bestForgeDist)
                {
                    bestForgeDist = sqr;
                    nearby.NearestForgeEntityId = entityId;
                    nearby.AvailableServices |= ProvidedService.Forge;
                }
                if ((po.Service & ProvidedService.Inn) != 0 && sqr <= INN_RANGE_SQR && sqr < bestInnDist)
                {
                    bestInnDist = sqr;
                    nearby.NearestInnEntityId = entityId;
                    nearby.AvailableServices |= ProvidedService.Inn;
                }
                if ((po.Service & ProvidedService.Shrine) != 0 && sqr <= SHRINE_RANGE_SQR && sqr < bestShrineDist)
                {
                    bestShrineDist = sqr;
                    nearby.NearestShrineEntityId = entityId;
                    nearby.AvailableServices |= ProvidedService.Shrine;
                }
                if ((po.Service & ProvidedService.Civic) != 0 && sqr <= SHRINE_RANGE_SQR && sqr < bestCivicDist)
                {
                    bestCivicDist = sqr;
                    nearby.NearestCivicEntityId = entityId;
                    nearby.AvailableServices |= ProvidedService.Civic;
                }
            }

            AR.s.Component.SetComponent(playerEntityId, nearby);
        }

        public void OnReset() { }

        private static void ResetNearby(ref PlayerNearbyServicesComponent nearby)
        {
            nearby.AvailableServices = ProvidedService.None;
            nearby.NearestShopEntityId = -1;
            nearby.NearestForgeEntityId = -1;
            nearby.NearestInnEntityId = -1;
            nearby.NearestShrineEntityId = -1;
            nearby.NearestCivicEntityId = -1;
            nearby.NearestVillageId = -1;
        }
    }
}
