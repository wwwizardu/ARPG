#nullable enable
using System.Collections.Generic;
using UnityEngine;
using ARPG.Creature;
using ARPG.Data;
using ARPG.History;
using ARPG.Village;

namespace ARPG.Npc.Narrative
{
    /// <summary>
    /// 상태·백본 → 적용 가능한 대화 토픽(니즈·소문·잡담·사연) 산출 + 선택.
    /// 선택은 관계별 레퍼토리 + 성격 편향 + 셔플백(중복 회피) + 실제 긴급 니즈 새치기다.
    /// 니즈는 "표현만"(충족/보상 루프 없음). 소문은 C-method(진짜 사실 → 소문 말투).
    /// 사연(Personal)은 개인 전기 Deeds·사별 기억 접지 — 난수 없이 최신순(결정론).
    /// 설계: NARRATIVE_ENGINE_SPIKE §4.5 + NARRATIVE_DIALOGUE_VARIETY §5 Phase 1
    /// </summary>
    public static class DialogueTopicSelector
    {
        private const float ShortageRatio = 0.25f;
        private const int DefaultResourceCap = 50;
        private const float ImmediateRepeatHours = 1f;
        private const float LongAbsentHours = 48f;
        // 야망 소문 토픽(DungeonRumor·ItemOwnerRumor)의 레퍼토리 우선순위 (P2c). 낮은 관계 단계의 잡담(Flavor)과 같다.
        private const int AmbitionRumorPriority = 4;

        private enum GreetingEncounter
        {
            FirstMeeting,
            FamiliarFace,
            Regular,
            LongAbsent,
            ImmediateRepeat,
        }

        private enum GreetingTone
        {
            Friendly,
            Direct,
            Greedy,
            Loyal,
            Courageous,
            Curious,
            Proud,
            Patient,
            Guarded,
        }

        // 마을 자원(식량 제외 — Food는 기아 니즈로 별도 처리)
        private static readonly GlobalEnum.ItemType[] ResourceTypes =
        {
            GlobalEnum.ItemType.Wood,
            GlobalEnum.ItemType.Stone,
            GlobalEnum.ItemType.Copper,
            GlobalEnum.ItemType.Iron,
            GlobalEnum.ItemType.Herb,
        };

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
            AddAmbitionRumors(topics, npcId, save, false);
            AddFlavor(topics, save);
            AddPersonal(topics, npcId, save);

            return topics;
        }

        /// <summary>
        /// 진단·검증용 (NPC_AMBITION P2c): 말하는 NPC가 가진 유효한 야망 소문 전부를 토픽 후보로 만든다 (대화 때는 종류마다 1개만 뽑힌다).
        /// 실제 대화와 같은 코드(AddAmbitionRumors)로 문구·검증을 만든다. 만든 후보는 소문 노출로 센다.
        /// </summary>
        public static void CollectAmbitionRumorTopics(int npcId, List<DialogueTopic> into)
        {
            NpcSaveData? save = AR.s.Npc?.GetSaveData(npcId);
            AddAmbitionRumors(into, npcId, save, true);
        }

        // ── 선택: 레퍼토리 캡(NPC별 1~3) + 그 안에서 셔플백(긴급 새치기) ──
        public static DialogueTopic Select(int npcId, List<DialogueTopic> topics, AffinityTier tier, HashSet<DialogueTopicType> recent)
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

            // 관계 단계에 따른 NPC별 화제 레퍼토리(2~5) 안에서 순환
            List<DialogueTopic> repertoire = BuildRepertoire(npcId, applicable, tier);
            if (repertoire.Count == 0)
                return greeting ?? GreetingFallback();

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

        /// <summary>호감도 수치를 대화 공개 단계로 변환한다.</summary>
        public static AffinityTier GetAffinityTier(int affinity) => affinity switch
        {
            < 20 => AffinityTier.Stranger,
            < 50 => AffinityTier.Acquaintance,
            < 80 => AffinityTier.Friend,
            _ => AffinityTier.Confidant,
        };

        /// <summary>
        /// 대화 전 관계 스냅샷으로 첫 만남·재방문 상태를 판정하고 NPC 성격에 맞는 인사를 고른다.
        /// 본문 토픽과 분리해 인사가 실제 화제를 밀어내지 않도록 한다.
        /// </summary>
        public static string SelectGreeting(int npcId, PlayerRelation relation, float currentGameTime)
        {
            GreetingEncounter encounter = GetGreetingEncounter(relation, currentGameTime);
            NpcSaveData? save = AR.s.Npc?.GetSaveData(npcId);
            GreetingTone tone = GetGreetingTone(save, relation);
            int variant = StableHash(StableHash(npcId, (int)encounter), relation.TalkCount) % 2;
            return GreetingLine(encounter, tone, GetAffinityTier(relation.Affinity), variant);
        }

        /// <summary>NPC별 화제 레퍼토리(2~5) — 관계가 깊을수록 넓어지고 성격에 맞는 화제가 선점한다.</summary>
        private static List<DialogueTopic> BuildRepertoire(int npcId, List<DialogueTopic> applicable, AffinityTier tier)
        {
            int cap = RepertoireSize(npcId, tier);
            List<DialogueTopic> result = new();

            // 1) 활성 긴급 니즈(현재는 기아)는 급하니 자동 포함
            for (int i = 0; i < applicable.Count && result.Count < cap; i++)
                if (applicable[i].Urgent)
                    result.Add(applicable[i]);

            // 2) 나머지: 관계 단계별 카테고리 우선순위 + 성격 편향 + npcId 안정 tiebreak로 채움
            List<DialogueTopic> rest = new();
            for (int i = 0; i < applicable.Count; i++)
                if (applicable[i].Urgent == false)
                    rest.Add(applicable[i]);
            NpcSaveData? save = AR.s.Npc?.GetSaveData(npcId);
            rest.Sort((a, b) => SortKey(npcId, tier, save, b).CompareTo(SortKey(npcId, tier, save, a)));
            for (int i = 0; i < rest.Count && result.Count < cap; i++)
                result.Add(rest[i]);

            return result;
        }

