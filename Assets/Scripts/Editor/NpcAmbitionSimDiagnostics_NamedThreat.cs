#nullable enable
using System;
using System.Collections.Generic;
using System.Text;
using ARPG.Component;
using ARPG.Data;
using ARPG.NamedThreat;
using ARPG.Npc;
using ARPG.Npc.Narrative;
using ARPG.Village;
using UnityEditor;
using UnityEngine;

namespace ARPG.EditorTools
{
    /// <summary>
    /// 네임드 위협 시나리오 (NAMED_THREAT_DESIGN §13 검증). Ambition Sim과 같은 복제·바꿔 끼우기 틀 안에서
    /// 네임드 기록(WorldData.NamedThreats)·narrative 원장·NamedThreatManager 런타임 필드도 복제본으로 바꿔 끼운다.
    /// - 일반 실행(Run): 라이브 네임드 기록의 복제본으로, 플레이어는 지금 위치에 머문 것으로 본다 (게임과 같은 마주침 판정).
    /// - 네임드 시나리오(RunNamedThreat): 빈 기록에서 시작하고, 플레이어가 72게임시간마다 가까운 다음 마을로 옮겨 가는 것을 흉내 낸다.
    /// 감독은 게임과 같은 NamedThreatManager.AdvanceDirector(live = false: 알림·저장 요청·엔티티 없음)를 시간마다 부른다.
    /// </summary>
    public static partial class NpcAmbitionSimDiagnostics
    {
        // 시나리오 플레이어가 한 마을에 머무는 시간 (게임시간)
        private const float NamedRouteStayHours = 72f;
        private static readonly float[] NamedRumorCheckpointHours = { 24f, 72f, 144f };

        [MenuItem("ARPG/Npc/Diagnostics/Ambition Sim/Named Threat (30 days)", false, 440)]
        public static void RunNamedThreat30Days() => RunNamedThreat(30);

        /// <summary>네임드 시나리오 실행. 요약 문자열을 돌려주고 콘솔에도 찍는다 (execute_code에서 직접 호출 가능).</summary>
        public static string RunNamedThreat(int days) => RunCore(days, false, true);

        private sealed class NamedThreatSimThreat
        {
            public int ThreatId;
            public int VillageId;
            public int TemplateId;
            public string Title = string.Empty;
            public float Appeared;
            public float Stage2 = -1f;
            public float Stage3 = -1f;
            public float Ended = -1f;
            public NamedThreatState EndState;
            public int LastStage = 1;
            public long Encounters;
            public long EncounterNpcs;
            public long Wounds;
            public long Severe;
            public readonly int[] RumorHolders = { -1, -1, -1 };
            public int MaxRumorHolders;
            public int VillageResidents;
        }

        private sealed class NamedThreatSimStats
        {
            public bool Scenario;
            public readonly List<int> Route = new();
            public int RouteIndex;
            public float NextMoveTime;
            public Vector2 PlayerPosition;
            public readonly Dictionary<int, NamedThreatSimThreat> Threats = new();
            public readonly List<int> ThreatOrder = new();
            public int MaxConcurrent;
            public int SameVillageViolations;
            public int IntervalViolations;
            public int DeathsFromThreat;
            public int VanishTimingViolations;
            public float LastAppear = -1f;
            public float LastKill = -1f;
            public readonly List<float> AppearGaps = new();
            public long Encounters, EncounterNpcs, Wounds, Severe, Flees;
            public readonly long[] EncountersByStage = new long[4];
            public readonly long[] EncounterNpcsByStage = new long[4];
            public readonly long[] WoundsByStage = new long[4];
            public readonly long[] SevereByStage = new long[4];
            // 시간 표본: 주 마을(단계별)·그 밖 마을의 부상 중 생존 NPC 비율
            public readonly double[] PrimaryWoundedSum = new double[4];
            public readonly long[] PrimaryWoundedSamples = new long[4];
            public double OtherWoundedSum;
            public long OtherWoundedSamples;
            public long ActiveThreatHours;
            public readonly long[] StageHours = new long[4];
            public readonly List<string> Failures = new();
        }

