namespace ARPG.Utility
{
    /// <summary>
    /// 밸런스 디자인 곡선 상수. Docs/BALANCE_DESIGN.md §3 참조.
    /// 시뮬 시트(Constants 탭)와 1:1 매칭 — 시트에서 검증한 값을 코드에 반영.
    /// 튜닝 시 시트 ↔ 이 파일 둘 다 갱신할 것.
    /// </summary>
    public static class BalanceConstants
    {
        /// <summary>몬스터 HP 레벨당 배율 (≈6레벨마다 2배)</summary>
        public const float HpPerLevel = 1.12f;

        /// <summary>몬스터 공격력 레벨당 배율 (≈7레벨마다 2배)</summary>
        public const float DmgPerLevel = 1.10f;

        /// <summary>무기 DPS DropLevel당 배율 (HP 곡선보다 가파름 → 장비 교체 압박)</summary>
        public const float WeaponDpsPerLevel = 1.13f;
    }
}
