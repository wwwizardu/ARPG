#nullable enable

using ARPG.Component;
using UnityEngine;

namespace ARPG.Utility
{
    /// <summary>
    /// 월드 아이템 연산 헬퍼 클래스
    /// 월드 아이템 생성, 줍기, 버리기, 자동 소멸 처리
    /// </summary>
    public static class WorldItemHelper
    {
        /// <summary>
        /// 기본 자동 소멸 시간 (초)
        /// </summary>
        public const float DEFAULT_EXPIRE_TIME = 300f; // 5분

        /// <summary>
        /// 기본 자동 줍기 범위
        /// </summary>
        public const float DEFAULT_AUTO_PICKUP_RANGE = 1.5f;

        /// <summary>
        /// 아이템 인스턴스 ID 카운터
        /// </summary>
        private static int _worldItemInstanceCounter = 0;

        /// <summary>
        /// 헬퍼 초기화
        /// </summary>
        public static void Initialize()
        {
            _worldItemInstanceCounter = 0;
        }

        #region Create World Item

        /// <summary>
        /// 월드 아이템 생성
        /// 장비 아이템인 경우 equipmentData를 전달
        /// </summary>
        /// <param name="position">월드 위치</param>
        /// <param name="itemTableId">아이템 테이블 ID</param>
        /// <param name="quantity">수량 (장비는 무조건 1)</param>
        /// <param name="autoPickup">자동 줍기 가능 여부</param>
        /// <param name="expireTime">자동 소멸 시간 (0이면 소멸 안함)</param>
        /// <param name="equipmentData">장비 데이터 (null이면 일반 아이템)</param>
        /// <returns>생성된 월드 아이템 엔티티 ID, 실패 시 -1</returns>
        public static int CreateWorldItem(
            Vector2 position,
            int itemTableId,
            int quantity,
            bool autoPickup = false,
            float expireTime = DEFAULT_EXPIRE_TIME,
            EquipmentInstanceComponent? equipmentData = null)
        {
            bool isEquipment = equipmentData.HasValue;
            int equipmentEntityId = 0;

            // 1. 장비인 경우 장비 인스턴스 엔티티 먼저 생성
            if (isEquipment)
            {
                equipmentEntityId = EntityIdHelper.CreateEntity();
            }

            // 2. 월드 아이템 엔티티 생성
            int entityId = EntityIdHelper.CreateEntity();

            // 3. TransformComponent 추가
            TransformComponent transform = new TransformComponent
            {
                Position = position,
                Rotation = 0f,
                Scale = Vector2.one
            };
            AR.s.Component.AddComponent(entityId, transform);

            // 4. WorldItemComponent 추가
            WorldItemComponent worldItem = new WorldItemComponent
            {
                ItemTableId = itemTableId,
                ItemInstanceId = GenerateWorldItemInstanceId(),
                Quantity = isEquipment ? 1 : quantity,  // 장비는 무조건 1개
                EquipmentEntityId = equipmentEntityId,
                DropTime = Time.time,
                ExpireTime = expireTime,
                AutoPickupEnabled = autoPickup,
                AutoPickupRange = autoPickup ? DEFAULT_AUTO_PICKUP_RANGE : 0f
            };
            AR.s.Component.AddComponent(entityId, worldItem);

            // 5. 장비인 경우 EquipmentInstanceComponent 추가
            if (isEquipment)
            {
                EquipmentInstanceComponent equipData = equipmentData.Value;
                equipData.SlotEntityId = 0;  // 아직 인벤토리에 없음
                AR.s.Component.AddComponent(equipmentEntityId, equipData);
            }

            Debug.Log($"[WorldItemHelper] World item created - EntityId: {entityId}, ItemId: {itemTableId}, Qty: {worldItem.Quantity}, IsEquip: {isEquipment}");
            return entityId;
        }

        #endregion

        #region Pickup Item

