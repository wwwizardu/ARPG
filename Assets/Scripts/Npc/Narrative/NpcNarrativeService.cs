#nullable enable
using System;
using System.Collections.Generic;
using System.Text;
using Cysharp.Threading.Tasks;
using Newtonsoft.Json;
using ARPG.Manager;

namespace ARPG.Npc.Narrative
{
    /// <summary>
    /// 검증 스파이크: 백본 문맥 → LLM(Groq/Llama) → 대화·소망 생성. static·무상태(순수 로직만).
    /// 대화·소망 캐시와 화제 셔플백은 NpcManager.Narrative(NpcNarrativeRuntime)가 소유 — 새 게임·씬 로드 시 Reset로 정리.
    /// 관계 실반영/세이브는 이번 스코프 밖. 티어는 호출측 지정.
    /// 설계: Docs/world_history/NARRATIVE_ENGINE_SPIKE.md
    /// </summary>
    public static class NpcNarrativeService
    {
        private const float DialogueTemperature = 0.8f;
        private const float WishTemperature = 0.7f;

        // ── 대화 ──────────────────────────────────────────────────────────
        public static async UniTask<DialogueResult> GetDialogueAsync(int npcId, AffinityTier tier, ThreatSeed threat)
        {
            NpcDialogueContext? ctx = NpcDialogueContextBuilder.Build(npcId, threat);
            if (ctx == null)
                return new DialogueResult { Text = "…", Source = NarrativeSource.Fallback, Tier = tier, Topic = DialogueTopicType.Greeting };

            // 토픽 선택 (상태·백본 → 적용 토픽 추림 → 티어 게이트 + 레퍼토리 캡 + 긴급 새치기·셔플백 1개)
            List<DialogueTopic> topics = DialogueTopicSelector.Collect(npcId, ctx, threat);
            DialogueTopic topic = DialogueTopicSelector.Select(npcId, topics, tier, AR.s.Npc.Narrative.GetRecentTopics(npcId));

            if (AR.s.Npc.Narrative.TryGetDialogue(npcId, tier, topic.Type, topic.CacheVariant, out string cached))
                return new DialogueResult { Text = cached, Source = NarrativeSource.Cache, Tier = tier, Topic = topic.Type };

            string? text = null;
            if (AR.s.LLM != null && AR.s.LLM.IsReady)
            {
                List<ChatMessage> msgs = new()
                {
                    new ChatMessage("system", DialogueSystemPrompt()),
                    new ChatMessage("user", ctx.ToPromptText() + DialogueUserSuffix(tier, topic)),
                };
                text = await AR.s.LLM.RequestWithContextAsync(msgs, LLMClient.MODEL_LLAMA_70B, DialogueTemperature);
            }

            // LLM 없거나 실패 → 선택 토픽의 BaseLine (LLM-optional)
            if (string.IsNullOrWhiteSpace(text))
                return new DialogueResult { Text = topic.BaseLine, Source = NarrativeSource.Fallback, Tier = tier, Topic = topic.Type };

            string clean = text!.Trim();
            AR.s.Npc.Narrative.SetDialogue(npcId, tier, topic.Type, topic.CacheVariant, clean);
            return new DialogueResult { Text = clean, Source = NarrativeSource.Llm, Tier = tier, Topic = topic.Type };
        }

        // ── 소망 ──────────────────────────────────────────────────────────
        public static async UniTask<NpcWish?> GetOrGenerateWishAsync(int npcId, AffinityTier tier, ThreatSeed threat)
        {
            if (tier < AffinityTier.Friend)   // Friend↑ 게이트
                return null;
            if (AR.s.Npc.Narrative.TryGetWish(npcId, out NpcWish cached))
                return cached;

            NpcDialogueContext? ctx = NpcDialogueContextBuilder.Build(npcId, threat);
            if (ctx == null)
                return null;

            NpcWish? wish = null;
            if (AR.s.LLM != null && AR.s.LLM.IsReady)
            {
                wish = await TryGenerateWishAsync(ctx, npcId);   // 1차
                if (wish == null)
                    wish = await TryGenerateWishAsync(ctx, npcId); // 재시도 1회
            }
            if (wish == null)
                wish = FallbackWish(ctx, npcId);   // 규칙 템플릿 폴백

            AR.s.Npc.Narrative.SetWish(npcId, wish);
            return wish;
        }

