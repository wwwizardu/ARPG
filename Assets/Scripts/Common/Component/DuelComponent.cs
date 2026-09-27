namespace ARPG.Component
{
    /// <summary>
    /// 결투 참가 컴포넌트 (NPC_COMBAT_DESIGN §3.3·§7). 결투 중인 두 참가자(플레이어·NPC)에게 같이 붙는다.
    /// 적대 판정(FactionHelper.IsEntityHostile)은 이 컴포넌트로 "결투 쌍이면 적대"를 진영보다 우선해 판정한다.
    /// 저장하지 않는다 — 결투는 저장·재접속·던전 전환을 넘어 이어지지 않는다(전환 시 무승부 종료).
    /// </summary>
    public struct DuelComponent
    {
        public int OpponentId;          // 결투 상대 EntityId
        public float ElapsedSeconds;    // 진행 시간(초). 게임이 멈춘(GameplayPaused) 동안은 System_Duel이 돌지 않아 세지 않는다 [D27]
        public int PendingLoserId;      // HP 0으로 패배가 확정된 참가자. -1이면 미확정. System_Duel이 다음 틱에 종료 처리한다
        public DuelGrievance Grievance; // 시작 시 AMBITION이 판정해 넘긴 명분 (B4 스텁: 항상 None)
    }

    /// <summary>결투 명분 등급 (NPC_AMBITION_DESIGN §8.8). B4에서는 항상 None(스텁).</summary>
    public enum DuelGrievance : byte
    {
        None = 0,   // 명분 없음
        Weak = 1,   // 약한 명분 (소문으로만 안다)
        Strong = 2, // 명분 있음 (플레이어에게 직접 잘못한 기록)
    }
}
