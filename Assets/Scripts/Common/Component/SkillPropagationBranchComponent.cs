using UnityEngine;

namespace ARPG.Component
{
    /// <summary>
    /// Marks a concrete propagation delivery and keeps its generation state.
    /// </summary>
    public struct SkillPropagationBranchComponent
    {
        public int RootEntityId;
        public int RemainingDepth;
        public int Generation;
        public Vector2 TravelDirection;

        /// <summary>
        /// Projectile 전달이 겨냥한 대상. 선택 시점에 루트 VisitedTargets에 예약되므로,
        /// 충돌 검사에서 "방문한 대상은 건너뛰되 겨냥 대상은 맞힌다"를 구분하는 데 쓴다.
        /// Instant 전달은 InstantSkillDeliveryComponent.TargetEntityId를 쓰므로 0.
        /// </summary>
        public int AimedTargetEntityId;
    }
}
