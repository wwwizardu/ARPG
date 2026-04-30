#nullable enable
using ARPG.Base;
using ARPG.Component;
using ARPG.Village;
using UnityEngine;

namespace ARPG.UI
{
    /// <summary>
    /// Phase D: MerchantStall 상점 UI (구매/판매 두 탭).
    /// ServiceUIRouter가 Show 호출 시 NearestShopEntityId로 Bind.
    ///
    /// Step 10 MVP: 인터페이스/스켈레톤만. UI 레이아웃·슬롯 위젯·이벤트 바인딩은 Step U1 prefab 작업 후.
    /// </summary>
    public class UIShopMerchant : UIBaseForm
    {
        // Step U1에서 prefab의 슬롯 영역에 연결할 참조 (현재 stub)
        // [SerializeField] private Transform _stockSlotRoot = null!;
        // [SerializeField] private Transform _inventorySlotRoot = null!;
        // [SerializeField] private Button _buyTabButton = null!;
        // [SerializeField] private Button _sellTabButton = null!;

        private int _shopEntityId = -1;
        private int _villageId = -1;
        private bool _isBuyTab = true;

        public override void Initialize(string inName, bool isForm = false)
        {
            base.Initialize(inName, isForm);
        }

        /// <summary>
        /// ServiceUIRouter가 Show 직후 호출 — 어떤 마을의 어떤 MerchantStall인지 바인딩.
        /// </summary>
        public void Bind(int shopEntityId)
        {
            _shopEntityId = shopEntityId;

            if (AR.s.Component.TryGetComponent<PlacedObjectComponent>(_shopEntityId, out var po) == false)
            {
                Debug.LogWarning($"[UIShopMerchant] Bind 실패 — entityId={shopEntityId} PlacedObject 없음");
                return;
            }
            _villageId = po.VillageId;

            // 24h 게이트 — 매물 풀 재롤
            AR.s.Village.EnsureMerchantStockFresh(_villageId);

            RefreshAll();
        }

        public override void OnOpen()
        {
            base.OnOpen();
            _isBuyTab = true;
            RefreshAll();
        }

        // ========== 탭 전환 ==========

        public void OnClickBuyTab()
        {
            _isBuyTab = true;
            RefreshAll();
        }

        public void OnClickSellTab()
        {
            _isBuyTab = false;
            RefreshAll();
        }

        // ========== 구매/판매 액션 ==========

        /// <summary>매물 슬롯 클릭 — 인덱스로 구매 시도. Step U1 prefab의 슬롯 위젯이 호출.</summary>
        public void OnClickBuyStock(int stockIndex, int amount)
        {
            if (_villageId < 0) return;
            int gold = AR.s.Village.BuyItemFromMerchant(_villageId, stockIndex, amount);
            if (gold < 0) return;
            RefreshAll();
        }

        /// <summary>인벤토리 슬롯 클릭 — 슬롯 인덱스로 판매 시도.</summary>
        public void OnClickSellSlot(int slotIndex, int amount)
        {
            if (_villageId < 0) return;
            int gold = AR.s.Village.SellItemToMerchant(_villageId, slotIndex, amount);
            if (gold < 0) return;
            RefreshAll();
        }

        // ========== 갱신 hook (prefab 작업 후 본격 구현) ==========

        private void RefreshAll()
        {
            // TODO(Step U1+): 매물 슬롯 / 인벤토리 슬롯 / Gold / 탭 표시 갱신
        }
    }
}
