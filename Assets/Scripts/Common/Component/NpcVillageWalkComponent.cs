using UnityEngine;

namespace ARPG.Component
{
    /// <summary>마을 안 걷기의 목적 (NPC_SIMULATION_DESIGN §3.1 D13·D14).</summary>
    public enum NpcVillageWalkKind : byte
    {
        Departure,  // 원정·이주 출발: 화면 밖 지점으로 걷다가 화면을 벗어나면 강등
        Return,     // 원정·이주 귀환: 화면 바로 밖에서 실체화해 귀환점까지 걸은 뒤 기본 AI
        ToInn,      // 원정 부상: 여관 문까지 걸어가 닿으면(또는 화면을 벗어나면) 강등
    }

    /// <summary>
    /// 마을 안 걷기 진행 컴포넌트 (NPC_SIMULATION_DESIGN §3.1). 원정 출발·귀환과 부상자의 여관 가기를 보여 주는 동안만 붙는다.
    /// 이 컴포넌트가 있는 동안 System_AI_Behavior는 AI 상태 핸들러를 건너뛰고, System_NpcVillageWalk가 이동을 소유한다.
    /// 저장하지 않는다 — 강등되면 엔티티와 함께 사라지고, 다시 실체화될 때 NpcManager가 D13·D14 규칙으로 다시 정한다.
    /// </summary>
    public struct NpcVillageWalkComponent
    {
        public NpcVillageWalkKind Kind;
        public Vector2 Target;              // 지금 걸어가는 지점 (화면 밖 지점·여관 문·귀환점)
        public Vector2 Heading;             // 방향 기준. 출발 = 원정 목표, 여관 = 여관 문(배치 전이면 마을 중심), 귀환 = 귀환점
        public float ElapsedSeconds;        // 현재 구간 경과 시간 (제한 시간 초과 시 실패 처리)
        public float PathFailedSeconds;     // 길찾기 연속 실패 시간 (일정 시간 넘으면 실패 처리)
    }
}
