using System;
using System.Collections.Generic;
using ARPG.History;
using ARPG.Village;

namespace ARPG.Npc
{
    /// <summary>
    /// L2 — 마을 첫 population 직후 1회, 기존(이미 스폰된) NPC roster에 가족/직업/성격 관계 +
    /// 호감도 시드 + 연대기 기반 sentiment를 생성한다. (NpcManager.EnsureVillagePopulated에서 호출)
    /// 검증 프로토타입: Tools/ChronicleSimPrototype/NpcBackfill.cs (NPC 생성 부분 제외, 관계/감정만 포팅).
    /// 설계: NPC_RELATION_HISTORY_DESIGN.md §4.3
    ///
    /// 단순화: 런타임 마을 roster는 VillageTable.DefaultNpcIds 기반 단일 종족이라, 프로토타입의
    /// "점령 지배종족 NPC 분파(파벌 분할)"는 만들 NPC가 없어 생략하고, 피정복/왕국/전쟁 영향은
    /// 개인 sentiment로 표현한다. (지배종족 거주민 스폰은 R2 범위 밖)
    /// </summary>
    public static class NpcRelationBackfill
    {
        private sealed class Worker
        {
            public int EntityId;
            public NpcSaveData Save;
            public GlobalEnum.JobType Job;
        }

        private sealed class ChronicleContext
        {
            public GlobalEnum.RaceType NativeRace;
            public int KingdomId = -1;
            public bool ForeignRuled;            // 정복당해 타종족 지배 하
            public int ConquerorSettlementId = -1;
            public List<int> WarEnemySettlements = new();
        }

        /// <summary>마을 첫 population 직후 1회 호출. village.NpcEntityIds = 방금 스폰된 roster.</summary>
        public static void Generate(VillageData village, IReadOnlyList<int> npcEntityIds)
        {
            if (village == null || npcEntityIds == null || npcEntityIds.Count == 0)
                return;

            int mapSeed = AR.s.Map.mapSeed;
            int settlementId = village.SettlementId;
            // 마을 시드 — 연대기 무관 관계도 항상 결정론. settlementId 없으면 villageId로 대체.
            int seedKey = mapSeed ^ (settlementId >= 0 ? settlementId : (1_000_000 + village.VillageId));
            var rng = new HistoryRng(DerivedSeed.Derive(seedKey, DerivedSeed.ChannelRelationBackfill));

            // roster 수집 (스폰 순서 = 결정론)
            var npcs = new List<Worker>(npcEntityIds.Count);
            for (int i = 0; i < npcEntityIds.Count; i++)
            {
                int eid = npcEntityIds[i];
                NpcSaveData saveData = AR.s.Npc.GetSaveData(eid);
                if (saveData == null)
                    continue;
                AR.s.Npc.MarkSaveDirty(eid);
                npcs.Add(new Worker
                {
                    EntityId = eid,
                    Save = saveData,
                    Job = JobOf(saveData.NpcTableId),
                });
            }
            if (npcs.Count == 0)
                return;

            GenerateFamilies(npcs, rng);
            GenerateMentor(npcs);
            GeneratePersonality(npcs, rng);

            ChronicleContext ctx = ExtractContext(settlementId);
            if (ctx != null)
                GenerateSentiments(npcs, ctx, rng);
        }

        // ── ① 가구 분할 + ② 가족 관계 ──
        private static void GenerateFamilies(List<Worker> npcs, HistoryRng rng)
        {
            int pos = 0;
            while (pos < npcs.Count)
            {
                int size = Math.Min(rng.Range(1, 6), npcs.Count - pos);   // 1~5인 가구
                if (size >= 2)
                {
                    int pa = npcs[pos].EntityId;
                    int ma = npcs[pos + 1].EntityId;
                    AddRel(pa, ma, NpcRelationType.Spouse);

                    // 자녀 (가구 3번째부터)
                    for (int c = pos + 2; c < pos + size; c++)
                    {
                        int child = npcs[c].EntityId;
                        AddRel(pa, child, NpcRelationType.ParentOf);
                        AddRel(ma, child, NpcRelationType.ParentOf);
                    }
                    // 형제
                    for (int i = pos + 2; i < pos + size; i++)
                        for (int j = i + 1; j < pos + size; j++)
                            AddRel(npcs[i].EntityId, npcs[j].EntityId, NpcRelationType.Sibling);
                }
                pos += size;
            }
        }

        // ── ③ 직업 사제 (같은 직업 ≥2 → 선임→후임 MentorOf) ──
        private static void GenerateMentor(List<Worker> npcs)
        {
            var byJob = new Dictionary<GlobalEnum.JobType, List<int>>();
            for (int i = 0; i < npcs.Count; i++)
            {
                if (npcs[i].Job == GlobalEnum.JobType.None)
                    continue;
                if (byJob.TryGetValue(npcs[i].Job, out List<int> list) == false)
                    byJob[npcs[i].Job] = list = new List<int>();
                list.Add(npcs[i].EntityId);   // 스폰 순서 유지 = 결정론
            }

            foreach (var kvp in byJob)
            {
                List<int> g = kvp.Value;
                if (g.Count < 2)
                    continue;
                int leader = g[0];
                for (int i = 1; i < g.Count && i <= 3; i++)   // 사제 사슬은 적당히
                    AddRel(leader, g[i], NpcRelationType.MentorOf);
            }
        }

