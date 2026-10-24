#nullable enable
using System.Collections.Generic;
using ARPG.Data;
using ARPG.Item;
using ARPG.Skill.Combat;
using ARPG.Tables;
using ARPG.Utility;
using UnityEngine;
using GE = GlobalEnum;

namespace ARPG.Npc
{
    /// <summary>
    /// 화면 밖 NPC 전투력 점수의 구성 요소. 저장하지 않고 NpcManager가 런타임 캐시로만 보관한다.
    /// </summary>
    public struct NpcPowerBreakdown
    {
        public float Attack;         // 무기 기본 능력 + 추가 피해 × 공격 속도 × 치명타 기대값
        public float Defense;        // 방어력 + 피할 확률(%) + 막기 확률 + 저항 요약
        public float Life;           // 최대 체력 + 체력 재생 요약
        public float EquipmentScore; // 공격·방어·생명력 가중 합
        public float SkillScore;     // SkillLevel 환산
        public float Total;          // NpcPower = EquipmentScore + SkillScore (용기 제외, D31)
    }

    /// <summary>
    /// NpcSaveData.InventoryEquip의 Mods만으로 NPC 전투력을 계산한다 (NPC_AMBITION_DESIGN §5.1, D31, D32).
    /// ECS 없이 동작하며, 실체화 때 EquipHelper.ApplyAllEquipmentModifiers가 쓰는 모드 해석 규칙을 그대로 따른다.
    /// - 무기 Base는 장착 무기 기본 능력(EquipmentData.WeaponStats)으로만 소비한다. 메인 무기 판정은 WeaponHelper와 같다.
    /// - Implicit/Prefix/Postfix와 방어구 Base는 Passive 경로(FlatStat/Added*Damage/IncreasedStat)로 합산한다.
    /// - OnCalculate/OnEvent 모드(ModPool 경로)는 전투 중 조건부 효과라 점수에 넣지 않는다.
    /// - 회피 항은 (기본 민첩 + 장비 민첩) × 2 + 장비 회피를 같은 지역 몬스터 정확도 기준 피할 확률(%)로 바꿔 넣는다
    ///   (PLAYER_LEVEL_CHARM_DESIGN §5.6, 2026-10-24 사용자 결정: 민첩 회피 포함). 지역 레벨은 고향 마을 청크 기준이다.
    /// 상태를 갖지 않는 순수 계산이다. 캐시는 호출자(NpcManager)가 소유한다.
    /// </summary>
    public static class NpcPowerEvaluator
    {
        // 가중치는 생성 규칙 그대로의 평범한 NPC에서 공격 ≈ 50%, 방어 ≈ 25%, 생명력 ≈ 25%가 되도록 맞춘 값이다
        // (2026-09-27 세이브 생존 2,387명 평균: Attack 12.9, Defense 54.6, Life 65.3 → NpcPower 평균 약 26).
        // 기본 아이템 장비(NPC_SHOP_EQUIPMENT, 2026-10-03) 뒤 평균: Attack 6.06, Defense 21.2, Life 21.5 → NpcPower 약 10.75.
        // 진단 메뉴 ARPG/Npc/Diagnostics/Hunt Power Normalization으로 분포를 확인한다.
        public const float AttackWeight = 1.0f;
        public const float DefenseWeight = 0.12f;
        public const float LifeWeight = 0.10f;
        // SkillLevel 1당 전투력. 기존 식에서 SkillLevel 10이 capability +0.25였던 비중을 유지한다
        // (HuntReferencePower × 0.05 — 기준 전투력이 바뀌면 같이 따라간다).
        public const float SkillScorePerLevel = NpcActivityResolver.HuntReferencePower * 0.05f;

        // 실체화 전투 기준값 (System_SkillAttackProfileCalculation: CritMultiplier = 1.5 + CriDamage/100)
        private const float BaseCritMultiplier = 1.5f;
        // 무기가 없으면 맨손 1회/초로 본다. 추가 피해 모드는 그대로 반영된다.
        private const float UnarmedAttackSpeed = 1.0f;
        private const float EvasionWeight = 1.0f;   // 피할 확률 1%p당
        private const float BlockChanceWeight = 1.0f;
        private const float ResistWeight = 0.25f;
        private const float HpGenerationWeight = 5.0f;

        public static float Evaluate(NpcSaveData saveData)
        {
            return EvaluateBreakdown(saveData, saveData.InventoryEquip).Total;
        }

        /// <summary>이 NPC의 기본 민첩·고향 지역 레벨로 주어진 장비 조합을 평가한다 (장비 교체 후보 비교용).</summary>
        public static NpcPowerBreakdown EvaluateBreakdown(NpcSaveData saveData, ItemData?[] equippedItems)
        {
            ResolveContext(saveData, out int baseDex, out int zoneLevel);
            return EvaluateBreakdown(equippedItems, saveData.SkillLevel, baseDex, zoneLevel);
        }