        private static async UniTask<NpcWish?> TryGenerateWishAsync(NpcDialogueContext ctx, int npcId)
        {
            List<ChatMessage> msgs = new()
            {
                new ChatMessage("system", WishSystemPrompt()),
                new ChatMessage("user", ctx.ToPromptText() + WishUserSuffix(ctx)),
            };
            string? raw = await AR.s.LLM.RequestWithContextAsync(msgs, LLMClient.MODEL_LLAMA_70B, WishTemperature);
            if (string.IsNullOrWhiteSpace(raw))
                return null;
            return ParseAndValidateWish(raw!, ctx, npcId);
        }

        // ── 검증 (R2: kind∈enum / target∈후보 / gold≤예산 / usedEventIds⊆문맥) ──
        private static NpcWish? ParseAndValidateWish(string raw, NpcDialogueContext ctx, int npcId)
        {
            try
            {
                string json = ExtractJson(raw);
                WishJson? w = JsonConvert.DeserializeObject<WishJson>(json);
                if (w == null)
                    return null;

                if (Enum.TryParse(w.Kind, true, out WishKind kind) == false)
                    return null;
                if (IsCandidate(ctx, w.Target) == false)
                    return null;

                int gold = w.Reward != null ? w.Reward.Gold : 0;
                if (gold < 0 || gold > ctx.RewardGoldMax)
                    return null;

                if (w.UsedEventIds != null)
                {
                    for (int i = 0; i < w.UsedEventIds.Count; i++)
                        if (ctx.ContextEventIds.Contains(w.UsedEventIds[i]) == false)
                            return null;
                }

                NpcWish wish = new()
                {
                    GiverNpcEntityId = npcId,
                    Kind = kind,
                    TargetRef = w.Target,
                    AffinityReward = w.AffinityReward,
                    RewardGold = gold,
                    RevealDialogue = w.RevealDialogue,
                    FulfillDialogue = w.FulfillDialogue,
                    State = WishState.Revealed,
                    ContextEventIds = w.UsedEventIds ?? new List<int>(),
                };
                if (w.Reward != null && w.Reward.Items != null)
                    wish.RewardItems = w.Reward.Items;
                return wish;
            }
            catch
            {
                return null;   // 파싱 실패 → 상위에서 재시도/폴백
            }
        }

        private static bool IsCandidate(NpcDialogueContext ctx, string target)
        {
            if (string.IsNullOrEmpty(target))
                return false;
            for (int i = 0; i < ctx.Candidates.Count; i++)
                if (ctx.Candidates[i].Ref == target)
                    return true;
            return false;
        }

        private static NpcWish FallbackWish(NpcDialogueContext ctx, int npcId)
        {
            string target = ctx.Candidates.Count > 0 ? ctx.Candidates[0].Ref : "item#heritage";
            string desc = ctx.Candidates.Count > 0 ? ctx.Candidates[0].Desc : "의미 있는 물건";
            return new NpcWish
            {
                GiverNpcEntityId = npcId,
                Kind = WishKind.ItemGift,
                TargetRef = target,
                AffinityReward = 20,
                RewardGold = 100,
                RevealDialogue = $"…{desc}이 있으면 좋겠소.",
                FulfillDialogue = "고맙소. 이 은혜는 잊지 않겠소.",
                State = WishState.Revealed,
                IsFallback = true,
            };
        }

        // ── 프롬프트 ─────────────────────────────────────────────────────
        private static string DialogueSystemPrompt()
        {
            return
                "너는 이 판타지 세계에서 살아가는 NPC 본인이다. 아래 정보에 근거해 1인칭으로 말한다.\n" +
                "- 제공된 정보(NPC/HISTORY/RELATIONS/SENTIMENT/SITUATION)에 없는 인물·사건·장소·아이템을 지어내지 마라.\n" +
                "- 성격 수치에 맞는 어조로 말한다. 한국어. 대사 텍스트만 출력(해설·따옴표·이름표 금지).\n" +
                "- 관계 단계가 낮으면 표면적인 이야기만, 높을수록 더 사적인 사연을 드러낸다.";
        }

