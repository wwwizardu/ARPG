#nullable enable
using System;
using System.ComponentModel;
using Newtonsoft.Json;

namespace ARPG.Npc
{
    // 단기 목표의 동작 종류 (NPC_OBJECTIVE_DESIGN §4.2, D147). 정의 표(시트 NpcObjective)는 이 이름으로 동작을 고른다.
    // 새 목표는 대부분 표의 행만 더한다. 새 계기·대상·완료 조건이 필요할 때만 값과 처리 코드를 하나씩 더한다. 모든 enum은 끝에만 추가한다.

    /// <summary>목표 분류. 값이 클수록 우선한다 (D143 — 강제 > 극적 > 일상, 같은 분류 안은 표의 Priority).</summary>
    public enum NpcObjectiveCategory
    {
        None = 0,
        Daily = 1,
        Dramatic = 2,
        Forced = 3,
    }

    /// <summary>계기 종류 (§4.3). 사건형은 사건 시점 1회 + 칸이 빌 때 다시 계산, 조건형은 Idle 전환마다 판정한다.</summary>
    public enum NpcObjectiveTrigger
    {
        None = 0,
        // 조건형: 유망한 소문 보유 (D76 확인 원정 이관, §5.1)
        PromisingRumor = 1,
        // 사건형: 거래 종료 때 예산만 넘어서 못 산 업그레이드 매물 (§5.2)
        UnaffordableUpgrade = 2,
        // 사건형: 같은 마을 관계자가 던전 원정에서 죽음 (§6.1)
        CompanionFellInDungeon = 3,
        // 사건형: 본인이 던전 원정에서 중상 (§6.1)
        SevereWoundInDungeon = 4,
    }

    /// <summary>대상 종류.</summary>
    public enum NpcObjectiveTargetKind
    {
        None = 0,
        Dungeon = 1,
        ShopItem = 2,
    }

    /// <summary>완료(이룸) 조건 (§4.5·§6.4).</summary>
    public enum NpcObjectiveComplete
    {
        None = 0,
        // 대상 던전 원정에서 살아 돌아옴
        VisitDungeon = 1,
        // 거래에서 대상 장비 칸을 업그레이드함 (같은 물건이 아니어도 된다)
        UpgradeSlot = 2,
        // 대상 던전이 닫힘 (본인·남·플레이어)
        DungeonClosed = 3,
        // 대상 던전 원정 결과가 성공
        ExpeditionSuccess = 4,
    }

    /// <summary>성격 배율 규칙 (§6.2).</summary>
    public enum NpcObjectiveReactProfile
    {
        None = 0,
        Loyal = 1,
        Proud = 2,
    }

    /// <summary>준비 끝·기한의 기준 시각 (§4.1). Event면 다시 계산해도 같은 값이 나온다 (D150).</summary>
    public enum NpcObjectiveTimeFrom
    {
        Created = 0,
        Event = 1,
    }

    /// <summary>목표 단계. 정의 행과 기준 시각에서 계산하고 저장하지 않는다 (§4.4, D70).</summary>
    public enum NpcObjectiveStage
    {
        None = 0,
        Preparing = 1,
        Attempting = 2,
    }

    /// <summary>목표가 칸에서 나간 방식 (§2.2·§4.5). 저장하지 않는다 — 진단 집계와 결말 처리 분기용.</summary>
    public enum NpcObjectiveExit
    {
        None = 0,
        // 끝남 — 이룸·꺾임: 다시 생기지 않는다. RecordDeed면 결말 Deed.
        Fulfilled = 1,
        Abandoned = 2,
        // 빠짐 — 더 높은 목표에 밀림·대상 없어짐·이주: 기록 없음, 칸이 빌 때 다시 계산 대상.
        Dropped = 3,
        // 주인 사망: 목표를 지운다.
        OwnerDied = 4,
    }

