#nullable enable
using System;
using System.Collections.Generic;
using System.Text;
using ARPG.Npc;
using ARPG.Tables;

namespace ARPG.EditorTools
{
    /// <summary>
    /// NPC 단기 목표 지표 (NPC_OBJECTIVE_DESIGN §12 진단 지표). Ambition Sim의 복제·바꿔 끼우기 틀 안에서
    /// 게임 쪽 카운터(NpcManager._objectiveCounters)를 복제 실행 전용 인스턴스로 바꿔 끼우고, 시간마다 목표 칸을 표본으로 잰다.
    /// 목표 판정은 게임과 같은 NpcManager 공용 메서드가 하고, 여기서는 세기만 한다.
    /// </summary>
    public static partial class NpcAmbitionSimDiagnostics
    {
        private const int ObjectiveKindCount = NpcObjectiveCounters.KindCount;
        // 활동 선택 인덱스: 0 Rest, 1 Hunt, 2 Trade, 3 DungeonDelve
        private const int ObjectiveActivityCount = 4;

        private sealed class ObjectiveSimStats
        {
            // 목표를 가진 NPC·시간 (계기 인덱스별)
            public readonly long[] OwnerHours = new long[ObjectiveKindCount];
            // 목표 주인의 활동 선택 수 [계기 × 4 + 활동]
            public readonly long[] OwnerSelect = new long[ObjectiveKindCount * ObjectiveActivityCount];
            // 목표 주인의 사망 (계기 인덱스별)
            public readonly long[] OwnerDeaths = new long[ObjectiveKindCount];
            // 마을당 동시 극적 목표 최대값과 그 마을
            public int MaxDramaticPerVillage;
            public int MaxDramaticVillageId = -1;
            public float MaxDramaticAt;
            // 같은 사망자를 쫓는 주인 수 최대값
            public int MaxOwnersPerSubject;
            public readonly Dictionary<int, int> VillageDramaticScratch = new();
            public readonly Dictionary<int, int> SubjectOwnersScratch = new();
            public readonly List<string> Failures = new();
            public readonly List<string> Examples = new();
        }

        /// <summary>
        /// 게임 쪽 목표 카운터·다시 계산 대기 목록을 복제 실행 전용 인스턴스로 바꿔 끼우고, 사건 시점 판정 = 다시 계산 대조(결정론)를 켠다.
        /// 끝나면 Swap 기록으로 되돌린다.
        /// </summary>
        private static void SwapInObjectiveClones(SimContext sim, List<FieldSwap> swaps)
        {
            Swap(swaps, AR.s.Npc, "_objectiveCounters", sim.ObjectiveCounters);
            Swap(swaps, AR.s.Npc, "_objectiveRecalcPending", new HashSet<int>());
            Swap(swaps, AR.s.Npc, "_objectiveDeterminismAudit", true);
        }

        /// <summary>목표 주인의 활동 선택 (목표 평가 직후의 칸 기준).</summary>
        private static void CountObjectiveOwnerSelection(SimContext sim, NpcSaveData saveData, NpcActivityKind kind)
        {
            int objectiveKind = GetObjectiveKindIndex(saveData.Objective);
            if (objectiveKind <= 0)
                return;
            int activity = kind switch
            {
                NpcActivityKind.Hunt => 1,
                NpcActivityKind.Trade => 2,
                NpcActivityKind.DungeonDelve => 3,
                _ => 0,
            };
            sim.Objectives.OwnerSelect[objectiveKind * ObjectiveActivityCount + activity]++;
        }

        /// <summary>목표 정의의 계기 인덱스. 표에 없으면 0.</summary>
        private static int GetObjectiveKindIndex(NpcObjective? objective)
        {
            if (objective == null)
                return 0;
            NpcObjectiveTable? definition = AR.s.Data.GetNpcObjective(objective.DefinitionId);
            return definition != null ? (int)definition.Trigger : 0;
        }

        private static string GetObjectiveKindName(int kind)
        {
            return AR.s.Data.TryGetNpcObjectiveByTrigger((NpcObjectiveTrigger)kind, out NpcObjectiveTable? definition) == true &&
                   definition != null
                ? definition.Name
                : ((NpcObjectiveTrigger)kind).ToString();
        }

        /// <summary>
        /// 시간마다 목표 칸 표본: 계기별 주인·시간, 마을당 동시 극적 목표 최대값, 같은 사망자를 쫓는 주인 수 최대값.
        /// </summary>
        private static void SampleObjectives(SimContext sim, float now)
        {
            ObjectiveSimStats stats = sim.Objectives;
            stats.VillageDramaticScratch.Clear();
            stats.SubjectOwnersScratch.Clear();
            for (int i = 0; i < sim.NpcIds.Count; i++)
            {
                if (sim.Npcs.TryGetValue(sim.NpcIds[i], out NpcSaveData? npc) == false || npc.Objective == null ||
                    npc.Condition == ARPG.Creature.CharacterConditions.Dead)
                    continue;
                NpcObjectiveTable? definition = AR.s.Data.GetNpcObjective(npc.Objective.DefinitionId);
                if (definition == null)
                    continue;
                stats.OwnerHours[(int)definition.Trigger]++;
                if (definition.Category != NpcObjectiveCategory.Dramatic)
                    continue;
                stats.VillageDramaticScratch.TryGetValue(npc.VillageId, out int villageCount);
                villageCount++;
                stats.VillageDramaticScratch[npc.VillageId] = villageCount;
                if (villageCount > stats.MaxDramaticPerVillage)
                {
                    stats.MaxDramaticPerVillage = villageCount;
                    stats.MaxDramaticVillageId = npc.VillageId;
                    stats.MaxDramaticAt = now;
                }
                if (npc.Objective.SubjectNpcId < 0)
                    continue;
                stats.SubjectOwnersScratch.TryGetValue(npc.Objective.SubjectNpcId, out int owners);
                owners++;
                stats.SubjectOwnersScratch[npc.Objective.SubjectNpcId] = owners;
                if (owners > stats.MaxOwnersPerSubject)
                    stats.MaxOwnersPerSubject = owners;
            }
        }

