#nullable enable
using System;
using System.Collections.Generic;
using System.Text;
using ARPG.Data;
using ARPG.Item;
using ARPG.Tables;
using ARPG.Utility;
using UnityEngine;
using GE = GlobalEnum;
using Random = UnityEngine.Random;

namespace ARPG.EditorTools
{
    /// <summary>
    /// 드롭 분포 표본 검사 (ITEM_DROP_BALANCE_DESIGN.md §7·§8). Monster Affix Distribution 메뉴가 함께 실행한다.
    /// 희귀도 곡선, 희귀 → 마법 → 일반 순서 판정(§8.7 표), 판정 횟수(수량), 장비 구간·부위, 등급별 옵션 수·품질,
    /// 마법 규칙, 화폐 비중, 마리당 평균 아이템 수, 던전 예시(§7.6)를 문서 값과 대조한다.
    /// </summary>
    public static partial class MonsterAffixDiagnostics
    {
        private const int GradeSampleSize = 100000;
        private const int RollSampleSize = 100000;
        private const int SelectorSampleSize = 50000;
        private const int ItemSampleSize = 3000;
        private const int ItemSampleId = 12;   // 가죽 조끼 (갑옷) — 접두·접미 후보가 모두 있는 방어구

        // §8.2 플레이어 희귀도 곡선 표
        private static readonly int[,] RarityCurveExpected =
        {
            { 0, 0 }, { 100, 100 }, { 150, 150 }, { 200, 183 }, { 300, 210 }, { 500, 228 }, { 1000, 240 },
        };

        // §8.7 최대 레벨 구간(90~99) "희귀 / 일반" 표. 행 = 플레이어 희귀도, 열 = 몬스터 배율(×1, ×1.25, ×1.7, ×2.0)
        private static readonly int[] MaxBandPlayerRarities = { 0, 100, 150, 200, 300 };
        private static readonly float[] MaxBandMonsterMultipliers = { 1f, 1.25f, 1.7f, 2f };
        private static readonly int[,] MaxBandExpectedRare =
        {
            { 10, 13, 17, 20 }, { 20, 25, 34, 40 }, { 25, 31, 43, 50 }, { 28, 35, 48, 57 }, { 31, 39, 53, 62 },
        };
        private static readonly int[,] MaxBandExpectedNormal =
        {
            { 64, 56, 42, 34 }, { 34, 21, 1, 0 }, { 21, 7, 0, 0 }, { 13, 0, 0, 0 }, { 7, 0, 0, 0 },
        };

        private static int _distributionFailures;

        private static void AppendDropDistribution(StringBuilder report)
        {
            _distributionFailures = 0;
            report.AppendLine("■ 드롭 분포 (ITEM_DROP_BALANCE_DESIGN §7·§8)");
            AppendRarityCurve(report);
            AppendGradeTable(report);
            AppendRollCounts(report);
            AppendEquipmentSelection(report);
            AppendCreatedEquipment(report);
            AppendCurrencyAndAmount(report);
            AppendDungeonExample(report);
            report.AppendLine(_distributionFailures == 0
                ? "  드롭 분포 결과: PASS"
                : $"  드롭 분포 결과: FAIL {_distributionFailures}건");
        }

        private static void AppendRarityCurve(StringBuilder report)
        {
            StringBuilder line = new StringBuilder("  희귀도 곡선: ");
            bool ok = true;
            for (int i = 0; i < RarityCurveExpected.GetLength(0); i++)
            {
                int input = RarityCurveExpected[i, 0];
                float applied = DropBonusResolver.ApplyPlayerRarityCurve(input);
                ok &= Mathf.Abs(Mathf.Round(applied) - RarityCurveExpected[i, 1]) <= 1f;
                line.Append($"+{input}%→{applied:F0}% ");
            }
            ok &= DropBonusResolver.ApplyPlayerRarityCurve(-40) == -40f
                && DropBonusResolver.ApplyPlayerRarityCurve(100000) < 250f;
            Check(report, ok, line.ToString() + "(문서 §8.2 표, 음수는 그대로, 최대 250 미만)");
        }

