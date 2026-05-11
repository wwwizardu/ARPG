#nullable enable
using System.Collections.Generic;
using ARPG.Component;
using ARPG.Data;
using ARPG.Tables;
using UnityEngine;
using GE = GlobalEnum;

namespace ARPG.Utility
{
    /// <summary>
    /// 장비 스탯 연동 유틸리티
    /// 장비 장착/해제 시 Mod를 처리:
    /// - Passive Mod → StatModifier 등록
    /// - OnCalculate/OnEvent Mod → ModPoolComponent 등록
    /// </summary>
    public static class EquipHelper
    {
        public static GE.EquipmentType SlotToEquipmentType(GE.EquipSlotType slotType)
        {
            switch (slotType)
            {
                case GE.EquipSlotType.WeaponLeft:
                case GE.EquipSlotType.WeaponRight:
                    return GE.EquipmentType.Weapon;
                case GE.EquipSlotType.Helmet:
                    return GE.EquipmentType.Helmet;
                case GE.EquipSlotType.Armor:
                    return GE.EquipmentType.Armor;
                case GE.EquipSlotType.Gloves:
                    return GE.EquipmentType.Gloves;
                case GE.EquipSlotType.Boots:
                    return GE.EquipmentType.Boots;
                case GE.EquipSlotType.Necklace:
                    return GE.EquipmentType.Necklace;
                case GE.EquipSlotType.RingLeft:
                case GE.EquipSlotType.RingRight:
                    return GE.EquipmentType.Ring;
                case GE.EquipSlotType.Belt:
                    return GE.EquipmentType.Belt;
                case GE.EquipSlotType.EarringLeft:
                case GE.EquipSlotType.EarringRight:
                    return GE.EquipmentType.Earring;
                default:
                    return GE.EquipmentType.Weapon;
            }
        }

        public static GE.EquipSlotType GetBestSlot(GE.EquipmentType inEquipType, ItemData?[] inEquippedItems)
        {
            switch (inEquipType)
            {
                case GE.EquipmentType.Weapon:
                    return GetDualSlot(inEquippedItems, GE.EquipSlotType.WeaponLeft, GE.EquipSlotType.WeaponRight);
                case GE.EquipmentType.Ring:
                    return GetDualSlot(inEquippedItems, GE.EquipSlotType.RingLeft, GE.EquipSlotType.RingRight);
                case GE.EquipmentType.Earring:
                    return GetDualSlot(inEquippedItems, GE.EquipSlotType.EarringLeft, GE.EquipSlotType.EarringRight);
                case GE.EquipmentType.Helmet:
                    return GE.EquipSlotType.Helmet;
                case GE.EquipmentType.Armor:
                    return GE.EquipSlotType.Armor;
                case GE.EquipmentType.Gloves:
                    return GE.EquipSlotType.Gloves;
                case GE.EquipmentType.Boots:
                    return GE.EquipSlotType.Boots;
                case GE.EquipmentType.Necklace:
                    return GE.EquipSlotType.Necklace;
                case GE.EquipmentType.Belt:
                    return GE.EquipSlotType.Belt;
                default:
                    return GE.EquipSlotType.WeaponLeft;
            }
        }

        private static GE.EquipSlotType GetDualSlot(ItemData?[] inEquippedItems, GE.EquipSlotType inLeftSlot, GE.EquipSlotType inRightSlot)
        {
            if (inEquippedItems[(int)inLeftSlot] == null)
                return inLeftSlot;

            if (inEquippedItems[(int)inRightSlot] == null)
                return inRightSlot;

            return inLeftSlot;
        }

