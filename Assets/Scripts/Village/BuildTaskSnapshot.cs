#nullable enable
using System;

namespace ARPG.Village
{
    /// <summary>
    /// 진행 중인 빌드 task의 영구 저장 스냅샷.
    /// BUILD_PRIORITY_DESIGN.md Step C — N개 동시 task 세이브 지원.
    ///
    /// 런타임에는 ObjectPlacementTaskComponent가 task entity에 부착됨.
    /// 세이브 시 task 풀 → 마을별 List&lt;BuildTaskSnapshot&gt;로 미러링.
    /// 로드 시 각 스냅샷마다 task entity를 발급한다 (VillageManager.RestoreTaskFromData).
    /// BuildDurationHours는 BuildableItemTable.BuildHours에서 직접 조회하므로 저장 안 함.
    /// </summary>
    [Serializable]
    public class BuildTaskSnapshot
    {
        public int TableId;
        public float StartedAt;
        public float AccumulatedHours;       // NPC가 실제 작업한 누적 시간 — 완료 판정 정본
        public int TileX;
        public int TileY;
        public int ReservedWood;
        public int ReservedStone;
        public int AssignedNpcEntityId = -1; // Task에 배정됐던 NPC EntityId — 로드 시 재부착용
        public int BuildingEntityId = -1;    // 시작 시 생성된 건설중 빌딩 엔티티 — 로드 시 진행도 동기화에 사용
    }
}
