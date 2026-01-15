#nullable enable

namespace ARPG.Component
{
    /// <summary>
    /// 인벤토리 슬롯 컴포넌트
    /// 각 슬롯은 별도의 엔티티로 관리되며, 결정적 ID 인코딩 사용
    /// ID 계산: ownerEntityId + (slotIndex + 1) * SLOT_OFFSET
    /// </summary>
    public struct InventorySlotComponent
    {
        /// <summary>
        /// 인벤토리 소유자 엔티티 ID
        /// </summary>
        public int OwnerEntityId;

        /// <summary>
        /// 슬롯 인덱스 (0부터 시작)
        /// </summary>
        public int SlotIndex;

        /// <summary>
        /// 아이템 테이블 ID (0이면 비어있음)
        /// </summary>
        public int ItemTableId;

        /// <summary>
        /// 아이템 인스턴스 ID (스택 내 고유 식별자)
        /// </summary>
        public int ItemInstanceId;

        /// <summary>
        /// 아이템 수량 (스택 가능 아이템용)
        /// </summary>
        public int Quantity;

        /// <summary>
        /// 장비 인스턴스 엔티티 ID (장비 아이템인 경우, 0이면 장비 아님)
        /// </summary>
        public int EquipmentEntityId;

        /// <summary>
        /// 슬롯이 비어있는지 확인
        /// </summary>
        public readonly bool IsEmpty => ItemTableId == 0;

        /// <summary>
        /// 장비 아이템인지 확인
        /// </summary>
        public readonly bool IsEquipment => EquipmentEntityId != 0;

        public InventorySlotComponent(int ownerEntityId, int slotIndex)
        {
            OwnerEntityId = ownerEntityId;
            SlotIndex = slotIndex;
            ItemTableId = 0;
            ItemInstanceId = 0;
            Quantity = 0;
            EquipmentEntityId = 0;
        }

        /// <summary>
        /// 슬롯 데이터 초기화 (엔티티는 유지, 데이터만 클리어)
        /// </summary>
        public void Clear()
        {
            ItemTableId = 0;
            ItemInstanceId = 0;
            Quantity = 0;
            EquipmentEntityId = 0;
        }
    }
}