        /// <summary>
        /// 장비의 모든 Mod를 적용
        /// - Passive → StatModifier 등록
        /// - OnCalculate/OnEvent → ModPoolComponent 등록
        /// </summary>
        public static void ApplyEquipmentModifiers(int playerEntityId, ItemData item)
        {
            if (item == null || item.Equipment == null)
                return;

            int sourceId = item.ItemInstanceId;
            List<ModInstance> mods = item.Equipment.Mods;
            bool isWeapon = item.Equipment.EquipType == GE.EquipmentType.Weapon;

            for (int i = 0; i < mods.Count; i++)
            {
                ModInstance mod = mods[i];
                if (mod.Table == null)
                {
                    mod.OnLoadCompleted();
                    if (mod.Table == null)
                        continue;
                }

                switch (mod.Table.ApplyType)
                {
                    case GE.ModApplyType.Passive:
                        ApplyPassiveMod(playerEntityId, sourceId, mod, isWeapon);
                        break;

                    case GE.ModApplyType.OnCalculate:
                    case GE.ModApplyType.OnEvent:
                        ApplyActiveModToPool(playerEntityId, sourceId, mod);
                        break;
                }
            }

            // 스탯 재계산 요청
            AR.s.Component.AddComponent(playerEntityId, new StatDirtyTag());

            Debug.Log($"[EquipHelper] Equipment mods applied - Player: {playerEntityId}, ItemInstanceId: {sourceId}, ModCount: {mods.Count}");
        }

        /// <summary>
        /// Passive Mod → StatModifier 변환 등록
        /// 단, 무기 아이템의 무기 전용 Mod는 StatModifier 등록 스킵 (WeaponHelper가 EquipmentData.WeaponStats 캐시에서 직접 조회)
        /// </summary>
        private static void ApplyPassiveMod(int playerEntityId, int sourceId, ModInstance mod, bool isWeapon)
        {
            if (mod.Table == null)
                return;

            Debug.Log($"[EquipHelper] ApplyPassiveMod - isWeapon={isWeapon}, Mod={mod.Table.Name}, EffectType={mod.Table.EffectType}, TargetStat={mod.Table.TargetStat}, Slot={mod.Slot}, V1={mod.Value1}, V2={mod.Value2}");

            // 무기 아이템의 무기 전용 Mod는 스킵 (WeaponHelper 경로로 적용됨)
            if (isWeapon && WeaponHelper.IsWeaponExclusiveMod(mod.Table.EffectType, mod.Table.TargetStat))
            {
                Debug.Log($"[EquipHelper]   → skipped (weapon-exclusive, handled by WeaponStats cache)");
                return;
            }

            switch (mod.Table.EffectType)
            {
                case GE.ModEffectType.FlatStat:
                    // 단일 스탯 추가 (+50 HP, +20 방어력 등)
                    if (mod.Value1 > 0)
                    {
                        StatModifierHelper.AddStatModifier(playerEntityId, StatModifierSource.Equipment, sourceId,
                            mod.Table.TargetStat, StatModifierType.Add, mod.Value1);
                    }
                    break;

                case GE.ModEffectType.AddedPhysDamage:
                    // 물리 데미지 추가 (Value1=Min, Value2=Max)
                    if (mod.Value1 > 0)
                        StatModifierHelper.AddStatModifier(playerEntityId, StatModifierSource.Equipment, sourceId,
                            GE.Stat.AttackMin, StatModifierType.Add, mod.Value1);
                    if (mod.Value2 > 0)
                        StatModifierHelper.AddStatModifier(playerEntityId, StatModifierSource.Equipment, sourceId,
                            GE.Stat.AttackMax, StatModifierType.Add, mod.Value2);
                    break;

                case GE.ModEffectType.AddedFireDamage:
                    if (mod.Value1 > 0)
                        StatModifierHelper.AddStatModifier(playerEntityId, StatModifierSource.Equipment, sourceId,
                            GE.Stat.FireAttackMin, StatModifierType.Add, mod.Value1);
                    if (mod.Value2 > 0)
                        StatModifierHelper.AddStatModifier(playerEntityId, StatModifierSource.Equipment, sourceId,
                            GE.Stat.FireAttackMax, StatModifierType.Add, mod.Value2);
                    break;

                case GE.ModEffectType.AddedIceDamage:
                    if (mod.Value1 > 0)
                        StatModifierHelper.AddStatModifier(playerEntityId, StatModifierSource.Equipment, sourceId,
                            GE.Stat.IceAttackMin, StatModifierType.Add, mod.Value1);
                    if (mod.Value2 > 0)
                        StatModifierHelper.AddStatModifier(playerEntityId, StatModifierSource.Equipment, sourceId,
                            GE.Stat.IceAttackMax, StatModifierType.Add, mod.Value2);
                    break;

                case GE.ModEffectType.AddedLightningDamage:
                    if (mod.Value1 > 0)
                        StatModifierHelper.AddStatModifier(playerEntityId, StatModifierSource.Equipment, sourceId,
                            GE.Stat.LightningAttackMin, StatModifierType.Add, mod.Value1);
                    if (mod.Value2 > 0)
                        StatModifierHelper.AddStatModifier(playerEntityId, StatModifierSource.Equipment, sourceId,
                            GE.Stat.LightningAttackMax, StatModifierType.Add, mod.Value2);
                    break;

                case GE.ModEffectType.AddedPoisonDamage:
                    if (mod.Value1 > 0)
                        StatModifierHelper.AddStatModifier(playerEntityId, StatModifierSource.Equipment, sourceId,
                            GE.Stat.PoisonAttackMin, StatModifierType.Add, mod.Value1);
                    if (mod.Value2 > 0)
                        StatModifierHelper.AddStatModifier(playerEntityId, StatModifierSource.Equipment, sourceId,
                            GE.Stat.PoisonAttackMax, StatModifierType.Add, mod.Value2);
                    break;

                case GE.ModEffectType.IncreasedStat:
                    // % 증가 스탯
                    if (mod.Value1 > 0)
                    {
                        StatModifierHelper.AddStatModifier(playerEntityId, StatModifierSource.Equipment, sourceId,
                            mod.Table.TargetStat, StatModifierType.Multiply, mod.Value1);
                    }
                    break;
            }
        }

