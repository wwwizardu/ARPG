namespace ARPG.Component
{
    /// <summary>
    /// 동행 추종 컴포넌트 (NPC_COMBAT_DESIGN §4.2). 이 컴포넌트가 있으면 NPC의 기본 AI 상태가 Follow가 된다.
    /// </summary>
    public struct NpcCompanionComponent
    {
        public int LeaderEntityId;      // 따라갈 대상 (플레이어)
        public float PathFailedSince;   // 길찾기 연속 실패 시작 시각. 0이면 실패 중 아님 (순간이동 판정용)
    }
}
