using System.Collections.Generic;
using ARPG.Component;
using ARPG.Message;
using ARPG.Utility;
using UnityEngine;
using GE = GlobalEnum;

namespace ARPG.Systems
{
    /// <summary>
    /// 스탯 재계산 시스템
    /// StatDirtyTag가 있는 엔티티의 스탯을 재계산합니다.
    ///
    /// 실행 흐름:
    /// 1. StatDirtyTag가 있는 엔티티 탐색
    /// 2. Base 스탯을 Final로 복사
    /// 3. StatModifierBuffer의 모든 보정을 우선순위대로 적용
    /// 4. Tag 제거
    /// </summary>
    public partial struct System_StatCalculation : IUpdateSystem
    {
        public int Priority => 50; // 이동/입력 시스템보다 먼저 실행

        public void OnCreate()
        {
            Debug.Log("System_StatCalculation Created");
        }

        public void OnReset()
        {
            Debug.Log("System_StatCalculation Reset called");
        }

        public void OnUpdate(float inDeltaTime)
        {
            // StatDirtyTag가 있는 엔티티만 처리
            SparseSet<StatDirtyTag> dirtyPool = AR.s.Component.GetComponentPool<StatDirtyTag>();
            if (dirtyPool == null || dirtyPool.Count == 0)
                return;

            // Dirty 엔티티 순회
            for (int i = 0; i < dirtyPool.Count; i++)
            {
                int entityId = dirtyPool.GetEntityId(i);

                // StatComponent가 없으면 스킵
                if (!AR.s.Component.TryGetComponent<StatComponent>(entityId, out StatComponent stat))
                {
                    AR.s.Component.RemoveComponent<StatDirtyTag>(entityId);
                    continue;
                }

                // 1단계: Base를 Final로 복사 (초기화)
                stat.CopyBaseToFinal();

                // 2단계: StatModifierHelper에서 모든 보정 가져오기
                List<StatModifier>? modifiers = StatModifierHelper.GetModifiers(entityId);
                if (modifiers == null || modifiers.Count == 0)
                {
                    // modifier가 없으면 Base = Final 그대로 저장
                    AR.s.Component.SetComponent(entityId, stat);
                    AR.s.Component.RemoveComponent<StatDirtyTag>(entityId);
                    if (entityId == AR.s.Data.CurrentPlayerEntityId)
                    {
                        AR.s.Message.Broadcast(new StatRecalculatedMessage { EntityId = entityId });
                    }
                    continue;
                }

                // 3단계: 우선순위 순으로 정렬 (낮은 숫자가 먼저)
                modifiers.Sort((a, b) => a.Priority.CompareTo(b.Priority));

                // 4단계: Add 타입 먼저 적용
                for (int j = 0; j < modifiers.Count; j++)
                {
                    StatModifier modifier = modifiers[j];
                    if (modifier.ModifierType == StatModifierType.Add)
                    {
                        ApplyModifier(ref stat, modifier);
                    }
                }

                // 5단계: Multiply 타입 적용
                for (int j = 0; j < modifiers.Count; j++)
                {
                    StatModifier modifier = modifiers[j];
                    if (modifier.ModifierType == StatModifierType.Multiply)
                    {
                        ApplyModifier(ref stat, modifier);
                    }
                }

                // 6단계: Override 타입 마지막 적용
                for (int j = 0; j < modifiers.Count; j++)
                {
                    StatModifier modifier = modifiers[j];
                    if (modifier.ModifierType == StatModifierType.Override)
                    {
                        ApplyModifier(ref stat, modifier);
                    }
                }

                // 7단계: 업데이트된 스탯 저장
                AR.s.Component.SetComponent(entityId, stat);

                // 8단계: Dirty 태그 제거
                AR.s.Component.RemoveComponent<StatDirtyTag>(entityId);

                // 9단계: 플레이어 스탯 재계산 완료 Broadcast
                if (entityId == AR.s.Data.CurrentPlayerEntityId)
                {
                    AR.s.Message.Broadcast(new StatRecalculatedMessage { EntityId = entityId });
                }
            }
        }

