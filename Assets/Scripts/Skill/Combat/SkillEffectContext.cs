#nullable enable
using UnityEngine;

namespace ARPG.Skill.Combat
{
    /// <summary>
    /// SkillEffect 실행 시점에 디스패처가 참조하는 컨텍스트.
    /// Trigger에 따라 채워지는 필드가 다르며, 효과(EffectType)가 자기 트리거에 맞는 필드만 사용한다.
    /// ref로 전달되어 OnSkillCommand에서 CancelOriginalCast를 세팅할 수 있다.
    /// </summary>
    public struct SkillEffectContext
    {
        // 공통
        public int SkillEntityId;       // 발동된 스킬 엔티티 ID
        public int SkillId;             // SkillTable ID (편의용)
        public int OwnerEntityId;       // 시전자 엔티티 ID (caster)

        // OnHit / OnCrit / OnKill
        public int TargetEntityId;      // 피격 대상
        public DamageResult DamageResult;

        // OnSkillCommand / OnSkillStart 등
        public Vector2 TargetPosition;  // 시전 시 지정된 위치 (또는 타겟 위치)

        // OnProjectileSpawn / OnProjectileHit
        public int ProjectileEntityId;

        // OnSkillCommand 전용 - 디스패처가 true로 세팅하면 원래 시전을 캔슬
        public bool CancelOriginalCast;
    }
}
