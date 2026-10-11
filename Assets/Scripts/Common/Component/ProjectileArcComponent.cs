using UnityEngine;

namespace ARPG.Component
{
    /// <summary>
    /// 목표 지점까지 포물선으로 날아가 내려앉는 발사체의 비행 데이터입니다.
    /// 비행 중에는 충돌하지 않고, 착지하면 System_Projectile이 LandingAreaEffectId 장판을 깔고 발사체를 제거합니다.
    /// </summary>
    public struct ProjectileArcComponent
    {
        public Vector2 StartPosition;       // 지면 기준 출발 위치
        public Vector2 EndPosition;         // 지면 기준 착지 위치
        public float FlightDuration;        // 비행 시간 (초) = 거리 / ProjectileTable.Speed
        public float Elapsed;               // 경과 시간
        public float StartHeight;           // 출발 높이 (시전자 몸통 중심). 착지할 때까지 0으로 줄어든다
        public float ArcHeight;             // 포물선 최대 추가 높이 (SkillTable.ArcHeight)
        public float Height;                // 현재 표시 높이 (System_Render가 Visual에 적용)
        public int LandingAreaEffectId;     // 착지 시 깔 AreaEffectTable Id
        public int SkillId;                 // 장판에 넘길 스킬 Id
        public Faction CasterFaction;       // 발사 시점 시전자 진영 (비행 중 시전자가 죽어도 장판 진영 유지)
    }
}