        // ================= 요약 =================

        private static string BuildObjectivesSummary(SimContext sim, int days)
        {
            NpcObjectiveCounters c = sim.ObjectiveCounters;
            ObjectiveSimStats stats = sim.Objectives;
            var sb = new StringBuilder();
            sb.AppendLine();
            sb.AppendLine($"==== 단기 목표 (NPC_OBJECTIVE_DESIGN §12, {days}일) ====");
            sb.AppendLine($"계기: 던전 원정 사망 {c.DungeonDeaths} · 던전 원정 중상 {c.DungeonSevereWounds} · 장비 마련 계기 거래 {c.UnaffordableUpgradeTrades} | " +
                          $"원수 갚기 판정 관계자 {c.RevengeJudgments}명 | 목표 원정 {c.ObjectiveDelves}회 | 연쇄 {c.ChainDeaths}건");
            for (int kind = 1; kind < ObjectiveKindCount; kind++)
            {
                string reaction = c.ReactRolls[kind] > 0
                    ? $" · 반응 {c.ReactPassed[kind]}/{c.ReactRolls[kind]} ({Pct(c.ReactPassed[kind], c.ReactRolls[kind]):F1}%)"
                    : string.Empty;
                sb.AppendLine($"  {GetObjectiveKindName(kind)}: 생성 {c.Created[kind]} (다시 계산 {c.Recalculated[kind]}) · 갱신 {c.Refreshed[kind]} · " +
                              $"순위에 밀려 버림 {c.RejectedByRank[kind]}{reaction} | 결말 이룸 {c.Fulfilled[kind]} (남이 {c.FulfilledByOther[kind]}) · " +
                              $"꺾임 {c.Abandoned[kind]} · 빠짐 {c.Dropped[kind]} · 주인 사망 {c.OwnerDied[kind]}");
            }
            var displaced = new StringBuilder();
            for (int newer = 1; newer < ObjectiveKindCount; newer++)
            {
                for (int older = 1; older < ObjectiveKindCount; older++)
                {
                    long count = c.Displaced[newer * ObjectiveKindCount + older];
                    if (count > 0)
                        displaced.Append($" {GetObjectiveKindName(newer)}→{GetObjectiveKindName(older)} {count}");
                }
            }
            sb.AppendLine($"밀어냄 (새 → 밀린):{(displaced.Length > 0 ? displaced.ToString() : " 없음")}");
            string maxVillage = stats.MaxDramaticVillageId >= 0
                ? $" (마을 {stats.MaxDramaticVillageId}, t={stats.MaxDramaticAt:F0})"
                : string.Empty;
            sb.AppendLine($"마을당 동시 극적 목표 최대 {stats.MaxDramaticPerVillage}{maxVillage} | 같은 사망자를 쫓는 주인 최대 {stats.MaxOwnersPerSubject}명");
            for (int kind = 1; kind < ObjectiveKindCount; kind++)
            {
                if (stats.OwnerHours[kind] <= 0)
                    continue;
                int b = kind * ObjectiveActivityCount;
                long selects = stats.OwnerSelect[b] + stats.OwnerSelect[b + 1] + stats.OwnerSelect[b + 2] + stats.OwnerSelect[b + 3];
                float ownerDays = stats.OwnerHours[kind] / 24f;
                sb.AppendLine($"  주인 {GetObjectiveKindName(kind)}: {ownerDays:F1}명·일 | 선택 Rest/Hunt/Trade/Delve " +
                              $"{Pct(stats.OwnerSelect[b], selects):F0}/{Pct(stats.OwnerSelect[b + 1], selects):F0}/" +
                              $"{Pct(stats.OwnerSelect[b + 2], selects):F0}/{Pct(stats.OwnerSelect[b + 3], selects):F0}% ({selects}회) | " +
                              $"사망 {stats.OwnerDeaths[kind]} (30명·일당 {(ownerDays > 0f ? stats.OwnerDeaths[kind] * 30f / ownerDays : 0f):F3})");
            }
            sb.AppendLine($"[{(c.DeterminismMismatches == 0 ? "PASS" : "FAIL")}] 사건 시점 판정 = 다시 계산 (결정론): 불일치 {c.DeterminismMismatches} / 대조 {c.DeterminismChecks}");
            for (int i = 0; i < stats.Examples.Count; i++)
                sb.AppendLine("    - " + stats.Examples[i]);
            for (int i = 0; i < stats.Failures.Count; i++)
                sb.AppendLine("  [FAIL] " + stats.Failures[i]);
            return sb.ToString();
        }
    }
}
