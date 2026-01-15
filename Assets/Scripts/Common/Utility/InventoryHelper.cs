#nullable enable

using ARPG.Component;
using UnityEngine;

namespace ARPG.Utility
{
    /// <summary>
    /// 인벤토리 연산 헬퍼 클래스
    /// BuffHelper 패턴을 따라 Add/Remove/Move 연산 제공
    /// </summary>
    public static class InventoryHelper
    {
        /// <summary>
        /// 아이템 인스턴스 ID 카운터
        /// </summary>
        private static int _itemInstanceCounter = 0;

        /// <summary>
        /// 헬퍼 초기화
        /// </summary>
        public static void Initialize()
        {
            _itemInstanceCounter = 0;
        }

        /// <summary>
        /// 인벤토리 초기화 (엔티티에 인벤토리 컴포넌트 추가)
        /// </summary>
        public static void InitializeInventory(int entityId, int maxSlotCount, GlobalEnum.InventoryType inventoryType = GlobalEnum.InventoryType.Character)
        {
            InventoryComponent inventory = new InventoryComponent(maxSlotCount, inventoryType);
            AR.s.Component.AddComponent(entityId, inventory);

            Debug.Log($"[InventoryHelper] Inventory initialized - Entity: {entityId}, MaxSlots: {maxSlotCount}, Type: {inventoryType}");
        }

        #region Add Item

        /// <summary>
        /// 아이템 추가 (첫 빈 슬롯 또는 기존 스택에 추가)
        /// </summary>
        /// <param name="ownerEntityId">인벤토리 소유자 엔티티 ID</param>
        /// <param name="itemTableId">아이템 테이블 ID</param>
        /// <param name="quantity">수량</param>
        /// <returns>추가된 슬롯 인덱스, 실패 시 -1</returns>
        public static int AddItem(int ownerEntityId, int itemTableId, int quantity)
        {
            if (quantity <= 0)
            {
                Debug.LogWarning("[InventoryHelper] Quantity must be greater than 0");
                return -1;
            }

            // 1. 인벤토리 컴포넌트 확인
            if (AR.s.Component.TryGetComponent<InventoryComponent>(ownerEntityId, out InventoryComponent inventory) == false)
            {
                Debug.LogError($"[InventoryHelper] Owner {ownerEntityId} has no InventoryComponent");
                return -1;
            }

            if (inventory.IsLocked)
            {
                Debug.LogWarning("[InventoryHelper] Inventory is locked");
                return -1;
            }

            // 2. 아이템 테이블 조회 (스택 가능 여부 확인)
            var itemTable = AR.s.Data?.GetItem(itemTableId);
            bool isStackable = itemTable?.Stackable ?? false;
            int maxStack = itemTable?.MaxStack ?? 1;

            // 3. 스택 가능한 경우 기존 스택 찾기
            if (isStackable)
            {
                int existingSlot = FindItemSlot(ownerEntityId, itemTableId, inventory.MaxSlotCount);
                if (existingSlot >= 0)
                {
                    int slotEntityId = InventorySlotEntityIdHelper.GetSlotEntityId(ownerEntityId, existingSlot);
                    if (AR.s.Component.TryGetComponent<InventorySlotComponent>(slotEntityId, out InventorySlotComponent slot))
                    {
                        // 최대 스택 체크
                        int newQuantity = slot.Quantity + quantity;
                        if (newQuantity <= maxStack)
                        {
                            slot.Quantity = newQuantity;
                            AR.s.Component.SetComponent(slotEntityId, slot);

                            // UI 갱신 태그 추가
                            MarkDirty(ownerEntityId);
                            return existingSlot;
                        }
                    }
                }
            }

            // 4. 빈 슬롯 찾기
            int emptySlot = FindEmptySlot(ownerEntityId, inventory.MaxSlotCount);
            if (emptySlot < 0)
            {
                Debug.LogWarning("[InventoryHelper] No empty slot available");
                return -1;
            }

            // 5. 슬롯 엔티티 생성 및 아이템 추가
            int newSlotEntityId = EntityIdHelper.CreateInventorySlotEntity(ownerEntityId, emptySlot);
            if (newSlotEntityId == -1)
            {
                Debug.LogError("[InventoryHelper] Failed to create slot entity");
                return -1;
            }

            InventorySlotComponent newSlot = new InventorySlotComponent(ownerEntityId, emptySlot)
            {
                ItemTableId = itemTableId,
                ItemInstanceId = GenerateItemInstanceId(),
                Quantity = quantity
            };

            AR.s.Component.AddComponent(newSlotEntityId, newSlot);

            // 6. 인벤토리 메타데이터 업데이트
            inventory.UsedSlotCount++;
            AR.s.Component.SetComponent(ownerEntityId, inventory);

            // 7. UI 갱신 태그 추가
            MarkDirty(ownerEntityId);

            Debug.Log($"[InventoryHelper] Item added - Owner: {ownerEntityId}, Slot: {emptySlot}, ItemId: {itemTableId}, Qty: {quantity}");
            return emptySlot;
        }

