using ARPG.Skill.Telegraph;
using UnityEngine;

namespace ARPG.Component
{
    /// <summary>
    /// 실행 중인 몬스터 공격 예고의 순수 런타임 데이터입니다.
    /// 렌더 오브젝트와 진행률 계산은 저장하지 않습니다.
    /// </summary>
    public struct AttackTelegraphComponent
    {
        public int OwnerEntityId;
        public AttackTelegraphShape Shape;
        public AttackTelegraphOriginMode OriginMode;

        public Vector2 SnapshotOrigin;
        public Vector2 TargetPosition;
        public Vector2 Direction;

        public float Range;
        public float HalfAngleDeg;
        public float Width;
        public int ProjectileCount;
        public float ProjectileSpreadAngleDeg;

        public float WarningStartFixedTime;
        public float NextCommitFixedTime;
        public int CommitIndex;
        public int CommitCount;
    }
}