        // ================= 바꿔 끼우기 =================

        /// <summary>
        /// 네임드 기록·narrative 원장·NamedThreatManager 런타임 필드를 복제본으로 바꿔 끼운다 (라이브는 절대 바꾸지 않는다).
        /// narrative 원장은 네임드 출현이 부탁 인스턴스를 만들고 NPC 던전 완료가 소문 사건을 끝내므로 함께 복제한다.
        /// </summary>
        private static void SwapInNamedThreatClones(SimContext sim, List<FieldSwap> swaps, bool scenario)
        {
            object worldData = GetField(AR.s.Data, "_worldData").GetValue(AR.s.Data)!;
            NamedThreatWorldState named = scenario == true ? new NamedThreatWorldState() : CloneJson(AR.s.Data.NamedThreats);
            named.Sanitize();
            Swap(swaps, worldData, "NamedThreats", named);

            NarrativeSaveRoot narrative = CloneJson(AR.s.Data.Narrative);
            narrative.Instances ??= new List<NarrativeContentInstance>();
            narrative.Definitions ??= new List<NarrativeDefinitionSnapshot>();
            narrative.Policies ??= new List<NarrativePolicySnapshot>();
            narrative.Selections ??= new List<NarrativeSelectionRecord>();
            Swap(swaps, worldData, "Narrative", narrative);
            object npc = AR.s.Npc;
            var instances = new Dictionary<string, NarrativeContentInstance>();
            for (int i = 0; i < narrative.Instances.Count; i++) instances[narrative.Instances[i].InstanceId] = narrative.Instances[i];
            var definitions = new Dictionary<string, NarrativeDefinitionSnapshot>();
            for (int i = 0; i < narrative.Definitions.Count; i++) definitions[narrative.Definitions[i].Hash] = narrative.Definitions[i];
            var policies = new Dictionary<string, NarrativePolicySnapshot>();
            for (int i = 0; i < narrative.Policies.Count; i++) policies[narrative.Policies[i].Hash] = narrative.Policies[i];
            var selections = new Dictionary<string, NarrativeSelectionRecord>();
            for (int i = 0; i < narrative.Selections.Count; i++) selections[narrative.Selections[i].DungeonId] = narrative.Selections[i];
            Swap(swaps, npc, "_narrativeInstances", instances);
            Swap(swaps, npc, "_narrativeDefinitions", definitions);
            Swap(swaps, npc, "_narrativePolicies", policies);
            Swap(swaps, npc, "_narrativeSelections", selections);
            Swap(swaps, npc, "_narrativeRevision", GetField(npc, "_narrativeRevision").GetValue(npc));
            Swap(swaps, npc, "_savedNarrativeRevision", GetField(npc, "_savedNarrativeRevision").GetValue(npc));

            object manager = AR.s.NamedThreat;
            Swap(swaps, manager, "_entityByThreat", new Dictionary<int, int>());
            Swap(swaps, manager, "_spawningThreats", new HashSet<int>());
            Swap(swaps, manager, "_failedSpawnStep", new Dictionary<int, int>());
            Swap(swaps, manager, "_trailThreatId", 0);
            Swap(swaps, manager, "_recordSavePending", false);
            Swap(swaps, manager, "_worldSavePending", false);
            Swap(swaps, manager, "_killSavePending", false);

            sim.Named.Scenario = scenario;
            sim.Named.PlayerPosition = AR.s.Component.TryGetComponent<TransformComponent>(AR.s.Data.CurrentPlayerEntityId,
                out TransformComponent transform) == true ? transform.Position : Vector2.zero;
            if (scenario == true)
                BuildNamedRoute(sim);
        }

