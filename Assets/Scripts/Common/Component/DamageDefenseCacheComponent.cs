namespace ARPG.Component
{
    /// <summary>
    /// 데미지 계산용 타겟측 방어 캐시.
    /// StatComponent의 FinalDefense/FinalXResist/FinalEvasion/FinalBlockChance/FinalBlockReduction을
    /// 매 히트마다 다시 계산하지 않도록 미리 (1 - r/(r+100)) 등 곱셈 상수 형태로 보관한다.
    /// System_StatCalculation 이 StatComponent 재계산 직후 항상 갱신.
    /// 캐시가 없을 경우 DamageCalculator가 lazy build 한다.
    /// </summary>
    public struct DamageDefenseCacheComponent
    {
        public float PhysReductionMul;       // 1 - reduction (Defense 기준)
        public float FireReductionMul;
        public float IceReductionMul;
        public float LightningReductionMul;
        public float PoisonReductionMul;

        public int   EvasionRate;            // FinalEvasion (확률 %, 0~100)
        public int   BlockChance;            // FinalBlockChance (확률 %, 0~100)
        public float BlockReductionMul;      // FinalBlockReduction/100 (0이면 0.5 fallback)
    }
}
