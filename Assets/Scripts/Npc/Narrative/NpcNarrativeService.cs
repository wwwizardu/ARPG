#nullable enable
using System.Collections.Generic;
using System.Text;
using Cysharp.Threading.Tasks;
using ARPG.Manager;

namespace ARPG.Npc.Narrative
{
    /// <summary>
    /// 검증 스파이크: 백본 문맥 → LLM(Groq/Llama) → 대화 생성. static·무상태(순수 로직만).
    /// 대화 캐시와 화제 셔플백은 NpcManager.Narrative(NpcNarrativeRuntime)가 소유 — 새 게임·씬 로드 시 Reset로 정리.
    /// 소망은 LLM으로 만들지 않고 작성 표(NarrativeContent)와 원장이 정한다 (NPC_NARRATIVE_CONTENT_DESIGN §2.2, 2026-10-10).
    /// 설계: Docs/world_history/NARRATIVE_ENGINE_SPIKE.md
    /// </summary>
    public static class NpcNarrativeService
    {
        private const float DialogueTemperature = 0.8f;

        // ── 대화 ──────────────────────────────────────────────────────────
        public static async UniTask<DialogueResult> GetDialogueAsync(
            int npcId, PlayerRelation relation, float currentGameTime, ThreatSeed threat)
        {
            AffinityTier tier = DialogueTopicSelector.GetAffinityTier(relation.Affinity);
            var runtime = AR.s.Npc.Narrative;
            int lifetime = runtime.Lifetime;
            string greeting = DialogueTopicSelector.SelectGreeting(npcId, relation, currentGameTime, out GreetingEncounter encounter);
            NpcDialogueContext? ctx = NpcDialogueContextBuilder.Build(npcId, threat);
            if (ctx == null)
                return new DialogueResult
                {
                    Text = ComposeDialogue(greeting, "…"),
                    Source = NarrativeSource.Fallback,
                    Tier = tier,
                    Topic = DialogueTopicType.Greeting,
                };

            // 토픽 선택 (상태·백본 → 적용 토픽 추림 → 티어 게이트 + 레퍼토리 캡 + 긴급 새치기·셔플백 1개)
            List<DialogueTopic> topics = DialogueTopicSelector.Collect(npcId, ctx, threat);
            DialogueTopic topic = DialogueTopicSelector.Select(npcId, topics, tier, AR.s.Npc.Narrative.GetRecentTopics(npcId));

            // 부탁·진행 확인·소망·발견 소문은 약속과 사실이 걸린 문장이라 작성 문구를 그대로 쓴다 (LLM·캐시 없음, NPC_NARRATIVE §6).
            // 네임드 부탁·진행 확인도 같다 (NAMED_THREAT_DESIGN §8.6). 네임드 소문 화제는 공개된 사실만 Facts에 넣고 LLM을 쓴다.
            if (topic.Type == DialogueTopicType.DungeonRequest || topic.Type == DialogueTopicType.DungeonRequestProgress ||
                topic.Type == DialogueTopicType.WishReveal || topic.Type == DialogueTopicType.WishReminder ||
                topic.Type == DialogueTopicType.NamedThreatRequest || topic.Type == DialogueTopicType.NamedThreatRequestProgress ||
                topic.Rumor?.IsDiscovery == true)
                return new DialogueResult { Text = ComposeDialogue(greeting, topic.BaseLine), Source = NarrativeSource.Fallback,
                    Tier = tier, Topic = topic.Type, NarrativeInstanceId = topic.NarrativeInstanceId,
                    IsNarrativeRequest = topic.Type == DialogueTopicType.DungeonRequest || topic.Type == DialogueTopicType.NamedThreatRequest,
                    IsWishReveal = topic.Type == DialogueTopicType.WishReveal, NarrativeContentId = topic.NarrativeContentId,
                    NamedThreatId = topic.NamedThreatId };

            // LLM 대사는 인사까지 포함해 생성·캐시하므로 작성 인사를 앞에 붙이지 않는다.
            if (AR.s.Npc.Narrative.TryGetDialogue(npcId, tier, encounter, topic.Type, topic.CacheVariant, out string? cached))
                return new DialogueResult
                {
                    Text = cached,
                    Source = NarrativeSource.Cache,
                    Tier = tier,
                    Topic = topic.Type,
                    NamedThreatId = topic.NamedThreatId,
                };

            string? text = null;
            if (AR.s.LLM != null && AR.s.LLM.IsReady)
            {
                List<ChatMessage> msgs = new()
                {
                    new ChatMessage("system", DialogueSystemPrompt()),
                    new ChatMessage("user", BuildDialoguePrompt(ctx, tier, relation, topic, encounter, greeting)),
                };
                text = await AR.s.LLM.RequestWithContextAsync(msgs, LLMClient.MODEL_GPT_OSS_120B, DialogueTemperature);
            }

            // LLM 없거나 실패 → 선택 토픽의 BaseLine (LLM-optional)
            if (string.IsNullOrWhiteSpace(text))
                return new DialogueResult
                {
                    Text = ComposeDialogue(greeting, topic.BaseLine),
                    Source = NarrativeSource.Fallback,
                    Tier = tier,
                    Topic = topic.Type,
                    NamedThreatId = topic.NamedThreatId,
                };

            string clean = text!.Trim();
            if (runtime.Lifetime == lifetime) runtime.SetDialogue(npcId, tier, encounter, topic.Type, topic.CacheVariant, clean);
            return new DialogueResult
            {
                Text = clean,
                Source = NarrativeSource.Llm,
                Tier = tier,
                Topic = topic.Type,
                NamedThreatId = topic.NamedThreatId,
            };
        }

