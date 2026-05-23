#nullable enable
using ARPG.Component;
using ARPG.UI;
using UnityEngine;

namespace ARPG.Village
{
    /// <summary>
    /// Interact 키 입력 시 PlayerNearbyServicesComponent를 보고 가장 가까운 후보의 UI 라우팅.
    /// 후보: 마을 시설(Shop/Forge/Inn/Shrine) + 활성 NPC.
    /// 정책: 임계 거리(System_VillageServiceProximity 상수) 안에 들어온 후보 중 거리 sqr 최소.
    /// 각 UI 클래스의 Bind(entityId)로 어떤 엔티티 기준인지 전달.
    /// </summary>
    public static class ServiceUIRouter
    {
        private enum CandidateKind { None, Shop, Forge, Inn, Shrine, Npc }

        public static bool Open(in PlayerNearbyServicesComponent nearby)
        {
            CandidateKind bestKind = CandidateKind.None;
            int bestEntityId = -1;
            float bestSqr = float.MaxValue;

            if ((nearby.AvailableServices & ProvidedService.Shop) != 0 && nearby.NearestShopEntityId >= 0
                && nearby.NearestShopDistanceSqr < bestSqr)
            {
                bestKind = CandidateKind.Shop;
                bestEntityId = nearby.NearestShopEntityId;
                bestSqr = nearby.NearestShopDistanceSqr;
            }
            if ((nearby.AvailableServices & ProvidedService.Forge) != 0 && nearby.NearestForgeEntityId >= 0
                && nearby.NearestForgeDistanceSqr < bestSqr)
            {
                bestKind = CandidateKind.Forge;
                bestEntityId = nearby.NearestForgeEntityId;
                bestSqr = nearby.NearestForgeDistanceSqr;
            }
            if ((nearby.AvailableServices & ProvidedService.Inn) != 0 && nearby.NearestInnEntityId >= 0
                && nearby.NearestInnDistanceSqr < bestSqr)
            {
                bestKind = CandidateKind.Inn;
                bestEntityId = nearby.NearestInnEntityId;
                bestSqr = nearby.NearestInnDistanceSqr;
            }
            if ((nearby.AvailableServices & ProvidedService.Shrine) != 0 && nearby.NearestShrineEntityId >= 0
                && nearby.NearestShrineDistanceSqr < bestSqr)
            {
                bestKind = CandidateKind.Shrine;
                bestEntityId = nearby.NearestShrineEntityId;
                bestSqr = nearby.NearestShrineDistanceSqr;
            }
            if (nearby.NearestNpcEntityId >= 0 && nearby.NearestNpcDistanceSqr < bestSqr)
            {
                bestKind = CandidateKind.Npc;
                bestEntityId = nearby.NearestNpcEntityId;
                bestSqr = nearby.NearestNpcDistanceSqr;
            }
            // Civic은 현재 UI 라우팅 대상 아님 (Phase E 이상)

            switch (bestKind)
            {
                case CandidateKind.Shop:
                {
                    Debug.Log($"[Service] v{nearby.NearestVillageId} 열기: Shop (entityId={bestEntityId}, sqr={bestSqr:F2})");
                    var ui = AR.s.UI.Show<UIShopMerchant>(AddressablePath.ShopMerchant, UIManager.Layer.Main);
                    ui?.Bind(bestEntityId);
                    return true;
                }
                case CandidateKind.Forge:
                {
                    Debug.Log($"[Service] v{nearby.NearestVillageId} 열기: Forge (entityId={bestEntityId}, sqr={bestSqr:F2})");
                    var ui = AR.s.UI.Show<UIForge>(AddressablePath.Forge, UIManager.Layer.Main);
                    ui?.Bind(bestEntityId);
                    return true;
                }
                case CandidateKind.Inn:
                {
                    Debug.Log($"[Service] v{nearby.NearestVillageId} 열기: Inn (entityId={bestEntityId}, sqr={bestSqr:F2})");
                    var ui = AR.s.UI.Show<UIInn>(AddressablePath.Inn, UIManager.Layer.Main);
                    ui?.Bind(bestEntityId);
                    return true;
                }
                case CandidateKind.Shrine:
                {
                    Debug.Log($"[Service] v{nearby.NearestVillageId} 열기: Shrine (entityId={bestEntityId}, sqr={bestSqr:F2})");
                    var ui = AR.s.UI.Show<UIShrine>(AddressablePath.Shrine, UIManager.Layer.Main);
                    ui?.Bind(bestEntityId);
                    return true;
                }
                case CandidateKind.Npc:
                {
                    Debug.Log($"[Service] NPC 열기: entityId={bestEntityId}, sqr={bestSqr:F2}");
                    var ui = AR.s.UI.Show<UINpcInspect>(AddressablePath.NpcInspect, UIManager.Layer.Main);
                    ui?.Bind(bestEntityId);
                    return true;
                }
                default:
                    return false;
            }
        }
    }
}