        // 지금 플레이어와 가장 가까운 마을에서 시작해 가장 가까운 아직 안 간 마을로 이어지는 경로 (주민이 있는 마을만)
        private static void BuildNamedRoute(SimContext sim)
        {
            NamedThreatSimStats stats = sim.Named;
            VillageData? current = NamedThreatManager.FindNearestVillage(stats.PlayerPosition);
            var visited = new HashSet<int>();
            while (current != null && stats.Route.Count < 64)
            {
                stats.Route.Add(current.VillageId);
                visited.Add(current.VillageId);
                VillageData? next = null;
                float best = float.MaxValue;
                for (int i = 0; i < sim.VillageList.Count; i++)
                {
                    VillageData candidate = sim.VillageList[i];
                    if (visited.Contains(candidate.VillageId) == true || CountLivingInVillage(sim, candidate) == 0)
                        continue;
                    float sqr = (candidate.Position - current.Position).sqrMagnitude;
                    if (sqr < best)
                    {
                        best = sqr;
                        next = candidate;
                    }
                }
                current = next;
            }
            if (stats.Route.Count > 0 && sim.Villages.TryGetValue(stats.Route[0], out VillageData? first) == true)
                stats.PlayerPosition = first.Position;
        }

        // ================= 시간마다 =================

        private static void AdvanceNamedThreats(SimContext sim, float now)
        {
            NamedThreatSimStats stats = sim.Named;
            if (stats.Scenario == true && now >= stats.NextMoveTime && stats.Route.Count > 0)
            {
                if (stats.NextMoveTime > 0f)
                    stats.RouteIndex = Math.Min(stats.RouteIndex + 1, stats.Route.Count - 1);
                if (sim.Villages.TryGetValue(stats.Route[stats.RouteIndex], out VillageData? village) == true)
                    stats.PlayerPosition = village.Position;
                stats.NextMoveTime = now + NamedRouteStayHours;
            }

            AR.s.NamedThreat.AdvanceDirector(now, stats.PlayerPosition, false);
            ObserveNamedThreats(sim, now);
        }

