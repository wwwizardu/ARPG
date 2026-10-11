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

        // OnHit propagation uses this as the forward axis for its first child search.
        // Zero preserves unrestricted behavior when the hit has no meaningful direction.
        public Vector2 ImpactDirection;

        // OnHit 전용 - 판정 범위 중심. 넉백·당기기의 기준점 (SKILL_DESIGN §6.10).
        // ImpactDirection은 전파 탐색 축이라 이 용도로 덮어쓰지 않는다.
        public HitOriginKind HitOriginKind;
        public Vector2 HitOrigin;           // 발 위치 기준. HitOriginKind가 None이면 사용하지 않음

        // SkillPropagation root shared by a propagated child delivery. 0 means the hit
        // is an original delivery and may start an OnHit propagation root.
        public int PropagationRootEntityId;

        // OnSkillCommit 전용 - 전파 EffectAction이 true로 세팅하면 Projectile/Area/직접
        // 기본 전달을 생성하지 않는다.
        public bool SuppressDefaultImpact;

        // OnSkillCommand 전용 - 디스패처가 true로 세팅하면 원래 시전을 캔슬
        public bool CancelOriginalCast;
    }

    /// <summary>
    /// 타격 판정 범위의 중심이 무엇인지. 넉백은 이 중심에서 바깥으로, 당기기는 중심 쪽으로 움직인다 (SKILL_DESIGN §6.10).
    /// </summary>
    public enum HitOriginKind : byte
    {
        None,   // 범위 중심 없음 (발사체·연쇄). 밀기는 날아온 방향, 당기기는 공격자 쪽
        Owner,  // 공격자 몸 (Direction형 직접 공격). 당기기는 두 몸이 닿는 거리에서 멈춤
        Point,  // 지정 지점·장판 중심. 당기기는 그 지점에서 멈춤
    }
}