        private static void AppendGradeTable(StringBuilder report)
        {
            DropBandTable? band = AR.s.Data.GetDropBand(90);
            if (band == null)
            {
                Check(report, false, "레벨 90 구간표 행 없음");
                return;
            }

            bool ok = true;
            StringBuilder table = new StringBuilder();
            for (int p = 0; p < MaxBandPlayerRarities.Length; p++)
            {
                table.Append($"    플레이어 +{MaxBandPlayerRarities[p]}%:");
                for (int m = 0; m < MaxBandMonsterMultipliers.Length; m++)
                {
                    float multiplier = (1f + DropBonusResolver.ApplyPlayerRarityCurve(MaxBandPlayerRarities[p]) / 100f)
                        * MaxBandMonsterMultipliers[m];
                    float rare = DropBonusResolver.GetRareChance(band, multiplier);
                    float normal = (1f - rare) * (1f - DropBonusResolver.GetMagicCheckChance(band, multiplier));
                    int rarePct = Mathf.RoundToInt(rare * 100f);
                    int normalPct = Mathf.RoundToInt(normal * 100f);
                    ok &= Mathf.Abs(rarePct - MaxBandExpectedRare[p, m]) <= 1
                        && Mathf.Abs(normalPct - MaxBandExpectedNormal[p, m]) <= 1;
                    table.Append($" ×{MaxBandMonsterMultipliers[m]:0.##} {rarePct}/{normalPct}");
                }
                table.AppendLine();
            }
            Check(report, ok, "레벨 90~99 희귀/일반 % (문서 §8.7 표, ±1%p)");
            report.Append(table);

            // 순서 판정 표본: 레벨 45 구간, M = 1.3 → 희귀 9.1 · 마법 28.0 · 일반 62.9 (§8.7 예시)
            DropBandTable? band45 = AR.s.Data.GetDropBand(45);
            if (band45 == null)
                return;
            int[] counts = new int[3];
            for (int i = 0; i < GradeSampleSize; i++)
                counts[(int)DropBonusResolver.ResolveGrade(band45, 1.3f, Random.value, Random.value)]++;
            double rareShare = 100.0 * counts[(int)GE.EquipmentGrade.Rare] / GradeSampleSize;
            double magicShare = 100.0 * counts[(int)GE.EquipmentGrade.Magic] / GradeSampleSize;
            double normalShare = 100.0 * counts[(int)GE.EquipmentGrade.Normal] / GradeSampleSize;
            Check(report, Math.Abs(rareShare - 9.1) < 0.5 && Math.Abs(magicShare - 28.0) < 0.6 && Math.Abs(normalShare - 62.9) < 0.6,
                $"레벨 45 희귀도 +30% 표본 {GradeSampleSize}회: 희귀 {rareShare:F1} · 마법 {magicShare:F1} · 일반 {normalShare:F1} (문서 9.1 · 28.0 · 62.9)");
        }

        private static void AppendRollCounts(StringBuilder report)
        {
            double mean135 = SampleRolls(1, 35);
            double mean115 = SampleRolls(10, 15);
            int capped = DropBonusResolver.ResolveRollCount(10, 500, 0.99f);
            int negative = DropBonusResolver.ResolveRollCount(1, -150, 0f);
            Check(report, Math.Abs(mean135 - 1.35) < 0.01 && Math.Abs(mean115 - 11.5) < 0.02
                    && capped == DropBonusResolver.MAX_ROLLS_PER_SOURCE && negative == 0,
                $"판정 횟수: 1회×(1+35%) 평균 {mean135:F3} (1.35), 10회×(1+15%) 평균 {mean115:F3} (11.5), " +
                $"10회×(1+500%) {capped} (상한 {DropBonusResolver.MAX_ROLLS_PER_SOURCE}), 수량 −150% {negative} (0)");
        }

