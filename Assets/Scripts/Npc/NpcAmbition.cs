#nullable enable
using ARPG.Component;

namespace ARPG.Npc
{
    /// <summary>
    /// NPC가 강해지려는 이유 (NPC_AMBITION_DESIGN §4.1). 성격에서 결정론적으로 도출하며 저장하지 않는다.
    /// </summary>
    public enum NpcAmbitionKind
    {
        None = 0,
        Explorer = 1,      // 탐험 — 호기심↑ 용기↑
        Glory = 2,         // 명예 — 자존심↑ 용기↑
        Wealth = 3,        // 부 — 탐욕↑ 인내↑
        Comrade = 4,       // 동료 — 충성↑ 친화↑
        Opportunist = 5,   // 기회 — 탐욕↑ 정직↓
        Cautious = 6,      // 안전 — 용기↓ 인내↑
    }

    /// <summary>주 야망(최고점)과 보조 야망(차점).</summary>
    public struct NpcAmbitionProfile
    {
        public NpcAmbitionKind Primary;
        public NpcAmbitionKind Secondary;
    }

    /// <summary>
    /// 성격 8수치 → 야망 도출 (§4.2). 원형마다 주 성격 두 수치의 선형 점수(0~200)를 매겨
    /// 최고점을 주 야망, 차점을 보조 야망으로 둔다. 동점이면 enum 순서가 앞선 쪽 — 결정론.
    /// 성격이 바뀌면 다음 조회부터 야망도 따라 바뀐다. 상태를 갖지 않는 순수 계산이다.
    /// </summary>
    public static class NpcAmbitionEvaluator
    {
        private const int FirstAmbition = (int)NpcAmbitionKind.Explorer;
        private const int LastAmbition = (int)NpcAmbitionKind.Cautious;

        public static NpcAmbitionProfile Evaluate(in NpcStatComponent stat)
        {
            NpcAmbitionKind primary = NpcAmbitionKind.None;
            NpcAmbitionKind secondary = NpcAmbitionKind.None;
            int primaryScore = int.MinValue;
            int secondaryScore = int.MinValue;
            for (int i = FirstAmbition; i <= LastAmbition; i++)
            {
                NpcAmbitionKind kind = (NpcAmbitionKind)i;
                int score = GetScore(kind, stat);
                if (score > primaryScore)
                {
                    secondary = primary;
                    secondaryScore = primaryScore;
                    primary = kind;
                    primaryScore = score;
                }
                else if (score > secondaryScore)
                {
                    secondary = kind;
                    secondaryScore = score;
                }
            }

            return new NpcAmbitionProfile { Primary = primary, Secondary = secondary };
        }

        /// <summary>§4.1 주 성격 표의 선형 점수. 예: Opportunist = Greed + (100 − Honesty).</summary>
        public static int GetScore(NpcAmbitionKind kind, in NpcStatComponent stat)
        {
            return kind switch
            {
                NpcAmbitionKind.Explorer => stat.Curiosity + stat.Courage,
                NpcAmbitionKind.Glory => stat.Pride + stat.Courage,
                NpcAmbitionKind.Wealth => stat.Greed + stat.Patience,
                NpcAmbitionKind.Comrade => stat.Loyalty + stat.Friendliness,
                NpcAmbitionKind.Opportunist => stat.Greed + (100 - stat.Honesty),
                NpcAmbitionKind.Cautious => (100 - stat.Courage) + stat.Patience,
                _ => 0,
            };
        }
    }
}