        private static void ObserveNamedThreats(SimContext sim, float now)
        {
            NamedThreatSimStats stats = sim.Named;
            NamedThreatWorldState state = AR.s.Data.NamedThreats;
            List<NamedThreatRecord> active = state.Active;
            stats.MaxConcurrent = Math.Max(stats.MaxConcurrent, active.Count);
            for (int i = 0; i < active.Count; i++)
            {
                NamedThreatRecord record = active[i];
                for (int j = i + 1; j < active.Count; j++)
                    if (active[j].PrimaryVillageId == record.PrimaryVillageId)
                        stats.SameVillageViolations++;
                if (stats.Threats.TryGetValue(record.ThreatId, out NamedThreatSimThreat? tracked) == false)
                {
                    tracked = new NamedThreatSimThreat
                    {
                        ThreatId = record.ThreatId, VillageId = record.PrimaryVillageId, TemplateId = record.TemplateId,
                        Title = record.Title, Appeared = record.AppearedGameTime,
                        VillageResidents = sim.Villages.TryGetValue(record.PrimaryVillageId, out VillageData? home) ? CountLivingInVillage(sim, home) : 0,
                    };
                    stats.Threats.Add(record.ThreatId, tracked);
                    stats.ThreatOrder.Add(record.ThreatId);
                    // 간격 (§4.1): 마지막 출현·처치 중 늦은 쪽 + 48게임시간
                    float reference = Mathf.Max(stats.LastAppear, stats.LastKill);
                    if (reference >= 0f)
                    {
                        stats.AppearGaps.Add(record.AppearedGameTime - reference);
                        if (record.AppearedGameTime - reference < NamedThreatRules.AppearIntervalHours - 0.01f)
                            stats.IntervalViolations++;
                    }
                    stats.LastAppear = record.AppearedGameTime;
                }
                if (record.Stage >= 2 && tracked.Stage2 < 0f) tracked.Stage2 = now;
                if (record.Stage >= 3 && tracked.Stage3 < 0f) tracked.Stage3 = now;
                tracked.LastStage = record.Stage;
                stats.ActiveThreatHours++;
                stats.StageHours[Mathf.Clamp(record.Stage, 1, 3)]++;

                float age = now - record.AppearedGameTime;
                for (int c = 0; c < NamedRumorCheckpointHours.Length; c++)
                    if (tracked.RumorHolders[c] < 0 && age >= NamedRumorCheckpointHours[c])
                        tracked.RumorHolders[c] = CountNamedRumorHolders(sim, record.ThreatId);
                if (age < 1.01f || (int)age % 24 == 0)
                    tracked.MaxRumorHolders = Math.Max(tracked.MaxRumorHolders, CountNamedRumorHolders(sim, record.ThreatId));
            }

            List<NamedThreatRecord> ended = state.Ended;
            for (int i = 0; i < ended.Count; i++)
            {
                NamedThreatRecord record = ended[i];
                if (stats.Threats.TryGetValue(record.ThreatId, out NamedThreatSimThreat? tracked) == false || tracked.Ended >= 0f)
                    continue;
                tracked.Ended = record.EndedGameTime;
                tracked.EndState = record.State;
                if (record.State == NamedThreatState.Slain)
                    stats.LastKill = Mathf.Max(stats.LastKill, record.EndedGameTime);
                // 사라짐 (§7.2): 출현 180게임시간 뒤, 1게임시간 해상도 안
                if (record.State == NamedThreatState.Vanished &&
                    Mathf.Abs(record.EndedGameTime - record.AppearedGameTime - NamedThreatRules.VanishAfterHours) > 1.01f)
                    stats.VanishTimingViolations++;
            }

            SampleNamedWounded(sim, active);
        }

        private static int CountNamedRumorHolders(SimContext sim, int threatId)
        {
            int holders = 0;
            for (int i = 0; i < sim.NpcIds.Count; i++)
            {
                NpcSaveData npc = sim.Npcs[sim.NpcIds[i]];
                if (npc.Condition == Creature.CharacterConditions.Dead)
                    continue;
                for (int r = 0; r < npc.Rumors.Count; r++)
                {
                    if (npc.Rumors[r].Kind == NpcRumorKind.NamedThreat && npc.Rumors[r].ThreatId == threatId)
                    {
                        holders++;
                        break;
                    }
                }
            }
            return holders;
        }

        // 주 마을(단계별)과 그 밖 마을의 부상 중 비율 (§13 부상 기준 — 2026-10-18 결정: 네임드로 늘어난 몫 +10%p 이하)
        private static void SampleNamedWounded(SimContext sim, List<NamedThreatRecord> active)
        {
            NamedThreatSimStats stats = sim.Named;
            int otherLiving = 0, otherWounded = 0;
            for (int v = 0; v < sim.VillageList.Count; v++)
            {
                VillageData village = sim.VillageList[v];
                int living = 0, wounded = 0;
                for (int n = 0; n < village.NpcEntityIds.Count; n++)
                {
                    if (sim.Npcs.TryGetValue(village.NpcEntityIds[n], out NpcSaveData? npc) == false ||
                        npc.Condition == Creature.CharacterConditions.Dead)
                        continue;
                    living++;
                    if (npc.Activity.Phase == NpcActivityPhase.Wounded)
                        wounded++;
                }
                if (living == 0)
                    continue;
                int stage = 0;
                for (int a = 0; a < active.Count; a++)
                    if (active[a].PrimaryVillageId == village.VillageId)
                        stage = active[a].Stage;
                if (stage > 0)
                {
                    stats.PrimaryWoundedSum[stage] += (double)wounded / living;
                    stats.PrimaryWoundedSamples[stage]++;
                }
                else
                {
                    otherLiving += living;
                    otherWounded += wounded;
                }
            }
            if (otherLiving > 0)
            {
                stats.OtherWoundedSum += (double)otherWounded / otherLiving;
                stats.OtherWoundedSamples++;
            }
        }