        private static string DialogueUserSuffix(AffinityTier tier, DialogueTopic topic)
        {
            StringBuilder sb = new();
            sb.AppendLine();
            sb.AppendLine($"[현재 관계 단계] {TierKorean(tier)}");
            sb.AppendLine($"[대화 주제] {TopicKorean(topic.Type)}");
            if (topic.Facts.Count > 0)
            {
                sb.AppendLine("[주제 관련 사실]");
                for (int i = 0; i < topic.Facts.Count; i++)
                    sb.AppendLine($"- {topic.Facts[i]}");
            }
            sb.AppendLine($"[기본 대사(참고)] {topic.BaseLine}");
            sb.AppendLine("위 주제로, 제공된 사실만 사용해 이 NPC의 대사 1~2문장을 한국어로 생성하라. 기본 대사보다 풍부하게, 성격·관계 단계에 맞게.");
            return sb.ToString();
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
            _ => "잡담",
        };

        private static string WishSystemPrompt()
        {
            return
                "너는 이 판타지 세계에서 이 NPC의 사연을 아는 작가다.\n" +
                "- 제공된 정보에 없는 인물·사건·장소·아이템을 지어내지 마라.\n" +
                "- 소망은 이 NPC의 사연(HISTORY/RELATIONS/SENTIMENT)에서 자연스럽게 비롯돼야 한다.\n" +
                "- kind는 [ItemGift, Recover, Reunion] 중 하나만 고른다.\n" +
                "- target은 CANDIDATES의 ref 중에서만 고른다(새 ref 금지).\n" +
                "- reward.gold는 REWARD_BUDGET 범위 안. usedEventIds는 HISTORY의 event 번호만.\n" +
                "- 서술은 한국어. 아래 JSON 스키마만 출력(코드블록·설명 금지):\n" +
                "{\"kind\":\"\",\"target\":\"\",\"affinityReward\":0,\"reward\":{\"gold\":0,\"items\":[]},\"revealDialogue\":\"\",\"fulfillDialogue\":\"\",\"usedEventIds\":[]}";
        }

        private static string WishUserSuffix(NpcDialogueContext ctx)
        {
            StringBuilder sb = new();
            sb.AppendLine();
            sb.AppendLine("[WISH_KINDS] ItemGift, Recover, Reunion");
            sb.AppendLine("[CANDIDATES] (ref | desc)");
            for (int i = 0; i < ctx.Candidates.Count; i++)
                sb.AppendLine($"- {ctx.Candidates[i].Ref} | {ctx.Candidates[i].Desc}");
            sb.AppendLine($"[REWARD_BUDGET] gold 0~{ctx.RewardGoldMax}");
            sb.AppendLine("위 정보로 이 NPC가 품을 법한 소망 1개를 위 JSON 스키마로 생성하라.");
            return sb.ToString();
        }

        private static string TierKorean(AffinityTier tier) => tier switch
        {
            AffinityTier.Stranger => "낯선 사이",
            AffinityTier.Acquaintance => "안면 있는 사이",
            AffinityTier.Friend => "친구",
            AffinityTier.Confidant => "속을 터놓는 사이",
            _ => "낯선 사이",
        };

        // ── JSON 파싱 보조 ───────────────────────────────────────────────
        private static string ExtractJson(string raw)
        {
            int start = raw.IndexOf('{');
            int end = raw.LastIndexOf('}');
            if (start >= 0 && end > start)
                return raw.Substring(start, end - start + 1);
            return raw;
        }

        private class WishJson
        {
            [JsonProperty("kind")] public string Kind = string.Empty;
            [JsonProperty("target")] public string Target = string.Empty;
            [JsonProperty("affinityReward")] public int AffinityReward;
            [JsonProperty("reward")] public RewardJson? Reward;
            [JsonProperty("revealDialogue")] public string RevealDialogue = string.Empty;
            [JsonProperty("fulfillDialogue")] public string FulfillDialogue = string.Empty;
            [JsonProperty("usedEventIds")] public List<int>? UsedEventIds;
        }

        private class RewardJson
        {
            [JsonProperty("gold")] public int Gold;
            [JsonProperty("items")] public List<string>? Items;
        }
    }
}
