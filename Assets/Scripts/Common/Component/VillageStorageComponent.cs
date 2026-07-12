#nullable enable

namespace ARPG.Component
{
    /// <summary>
    /// 마을의 자원 저장 상태를 담는 ECS 컴포넌트.
    /// VillageManager가 Village 엔티티에 1:1로 붙인다.
    /// Phase A에서는 VillageData.Resources와 병존 (Phase B에서 단일화).
    /// 저장 수치(Amount)는 정수. 생산 소수부는 *Carry 버퍼에 누적한다.
    /// </summary>
    public struct VillageStorageComponent
    {
        public int VillageId;

        // 자원 수치 (정수)
        public int FoodAmount;
        public int WoodAmount;
        public int StoneAmount;

        // 자원 Cap (기본 50, ResourceCaps로 오버라이드 가능)
        public int FoodCap;
        public int WoodCap;
        public int StoneCap;

        // 생산 소수 누적 버퍼 (런타임 전용 — 실체화 중에만 유효, 세이브 안 함).
        // prodPop 압축으로 시간당 생산이 소수가 되므로, 정수부만 Amount에 반영하고 나머지를 여기 이월한다.
        // 마을이탈(Dematerialize) 시 <1 손실은 무시 (재실체화 시 0에서 재개).
        public float FoodCarry;
        public float WoodCarry;
        public float StoneCarry;
        public float IronCarry;
        public float GoldCarry;

        // Food 0 유지된 게임시간 누적 (24h 넘으면 경고 1회)
        public int HungerHoursAccumulated;

        // Cap 도달 여부 비트 플래그
        public byte SurplusFlags;
    }

    public static class VillageSurplusFlags
    {
        public const byte Food = 1 << 0;
        public const byte Wood = 1 << 1;
        public const byte Stone = 1 << 2;
    }
}