    /// <summary>
    /// NPC의 지금 단기 목표 1개 (NPC_OBJECTIVE_DESIGN §9.1, D152). NpcSaveData.Objective — 없으면 null이고 저장하지 않는다.
    /// 단계·기한은 정의 행과 EventGameTime·CreatedGameTime에서 계산한다. 시도 횟수만 계산할 수 없어 저장한다(다시 생기면 0부터).
    /// 기본값인 필드는 저장하지 않는다.
    /// </summary>
    [Serializable]
    public sealed class NpcObjective
    {
        // NpcObjective 표 Id
        public int DefinitionId;
        // 계기 사건 시각 (TimeFrom = Event의 기준). 조건형(소문 확인)은 그 소문의 사건 시각.
        public float EventGameTime;
        // 칸에 들어온 시각 (TimeFrom = Created의 기준)
        public float CreatedGameTime;
        // Target = Dungeon: DungeonLayout.Id (시드, 마을, 슬롯, 세대)
        [DefaultValue("")]
        [JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)]
        public string TargetDungeonId = string.Empty;
        // Target = ShopItem: 기본 아이템 행 (ItemTable Id)
        [JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)]
        public int TargetBasicItemId;
        // Target = ShopItem: 업그레이드할 장비 칸 (EquipSlotType). -1 = 없음.
        [DefaultValue(-1)]
        [JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)]
        public int TargetEquipSlot = -1;
        // 목표를 세울 때 본 가격 (표시용)
        [JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)]
        public int TargetPrice;
        // 원수 갚기: 죽은 NPC. -1 = 없음.
        [DefaultValue(-1)]
        [JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)]
        public int SubjectNpcId = -1;
        // 원수 갚기: 사망자 이름 스냅샷
        [DefaultValue("")]
        [JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)]
        public string SubjectName = string.Empty;
        // 목표 던전으로 간 원정 횟수 (계산할 수 없는 유일한 진행 값)
        [JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)]
        public int Attempts;

        public void EnsureInitialized()
        {
            if (TargetDungeonId == null)
                TargetDungeonId = string.Empty;
            if (SubjectName == null)
                SubjectName = string.Empty;
        }
    }

    /// <summary>
    /// 결말 화제용 최근 결말 1건 (§9.1). NpcSaveData.LastObjectiveOutcome — OutcomeTopicHours가 지나면 지운다.
    /// 빠짐·주인 사망은 기록하지 않는다.
    /// </summary>
    [Serializable]
    public sealed class NpcObjectiveOutcomeRecord
    {
        public int DefinitionId;
        // false = 꺾임
        [JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)]
        public bool Fulfilled;
        // 남이 이뤘음 (원수 갚기 — 다른 NPC 파티·플레이어 측이 던전을 닫음)
        [JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)]
        public bool ByOther;
        // 던전 방향 문구 또는 실제로 산 아이템 이름
        [DefaultValue("")]
        [JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)]
        public string TargetDisplayName = string.Empty;
        [DefaultValue("")]
        [JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)]
        public string SubjectName = string.Empty;
        public float When;

        public void EnsureInitialized()
        {
            if (TargetDisplayName == null)
                TargetDisplayName = string.Empty;
            if (SubjectName == null)
                SubjectName = string.Empty;
        }
    }

    /// <summary>
    /// 단기 목표 진단 카운터 (휘발성, 세이브 대상 아님, NPC_OBJECTIVE_DESIGN §12 진단 지표). NpcManager 인스턴스가 소유하고
    /// Reset에서 새로 만든다. 진단 도구가 시뮬레이션 동안 새 인스턴스로 바꿔 끼워 읽는다.
    /// 배열 인덱스는 NpcObjectiveTrigger 값이다 (1단계는 계기 하나에 정의 하나).
    /// </summary>
    public sealed class NpcObjectiveCounters
    {
        public const int KindCount = 5;
        public readonly long[] Created = new long[KindCount];            // 칸에 들어감 (사건 시점·조건형, 다시 계산 포함)
        public readonly long[] Recalculated = new long[KindCount];       // 그중 다시 계산으로 돌아온 목표
        public readonly long[] Refreshed = new long[KindCount];          // 같은 종류 갱신으로 대상만 바뀜
        public readonly long[] ReactRolls = new long[KindCount];         // 사건 시점에 생성 확률을 굴린 판정 (극적)
        public readonly long[] ReactPassed = new long[KindCount];
        public readonly long[] RejectedByRank = new long[KindCount];     // 지금 목표보다 낮아 버린 후보
        public readonly long[] Fulfilled = new long[KindCount];
        public readonly long[] FulfilledByOther = new long[KindCount];   // 그중 남이 이룸 (원수 갚기)
        public readonly long[] Abandoned = new long[KindCount];
        public readonly long[] Dropped = new long[KindCount];
        public readonly long[] OwnerDied = new long[KindCount];
        public readonly long[] Displaced = new long[KindCount * KindCount];   // [새 계기 × KindCount + 밀린 계기]
        public long DungeonDeaths;              // 던전 원정 사망 (원수 갚기 계기)
        public long DungeonSevereWounds;        // 던전 원정 중상 (설욕 계기)
        public long UnaffordableUpgradeTrades;  // 예산만 넘어 못 산 업그레이드가 있던 거래 (장비 마련 계기)
        public long RevengeJudgments;           // 원수 갚기를 판정한 관계자 수 (사망 1건에 여러 명)
        public long ChainDeaths;                // 목표 주인이 목표 던전 원정에서 죽음 (연쇄 건수)
        public long ObjectiveDelves;            // 목표 던전으로 출발한 원정 (솔로·파티 1회씩)
        public long DeterminismChecks;          // 사건 시점 판정과 다시 계산의 대조
        public long DeterminismMismatches;      // 항상 0이어야 한다
    }
}
