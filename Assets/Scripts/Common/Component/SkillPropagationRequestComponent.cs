using UnityEngine;
using GE = GlobalEnum;

namespace ARPG.Component
{
    /// <summary>
    /// Delayed work item that selects and creates the next propagation deliveries.
    /// 대기 중인 주문의 위치는 같은 엔티티의 TransformComponent가 유일한 출처다.
    /// IsFollowingSourceTarget이면 System_SkillPropagation이 대기 동안 source 대상의 현재 위치로 갱신하고,
    /// 대상이 제거(예약)되면 추적을 끊고 마지막 위치에 남는다.
    /// </summary>
    public struct SkillPropagationRequestComponent
    {
        public int RootEntityId;
        public int SourceTargetEntityId;
        public bool IsFollowingSourceTarget;
        public int RemainingDepth;
        public int Generation;
        public float RemainingDelay;
        public float SearchRadius;
        public GE.SkillDeliveryType DeliveryType;
        public Vector2 IncomingDirection;
    }
}
