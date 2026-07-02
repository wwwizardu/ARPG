#nullable enable
using System.Collections.Generic;
using UnityEngine;
using ARPG.History;
using ARPG.Village;

namespace ARPG.Npc.Narrative
{
    /// <summary>
    /// 상태·백본 → 적용 가능한 대화 토픽(니즈·소문·잡담) 산출 + 선택.
    /// 선택은 셔플백(중복 회피) + 긴급(기아·위협) 새치기 — 대화할 때마다 골고루 돌면서 급한 건 놓치지 않는다.
    /// 니즈는 "표현만"(충족/보상 루프 없음). 소문은 C-method(진짜 사실 → 소문 말투).
    /// 설계: NARRATIVE_ENGINE_SPIKE §4.5
    /// </summary>
    public static class DialogueTopicSelector
    {
        private const float ShortageRatio = 0.25f;
        private const int DefaultResourceCap = 50;

        // 마을 자원(식량 제외 — Food는 기아 니즈로 별도 처리)
        private static readonly GlobalEnum.ItemType[] ResourceTypes =
        {
            GlobalEnum.ItemType.Wood,
            GlobalEnum.ItemType.Stone,
            GlobalEnum.ItemType.Copper,
            GlobalEnum.ItemType.Iron,
            GlobalEnum.ItemType.Herb,
        };

        // NPC별 최근 말한 토픽 (셔플백: 소진 시 리셋)
        private static readonly Dictionary<int, HashSet<DialogueTopicType>> _recent = new();

        // ── 산출 ──
        public static List<DialogueTopic> Collect(int npcId, NpcDialogueContext ctx, ThreatSeed threat)
        {
            List<DialogueTopic> topics = new();
            NpcSaveData? save = AR.s.Npc?.GetSaveData(npcId);
            VillageData? village = save != null ? AR.s.Village?.GetVillage(save.VillageId) : null;
            ChronicleSaveData? chronicle = AR.s.Data?.Chronicle;

            // 중립 폴백
            topics.Add(new DialogueTopic
            {
                Type = DialogueTopicType.Greeting,
                Category = DialogueTopicCategory.Neutral,
                MinTier = AffinityTier.Stranger,
                BaseLine = $"{ctx.NpcName}이오. 무슨 일이시오?",
            });

            AddNeeds(topics, save, village, threat);
            AddRumors(topics, npcId, chronicle);
            AddFlavor(topics, save);

            return topics;
        }

        // ── 선택: 레퍼토리 캡(NPC별 1~3) + 그 안에서 셔플백(긴급 새치기) ──
        public static DialogueTopic Select(int npcId, List<DialogueTopic> topics, AffinityTier tier)
        {
            // 티어 통과 토픽 수집 (Greeting은 폴백이라 캡에서 분리)
            List<DialogueTopic> applicable = new();
            DialogueTopic? greeting = null;
            for (int i = 0; i < topics.Count; i++)
            {
                if (topics[i].MinTier > tier)
                    continue;
                if (topics[i].Type == DialogueTopicType.Greeting)
                {
                    greeting = topics[i];
                    continue;
                }
                applicable.Add(topics[i]);
            }

            // NPC별 소수 화제(1~3)로 캡 — 캐릭터가 늘 하는 얘기만 돌게
            List<DialogueTopic> repertoire = BuildRepertoire(npcId, applicable);
            if (repertoire.Count == 0)
                return greeting ?? GreetingFallback();

            HashSet<DialogueTopicType> recent = GetRecent(npcId);

            // 1) 긴급 & 최근 안 함 → 2) 최근 안 한 것 → 3) 소진 시 리셋 후 재선택
            DialogueTopic? pick = PickRandom(repertoire, recent, urgentOnly: true);
            if (pick == null)
                pick = PickRandom(repertoire, recent, urgentOnly: false);
            if (pick == null)
            {
                recent.Clear();
                pick = PickRandom(repertoire, recent, urgentOnly: true)
                       ?? PickRandom(repertoire, recent, urgentOnly: false);
            }
            if (pick == null)
                pick = repertoire[0];

            recent.Add(pick.Type);
            return pick;
        }