        // 기본 1~3에 관계 단계만큼 확장하되, 최소 2·최대 5로 제한한다.
        private static int RepertoireSize(int npcId, AffinityTier tier)
        {
            int r = StableHash(npcId, 777) % 100;
            int baseSize = r < 25 ? 1 : r < 75 ? 2 : 3;
            return Mathf.Clamp(baseSize + (int)tier, 2, 5);
        }

        // 관계 단계 우선순위*10000 + 성격 편향*100 + NPC별 안정 해시.
        private static int SortKey(int npcId, AffinityTier tier, NpcSaveData? save, DialogueTopic t)
        {
            int priority = CategoryPriority(t, tier);
            int personality = PersonalityBias(save, t.Type);
            return priority * 10000 + personality * 100 + (StableHash(npcId, (int)t.Type) % 100);
        }

        private static int CategoryPriority(DialogueTopic topic, AffinityTier tier)
        {
            // 상시 존재하는 야외 몬스터는 배경 화제다. 실제 사건이 생기기 전에는 일상·사연보다 앞서지 않는다.
            if (topic.Type == DialogueTopicType.ClearThreat)
                return 2;
            // 야망 소문은 지금 확인할 수 있는 실제 사건(유효 검증 통과)이라 연대기 소문보다 앞세운다 (P2c) — 낯선 사이에서도 일상 화제와 겨룬다.
            if (topic.Type == DialogueTopicType.DungeonRumor || topic.Type == DialogueTopicType.ItemOwnerRumor)
                return AmbitionRumorPriority;

            return topic.Category switch
            {
                DialogueTopicCategory.Need => 5,
                DialogueTopicCategory.Personal => tier >= AffinityTier.Friend ? 5 : tier >= AffinityTier.Acquaintance ? 3 : 2,
                DialogueTopicCategory.Flavor => tier <= AffinityTier.Acquaintance ? 4 : 3,
                DialogueTopicCategory.Rumor => 2,
                _ => 0,
            };
        }

        private static int PersonalityBias(NpcSaveData? save, DialogueTopicType type)
        {
            if (save == null)
                return 0;

            return type switch
            {
                DialogueTopicType.SmallTalk => save.NpcStat.Friendliness / 5,
                DialogueTopicType.JobMood => (save.NpcStat.Pride + save.NpcStat.Patience) / 10,
                DialogueTopicType.HistoryRumor => save.NpcStat.Curiosity / 5,
                DialogueTopicType.SettlementRumor => save.NpcStat.Curiosity / 5,
                DialogueTopicType.DangerRumor => (save.NpcStat.Curiosity + save.NpcStat.Courage) / 10,
                DialogueTopicType.Gossip => (save.NpcStat.Friendliness + save.NpcStat.Curiosity) / 10,
                DialogueTopicType.ClearThreat => (100 - save.NpcStat.Courage) / 5,
                DialogueTopicType.ResourceShortage => save.NpcStat.Greed / 5,
                DialogueTopicType.CraftMaterial => (save.NpcStat.Greed + save.NpcStat.Patience) / 10,
                DialogueTopicType.RecentHunt => (save.NpcStat.Courage + save.NpcStat.Pride) / 10,
                DialogueTopicType.LostCompanion => save.NpcStat.Loyalty / 5,
                DialogueTopicType.Boast => (save.NpcStat.Pride + save.NpcStat.Greed) / 10,
                DialogueTopicType.PartyBond => (save.NpcStat.Loyalty + save.NpcStat.Friendliness) / 10,
                DialogueTopicType.DungeonRumor => (save.NpcStat.Curiosity + save.NpcStat.Courage) / 10,
                DialogueTopicType.ItemOwnerRumor => (save.NpcStat.Curiosity + save.NpcStat.Greed) / 10,
                _ => 0,
            };
        }

        private static GreetingEncounter GetGreetingEncounter(PlayerRelation relation, float currentGameTime)
        {
            if (relation.TalkCount <= 0)
                return GreetingEncounter.FirstMeeting;

            if (relation.LastTalkGameTime >= 0f)
            {
                float elapsed = currentGameTime - relation.LastTalkGameTime;
                if (elapsed >= 0f && elapsed <= ImmediateRepeatHours)
                    return GreetingEncounter.ImmediateRepeat;
                if (elapsed >= LongAbsentHours)
                    return GreetingEncounter.LongAbsent;
            }

            return relation.TalkCount <= 3
                ? GreetingEncounter.FamiliarFace
                : GreetingEncounter.Regular;
        }

        private static GreetingTone GetGreetingTone(NpcSaveData? save, PlayerRelation relation)
        {
            if (relation.Fear >= 50)
                return GreetingTone.Guarded;
            if (save == null)
                return GreetingTone.Direct;
            if (relation.Intimacy >= 70 && save.NpcStat.Friendliness >= 35)
                return GreetingTone.Friendly;
            if (relation.Trust >= 70 && save.NpcStat.Loyalty >= 50)
                return GreetingTone.Loyal;

            int highest = save.NpcStat.Friendliness;
            GreetingTone result = GreetingTone.Friendly;
            if (save.NpcStat.Honesty > highest)
            {
                highest = save.NpcStat.Honesty;
                result = GreetingTone.Direct;
            }
            if (save.NpcStat.Greed > highest)
            {
                highest = save.NpcStat.Greed;
                result = GreetingTone.Greedy;
            }
            if (save.NpcStat.Loyalty > highest)
            {
                highest = save.NpcStat.Loyalty;
                result = GreetingTone.Loyal;
            }
            if (save.NpcStat.Courage > highest)
            {
                highest = save.NpcStat.Courage;
                result = GreetingTone.Courageous;
            }
            if (save.NpcStat.Curiosity > highest)
            {
                highest = save.NpcStat.Curiosity;
                result = GreetingTone.Curious;
            }
            if (save.NpcStat.Pride > highest)
            {
                highest = save.NpcStat.Pride;
                result = GreetingTone.Proud;
            }
            if (save.NpcStat.Patience > highest)
                result = GreetingTone.Patient;
            return result;
        }

