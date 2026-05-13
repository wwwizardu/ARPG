namespace ARPG.Component
{
    /// <summary>
    /// 데미지 계산용 타겟측 방어 캐시.
    /// 원소 저항/회피/블록은 미리 계산해 곱셈 상수로 보관.
    /// 아머는 PoE 원본 공식이 들어오는 데미지에 의존하므로 raw 값만 보관 — 매 히트 시 Armor / (Armor + 10×Damage) 계산.
    /// System_StatCalculation 이 StatComponent 재계산 직후 항상 갱신. 없을 경우 DamageCalculator가 lazy build.
    /// </summary>
    public struct DamageDefenseCacheComponent
    {
        public int   Armor;                  // FinalDefense raw (= 물리 아머값). 매 히트 시 PoE 원본 공식으로 계산
        public float FireReductionMul;
        public float IceReductionMul;
        public float LightningReductionMul;
        public float PoisonReductionMul;

        public int   EvasionRate;            // FinalEvasion (확률 %, 0~100)
        public int   BlockChance;            // FinalBlockChance (확률 %, 0~100)
        public float BlockReductionMul;      // FinalBlockReduction/100 (0이면 0.5 fallback)
    }
}