        /// <summary>NPC별 화제 레퍼토리(1~3) — 활성 긴급 니즈 우선 포함, 나머지는 우선순위+NPC별 안정 tiebreak.</summary>
        private static List<DialogueTopic> BuildRepertoire(int npcId, List<DialogueTopic> applicable)
        {
            int cap = RepertoireSize(npcId);
            List<DialogueTopic> result = new();

            // 1) 활성 긴급 니즈(위협·기아)는 급하니 자동 포함
            for (int i = 0; i < applicable.Count && result.Count < cap; i++)
                if (applicable[i].Urgent)
                    result.Add(applicable[i]);

            // 2) 나머지: 우선순위(Need>Rumor>Flavor) + npcId 안정 tiebreak로 채움
            List<DialogueTopic> rest = new();
            for (int i = 0; i < applicable.Count; i++)
                if (applicable[i].Urgent == false)
                    rest.Add(applicable[i]);
            rest.Sort((a, b) => SortKey(npcId, b).CompareTo(SortKey(npcId, a)));
            for (int i = 0; i < rest.Count && result.Count < cap; i++)
                result.Add(rest[i]);

            return result;
        }

        // npcId 시드로 안정적인 1~3 (2에 치우침: 25/50/25)
        private static int RepertoireSize(int npcId)
        {
            int r = StableHash(npcId, 777) % 100;
            if (r < 25) return 1;
            if (r < 75) return 2;
            return 3;
        }

        // 우선순위(Need>Rumor>Flavor)*1000 + NPC별 토픽 안정 해시 → NPC마다 다른 화제 선호, 같은 NPC는 일관
        private static int SortKey(int npcId, DialogueTopic t)
        {
            int pri = t.Category switch
            {
                DialogueTopicCategory.Need => 3,
                DialogueTopicCategory.Rumor => 2,
                DialogueTopicCategory.Flavor => 1,
                _ => 0,
            };
            return pri * 1000 + (StableHash(npcId, (int)t.Type) % 1000);
        }

        private static int StableHash(int a, int b)
        {
            unchecked
            {
                int h = 17;
                h = h * 31 + a;
                h = h * 31 + b;
                h ^= h >> 15;
                return h & 0x7fffffff;
            }
        }