        /// <summary>
        /// 월드 아이템 줍기 (인벤토리에 추가)
        /// </summary>
        /// <param name="worldItemEntityId">월드 아이템 엔티티 ID</param>
        /// <param name="pickerEntityId">줍는 캐릭터 엔티티 ID</param>
        /// <returns>인벤토리 슬롯 인덱스, 실패 시 -1</returns>
        public static int PickupItem(int worldItemEntityId, int pickerEntityId)
        {
            // 1. 월드 아이템 컴포넌트 조회
            if (AR.s.Component.TryGetComponent<WorldItemComponent>(worldItemEntityId, out WorldItemComponent worldItem) == false)
            {
                Debug.LogWarning($"[WorldItemHelper] World item not found: {worldItemEntityId}");
                return -1;
            }

            // 2. 줍는 캐릭터의 인벤토리 확인
            if (AR.s.Component.TryGetComponent<InventoryComponent>(pickerEntityId, out InventoryComponent inventory) == false)
            {
                Debug.LogWarning($"[WorldItemHelper] Picker has no inventory: {pickerEntityId}");
                return -1;
            }

            if (inventory.IsFull)
            {
                Debug.LogWarning("[WorldItemHelper] Inventory is full");
                return -1;
            }

            int slotIndex;

            // 3. 장비 아이템인 경우
            if (worldItem.IsEquipment)
            {
                // 장비 인스턴스 컴포넌트 조회
                if (AR.s.Component.TryGetComponent<EquipmentInstanceComponent>(worldItem.EquipmentEntityId, out EquipmentInstanceComponent equipData) == false)
                {
                    Debug.LogError($"[WorldItemHelper] Equipment instance not found: {worldItem.EquipmentEntityId}");
                    return -1;
                }

                // 인벤토리에 장비 추가
                slotIndex = InventoryHelper.AddEquipmentItem(pickerEntityId, worldItem.ItemTableId, equipData);

                if (slotIndex >= 0)
                {
                    // 기존 장비 엔티티 제거 (인벤토리에서 새로 생성됨)
                    EntityIdHelper.DestroyEntity(worldItem.EquipmentEntityId);
                }
            }
            else
            {
                // 4. 일반 아이템인 경우
                slotIndex = InventoryHelper.AddItem(pickerEntityId, worldItem.ItemTableId, worldItem.Quantity);
            }

            // 5. 성공 시 월드 아이템 제거
            if (slotIndex >= 0)
            {
                DestroyWorldItem(worldItemEntityId);
                Debug.Log($"[WorldItemHelper] Item picked up - WorldItem: {worldItemEntityId} -> Inventory slot: {slotIndex}");
            }

            return slotIndex;
        }

        /// <summary>
        /// 범위 내 자동 줍기 처리
        /// </summary>
        /// <param name="pickerEntityId">줍는 캐릭터 엔티티 ID</param>
        /// <param name="pickerPosition">캐릭터 위치</param>
        /// <returns>주운 아이템 개수</returns>
        public static int AutoPickupInRange(int pickerEntityId, Vector2 pickerPosition)
        {
            int pickedCount = 0;

            SparseSet<WorldItemComponent> worldItemPool = AR.s.Component.GetComponentPool<WorldItemComponent>();
            if (worldItemPool == null || worldItemPool.Count == 0)
                return 0;

            // 역순으로 순회 (삭제 안전)
            for (int i = worldItemPool.Count - 1; i >= 0; i--)
            {
                int entityId = worldItemPool.GetEntityId(i);
                WorldItemComponent worldItem = worldItemPool.GetByIndex(i);

                // 자동 줍기 비활성화면 스킵
                if (worldItem.AutoPickupEnabled == false)
                    continue;

                // 위치 확인
                if (AR.s.Component.TryGetComponent<TransformComponent>(entityId, out TransformComponent transform) == false)
                    continue;

                // 거리 체크 (SqrMagnitude 사용)
                float sqrDistance = (transform.Position - pickerPosition).sqrMagnitude;
                float sqrRange = worldItem.AutoPickupRange * worldItem.AutoPickupRange;

                if (sqrDistance <= sqrRange)
                {
                    int result = PickupItem(entityId, pickerEntityId);
                    if (result >= 0)
                    {
                        pickedCount++;
                    }
                }
            }

            return pickedCount;
        }

        #endregion

        #region Drop Item

