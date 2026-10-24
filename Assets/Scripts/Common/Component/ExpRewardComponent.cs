namespace ARPG.Component
{
    /// <summary>
    /// 몬스터 처치 경험치 정보 (PLAYER_LEVEL_CHARM_DESIGN.md §3.2). 생성할 때 붙이고 System_HpCheck가 사망 때 읽는다.
    /// 받는 경험치 = 기본 경험치(MonsterLevel) × ExpMul × 레벨 차이 보정 — 보정은 처치 순간의 플레이어 레벨로 계산한다.
    /// </summary>
    public struct ExpRewardComponent
    {
        public int MonsterLevel;
        public float ExpMul;    // MonsterArchetypeTable.ExpMul
    }
}
