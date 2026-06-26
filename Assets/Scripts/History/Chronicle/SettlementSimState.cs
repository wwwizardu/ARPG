namespace ARPG.History
{
    /// <summary>
    /// L1 시뮬 내부 전용 정착지 상태 (저장 안 함 — SettlementSnapshot으로 평탄화하여 저장).
    /// 참조 타입(class): 시뮬이 Get(id)로 꺼낸 객체를 제자리 변경하므로 값 복사를 피한다.
    /// 검증 프로토타입(Tools/ChronicleSimPrototype/ChronicleSimulator.cs:SettlementState)에서 포팅.
    /// 설계: PROCEDURAL_HISTORY_DESIGN.md §3.1
    /// </summary>
    public sealed class SettlementSimState
    {
        public int SettlementId;             // placement 인덱스 기반 결정론 ID
        public GlobalEnum.RaceType Race;
        public GlobalEnum.BiomeType Biome;   // ChunkBiome (settlement 위치 청크)
        public int RegionX, RegionY;
        public int WorldX, WorldY;
        public double Pop;
        public int KingdomId = -1;           // -1 = 무소속
        public SettlementFate Fate = SettlementFate.Alive;
        public int MergedIntoId = -1;        // Fate == Merged일 때
        public int ConqueredByKingdomId = -1; // Fate == Conquered일 때 (정복국 KingdomId)
        public int FoundedYear;

        // 정복지(Conquered)는 살아있는 마을로 취급 — 능동 사건 주체가 될 수 있음.
        public bool Active => Fate == SettlementFate.Alive || Fate == SettlementFate.Conquered;
        public bool Gone => Fate == SettlementFate.Merged || Fate == SettlementFate.Ruined;
    }
}