        // ── ④ 성격 기반 (Friendliness→Friend, Pride×동일직업→Rival) ──
        private static void GeneratePersonality(List<Worker> npcs, HistoryRng rng)
        {
            int n = npcs.Count;
            for (int i = 0; i < n; i++)
            {
                Worker a = npcs[i];
                int friendliness = a.Save.NpcStat.Friendliness;
                if (friendliness >= 60)
                {
                    int friends = friendliness >= 85 ? 2 : 1;
                    for (int f = 0; f < friends; f++)
                    {
                        int j = rng.Range(0, n);
                        if (j != i)
                            AddRel(a.EntityId, npcs[j].EntityId, NpcRelationType.Friend);
                    }
                }

                if (a.Save.NpcStat.Pride >= 70)
                {
                    for (int j = 0; j < n; j++)
                    {
                        if (j == i)
                            continue;
                        if (npcs[j].Job == a.Job && npcs[j].Save.NpcStat.Pride >= 55)
                        {
                            AddRel(a.EntityId, npcs[j].EntityId, NpcRelationType.Rival);
                            break;
                        }
                    }
                }
            }
        }

        // ── ⑤ 연대기 제약 → sentiment ──
        private static void GenerateSentiments(List<Worker> npcs, ChronicleContext ctx, HistoryRng rng)
        {
            for (int i = 0; i < npcs.Count; i++)
            {
                NpcSaveData s = npcs[i].Save;
                int loyalty = s.NpcStat.Loyalty;
                int courage = s.NpcStat.Courage;

                if (ctx.ForeignRuled)
                {
                    // 피정복: Loyalty 낮을수록 지배 왕국에 반감
                    if (loyalty < 70)
                        s.Sentiments.Add(new NpcSentiment(NpcSentimentType.ResentsKingdom, ctx.ConquerorSettlementId));
                    else
                        s.Sentiments.Add(new NpcSentiment(NpcSentimentType.ProudOfKingdom));   // 체념·순응

                    // 일부는 정복 전쟁 유족
                    if (ctx.ConquerorSettlementId >= 0 && courage >= 55 && rng.Chance(0.35))
                        s.Sentiments.Add(new NpcSentiment(NpcSentimentType.LostFamilyToWar, ctx.ConquerorSettlementId));
                }
                else if (ctx.KingdomId != -1)
                {
                    // 자국(동족) 왕국 — 충성 높으면 자부심
                    if (loyalty >= 55)
                        s.Sentiments.Add(new NpcSentiment(NpcSentimentType.ProudOfKingdom));
                }

                // 전쟁 이력(정복 외) 유족
                if (ctx.WarEnemySettlements.Count > 0 && rng.Chance(0.18))
                {
                    int enemy = ctx.WarEnemySettlements[rng.Range(0, ctx.WarEnemySettlements.Count)];
                    if (enemy != ctx.ConquerorSettlementId)
                        s.Sentiments.Add(new NpcSentiment(NpcSentimentType.LostFamilyToWar, enemy));
                }
            }
        }

        // ── 연대기 context 추출 (WorldData.Chronicle, settlementId 기준) ──
        private static ChronicleContext ExtractContext(int settlementId)
        {
            if (settlementId < 0)
                return null;
            ChronicleSaveData chronicle = AR.s.Data.Chronicle;
            if (chronicle == null || chronicle.Events == null)
                return null;

            SettlementSnapshot self = null;
            for (int i = 0; i < chronicle.Settlements.Count; i++)
            {
                if (chronicle.Settlements[i].SettlementId == settlementId)
                {
                    self = chronicle.Settlements[i];
                    break;
                }
            }
            if (self == null)
                return null;

            var ctx = new ChronicleContext
            {
                NativeRace = self.Race,
                KingdomId = self.KingdomId,
            };

            if (self.Fate == SettlementFate.Conquered && self.KingdomId >= 0)
            {
                KingdomData k = FindKingdom(chronicle, self.KingdomId);
                if (k != null && k.FoundingRace != self.Race)
                    ctx.ForeignRuled = true;
            }

            for (int i = 0; i < chronicle.Events.Count; i++)
            {
                ChronicleEvent e = chronicle.Events[i];
                if (e.Type == ChronicleEventType.Conquer && e.SubjectSettlementId == settlementId)
                    ctx.ConquerorSettlementId = e.ObjectSettlementId;          // 나를 정복한 마을(승자)
                else if (e.Type == ChronicleEventType.War && e.SubjectSettlementId == settlementId)
                    ctx.WarEnemySettlements.Add(e.ObjectSettlementId);
                else if (e.Type == ChronicleEventType.War && e.ObjectSettlementId == settlementId)
                    ctx.WarEnemySettlements.Add(e.SubjectSettlementId);
            }

            return ctx;
        }

        private static KingdomData FindKingdom(ChronicleSaveData chronicle, int kingdomId)
        {
            for (int i = 0; i < chronicle.Kingdoms.Count; i++)
                if (chronicle.Kingdoms[i].KingdomId == kingdomId)
                    return chronicle.Kingdoms[i];
            return null;
        }

        // ── 헬퍼 ──
        private static GlobalEnum.JobType JobOf(int npcTableId)
        {
            Tables.NpcTable table = AR.s.Data.GetNpc(npcTableId);
            return table != null ? table.JobType : GlobalEnum.JobType.None;
        }

        /// <summary>관계 추가 + 관계 유형에 따른 초기 호감도 시드 (설계 §4.3 step6).</summary>
        private static void AddRel(int from, int to, NpcRelationType type)
        {
            AR.s.Npc.AddRelation(from, to, type);
            AR.s.Npc.SetAffinity(from, to, AffinityFor(type));
        }

        private static int AffinityFor(NpcRelationType type) => type switch
        {
            NpcRelationType.Spouse => 60,
            NpcRelationType.ParentOf => 50,
            NpcRelationType.Sibling => 40,
            NpcRelationType.Friend => 30,
            NpcRelationType.MentorOf => 20,
            NpcRelationType.Rival => -20,
            NpcRelationType.GrudgeAgainst => -50,
            _ => 0,
        };
    }
}