        public static string BuildDialoguePrompt(NpcDialogueContext context, AffinityTier tier, PlayerRelation relation, DialogueTopic topic,
            GreetingEncounter encounter, string greetingExample)
        {
            // Topic.Facts already projects the selected history, rumor or personal story. Sending the entire
            // backbone here mixes unrelated threats and can reveal facts the chosen speaker does not know.
            return "[NPC]\n" + context.NpcBlock + DialogueUserSuffix(tier, relation, topic, encounter, greetingExample);
        }

        // ── 프롬프트 ─────────────────────────────────────────────────────
        private static string DialogueSystemPrompt()
        {
            return
                "너는 이 판타지 세계에서 살아가는 NPC 본인이다. 아래 정보에 근거해 1인칭으로 말한다.\n" +
                "- 제공된 정보(NPC/HISTORY/RELATIONS/SENTIMENT/SITUATION)에 없는 인물·사건·장소·아이템을 지어내지 마라.\n" +
                "- 말투는 성별·성격·관계에 맞춘다. 남성은 '자네'를 쓰는 하게체·하오체, 여성은 '당신'을 쓰는 해요체나 '너'를 쓰는 부드러운 반말을 기본으로 한다. 성별이 미상이면 성별을 추측하지 마라.\n" +
                "- 대사는 [만남 상황]에 맞는 짧은 인사 한 문장으로 시작하고, 본문도 같은 말투를 유지한다.\n" +
                "- [참고 인사말]은 호칭·어미의 예시일 뿐이다. 그 문장을 그대로 쓰지 말고 비슷한 투로 새로 인사한다. [기본 대사]는 내용만 참고하고 말투는 따르지 마라.\n" +
                "- 성격 수치에 맞는 어조로 말한다. 한국어. 대사 텍스트만 출력(해설·따옴표·이름표 금지).\n" +
                "- 관계 단계가 낮으면 표면적인 이야기만, 높을수록 더 사적인 사연을 드러낸다.";
        }

