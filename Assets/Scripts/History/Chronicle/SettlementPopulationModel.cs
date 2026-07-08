using System;

namespace ARPG.History
{
    /// <summary>
    /// 런타임 추상 정착지의 인구 성장 모델 (무상태 순수 로직).
    /// New Game 딥타임 시뮬(<see cref="SettlementChronicleSimulator"/>.GrowthPhase)과 **같은 로지스틱 모델**
    /// (dP/dt = r·P·(1 − P/carry), 같은 <see cref="BiomeData"/> 파라미터)을 이어받아 굴린다 → 같은 세계관의 연속.
    ///
    /// 핵심: <see cref="Advance"/>는 경과 일수 days에 대해 O(1) — 하루씩(또는 해마다) 루프하지 않는다.
    /// 연속 로지스틱의 닫힌형 해를 평가하므로, 플레이어가 오래 자리를 비워 days가 커도
    /// (수면/시간점프 catch-up) 한 번의 Exp 연산으로 처리되어 프레임 스파이크가 없다.
    /// PROCEDURAL_HISTORY_DESIGN.md §0.
    /// </summary>
    public static class SettlementPopulationModel
    {
        // 딥타임 시뮬 성장률(연 단위)을 런타임 게임-일로 환산하는 계수 — "게임 N일 = 시뮬 1년".
        // 튜닝 지점: 값이 클수록 런타임 인구 변동이 느려진다.
        public const float GameDaysPerSimYear = 30f;

        /// <summary>
        /// 정착지 인구를 게임 days일만큼 전진시킨다(제자리 변경). days ≤ 0이면 no-op.
        /// Ruined(폐허)는 0 고정. 성장은 로지스틱 S자로 carry에 수렴한다.
        ///
        /// 딥타임 연대기의 연 단위 로지스틱(dP/dt = r·P·(1 − P/carry))과 동일 모델을,
        /// 연속 로지스틱의 닫힌형 해로 평가한다(days에 무관한 O(1)).
        ///   연대기 discrete 스텝의 +1.0(이민 하한)·Noise()는 런타임에선 제외 — 추상 층은 이미 규모가 있는
        ///   정착지를 "이어서" 굴리는 역할이라 0→부트스트랩이 불필요하고, 결정론이 세이브/틱 안정성에 유리.
        /// </summary>
        public static void Advance(ref SettlementLiveState s, int days)
        {
            if (days <= 0)
                return;

            if (s.Fate == SettlementFate.Ruined)
            {
                s.Pop = 0f;
                return;
            }

            // 로지스틱은 자기촉매(성장 ∝ 현재 인구) — Pop ≤ 0이면 성장 없음(0은 불안정 평형).
            // 연대기의 +1.0 하한을 런타임에선 쓰지 않으므로 0에서의 재생성은 별도 규칙(후속).
            if (s.Pop <= 0f)
                return;

            double rate = BiomeData.GrowthRate(s.Biome);
            double carry = BiomeData.Carry(s.Biome);

            // 게임일 → 시뮬년(연대기가 연 단위이므로 동일 단위로 환산). 소수 허용.
            double t = days / (double)GameDaysPerSimYear;

            // 연속 로지스틱 닫힌형 해: P(t) = carry / (1 + A·e^(−rate·t)),  A = (carry − P₀)/P₀.
            // days에 무관한 닫힌형 → O(1). P₀<carry면 S자로 증가, P₀>carry면 감소(둘 다 carry로 수렴).
            double a = (carry - s.Pop) / s.Pop;
            double newPop = carry / (1.0 + a * Math.Exp(-rate * t));

            if (newPop < 0.0)
                newPop = 0.0;

            s.Pop = (float)newPop;
        }
    }
}
