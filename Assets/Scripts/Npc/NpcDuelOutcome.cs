#nullable enable
using System.Collections.Generic;
using ARPG.Component;

namespace ARPG.Npc
{
    /// <summary>결투 종료 사유 (NPC_COMBAT_DESIGN §7.2).</summary>
    public enum DuelEndReason : byte
    {
        Defeat = 0,             // 한쪽 HP 0 → HP 1로 두고 종료 [D22]
        Timeout = 1,            // 실제 시간 3분 경과 → 무승부 [D27]
        ParticipantLost = 2,    // 참가자 엔티티 소멸(청크 이탈 비실체화 등) → 무승부 (기술적 정리)
    }

    /// <summary>
    /// 결투 결과 처리 훅(§7.4)에 넘기는 정보. AMBITION 쪽 규칙(명분·관계 파장·목격 소문)이 이 값을 받는다.
    /// B4에서는 NpcManager.ApplyDuelOutcome이 Deeds 기록만 한다(Q12). C2에서 AMBITION 규칙으로 교체한다.
    /// </summary>
    public sealed class NpcDuelOutcome
    {
        public int PlayerId = -1;               // 참가자 1 (플레이어)
        public int NpcId = -1;                  // 참가자 2 (NPC)
        public int WinnerId = -1;               // 무승부면 -1
        public int LoserId = -1;                // HP 0이 된 쪽. 무승부면 -1
        public DuelEndReason Reason;
        public DuelGrievance Grievance;         // 시작 시 판정된 명분 (B4 스텁: None)
        public float EndedGameTime;
        public readonly List<int> Witnesses = new();   // 주변 NPC (참가자 제외)

        public bool IsDraw => WinnerId < 0;
    }
}
