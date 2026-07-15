#nullable enable
using System.Collections.Generic;
using System.Text;

namespace ARPG.Npc.Narrative
{
    // ── 검증 스파이크 타입 모음 (DTO·enum) ──
    // 설계: Docs/world_history/NARRATIVE_ENGINE_SPIKE.md
    // 소망(NpcWish)의 정본 데이터 모델은 QUEST_SYSTEM_DESIGN §2 (추후 ARPG.Quest로 이관). 여기선 스파이크 부분집합.

    /// <summary>호감도 단계 (interactionSystem §4.2). 스파이크는 디버그로 강제 전환.</summary>
    public enum AffinityTier { Stranger, Acquaintance, Friend, Confidant }

    /// <summary>대화 토픽 갈래. NARRATIVE_ENGINE_SPIKE §4.5 + 사연(NARRATIVE_DIALOGUE_VARIETY Phase 1).</summary>
    public enum DialogueTopicCategory { Neutral, Need, Rumor, Flavor, Personal }

    /// <summary>대화 토픽 (니즈·소문·잡담·사연). 사연(Personal)은 관계 계층(호감도 실반영) 구현으로 게이트 충족(2026-07-15).</summary>
    public enum DialogueTopicType
    {
        Greeting,
        // 니즈 (표현만 — 충족/보상 루프는 별개)
        ClearThreat, ResourceShortage, Food, CraftMaterial, Medicine,
        // 소문 (C-method: 진짜 사실 → 소문 말투만)
        HistoryRumor, SettlementRumor, Gossip, DangerRumor,
        // 잡담 (순수 색감)
        SmallTalk, JobMood,
        // 사연 (Personal — 개인 전기 Deeds·사별 기억 접지. NARRATIVE_DIALOGUE_VARIETY §5 Phase 1)
        RecentHunt, LostCompanion, Boast, PartyBond,
    }

    /// <summary>대화 토픽 1건 — 시스템이 상태·백본에서 산출. LLM 없으면 BaseLine, 있으면 이 토픽을 풍부하게 서술.</summary>
    public class DialogueTopic
    {
        public DialogueTopicType Type;
        public DialogueTopicCategory Category;
        public AffinityTier MinTier;         // 티어 게이트
        public bool Urgent;                  // 긴급(기아·위협) — 셔플백에서 우선 등장
        public List<string> Facts = new();   // LLM에 줄 근거(이 토픽 관련 사실만)
        public string BaseLine = string.Empty; // LLM 없을 때 대사 (슬롯 치환 완료본)
        public int CacheVariant;             // 같은 토픽 타입 안의 내용 구분자(예: Deed 인덱스) — 대화 캐시 키에 포함, 새 사건이면 새 대사
    }

    /// <summary>소망 종류 (닫힌 enum — LLM은 이 밖을 못 쓴다). QUEST §2.1.</summary>
    public enum WishKind { ItemGift, Recover, Reunion }

    public enum WishState { Hidden, Revealed, Fulfilled, Failed }

    /// <summary>생성 결과 출처 — 폴백 발동 여부 확인용(R4).</summary>
    public enum NarrativeSource { Llm, Cache, Fallback }

    /// <summary>소망 후보 (C-method: LLM은 이 ref 중에서만 고른다). QUEST §8.</summary>
    public struct WishCandidate
    {
        public string Ref;    // "npc#1857" / "item#heritage" 등
        public string Desc;   // "여동생 카운"

        public WishCandidate(string inRef, string inDesc)
        {
            Ref = inRef;
            Desc = inDesc;
        }
    }

    /// <summary>현재 상황(위협) 시드 — D2. VillageData.ThreatLevel(=0) 우회, 나중에 실데이터로 교체.</summary>
    public struct ThreatSeed
    {
        public bool NearbyMonsters;
        public string Note;
    }

    /// <summary>조립된 대화·소망 생성 문맥 (DTO — 로직 없음).</summary>
    public class NpcDialogueContext
    {
        public int NpcId;
        public string NpcName = string.Empty;
        public string NpcBlock = string.Empty;       // 이름/직업/성격/설명
        public string HistoryBlock = string.Empty;   // 연대기 1줄들
        public string RelationsBlock = string.Empty; // 가족/스승/원한
        public string SentimentBlock = string.Empty; // 감정
        public string SituationBlock = string.Empty; // 현재 상황(위협 시드)
        public List<int> ContextEventIds = new();     // usedEventIds ⊆ context 검증용
        public List<WishCandidate> Candidates = new();
        public int RewardGoldMax = 200;

        /// <summary>프롬프트 user 파트로 넣을 평문 문맥.</summary>
        public string ToPromptText()
        {
            StringBuilder sb = new();
            sb.AppendLine("[NPC]").AppendLine(NpcBlock);
            if (string.IsNullOrEmpty(HistoryBlock) == false)
                sb.AppendLine("[HISTORY]").AppendLine(HistoryBlock);
            if (string.IsNullOrEmpty(RelationsBlock) == false)
                sb.AppendLine("[RELATIONS]").AppendLine(RelationsBlock);
            if (string.IsNullOrEmpty(SentimentBlock) == false)
                sb.AppendLine("[SENTIMENT]").AppendLine(SentimentBlock);
            if (string.IsNullOrEmpty(SituationBlock) == false)
                sb.AppendLine("[SITUATION]").AppendLine(SituationBlock);
            return sb.ToString().TrimEnd();
        }
    }

    /// <summary>대화 생성 결과.</summary>
    public struct DialogueResult
    {
        public string Text;
        public NarrativeSource Source;
        public AffinityTier Tier;
        public DialogueTopicType Topic;
    }

    /// <summary>소망 1건 (스파이크 부분집합 — 정본은 QUEST §2).</summary>
    public class NpcWish
    {
        public int GiverNpcEntityId;
        public WishKind Kind;
        public string TargetRef = string.Empty;
        public int AffinityReward;
        public int RewardGold;
        public List<string> RewardItems = new();
        public string RevealDialogue = string.Empty;
        public string FulfillDialogue = string.Empty;
        public WishState State = WishState.Revealed;
        public List<int> ContextEventIds = new();
        public bool IsFallback;   // 규칙 템플릿 폴백으로 생성됐는가 (R2 확인용)
    }
}
