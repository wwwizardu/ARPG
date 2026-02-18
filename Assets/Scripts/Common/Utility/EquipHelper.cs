using ARPG.Component;
using ARPG.Data;
using ARPG.Tables;
using UnityEngine;

namespace ARPG.Utility
{
    /// <summary>
    /// 장비 스탯 연동 유틸리티
    /// 장비 장착/해제 시 StatModifier를 생성/제거하여 스탯 시스템과 연결
    /// </summary>
    public static class EquipHelper
    {
        /// <summary>
        /// 장비 아이템의 Prefix/Postfix 스탯을 StatModifier로 등록
        /// </summary>
        /// <param name="playerEntityId">플레이어 엔티티 ID</param>
        /// <param name="item">장착할 아이템 데이터</param>
        public static void ApplyEquipmentModifiers(int playerEntityId, ItemData item)
        {
            if (item == null || item.Equipment == null || item.Equipment.StatData == null)
                return;

            EquipmentStatData statData = item.Equipment.StatData;
            int sourceId = item.ItemInstanceId;

            // Prefix 스탯 적용
            for (int i = 0; i < statData.Prefix.Count; i++)
            {
                Stat stat = statData.Prefix[i];
                StatModifierHelper.AddStatModifier(playerEntityId, StatModifierSource.Equipment, sourceId, stat.Type, StatModifierType.Add, stat.Value);
            }

            // Postfix 스탯 적용
            for (int i = 0; i < statData.Postfix.Count; i++)
            {
                Stat stat = statData.Postfix[i];
                StatModifierHelper.AddStatModifier(playerEntityId, StatModifierSource.Equipment, sourceId, stat.Type, StatModifierType.Add, stat.Value);
            }

            // 스탯 재계산 요청
            AR.s.Component.AddComponent(playerEntityId, new StatDirtyTag());

            Debug.Log($"[EquipHelper] Equipment modifiers applied - Player: {playerEntityId}, ItemInstanceId: {sourceId}");
        }

        /// <summary>
        /// 장비 아이템의 StatModifier를 모두 제거
        /// </summary>
        /// <param name="playerEntityId">플레이어 엔티티 ID</param>
        /// <param name="itemInstanceId">제거할 아이템의 인스턴스 ID</param>
        public static void RemoveEquipmentModifiers(int playerEntityId, int itemInstanceId)
        {
            int removedCount = StatModifierHelper.RemoveModifiersBySource(playerEntityId, StatModifierSource.Equipment, itemInstanceId);

            // 스탯 재계산 요청
            AR.s.Component.AddComponent(playerEntityId, new StatDirtyTag());

            Debug.Log($"[EquipHelper] Equipment modifiers removed - Player: {playerEntityId}, ItemInstanceId: {itemInstanceId}, Removed: {removedCount}");
        }

        /// <summary>
        /// 게임 로드 시 장착된 모든 장비의 modifier를 일괄 등록
        /// </summary>
        /// <param name="playerEntityId">플레이어 엔티티 ID</param>
        /// <param name="equippedItems">장착 아이템 배열</param>
        public static void ApplyAllEquipmentModifiers(int playerEntityId, ItemData?[] equippedItems)
        {
            if (equippedItems == null)
                return;

            for (int i = 0; i < equippedItems.Length; i++)
            {
                if (equippedItems[i] != null)
                {
                    ApplyEquipmentModifiers(playerEntityId, equippedItems[i]);
                }
            }

            Debug.Log($"[EquipHelper] All equipment modifiers applied - Player: {playerEntityId}");
        }
    }
}
