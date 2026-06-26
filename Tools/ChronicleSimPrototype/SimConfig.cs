namespace ChronicleSim;

/// <summary>
/// 시뮬 튜닝 노브 전부 한 곳에. 값 바꾸고 `dotnet run` 하면 즉시 반영.
/// 설계 §11.1 Tier 2(정복-병합 + 과확장 페널티) 모델.
/// </summary>
public static class SimConfig
{
    // ── 기간/공간 ──
    public static int SimYears = 150;          // §11-1 가설값
    public static int NeighborRegionDist = 2;  // region 유클리드 거리 ≤ 2 → 이웃 (실측: 거리3은 ~27이웃 과밀)

    // ── 호감도 임계 (event 분기) ──
    public static int AllianceThreshold = 45;
    public static int TradeThreshold = 8;
    public static int SkirmishThreshold = -8;
    public static int WarThreshold = -28;
    public static int JoinThreshold = 28;      // 왕국 인접 + 우호 ≥ 이 값 → KingdomJoin (편입 촉진)

    // ── 호감도 변동량 ──
    public static int AffTrade = 4;
    public static int AffSkirmish = -5;
    public static int AffWar = -10;
    public static int AffAllianceTick = 2;
    public static int AffMin = -100, AffMax = 100;

    // ── event 발생 확률 (페어당 연 1회 roll) ──
    public static double PTrade = 0.25;
    public static double PSkirmish = 0.30;
    public static double PWar = 0.28;
    public static double PMerge = 0.05;        // 동맹 + 인구비 충족 시 base

    // ── 합병/정복 ──
    public static double MergeRatio = 4.0;     // 큰 쪽 인구 ≥ 4× → 자발적 합병 가능 (합병 억제)
    public static double MergeAbsorb = 0.5;    // 합병 시 작은 쪽 인구의 흡수 비율
    public static double ConquerRatio = 1.25;  // 전쟁 승자 인구 ≥ 1.25× 패자 → 정복(편입) (정복 활성화)
    public static double WarLoserLoss = 0.40;  // 패자 인구 손실률
    public static double WarWinnerLoss = 0.08; // 승자 인구 손실률
    public static int RuinPopFloor = 12;       // 전쟁 후 패자 인구 ≤ → 폐허

    // ── 왕국 ──
    public static int KingdomMinCluster = 5;   // 동맹 클러스터 ≥ → KingdomFound (형성 지연 → 소수 집중)

    // ── 과확장 페널티 (Tier 2 핵심 제동) ──
    // 확장(정복/합병/편입) 확률에 곱해지는 계수 = max(Min, 1 - Slope * members).
    // 왕국이 클수록 추가 확장이 어려워져 단일 제국 수렴을 막는다.
    public static double OverextensionSlope = 0.008;
    public static double OverextensionMin = 0.18;

    public static double Overextension(int kingdomMembers)
        => Math.Max(OverextensionMin, 1.0 - OverextensionSlope * kingdomMembers);
}
