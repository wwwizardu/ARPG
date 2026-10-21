#nullable enable
using System;
using System.Collections.Generic;
using System.Text;
using ARPG.Component;
using ARPG.Monster;
using ARPG.Tables;
using UnityEditor;
using UnityEngine;
using GE = GlobalEnum;
using Random = UnityEngine.Random;

namespace ARPG.EditorTools
{
    /// <summary>
    /// 몬스터 접사 판정 진단 (MONSTER_AFFIX_DESIGN.md §13).
    /// - 등급별 접사 개수 분포를 대량 판정해 문서 §3 표와 대조한다.
    /// - 등급별·필드 평균 드랍 판정과 필드 총량을 유지하는 NothingRate를 계산한다(§8.3 식).
    /// - 종별로 실제 붙는 접사 수를 재서 피해 종류 조건·Group 제외로 후보가 모자란 종을 찾는다.
    /// - 드롭 분포 표본 검사(ITEM_DROP_BALANCE_DESIGN.md §7·§8): MonsterAffixDiagnostics_DropDistribution.cs.
    /// 테이블은 AR.s.Data에서 읽으므로 플레이 모드(GameScene 로드 후)에서만 동작한다. 난수 상태는 실행 뒤 되돌린다.
    /// </summary>
    public static partial class MonsterAffixDiagnostics
    {
        private const int CountSampleSize = 100000;
        private const int SpeciesSampleSize = 2000;
        private const int SampleSeed = 20261011;

        // 필드 몬스터 등급 비율 (MONSTER_RARITY_DESIGN.md R3: 마법 무리 15%, 희귀 무리 8%, 평균 무리 크기 2)
        private const double FieldNormalShare = 0.81;
        private const double FieldMagicShare = 0.15;
        private const double FieldRareShare = 0.04;
        // 마리당 평균 드랍 개수 목표 (등급 도입 전과 같은 0.8개, MONSTER_RARITY_DESIGN.md §3.2)
        private const double FieldItemsPerMonsterTarget = 0.8;
        private const int FieldDropTableId = 1;

        private static readonly GE.MonsterArchetype[] Archetypes =
        {
            GE.MonsterArchetype.Normal, GE.MonsterArchetype.Magic, GE.MonsterArchetype.Rare,
        };

        [MenuItem("ARPG/Diagnostics/Monster Affix Distribution", false, 610)]
        public static void Run()
        {
            if (EditorApplication.isPlaying == false || AR.s == null || AR.s.Data == null)
            {
                Debug.LogError("[MonsterAffixDiagnostics] 플레이 모드에서 GameScene이 로드된 뒤 실행하세요.");
                return;
            }

            Random.State previousState = Random.state;
            try
            {
                Random.InitState(SampleSeed);
                StringBuilder report = new StringBuilder();
                report.AppendLine($"[MonsterAffixDiagnostics] 접사 테이블 {AR.s.Data.MonsterAffixes.Count}행");

                double[] meanCounts = new double[Archetypes.Length];
                AppendCountDistribution(report, meanCounts);
                AppendDropRolls(report, meanCounts);
                AppendSpeciesShortage(report);
                AppendDropDistribution(report);

                Debug.Log(report.ToString());
            }
            finally
            {
                Random.state = previousState;
            }
        }

        private static void AppendCountDistribution(StringBuilder report, double[] meanCounts)
        {
            report.AppendLine($"■ 등급별 접사 개수 분포 (판정 {CountSampleSize}회, 100마리 기준 %)");
            for (int a = 0; a < Archetypes.Length; a++)
            {
                MonsterArchetypeTable? table = AR.s.Data.GetMonsterArchetype(Archetypes[a]);
                if (table == null)
                {
                    report.AppendLine($"  {Archetypes[a]}: 등급 행 없음");
                    continue;
                }

                int[] histogram = new int[MonsterAffixComponent.MAX_AFFIXES + 1];
                long total = 0;
                for (int i = 0; i < CountSampleSize; i++)
                {
                    int count = MonsterAffixRoller.RollCount(table);
                    histogram[count]++;
                    total += count;
                }

                meanCounts[a] = (double)total / CountSampleSize;
                StringBuilder line = new StringBuilder();
                line.Append($"  {Archetypes[a]} (기본 {table.AffixBaseCount}, 추가 {table.AffixExtraChance}%): ");
                int fivePlus = 0;
                for (int c = 0; c < histogram.Length; c++)
                {
                    if (c >= 5)
                    {
                        fivePlus += histogram[c];
                        continue;
                    }

                    line.Append($"{c}개 {Percent(histogram[c], CountSampleSize)} / ");
                }

                line.Append($"5개+ {Percent(fivePlus, CountSampleSize)} / 10개 {Percent(histogram[MonsterAffixComponent.MAX_AFFIXES], CountSampleSize)}");
                line.Append($" | 평균 {meanCounts[a]:F3}");
                report.AppendLine(line.ToString());
            }

            report.AppendLine("  문서 §3 기대값: Normal 평균 0.11 / Magic 1.54 / Rare 2.00");
        }

