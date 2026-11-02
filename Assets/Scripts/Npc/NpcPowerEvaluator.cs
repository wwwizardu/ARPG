#nullable enable
using System.Collections.Generic;
using ARPG.Data;
using ARPG.Item;
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
        public float Defense;        // 방어력 + 회피 + 막기 확률 + 저항 요약
        public float Life;           // 최대 체력 + 체력 재생 요약
        public float EquipmentScore; // 공격·방어·생명력 가중 합
        public float SkillScore;     // SkillLevel 환산
        public float Total;          // NpcPower = EquipmentScore + SkillScore (용기 제외, D31)
    }

    /// <summary>
    /// NpcSaveData.InventoryEquip의 Mods만으로 NPC 전투력을 계산한다 (NPC_AMBITION_DESIGN §5.1, D31, D32).
    /// ECS 없이 동작하며, 실체화 때 EquipHelper.ApplyAllEquipmentModifiers가 쓰는 모드 해석 규칙을 그대로 따른다.
    /// - 무기 Base는 실제 공격 프로필과 같은 WeaponHelper.GetCombinedWeaponStats로만 소비한다. 무기 두 개면 쌍수 합성
    ///   (피해 (R + L) × 0.7, 공속·기본 치명타 평균, WEAPON_PROGRESSION_DESIGN §쌍수 계산 초기안)이다.
    /// - Implicit/Prefix/Postfix와 방어구 Base는 Passive 경로(FlatStat/Added*Damage/IncreasedStat)로 합산한다.
    /// - OnCalculate/OnEvent 모드(ModPool 경로)는 전투 중 조건부 효과라 점수에 넣지 않는다.
    /// - 회피 항은 (기본 민첩 + 장비 민첩) × 2 + 장비 회피를 다른 방어 항처럼 수치 그대로 가중치를 곱해 더한다
    ///   (PLAYER_LEVEL_CHARM_DESIGN §5.6, 2026-10-24 사용자 결정: 민첩 회피 포함). 몬스터 정확도와 비교하지 않는 절대값이라
    ///   사는 지역과 무관하고 이주해도 전투력이 바뀌지 않는다 (2026-11-01 사용자 결정).
    /// 상태를 갖지 않는 순수 계산이다. 캐시는 호출자(NpcManager)가 소유한다.
    /// </summary>
    public static class NpcPowerEvaluator
    {
        // 가중치는 생성 규칙 그대로의 평범한 NPC에서 공격 ≈ 50%, 방어 ≈ 25%, 생명력 ≈ 25%가 되도록 맞춘 값이다
        // (2026-09-27 세이브 생존 2,387명 평균: Attack 12.9, Defense 54.6, Life 65.3 → NpcPower 평균 약 26).
        // 기본 아이템 장비(NPC_SHOP_EQUIPMENT, 2026-10-03) 뒤 평균: Attack 6.06, Defense 21.2, Life 21.5 → NpcPower 약 10.75.
        // 회피 × 1.0(2026-11-01) 뒤 세이브 평균: Attack 5.06, Defense 30.57, Life 25.0 → NpcPower 약 11.22.
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
        // 회피 1당. 방어도와 같은 가중치다 (2026-11-01 사용자 결정: 최종 밸런스에서 같은 수치의 방어도와 회피가 같은 성능을 갖게 한다).
        private const float EvasionWeight = 1.0f;
        private const float BlockChanceWeight = 1.0f;
        private const float ResistWeight = 0.25f;
        private const float HpGenerationWeight = 5.0f;

        public static float Evaluate(NpcSaveData saveData)
        {
            return EvaluateBreakdown(saveData, saveData.InventoryEquip).Total;
        }

        /// <summary>이 NPC의 기본 민첩으로 주어진 장비 조합을 평가한다 (장비 교체 후보 비교용).</summary>
        public static NpcPowerBreakdown EvaluateBreakdown(NpcSaveData saveData, ItemData?[] equippedItems)
        {
            return EvaluateBreakdown(equippedItems, saveData.SkillLevel, ResolveBaseDex(saveData));
        }

        /// <summary>장비만 본 점수 (기본 민첩 0). 장비끼리 비교할 때 쓴다.</summary>
        public static NpcPowerBreakdown EvaluateBreakdown(ItemData?[] equippedItems, int skillLevel)
        {
            return EvaluateBreakdown(equippedItems, skillLevel, 0);
        }

        public static NpcPowerBreakdown EvaluateBreakdown(ItemData?[] equippedItems, int skillLevel, int baseDex)
        {
            NpcPowerBreakdown result = new NpcPowerBreakdown();
            if (equippedItems == null)
                equippedItems = System.Array.Empty<ItemData?>();

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

            // 실제 공격 프로필과 같은 합성 무기 수치 (무기 두 개면 쌍수 합성)
            CombinedWeaponStats weapon = WeaponHelper.GetCombinedWeaponStats(equippedItems);
            float weaponDamageAvg = weapon.Physics.Average + weapon.Fire.Average + weapon.Ice.Average +
                weapon.Lightning.Average + weapon.Poison.Average;
            float attackSpeed = weapon.AttackSpeed > 0f ? weapon.AttackSpeed : UnarmedAttackSpeed;
            float weaponCritRate = weapon.CriRate;

            float hitDamage = weaponDamageAvg + addedDamageAvg + (flatAttackMin + flatAttackMax) * 0.5f;
            attackSpeed *= 1f + attackSpeedIncreasePct / 100f;
            float critRate = Mathf.Clamp((weaponCritRate + critRateFlat) * (1f + critRateIncreasePct / 100f), 0f, 100f);
            float critMultiplier = BaseCritMultiplier + critDamagePct / 100f;
            float critFactor = 1f + critRate / 100f * (critMultiplier - 1f);

            result.Attack = hitDamage * attackSpeed * critFactor;
            float totalEvasion = evasion + AttributeBonusRules.GetBonusEvasion(Mathf.RoundToInt(dex));
            result.Defense = defense + totalEvasion * EvasionWeight + blockChance * BlockChanceWeight + resistSum * ResistWeight;
            result.Life = hp + hpGeneration * HpGenerationWeight;
            result.EquipmentScore = result.Attack * AttackWeight + result.Defense * DefenseWeight + result.Life * LifeWeight;
            result.SkillScore = Mathf.Max(0, skillLevel) * SkillScorePerLevel;
            result.Total = result.EquipmentScore + result.SkillScore;
            return result;
        }

        /// <summary>표의 기본 민첩. AR이 없으면 0으로 본다.</summary>
        private static int ResolveBaseDex(NpcSaveData saveData)
        {
            NpcTable? table = AR.s?.Data != null ? AR.s.Data.GetNpc(saveData.NpcTableId) : null;
            return table?.Stat != null ? table.Stat.Dex : 0;
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
