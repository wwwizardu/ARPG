using UnityEngine;

namespace ARPG.Component
{
    /// <summary>몬스터 군집 이동의 일시 상태. 저장하지 않으며 희망 속도(VelocityComponent)는 변경하지 않는다.</summary>
    public struct MonsterCrowdComponent
    {
        public int MonsterTableId;
        // 군집 이동으로 스스로 이동한 속도 (겹침 보정 제외). 다음 프레임 이웃이 이 개체의 이동을 예측하는 데 쓴다
        public Vector2 ActualVelocity;
        public bool IsWaiting;
        public float WaitStartTime;
        // 대기 중 정면 이동을 막은 몬스터 ID. 0 = 몬스터가 아니라 지형 때문에 정면이 막힌 대기 (A* stuck 재계산을 미루지 않는다)
        public int WaitBlockerId;
        public float RetryAt;
        public int AvoidSide;
        public float AvoidUntil;
        // 서로 막아 오래 기다린 상대(또는 움직일 수 없는 몬스터)를 통과하는 동안의 상대 ID와 끝 시각. 0 = 없음
        public int PassPartnerId;
        public float PassUntil;
    }
}