        private static string GreetingLine(
            GreetingEncounter encounter, GreetingTone tone, AffinityTier tier, int variant)
        {
            if (encounter == GreetingEncounter.Regular && tier == AffinityTier.Confidant)
                return ConfidantLine(tone, variant);
            if (encounter == GreetingEncounter.Regular && tier == AffinityTier.Friend)
                return FriendLine(tone, variant);

            return encounter switch
            {
                GreetingEncounter.FirstMeeting => FirstMeetingLine(tone, variant),
                GreetingEncounter.FamiliarFace => FamiliarFaceLine(tone, variant),
                GreetingEncounter.Regular => RegularLine(tone, variant),
                GreetingEncounter.LongAbsent => LongAbsentLine(tone, variant),
                GreetingEncounter.ImmediateRepeat => ImmediateRepeatLine(tone, variant),
                _ => "무슨 일이오?",
            };
        }

        private static string FriendLine(GreetingTone tone, int variant) => tone switch
        {
            GreetingTone.Friendly => Variant(variant, "왔군! 자네를 보면 마음이 놓여.", "어서 오게. 자네와 이야기하면 늘 즐겁다네."),
            GreetingTone.Direct => Variant(variant, "왔나. 자네라면 바로 본론을 말해도 되겠군.", "자네로군. 돌려 말할 사이는 아니지."),
            GreetingTone.Greedy => Variant(variant, "왔군. 자네와는 장사 말고도 나눌 이야기가 있지.", "어서 오게. 좋은 친구에게까지 값을 매기진 않는다네."),
            GreetingTone.Loyal => Variant(variant, "왔나, 친구. 자네라면 믿을 수 있지.", "잘 왔네. 자네가 곁에 있으면 든든하군."),
            GreetingTone.Courageous => Variant(variant, "왔군, 친구. 다음엔 함께 길을 나서도 좋겠어.", "자네로군. 믿고 등을 맡길 사람이 와서 반갑네."),
            GreetingTone.Curious => Variant(variant, "왔군! 자네에게 묻고 싶은 게 많았네.", "잘 왔어. 오늘은 밤새 이야기해도 모자라겠군."),
            GreetingTone.Proud => Variant(variant, "왔나. 자네 정도라면 내 친구라 불러도 부끄럽지 않지.", "자네로군. 내가 인정한 사람은 흔치 않다네."),
            GreetingTone.Patient => Variant(variant, "왔나, 친구. 편히 쉬면서 이야기하게.", "잘 왔네. 자네 이야기라면 오래 들어도 좋지."),
            GreetingTone.Guarded => Variant(variant, "왔군. 아직 조심스럽지만 자네는 믿어 보겠소.", "자네로군. 다른 이보다야 마음을 놓을 수 있지."),
            _ => "왔나, 친구.",
        };

        private static string ConfidantLine(GreetingTone tone, int variant) => tone switch
        {
            GreetingTone.Friendly => Variant(variant, "왔구나. 자네 앞에서는 굳이 마음을 숨길 필요가 없지.", "기다리고 있었네. 자네에게만 할 수 있는 이야기가 있어."),
            GreetingTone.Direct => Variant(variant, "왔군. 자네에게는 솔직히 말하지.", "자네로군. 숨김없이 이야기해도 되겠지."),
            GreetingTone.Greedy => Variant(variant, "왔나. 재물보다 귀한 신뢰를 나눌 사람은 자네뿐이군.", "자네에게만은 손익을 따지고 싶지 않네."),
            GreetingTone.Loyal => Variant(variant, "왔나. 끝까지 믿을 수 있는 사람은 자네뿐이야.", "잘 왔네. 자네에게 맡기지 못할 이야기는 없지."),
            GreetingTone.Courageous => Variant(variant, "왔군. 자네와 함께라면 어떤 위험도 두렵지 않네.", "자네로군. 내 등을 맡길 사람은 역시 자네뿐이지."),
            GreetingTone.Curious => Variant(variant, "왔군! 자네와 나눌 이야기를 계속 생각하고 있었네.", "잘 왔어. 자네라면 내 속마음까지 이해하겠지."),
            GreetingTone.Proud => Variant(variant, "왔나. 내가 속을 터놓는 사람은 자네뿐이라네.", "자네로군. 내 신뢰를 얻은 걸 자랑스럽게 여겨도 좋아."),
            GreetingTone.Patient => Variant(variant, "왔구나. 오늘도 마음 놓고 이야기해 보세.", "잘 왔네. 자네 앞에서는 서두를 것도 숨길 것도 없지."),
            GreetingTone.Guarded => Variant(variant, "자네로군. 다른 이는 몰라도 자네에게는 말할 수 있소.", "왔소? 자네 앞에서만큼은 경계를 내려놓아도 되겠지."),
            _ => "왔군. 자네에게만 할 이야기가 있네.",
        };

