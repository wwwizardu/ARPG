using UnityEngine;

namespace ARPG.Component
{
    /// <summary>몬스터 군집 이동의 일시 상태. 저장하지 않으며 희망 속도(VelocityComponent)는 변경하지 않는다.</summary>
    public struct MonsterCrowdComponent
    {
        public int MonsterTableId;
        public bool HasIntegrated;
        public int IntegratedFrame;
        public Vector2 ActualVelocity;
        public bool IsWaiting;
        public float RetryAt;
        public int AvoidSide;
        public float AvoidUntil;
    }
}
