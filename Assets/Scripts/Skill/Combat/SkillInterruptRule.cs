#nullable enable
using ARPG.Component;

namespace ARPG.Skill.Combat
{
    /// <summary>
    /// 시전자 쪽 사유로 진행 중인 스킬을 더 이상 유지할 수 없는지 판정하는 단일 정본.
    /// System_Skill(커밋 취소)과 System_AttackTelegraph(예고 제거)가 같은 규칙을 공유해야 하므로
    /// 두 시스템에 조건을 복제하지 않고 이 클래스만 사용한다.
    /// </summary>
    public static class SkillInterruptRule
    {
        /// <summary>
        /// 중단 사유를 평가한다. None이면 계속 진행 가능.
        /// </summary>
        public static SkillInterruptReason Evaluate(ComponentManager inComponentManager, int inOwnerEntityId)
        {
            if (inComponentManager.HasComponent<DestroyTag>(inOwnerEntityId) == true)
                return SkillInterruptReason.OwnerDestroyed;

            // Stunned 이상의 상태는 Input도 영향을 주지 못하므로 스킬도 유지하지 않는다.
            if (inComponentManager.TryGetComponent<StateComponent>(inOwnerEntityId, out StateComponent ownerState) == true
                && Creature.CharacterConditions.Stunned <= ownerState.Condition)
            {
                return SkillInterruptReason.OwnerCondition;
            }

            if (inComponentManager.TryGetComponent<StatComponent>(inOwnerEntityId, out StatComponent ownerStat) == true
                && ownerStat.CurrentHp <= 0f)
            {
                return SkillInterruptReason.OwnerDead;
            }

            // 아래는 몬스터 생명주기 전용 규칙 (Warm 청크 전환, 거리 비활성화)
            if (inComponentManager.HasComponent<MonsterTag>(inOwnerEntityId) == false)
                return SkillInterruptReason.None;

            if (inComponentManager.TryGetComponent<ActivationDistanceComponent>(inOwnerEntityId, out ActivationDistanceComponent activation) == true
                && activation.IsActivated == false)
            {
                return SkillInterruptReason.OwnerDeactivated;
            }

            if (AR.s.Monster != null
                && AR.s.Monster.IsMonsterSourceChunkActive(inOwnerEntityId) == false)
            {
                return SkillInterruptReason.OwnerChunkInactive;
            }

            return SkillInterruptReason.None;
        }
    }

    /// <summary>
    /// 스킬 중단 사유. 로그와 분기에서 사유를 구분하기 위해 bool 대신 사용한다.
    /// </summary>
    public enum SkillInterruptReason : byte
    {
        None,
        OwnerDestroyed,
        OwnerCondition,
        OwnerDead,
        OwnerDeactivated,
        OwnerChunkInactive,
    }
}