        private static void AppendDropRolls(StringBuilder report, double[] meanCounts)
        {
            report.AppendLine("■ 드랍 판정 (§8)");
            double[] shares = { FieldNormalShare, FieldMagicShare, FieldRareShare };
            double fieldRolls = 0;
            for (int a = 0; a < Archetypes.Length; a++)
            {
                MonsterArchetypeTable? table = AR.s.Data.GetMonsterArchetype(Archetypes[a]);
                if (table == null)
                    continue;

                double rolls = Math.Max(1, table.DropRolls) + table.DropRollsPerAffix * meanCounts[a];
                fieldRolls += shares[a] * rolls;
                report.AppendLine($"  {Archetypes[a]}: 기본 {table.DropRolls} + 접사당 {table.DropRollsPerAffix} × {meanCounts[a]:F3} = 평균 {rolls:F3}회");
            }

            report.AppendLine($"  필드 마리당 평균 기본 판정 {fieldRolls:F3}회 (문서 1.68, 접사 도입 전 1.66)");

            DropTable? drop = AR.s.Data.GetDrop(FieldDropTableId);
            if (drop == null || fieldRolls <= 0)
                return;

            // 수량 방식 (ITEM_DROP_BALANCE_DESIGN §8.2): 판정 횟수 = 기본 판정 × (1 + Q/100). 꽝 칸은 줄이지 않는다.
            // 총량 유지 기준(R7)은 레벨 1~9 구간이다 — 그 구간의 장소 수량 보너스를 곱한 판정 수로 권장값을 계산한다.
            DropBandTable? baseBand = AR.s.Data.GetDropBand(1);
            int baseQuantity = baseBand != null ? baseBand.QuantityBonus : 0;
            double baseFieldRolls = fieldRolls * Math.Max(0d, 1d + baseQuantity / 100d);
            int otherRates = drop.CurrencyRate + drop.EquipmentRate + drop.SkillBookRate + drop.SkillPageRate;
            double perRoll = FieldItemsPerMonsterTarget / baseFieldRolls;
            double recommendedNothing = otherRates * (1.0 - perRoll) / perRoll;
            double currentPerRoll = otherRates / (double)Math.Max(1, otherRates + drop.NothingRate);
            report.AppendLine($"  ItemDrop {FieldDropTableId}: NothingRate 현재 {drop.NothingRate}, 레벨 1~9 총량 유지 권장 {recommendedNothing:F1} " +
                $"(나머지 칸 합 {otherRates}, 구간 수량 {baseQuantity}% → 판정 {baseFieldRolls:F3}회) → 현재 값의 마리당 평균 " +
                $"{baseFieldRolls * currentPerRoll:F3}개 (목표 {FieldItemsPerMonsterTarget})");
        }

        private static void AppendSpeciesShortage(StringBuilder report)
        {
            List<int> species = CollectSpawnableSpecies();
            report.AppendLine($"■ 종별 실제 부착 수 (종 {species.Count}개, 종당 {SpeciesSampleSize}회, 레벨 1·99) — 후보 부족 종만 표시");
            List<int> memberIds = new List<int>(1);
            int shortageCount = 0;
            for (int a = 0; a < Archetypes.Length; a++)
            {
                MonsterArchetypeTable? table = AR.s.Data.GetMonsterArchetype(Archetypes[a]);
                if (table == null)
                    continue;

                for (int s = 0; s < species.Count; s++)
                {
                    memberIds.Clear();
                    memberIds.Add(species[s]);
                    for (int levelIndex = 0; levelIndex < 2; levelIndex++)
                    {
                        int level = levelIndex == 0 ? 1 : 99;
                        long requested = 0;
                        long attached = 0;
                        for (int i = 0; i < SpeciesSampleSize; i++)
                        {
                            requested += MonsterAffixRoller.RollCount(table);
                            attached += MonsterAffixRoller.Roll(Archetypes[a], level, memberIds).Count;
                        }

                        double requestedMean = (double)requested / SpeciesSampleSize;
                        double attachedMean = (double)attached / SpeciesSampleSize;
                        // 두 값은 독립 표본이라 작은 차이는 표본 오차다. 5% 넘게 모자랄 때만 표시한다.
                        if (attachedMean < requestedMean * 0.95)
                        {
                            shortageCount++;
                            MonsterTable? monster = AR.s.Data.GetMonster(species[s]);
                            report.AppendLine($"  {Archetypes[a]} Lv{level} {species[s]}({(monster != null ? monster.Name : "?")}): " +
                                $"판정 평균 {requestedMean:F3} → 실제 {attachedMean:F3}");
                        }
                    }
                }
            }

            if (shortageCount == 0)
                report.AppendLine("  후보 부족 종 없음");
        }

        private static List<int> CollectSpawnableSpecies()
        {
            HashSet<int> unique = new HashSet<int>();
            List<int> result = new List<int>();
            AddSpecies(AR.s.Data.GetCommonMonsterIds(), unique, result);
            foreach (GE.BiomeType biome in Enum.GetValues(typeof(GE.BiomeType)))
            {
                AddSpecies(AR.s.Data.GetBiomeSpecificMonsterIds(biome), unique, result);
            }

            result.Sort();
            return result;
        }

        private static void AddSpecies(IReadOnlyList<int> ids, HashSet<int> unique, List<int> result)
        {
            for (int i = 0; i < ids.Count; i++)
            {
                if (unique.Add(ids[i]) == true)
                    result.Add(ids[i]);
            }
        }

        private static string Percent(int count, int total)
        {
            return $"{100.0 * count / total:F1}";
        }
    }
}