        private static string FirstMeetingLine(GreetingTone tone, int variant) => tone switch
        {
            GreetingTone.Friendly => Variant(variant, "처음 보는 얼굴이네. 이 마을엔 처음인가?", "낯선 얼굴이지만 반갑군. 여행자인가?"),
            GreetingTone.Direct => Variant(variant, "처음 보는 얼굴인데. 용건이 뭐지?", "마을 사람은 아닌 것 같군. 무슨 일이지?"),
            GreetingTone.Greedy => Variant(variant, "새 손님인가? 팔 만한 물건은 있나?", "처음 보는 얼굴이군. 돈이 될 만한 이야기는 환영이지."),
            GreetingTone.Loyal => Variant(variant, "낯선 이로군. 이 마을에는 무슨 일이지?", "처음 보는 얼굴인데, 마을에 해를 끼칠 생각은 없겠지?"),
            GreetingTone.Courageous => Variant(variant, "낯선 이로군. 험한 길을 지나왔나 보지?", "처음 보는 얼굴인데도 기세가 좋군. 모험가인가?"),
            GreetingTone.Curious => Variant(variant, "처음 보는 얼굴인데, 어디서 왔지?", "여기선 못 보던 사람이군. 어떤 길로 왔나?"),
            GreetingTone.Proud => Variant(variant, "처음 보는 자로군. 내 앞에서는 예를 갖추게.", "낯선 얼굴이군. 누구와 이야기하는지는 알고 있겠지?"),
            GreetingTone.Patient => Variant(variant, "처음 보는 얼굴이군. 천천히 이야기해 보게.", "낯선 사람이로군. 급할 것 없으니 용건을 말해 보게."),
            GreetingTone.Guarded => Variant(variant, "누구시오? 마을 사람은 아닌 것 같은데.", "거기서 말하시오. 처음 보는 얼굴이라 경계가 되는군."),
            _ => "처음 보는 얼굴이군.",
        };

        private static string FamiliarFaceLine(GreetingTone tone, int variant) => tone switch
        {
            GreetingTone.Friendly => Variant(variant, "또 왔네. 반가워.", "이제 낯이 익는군. 오늘은 어떤가?"),
            GreetingTone.Direct => Variant(variant, "또 왔군. 이번 용건은 뭐지?", "전에 본 얼굴이군. 할 말이 있나?"),
            GreetingTone.Greedy => Variant(variant, "또 왔나? 이번엔 좋은 물건이라도 있나?", "낯이 익군. 돈 될 이야기를 가져왔겠지?"),
            GreetingTone.Loyal => Variant(variant, "다시 왔군. 마을은 좀 둘러봤나?", "전에 본 사람이군. 별일은 없었겠지?"),
            GreetingTone.Courageous => Variant(variant, "또 만났군. 밖에서 별일은 없었나?", "낯이 익는군. 이번에도 험한 길을 다녀왔나?"),
            GreetingTone.Curious => Variant(variant, "다시 왔군. 지난번 이야기가 계속 궁금했는데.", "또 만났네. 이번엔 어디를 다녀왔지?"),
            GreetingTone.Proud => Variant(variant, "또 찾아왔군. 내 이야기가 꽤 마음에 들었나 보지?", "낯이 익군. 다시 나를 찾아온 이유가 있겠지?"),
            GreetingTone.Patient => Variant(variant, "다시 왔군. 오늘도 천천히 이야기하지.", "전에 본 얼굴이네. 서두르지 말고 말해 보게."),
            GreetingTone.Guarded => Variant(variant, "또 왔소? 무슨 일인지 먼저 말하시오.", "전에 본 얼굴이군. 아직은 경계를 풀 수 없소."),
            _ => "또 왔군.",
        };

        private static string RegularLine(GreetingTone tone, int variant) => tone switch
        {
            GreetingTone.Friendly => Variant(variant, "왔나? 얼굴을 보니 반갑군.", "오늘도 들렀네. 잘 지냈나?"),
            GreetingTone.Direct => Variant(variant, "왔군. 무슨 이야기지?", "자네로군. 용건을 말해 보게."),
            GreetingTone.Greedy => Variant(variant, "왔군. 오늘은 어떤 거래가 될까?", "자주 보는 얼굴이니 좋은 이야기를 기대해도 되겠지?"),
            GreetingTone.Loyal => Variant(variant, "왔나. 자네라면 마을 이야기를 해도 되겠군.", "자주 와 주는군. 마을에 마음을 둔 모양이야."),
            GreetingTone.Courageous => Variant(variant, "왔군. 오늘도 멀쩡한 걸 보니 잘 싸우고 다니나 보지?", "자네로군. 바깥일은 어떻던가?"),
            GreetingTone.Curious => Variant(variant, "왔군. 오늘은 무슨 이야기를 들려줄 텐가?", "자네가 오면 늘 새로운 이야기가 생기더군."),
            GreetingTone.Proud => Variant(variant, "또 나를 찾아왔군. 안목은 여전하구나.", "왔나. 자네 정도면 내 시간을 조금 내주지."),
            GreetingTone.Patient => Variant(variant, "왔군. 편히 앉아 이야기하게.", "오늘도 왔구나. 천천히 듣고 있겠네."),
            GreetingTone.Guarded => Variant(variant, "자네로군. 아직 긴장은 놓을 수 없소.", "왔소? 익숙한 얼굴이어도 조심해서 나쁠 건 없지."),
            _ => "왔군.",
        };