        private static double SampleRolls(int baseRolls, int quantity)
        {
            long total = 0;
            for (int i = 0; i < RollSampleSize; i++)
                total += DropBonusResolver.ResolveRollCount(baseRolls, quantity, Random.value);
            return (double)total / RollSampleSize;
        }

        private static void AppendEquipmentSelection(StringBuilder report)
        {
            List<ItemTable> items = AR.s.Data.GetAllItems();
            int[] levels = { 5, 25, 90 };
            for (int l = 0; l < levels.Length; l++)
            {
                int level = levels[l];
                PlayerEquipmentDropSelector.ResolveBands(items, level, out int currentBand, out int previousBand);
                DropBandTable? band = AR.s.Data.GetDropBand(level);
                int share = band != null ? band.CurrentBandShare : PlayerEquipmentDropSelector.DEFAULT_CURRENT_BAND_SHARE;
                int current = 0;
                int weapon = 0;
                int valid = 0;
                for (int i = 0; i < SelectorSampleSize; i++)
                {
                    int itemId = PlayerEquipmentDropSelector.Select(level, share, Random.value, Random.value, Random.value);
                    ItemTable? item = AR.s.Data.GetItem(itemId);
                    if (item == null)
                        continue;
                    valid++;
                    if (Mathf.Max(1, item.DropLevel) == currentBand)
                        current++;
                    if (item.Category == GE.ItemCategory.Weapon)
                        weapon++;
                }
                double currentShare = valid > 0 ? 100.0 * current / valid : 0d;
                double weaponShare = valid > 0 ? 100.0 * weapon / valid : 0d;
                double expectedCurrent = previousBand > 0 ? share : 100;
                Check(report, valid == SelectorSampleSize && Math.Abs(currentShare - expectedCurrent) < 1.0
                        && Math.Abs(weaponShare - 40.0) < 1.0,
                    $"장비 선택 레벨 {level}: 현재 구간 {currentBand} {currentShare:F1}% (기대 {expectedCurrent}%, 직전 {previousBand}), " +
                    $"무기 {weaponShare:F1}% (기대 40%)");
            }
        }

