#nullable enable

namespace ARPG.Village
{
    public enum WallType
    {
        Palisade = 0,    // Phase C: 나무 울타리
        StoneWall = 1,   // Phase C+: 돌 성벽 (예약)
    }

    public enum WallOrientation
    {
        Horizontal = 0,
        Vertical = 1,
        CornerNE = 2,
        CornerNW = 3,
        CornerSE = 4,
        CornerSW = 5,
        Gate = 6,
    }
}