        /// <summary>
        /// 장비 아이템 추가 (장비 인스턴스 엔티티 포함)
        /// </summary>
        public static int AddEquipmentItem(int ownerEntityId, int itemTableId, EquipmentInstanceComponent equipmentData)
        {
            // 1. 인벤토리 컴포넌트 확인
            if (AR.s.Component.TryGetComponent<InventoryComponent>(ownerEntityId, out InventoryComponent inventory) == false)
            {
                Debug.LogError($"[InventoryHelper] Owner {ownerEntityId} has no InventoryComponent");
                return -1;
            }

            if (inventory.IsLocked)
            {
                Debug.LogWarning("[InventoryHelper] Inventory is locked");
                return -1;
            }

            // 2. 빈 슬롯 찾기
            int emptySlot = FindEmptySlot(ownerEntityId, inventory.MaxSlotCount);
            if (emptySlot < 0)
            {
                Debug.LogWarning("[InventoryHelper] No empty slot available");
                return -1;
            }

            // 3. 장비 인스턴스 엔티티 생성
            int equipmentEntityId = EntityIdHelper.CreateEquipmentEntity();

            // 4. 슬롯 엔티티 생성
            int slotEntityId = EntityIdHelper.CreateInventorySlotEntity(ownerEntityId, emptySlot);
            if (slotEntityId == -1)
            {
                EntityIdHelper.DestroyEquipmentEntity(equipmentEntityId);
                Debug.LogError("[InventoryHelper] Failed to create slot entity");
                return -1;
            }

            // 5. 장비 인스턴스 컴포넌트 설정
            equipmentData.SlotEntityId = slotEntityId;
            AR.s.Component.AddComponent(equipmentEntityId, equipmentData);

            // 6. 슬롯 컴포넌트 설정
            InventorySlotComponent slot = new InventorySlotComponent(ownerEntityId, emptySlot)
            {
                ItemTableId = itemTableId,
                ItemInstanceId = GenerateItemInstanceId(),
                Quantity = 1,
                EquipmentEntityId = equipmentEntityId
            };

            AR.s.Component.AddComponent(slotEntityId, slot);

            // 7. 인벤토리 메타데이터 업데이트
            inventory.UsedSlotCount++;
            AR.s.Component.SetComponent(ownerEntityId, inventory);

            // 8. UI 갱신 태그 추가
            MarkDirty(ownerEntityId);

            Debug.Log($"[InventoryHelper] Equipment added - Owner: {ownerEntityId}, Slot: {emptySlot}, ItemId: {itemTableId}");
            return emptySlot;
        }

        #endregion

        #region Remove Item

        /// <summary>
        /// 아이템 제거
        /// </summary>
        /// <param name="ownerEntityId">인벤토리 소유자 엔티티 ID</param>
        /// <param name="slotIndex">슬롯 인덱스</param>
        /// <param name="quantity">제거할 수량</param>
        /// <returns>성공 여부</returns>
        public static bool RemoveItem(int ownerEntityId, int slotIndex, int quantity)
        {
            if (quantity <= 0)
            {
                Debug.LogWarning("[InventoryHelper] Quantity must be greater than 0");
                return false;
            }

            // 1. 슬롯 엔티티 조회
            int slotEntityId = InventorySlotEntityIdHelper.GetSlotEntityId(ownerEntityId, slotIndex);
            if (AR.s.Component.TryGetComponent<InventorySlotComponent>(slotEntityId, out InventorySlotComponent slot) == false)
            {
                Debug.LogWarning($"[InventoryHelper] Slot not found - Owner: {ownerEntityId}, Slot: {slotIndex}");
                return false;
            }

            if (slot.IsEmpty)
            {
                Debug.LogWarning("[InventoryHelper] Slot is empty");
                return false;
            }

            // 2. 수량 감소 또는 슬롯 클리어
            if (slot.Quantity <= quantity)
            {
                // 슬롯 전체 클리어
                ClearSlot(ownerEntityId, slotIndex);
            }
            else
            {
                // 수량만 감소
                slot.Quantity -= quantity;
                AR.s.Component.SetComponent(slotEntityId, slot);
            }

            // 3. UI 갱신 태그 추가
            MarkDirty(ownerEntityId);

            Debug.Log($"[InventoryHelper] Item removed - Owner: {ownerEntityId}, Slot: {slotIndex}, Qty: {quantity}");
            return true;
        }