        private static void AppendCreatedEquipment(StringBuilder report)
        {
            DropBandTable? band = AR.s.Data.GetDropBand(90);
            if (band == null || AR.s.Data.GetItem(ItemSampleId) == null)
            {
                Check(report, false, $"장비 생성 표본 준비 실패 (구간표 90 또는 ItemTable {ItemSampleId})");
                return;
            }

            DropBonus bonus = new DropBonus(0, 1f, band);
            int[] gradeCounts = new int[3];
            int invalid = 0;
            int qualityMin = int.MaxValue;
            int qualityMax = int.MinValue;
            int magicTop = 0, magicAffixes = 0, rareTop = 0, rareAffixes = 0;
            for (int i = 0; i < ItemSampleSize; i++)
            {
                EquipmentRollOptions options = DropHelper.RollEquipmentOptions(90, bonus, 0, 0);
                ItemData? item = AR.s.Item.CreateInventoryItemData(ItemSampleId, 1, null, options);
                EquipmentData? equipment = item?.Equipment;
                if (equipment == null)
                {
                    invalid++;
                    continue;
                }

                GE.EquipmentGrade grade = equipment.GetGrade();
                gradeCounts[(int)grade]++;
                equipment.CountAffixes(out int prefixCount, out int postfixCount);
                int affixes = prefixCount + postfixCount;
                switch (grade)
                {
                    case GE.EquipmentGrade.Normal:
                        qualityMin = Mathf.Min(qualityMin, equipment.Quality);
                        qualityMax = Mathf.Max(qualityMax, equipment.Quality);
                        break;
                    case GE.EquipmentGrade.Magic:
                        if (equipment.Quality != 0 || affixes < 1 || affixes > 2)
                            invalid++;
                        CountTopTiers(equipment, ref magicTop, ref magicAffixes);
                        break;
                    default:
                        if (equipment.Quality != 0 || affixes < band.RareModMin || affixes > band.RareModMax
                            || prefixCount > ItemEnchantmentRules.AFFIX_LIMIT || postfixCount > ItemEnchantmentRules.AFFIX_LIMIT)
                            invalid++;
                        CountTopTiers(equipment, ref rareTop, ref rareAffixes);
                        break;
                }
                if (grade != options.Grade)
                    invalid++;
            }

            double normal = 100.0 * gradeCounts[(int)GE.EquipmentGrade.Normal] / ItemSampleSize;
            double magic = 100.0 * gradeCounts[(int)GE.EquipmentGrade.Magic] / ItemSampleSize;
            double rare = 100.0 * gradeCounts[(int)GE.EquipmentGrade.Rare] / ItemSampleSize;
            Check(report, invalid == 0 && Math.Abs(normal - band.NormalRate) < 3.0 && Math.Abs(magic - band.MagicRate) < 3.0
                    && Math.Abs(rare - band.RareRate) < 2.0
                    && qualityMin >= band.NormalQualityMin && qualityMax <= band.NormalQualityMax,
                $"장비 생성 {ItemSampleSize}개 (레벨 90, 보너스 없음): 일반 {normal:F1} · 마법 {magic:F1} · 희귀 {rare:F1} " +
                $"(기대 {band.NormalRate} · {band.MagicRate} · {band.RareRate}), 일반 품질 {qualityMin}~{qualityMax} " +
                $"(구간 {band.NormalQualityMin}~{band.NormalQualityMax}), 규칙 위반 {invalid}");

            double magicTopShare = magicAffixes > 0 ? 100.0 * magicTop / magicAffixes : 0d;
            double rareTopShare = rareAffixes > 0 ? 100.0 * rareTop / rareAffixes : 0d;
            Check(report, magicAffixes > 0 && rareAffixes > 0 && magicTopShare > rareTopShare * 1.3,
                $"마법 규칙: T9·T10 비중 마법 {magicTopShare:F1}% vs 희귀(일반 규칙) {rareTopShare:F1}% (마법 쪽이 뚜렷이 커야 함)");
        }

        private static void CountTopTiers(EquipmentData equipment, ref int top, ref int total)
        {
            for (int i = 0; i < equipment.Mods.Count; i++)
            {
                ModInstance mod = equipment.Mods[i];
                if (mod.Slot != GE.ModSlot.Prefix && mod.Slot != GE.ModSlot.Postfix)
                    continue;
                total++;
                if (mod.Tier >= 9)
                    top++;
            }
        }

        private static void AppendCurrencyAndAmount(StringBuilder report)
        {
            DropTable? drop = AR.s.Data.GetDrop(FieldDropTableId);
            if (drop == null)
                return;

            double exalted1 = GetCurrencyShare(1, 1002);
            double exalted90 = GetCurrencyShare(90, 1002);
            double chaos = GetCurrencyShare(90, 1003) + GetCurrencyShare(90, 1004);
            Check(report, Math.Abs(exalted1 - 16.7) < 0.5 && Math.Abs(exalted90 - 33.3) < 0.5 && chaos == 0d,
                $"화폐 비중 (희귀도 0%): 엑잘티드 레벨 1~9 {exalted1:F1}% (17), 90~99 {exalted90:F1}% (33), 카오스·신성 {chaos:F1}% (0)");

            int otherRates = drop.CurrencyRate + drop.EquipmentRate + drop.SkillBookRate + drop.SkillPageRate;
            double perRoll = otherRates / (double)Math.Max(1, otherRates + drop.NothingRate);
            int[] levels = { 5, 25, 90 };
            double[] expected = { 0.476, 0.524, 0.595 };
            StringBuilder line = new StringBuilder("일반 몬스터 마리당 평균 아이템 (보너스 없음): ");
            bool ok = true;
            for (int i = 0; i < levels.Length; i++)
            {
                DropBandTable? band = AR.s.Data.GetDropBand(levels[i]);
                double perMonster = perRoll * (1d + (band != null ? band.QuantityBonus : 0) / 100d);
                ok &= Math.Abs(perMonster - expected[i]) < 0.002;
                line.Append($"레벨 {levels[i]} {perMonster:F3} ({expected[i]}) ");
            }
            Check(report, ok, line.ToString() + "(문서 §7.5)");
        }