        private static string DialogueUserSuffix(AffinityTier tier, PlayerRelation relation, DialogueTopic topic,
            GreetingEncounter encounter, string greetingExample)
        {
            StringBuilder sb = new();
            sb.AppendLine();
            sb.AppendLine($"[만남 상황] {EncounterKorean(encounter)}");
            if (string.IsNullOrWhiteSpace(greetingExample) == false)
                sb.AppendLine($"[참고 인사말] {greetingExample}");
            sb.AppendLine($"[현재 관계 단계] {TierKorean(tier)}");
            sb.AppendLine($"[플레이어와 관계 수치] 호감 {relation.Affinity}, 신뢰 {relation.Trust}, 두려움 {relation.Fear}, 친밀 {relation.Intimacy}");
            sb.AppendLine($"[대화 주제] {TopicKorean(topic.Type)}");
            if (topic.Facts.Count > 0)
            {
                sb.AppendLine("[주제 관련 사실]");
                for (int i = 0; i < topic.Facts.Count; i++)
                    sb.AppendLine($"- {topic.Facts[i]}");
            }
            sb.AppendLine($"[기본 대사(참고)] {topic.BaseLine}");
            sb.AppendLine("먼저 만남 상황에 맞는 인사 한 문장을 참고 인사말과 비슷한 투로 새로 쓰고(그대로 베끼지 말 것), 이어서 위 주제로 제공된 사실만 사용해 본문 1~2문장을 한국어로 생성하라. 기본 대사보다 풍부하게, 성격·관계 단계와 관계 수치에 맞게.");
            return sb.ToString();
        }

        private static string ComposeDialogue(string greeting, string body)
        {
            if (string.IsNullOrWhiteSpace(greeting))
                return body;
            if (string.IsNullOrWhiteSpace(body))
                return greeting;
            return $"{greeting} {body}";
        }

        private static string TopicKorean(DialogueTopicType t) => t switch
        {
            DialogueTopicType.Greeting => "인사",
            DialogueTopicType.ClearThreat => "근처 몬스터 위협",
            DialogueTopicType.ResourceShortage => "마을 자원 부족",
            DialogueTopicType.Food => "식량난",
            DialogueTopicType.CraftMaterial => "일감/재료 부족",
            DialogueTopicType.Medicine => "몸 상태",
            DialogueTopicType.HistoryRumor => "옛 사건 소문",
            DialogueTopicType.SettlementRumor => "다른 마을 소문",
            DialogueTopicType.Gossip => "마을 사람 이야기",
            DialogueTopicType.DangerRumor => "위험한 곳 소문",
            DialogueTopicType.SmallTalk => "잡담",
            DialogueTopicType.JobMood => "직업 이야기",
            DialogueTopicType.RecentHunt => "지난 사냥 이야기",
            DialogueTopicType.LostCompanion => "잃은 사람 이야기",
            DialogueTopicType.Boast => "자랑거리",
            DialogueTopicType.PartyBond => "동료 이야기",
            DialogueTopicType.DungeonRumor => "이 마을 던전 소문",
            DialogueTopicType.ItemOwnerRumor => "누가 좋은 장비를 얻었다는 소문",
            DialogueTopicType.DungeonRequest => "던전 일 부탁",
            DialogueTopicType.DungeonRequestProgress => "부탁한 던전 일의 진행",
            DialogueTopicType.WishReveal => "마음에 품은 소망",
            DialogueTopicType.WishReminder => "털어놓았던 소망",
            DialogueTopicType.NamedThreatRumor => "사냥꾼들을 해치는 이름난 짐승 소문",
            DialogueTopicType.NamedThreatRequest => "이름난 짐승 사냥 부탁",
            DialogueTopicType.NamedThreatRequestProgress => "부탁한 짐승 사냥의 진행",
            _ => "잡담",
        };

        private static string TierKorean(AffinityTier tier) => tier switch
        {
            AffinityTier.Stranger => "낯선 사이",
            AffinityTier.Acquaintance => "안면 있는 사이",
            AffinityTier.Friend => "친구",
            AffinityTier.Confidant => "속을 터놓는 사이",
            _ => "낯선 사이",
        };

        private static string EncounterKorean(GreetingEncounter encounter) => encounter switch
        {
            GreetingEncounter.FirstMeeting => "처음 만남 — 플레이어를 오늘 처음 본다",
            GreetingEncounter.FamiliarFace => "몇 번 본 사이 — 얼굴이 막 익기 시작했다",
            GreetingEncounter.Regular => "자주 찾아오는 사이",
            GreetingEncounter.LongAbsent => "오랜만에 다시 찾아옴 — 한동안 보이지 않았다",
            GreetingEncounter.ImmediateRepeat => "방금 이야기를 나눈 직후 다시 말을 걸어옴",
            _ => "자주 찾아오는 사이",
        };
    }
}