        /// <summary>
        /// 슬롯 전체 클리어
        /// </summary>
        public static void ClearSlot(int ownerEntityId, int slotIndex)
        {
            int slotEntityId = InventorySlotEntityIdHelper.GetSlotEntityId(ownerEntityId, slotIndex);

            if (AR.s.Component.TryGetComponent<InventorySlotComponent>(slotEntityId, out InventorySlotComponent slot) == false)
            {
                return;
            }

            // 장비 인스턴스 엔티티 제거
            if (slot.EquipmentEntityId != 0)
            {
                EntityIdHelper.DestroyEquipmentEntity(slot.EquipmentEntityId);
            }

            // 슬롯 데이터 클리어
            slot.Clear();
            AR.s.Component.SetComponent(slotEntityId, slot);

            // 인벤토리 메타데이터 업데이트
            if (AR.s.Component.TryGetComponent<InventoryComponent>(ownerEntityId, out InventoryComponent inventory))
            {
                inventory.UsedSlotCount = Mathf.Max(0, inventory.UsedSlotCount - 1);
                AR.s.Component.SetComponent(ownerEntityId, inventory);
            }
        }

        #endregion

        #region Move Item

        /// <summary>
        /// 아이템 이동/교환
        /// 같은 인벤토리 내 이동 또는 다른 인벤토리 간 이동 모두 지원
        /// </summary>
        public static bool MoveItem(int fromOwner, int fromSlot, int toOwner, int toSlot)
        {
            int fromSlotEntityId = InventorySlotEntityIdHelper.GetSlotEntityId(fromOwner, fromSlot);
            int toSlotEntityId = InventorySlotEntityIdHelper.GetSlotEntityId(toOwner, toSlot);

            // 1. 출발지 슬롯 확인
            bool hasFromSlot = AR.s.Component.TryGetComponent<InventorySlotComponent>(fromSlotEntityId, out InventorySlotComponent fromSlotData);

            if (hasFromSlot == false || fromSlotData.IsEmpty)
            {
                Debug.LogWarning("[InventoryHelper] Source slot is empty or not found");
                return false;
            }

            // 2. 목적지 인벤토리 확인
            if (AR.s.Component.TryGetComponent<InventoryComponent>(toOwner, out InventoryComponent toInventory) == false)
            {
                Debug.LogError($"[InventoryHelper] Target owner {toOwner} has no InventoryComponent");
                return false;
            }

            if (toInventory.IsLocked)
            {
                Debug.LogWarning("[InventoryHelper] Target inventory is locked");
                return false;
            }

            // 3. 슬롯 인덱스 범위 확인
            if (toSlot < 0 || toSlot >= toInventory.MaxSlotCount)
            {
                Debug.LogWarning($"[InventoryHelper] Target slot index out of range: {toSlot}");
                return false;
            }

            // 4. 목적지 슬롯 확인
            bool hasToSlot = AR.s.Component.TryGetComponent<InventorySlotComponent>(toSlotEntityId, out InventorySlotComponent toSlotData);

            if (hasToSlot && toSlotData.IsEmpty == false)
            {
                // 교환 (Swap)
                SwapSlots(fromOwner, fromSlot, fromSlotEntityId, ref fromSlotData,
                          toOwner, toSlot, toSlotEntityId, ref toSlotData);
            }
            else
            {
                // 단순 이동
                MoveToEmptySlot(fromOwner, fromSlot, fromSlotEntityId, ref fromSlotData,
                                toOwner, toSlot, toSlotEntityId);
            }

            // 5. UI 갱신 태그 추가
            MarkDirty(fromOwner);
            if (fromOwner != toOwner)
            {
                MarkDirty(toOwner);
            }

            Debug.Log($"[InventoryHelper] Item moved - From: {fromOwner}:{fromSlot} To: {toOwner}:{toSlot}");
            return true;
        }