        /// <summary>장비만 본 점수 (기본 민첩 0, 지역 레벨 1). 장비끼리 비교할 때 쓴다.</summary>
        public static NpcPowerBreakdown EvaluateBreakdown(ItemData?[] equippedItems, int skillLevel)
        {
            return EvaluateBreakdown(equippedItems, skillLevel, 0, 1);
        }

        public static NpcPowerBreakdown EvaluateBreakdown(ItemData?[] equippedItems, int skillLevel, int baseDex, int zoneLevel)
        {
            NpcPowerBreakdown result = new NpcPowerBreakdown();
            if (equippedItems == null)
                equippedItems = System.Array.Empty<ItemData?>();

            ItemData? mainWeapon = GetMainWeapon(equippedItems);

            float addedDamageAvg = 0f;
            float flatAttackMin = 0f;
            float flatAttackMax = 0f;
            float critRateFlat = 0f;
            float critRateIncreasePct = 0f;
            float critDamagePct = 0f;
            float attackSpeedIncreasePct = 0f;
            float defense = 0f;
            float evasion = 0f;
            float dex = baseDex;
            float blockChance = 0f;
            float resistSum = 0f;
            float hp = 0f;
            float hpGeneration = 0f;

            for (int i = 0; i < equippedItems.Length; i++)
            {
                ItemData? item = equippedItems[i];
                if (item == null || item.Equipment == null || item.Equipment.Mods == null)
                    continue;

                bool isWeapon = item.Equipment.EquipType == GE.EquipmentTypeMask.Weapon;
                List<ModInstance> mods = item.Equipment.Mods;
                for (int m = 0; m < mods.Count; m++)
                {
                    ModInstance sourceMod = mods[m];
                    if (sourceMod.Table == null)
                    {
                        sourceMod.OnLoadCompleted();
                        if (sourceMod.Table == null)
                            continue;
                    }

                    // EquipHelper.ApplyEquipmentModifiers와 같다 — 무기 Base는 무기 기본 능력 캐시에서만 소비한다.
                    if (isWeapon == true && sourceMod.Slot == GE.ModSlot.Base)
                        continue;
                    // 방어구 Base에는 품질을 적용한다 (ITEM_DROP_BALANCE_DESIGN §5). NPC 장비는 품질 0이라 값이 그대로다
                    ModInstance mod = EquipmentQualityRules.ApplyToBaseMod(sourceMod, item.Equipment.Quality);
                    if (mod.Table == null || mod.Table.ApplyType != GE.ModApplyType.Passive)
                        continue;

                    switch (mod.Table.EffectType)
                    {
                        case GE.ModEffectType.FlatStat:
                            if (mod.Value1 <= 0)
                                break;
                            // 비-Base AttackSpeed는 증가율 버킷으로 정규화한다 (EquipHelper.ApplyPassiveMod와 동일).
                            if (mod.Table.TargetStat == GE.Stat.AttackSpeed && mod.Slot != GE.ModSlot.Base)
                            {
                                attackSpeedIncreasePct += mod.Value1;
                                break;
                            }
                            AccumulateFlat(mod.Table.TargetStat, mod.Value1, ref flatAttackMin, ref flatAttackMax,
                                ref critRateFlat, ref critDamagePct, ref defense, ref evasion, ref dex, ref blockChance,
                                ref resistSum, ref hp, ref hpGeneration);
                            break;

                        case GE.ModEffectType.AddedPhysDamage:
                        case GE.ModEffectType.AddedFireDamage:
                        case GE.ModEffectType.AddedIceDamage:
                        case GE.ModEffectType.AddedLightningDamage:
                        case GE.ModEffectType.AddedPoisonDamage:
                            addedDamageAvg += (mod.Value1 + mod.Value2) * 0.5f;
                            break;

                        case GE.ModEffectType.IncreasedStat:
                            if (mod.Value1 <= 0)
                                break;
                            if (mod.Table.TargetStat == GE.Stat.AttackSpeedMul || mod.Table.TargetStat == GE.Stat.AttackSpeed)
                                attackSpeedIncreasePct += mod.Value1;
                            else if (mod.Table.TargetStat == GE.Stat.CriRateMul || mod.Table.TargetStat == GE.Stat.CriRate)
                                critRateIncreasePct += mod.Value1;
                            break;
                    }
                }
            }

            float weaponDamageAvg = 0f;
            float attackSpeed = UnarmedAttackSpeed;
            float weaponCritRate = 0f;
            if (mainWeapon != null && mainWeapon.Equipment != null)
            {
                WeaponStatCache stats = mainWeapon.Equipment.WeaponStats;
                weaponDamageAvg = AverageOf(stats.Physics) + AverageOf(stats.Fire) + AverageOf(stats.Ice) +
                    AverageOf(stats.Lightning) + AverageOf(stats.Poison);
                if (stats.AttackSpeed > 0f)
                    attackSpeed = stats.AttackSpeed;
                weaponCritRate = stats.CriRate;
            }

            float hitDamage = weaponDamageAvg + addedDamageAvg + (flatAttackMin + flatAttackMax) * 0.5f;
            attackSpeed *= 1f + attackSpeedIncreasePct / 100f;
            float critRate = Mathf.Clamp((weaponCritRate + critRateFlat) * (1f + critRateIncreasePct / 100f), 0f, 100f);
            float critMultiplier = BaseCritMultiplier + critDamagePct / 100f;
            float critFactor = 1f + critRate / 100f * (critMultiplier - 1f);

            result.Attack = hitDamage * attackSpeed * critFactor;
            float evadePercent = GetEvadePercent(evasion + AttributeBonusRules.GetBonusEvasion(Mathf.RoundToInt(dex)), zoneLevel);
            result.Defense = defense + evadePercent * EvasionWeight + blockChance * BlockChanceWeight + resistSum * ResistWeight;
            result.Life = hp + hpGeneration * HpGenerationWeight;
            result.EquipmentScore = result.Attack * AttackWeight + result.Defense * DefenseWeight + result.Life * LifeWeight;
            result.SkillScore = Mathf.Max(0, skillLevel) * SkillScorePerLevel;
            result.Total = result.EquipmentScore + result.SkillScore;
            return result;
        }