        // ================= 사냥 마주침 집계 (System_NpcSim과 같은 공용 메서드를 부른 뒤) =================

        private static void CountNamedEncounterEvent(SimContext sim, int threatId)
        {
            NamedThreatRecord? record = AR.s.NamedThreat.GetActive(threatId);
            int stage = record != null ? Mathf.Clamp(record.Stage, 1, 3) : 1;
            sim.Named.Encounters++;
            sim.Named.EncountersByStage[stage]++;
            if (sim.Named.Threats.TryGetValue(threatId, out NamedThreatSimThreat? tracked) == true)
                tracked.Encounters++;
        }

        private static void CountNamedEncounterResult(SimContext sim, int threatId, NpcHuntOutcome outcome, bool severe)
        {
            NamedThreatRecord? record = AR.s.NamedThreat.GetActive(threatId);
            int stage = record != null ? Mathf.Clamp(record.Stage, 1, 3) : 1;
            NamedThreatSimStats stats = sim.Named;
            stats.EncounterNpcs++;
            stats.EncounterNpcsByStage[stage]++;
            stats.Threats.TryGetValue(threatId, out NamedThreatSimThreat? tracked);
            if (tracked != null)
                tracked.EncounterNpcs++;
            if (outcome == NpcHuntOutcome.Dead)
            {
                stats.DeathsFromThreat++;
                return;
            }
            if (outcome != NpcHuntOutcome.Wounded)
            {
                stats.Flees++;
                return;
            }
            stats.Wounds++;
            stats.WoundsByStage[stage]++;
            if (tracked != null)
                tracked.Wounds++;
            if (severe == true)
            {
                stats.Severe++;
                stats.SevereByStage[stage]++;
                if (tracked != null)
                    tracked.Severe++;
            }
        }

        // ================= 소문 토픽 감사 (대화 토픽 검증의 네임드 부분) =================

        /// <summary>
        /// 네임드 소문 토픽의 사실 대조 (§8.3): 그 네임드가 활성이고, 칭호 공개 전에는 칭호를 말하지 않고 공개 뒤에는 말하며,
        /// 가까운 소문은 주 마을에서 영역을 본 방향, 먼 소문은 말하는 NPC 마을에서 주 마을을 본 방향을 말한다.
        /// </summary>
        private static void AuditNamedThreatTopic(SimContext sim, TopicAudit a, int npcId, NpcSaveData save, DialogueTopic topic, NpcRumor rumor)
        {
            a.NamedTopics++;
            string? failure = null;
            NamedThreatRecord? record = null;
            List<NamedThreatRecord> active = AR.s.Data.NamedThreats.Active;
            for (int i = 0; i < active.Count; i++)
                if (active[i].ThreatId == rumor.ThreatId)
                    record = active[i];
            if (record == null)
                failure = "활성이 아닌 네임드의 소문이 토픽이 됨";
            else
            {
                bool hasTitle = topic.BaseLine.Contains(record.Title);
                if (record.TitleRevealed == true && hasTitle == false)
                    failure = $"칭호 공개 뒤인데 문구에 칭호({record.Title}) 없음";
                else if (record.TitleRevealed == false && hasTitle == true)
                    failure = "칭호 공개 전인데 문구에 칭호가 있음";
                bool distant = rumor.IsDistant == true || save.VillageId != record.PrimaryVillageId;
                sim.Villages.TryGetValue(record.PrimaryVillageId, out VillageData? primary);
                sim.Villages.TryGetValue(save.VillageId, out VillageData? speaker);
                if (primary == null)
                    failure ??= "주 마을 없음";
                else if (distant == true)
                {
                    int bearing = speaker != null ? NamedThreatRules.GetBearing(speaker.Position, primary.Position) : -1;
                    if (bearing >= 0 && topic.BaseLine.Contains(NpcRumorPhrasing.BearingName(bearing) + "으로") == false)
                        failure ??= $"먼 소문 방향 불일치 (주 마을 기준 {NpcRumorPhrasing.BearingName(bearing)})";
                    if (topic.BaseLine.Contains("걸음 가면 있는 마을") == false)
                        failure ??= "먼 소문에 마을 거리 표현 없음";
                }
                else
                {
                    int bearing = NamedThreatRules.GetBearing(primary.Position, record.Territory);
                    if (bearing >= 0 && topic.BaseLine.Contains(NpcRumorPhrasing.BearingName(bearing) + "으로") == false)
                        failure ??= $"가까운 소문 방향 불일치 (영역 기준 {NpcRumorPhrasing.BearingName(bearing)})";
                    if (topic.BaseLine.Contains("사냥터") == false)
                        failure ??= "가까운 소문에 사냥터 표현 없음";
                }
            }
            if (failure == null)
                a.NamedOk++;
            else
            {
                a.NamedFail++;
                if (a.NamedFailures.Count < MaxExampleCases)
                    a.NamedFailures.Add($"npc={npcId}: {failure} | {topic.BaseLine}");
            }
            if (a.NamedExamples.Count < MaxTopicExamplesPerKind)
                a.NamedExamples.Add($"{NpcRumorPhrasing.NpcName(npcId)}(#{npcId}, 마을 {save.VillageId}, {(rumor.IsDistant == true ? "먼" : "가까운")} 소문): \"{topic.BaseLine}\"");
        }