        private static double GetCurrencyShare(int level, int itemId)
        {
            List<DropPoolEntry> pool = DropPoolBuilder.GetCurrencyPool(level);
            DropBandTable? band = AR.s.Data.GetDropBand(level);
            float total = 0f;
            float target = 0f;
            for (int i = 0; i < pool.Count; i++)
            {
                float weight = DropBonusResolver.GetCurrencyWeight(pool[i].Weight, pool[i].Tier, band, 1f);
                total += weight;
                if (pool[i].ItemId == itemId)
                    target += weight;
            }
            return total > 0f ? 100.0 * target / total : 0d;
        }

        private static void AppendDungeonExample(StringBuilder report)
        {
            // §7.6 예시: 지역 레벨 30 입구 → 일반 32, 보스 34. 구간 수량 10% + 던전 수량 70% → 판정 ×1.8
            DungeonTable? dungeon = AR.s.Data.GetDungeonTable(1);
            MonsterArchetypeTable? elite = AR.s.Data.GetMonsterArchetype(GE.MonsterArchetype.Elite);
            DropBandTable? band = AR.s.Data.GetDropBand(32);
            if (dungeon == null || elite == null || band == null)
            {
                Check(report, false, "던전 예시 준비 실패 (DungeonTable 1, Elite 행, 구간표 32)");
                return;
            }

            int monsterLevel = ARPG.Dungeon.DungeonLayout.ResolveEncounterLevel(30, dungeon.MonsterLevelOffset);
            int bossLevel = ARPG.Dungeon.DungeonLayout.ResolveEncounterLevel(30, dungeon.BossLevelOffset);
            double rollMul = 1d + (band.QuantityBonus + dungeon.DropQuantityBonus) / 100d;
            float bossMultiplier = DropBonusResolver.ResolveRarityMultiplier(0, elite.DropRarityBonus, dungeon.DropRarityBonus);
            float bossRare = DropBonusResolver.GetRareChance(band, bossMultiplier);
            float bossNormal = (1f - bossRare) * (1f - DropBonusResolver.GetMagicCheckChance(band, bossMultiplier));
            Check(report, monsterLevel == 32 && bossLevel == 34 && Math.Abs(rollMul - 1.8) < 1e-6
                    && elite.DropRolls == 10 && dungeon.ChestDropRolls == 3
                    && Mathf.RoundToInt(bossRare * 100f) == 12 && Mathf.RoundToInt(bossNormal * 100f) == 51,
                $"던전 예시(지역 30): 일반 {monsterLevel}·보스 {bossLevel} (32·34), 판정 ×{rollMul:F2} (1.8), " +
                $"보스 판정 {elite.DropRolls}회 → {elite.DropRolls * rollMul:F1}회 (18), 상자 {dungeon.ChestDropRolls}회, " +
                $"보스 희귀 {bossRare * 100f:F0}% · 일반 {bossNormal * 100f:F0}% (12 · 51)");
        }

        private static void Check(StringBuilder report, bool ok, string message)
        {
            if (ok == false)
                _distributionFailures++;
            report.AppendLine($"  [{(ok == true ? "PASS" : "FAIL")}] {message}");
        }
    }
}