        private static void SwapSlots(
            int fromOwner, int fromSlot, int fromSlotEntityId, ref InventorySlotComponent fromSlotData,
            int toOwner, int toSlot, int toSlotEntityId, ref InventorySlotComponent toSlotData)
        {
            // 데이터 임시 저장
            int tempItemTableId = fromSlotData.ItemTableId;
            int tempItemInstanceId = fromSlotData.ItemInstanceId;
            int tempQuantity = fromSlotData.Quantity;
            int tempEquipmentEntityId = fromSlotData.EquipmentEntityId;

            // from <- to
            fromSlotData.ItemTableId = toSlotData.ItemTableId;
            fromSlotData.ItemInstanceId = toSlotData.ItemInstanceId;
            fromSlotData.Quantity = toSlotData.Quantity;
            fromSlotData.EquipmentEntityId = toSlotData.EquipmentEntityId;

            // to <- temp (original from)
            toSlotData.ItemTableId = tempItemTableId;
            toSlotData.ItemInstanceId = tempItemInstanceId;
            toSlotData.Quantity = tempQuantity;
            toSlotData.EquipmentEntityId = tempEquipmentEntityId;

            // 장비 인스턴스의 SlotEntityId 업데이트
            if (fromSlotData.EquipmentEntityId != 0)
            {
                UpdateEquipmentSlotReference(fromSlotData.EquipmentEntityId, fromSlotEntityId);
            }
            if (toSlotData.EquipmentEntityId != 0)
            {
                UpdateEquipmentSlotReference(toSlotData.EquipmentEntityId, toSlotEntityId);
            }

            AR.s.Component.SetComponent(fromSlotEntityId, fromSlotData);
            AR.s.Component.SetComponent(toSlotEntityId, toSlotData);
        }

        private static void MoveToEmptySlot(
            int fromOwner, int fromSlot, int fromSlotEntityId, ref InventorySlotComponent fromSlotData,
            int toOwner, int toSlot, int toSlotEntityId)
        {
            // 목적지 슬롯 엔티티 생성 (없으면)
            if (AR.s.Component.HasComponent<InventorySlotComponent>(toSlotEntityId) == false)
            {
                EntityIdHelper.CreateInventorySlotEntity(toOwner, toSlot);
            }

            // 새 슬롯 데이터 생성
            InventorySlotComponent newSlot = new InventorySlotComponent(toOwner, toSlot)
            {
                ItemTableId = fromSlotData.ItemTableId,
                ItemInstanceId = fromSlotData.ItemInstanceId,
                Quantity = fromSlotData.Quantity,
                EquipmentEntityId = fromSlotData.EquipmentEntityId
            };

            // 장비 인스턴스의 SlotEntityId 업데이트
            if (newSlot.EquipmentEntityId != 0)
            {
                UpdateEquipmentSlotReference(newSlot.EquipmentEntityId, toSlotEntityId);
            }

            AR.s.Component.AddComponent(toSlotEntityId, newSlot);

            // 출발지 슬롯 클리어 (장비 엔티티는 이동했으므로 삭제하지 않음)
            fromSlotData.Clear();
            AR.s.Component.SetComponent(fromSlotEntityId, fromSlotData);

            // 메타데이터 업데이트 (다른 인벤토리 간 이동 시)
            if (fromOwner != toOwner)
            {
                if (AR.s.Component.TryGetComponent<InventoryComponent>(fromOwner, out InventoryComponent fromInventory))
                {
                    fromInventory.UsedSlotCount = Mathf.Max(0, fromInventory.UsedSlotCount - 1);
                    AR.s.Component.SetComponent(fromOwner, fromInventory);
                }

                if (AR.s.Component.TryGetComponent<InventoryComponent>(toOwner, out InventoryComponent toInventory))
                {
                    toInventory.UsedSlotCount++;
                    AR.s.Component.SetComponent(toOwner, toInventory);
                }
            }
        }

