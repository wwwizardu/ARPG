using UnityEngine;

namespace ARPG.Component
{
    /// <summary>
    /// Snapshot-based immediate delivery between two selected propagation points.
    /// Generation/RemainingDepth는 같은 엔티티의 SkillPropagationBranchComponent가 유일한 출처이고,
    /// 대상 위치는 처리 시점에 재조회한다 (생성 시점 스냅샷은 대상이 움직였을 수 있어 보관하지 않음).
    /// </summary>
    public struct InstantSkillDeliveryComponent
    {
        public int RootEntityId;
        public int TargetEntityId;
        public Vector2 SourcePosition;
    }
}
