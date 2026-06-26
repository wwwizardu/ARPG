using System;

namespace ARPG.History
{
    /// <summary>
    /// 정착지 연대기 시뮬(L1, Tier 2: 정복-병합 + 과확장 페널티) 튜닝 노브.
    /// 검증 프로토타입(Tools/ChronicleSimPrototype/SimConfig.cs)에서 포팅.
    /// 설계: PROCEDURAL_HISTORY_DESIGN.md §3.2~§4
    /// ★ 단조롭거나 단일 제국 수렴 시 이 값들을 튜닝한다 (덤프 보고).
    /// </summary>
    public static class SettlementSimConfig
    {
        // ── 기간/공간 ──
        public const int SimYears = 150;            // §11-1 가설값
        public const int NeighborRegionDist = 2;    // region 유클리드 거리 ≤ 2 → 이웃 (거리3은 과밀)

        // ── 호감도 임계 (event 분기) ──
        public const int AllianceThreshold = 45;
        public const int TradeThreshold = 8;
        public const int SkirmishThreshold = -8;
        public const int WarThreshold = -28;
        public const int JoinThreshold = 28;        // 왕국 인접 + 우호 ≥ → KingdomJoin

        // ── 호감도 변동량 ──
        public const int AffTrade = 4;
        public const int AffSkirmish = -5;
        public const int AffWar = -10;
        public const int AffAllianceTick = 2;
        public const int AffMin = -100, AffMax = 100;

        // ── event 발생 확률 (페어당 연 1회 roll) ──
        public const double PTrade = 0.25;
        public const double PSkirmish = 0.30;
        public const double PWar = 0.28;
        public const double PMerge = 0.05;          // 동맹 + 인구비 충족 시 base

        // ── 합병/정복 ──
        public const double MergeRatio = 4.0;       // 큰 쪽 인구 ≥ 4× → 자발적 합병 가능
        public const double MergeAbsorb = 0.5;      // 합병 시 작은 쪽 인구 흡수 비율
        public const double ConquerRatio = 1.25;    // 전쟁 승자 인구 ≥ 1.25× 패자 → 정복(편입)
        public const double WarLoserLoss = 0.40;    // 패자 인구 손실률
        public const double WarWinnerLoss = 0.08;   // 승자 인구 손실률
        public const int RuinPopFloor = 12;         // 전쟁 후 패자 인구 ≤ → 폐허

        // ── 왕국 ──
        public const int KingdomMinCluster = 5;     // 동맹 클러스터 ≥ → KingdomFound

        // ── 과확장 페널티 (Tier 2 핵심 제동) ──
        // 확장(정복/합병/편입) 확률에 곱해지는 계수 = max(Min, 1 - Slope * members).
        public const double OverextensionSlope = 0.008;
        public const double OverextensionMin = 0.18;

        public static double Overextension(int kingdomMembers)
            => Math.Max(OverextensionMin, 1.0 - OverextensionSlope * kingdomMembers);
    }
}