        /// <summary>같은 지역 균형형 몬스터(정확도 배율 1)의 공격을 피할 확률(%). 최대 75 (적중률 하한 25%).</summary>
        public static float GetEvadePercent(float evasion, int zoneLevel)
        {
            if (evasion <= 0f)
                return 0f;

            int monsterAccuracy = HitChanceRules.GetMonsterAccuracy(Mathf.Max(1, zoneLevel), 1f);
            return (1f - HitChanceRules.GetHitChance(monsterAccuracy, Mathf.RoundToInt(evasion))) * 100f;
        }

        /// <summary>표의 기본 민첩과 고향 마을 지역 레벨. AR이 없으면 0·1로 본다.</summary>
        private static void ResolveContext(NpcSaveData saveData, out int baseDex, out int zoneLevel)
        {
            NpcTable? table = AR.s?.Data != null ? AR.s.Data.GetNpc(saveData.NpcTableId) : null;
            baseDex = table?.Stat != null ? table.Stat.Dex : 0;
            zoneLevel = AR.s?.Npc != null ? AR.s.Npc.GetNpcLootZoneLevel(saveData) : 1;
        }

        /// <summary>WeaponHelper.GetEquippedWeapon과 같은 규칙 — WeaponRight 우선, 방패는 무기로 보지 않는다.</summary>
        private static ItemData? GetMainWeapon(ItemData?[] equippedItems)
        {
            if (equippedItems.Length <= (int)GE.EquipSlotType.WeaponLeft)
                return null;

            ItemData? rightWeapon = equippedItems[(int)GE.EquipSlotType.WeaponRight];
            if (rightWeapon?.Equipment != null && rightWeapon.Equipment.EquipType == GE.EquipmentTypeMask.Weapon)
                return rightWeapon;

            ItemData? leftWeapon = equippedItems[(int)GE.EquipSlotType.WeaponLeft];
            if (leftWeapon?.Equipment != null && leftWeapon.Equipment.EquipType == GE.EquipmentTypeMask.Weapon)
                return leftWeapon;

            return null;
        }

        private static float AverageOf(DamageRange range)
        {
            return (range.Min + range.Max) * 0.5f;
        }

        private static void AccumulateFlat(GE.Stat stat, int value, ref float attackMin, ref float attackMax,
            ref float critRate, ref float critDamagePct, ref float defense, ref float evasion, ref float dex, ref float blockChance,
            ref float resistSum, ref float hp, ref float hpGeneration)
        {
            switch (stat)
            {
                case GE.Stat.AttackMin: attackMin += value; break;
                case GE.Stat.AttackMax: attackMax += value; break;
                case GE.Stat.CriRate: critRate += value; break;
                case GE.Stat.CriDamageMul: critDamagePct += value; break;
                case GE.Stat.Defense: defense += value; break;
                case GE.Stat.Evasion: evasion += value; break;
                case GE.Stat.Dex: dex += value; break;
                case GE.Stat.BlockChance: blockChance += value; break;
                case GE.Stat.FireResist:
                case GE.Stat.IceResist:
                case GE.Stat.LightningResist:
                case GE.Stat.PoisonResist:
                    resistSum += value;
                    break;
                case GE.Stat.Hp: hp += value; break;
                case GE.Stat.HpGeneration: hpGeneration += value; break;
            }
        }
    }
}