        // ── 니즈 ──
        private static void AddNeeds(List<DialogueTopic> topics, NpcSaveData? save, VillageData? village, ThreatSeed threat)
        {
            // ClearThreat (긴급)
            bool threatOn = threat.NearbyMonsters || (village != null && village.ThreatLevel > 0f);
            if (threatOn)
            {
                DialogueTopic t = new()
                {
                    Type = DialogueTopicType.ClearThreat,
                    Category = DialogueTopicCategory.Need,
                    MinTier = AffinityTier.Stranger,
                    Urgent = true,
                    BaseLine = "요즘 마을 밖이 흉흉하오. 몬스터 탓에 나다니기가 무섭소.",
                };
                t.Facts.Add(string.IsNullOrEmpty(threat.Note) == false
                    ? threat.Note
                    : "마을 근처에 몬스터가 출몰해 마을 밖이 위험하다.");
                topics.Add(t);
            }

            // Food (긴급) — 기아
            if (village != null && village.HungerHoursAccumulated > 0)
            {
                DialogueTopic t = new()
                {
                    Type = DialogueTopicType.Food,
                    Category = DialogueTopicCategory.Need,
                    MinTier = AffinityTier.Stranger,
                    Urgent = true,
                    BaseLine = "곳간이 비어가오. 다들 굶주리고 있소.",
                };
                t.Facts.Add($"마을이 {village.HungerHoursAccumulated}시간째 식량 부족 상태다.");
                topics.Add(t);
            }

            // ResourceShortage (식량 제외)
            if (village != null && TryFindShortage(village, out GlobalEnum.ItemType lowType, out int lowAmt))
            {
                string res = ResourceName(lowType);
                DialogueTopic t = new()
                {
                    Type = DialogueTopicType.ResourceShortage,
                    Category = DialogueTopicCategory.Need,
                    MinTier = AffinityTier.Acquaintance,
                    BaseLine = $"{res}이(가) 모자라 큰일이오.",
                };
                t.Facts.Add($"마을에 {res}이(가) 부족하다(현재 {lowAmt}).");
                topics.Add(t);
            }

            if (save == null)
                return;

            // CraftMaterial (직업 재료)
            string? material = CraftMaterialFor(save.JobType);
            if (material != null)
            {
                DialogueTopic t = new()
                {
                    Type = DialogueTopicType.CraftMaterial,
                    Category = DialogueTopicCategory.Need,
                    MinTier = AffinityTier.Stranger,
                    BaseLine = $"{material}만 넉넉하면 일이 수월할 텐데.",
                };
                t.Facts.Add($"직업: {save.JobType} — {material}이(가) 아쉽다.");
                topics.Add(t);
            }

            // Medicine — 부상/이상 상태
            if (save.Condition != CharacterConditions.Normal)
            {
                DialogueTopic t = new()
                {
                    Type = DialogueTopicType.Medicine,
                    Category = DialogueTopicCategory.Need,
                    MinTier = AffinityTier.Stranger,
                    BaseLine = "몸이 영 성치 않소. 약이라도 있으면 좋으련만.",
                };
                t.Facts.Add($"몸 상태: {save.Condition}");
                topics.Add(t);
            }
        }

        // ── 소문 (C-method: 진짜 사실을 소문 말투로) ──
        private static void AddRumors(List<DialogueTopic> topics, int npcId, ChronicleSaveData? chronicle)
        {
            if (chronicle != null && chronicle.Events.Count > 0)
            {
                // HistoryRumor — 주요 사건 하나
                List<ChronicleEvent> notable = new();
                for (int i = 0; i < chronicle.Events.Count; i++)
                    if (IsNotable(chronicle.Events[i].Type))
                        notable.Add(chronicle.Events[i]);
                if (notable.Count > 0)
                {
                    ChronicleEvent e = notable[Random.Range(0, notable.Count)];
                    DialogueTopic t = new()
                    {
                        Type = DialogueTopicType.HistoryRumor,
                        Category = DialogueTopicCategory.Rumor,
                        MinTier = AffinityTier.Stranger,
                        BaseLine = "들었소? 오래전 이 근방에서 큰일이 있었다더군.",
                    };
                    t.Facts.Add(FlattenEvent(e));
                    topics.Add(t);
                }
            }

            if (chronicle != null && chronicle.Settlements.Count > 0)
            {
                // SettlementRumor / DangerRumor — 몰락한 정착지
                List<SettlementSnapshot> fallen = new();
                for (int i = 0; i < chronicle.Settlements.Count; i++)
                    if (chronicle.Settlements[i].Fate != SettlementFate.Alive)
                        fallen.Add(chronicle.Settlements[i]);
                if (fallen.Count > 0)
                {
                    SettlementSnapshot s = fallen[Random.Range(0, fallen.Count)];

                    DialogueTopic rumor = new()
                    {
                        Type = DialogueTopicType.SettlementRumor,
                        Category = DialogueTopicCategory.Rumor,
                        MinTier = AffinityTier.Stranger,
                        BaseLine = "어느 마을은 잿더미가 됐다더군. 세상이 흉흉하오.",
                    };
                    rumor.Facts.Add($"정착지#{s.SettlementId}({s.Race}) — 운명 {s.Fate}");
                    topics.Add(rumor);

                    DialogueTopic danger = new()
                    {
                        Type = DialogueTopicType.DangerRumor,
                        Category = DialogueTopicCategory.Rumor,
                        MinTier = AffinityTier.Stranger,
                        BaseLine = "옛 폐허엔 함부로 손대지 않는 게 좋다더군.",
                    };
                    danger.Facts.Add($"옛 {s.Race} 정착지(#{s.SettlementId})가 {s.Fate} 상태로 남아 있다.");
                    topics.Add(danger);
                }
            }

            // Gossip — 자기 관계(라이벌/친구)
            IReadOnlyList<NpcRelationEntry>? relations = AR.s.Npc?.GetRelations(npcId);
            if (relations != null)
            {
                for (int i = 0; i < relations.Count; i++)
                {
                    NpcRelationEntry r = relations[i];
                    if (r.Type != NpcRelationType.Rival && r.Type != NpcRelationType.Friend)
                        continue;
                    int otherId = r.FromEntityId == npcId ? r.ToEntityId : r.FromEntityId;
                    string name = ResolveNpcName(otherId);
                    string label = r.Type == NpcRelationType.Rival ? "라이벌" : "오랜 친구";
                    DialogueTopic t = new()
                    {
                        Type = DialogueTopicType.Gossip,
                        Category = DialogueTopicCategory.Rumor,
                        MinTier = AffinityTier.Acquaintance,
                        BaseLine = $"{name}과는 사연이 좀 있소.",
                    };
                    t.Facts.Add($"{name}은(는) 이 NPC의 {label}이다.");
                    topics.Add(t);
                    break;   // 하나면 충분
                }
            }
        }