        // ================= 요약 =================

        private static string BuildNamedThreatSummary(SimContext sim, int days)
        {
            NamedThreatSimStats stats = sim.Named;
            var sb = new StringBuilder();
            sb.AppendLine();
            sb.AppendLine($"==== 네임드 위협 (NAMED_THREAT_DESIGN §13{(stats.Scenario == true ? ", 시나리오: 플레이어가 72h마다 다음 마을" : ", 일반 실행: 플레이어 제자리")}) ====");
            if (stats.Scenario == true)
                sb.AppendLine($"경로 마을 {Math.Min(stats.Route.Count, stats.RouteIndex + 1)}곳 방문 (후보 {stats.Route.Count}곳)");
            sb.AppendLine($"출현 {stats.Threats.Count}마리 | 세계 동시 최대 {stats.MaxConcurrent}마리 | 한 마을 2마리 이상 {stats.SameVillageViolations}건 (0이어야 함) | " +
                          $"출현 간격 위반 {stats.IntervalViolations}건 (0이어야 함, 간격 {NamedThreatRules.AppearIntervalHours:F0}h)");
            if (stats.AppearGaps.Count > 0)
            {
                stats.AppearGaps.Sort();
                sb.AppendLine($"  출현 간격(마지막 출현·처치 기준): 최소 {stats.AppearGaps[0]:F1}h / 중앙 {stats.AppearGaps[stats.AppearGaps.Count / 2]:F1}h / " +
                              $"최대 {stats.AppearGaps[stats.AppearGaps.Count - 1]:F1}h");
            }
            sb.AppendLine($"네임드로 인한 사망 {stats.DeathsFromThreat}건 (0이어야 함) | 사라짐 시각 위반 {stats.VanishTimingViolations}건 (0이어야 함, 출현 {NamedThreatRules.VanishAfterHours:F0}h 뒤)");
            float activeDays = stats.ActiveThreatHours / 24f;
            sb.AppendLine($"마주침 {stats.Encounters}회(파티당 1회) · 마주친 NPC {stats.EncounterNpcs}명 · 부상 {stats.Wounds} (중상 {stats.Severe}, " +
                          $"{Pct(stats.Severe, Math.Max(1, stats.Wounds)):F1}%) · 도망 {stats.Flees} | 활성 네임드 {activeDays:F1}마리·일");
            for (int s = 1; s <= 3; s++)
            {
                float stageDays = stats.StageHours[s] / 24f;
                if (stageDays <= 0f)
                    continue;
                double primary = stats.PrimaryWoundedSamples[s] > 0 ? stats.PrimaryWoundedSum[s] / stats.PrimaryWoundedSamples[s] : 0d;
                sb.AppendLine($"  {s}단계: {stageDays:F1}마리·일 | 마을당 하루 마주침 {stats.EncountersByStage[s] / stageDays:F2}회 · 마주친 NPC " +
                              $"{stats.EncounterNpcsByStage[s] / stageDays:F2}명 · 부상 {stats.WoundsByStage[s] / stageDays:F2}건 (중상 " +
                              $"{stats.SevereByStage[s] / stageDays:F2}) | 주 마을 동시 부상 {primary * 100d:F1}%");
            }
            double other = stats.OtherWoundedSamples > 0 ? stats.OtherWoundedSum / stats.OtherWoundedSamples : 0d;
            sb.AppendLine($"  그 밖 마을 동시 부상 {other * 100d:F1}% (기준선). 네임드로 늘어난 몫은 주 마을 − 기준선 (§13 기준 +10%p 이하)");
            for (int s = 1; s <= 3; s++)
            {
                if (stats.PrimaryWoundedSamples[s] == 0)
                    continue;
                double primary = stats.PrimaryWoundedSum[s] / stats.PrimaryWoundedSamples[s];
                double extra = (primary - other) * 100d;
                sb.AppendLine($"  [{(extra <= 10d ? "PASS" : "FAIL")}] {s}단계 늘어난 몫 {extra:+0.0;-0.0}%p");
            }
            sb.AppendLine("네임드별 (출현 → 2단계 → 3단계 → 끝, 마주침/부상/중상, 소문 보유 24h/72h/144h/최대):");
            for (int i = 0; i < stats.ThreatOrder.Count; i++)
            {
                NamedThreatSimThreat t = stats.Threats[stats.ThreatOrder[i]];
                string stage2 = t.Stage2 >= 0f ? $"+{t.Stage2 - t.Appeared:F0}h" : "-";
                string stage3 = t.Stage3 >= 0f ? $"+{t.Stage3 - t.Appeared:F0}h" : "-";
                string end = t.Ended >= 0f ? $"{t.EndState} +{t.Ended - t.Appeared:F0}h" : "활성";
                sb.AppendLine($"  - #{t.ThreatId} {t.Title}(템플릿 {t.TemplateId}) 마을 {t.VillageId}(주민 {t.VillageResidents}) t={t.Appeared:F0} → {stage2} → {stage3} → {end} | " +
                              $"마주침 {t.Encounters}/{t.EncounterNpcs}명 부상 {t.Wounds} 중상 {t.Severe} | 소문 {t.RumorHolders[0]}/{t.RumorHolders[1]}/{t.RumorHolders[2]}/{t.MaxRumorHolders}");
            }
            TopicAudit topics = sim.Topics;
            sb.AppendLine($"[{(topics.NamedFail == 0 ? "PASS" : "FAIL")}] 네임드 소문 토픽 사실 대조 (활성·칭호 공개·방향): {topics.NamedOk}/{topics.NamedTopics}");
            for (int i = 0; i < topics.NamedFailures.Count; i++)
                sb.AppendLine("    ! " + topics.NamedFailures[i]);
            for (int i = 0; i < topics.NamedExamples.Count; i++)
                sb.AppendLine("    - " + topics.NamedExamples[i]);
            for (int i = 0; i < stats.Failures.Count; i++)
                sb.AppendLine("  [FAIL] " + stats.Failures[i]);
            return sb.ToString();
        }
    }
}