        /// <summary>
        /// OnCalculate/OnEvent Mod → ModPoolComponent에 등록
        /// </summary>
        private static void ApplyActiveModToPool(int playerEntityId, int sourceId, ModInstance mod)
        {
            if (mod.Table == null)
                return;

            if (AR.s.Component.TryGetComponent<ModPoolComponent>(playerEntityId, out var modPool) == false)
            {
                modPool = new ModPoolComponent();
            }

            modPool.Add(new ActiveMod
            {
                SourceItemInstanceId = sourceId,
                ModTableId = mod.ModTableId,
                EffectType = mod.Table.EffectType,
                ApplyType = mod.Table.ApplyType,
                Element = mod.Table.Element,
                Tags = mod.Table.Tags,
                Value1 = mod.Value1,
                Value2 = mod.Value2,
            });

            AR.s.Component.SetComponent(playerEntityId, modPool);
        }

        /// <summary>
        /// 장비 해제 시 모든 Mod 효과 제거
        /// </summary>
        public static void RemoveEquipmentModifiers(int playerEntityId, int itemInstanceId)
        {
            // StatModifier 제거
            int removedCount = StatModifierHelper.RemoveModifiersBySource(playerEntityId, StatModifierSource.Equipment, itemInstanceId);

            // ModPoolComponent에서 제거
            if (AR.s.Component.TryGetComponent<ModPoolComponent>(playerEntityId, out var modPool))
            {
                int poolRemoved = modPool.RemoveBySource(itemInstanceId);
                if (poolRemoved > 0)
                {
                    AR.s.Component.SetComponent(playerEntityId, modPool);
                }
                removedCount += poolRemoved;
            }

            // 스탯 재계산 요청
            AR.s.Component.AddComponent(playerEntityId, new StatDirtyTag());

            Debug.Log($"[EquipHelper] Equipment modifiers removed - Player: {playerEntityId}, ItemInstanceId: {itemInstanceId}, Removed: {removedCount}");
        }

        /// <summary>
        /// 게임 로드 시 장착된 모든 장비의 modifier를 일괄 등록
        /// </summary>
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