        // ── 잡담 ──
        private static void AddFlavor(List<DialogueTopic> topics, NpcSaveData? save)
        {
            topics.Add(new DialogueTopic
            {
                Type = DialogueTopicType.SmallTalk,
                Category = DialogueTopicCategory.Flavor,
                MinTier = AffinityTier.Stranger,
                BaseLine = "요즘은 그럭저럭 지낼 만하오.",
            });

            if (save != null && save.JobType != GlobalEnum.JobType.None)
            {
                DialogueTopic t = new()
                {
                    Type = DialogueTopicType.JobMood,
                    Category = DialogueTopicCategory.Flavor,
                    MinTier = AffinityTier.Stranger,
                    BaseLine = JobMoodLine(save.JobType),
                };
                t.Facts.Add($"직업: {save.JobType}");
                topics.Add(t);
            }
        }

        // ── 헬퍼 ──
        private static DialogueTopic? PickRandom(List<DialogueTopic> pool, HashSet<DialogueTopicType> recent, bool urgentOnly)
        {
            List<DialogueTopic> cands = new();
            for (int i = 0; i < pool.Count; i++)
            {
                if (urgentOnly && pool[i].Urgent == false)
                    continue;
                if (recent.Contains(pool[i].Type))
                    continue;
                cands.Add(pool[i]);
            }
            if (cands.Count == 0)
                return null;
            return cands[Random.Range(0, cands.Count)];
        }

        private static HashSet<DialogueTopicType> GetRecent(int npcId)
        {
            if (_recent.TryGetValue(npcId, out HashSet<DialogueTopicType> set) == false)
            {
                set = new HashSet<DialogueTopicType>();
                _recent[npcId] = set;
            }
            return set;
        }

        private static DialogueTopic GreetingFallback() => new()
        {
            Type = DialogueTopicType.Greeting,
            Category = DialogueTopicCategory.Neutral,
            MinTier = AffinityTier.Stranger,
            BaseLine = "무슨 일이시오?",
        };

