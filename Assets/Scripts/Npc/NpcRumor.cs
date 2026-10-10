#nullable enable
using System;
using System.ComponentModel;
using Newtonsoft.Json;

namespace ARPG.Npc
{
    /// <summary>
    /// NPC 소문 종류 (NPC_AMBITION_DESIGN §5.2, D50). 모든 소문은 사실이다 (D51) — 거짓 여부 필드가 없다.
    /// 새 종류는 끝에만 추가한다 (세이브는 enum 이름으로 저장).
    /// </summary>
    public enum NpcRumorKind
    {
        None = 0,
        // 소속 마을 던전 슬롯의 한 세대에 대한 사실 (원정 성공 보고 / NPC 완료 뒤 새 세대)
        DungeonInfo = 1,
        // 어떤 NPC가 어떤 장비를 장착하고 있다는 사실 (업그레이드 장착 시 생성)
        ItemOwner = 2,
        // 어떤 NPC가 어떤 아이템을 상점에 팔았다는 사실 (D104). 훔친 물건을 팔 때만 생긴다. 이미 일어난 판매라 뒤집히지 않는다.
        ItemSold = 3,
        // 배신 (D105, 공용). 세부 종류(BetrayalKind)로 구분한다 — P4 절도 현장 적발·늦은 발각, P3 배신 유형은 세부 종류 끝에 추가.
        Betrayal = 4,
        // 플레이어가 한 일 (NPC_NARRATIVE_CONTENT_DESIGN §4.1). 세부 종류(PlayerDeedKind)로 구분한다 — 소문 해결·던전 클리어,
        // NPC_AMBITION P5 "살인자" 소문도 세부 종류 끝에 추가한다. 이미 일어난 일이라 항상 유효하다.
        PlayerDeed = 5,
    }

    /// <summary>
    /// PlayerDeed 소문의 세부 종류 (NPC_NARRATIVE_CONTENT_DESIGN §1.1·§4.1). NPC 사건 기록(NpcDeedKind)과 다른 데이터다. 끝에만 추가한다.
    /// </summary>
    public enum PlayerDeedKind
    {
        None = 0,
        // 플레이어나 동행이 소문이 붙은 던전의 보스를 처치해 던전을 닫았다
        RumorResolved = 1,
        // 플레이어 측이 소문이 붙지 않은 던전의 보스를 처치했다
        DungeonCleared = 2,
        // NPC의 소망을 실제로 채웠다 (3단계)
        WishFulfilled = 3,
    }

    /// <summary>Betrayal 소문의 세부 종류 (D105). 끝에만 추가한다 (P3 배신 유형).</summary>
    public enum NpcBetrayalKind
    {
        None = 0,
        // 절도 현장 적발 — 물건은 옮겨지지 않았다
        TheftCaughtInAct = 1,
        // 절도 늦은 발각 — 물건이 옮겨진 뒤 범인이 드러났다
        TheftDiscovered = 2,
    }