        /// <summary>
        /// 개별 modifier를 스탯에 적용
        /// </summary>
        private void ApplyModifier(ref StatComponent stat, StatModifier modifier)
        {
            switch (modifier.StatType)
            {
                case GE.Stat.Str:
                    stat.FinalStr = ApplyValue(stat.FinalStr, modifier);
                    break;
                case GE.Stat.Dex:
                    stat.FinalDex = ApplyValue(stat.FinalDex, modifier);
                    break;
                case GE.Stat.Int:
                    stat.FinalInt = ApplyValue(stat.FinalInt, modifier);
                    break;
                case GE.Stat.Hp:
                    stat.FinalMaxHp = ApplyValue(stat.FinalMaxHp, modifier);
                    break;
                case GE.Stat.Mp:
                    stat.FinalMaxMp = ApplyValue(stat.FinalMaxMp, modifier);
                    break;
                case GE.Stat.HpGeneration:
                    stat.FinalHpGeneration = ApplyValue(stat.FinalHpGeneration, modifier);
                    break;
                case GE.Stat.MpGeneration:
                    stat.FinalMpGeneration = ApplyValue(stat.FinalMpGeneration, modifier);
                    break;
                case GE.Stat.AttackMin:
                    stat.FinalAttackMin = ApplyValue(stat.FinalAttackMin, modifier);
                    break;
                case GE.Stat.AttackMax:
                    stat.FinalAttackMax = ApplyValue(stat.FinalAttackMax, modifier);
                    break;
                case GE.Stat.CriRate:
                    stat.FinalCriRate = ApplyValue(stat.FinalCriRate, modifier);
                    break;
                case GE.Stat.CriDamageMul:
                    stat.FinalCriDamage = ApplyValue(stat.FinalCriDamage, modifier);
                    break;
                case GE.Stat.MoveSpeed:
                case GE.Stat.MoveSpeedMul:
                    stat.FinalMoveSpeed = ApplyValue(stat.FinalMoveSpeed, modifier);
                    break;
                case GE.Stat.AttackSpeed:
                case GE.Stat.AttackSpeedMul:
                    stat.FinalAttackSpeed = ApplyValue(stat.FinalAttackSpeed, modifier);
                    break;
                case GE.Stat.CastSpeed:
                case GE.Stat.CastSpeedMul:
                    stat.FinalCastSpeed = ApplyValue(stat.FinalCastSpeed, modifier);
                    break;
                case GE.Stat.Defense:
                    stat.FinalDefense = ApplyValue(stat.FinalDefense, modifier);
                    break;
                case GE.Stat.FireResist:
                    stat.FinalFireResist = ApplyValue(stat.FinalFireResist, modifier);
                    break;
                case GE.Stat.IceResist:
                    stat.FinalIceResist = ApplyValue(stat.FinalIceResist, modifier);
                    break;
                case GE.Stat.LightningResist:
                    stat.FinalLightningResist = ApplyValue(stat.FinalLightningResist, modifier);
                    break;
                case GE.Stat.PoisonResist:
                    stat.FinalPoisonResist = ApplyValue(stat.FinalPoisonResist, modifier);
                    break;
                case GE.Stat.Luck:
                    stat.FinalLuck = ApplyValue(stat.FinalLuck, modifier);
                    break;
                case GE.Stat.BloodingRate:
                    stat.FinalBloodingRate = ApplyValue(stat.FinalBloodingRate, modifier);
                    break;
                case GE.Stat.IgniteRate:
                    stat.FinalIgniteRate = ApplyValue(stat.FinalIgniteRate, modifier);
                    break;
                case GE.Stat.FireAttackMin:
                    stat.FinalFireAttackMin = ApplyValue(stat.FinalFireAttackMin, modifier);
                    break;
                case GE.Stat.FireAttackMax:
                    stat.FinalFireAttackMax = ApplyValue(stat.FinalFireAttackMax, modifier);
                    break;
                case GE.Stat.IceAttackMin:
                    stat.FinalIceAttackMin = ApplyValue(stat.FinalIceAttackMin, modifier);
                    break;
                case GE.Stat.IceAttackMax:
                    stat.FinalIceAttackMax = ApplyValue(stat.FinalIceAttackMax, modifier);
                    break;
                case GE.Stat.LightningAttackMin:
                    stat.FinalLightningAttackMin = ApplyValue(stat.FinalLightningAttackMin, modifier);
                    break;
                case GE.Stat.LightningAttackMax:
                    stat.FinalLightningAttackMax = ApplyValue(stat.FinalLightningAttackMax, modifier);
                    break;
                case GE.Stat.PoisonAttackMin:
                    stat.FinalPoisonAttackMin = ApplyValue(stat.FinalPoisonAttackMin, modifier);
                    break;
                case GE.Stat.PoisonAttackMax:
                    stat.FinalPoisonAttackMax = ApplyValue(stat.FinalPoisonAttackMax, modifier);
                    break;
            }
        }

        /// <summary>
        /// modifier 타입에 따라 값 계산
        /// </summary>
        private readonly int ApplyValue(int currentValue, StatModifier modifier)
        {
            switch (modifier.ModifierType)
            {
                case StatModifierType.Add:
                    return currentValue + modifier.Value;

                case StatModifierType.Multiply:
                    // Value가 백분율(%)로 저장되어 있다고 가정
                    // 예: 50% 증가 = Value = 50
                    return currentValue + (currentValue * modifier.Value / 100);

                case StatModifierType.Override:
                    return modifier.Value;

                default:
                    return currentValue;
            }
        }
    }
}
