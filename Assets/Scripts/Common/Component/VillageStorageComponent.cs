#nullable enable

namespace ARPG.Component
{
    /// <summary>
    /// 마을의 자원 저장 상태를 담는 ECS 컴포넌트.
    /// VillageManager가 Village 엔티티에 1:1로 붙인다.
    /// Phase A에서는 VillageData.Resources와 병존 (Phase B에서 단일화).
    /// 모든 수치는 정수 - 소수 버퍼 없음.
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

        // Stone은 5시간당 +1 → 시간 누적 카운터 (0~4)
        public int StoneTimer;

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