    /// <summary>
    /// NPC 소문 1건 (P2b, D70). 계산할 수 있는 값은 저장하지 않는다:
    /// DungeonId는 (VillageId, SlotIndex, Generation)으로 DungeonLayout.Id가 다시 만들고,
    /// ItemOwner의 아이템 내용(ItemId·이름)은 유효할 때 소유자 장비에서 읽는다.
    /// 유효성(대상이 지금도 존재하는가)은 저장하지 않고 NpcManager.IsRumorValid가 매번 판정한다 (D72).
    /// </summary>
    [Serializable]
    public class NpcRumor
    {
        public NpcRumorKind Kind;
        // DungeonInfo: true = "○○ 일행이 이 슬롯의 이전 던전을 끝냈다"(가리키는 세대는 완료 뒤 새 세대). false = 원정 성공 보고.
        public bool Cleared;
        // DungeonInfo: true = 소문 주인이 처음 알게 된 입구 발견 소문 (원정 보고가 아니다). 기본값은 기존 원정 소문이다.
        [JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)]
        public bool IsDiscovery;
        // 관련 콘텐츠 인스턴스 (NPC_NARRATIVE_CONTENT_DESIGN §7.2). 발견 소문 = 소문 던전의 인스턴스(일반 던전은 빈 값),
        // PlayerDeed = 해결한 소문 인스턴스(소문 없는 던전 클리어는 빈 값).
        [DefaultValue("")]
        [JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)]
        public string InstanceId = string.Empty;
        // PlayerDeed 세부 종류. None = PlayerDeed가 아님.
        [JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)]
        public PlayerDeedKind DeedKind;
        // PlayerDeed: 이 NPC가 이미 반응했다. 소문이 퍼질 때 옮기지 않는다 (§4.1).
        [JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)]
        public bool Reacted;
        // DungeonInfo 대상: 던전 슬롯의 마을·슬롯 번호·세대. 현재 슬롯 세대와 같을 때만 유효하다.
        public int VillageId = -1;
        public int SlotIndex = -1;
        public int Generation = -1;
        // DungeonInfo: 원정 당사자(파티면 리더) / ItemOwner: 소유자
        public int SubjectNpcId = -1;
        // ItemOwner 대상: 소유자 장비 슬롯(EquipSlotType 인덱스)과 그 자리 아이템의 인스턴스 Id
        public int EquipSlot = -1;
        public int ItemInstanceId;
        // 실제 가치(BasePrice × 수량). DungeonInfo = 당사자가 실제로 얻은 전리품, ItemOwner = 그 아이템. 유망 판정·교체 우선순위.
        public int Value;
        // 사건이 일어난 게임시각. 전달돼도 바뀌지 않는다 (신선도·같은 사건 판정·교체 기준).
        public float EventGameTime;
        // 누구에게 들었나. -1 = 직접 겪음.
        public int SourceNpcId = -1;
        // 전달 횟수. 0 = 직접, 1 = 당사자·목격자에게 들음, 2+ = 건너 들음. 제한 없이 기록만 한다 (사용자 결정 2026-09-29).
        public int Hops;
        // 확인 원정을 다녀옴 — 유망 판정에서 빠진다. 같은 사건을 다시 받아도 되살아나지 않는다.
        public bool Checked;
        // ---- P4b (D104·D105). 아래 필드는 기본값이면 저장하지 않는다 — 기존 소문의 JSON 크기는 그대로다. ----
        // Betrayal: 피해자 EntityId (가해자 = SubjectNpcId). -1 = 없음.
        [DefaultValue(-1)]
        [JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)]
        public int VictimNpcId = -1;
        // Betrayal 세부 종류. None = Betrayal이 아님.
        [JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)]
        public NpcBetrayalKind BetrayalKind;
        // ItemSold·Betrayal: 표시용 아이템 테이블 Id (이름은 테이블에서 읽는다). 물건이 이미 주인을 떠났을 수 있어 인스턴스로는 못 찾는다.
        // ItemOwner는 소유자 장비에서 읽으므로 쓰지 않는다. -1 = 없음.
        [DefaultValue(-1)]
        [JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)]
        public int ItemId = -1;

        public NpcRumor Clone()
        {
            return (NpcRumor)MemberwiseClone();
        }
    }

    /// <summary>
    /// 던전 소문의 장소 표현 (P2c, D60 개정 — 마을에 이름이 없으므로 말하는 NPC 마을 중심 기준 방향으로만 말한다). 저장하지 않는다.
    /// </summary>
    public enum NpcRumorPlace
    {
        // 슬롯·마을을 찾지 못함 (무효 소문 — 노출하면 안 됨)
        Unavailable = 0,
        // 말하는 NPC의 마을이 아닌 던전 (D75상 지금은 생기지 않음 — 대화 토픽에서 제외)
        OtherVillage,
        // 입구가 아직 배치되지 않았거나 마을 중심과 겹침 → "이 근처"
        Nearby,
        // 입구 좌표가 있어 8방위로 말할 수 있음
        Bearing,
    }

    /// <summary>소문 추가 결과 (진단 집계용).</summary>
    public enum NpcRumorAddResult
    {
        Added = 0,
        // 같은 대상의 더 오래된 사건을 새 사건으로 교체
        Replaced,
        // 같은 대상의 같거나 더 새 사건을 이미 보유 — 받지 않음
        Duplicate,
        // 가득 차서 다른 소문을 버리고 추가
        AddedWithEviction,
        // 가득 차서 새 소문 자신이 가장 낮은 순위라 버림
        DroppedByCap,
        Invalid,
    }

    /// <summary>
    /// 소문 진단 카운터 (휘발성, 세이브 대상 아님). NpcManager 인스턴스가 소유하고 Reset에서 새로 만든다.
    /// 진단 도구가 시뮬레이션 동안 새 인스턴스로 바꿔 끼워 읽는다.
    /// </summary>
    public sealed class NpcRumorCounters
    {
        public long RestEnds;
        public long ExchangeRollsPassed;
        public long NoPeer;
        public long ExchangeAttempts;          // 상대 1명과의 교환 시도 (쌍 단위)
        public long ExchangesWithTransfer;     // 적어도 한쪽이 소문을 받아들인 쌍
        public long Offers;                    // 건넨 소문 수 (노출)
        public long Accepted;
        public long RejectedByCuriosity;
        public long NothingToOffer;            // 줄 유효 소문이 없거나 전부 상대가 이미 앎
        public long DuplicateSkips;            // 줄 후보 중 상대가 같거나 더 새 사건을 가져 건너뛴 수
        public long AddDuplicate;
        public long CapEvictions;
        public long CapDrops;
        public long CreatedDelve;
        public long CreatedCleared;
        public long CreatedItemOwner;
        public long CreatedItemSold;                                   // 훔친 물건 판매 (D104)
        public long CreatedPlayerDeed;                                 // 플레이어 행적 (보고·동행·소문 전 해결)
        public long AcceptedPlayerDeed;
        public readonly long[] CreatedBetrayal = new long[3];          // NpcBetrayalKind 인덱스 (D105)
        public long AcceptedItemSold;
        public long AcceptedBetrayal;
        public long ConfirmationDelves;        // 유망 소문 대상 슬롯으로 출발한 원정 (솔로·파티 1회씩)
        public long FilteredInvalidAtUse;      // 사용 직전 검증에서 걸러진 무효 소문
        public long PrunedInvalid;             // 정리로 지운 무효 소문
        public long Exposed;                   // 노출·사용된 소문 수 (교환 제공·확인 원정 대상·검사 UI)
        public long ExposedInvalid;            // 노출 시점 독립 감사가 무효라고 본 수 — 항상 0이어야 한다
        public readonly long[] ReceivedByHops = new long[4];   // 받아들인 소문의 새 Hops: 1 / 2 / 3 / 4+ (인덱스 0..3)
    }
}
