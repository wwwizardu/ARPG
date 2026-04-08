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
    }
}
