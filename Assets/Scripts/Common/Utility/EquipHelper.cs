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
        /// <summary>
        /// 슬롯이 받을 수 있는 장비 타입 집합.
        /// - WeaponRight(메인 무기): Weapon 전용. 방패는 거부.
        /// - WeaponLeft(보조): Weapon | Shield. 한손+방패 또는 쌍수 무기 모두 허용.
        /// 양손 무기(TwoHanded) 미래 확장 시: WeaponRight 마스크에 OR로 비트 추가하고,
        /// PlayerData.EquipItem에서 "양손 무기 장착 시 WeaponLeft 강제 해제" 룰을 별도로 처리한다.
        /// </summary>
        public static GE.EquipmentTypeMask SlotToEquipmentTypeMask(GE.EquipSlotType slotType)
        {
            switch (slotType)
            {
                case GE.EquipSlotType.WeaponRight:
                    return GE.EquipmentTypeMask.Weapon;
                case GE.EquipSlotType.WeaponLeft:
                    return GE.EquipmentTypeMask.Weapon | GE.EquipmentTypeMask.Shield;
                case GE.EquipSlotType.Helmet:
                    return GE.EquipmentTypeMask.Helmet;
                case GE.EquipSlotType.Armor:
                    return GE.EquipmentTypeMask.Armor;
                case GE.EquipSlotType.Gloves:
                    return GE.EquipmentTypeMask.Gloves;
                case GE.EquipSlotType.Boots:
                    return GE.EquipmentTypeMask.Boots;
                case GE.EquipSlotType.Necklace:
                    return GE.EquipmentTypeMask.Necklace;
                case GE.EquipSlotType.RingLeft:
                case GE.EquipSlotType.RingRight:
                    return GE.EquipmentTypeMask.Ring;
                case GE.EquipSlotType.Belt:
                    return GE.EquipmentTypeMask.Belt;
                case GE.EquipSlotType.EarringLeft:
                case GE.EquipSlotType.EarringRight:
                    return GE.EquipmentTypeMask.Earring;
                default:
                    Debug.LogError($"[EquipHelper] SlotToEquipmentTypeMask - Unhandled slot: {slotType}");
                    return GE.EquipmentTypeMask.None;
            }
        }

        /// <summary>
        /// 아이템의 단일 비트 EquipType에 가장 적합한 슬롯을 찾는다.
        /// - Weapon: WeaponRight 우선(메인). Right가 차있으면 WeaponLeft(쌍수 보조)로.
        /// - Shield: WeaponLeft 전용.
        /// - 양손 무기 추가 시: WeaponRight 반환 + PlayerData.EquipItem에서 WeaponLeft 강제 해제 룰 필요.
        /// </summary>
        public static GE.EquipSlotType GetBestSlot(GE.EquipmentTypeMask inEquipType, ItemData?[] inEquippedItems)
        {
            switch (inEquipType)
            {
                case GE.EquipmentTypeMask.Weapon:
                    return GetDualSlot(inEquippedItems, GE.EquipSlotType.WeaponRight, GE.EquipSlotType.WeaponLeft);
                case GE.EquipmentTypeMask.Shield:
                    return GE.EquipSlotType.WeaponLeft;
                case GE.EquipmentTypeMask.Ring:
                    return GetDualSlot(inEquippedItems, GE.EquipSlotType.RingRight, GE.EquipSlotType.RingLeft);
                case GE.EquipmentTypeMask.Earring:
                    return GetDualSlot(inEquippedItems, GE.EquipSlotType.EarringRight, GE.EquipSlotType.EarringLeft);
                case GE.EquipmentTypeMask.Helmet:
                    return GE.EquipSlotType.Helmet;
                case GE.EquipmentTypeMask.Armor:
                    return GE.EquipSlotType.Armor;
                case GE.EquipmentTypeMask.Gloves:
                    return GE.EquipSlotType.Gloves;
                case GE.EquipmentTypeMask.Boots:
                    return GE.EquipSlotType.Boots;
                case GE.EquipmentTypeMask.Necklace:
                    return GE.EquipSlotType.Necklace;
                case GE.EquipmentTypeMask.Belt:
                    return GE.EquipSlotType.Belt;
                default:
                    Debug.LogError($"[EquipHelper] GetBestSlot - Unhandled equip type: {inEquipType}");
                    return GE.EquipSlotType.WeaponRight;
            }
        }

        /// <summary>
        /// 장비 화면에서 좌클릭(화면 왼쪽)·우클릭(화면 오른쪽)에 대응하는 장착 슬롯.
        /// 캐릭터가 화면을 마주 보는 배치라 오른쪽 슬롯(WeaponRight·RingRight·EarringRight)이 화면 왼쪽에 있다 (CharacterUI.prefab).
        /// 칸이 하나인 장비는 두 값 모두 그 슬롯이다.
        /// </summary>
        public static void GetScreenSideSlots(GE.EquipmentTypeMask inEquipType, out GE.EquipSlotType outScreenLeftSlot, out GE.EquipSlotType outScreenRightSlot)
        {
            switch (inEquipType)
            {
                case GE.EquipmentTypeMask.Weapon:
                    outScreenLeftSlot = GE.EquipSlotType.WeaponRight;
                    outScreenRightSlot = GE.EquipSlotType.WeaponLeft;
                    return;
                case GE.EquipmentTypeMask.Ring:
                    outScreenLeftSlot = GE.EquipSlotType.RingRight;
                    outScreenRightSlot = GE.EquipSlotType.RingLeft;
                    return;
                case GE.EquipmentTypeMask.Earring:
                    outScreenLeftSlot = GE.EquipSlotType.EarringRight;
                    outScreenRightSlot = GE.EquipSlotType.EarringLeft;
                    return;
                case GE.EquipmentTypeMask.Shield:
                    outScreenLeftSlot = GE.EquipSlotType.WeaponLeft;
                    outScreenRightSlot = GE.EquipSlotType.WeaponLeft;
                    return;
                case GE.EquipmentTypeMask.Helmet:
                    outScreenLeftSlot = GE.EquipSlotType.Helmet;
                    break;
                case GE.EquipmentTypeMask.Armor:
                    outScreenLeftSlot = GE.EquipSlotType.Armor;
                    break;
                case GE.EquipmentTypeMask.Gloves:
                    outScreenLeftSlot = GE.EquipSlotType.Gloves;
                    break;
                case GE.EquipmentTypeMask.Boots:
                    outScreenLeftSlot = GE.EquipSlotType.Boots;
                    break;
                case GE.EquipmentTypeMask.Necklace:
                    outScreenLeftSlot = GE.EquipSlotType.Necklace;
                    break;
                case GE.EquipmentTypeMask.Belt:
                    outScreenLeftSlot = GE.EquipSlotType.Belt;
                    break;
                default:
                    Debug.LogError($"[EquipHelper] GetScreenSideSlots - Unhandled equip type: {inEquipType}");
                    outScreenLeftSlot = GE.EquipSlotType.WeaponRight;
                    break;
            }

            outScreenRightSlot = outScreenLeftSlot;
        }

        // 먼저 받은 슬롯이 비었으면 그 슬롯, 다음 슬롯이 비었으면 다음 슬롯, 둘 다 차 있으면 먼저 받은 슬롯.
        // GetBestSlot은 오른쪽 슬롯(화면 왼쪽)을 먼저 넘긴다.
        private static GE.EquipSlotType GetDualSlot(ItemData?[] inEquippedItems, GE.EquipSlotType inPreferredSlot, GE.EquipSlotType inFallbackSlot)
        {
            if (inEquippedItems[(int)inPreferredSlot] == null)
                return inPreferredSlot;

            if (inEquippedItems[(int)inFallbackSlot] == null)
                return inFallbackSlot;

            return inPreferredSlot;
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
            bool isWeapon = item.Equipment.EquipType == GE.EquipmentTypeMask.Weapon;

            for (int i = 0; i < mods.Count; i++)
            {
                ModInstance mod = mods[i];
                if (mod.Table == null)
                {
                    mod.OnLoadCompleted();
                    if (mod.Table == null)
                        continue;
                }

                // 무기 Base는 무기 기본 능력 캐시에서만 소비한다.
                if (isWeapon == true && mod.Slot == GE.ModSlot.Base)
                    continue;

                switch (mod.Table.ApplyType)
                {
                    case GE.ModApplyType.Passive:
                        ApplyPassiveMod(playerEntityId, sourceId, mod);
                        break;

                    case GE.ModApplyType.OnCalculate:
                    case GE.ModApplyType.OnEvent:
                        ApplyActiveModToPool(playerEntityId, sourceId, mod);
                        break;
                }
            }

            // 스탯 재계산 요청.
            // 프로필(BuildOwnedProfiles)은 여기서 부르지 않는다 — BuildProfile이 CritMultiplier/SkillDamageMul/
            // LifeStealRatio를 StatComponent에서 읽는데 이 시점의 StatComponent는 아직 재계산 전이라 값이 틀린다.
            // System_StatCalculation이 재계산 직후 SyncDerivedStatComponents에서 올바른 값으로 갱신한다.
            AR.s.Component.AddComponent(playerEntityId, new StatDirtyTag());

            Debug.Log($"[EquipHelper] Equipment mods applied - Player: {playerEntityId}, ItemInstanceId: {sourceId}, ModCount: {mods.Count}");
        }

        /// <summary>
        /// Passive Mod → StatModifier 변환 등록.
        /// 무기 Base는 호출 전에 제외되며 Implicit/Prefix/Postfix는 장비 부위와 무관하게 이 경로를 사용한다.
        /// </summary>
        private static void ApplyPassiveMod(int playerEntityId, int sourceId, ModInstance mod)
        {
            if (mod.Table == null)
                return;

            Debug.Log($"[EquipHelper] ApplyPassiveMod - Mod={mod.Table.Name}, EffectType={mod.Table.EffectType}, TargetStat={mod.Table.TargetStat}, Slot={mod.Slot}, Tags={mod.Table.Tags}, V1={mod.Value1}, V2={mod.Value2}");

            switch (mod.Table.EffectType)
            {
                case GE.ModEffectType.FlatStat:
                    // 단일 스탯 추가 (+50 HP, +20 방어력 등)
                    if (mod.Value1 > 0)
                    {
                        // 기존 데이터의 비-Base AttackSpeed는 이름/값 모두 증가율 의미이므로 글로벌 증가 버킷으로 정규화한다.
                        if (mod.Table.TargetStat == GE.Stat.AttackSpeed
                            && mod.Slot != GE.ModSlot.Base)
                        {
                            AR.s.Data.AddStatModifier(playerEntityId, StatModifierSource.Equipment, sourceId,
                                GE.Stat.AttackSpeedMul, StatModifierType.Multiply, mod.Value1, tags: mod.Table.Tags);
                        }
                        else
                        {
                            AR.s.Data.AddStatModifier(playerEntityId, StatModifierSource.Equipment, sourceId,
                                mod.Table.TargetStat, StatModifierType.Add, mod.Value1, tags: mod.Table.Tags);
                        }
                    }
                    break;

                case GE.ModEffectType.AddedPhysDamage:
                    // 물리 데미지 추가 (Value1=Min, Value2=Max)
                    if (mod.Value1 > 0)
                        AR.s.Data.AddStatModifier(playerEntityId, StatModifierSource.Equipment, sourceId,
                            GE.Stat.AttackMin, StatModifierType.Add, mod.Value1, tags: mod.Table.Tags);
                    if (mod.Value2 > 0)
                        AR.s.Data.AddStatModifier(playerEntityId, StatModifierSource.Equipment, sourceId,
                            GE.Stat.AttackMax, StatModifierType.Add, mod.Value2, tags: mod.Table.Tags);
                    break;

                case GE.ModEffectType.AddedFireDamage:
                    if (mod.Value1 > 0)
                        AR.s.Data.AddStatModifier(playerEntityId, StatModifierSource.Equipment, sourceId,
                            GE.Stat.FireAttackMin, StatModifierType.Add, mod.Value1, tags: mod.Table.Tags);
                    if (mod.Value2 > 0)
                        AR.s.Data.AddStatModifier(playerEntityId, StatModifierSource.Equipment, sourceId,
                            GE.Stat.FireAttackMax, StatModifierType.Add, mod.Value2, tags: mod.Table.Tags);
                    break;

                case GE.ModEffectType.AddedIceDamage:
                    if (mod.Value1 > 0)
                        AR.s.Data.AddStatModifier(playerEntityId, StatModifierSource.Equipment, sourceId,
                            GE.Stat.IceAttackMin, StatModifierType.Add, mod.Value1, tags: mod.Table.Tags);
                    if (mod.Value2 > 0)
                        AR.s.Data.AddStatModifier(playerEntityId, StatModifierSource.Equipment, sourceId,
                            GE.Stat.IceAttackMax, StatModifierType.Add, mod.Value2, tags: mod.Table.Tags);
                    break;

                case GE.ModEffectType.AddedLightningDamage:
                    if (mod.Value1 > 0)
                        AR.s.Data.AddStatModifier(playerEntityId, StatModifierSource.Equipment, sourceId,
                            GE.Stat.LightningAttackMin, StatModifierType.Add, mod.Value1, tags: mod.Table.Tags);
                    if (mod.Value2 > 0)
                        AR.s.Data.AddStatModifier(playerEntityId, StatModifierSource.Equipment, sourceId,
                            GE.Stat.LightningAttackMax, StatModifierType.Add, mod.Value2, tags: mod.Table.Tags);
                    break;

                case GE.ModEffectType.AddedPoisonDamage:
                    if (mod.Value1 > 0)
                        AR.s.Data.AddStatModifier(playerEntityId, StatModifierSource.Equipment, sourceId,
                            GE.Stat.PoisonAttackMin, StatModifierType.Add, mod.Value1, tags: mod.Table.Tags);
                    if (mod.Value2 > 0)
                        AR.s.Data.AddStatModifier(playerEntityId, StatModifierSource.Equipment, sourceId,
                            GE.Stat.PoisonAttackMax, StatModifierType.Add, mod.Value2, tags: mod.Table.Tags);
                    break;

                case GE.ModEffectType.IncreasedStat:
                    // % 증가 스탯
                    if (mod.Value1 > 0)
                    {
                        AR.s.Data.AddStatModifier(playerEntityId, StatModifierSource.Equipment, sourceId,
                            mod.Table.TargetStat, StatModifierType.Multiply, mod.Value1, tags: mod.Table.Tags);
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
            int removedCount = AR.s.Data.RemoveModifiersBySource(playerEntityId, StatModifierSource.Equipment, itemInstanceId);

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

            // 스탯 재계산 요청. 프로필 갱신은 System_StatCalculation의 SyncDerivedStatComponents가 담당한다
            // (여기서 직접 부르면 재계산 전 StatComponent로 잘못 계산한 뒤 같은 프레임에 덮어쓰게 된다).
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
                ItemData? item = equippedItems[i];
                if (item != null)
                {
                    ApplyEquipmentModifiers(playerEntityId, item);
                }
            }

            Debug.Log($"[EquipHelper] All equipment modifiers applied - Player: {playerEntityId}");
        }
    }
}