        private static bool TryFindShortage(VillageData v, out GlobalEnum.ItemType type, out int amount)
        {
            type = GlobalEnum.ItemType.Wood;
            amount = 0;
            bool found = false;
            float lowest = float.MaxValue;
            for (int i = 0; i < ResourceTypes.Length; i++)
            {
                GlobalEnum.ItemType rt = ResourceTypes[i];
                if (v.Resources.TryGetValue(rt, out int amt) == false)
                    continue;
                int cap = v.ResourceCaps.TryGetValue(rt, out int c) && c > 0 ? c : DefaultResourceCap;
                float ratio = cap > 0 ? (float)amt / cap : 1f;
                if (ratio < ShortageRatio && ratio < lowest)
                {
                    lowest = ratio;
                    type = rt;
                    amount = amt;
                    found = true;
                }
            }
            return found;
        }

        private static bool IsNotable(ChronicleEventType t) =>
            t == ChronicleEventType.War || t == ChronicleEventType.Conquer ||
            t == ChronicleEventType.Ruin || t == ChronicleEventType.Alliance ||
            t == ChronicleEventType.Merge;

        private static string FlattenEvent(ChronicleEvent e)
        {
            string obj = e.ObjectSettlementId >= 0 ? $" → 정착지#{e.ObjectSettlementId}" : string.Empty;
            string kingdom = e.KingdomId >= 0 ? $" (왕국#{e.KingdomId})" : string.Empty;
            return $"{e.Year}년 {e.Type} — 정착지#{e.SubjectSettlementId}{obj}{kingdom}";
        }

        private static string ResolveNpcName(int otherId)
        {
            NpcSaveData? o = AR.s.Npc?.GetSaveData(otherId);
            if (o == null)
                return $"NPC#{otherId}";
            Tables.NpcTable? t = AR.s.Data?.GetNpc(o.NpcTableId);
            return t != null ? t.Name : $"NPC#{otherId}";
        }

        private static string? CraftMaterialFor(GlobalEnum.JobType job) => job switch
        {
            GlobalEnum.JobType.Blacksmith => "좋은 광석과 철",
            GlobalEnum.JobType.Miner => "쓸 만한 곡괭이",
            GlobalEnum.JobType.Woodcutter => "잘 벼른 도끼",
            GlobalEnum.JobType.Farmer => "튼실한 씨앗",
            GlobalEnum.JobType.Hunter => "질 좋은 화살",
            _ => null,
        };

        private static string ResourceName(GlobalEnum.ItemType t) => t switch
        {
            GlobalEnum.ItemType.Food => "식량",
            GlobalEnum.ItemType.Wood => "목재",
            GlobalEnum.ItemType.Stone => "석재",
            GlobalEnum.ItemType.Copper => "구리",
            GlobalEnum.ItemType.Iron => "철",
            GlobalEnum.ItemType.Herb => "약초",
            _ => t.ToString(),
        };

        private static string JobMoodLine(GlobalEnum.JobType job) => job switch
        {
            GlobalEnum.JobType.Farmer => "밭일이 끝이 없구려. 그래도 흙 만지는 게 낫소.",
            GlobalEnum.JobType.Blacksmith => "쇠 두드리는 소리가 곧 내 낙이오.",
            GlobalEnum.JobType.Merchant => "물건 보는 눈은 자신 있소.",
            GlobalEnum.JobType.Hunter => "사냥감을 쫓다 보면 하루가 짧소.",
            GlobalEnum.JobType.Builder => "손볼 데가 한둘이 아니오.",
            GlobalEnum.JobType.Scholar => "오래된 것들엔 다 사연이 있는 법이오.",
            GlobalEnum.JobType.Guard => "마을은 내가 지키오. 걱정 마시오.",
            GlobalEnum.JobType.Chief => "마을 살림이 쉽지만은 않구려.",
            GlobalEnum.JobType.Woodcutter => "나무 베는 소리가 곧 밥벌이요.",
            GlobalEnum.JobType.Miner => "곡괭이질에 이력이 났소.",
            GlobalEnum.JobType.Gatherer => "이것저것 주워 오는 게 내 일이오.",
            _ => "그럭저럭 지내오.",
        };
    }
}
