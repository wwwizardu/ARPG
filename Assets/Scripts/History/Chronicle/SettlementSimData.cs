using System;

namespace ARPG.History
{
    /// <summary>
    /// 종족 간 기본 성향 (= 설계의 RaceRelationTable v1: 코드 상수로 시작, §7-2 결정).
    /// 대칭 행렬. 같은 종족 +40, 우호 종족 양수, 적대 종족 음수.
    /// 검증 프로토타입(Tools/ChronicleSimPrototype/RaceData.cs)에서 포팅.
    /// ★ 튜닝 지점: 누가 누구와 동맹/전쟁하는지를 결정한다.
    /// </summary>
    public static class RaceRelations
    {
        // [a,b] 기본 호감도. 반드시 대칭으로 유지할 것. 인덱스 = (int)GlobalEnum.RaceType (Human=0..Yeti=6)
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

        public static int BaseAffinity(GlobalEnum.RaceType a, GlobalEnum.RaceType b)
            => Base[(int)a, (int)b];

        /// <summary>자발적 합병 허용 — 우호적(기본 호감도 ≥ 0)일 때만. 정복(Conquer)은 종족 무관 강제.</summary>
        public static bool MergeAllowed(GlobalEnum.RaceType a, GlobalEnum.RaceType b)
            => BaseAffinity(a, b) >= 0;
    }

    /// <summary>
    /// biome별 인구 동역학 (로지스틱 성장). ★ 튜닝 지점.
    /// 정착지의 ChunkBiome 기준 (export/검증과 동일).
    /// 검증 프로토타입(Tools/ChronicleSimPrototype/RaceData.cs)에서 포팅.
    /// </summary>
    public static class BiomeData
    {
        public static int InitialPopulation(GlobalEnum.BiomeType b) => b switch
        {
            GlobalEnum.BiomeType.Plains => 120,
            GlobalEnum.BiomeType.Forest => 90,
            GlobalEnum.BiomeType.Steppe => 80,
            GlobalEnum.BiomeType.Mountain => 70,
            GlobalEnum.BiomeType.Swamp => 55,
            GlobalEnum.BiomeType.Snow => 50,
            GlobalEnum.BiomeType.Desert => 50,
            GlobalEnum.BiomeType.Tundra => 45,
            _ => 60,
        };

        public static double GrowthRate(GlobalEnum.BiomeType b) => b switch
        {
            GlobalEnum.BiomeType.Plains => 0.030,
            GlobalEnum.BiomeType.Forest => 0.025,
            GlobalEnum.BiomeType.Steppe => 0.022,
            GlobalEnum.BiomeType.Mountain => 0.020,
            GlobalEnum.BiomeType.Swamp => 0.013,
            GlobalEnum.BiomeType.Desert => 0.012,
            GlobalEnum.BiomeType.Snow => 0.012,
            GlobalEnum.BiomeType.Tundra => 0.010,
            _ => 0.018,
        };

        public static int Carry(GlobalEnum.BiomeType b) => b switch
        {
            GlobalEnum.BiomeType.Plains => 3000,
            GlobalEnum.BiomeType.Forest => 2200,
            GlobalEnum.BiomeType.Steppe => 2000,
            GlobalEnum.BiomeType.Mountain => 1800,
            GlobalEnum.BiomeType.Swamp => 1100,
            GlobalEnum.BiomeType.Desert => 1000,
            GlobalEnum.BiomeType.Snow => 900,
            GlobalEnum.BiomeType.Tundra => 800,
            _ => 1500,
        };
    }
}
