using UnityEngine;

namespace ARPG.Skill.Combat
{
    /// <summary>
    /// 정확도 대 회피 적중 판정 규칙 (PLAYER_LEVEL_CHARM_DESIGN.md §5.2·§5.3). 상태 없는 순수 계산이다.
    /// 적중률 = 정확도 ÷ (정확도 + 회피), 하한 25%. 회피 불가 태그 스킬은 판정 자체를 하지 않는다(DamageCalculator).
    /// </summary>
    public static class HitChanceRules
    {
        public const float MIN_HIT_CHANCE = 0.25f;

        // A(L) = 100 + 10 × (L − 1)
        public const int BASE_ACCURACY_AT_LEVEL_1 = 100;
        public const int ACCURACY_PER_LEVEL = 10;

        // 몬스터 회피 E(L) = A(L) ÷ 9 × 종 회피 배율
        public const float MONSTER_EVASION_DIVISOR = 9f;

        /// <summary>플레이어 기본 정확도 A(L). NPC는 고향 마을 지역 레벨을 넣는다(§5.4).</summary>
        public static int GetLevelAccuracy(int level)
        {
            int clamped = Mathf.Max(1, level);
            return BASE_ACCURACY_AT_LEVEL_1 + ACCURACY_PER_LEVEL * (clamped - 1);
        }

        /// <summary>몬스터 정확도 M(L) = A(L) × 종 정확도 배율.</summary>
        public static int GetMonsterAccuracy(int level, float accuracyMul)
        {
            return Mathf.Max(0, Mathf.RoundToInt(GetLevelAccuracy(level) * Mathf.Max(0f, accuracyMul)));
        }

        /// <summary>몬스터 기본 회피 E(L) = A(L) ÷ 9 × 종 회피 배율. 회피 증가(접사)는 스탯 계산이 곱한다.</summary>
        public static int GetMonsterEvasion(int level, float evasionMul)
        {
            return Mathf.Max(0, Mathf.RoundToInt(GetLevelAccuracy(level) / MONSTER_EVASION_DIVISOR * Mathf.Max(0f, evasionMul)));
        }

        /// <summary>적중률 (0.25~1). 회피가 0 이하면 항상 맞는다.</summary>
        public static float GetHitChance(int accuracy, int evasion)
        {
            if (evasion <= 0)
                return 1f;

            float acc = Mathf.Max(0, accuracy);
            return Mathf.Max(MIN_HIT_CHANCE, acc / (acc + evasion));
        }
    }
}
