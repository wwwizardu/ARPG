namespace ChronicleSim;

// Unity GlobalEnum.RaceType / BiomeType 미러 (값 동일)
public enum Race { Human = 0, Elf = 1, Dwarf = 2, Orc = 3, Nomad = 4, Lizardman = 5, Yeti = 6 }

public enum Biome { Plains = 0, Forest = 1, Desert = 2, Swamp = 3, Steppe = 4, Tundra = 5, Snow = 6, Mountain = 10, Wasteland = 30 }

/// <summary>
/// 종족 간 기본 성향 (= 설계의 RaceRelationTable v1: 코드 상수로 시작, §9-3 결정).
/// 대칭 행렬. 같은 종족 +40, 우호 종족 양수, 적대 종족 음수.
/// ★ 튜닝 지점: 이 값들이 누가 누구와 동맹/전쟁하는지를 결정한다.
/// </summary>
public static class RaceRelations
{
    private const int N = 7;

    // [a,b] 기본 호감도. 반드시 대칭으로 유지할 것.
    private static readonly int[,] Base =
    {
        //            Human  Elf  Dwarf  Orc  Nomad Lizard Yeti
        /*Human */ {   40,   12,   15,  -25,    5,  -15,  -18 },
        /*Elf   */ {   12,   40,   -8,  -30,    0,  -12,  -12 },
        /*Dwarf */ {   15,   -8,   40,  -35,    2,  -12,    8 },
        /*Orc   */ {  -25,  -30,  -35,   40,    8,   -5,  -10 },
        /*Nomad */ {    5,    0,    2,    8,   40,   -5,    6 },
        /*Lizard*/ {  -15,  -12,  -12,   -5,   -5,   40,  -15 },
        /*Yeti  */ {  -18,  -12,    8,  -10,    6,  -15,   40 },
    };

    public static int BaseAffinity(Race a, Race b) => Base[(int)a, (int)b];

    /// <summary>자발적 합병 허용 — 우호적(기본 호감도 ≥ 0)일 때만. 정복(Conquer)은 종족 무관 강제.</summary>
    public static bool MergeAllowed(Race a, Race b) => BaseAffinity(a, b) >= 0;
}

/// <summary>biome별 인구 동역학 (로지스틱 성장). ★ 튜닝 지점.</summary>
public static class BiomeData
{
    public static int InitialPopulation(Biome b) => b switch
    {
        Biome.Plains => 120,
        Biome.Forest => 90,
        Biome.Steppe => 80,
        Biome.Mountain => 70,
        Biome.Swamp => 55,
        Biome.Snow => 50,
        Biome.Desert => 50,
        Biome.Tundra => 45,
        _ => 60,
    };

    public static double GrowthRate(Biome b) => b switch
    {
        Biome.Plains => 0.030,
        Biome.Forest => 0.025,
        Biome.Steppe => 0.022,
        Biome.Mountain => 0.020,
        Biome.Swamp => 0.013,
        Biome.Desert => 0.012,
        Biome.Snow => 0.012,
        Biome.Tundra => 0.010,
        _ => 0.018,
    };

    public static int Carry(Biome b) => b switch
    {
        Biome.Plains => 3000,
        Biome.Forest => 2200,
        Biome.Steppe => 2000,
        Biome.Mountain => 1800,
        Biome.Swamp => 1100,
        Biome.Desert => 1000,
        Biome.Snow => 900,
        Biome.Tundra => 800,
        _ => 1500,
    };
}
