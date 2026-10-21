#nullable enable
using ARPG.Tables;
using UnityEngine;

namespace ARPG.Item
{
    /// <summary>
    /// 장비 품질 규칙 (ITEM_DROP_BALANCE_DESIGN.md §5). 품질은 0~20이고 기본 능력(Base 칸) 수치에만
    /// × (1 + 품질/100)을 적용한다. 고정 옵션·접두·접미·무기 고유 강화에는 적용하지 않는다.
    /// 품질은 일반 등급(접두·접미 0개)일 때만 바꿀 수 있다.
    /// </summary>
    public static class EquipmentQualityRules
    {
        public const int MIN_QUALITY = 0;
        public const int MAX_QUALITY = 20;

        public static int ClampQuality(int quality)
        {
            return Mathf.Clamp(quality, MIN_QUALITY, MAX_QUALITY);
        }

        /// <summary>
        /// Base 칸 수치에 품질을 적용한다. 가장 가까운 정수로 반올림하고 정확히 .5면 올린다.
        /// 예: 방어도 90, 품질 20 → 108. 방어도 2, 품질 5 → 2.1 → 2.
        /// </summary>
        public static int ApplyToBaseValue(int value, int quality)
        {
            return ApplyIncreasePercent(value, ClampQuality(quality));
        }

        /// <summary>value × (1 + percent/100)을 반올림(.5는 올림)한다. percent가 0 이하면 그대로.</summary>
        public static int ApplyIncreasePercent(int value, int percent)
        {
            if (percent <= 0 || value == 0)
                return value;

            long scaled = (long)value * (100 + percent);
            // 음수 값은 없지만, 있으면 0에서 먼 쪽으로 .5를 올려 대칭을 유지한다
            long rounded = scaled >= 0 ? (scaled + 50) / 100 : -((-scaled + 50) / 100);
            return (int)System.Math.Clamp(rounded, int.MinValue, int.MaxValue);
        }

        /// <summary>
        /// 표시·비교용: Base 칸이고 품질이 있으면 품질을 적용한 복사본, 아니면 원본을 그대로 돌려준다. 원본은 바꾸지 않는다.
        /// </summary>
        public static ModInstance ApplyToBaseMod(ModInstance mod, int quality)
        {
            int q = ClampQuality(quality);
            if (q == 0 || mod.Slot != GlobalEnum.ModSlot.Base)
                return mod;

            return new ModInstance
            {
                ModTableId = mod.ModTableId,
                Slot = mod.Slot,
                Tier = mod.Tier,
                Value1 = (ushort)Mathf.Clamp(ApplyToBaseValue(mod.Value1, q), 0, ushort.MaxValue),
                Value2 = (ushort)Mathf.Clamp(ApplyToBaseValue(mod.Value2, q), 0, ushort.MaxValue),
                Table = mod.Table,
            };
        }

        /// <summary>
        /// 무기 피해 증가율: 품질은 무기 고유 강화의 증가율과 더한 뒤 한 번 곱한다(증가율 공통 규칙).
        /// </summary>
        public static int CombineWithIncrease(int increasePercent, int quality)
        {
            return increasePercent + ClampQuality(quality);
        }
    }
}