        private static string LongAbsentLine(GreetingTone tone, int variant) => tone switch
        {
            GreetingTone.Friendly => Variant(variant, "오랜만이네! 어디 다친 데는 없나?", "한동안 보이지 않더니 돌아왔군. 반가워."),
            GreetingTone.Direct => Variant(variant, "오랜만이군. 어디에 있었지?", "한동안 안 보이더니 이제 왔나?"),
            GreetingTone.Greedy => Variant(variant, "오랜만이군. 먼 곳에서 귀한 것이라도 가져왔나?", "한동안 안 보였는데, 빈손으로 온 건 아니겠지?"),
            GreetingTone.Loyal => Variant(variant, "돌아왔군. 마을을 잊은 줄 알았네.", "오랜만이네. 다시 와 줘서 다행이군."),
            GreetingTone.Courageous => Variant(variant, "살아서 돌아왔군. 제법 먼 길이었나?", "오랜만이야. 또 험한 일을 겪고 온 얼굴이군."),
            GreetingTone.Curious => Variant(variant, "드디어 돌아왔군! 어디를 다녀온 건가?", "오랜만이네. 들려줄 이야기가 잔뜩 있겠지?"),
            GreetingTone.Proud => Variant(variant, "이제야 다시 왔군. 내 이야기가 그리웠나?", "한동안 뜸하더니 결국 돌아왔구나."),
            GreetingTone.Patient => Variant(variant, "오랜만이군. 돌아왔으니 천천히 이야기하게.", "한동안 못 봤네. 잘 지냈다면 그걸로 됐지."),
            GreetingTone.Guarded => Variant(variant, "오랜만이오. 갑자기 돌아온 이유가 뭐요?", "한동안 보이지 않더니 다시 나타났군. 무슨 일이지?"),
            _ => "오랜만이군.",
        };

        private static string ImmediateRepeatLine(GreetingTone tone, int variant) => tone switch
        {
            GreetingTone.Friendly => Variant(variant, "더 이야기하고 싶은 게 있나?", "그래, 무슨 이야기가 더 남았지?"),
            GreetingTone.Direct => Variant(variant, "할 말이 더 있나?", "아직 용건이 남았나?"),
            GreetingTone.Greedy => Variant(variant, "또 무슨 좋은 제안이라도 있나?", "더 볼 일이 있다면 값어치 있는 이야기겠지?"),
            GreetingTone.Loyal => Variant(variant, "마을에 관해 더 궁금한 게 있나?", "그래, 내가 아는 거라면 더 말해 주지."),
            GreetingTone.Courageous => Variant(variant, "아직 모험담이 부족한가?", "더 묻고 싶은 게 있다면 말해 보게."),
            GreetingTone.Curious => Variant(variant, "잠깐, 나도 더 물어볼 게 있었네.", "좋아, 이야기를 조금 더 이어 보지."),
            GreetingTone.Proud => Variant(variant, "내 이야기를 더 듣고 싶은 모양이군.", "아직도 내게 배울 것이 남았나?"),
            GreetingTone.Patient => Variant(variant, "괜찮네. 더 이야기해 보게.", "시간은 있으니 천천히 말하게."),
            GreetingTone.Guarded => Variant(variant, "아직도 할 말이 남았소?", "무슨 일인지 분명히 말하시오."),
            _ => "할 말이 더 있나?",
        };

