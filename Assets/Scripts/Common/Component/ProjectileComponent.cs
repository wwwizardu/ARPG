using UnityEngine;

namespace ARPG.Component
{
    public struct ProjectileComponent
    {
        public int OwnerEntityId;       // 발사한 엔티티
        public int SkillEntityId;       // 스킬 엔티티 ID (데미지 계산용)
        public int ProjectileTableId;   // 발사체 테이블 ID
        public float LifeTime;          // 최대 수명 (초)
        public float CurrentLifeTime;   // 경과 시간
        public float HitRadius;         // 충돌 반경
        public bool IsPiercing;         // 관통 여부
        public long VisualRequestToken; // 비동기 View 결과의 세대 검증 토큰

        // 위치는 System_Render가 속도로 옮기므로, System_Projectile은 지난 검사 위치부터 지금 위치까지의 선분으로 충돌을 검사한다.
        // 프레임이 끊겨 한 번에 멀리 건너뛰어도 그 사이의 대상을 지나치지 않는다.
        public Vector2 LastSweepPosition;

        // 관통 발사체가 맞힌 뒤 아직 겹쳐 있는 대상 (최대 16). 겹쳐 있는 동안은 다시 맞히지 않고,
        // 빠져나가면 목록에서 빠져 다시 들어오면 또 맞는다. 관통하지 않는 발사체는 첫 적중에 사라지므로 쓰지 않는다.
        public SkillPropagationTargetIds OverlappingHitTargets;
    }
}
