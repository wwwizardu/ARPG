#nullable enable
using UnityEngine;

namespace ARPG.Component
{
    /// <summary>
    /// 마을의 라이프사이클 상태(Stage, Bounds, ThreatLevel)를 담는 ECS 컴포넌트.
    /// VillageManager가 마을 엔티티에 1:1로 부착.
    /// VillageStorageComponent와 별도 — 자원은 Storage, 상태/경계는 이 컴포넌트.
    /// 세이브 정본은 VillageData (Phase B 결정과 일관).
    /// </summary>
    public struct VillageComponent
    {
        public int VillageId;
        public VillageStage Stage;
        public RectInt Bounds;            // 마을 경계 (Tier 승격 시 확장)
        public float ThreatLevel;         // 0.0~1.0, Phase F에서 본격 사용 (Phase C는 0 고정)
        public int WallSegmentCount;      // Stage 3+ 통계
        public int CompletedWallSegments;

        // (deprecated) BUILD_PRIORITY_DESIGN.md에서 평가기 인라인화로 이 게이트 폐기.
        // 필드는 세이브 호환을 위해 유지 — 다음 메이저 마이그레이션 시 제거.
        public float LastNeedsEvalGameHour;
    }
}