        private static void UpdateEquipmentSlotReference(int equipmentEntityId, int newSlotEntityId)
        {
            if (AR.s.Component.TryGetComponent<EquipmentInstanceComponent>(equipmentEntityId, out EquipmentInstanceComponent equipment))
            {
                equipment.SlotEntityId = newSlotEntityId;
                AR.s.Component.SetComponent(equipmentEntityId, equipment);
            }
        }

        #endregion

        #region Query

        /// <summary>
        /// 슬롯 데이터 조회
        /// </summary>
        public static bool TryGetSlot(int ownerEntityId, int slotIndex, out InventorySlotComponent slot)
        {
            int slotEntityId = InventorySlotEntityIdHelper.GetSlotEntityId(ownerEntityId, slotIndex);
            return AR.s.Component.TryGetComponent(slotEntityId, out slot);
        }

        /// <summary>
        /// 특정 아이템이 있는 슬롯 찾기
        /// </summary>
        /// <returns>슬롯 인덱스, 없으면 -1</returns>
        public static int FindItemSlot(int ownerEntityId, int itemTableId, int maxSlots)
        {
            for (int i = 0; i < maxSlots; i++)
            {
                int slotEntityId = InventorySlotEntityIdHelper.GetSlotEntityId(ownerEntityId, i);
                if (AR.s.Component.TryGetComponent<InventorySlotComponent>(slotEntityId, out InventorySlotComponent slot))
                {
                    if (slot.ItemTableId == itemTableId)
                    {
                        return i;
                    }
                }
            }
            return -1;
        }

        /// <summary>
        /// 빈 슬롯 찾기
        /// </summary>
        /// <returns>빈 슬롯 인덱스, 없으면 -1</returns>
        public static int FindEmptySlot(int ownerEntityId, int maxSlots)
        {
            for (int i = 0; i < maxSlots; i++)
            {
                int slotEntityId = InventorySlotEntityIdHelper.GetSlotEntityId(ownerEntityId, i);

                // 슬롯 엔티티가 없거나 비어있으면 빈 슬롯
                if (AR.s.Component.HasComponent<InventorySlotComponent>(slotEntityId) == false)
                {
                    return i;
                }

                if (AR.s.Component.TryGetComponent<InventorySlotComponent>(slotEntityId, out InventorySlotComponent slot))
                {
                    if (slot.IsEmpty)
                    {
                        return i;
                    }
                }
            }
            return -1;
        }

        /// <summary>
        /// 특정 아이템의 총 수량 계산
        /// </summary>
        public static int GetItemTotalQuantity(int ownerEntityId, int itemTableId)
        {
            if (AR.s.Component.TryGetComponent<InventoryComponent>(ownerEntityId, out InventoryComponent inventory) == false)
            {
                return 0;
            }

            int total = 0;
            for (int i = 0; i < inventory.MaxSlotCount; i++)
            {
                int slotEntityId = InventorySlotEntityIdHelper.GetSlotEntityId(ownerEntityId, i);
                if (AR.s.Component.TryGetComponent<InventorySlotComponent>(slotEntityId, out InventorySlotComponent slot))
                {
                    if (slot.ItemTableId == itemTableId)
                    {
                        total += slot.Quantity;
                    }
                }
            }
            return total;
        }

        #endregion

        #region Utility

        /// <summary>
        /// 인벤토리 UI 갱신 태그 추가
        /// </summary>
        private static void MarkDirty(int ownerEntityId)
        {
            if (AR.s.Component.HasComponent<InventoryDirtyTag>(ownerEntityId) == false)
            {
                AR.s.Component.AddComponent(ownerEntityId, new InventoryDirtyTag());
            }
        }

        /// <summary>
        /// 아이템 인스턴스 ID 생성
        /// </summary>
        private static int GenerateItemInstanceId()
        {
            return ++_itemInstanceCounter;
        }

        /// <summary>
        /// 인벤토리 정리 (엔티티의 모든 인벤토리 데이터 제거)
        /// </summary>
        public static void CleanupInventory(int ownerEntityId)
        {
            // 모든 슬롯 엔티티 제거
            EntityIdHelper.DestroyAllInventorySlots(ownerEntityId);

            // 인벤토리 컴포넌트 제거
            AR.s.Component.RemoveComponent<InventoryComponent>(ownerEntityId);
            AR.s.Component.RemoveComponent<InventoryDirtyTag>(ownerEntityId);

            Debug.Log($"[InventoryHelper] Inventory cleaned up - Entity: {ownerEntityId}");
        }

        #endregion
    }
}
