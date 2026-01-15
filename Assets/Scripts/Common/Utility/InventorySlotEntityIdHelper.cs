#nullable enable

namespace ARPG.Utility
{
    /// <summary>
    /// 인벤토리 슬롯 엔티티 ID를 생성하고 파싱하는 헬퍼 클래스
    ///
    /// ID 생성 방식: ownerEntityId + (slotIndex + 1) * SLOT_OFFSET
    /// 예시:
    /// - Owner Entity ID: 5
    /// - Slot 0: 5 + 1*10000 = 10005
    /// - Slot 1: 5 + 2*10000 = 20005
    /// - Slot 49: 5 + 50*10000 = 500005
    ///
    /// 결정적 ID 인코딩으로 O(1) 슬롯 접근 가능
    /// </summary>
    public static class InventorySlotEntityIdHelper
    {
        /// <summary>
        /// 슬롯 오프셋 (10,000)
        /// 각 슬롯마다 10,000 범위 할당
        /// </summary>
        private const int SLOT_OFFSET = 10000;

        /// <summary>
        /// 최대 슬롯 수 (안전성을 위한 상한선)
        /// </summary>
        public const int MAX_SLOTS = 200;

        /// <summary>
        /// 소유자 엔티티 ID와 슬롯 인덱스로 슬롯 엔티티 ID 계산
        /// </summary>
        /// <param name="ownerEntityId">인벤토리 소유자 엔티티 ID</param>
        /// <param name="slotIndex">슬롯 인덱스 (0부터 시작)</param>
        /// <returns>슬롯 엔티티 ID, 실패 시 -1</returns>
        public static int GetSlotEntityId(int ownerEntityId, int slotIndex)
        {
            if (slotIndex < 0 || slotIndex >= MAX_SLOTS)
            {
                UnityEngine.Debug.LogError($"[InventorySlotEntityIdHelper] Invalid slot index: {slotIndex}. Must be between 0 and {MAX_SLOTS - 1}");
                return -1;
            }

            return ownerEntityId + (slotIndex + 1) * SLOT_OFFSET;
        }

        /// <summary>
        /// 슬롯 엔티티 ID에서 소유자 엔티티 ID 추출
        /// </summary>
        /// <param name="slotEntityId">슬롯 엔티티 ID</param>
        /// <returns>소유자 엔티티 ID</returns>
        public static int GetOwnerEntityId(int slotEntityId)
        {
            return slotEntityId % SLOT_OFFSET;
        }

        /// <summary>
        /// 슬롯 엔티티 ID에서 슬롯 인덱스 추출
        /// </summary>
        /// <param name="slotEntityId">슬롯 엔티티 ID</param>
        /// <returns>슬롯 인덱스</returns>
        public static int GetSlotIndex(int slotEntityId)
        {
            return (slotEntityId / SLOT_OFFSET) - 1;
        }

        /// <summary>
        /// 슬롯 엔티티 ID가 유효한지 확인
        /// </summary>
        /// <param name="slotEntityId">슬롯 엔티티 ID</param>
        /// <returns>유효하면 true</returns>
        public static bool IsValidSlotEntityId(int slotEntityId)
        {
            int slotIndex = GetSlotIndex(slotEntityId);
            return slotIndex >= 0 && slotIndex < MAX_SLOTS;
        }

        /// <summary>
        /// 디버그용: 슬롯 엔티티 ID 정보를 문자열로 반환
        /// </summary>
        public static string GetDebugString(int slotEntityId)
        {
            int ownerId = GetOwnerEntityId(slotEntityId);
            int slotIndex = GetSlotIndex(slotEntityId);
            return $"SlotEntityId={slotEntityId} (OwnerId={ownerId}, SlotIndex={slotIndex})";
        }
    }
}
