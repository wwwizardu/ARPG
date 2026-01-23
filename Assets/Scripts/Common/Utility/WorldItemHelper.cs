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

        #region Pickup Item

        /// <summary>
        /// 월드 아이템 줍기 (인벤토리에 추가)
        /// </summary>
        /// <param name="worldItemEntityId">월드 아이템 엔티티 ID</param>
        /// <returns>추가된 슬롯 인덱스, 실패 시 -1</returns>
        public static int PickupItem(int worldItemEntityId)
        {
            // 월드 아이템 데이터 조회
            // if (TryGetWorldItem(worldItemEntityId, out WorldItemComponent worldItem) == false)
            // {
            //     Debug.LogWarning($"[WorldItemHelper] PickupItem - World item not found: {worldItemEntityId}");
            //     return -1;
            // }

            // // ItemData 생성
            // ItemData itemData = new ItemData
            // {
            //     Id = worldItem.ItemTableId,
            //     ItemInstanceId = worldItem.EntityId,
            //     Quantity = worldItem.Quantity
            // };

            // // 장비인 경우 EquipmentData 생성
            // if (worldItem.IsEquipment)
            // {
            //     if (AR.s.Component.TryGetComponent<EquipmentInstanceComponent>(worldItem.EquipmentEntityId, out EquipmentInstanceComponent equipInstance))
            //     {
            //         itemData.Equipment = ConvertToEquipmentData(worldItem.ItemTableId, equipInstance);
            //     }
            //     else
            //     {
            //         Debug.LogWarning($"[WorldItemHelper] PickupItem - Equipment instance not found: {worldItem.EquipmentEntityId}");
            //     }
            // }

            // 테이블 데이터 세팅
            // itemData.OnLoadCompleted();

            // // 인벤토리에 추가
            // int slotIndex = AR.s.MyPlayer.Inventory.AddItem(itemData);

            // if (slotIndex >= 0)
            // {
            //     // 월드 아이템 제거
            //     DestroyWorldItem(worldItemEntityId);
            //     Debug.Log($"[WorldItemHelper] PickupItem - Item picked up to slot {slotIndex}: {worldItem.ItemTableId}");
            // }
            // else
            // {
            //     Debug.LogWarning($"[WorldItemHelper] PickupItem - Inventory full, cannot pick up item: {worldItem.ItemTableId}");
            // }

            // return slotIndex;
            return 0;
        }

        // TODO: PlayerData 인벤토리 연동 구현 필요
        // /// <summary>
        // /// 범위 내 자동 줍기 처리
        // /// </summary>
        // public static int AutoPickupInRange(PlayerData playerData, Vector2 pickerPosition) { }

        #endregion

        #region Drop Item

        // TODO: PlayerData 인벤토리 연동 구현 필요
        // /// <summary>
        // /// 인벤토리에서 아이템 버리기
        // /// </summary>
        // public static int DropItemFromInventory(PlayerData playerData, int slotIndex, Vector2 dropPosition, int quantity = 0) { }

        #endregion

        #region Destroy / Expire

        /// <summary>
        /// 월드 아이템 제거
        /// </summary>
        public static void DestroyWorldItem(int worldItemEntityId)
        {
            // 장비 엔티티가 있으면 함께 제거
            // if (AR.s.Component.TryGetComponent<WorldItemComponent>(worldItemEntityId, out WorldItemComponent worldItem))
            // {
            //     if (worldItem.IsEquipment)
            //     {
            //         EntityIdHelper.DestroyEntity(worldItem.EquipmentEntityId);
            //     }
            // }

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
