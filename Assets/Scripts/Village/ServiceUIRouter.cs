#nullable enable
using ARPG.Component;
using ARPG.UI;
using UnityEngine;

namespace ARPG.Village
{
    /// <summary>
    /// Phase D: 플레이어 F키 입력 시 PlayerNearbyServicesComponent 보고 적절한 UI 라우팅.
    /// 우선순위: Shop > Forge > Inn > Shrine.
    /// 각 UI 클래스의 Bind(entityId)로 어떤 PlacedObject 기준인지 전달.
    /// </summary>
    public static class ServiceUIRouter
    {
        public static bool Open(in PlayerNearbyServicesComponent nearby)
        {
            if (nearby.AvailableServices == ProvidedService.None) return false;

            // 우선순위 순회 — 첫 번째 매칭 UI 호출
            if ((nearby.AvailableServices & ProvidedService.Shop) != 0 && nearby.NearestShopEntityId >= 0)
            {
                Debug.Log($"[Service] v{nearby.NearestVillageId} 서비스 열기: Shop (entityId={nearby.NearestShopEntityId})");
                var ui = AR.s.UI.Show<UIShopMerchant>(AddressablePath.ShopMerchant, UIManager.Layer.Main);
                ui?.Bind(nearby.NearestShopEntityId);
                return true;
            }
            if ((nearby.AvailableServices & ProvidedService.Forge) != 0 && nearby.NearestForgeEntityId >= 0)
            {
                Debug.Log($"[Service] v{nearby.NearestVillageId} 서비스 열기: Forge (entityId={nearby.NearestForgeEntityId})");
                var ui = AR.s.UI.Show<UIForge>(AddressablePath.Forge, UIManager.Layer.Main);
                ui?.Bind(nearby.NearestForgeEntityId);
                return true;
            }
            if ((nearby.AvailableServices & ProvidedService.Inn) != 0 && nearby.NearestInnEntityId >= 0)
            {
                Debug.Log($"[Service] v{nearby.NearestVillageId} 서비스 열기: Inn (entityId={nearby.NearestInnEntityId})");
                var ui = AR.s.UI.Show<UIInn>(AddressablePath.Inn, UIManager.Layer.Main);
                ui?.Bind(nearby.NearestInnEntityId);
                return true;
            }
            if ((nearby.AvailableServices & ProvidedService.Shrine) != 0 && nearby.NearestShrineEntityId >= 0)
            {
                Debug.Log($"[Service] v{nearby.NearestVillageId} 서비스 열기: Shrine (entityId={nearby.NearestShrineEntityId})");
                var ui = AR.s.UI.Show<UIShrine>(AddressablePath.Shrine, UIManager.Layer.Main);
                ui?.Bind(nearby.NearestShrineEntityId);
                return true;
            }
            return false;
        }
    }
}
