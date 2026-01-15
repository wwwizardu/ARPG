#nullable enable

namespace ARPG.Component
{
    /// <summary>
    /// 인벤토리 메타데이터 컴포넌트
    /// 인벤토리를 가진 엔티티에 부착되어 인벤토리 설정 및 상태를 추적
    /// </summary>
    public struct InventoryComponent
    {
        /// <summary>
        /// 최대 슬롯 수 (인벤토리별로 다를 수 있음)
        /// </summary>
        public int MaxSlotCount;

        /// <summary>
        /// 현재 사용 중인 슬롯 수
        /// </summary>
        public int UsedSlotCount;

        /// <summary>
        /// 인벤토리 타입 (캐릭터, 창고, 상점, 드랍 등)
        /// </summary>
        public GlobalEnum.InventoryType InventoryType;

        /// <summary>
        /// 잠금 상태 (잠금 시 아이템 추가/제거 불가)
        /// </summary>
        public bool IsLocked;

        /// <summary>
        /// 인벤토리가 가득 찼는지 확인
        /// </summary>
        public readonly bool IsFull => UsedSlotCount >= MaxSlotCount;

        /// <summary>
        /// 사용 가능한 슬롯 수 반환
        /// </summary>
        public readonly int AvailableSlotCount => MaxSlotCount - UsedSlotCount;

        public InventoryComponent(int maxSlotCount, GlobalEnum.InventoryType inventoryType = GlobalEnum.InventoryType.Character)
        {
            MaxSlotCount = maxSlotCount;
            UsedSlotCount = 0;
            InventoryType = inventoryType;
            IsLocked = false;
        }
    }
}