        /// <summary>
        /// 인벤토리에서 아이템 버리기
        /// </summary>
        /// <param name="ownerEntityId">인벤토리 소유자 엔티티 ID</param>
        /// <param name="slotIndex">버릴 슬롯 인덱스</param>
        /// <param name="dropPosition">버릴 위치</param>
        /// <param name="quantity">버릴 수량 (0이면 전부)</param>
        /// <returns>생성된 월드 아이템 엔티티 ID, 실패 시 -1</returns>
        public static int DropItemFromInventory(int ownerEntityId, int slotIndex, Vector2 dropPosition, int quantity = 0)
        {
            // 1. 슬롯 데이터 조회
            if (InventoryHelper.TryGetSlot(ownerEntityId, slotIndex, out InventorySlotComponent slot) == false)
            {
                Debug.LogWarning($"[WorldItemHelper] Slot not found: owner={ownerEntityId}, slot={slotIndex}");
                return -1;
            }

            if (slot.IsEmpty)
            {
                Debug.LogWarning("[WorldItemHelper] Slot is empty");
                return -1;
            }

            // 수량 결정
            int dropQuantity = (quantity <= 0 || quantity >= slot.Quantity) ? slot.Quantity : quantity;

            // 2. 장비 데이터 조회 (장비인 경우)
            EquipmentInstanceComponent? equipData = null;
            if (slot.IsEquipment)
            {
                if (AR.s.Component.TryGetComponent<EquipmentInstanceComponent>(slot.EquipmentEntityId, out EquipmentInstanceComponent equipComponent) == false)
                {
                    Debug.LogError("[WorldItemHelper] Equipment data not found");
                    return -1;
                }
                equipData = equipComponent;
            }

            // 3. 월드 아이템 생성
            int worldItemEntityId = CreateWorldItem(dropPosition, slot.ItemTableId, dropQuantity, equipmentData: equipData);

            // 4. 성공 시 인벤토리에서 제거
            if (worldItemEntityId >= 0)
            {
                InventoryHelper.RemoveItem(ownerEntityId, slotIndex, dropQuantity);
                Debug.Log($"[WorldItemHelper] Item dropped - Slot: {slotIndex} -> WorldItem: {worldItemEntityId}");
            }

            return worldItemEntityId;
        }

        #endregion

        #region Destroy / Expire

        /// <summary>
        /// 월드 아이템 제거
        /// </summary>
        public static void DestroyWorldItem(int worldItemEntityId)
        {
            // 장비 엔티티가 있으면 함께 제거
            if (AR.s.Component.TryGetComponent<WorldItemComponent>(worldItemEntityId, out WorldItemComponent worldItem))
            {
                if (worldItem.IsEquipment)
                {
                    EntityIdHelper.DestroyEntity(worldItem.EquipmentEntityId);
                }
            }

            // 월드 아이템 엔티티 제거
            EntityIdHelper.DestroyEntity(worldItemEntityId);
        }

        /// <summary>
        /// 만료된 월드 아이템 정리
        /// System에서 호출하거나 주기적으로 호출
        /// </summary>
        /// <returns>제거된 아이템 수</returns>
        public static int CleanupExpiredItems()
        {
            int removedCount = 0;
            float currentTime = Time.time;

            SparseSet<WorldItemComponent> worldItemPool = AR.s.Component.GetComponentPool<WorldItemComponent>();
            if (worldItemPool == null || worldItemPool.Count == 0)
                return 0;

            // 역순으로 순회 (삭제 안전)
            for (int i = worldItemPool.Count - 1; i >= 0; i--)
            {
                int entityId = worldItemPool.GetEntityId(i);
                WorldItemComponent worldItem = worldItemPool.GetByIndex(i);

                if (worldItem.IsExpired(currentTime))
                {
                    DestroyWorldItem(entityId);
                    removedCount++;
                }
            }

            if (removedCount > 0)
            {
                Debug.Log($"[WorldItemHelper] Cleaned up {removedCount} expired items");
            }

            return removedCount;
        }

        #endregion

        #region Query

        /// <summary>
        /// 월드 아이템 데이터 조회
        /// </summary>
        public static bool TryGetWorldItem(int worldItemEntityId, out WorldItemComponent worldItem)
        {
            return AR.s.Component.TryGetComponent(worldItemEntityId, out worldItem);
        }

        /// <summary>
        /// 특정 위치 근처의 월드 아이템 찾기
        /// </summary>
        /// <param name="position">기준 위치</param>
        /// <param name="range">검색 범위</param>
        /// <returns>가장 가까운 월드 아이템 엔티티 ID, 없으면 -1</returns>
        public static int FindNearestWorldItem(Vector2 position, float range)
        {
            SparseSet<WorldItemComponent> worldItemPool = AR.s.Component.GetComponentPool<WorldItemComponent>();
            if (worldItemPool == null || worldItemPool.Count == 0)
                return -1;

            float sqrRange = range * range;
            float nearestSqrDistance = float.MaxValue;
            int nearestEntityId = -1;

            for (int i = 0; i < worldItemPool.Count; i++)
            {
                int entityId = worldItemPool.GetEntityId(i);

                if (AR.s.Component.TryGetComponent<TransformComponent>(entityId, out TransformComponent transform) == false)
                    continue;

                float sqrDistance = (transform.Position - position).sqrMagnitude;

                if (sqrDistance <= sqrRange && sqrDistance < nearestSqrDistance)
                {
                    nearestSqrDistance = sqrDistance;
                    nearestEntityId = entityId;
                }
            }

            return nearestEntityId;
        }

        #endregion

        #region Utility

        private static int GenerateWorldItemInstanceId()
        {
            return ++_worldItemInstanceCounter;
        }

        #endregion
    }
}
