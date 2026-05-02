using UnityEngine;

namespace ARPG.Component
{
    /// <summary>
    /// NPC가 배정받은 진행 중 건설 작업(ObjectPlacementTaskComponent) 정보.
    /// System_VillageBuildQueue가 Task 시작 시 부착, 완료 시 제거 (1:1 매칭).
    /// BuildStateHandler가 이 컴포넌트를 보고 BuildSitePosition으로 이동/작업.
    ///
    /// AccumulatedBuildHours 진행은 NPC가 Build 상태이고 현장 도달 상태일 때만 누적된다 — 위협
    /// 감지로 Flee/Chase 전이 시 자동으로 일시정지, 복귀 시 자동 재개.
    /// </summary>
    public struct NpcBuildAssignmentComponent
    {
        public int TaskEntityId;            // ObjectPlacementTaskComponent가 부착된 task entity
        public int VillageId;
        public Vector2 BuildSitePosition;   // 건설지 월드 좌표 (타일 중심)
    }
}