        private static string Variant(int variant, string first, string second) => variant == 0 ? first : second;

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
            // ClearThreat — 야외 몬스터가 상시 존재하는 세계의 배경 화제. 실제 습격 사건 전에는 긴급하지 않다.
            bool threatOn = threat.NearbyMonsters || (village != null && village.ThreatLevel > 0f);
            if (threatOn)
            {
                DialogueTopic t = new()
                {
                    Type = DialogueTopicType.ClearThreat,
                    Category = DialogueTopicCategory.Rumor,
                    MinTier = AffinityTier.Stranger,
                    Urgent = false,
                    BaseLine = "마을 밖에는 늘 몬스터가 돌아다니니 길을 나설 때는 조심하시오.",
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

        // ── 야망 소문 (NPC_AMBITION P2c, D60·D72·D75) ──
        // 데이터 소스 = 말하는 NPC의 NpcSaveData.Rumors. 노출 직전 IsRumorValid로 검증하고, 다른 마을 던전 소문은 방어적으로 뺀다.
        // 자기 장비 소문(직접 겪은 ItemOwner)은 자랑(Boast)이 맡으므로 뺀다. 대화 때는 전체에서 1개(종류 → 후보 순)를
        // UnityEngine.Random으로 고른다 (연대기 소문과 같은 관례 — 대화 연출은 결정론 계약 밖). pickAll은 진단용(전부 반환).
        private static void AddAmbitionRumors(List<DialogueTopic> topics, int npcId, NpcSaveData? save, bool pickAll)
        {
            if (save == null || save.Rumors == null || save.Rumors.Count == 0 || AR.s.Npc == null)
                return;

            float now = AR.s.Time != null ? AR.s.Time.CurrentGameTime : 0f;
            List<DialogueTopic> dungeon = new();
            List<DialogueTopic> item = new();
            List<NpcRumor> rumors = save.Rumors;
            for (int i = 0; i < rumors.Count; i++)
            {
                NpcRumor rumor = rumors[i];
                if (AR.s.Npc.IsRumorValid(rumor) == false)
                    continue;
                DialogueTopic? t = rumor.Kind switch
                {
                    NpcRumorKind.DungeonInfo => BuildDungeonRumorTopic(npcId, save, rumor, now),
                    NpcRumorKind.ItemOwner => BuildItemOwnerRumorTopic(npcId, rumor, now),
                    _ => null,
                };
                if (t == null)
                    continue;
                if (t.Type == DialogueTopicType.DungeonRumor)
                    dungeon.Add(t);
                else
                    item.Add(t);
            }

            if (pickAll == true)
            {
                AddAllRumorTopics(topics, dungeon);
                AddAllRumorTopics(topics, item);
                return;
            }

            // 대화 1회분에는 야망 소문 토픽을 최대 1개만 넣는다 — 소문을 가진 NPC가 대부분이라 둘 다 넣으면
            // 낮은 관계 단계의 좁은 레퍼토리(2~3)를 소문이 모두 차지한다. 종류를 먼저 고르고(둘 다 있으면 반반) 그 안에서 1개.
            List<DialogueTopic> kind = dungeon.Count > 0 && (item.Count == 0 || Random.value < 0.5f) ? dungeon : item;
            if (kind.Count == 0)
                return;
            DialogueTopic picked = kind[Random.Range(0, kind.Count)];
            AR.s.Npc.RecordRumorExposure(picked.Rumor!);
            topics.Add(picked);
        }

        private static void AddAllRumorTopics(List<DialogueTopic> topics, List<DialogueTopic> candidates)
        {
            for (int i = 0; i < candidates.Count; i++)
            {
                AR.s.Npc.RecordRumorExposure(candidates[i].Rumor!);
                topics.Add(candidates[i]);
            }
        }

        private static DialogueTopic? BuildDungeonRumorTopic(int npcId, NpcSaveData save, NpcRumor rumor, float now)
        {
            NpcRumorPlace place = AR.s.Npc.ResolveRumorDungeonPlace(rumor, save.VillageId, out int bearing);
            // 다른 마을 던전(D75상 지금은 없음)·슬롯을 못 찾은 소문은 말하지 않는다 — 플레이어가 찾을 수 없는 장소를 가리키지 않게.
            if (place != NpcRumorPlace.Bearing && place != NpcRumorPlace.Nearby)
                return null;

            string placeCore = NpcRumorPhrasing.PlaceCore(place, bearing);
            string ago = NpcRumorPhrasing.AgoPhrase(rumor.EventGameTime, now);
            string subject = NpcRumorPhrasing.NpcName(rumor.SubjectNpcId);
            bool firsthand = rumor.SourceNpcId < 0;
            bool self = rumor.SubjectNpcId == npcId;
            string line;
            if (rumor.Cleared == true)
            {
                string where = $"그 자리에 새로 생긴 던전은 {placeCore}에 있소.";
                if (firsthand == true)
                    line = self == true
                        ? $"{ago} 내가 이 마을 던전 하나를 끝장냈소. {where}"
                        : $"{ago} {NpcRumorPhrasing.Josa(subject, "과", "와")} 함께 이 마을 던전 하나를 끝장냈소. {where}";
                else if (rumor.SourceNpcId == rumor.SubjectNpcId && rumor.Hops == 1)
                    line = $"{subject} 일행이 {ago} 이 마을 던전 하나를 끝냈다고 내게 직접 그러더군. " +
                           $"그 자리에 새 던전이 생겼는데, {placeCore}에 있다지.";
                else
                    line = $"{NpcRumorPhrasing.HeardPrefix(rumor)}{subject} 일행이 {ago} 이 마을 던전 하나를 끝냈다더군. " +
                           $"그 자리에 새 던전이 생겼는데, {placeCore}에 있다지.";
            }
            else
            {
                string dungeonPlace = $"{placeCore} 던전";
                if (firsthand == true)
                    line = self == true
                        ? $"{ago} {dungeonPlace}에 다녀왔소. 쓸 만한 걸 건져 왔지."
                        : $"{ago} {NpcRumorPhrasing.Josa(subject, "과", "와")} 함께 {dungeonPlace}에 다녀왔소. 쓸 만한 걸 건져 왔지.";
                else if (rumor.SourceNpcId == rumor.SubjectNpcId && rumor.Hops == 1)
                    line = $"{NpcRumorPhrasing.Josa(subject, "이", "가")} {ago} {dungeonPlace}에서 한몫 챙겼다고 내게 직접 그러더군.";
                else
                    line = $"{NpcRumorPhrasing.HeardPrefix(rumor)}{NpcRumorPhrasing.Josa(subject, "이", "가")} {ago} " +
                           $"{dungeonPlace}에서 한몫 챙겼다더군.";
            }

            DialogueTopic t = new()
            {
                Type = DialogueTopicType.DungeonRumor,
                Category = DialogueTopicCategory.Rumor,
                MinTier = AffinityTier.Stranger,
                BaseLine = line,
                CacheVariant = RumorCacheVariant(rumor),
                Rumor = rumor,
                RumorPlace = place,
                RumorBearing = bearing,
            };
            t.Facts.Add(rumor.Cleared == true
                ? $"{subject} 일행이 소속 마을의 던전 하나를 끝까지 공략했다. 그 던전은 사라졌고 같은 자리에 새 던전이 생겼다."
                : $"{subject}{(self == true ? "(본인)" : string.Empty)}이(가) 소속 마을 던전 원정에 성공해 전리품(가치 {rumor.Value})을 얻었다.");
            t.Facts.Add(place == NpcRumorPlace.Bearing
                ? $"던전 위치: {placeCore} (입구가 있음). 마을·정착지에는 이름이 없으니 이름을 지어내지 말고 방향으로만 말할 것."
                : "던전 위치: 이 근처 (입구가 아직 드러나지 않음). 방향이나 마을 이름을 지어내지 말 것.");
            t.Facts.Add($"사건 시점: {ago} (게임 시간 {rumor.EventGameTime:F1}). 출처: {NpcRumorPhrasing.SourceNote(rumor)}.");
            return t;
        }

        private static DialogueTopic? BuildItemOwnerRumorTopic(int npcId, NpcRumor rumor, float now)
        {
            // 자기 장비는 자랑(Boast) 토픽이 말한다.
            if (rumor.SubjectNpcId == npcId || AR.s.Npc.TryGetRumorItem(rumor, out ItemData? item) == false || item == null)
                return null;

            string owner = NpcRumorPhrasing.NpcName(rumor.SubjectNpcId);
            string itemName = item.Table != null ? item.Table.Name : "귀한 장비";
            string ago = NpcRumorPhrasing.AgoPhrase(rumor.EventGameTime, now);
            string line = rumor.SourceNpcId == rumor.SubjectNpcId && rumor.Hops == 1
                ? $"{NpcRumorPhrasing.Josa(owner, "이", "가")} {ago} {NpcRumorPhrasing.Josa(itemName, "을", "를")} 손에 넣었다고 내게 직접 자랑하더군."
                : $"{NpcRumorPhrasing.HeardPrefix(rumor)}{NpcRumorPhrasing.Josa(owner, "이", "가")} {ago} " +
                  $"{NpcRumorPhrasing.Josa(itemName, "을", "를")} 손에 넣었다더군. 요즘 그걸 차고 다닌다지.";

            DialogueTopic t = new()
            {
                Type = DialogueTopicType.ItemOwnerRumor,
                Category = DialogueTopicCategory.Rumor,
                MinTier = AffinityTier.Stranger,
                BaseLine = line,
                CacheVariant = RumorCacheVariant(rumor),
                Rumor = rumor,
            };
            t.Facts.Add($"{owner}이(가) {itemName}을(를) 새로 장착하고 다닌다 (가치 {rumor.Value}). 지금도 사실이다.");
            t.Facts.Add($"사건 시점: {ago} (게임 시간 {rumor.EventGameTime:F1}). 출처: {NpcRumorPhrasing.SourceNote(rumor)}.");
            return t;
        }

        // 소문 내용·출처가 바뀌면 다른 값 → 다른 대화 캐시 항목 (같은 토픽 타입 안의 구분자)
        private static int RumorCacheVariant(NpcRumor rumor)
        {
            bool dungeon = rumor.Kind == NpcRumorKind.DungeonInfo;
            int h = StableHash((int)rumor.Kind, rumor.Cleared == true ? 1 : 0);
            h = StableHash(h, dungeon == true ? rumor.VillageId : rumor.EquipSlot);
            h = StableHash(h, dungeon == true ? rumor.SlotIndex : rumor.ItemInstanceId);
            h = StableHash(h, dungeon == true ? rumor.Generation : 0);
            h = StableHash(h, rumor.SubjectNpcId);
            h = StableHash(h, (int)(rumor.EventGameTime * 100f));
            h = StableHash(h, rumor.SourceNpcId);
            return StableHash(h, rumor.Hops);
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

        // ── 사연 (Personal — 개인 전기 Deeds·사별 기억. 난수 없이 최신순 = 결정론) ──
        // Fell은 사망자 본인 전기에만 남으므로 화자(생존자)의 상실은 DeceasedRelations가 접지.
        // Boast = NotableItemAcquired(업그레이드 장착, P2c 배선) 또는 DungeonCleared(NPC 던전 완료) 중 최신 1건.
        // CacheVariant에 사건 식별자(Deeds 인덱스/사망자 id)를 실어 새 사건이면 캐시가 갈린다.
        private static void AddPersonal(List<DialogueTopic> topics, int npcId, NpcSaveData? save)
        {
            if (save == null)
                return;

            // Deeds 최신순 스캔 — 토픽 타입당 최신 1건, 합계 최대 2건 (Deeds는 append-only·불삭제라 인덱스가 안정 식별자)
            bool hasHunt = false;
            bool hasBond = false;
            bool hasBoast = false;
            int added = 0;
            for (int i = save.Deeds.Count - 1; i >= 0 && added < 2; i--)
            {
                NpcDeedEntry deed = save.Deeds[i];
                if (deed == null)
                    continue;

                if (deed.Kind == NpcDeedKind.Wounded && hasHunt == false)
                {
                    hasHunt = true;
                    added++;
                    bool delve = deed.Activity == NpcActivityKind.DungeonDelve;
                    DialogueTopic t = new()
                    {
                        Type = DialogueTopicType.RecentHunt,
                        Category = DialogueTopicCategory.Personal,
                        MinTier = AffinityTier.Stranger,
                        BaseLine = delve
                            ? "지난 던전 원정에서 호되게 당했소. 아직 몸이 뻐근하구려."
                            : "지난 사냥에서 호되게 당했소. 아직 몸이 뻐근하구려.",
                        CacheVariant = i,
                    };
                    t.Facts.Add($"게임 시간 {deed.When:F1}에 {(delve ? "던전 원정" : "사냥")}을 나갔다가 부상을 입었다 ({KnowledgeHint(deed.HowKnown)}).");
                    topics.Add(t);
                }
                else if (deed.Kind == NpcDeedKind.PartyAffinityChanged && hasBond == false)
                {
                    hasBond = true;
                    added++;
                    string other = OtherParticipantName(deed, npcId);
                    bool closer = deed.AffinityDelta >= 0;
                    DialogueTopic t = new()
                    {
                        Type = DialogueTopicType.PartyBond,
                        Category = DialogueTopicCategory.Personal,
                        MinTier = AffinityTier.Acquaintance,
                        BaseLine = closer ? $"{other}와는 요즘 손발이 잘 맞소." : $"{other}와는 요즘 영 서먹하오.",
                        CacheVariant = i,
                    };
                    t.Facts.Add($"게임 시간 {deed.When:F1}, {other}와 함께 사냥했고 사이가 {(closer ? "가까워졌다" : "서먹해졌다")} ({KnowledgeHint(deed.HowKnown)}).");
                    topics.Add(t);
                }
                else if (deed.Kind == NpcDeedKind.NotableItemAcquired && hasBoast == false)
                {
                    hasBoast = true;
                    added++;
                    string item = string.IsNullOrEmpty(deed.ItemDisplayName) ? "귀한 물건" : deed.ItemDisplayName;
                    DialogueTopic t = new()
                    {
                        Type = DialogueTopicType.Boast,
                        Category = DialogueTopicCategory.Personal,
                        MinTier = AffinityTier.Stranger,
                        BaseLine = $"{NpcRumorPhrasing.Josa(item, "을", "를")} 손에 넣었소. 보는 눈이 있다면 알아보겠지.",
                        CacheVariant = i,
                    };
                    t.Facts.Add($"게임 시간 {deed.When:F1}에 {item}을(를) 손에 넣었다 ({KnowledgeHint(deed.HowKnown)}).");
                    topics.Add(t);
                }
                else if (deed.Kind == NpcDeedKind.DungeonCleared && hasBoast == false)
                {
                    // 자랑 (P2c, D61): NPC 던전 완료. NotableItemAcquired와 같은 Boast 타입이라 둘 중 최신 1건만 말한다.
                    hasBoast = true;
                    added++;
                    string companions = OtherParticipantNames(deed, npcId);
                    float now = AR.s.Time != null ? AR.s.Time.CurrentGameTime : deed.When;
                    string ago = NpcRumorPhrasing.AgoPhrase(deed.When, now);
                    DialogueTopic t = new()
                    {
                        Type = DialogueTopicType.Boast,
                        Category = DialogueTopicCategory.Personal,
                        MinTier = AffinityTier.Stranger,
                        BaseLine = string.IsNullOrEmpty(companions)
                            ? $"{ago} 이 마을 던전 하나를 내 손으로 끝장냈소. 그 자리엔 벌써 새 던전이 들어섰지."
                            : $"{ago} {NpcRumorPhrasing.Josa(companions, "과", "와")} 함께 이 마을 던전 하나를 끝장냈소. 그 자리엔 벌써 새 던전이 들어섰지.",
                        CacheVariant = i,
                    };
                    t.Facts.Add($"게임 시간 {deed.When:F1}에 소속 마을의 던전 하나를 끝까지 공략했다" +
                                (string.IsNullOrEmpty(companions) ? " (혼자)" : $" ({companions}와(과) 함께)") +
                                $". 끝낸 던전은 사라지고 그 자리에 새 던전이 생겼다 ({KnowledgeHint(deed.HowKnown)}).");
                    topics.Add(t);
                }
            }

            // 사별 기억 → LostCompanion (최신 1건, 사적인 이야기라 Acquaintance+)
            if (save.DeceasedRelations != null && save.DeceasedRelations.Count > 0)
            {
                DeceasedRelationMemory memory = save.DeceasedRelations[save.DeceasedRelations.Count - 1];
                string label = NpcDialogueContextBuilder.RelationLabel(memory.RelationType, memory.WasFromEntity);
                DialogueTopic t = new()
                {
                    Type = DialogueTopicType.LostCompanion,
                    Category = DialogueTopicCategory.Personal,
                    MinTier = AffinityTier.Acquaintance,
                    BaseLine = $"{memory.DeceasedNpcName}… 이젠 없는 사람 이야기요.",
                    CacheVariant = memory.DeceasedNpcEntityId,
                };
                t.Facts.Add($"{label}인 {memory.DeceasedNpcName}이(가) 게임 시간 {memory.DeathGameTime:F1}에 세상을 떠났다 (본인이 겪은 상실).");
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

        /// <summary>Deed 참가자 중 화자가 아닌 쪽의 표시명 (스냅샷 우선 → 현재 데이터 → "동료").</summary>
        private static string OtherParticipantName(NpcDeedEntry deed, int npcId)
        {
            for (int i = 0; i < deed.ParticipantSnapshots.Count; i++)
            {
                NpcDeedParticipant participant = deed.ParticipantSnapshots[i];
                if (participant.EntityId == npcId)
                    continue;
                if (string.IsNullOrEmpty(participant.DisplayName) == false)
                    return participant.DisplayName;
            }
            for (int i = 0; i < deed.Participants.Count; i++)
            {
                int participantId = deed.Participants[i];
                if (participantId == npcId)
                    continue;
                return ResolveNpcName(participantId);
            }
            return "동료";
        }

        /// <summary>Deed 참가자 중 화자가 아닌 쪽 전원의 표시명("A, B"). 없으면 빈 문자열. 스냅샷 이름을 우선한다.</summary>
        private static string OtherParticipantNames(NpcDeedEntry deed, int npcId)
        {
            string result = string.Empty;
            for (int i = 0; i < deed.ParticipantSnapshots.Count; i++)
            {
                NpcDeedParticipant participant = deed.ParticipantSnapshots[i];
                if (participant.EntityId == npcId)
                    continue;
                string name = string.IsNullOrEmpty(participant.DisplayName) == false
                    ? participant.DisplayName
                    : NpcRumorPhrasing.NpcName(participant.EntityId);
                result = result.Length == 0 ? name : $"{result}, {name}";
            }
            return result;
        }

        /// <summary>지식 거리(NpcKnowledgeLevel) → 프롬프트 어조 힌트. 직접 겪은 일과 전해 들은 일의 말투를 가른다.</summary>
        private static string KnowledgeHint(NpcKnowledgeLevel level) => level switch
        {
            NpcKnowledgeLevel.Lived => "본인이 직접 겪은 일",
            NpcKnowledgeLevel.Did => "본인이 직접 겪은 일",
            NpcKnowledgeLevel.Witnessed => "직접 목격한 일",
            NpcKnowledgeLevel.Heard => "전해 들은 이야기",
            _ => "본인이 아는 일",
        };

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
