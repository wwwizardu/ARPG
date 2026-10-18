using UnityEngine;

namespace ARPG.Component
{
    /// <summary>
    /// 돌진 스킬 엔티티의 경로 판정 상태 (SKILL_DESIGN §6.11). System_Skill만 읽고 쓴다.
    /// 지난 판정 이후 지나온 선분을 다음 판정에 쓰고, 같은 돌진에서 이미 맞은 대상은 다시 맞히지 않는다.
    /// </summary>
    public struct SkillDashHitComponent
    {
        public Vector2 LastSweepPosition;           // 마지막으로 판정한 돌진자 몸 중심 (발 위치 + HitOffset)
        public SkillPropagationTargetIds HitTargets; // 이번 돌진에서 이미 맞은 대상 (최대 16)
    }
}
