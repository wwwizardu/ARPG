#nullable enable
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
        public static GlobalEnum.EquipmentType SlotToEquipmentType(GlobalEnum.EquipSlotType slotType)
        {
            switch (slotType)
            {
                case GlobalEnum.EquipSlotType.WeaponLeft:
                case GlobalEnum.EquipSlotType.WeaponRight:
                    return GlobalEnum.EquipmentType.Weapon;
                case GlobalEnum.EquipSlotType.Helmet:
                    return GlobalEnum.EquipmentType.Helmet;
                case GlobalEnum.EquipSlotType.Armor:
                    return GlobalEnum.EquipmentType.Armor;
                case GlobalEnum.EquipSlotType.Gloves:
                    return GlobalEnum.EquipmentType.Gloves;
                case GlobalEnum.EquipSlotType.Boots:
                    return GlobalEnum.EquipmentType.Boots;
                case GlobalEnum.EquipSlotType.Necklace:
                    return GlobalEnum.EquipmentType.Necklace;
                case GlobalEnum.EquipSlotType.RingLeft:
                case GlobalEnum.EquipSlotType.RingRight:
                    return GlobalEnum.EquipmentType.Ring;
                case GlobalEnum.EquipSlotType.Belt:
                    return GlobalEnum.EquipmentType.Belt;
                case GlobalEnum.EquipSlotType.EarringLeft:
                case GlobalEnum.EquipSlotType.EarringRight:
                    return GlobalEnum.EquipmentType.Earring;
                default:
                    return GlobalEnum.EquipmentType.Weapon;
            }
        }

        public static GlobalEnum.EquipSlotType GetBestSlot(GlobalEnum.EquipmentType inEquipType, ItemData?[] inEquippedItems)
        {
            switch (inEquipType)
            {
                case GlobalEnum.EquipmentType.Weapon:
                    return GetDualSlot(inEquippedItems, GlobalEnum.EquipSlotType.WeaponLeft, GlobalEnum.EquipSlotType.WeaponRight);
                case GlobalEnum.EquipmentType.Ring:
                    return GetDualSlot(inEquippedItems, GlobalEnum.EquipSlotType.RingLeft, GlobalEnum.EquipSlotType.RingRight);
                case GlobalEnum.EquipmentType.Earring:
                    return GetDualSlot(inEquippedItems, GlobalEnum.EquipSlotType.EarringLeft, GlobalEnum.EquipSlotType.EarringRight);
                case GlobalEnum.EquipmentType.Helmet:
                    return GlobalEnum.EquipSlotType.Helmet;
                case GlobalEnum.EquipmentType.Armor:
                    return GlobalEnum.EquipSlotType.Armor;
                case GlobalEnum.EquipmentType.Gloves:
                    return GlobalEnum.EquipSlotType.Gloves;
                case GlobalEnum.EquipmentType.Boots:
                    return GlobalEnum.EquipSlotType.Boots;
                case GlobalEnum.EquipmentType.Necklace:
                    return GlobalEnum.EquipSlotType.Necklace;
                case GlobalEnum.EquipmentType.Belt:
                    return GlobalEnum.EquipSlotType.Belt;
                default:
                    return GlobalEnum.EquipSlotType.WeaponLeft;
            }
        }

        private static GlobalEnum.EquipSlotType GetDualSlot(ItemData?[] inEquippedItems, GlobalEnum.EquipSlotType inLeftSlot, GlobalEnum.EquipSlotType inRightSlot)
        {
            if (inEquippedItems[(int)inLeftSlot] == null)
                return inLeftSlot;

            if (inEquippedItems[(int)inRightSlot] == null)
                return inRightSlot;

            return inLeftSlot;
        }

        /// <summary>
        /// 장비 아이템의 Prefix/Postfix 스탯을 StatModifier로 등록
        /// </summary>
        /// <param name="playerEntityId">플레이어 엔티티 ID</param>
        /// <param name="item">장착할 아이템 데이터</param>
        public static void ApplyEquipmentModifiers(int playerEntityId, ItemData item)
        {
            if (item == null || item.Equipment == null)
                return;

            int sourceId = item.ItemInstanceId;

            // 무기 기본 공격력 적용
            if (item.Equipment.WeaponData != null)
            {
                var (physMin, physMax) = item.Equipment.GetPhysicsDamage();
                if (physMin > 0 || physMax > 0)
                {
                    StatModifierHelper.AddStatModifier(playerEntityId, StatModifierSource.Equipment, sourceId, GlobalEnum.Stat.AttackMin, StatModifierType.Add, physMin);
                    StatModifierHelper.AddStatModifier(playerEntityId, StatModifierSource.Equipment, sourceId, GlobalEnum.Stat.AttackMax, StatModifierType.Add, physMax);
                }

                if (item.Equipment.WeaponData.CriticalRate > 0)
                {
                    StatModifierHelper.AddStatModifier(playerEntityId, StatModifierSource.Equipment, sourceId, GlobalEnum.Stat.CriRate, StatModifierType.Add, item.Equipment.WeaponData.CriticalRate);
                }
            }

            // Prefix/Postfix 스탯 적용
            EquipmentStatData? statData = item.Equipment.StatData;
            if (statData != null)
            {
                for (int i = 0; i < statData.Prefix.Count; i++)
                {
                    Stat stat = statData.Prefix[i];
                    StatModifierHelper.AddStatModifier(playerEntityId, StatModifierSource.Equipment, sourceId, stat.Type, StatModifierType.Add, stat.Value);
                }

                for (int i = 0; i < statData.Postfix.Count; i++)
                {
                    Stat stat = statData.Postfix[i];
                    StatModifierHelper.AddStatModifier(playerEntityId, StatModifierSource.Equipment, sourceId, stat.Type, StatModifierType.Add, stat.Value);
                }
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
