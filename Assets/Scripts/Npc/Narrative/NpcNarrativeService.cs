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
            string greeting = DialogueTopicSelector.SelectGreeting(npcId, relation, currentGameTime);
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
            if (topic.Type == DialogueTopicType.DungeonRequest || topic.Type == DialogueTopicType.DungeonRequestProgress ||
                topic.Type == DialogueTopicType.WishReveal || topic.Type == DialogueTopicType.WishReminder ||
                topic.Rumor?.IsDiscovery == true)
                return new DialogueResult { Text = ComposeDialogue(greeting, topic.BaseLine), Source = NarrativeSource.Fallback,
                    Tier = tier, Topic = topic.Type, NarrativeInstanceId = topic.NarrativeInstanceId,
                    IsNarrativeRequest = topic.Type == DialogueTopicType.DungeonRequest,
                    IsWishReveal = topic.Type == DialogueTopicType.WishReveal, NarrativeContentId = topic.NarrativeContentId };

            if (AR.s.Npc.Narrative.TryGetDialogue(npcId, tier, topic.Type, topic.CacheVariant, out string? cached))
                return new DialogueResult
                {
                    Text = ComposeDialogue(greeting, cached),
                    Source = NarrativeSource.Cache,
                    Tier = tier,
                    Topic = topic.Type,
                };

            string? text = null;
            if (AR.s.LLM != null && AR.s.LLM.IsReady)
            {
                List<ChatMessage> msgs = new()
                {
                    new ChatMessage("system", DialogueSystemPrompt()),
                    new ChatMessage("user", BuildDialoguePrompt(ctx, tier, relation, topic)),
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
                };

            string clean = text!.Trim();
            if (runtime.Lifetime == lifetime) runtime.SetDialogue(npcId, tier, topic.Type, topic.CacheVariant, clean);
            return new DialogueResult
            {
                Text = ComposeDialogue(greeting, clean),
                Source = NarrativeSource.Llm,
                Tier = tier,
                Topic = topic.Type,
            };
        }

        public static string BuildDialoguePrompt(NpcDialogueContext context, AffinityTier tier, PlayerRelation relation, DialogueTopic topic)
        {
            // Topic.Facts already projects the selected history, rumor or personal story. Sending the entire
            // backbone here mixes unrelated threats and can reveal facts the chosen speaker does not know.
            return "[NPC]\n" + context.NpcBlock + DialogueUserSuffix(tier, relation, topic);
        }

        // ── 프롬프트 ─────────────────────────────────────────────────────
        private static string DialogueSystemPrompt()
        {
            return
                "너는 이 판타지 세계에서 살아가는 NPC 본인이다. 아래 정보에 근거해 1인칭으로 말한다.\n" +
                "- 제공된 정보(NPC/HISTORY/RELATIONS/SENTIMENT/SITUATION)에 없는 인물·사건·장소·아이템을 지어내지 마라.\n" +
                "- NPC의 성별 정보에 맞는 자기 표현을 사용한다. 성별이 미상이면 추측하지 마라. 말투는 성격과 관계를 기준으로 정한다.\n" +
                "- 성격 수치에 맞는 어조로 말한다. 한국어. 대사 텍스트만 출력(해설·따옴표·이름표 금지).\n" +
                "- 관계 단계가 낮으면 표면적인 이야기만, 높을수록 더 사적인 사연을 드러낸다.";
        }

        private static string DialogueUserSuffix(AffinityTier tier, PlayerRelation relation, DialogueTopic topic)
        {
            StringBuilder sb = new();
            sb.AppendLine();
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
            sb.AppendLine("관계형 인사말은 별도로 표시된다. 인사를 반복하지 말고, 위 주제로 제공된 사실만 사용해 이 NPC의 본문 대사 1~2문장을 한국어로 생성하라. 기본 대사보다 풍부하게, 성격·관계 단계와 관계 수치에 맞게.");
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
    }
}
