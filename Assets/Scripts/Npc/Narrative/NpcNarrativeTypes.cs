#nullable enable
using System.Collections.Generic;
using System.Text;

namespace ARPG.Npc.Narrative
{
    // ── 대화 생성 타입 모음 (DTO·enum) ──
    // 설계: Docs/world_history/NARRATIVE_ENGINE_SPIKE.md
    // 소망 실행 기록은 NarrativeContentInstance(Kind=Wish)다 (NPC_NARRATIVE_CONTENT_DESIGN §7.1). LLM 소망 생성 스파이크는 2026-10-10에 지웠다.

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
        // 야망 소문 (NPC_AMBITION P2c, D60 — NpcSaveData.Rumors 접지, 노출 직전 IsRumorValid 검증). 끝에만 추가한다.
        DungeonRumor, ItemOwnerRumor,
        // NPC 간 절도 (NPC_AMBITION P4b, D106): 피해자 도난 사연(Personal) / 배신 소문(Betrayal, 야망 소문 1개 규칙 D82에 포함)
        TheftVictim, BetrayalRumor,
        // 던전 소문 부탁 (NPC_NARRATIVE_CONTENT_DESIGN §4.1): 소문 주인의 부탁 / 부탁 칸이 찬 동안의 진행 확인. 둘 다 대화 후보 고정(Pinned).
        DungeonRequest, DungeonRequestProgress,
        // 소망 (§2.2 Wish + Item): 털어놓기 / 열린 소망 떠올리기. 둘 다 대화 후보 고정(Pinned).
        WishReveal, WishReminder,
    }

    /// <summary>대화 토픽 1건 — 시스템이 상태·백본에서 산출. LLM 없으면 BaseLine, 있으면 이 토픽을 풍부하게 서술.</summary>
    public class DialogueTopic
    {
        public DialogueTopicType Type;
        public DialogueTopicCategory Category;
        public AffinityTier MinTier;         // 티어 게이트
        public bool Urgent;                  // 긴급(기아·위협) — 셔플백에서 우선 등장
        public bool Pinned;                  // 레퍼토리에 반드시 들지만 새치기하지 않고 다른 화제와 번갈아 나온다 (던전 소문 부탁·진행 확인·소망)
        public List<string> Facts = new();   // LLM에 줄 근거(이 토픽 관련 사실만)
        public string BaseLine = string.Empty; // LLM 없을 때 대사 (슬롯 치환 완료본)
        public int CacheVariant;             // 같은 토픽 타입 안의 내용 구분자(예: Deed 인덱스) — 대화 캐시 키에 포함, 새 사건이면 새 대사
        public NpcRumor? Rumor;              // 야망 소문 토픽의 근거 소문 (P2c) — 진단 검증용, 그 외 토픽은 null
        public string NarrativeInstanceId = string.Empty; // 부탁·진행 확인·소망 화제가 가리키는 콘텐츠 인스턴스
        public int NarrativeContentId;       // 소망 털어놓기 화제의 콘텐츠 (표시 뒤 이 콘텐츠로 소망 사건을 만든다)
        public NpcRumorPlace RumorPlace;     // 던전 소문의 장소 표현 종류 (P2c)
        public int RumorBearing = -1;        // 던전 소문의 8방위 (0 북 … 7 북서, -1 = 방향 없음)
    }

    /// <summary>소망 종류 (QUEST §2.1). 지금 실행하는 것은 ItemGift(콘텐츠 Wish + Item)뿐이다.</summary>
    public enum WishKind { ItemGift, Recover, Reunion }

    /// <summary>소망 진행 (QUEST §2.2). 저장되므로 값을 바꾸지 않는다. 소망 사건은 털어놓을 때 Revealed로 만든다.</summary>
    public enum WishState { Hidden, Revealed, Fulfilled, Failed }

    /// <summary>생성 결과 출처 — 폴백 발동 여부 확인용(R4).</summary>
    public enum NarrativeSource { Llm, Cache, Fallback }

    /// <summary>현재 상황(위협) 시드 — D2. VillageData.ThreatLevel(=0) 우회, 나중에 실데이터로 교체.</summary>
    public struct ThreatSeed
    {
        public bool NearbyMonsters;
        public string Note;
    }

    /// <summary>조립된 대화 생성 문맥 (DTO — 로직 없음).</summary>
    public class NpcDialogueContext
    {
        public int NpcId;
        public string NpcName = string.Empty;
        public string NpcBlock = string.Empty;       // 이름/직업/성격/설명
        public string HistoryBlock = string.Empty;   // 연대기 1줄들
        public string RelationsBlock = string.Empty; // 가족/스승/원한
        public string SentimentBlock = string.Empty; // 감정
        public string SituationBlock = string.Empty; // 현재 상황(위협 시드)
        public List<int> ContextEventIds = new();     // 문맥에 넣은 연대기 사건 번호

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
        public string NarrativeInstanceId;   // 부탁·소망 화제였으면 그 인스턴스 — 실제 표시 뒤 부탁 칸에 넣거나 소망 사건을 만든다
        public bool IsNarrativeRequest;
        public bool IsWishReveal;            // 소망 털어놓기 화제였음 — 실제 표시 뒤 NarrativeContentId로 소망 사건을 만든다
        public int NarrativeContentId;
    }
}
