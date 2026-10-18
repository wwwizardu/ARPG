#nullable enable
using System;
using System.Collections.Generic;
using ARPG.Component;
using ARPG.Creature;
using ARPG.Data;
using ARPG.Utility;
using Newtonsoft.Json;
using UnityEngine;

namespace ARPG.Npc
{
    /// <summary>NPC 성별. 정본은 NpcTable.Gender(종류별 고정)이고 세이브에는 저장하지 않는다. Unknown은 표 미입력 표시다.</summary>
    public enum NpcGender
    {
        Unknown = 0,
        Male = 1,
        Female = 2,
    }

    /// <summary>
    /// NPC의 영구 저장 데이터.
    /// 청크 비활성화 시 ECS 컴포넌트에서 스냅샷하여 보관하고,
    /// 청크 재활성화 시 이 데이터로 엔티티를 복원한다.
    /// </summary>
    public partial class NpcSaveData
    {
        public int NpcTableId;
        public float PositionX;
        public float PositionY;
        public CharacterConditions Condition;
        public int EntityId;
        public bool IsActive;

        /// <summary>스폰 진행 중 플래그 (async CreateNpc 대기 구간). 이중 스폰 차단용. 세이브 대상 아님.</summary>
        [JsonIgnore]
        public bool IsSpawning;

        // 마을 시스템 데이터. JobType/SkillLevel은 flavor·장비 시드 용도로만 잔존 —
        // JobType은 모험가 특성이며 마을 생산 직업이 아니다 (VILLAGE_NPC_BOUNDARY_DESIGN §6).
        public int VillageId;
        public GlobalEnum.JobType JobType;
        public int SkillLevel;

        // 인스턴스별 고유 이름 — 생성 시 이름 풀에서 랜덤 선택, 이후 고정.
        // 같은 NpcTableId(공유 테이블 이름)라도 NPC마다 다른 이름이 보이도록 인스턴스에 둔다.
        public string Name = string.Empty;

        // 인스턴스별 flavor — 생성 시 직업 기반 풀에서 랜덤 선택, 이후 고정.
        // 같은 NpcTableId라도 NPC마다 다른 소개가 보이도록 데이터를 인스턴스에 둔다.
        public string Description = string.Empty;

        // NPC 고유 성향 (8가지 성격 스탯). 신규 생성 시 랜덤 부여 후 영속화.
        public NpcStatComponent NpcStat;

        // NPC 시뮬레이션 정본. ECS 엔티티가 없는 오프스크린 상태에서도 계속 전진한다.
        public NpcActivityState Activity = new();
        public float LastSimGameTime;

        // 주요 사건 이력. 대화는 PersonalStories의 최신 참조를 읽으며 반복 절도 피해는 이력에 누적하지 않는다.
        public List<NpcDeedEntry> Deeds = new();

        // 종류별 최신 개인 사연. 주요 이력은 인덱스로 참조하고 반복 절도 피해는 여기 한 건만 저장한다.
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore, DefaultValueHandling = DefaultValueHandling.Ignore)]
        public NpcPersonalStories? PersonalStories;

        // 사망 NPC의 관계를 현재 관계 그래프와 분리해 보존하는 회고용 스냅샷.
        public List<DeceasedRelationMemory> DeceasedRelations = new();

        // 사망으로 호감도 채널에서 빠진 동료와의 공동 활동 통계. 빈 목록은 저장하지 않는다.
        public List<NpcSharedActivityMemory> SharedActivityMemories = new();

        // NPC → 정착지 단위 감정 (피정복 반감/왕국 자부심 등). 관계 backfill(L2)이 시드. 보통 0~1개.
        // 설계: NPC_RELATION_HISTORY_DESIGN.md §4.2
        public List<NpcSentiment> Sentiments = new();

        // 장비 슬롯 (EquipSlotType.Max 길이) — null은 빈 슬롯.
        // 신규 NPC는 NpcEquipTable 기반으로 시드, 재로드는 기존 ItemData 그대로 복원.
        public ItemData?[] InventoryEquip = new ItemData?[(int)GlobalEnum.EquipSlotType.Max];

        // NPC 인벤토리 (NPC_INVENTORY_SLOTCOUNT_MAX 칸). 슬롯 인덱스가 의미를 가지므로 null 슬롯 유지.
        public List<ItemData?> Inventory = new List<ItemData?>();

        // 모험가 경제 (NPC_AMBITION_DESIGN §5, §12). 구 세이브에는 필드가 없어 기본값 0으로 로드된다.
        // 개인 소지금. Hunt는 골드를 주지 않고 전리품 판매로만 늘어난다 (D33). 시작값은 결정론 30~90G (D37).
        public int Gold;
        // 개인 상한의 기준 전투력. 초기 장비 롤이 비결정론이라 재계산할 수 없어 저장한다 (D38).
        // 0 이하 = 미초기화 → NpcManager가 생성 직후 또는 첫 로드 때 현재 장비로 1회 기록한다 (시작 Gold도 같은 시점).
        public float InitialPower;
        // 개인 편차. 개인 상한 = InitialPower × (1 + PowerCapBonus). 평범 +0.20, 강자(10%) +0.35~0.50 (§10.1).
        public float PowerCapBonus;
        // 마지막 상점 구매 게임시각 (NPC별 쇼핑 쿨다운 48h, §7.2). -1 = 구매 기록 없음 (구 세이브 포함).
        public float LastTradeGameTime = -1f;
        // 소문 (NPC_AMBITION_DESIGN §5.2·§7.4, P2b D70~D78). 최대 8개, 대상별 1개. 구 세이브는 필드가 없어 빈 리스트로 보정한다.
        // 유효성은 저장하지 않고 조회 시 NpcManager.IsRumorValid가 판정한다 (D72).
        public List<NpcRumor> Rumors = new();
        // 부탁 칸 (NPC_NARRATIVE_CONTENT_DESIGN §4.1). 플레이어에게 맡긴 던전 소문 하나. 비어 있으면 저장하지 않는다.
        // 칸이 찬 동안 새 소문 의뢰를 하지 않고 진행 확인 화제를 꺼낸다. 소문 목록(Rumors)과 따로 저장한다.
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore, DefaultValueHandling = DefaultValueHandling.Ignore)]
        public NpcNarrativeRequest? NarrativeRequest;
        // 단기 목표 (NPC_OBJECTIVE_DESIGN §9.1, D152). 지금 목표 1개. 없으면 null이고 저장하지 않는다.
        // 단계·기한은 정의 행과 사건·생성 시각에서 계산한다. 빠진 목표는 저장하지 않는다 (칸이 빌 때 다시 계산, §4.8).
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore, DefaultValueHandling = DefaultValueHandling.Ignore)]
        public NpcObjective? Objective;
        // 결말 화제용 최근 결말 1건 (§9.1). OutcomeTopicHours가 지나면 지운다. 없으면 저장하지 않는다.
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore, DefaultValueHandling = DefaultValueHandling.Ignore)]
        public NpcObjectiveOutcomeRecord? LastObjectiveOutcome;
        // 원정 부상 요약 (NPC_AMBITION_DESIGN D91 ①). 가벼운 부상은 Deed 없이 여기만 갱신하고, 중상은 SevereWound Deed와 함께 갱신한다.
        // 치료 여부는 저장하지 않는다. 기본값(부상 없음)은 저장하지 않아 부상 없는 NPC의 저장 크기는 늘지 않는다.
        // 구 세이브의 Wounded Deed는 로드 때 MigrateLegacyWoundedDeeds가 여기로 합친다.
        [JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)]
        public int WoundCount;
        // 마지막 원정 부상 게임시각. -1 = 부상 기록 없음.
        [System.ComponentModel.DefaultValue(-1f)]
        [JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)]
        public float LastWoundGameTime = -1f;
        // 마지막 원정 부상의 활동 (Hunt / DungeonDelve). None = 부상 기록 없음.
        [JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)]
        public NpcActivityKind LastWoundActivity;
        // 마지막 원정 부상을 입은 던전 (NPC_NARRATIVE_CONTENT_DESIGN §4.2 피해자 판정). 사냥 부상이면 비운다. 비어 있으면 저장하지 않는다.
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore, DefaultValueHandling = DefaultValueHandling.Ignore)]
        public string? LastWoundDungeonId;
        // 마지막 원정 부상을 입힌 네임드 위협 (NAMED_THREAT_DESIGN §6.3·§9.3 피해자 판정). 다른 부상이면 0. 0은 저장하지 않는다.
        [JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)]
        public int LastWoundThreatId;
        // 마지막 절도 시도 게임시각 (NPC_AMBITION_DESIGN D100 — 도둑 쿨다운 168h, 시도 기준). -1 = 시도 없음.
        // 같은 마을 쿨다운(72h)은 새 필드 없이 마을 명단의 이 값 최댓값으로 계산한다. 기본값은 저장하지 않는다.
        [System.ComponentModel.DefaultValue(-1f)]
        [JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)]
        public float LastTheftGameTime = -1f;
        // 도난 단서 (D103): 안 들킨 절도의 피해자에게 남는 {아이템 인스턴스, 실제 범인, 시각}. 최대 3건(넘치면 가장 오래된 것부터 지움).
        // 범인 사망·14일 경과 때 지운다(Rest 종료 때 정리, 매칭 때도 같은 조건을 본다). 검사 창에 표시하지 않는다. 빈 목록은 저장하지 않는다.
        public List<NpcTheftClue> TheftClues = new();
        // 사라진 터(폐허·합병) 소속으로 처음 확인된 게임시각 (NPC_AMBITION_DESIGN D139 — 체류 기한 72h의 기준).
        // -1 = 터 소속 아님/아직 확인 전. 기존 세이브는 로드 뒤 첫 활동 선택 때 그 시각으로 정한다. 이주 도착 때 -1로 되돌린다.
        [System.ComponentModel.DefaultValue(-1f)]
        [JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)]
        public float AbandonedSiteSinceGameTime = -1f;

        [JsonIgnore]
        public Vector2 Position
        {
            get => new Vector2(PositionX, PositionY);
            set { PositionX = value.x; PositionY = value.y; }
        }

        public NpcSaveData(int npcTableId, Vector2 position)
        {
            NpcTableId = npcTableId;
            Position = position;
            Condition = CharacterConditions.Normal;
            EntityId = -1;
            IsActive = false;
            VillageId = 0;
            JobType = GlobalEnum.JobType.None;
            SkillLevel = 0;
            Name = string.Empty;
            Description = string.Empty;
            NpcStat = new NpcStatComponent
            {
                Friendliness = UnityEngine.Random.Range(0, 101),
                Honesty = UnityEngine.Random.Range(0, 101),
                Greed = UnityEngine.Random.Range(0, 101),
                Loyalty = UnityEngine.Random.Range(0, 101),
                Courage = UnityEngine.Random.Range(0, 101),
                Curiosity = UnityEngine.Random.Range(0, 101),
                Pride = UnityEngine.Random.Range(0, 101),
                Patience = UnityEngine.Random.Range(0, 101),
            };
            EnsureSlotsInitialized();
        }

        /// <summary>
        /// 장비/인벤토리 배열을 기대 길이로 보정한다. 구 세이브(필드 없음) 로드 시에도 안전.
        /// 호출 측: 생성자, OnLoadCompleted.
        /// </summary>
        public void EnsureSlotsInitialized()
        {
            if (NarrativeRequest != null && string.IsNullOrEmpty(NarrativeRequest.InstanceId) == true)
                NarrativeRequest = null;
            // 단기 목표 null 보정 (§9.1). 정의 표 대조·시도 횟수 자르기는 표가 필요해 NpcManager.NormalizeObjectiveOnLoad가 한다.
            Objective?.EnsureInitialized();
            LastObjectiveOutcome?.EnsureInitialized();
            int slotMax = (int)GlobalEnum.EquipSlotType.Max;
            if (InventoryEquip == null || InventoryEquip.Length != slotMax)
            {
                var newArr = new ItemData?[slotMax];
                if (InventoryEquip != null)
                {
                    int copy = System.Math.Min(InventoryEquip.Length, slotMax);
                    for (int i = 0; i < copy; i++) newArr[i] = InventoryEquip[i];
                }
                InventoryEquip = newArr;
            }

            if (Inventory == null)
                Inventory = new List<ItemData?>();
            while (Inventory.Count < GlobalEnum.NPC_INVENTORY_SLOTCOUNT_MAX)
                Inventory.Add(null);

            // 구 세이브(필드 없음) 로드 시 null 방어
            if (Sentiments == null)
                Sentiments = new List<NpcSentiment>();
            if (Activity == null)
                Activity = new NpcActivityState();
            Activity.EnsureInitialized();
            if (Deeds == null)
                Deeds = new List<NpcDeedEntry>();
            for (int i = 0; i < Deeds.Count; i++)
                Deeds[i].EnsureInitialized();
            if (DeceasedRelations == null)
                DeceasedRelations = new List<DeceasedRelationMemory>();
            for (int i = DeceasedRelations.Count - 1; i >= 0; i--)
            {
                if (DeceasedRelations[i] == null)
                    DeceasedRelations.RemoveAt(i);
            }
            // 최신 절도 피해는 Deeds 밖에 따로 저장되므로 같은 null 보정을 따로 적용한다.
            PersonalStories?.TheftVictim?.EnsureInitialized();
            if (SharedActivityMemories == null)
                SharedActivityMemories = new List<NpcSharedActivityMemory>();
            if (Rumors == null)
                Rumors = new List<NpcRumor>();
            for (int i = Rumors.Count - 1; i >= 0; i--)
            {
                if (Rumors[i] == null)
                    Rumors.RemoveAt(i);
                else if (Rumors[i].InstanceId == null)
                    Rumors[i].InstanceId = string.Empty;
            }
            // 원정 부상 요약 보정 (D91). 횟수가 0이면 마지막 부상 정보도 비운다. 활동이 없으면 사냥으로 읽는다.
            if (WoundCount <= 0)
            {
                WoundCount = 0;
                LastWoundGameTime = -1f;
                LastWoundActivity = NpcActivityKind.None;
            }
            else if (LastWoundActivity != NpcActivityKind.DungeonDelve)
            {
                LastWoundActivity = NpcActivityKind.Hunt;
            }
            // 피해자 판정용 던전은 마지막 부상이 던전 원정일 때만 남긴다 (§4.2).
            if (LastWoundActivity != NpcActivityKind.DungeonDelve || string.IsNullOrEmpty(LastWoundDungeonId) == true)
                LastWoundDungeonId = null;
            // 네임드 부상은 사냥 부상이다 (§6.1). 다른 활동이거나 음수면 비운다.
            if (LastWoundActivity != NpcActivityKind.Hunt || LastWoundThreatId < 0)
                LastWoundThreatId = 0;
            // 절도 시도 시각 보정 (D100). 음수·NaN은 "시도 없음"(-1)으로 본다.
            if (float.IsNaN(LastTheftGameTime) == true || LastTheftGameTime < 0f)
                LastTheftGameTime = -1f;
            // 터 체류 시작 시각 보정 (D139). 음수·NaN은 "확인 전"(-1)으로 본다.
            if (float.IsNaN(AbandonedSiteSinceGameTime) == true || AbandonedSiteSinceGameTime < 0f)
                AbandonedSiteSinceGameTime = -1f;
            // 도난 단서 보정 (D103): 인스턴스 없음·범인 없음은 지우고, 상한(3)을 넘으면 오래된 것(앞쪽)부터 자른다.
            if (TheftClues == null)
                TheftClues = new List<NpcTheftClue>();
            for (int i = TheftClues.Count - 1; i >= 0; i--)
            {
                NpcTheftClue clue = TheftClues[i];
                if (clue == null || clue.ItemInstanceId == 0 || clue.ThiefId < 0)
                    TheftClues.RemoveAt(i);
            }
            if (TheftClues.Count > MaxTheftClues)
                TheftClues.RemoveRange(0, TheftClues.Count - MaxTheftClues);
        }

        // 도난 단서 상한 (D103)
        public const int MaxTheftClues = 3;

        // Newtonsoft 조건부 직렬화 규약: 빈 단서 목록은 저장하지 않는다 (로드 때 필드 초기값 = 빈 목록).
        public bool ShouldSerializeTheftClues() => TheftClues != null && TheftClues.Count > 0;

        public bool ShouldSerializeSharedActivityMemories() => SharedActivityMemories != null && SharedActivityMemories.Count > 0;

        /// <summary>
        /// 원정 부상 1건을 누적 요약에 반영한다 (D91 ①·②, 가벼운 부상·중상 공통).
        /// </summary>
        /// <param name="threatId">네임드 위협에게 입은 부상이면 그 네임드 ID (NAMED_THREAT_DESIGN §6.3). 아니면 0</param>
        public void RecordExpeditionWound(float when, NpcActivityKind activity, bool severe = false, string? dungeonId = null,
            int threatId = 0)
        {
            WoundCount++;
            if (when >= LastWoundGameTime)
            {
                LastWoundGameTime = when;
                (PersonalStories ??= new()).LastWoundSevere = severe;
                LastWoundActivity = activity == NpcActivityKind.DungeonDelve
                    ? NpcActivityKind.DungeonDelve
                    : NpcActivityKind.Hunt;
                // 던전 원정 부상이면 그 던전을, 사냥 부상이면 비운다. 한 칸을 덮어써 쌓이지 않는다 (NPC_NARRATIVE §4.2).
                LastWoundDungeonId = LastWoundActivity == NpcActivityKind.DungeonDelve && string.IsNullOrEmpty(dungeonId) == false
                    ? dungeonId
                    : null;
                // 마지막 부상이 네임드에게서 온 것일 때만 값이 있다 (LastWoundDungeonId와 같은 방식)
                LastWoundThreatId = LastWoundActivity == NpcActivityKind.Hunt && threatId > 0 ? threatId : 0;
            }
        }

        /// <summary>
        /// 구 세이브의 Wounded Deed(D91 이전 원정 부상)를 가벼운 부상으로 보고 누적 요약에 합친 뒤 목록에서 지운다 (D91 ④).
        /// 지운 뒤에는 대상이 없으므로 다시 불러도 결과가 같다. 스키마 버전은 그대로다. 합친 건수를 돌려준다.
        /// </summary>
        public int MigrateLegacyWoundedDeeds()
        {
            if (Deeds == null)
                return 0;

            int merged = 0;
            int write = 0;
            for (int read = 0; read < Deeds.Count; read++)
            {
                NpcDeedEntry deed = Deeds[read];
                if (deed != null && deed.Kind == NpcDeedKind.Wounded)
                {
                    RecordExpeditionWound(deed.When, deed.Activity);
                    merged++;
                    continue;
                }
                Deeds[write] = deed!;
                write++;
            }
            if (merged > 0)
                Deeds.RemoveRange(write, Deeds.Count - write);
            return merged;
        }

        /// <summary>
        /// 구 세이브의 같은 상대 PartyAffinityChanged Deed 중복을 상대별 1건으로 합친다 (D108). 시각 = 가장 최근, 변화량 = 합.
        /// 남는 1건은 그 상대의 목록상 마지막 Deed 자리에 있어 "최근 Deed" 순서가 유지된다. 합친 뒤에는 중복이 없으므로
        /// 다시 불러도 결과가 같다. 스키마 버전은 그대로다. 합쳐서 지운 건수를 돌려준다.
        /// </summary>
        public int MergeDuplicatePartyAffinityDeeds()
        {
            if (Deeds == null)
                return 0;

            int partyDeedCount = 0;
            for (int i = 0; i < Deeds.Count; i++)
            {
                if (Deeds[i] != null && Deeds[i].Kind == NpcDeedKind.PartyAffinityChanged)
                    partyDeedCount++;
            }
            if (partyDeedCount < 2)
                return 0;

            // 뒤에서부터: 상대별로 처음 만나는 Deed(목록상 마지막)를 남기고, 앞쪽 중복의 변화량·시각을 거기에 합친다.
            Dictionary<int, NpcDeedEntry> keptByPartner = new Dictionary<int, NpcDeedEntry>();
            int merged = 0;
            for (int i = Deeds.Count - 1; i >= 0; i--)
            {
                NpcDeedEntry deed = Deeds[i];
                if (deed == null || deed.Kind != NpcDeedKind.PartyAffinityChanged)
                    continue;
                int partnerId = GetPartyAffinityPartnerId(deed, EntityId);
                if (partnerId < 0)
                    continue;
                if (keptByPartner.TryGetValue(partnerId, out NpcDeedEntry? kept) == false)
                {
                    keptByPartner.Add(partnerId, deed);
                    continue;
                }
                kept.AffinityDelta += deed.AffinityDelta;
                if (deed.When > kept.When)
                    kept.When = deed.When;
                merged++;
            }
            if (merged == 0)
                return 0;

            int write = 0;
            for (int read = 0; read < Deeds.Count; read++)
            {
                NpcDeedEntry deed = Deeds[read];
                if (deed != null && deed.Kind == NpcDeedKind.PartyAffinityChanged)
                {
                    int partnerId = GetPartyAffinityPartnerId(deed, EntityId);
                    if (partnerId >= 0 && ReferenceEquals(keptByPartner[partnerId], deed) == false)
                        continue;
                }
                Deeds[write] = deed!;
                write++;
            }
            Deeds.RemoveRange(write, Deeds.Count - write);
            return merged;
        }

        /// <summary>
        /// PartyAffinityChanged Deed의 상대 EntityId. 구 기록의 참가자 = 본인 + 상대.
        /// 본인이 참가자에 없거나 상대가 없으면 -1 — 합치기·누적 대상에서 빠진다.
        /// </summary>
        public static int GetPartyAffinityPartnerId(NpcDeedEntry deed, int ownerEntityId)
        {
            int partnerId = -1;
            bool hasOwner = false;
            for (int i = 0; i < deed.Participants.Count; i++)
            {
                int participantId = deed.Participants[i];
                if (participantId == ownerEntityId)
                    hasOwner = true;
                else if (partnerId < 0)
                    partnerId = participantId;
            }
            return hasOwner == true ? partnerId : -1;
        }

        /// <summary>
        /// 로드 직후 ItemData 내부 테이블 참조 복원. PlayerData.LoadCompleted와 동일 패턴.
        /// </summary>
        public void OnLoadCompleted()
        {
            EnsureSlotsInitialized();
            MigrateLegacyWoundedDeeds();
            MergeDuplicatePartyAffinityDeeds();
            MigratePersonalStories();

            for (int i = 0; i < InventoryEquip.Length; i++)
                InventoryEquip[i]?.OnLoadCompleted();

            for (int i = 0; i < Inventory.Count; i++)
                Inventory[i]?.OnLoadCompleted();
        }

        public ItemData? GetEquipedItem(GlobalEnum.EquipSlotType slot)
        {
            return InventoryEquip[(int)slot];
        }

        public bool EquipItem(GlobalEnum.EquipSlotType slot, ItemData item, out ItemData? replacedItem)
        {
            replacedItem = null;
            if (item == null || item.Equipment == null)
                return false;

            GlobalEnum.EquipmentTypeMask allowedMask = EquipHelper.SlotToEquipmentTypeMask(slot);
            if (Utils.ContainsEquipmentType(allowedMask, item.Equipment.EquipType) == false)
                return false;

            if (InventoryEquip[(int)slot] != null)
                replacedItem = InventoryEquip[(int)slot];

            InventoryEquip[(int)slot] = item;
            return true;
        }

        public bool UnequipItem(GlobalEnum.EquipSlotType slot, out ItemData? unequippedItem)
        {
            unequippedItem = null;
            if (InventoryEquip[(int)slot] == null)
                return false;

            unequippedItem = InventoryEquip[(int)slot];
            InventoryEquip[(int)slot] = null;
            return true;
        }
    }

    /// <summary>
    /// 도난 단서 1건 (NPC_AMBITION_DESIGN D103). 안 들킨 절도의 피해자가 가진다 — 피해자는 범인을 모르지만, 그 인스턴스를 가리키는
    /// ItemOwner·ItemSold 소문을 받아들였고 소문 주체가 이 범인이면 그때 발각된다. 세 필드 모두 항상 값이 있다.
    /// </summary>
    [Serializable]
    public class NpcTheftClue
    {
        public int ItemInstanceId;
        public int ThiefId = -1;
        // 절도 시각 (14일 경과 판정·Betrayal 소문의 사건 시각)
        public float When;

        public NpcTheftClue() { }

        public NpcTheftClue(int itemInstanceId, int thiefId, float when)
        {
            ItemInstanceId = itemInstanceId;
            ThiefId = thiefId;
            When = when;
        }
    }

    /// <summary>
    /// NPC 쌍의 정규화된 키. 양방향 대칭 호감도용 (A↔B = B↔A).
    /// 작은 EntityId가 Lo, 큰 EntityId가 Hi에 들어가도록 생성자에서 정규화.
    /// </summary>
    public readonly struct NpcPairKey : IEquatable<NpcPairKey>
    {
        public readonly int Lo;
        public readonly int Hi;

        public NpcPairKey(int a, int b)
        {
            if (a < b)
            {
                Lo = a;
                Hi = b;
            }
            else
            {
                Lo = b;
                Hi = a;
            }
        }

        public bool Equals(NpcPairKey other) => Lo == other.Lo && Hi == other.Hi;
        public override bool Equals(object? obj) => obj is NpcPairKey other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(Lo, Hi);
        public static bool operator ==(NpcPairKey a, NpcPairKey b) => a.Equals(b);
        public static bool operator !=(NpcPairKey a, NpcPairKey b) => a.Equals(b) == false;
    }

    /// <summary>
    /// 직렬화용 호감도 엔트리. WorldData에 List 형태로 저장.
    /// 런타임에서는 NpcManager가 NPC 쌍당 한 엔트리로 보관. 공동 활동 통계가 없으면 기존 3필드만 저장한다.
    /// </summary>
    [Serializable]
    public class NpcAffinityEntry
    {
        public int NpcA;
        public int NpcB;
        public int Affinity;

        [JsonProperty(NullValueHandling = NullValueHandling.Ignore, DefaultValueHandling = DefaultValueHandling.Ignore)]
        public NpcSharedActivityStats? SharedActivities;

        public NpcAffinityEntry() { }

        public NpcAffinityEntry(int npcA, int npcB, int affinity)
        {
            NpcA = npcA;
            NpcB = npcB;
            Affinity = affinity;
        }

        public NpcAffinityEntry Copy() => new NpcAffinityEntry(NpcA, NpcB, Affinity)
        {
            SharedActivities = SharedActivities?.Copy(),
        };
    }

    /// <summary>함께 귀환한 NPC 쌍의 활동 통계. 구 Deed의 누적값은 보존하지만 과거 횟수는 추정하지 않는다.</summary>
    [Serializable]
    public class NpcSharedActivityStats
    {
        [JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)]
        public int HuntCount;
        [JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)]
        public int DungeonDelveCount;
        [JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)]
        public int DungeonClearCount;
        [JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)]
        public int AffinityGain;
        [System.ComponentModel.DefaultValue(-1f)]
        [JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)]
        public float LastGameTime = -1f;
        [JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)]
        public NpcActivityKind LastActivity;
        [JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)]
        public bool HasUncountedHistory;

        public NpcSharedActivityStats Copy() => (NpcSharedActivityStats)MemberwiseClone();

        public string Describe()
        {
            // 하지 않은 활동의 0회는 적지 않는다. 새 기록은 사냥·던전 원정 중 하나가 항상 1 이상이다.
            string counts = AppendCount(string.Empty, "사냥", HuntCount);
            counts = AppendCount(counts, "던전 원정", DungeonDelveCount);
            counts = AppendCount(counts, "클리어", DungeonClearCount);
            if (counts.Length == 0)
                counts = "횟수 기록 없음";
            return HasUncountedHistory == true ? $"기록된 활동: {counts} (이전 원정 경험 있음)" : counts;
        }

        private static string AppendCount(string counts, string label, int count)
        {
            if (count <= 0)
                return counts;
            return counts.Length == 0 ? $"{label} {count}회" : $"{counts} · {label} {count}회";
        }
    }

    /// <summary>활성 호감도에서 제거된 쌍의 원정 경험. 상대 이름과 통계를 소유 NPC에게 보존한다.</summary>
    [Serializable]
    public class NpcSharedActivityMemory
    {
        public int OtherNpcId;
        public string OtherNpcName = string.Empty;
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore, DefaultValueHandling = DefaultValueHandling.Ignore)]
        public int? LastAffinity;
        public NpcSharedActivityStats Activities = new();
    }

    /// <summary>
    /// 플레이어 한 명과 NPC 한 명 사이의 영속 관계 상태.
    /// 정본은 NpcManager이며, 청크 비활성화와 무관하게 WorldData에 저장된다.
    /// </summary>
    public struct PlayerRelation
    {
        public int Affinity;
        public int Trust;
        public int Fear;
        public int Intimacy;
        public int TrustCap;
        public int TalkCount;
        public float LastTalkGameTime;

        public static PlayerRelation Default => new PlayerRelation
        {
            Affinity = 0,
            Trust = 0,
            Fear = 0,
            Intimacy = 0,
            TrustCap = 100,
            TalkCount = 0,
            LastTalkGameTime = -1f,
        };
    }

    /// <summary>
    /// PlayerRelation의 직렬화 DTO. 한 명의 플레이어만 지원하므로 NPC ID만 키로 보관한다.
    /// </summary>
    [Serializable]
    public class PlayerNpcRelationEntry
    {
        public int NpcId;
        public int Affinity;
        public int Trust;
        public int Fear;
        public int Intimacy;
        public int TrustCap = 100;
        // nullable은 구 세이브의 "필드 없음"과 테스트 Reset의 명시적 0/-1을 구분하기 위한 DTO 경계다.
        public int? TalkCount;
        public float? LastTalkGameTime;

        public PlayerNpcRelationEntry() { }

        public PlayerNpcRelationEntry(int npcId, PlayerRelation relation)
        {
            NpcId = npcId;
            Affinity = relation.Affinity;
            Trust = relation.Trust;
            Fear = relation.Fear;
            Intimacy = relation.Intimacy;
            TrustCap = relation.TrustCap;
            TalkCount = relation.TalkCount;
            LastTalkGameTime = relation.LastTalkGameTime;
        }

        public PlayerRelation ToRelation()
        {
            int restoredTalkCount;
            if (TalkCount.HasValue)
            {
                restoredTalkCount = TalkCount.Value;
            }
            else
            {
                // 구 세이브에는 만남 이력이 없으므로 이미 관계가 있던 NPC를 첫 만남으로 되돌리지 않는다.
                restoredTalkCount = Affinity != 0 || Trust != 0 || Fear != 0 || Intimacy != 0 ? 1 : 0;
            }
            float restoredLastTalkGameTime = LastTalkGameTime ?? -1f;

            return new PlayerRelation
            {
                Affinity = Affinity,
                Trust = Trust,
                Fear = Fear,
                Intimacy = Intimacy,
                TrustCap = TrustCap,
                TalkCount = restoredTalkCount,
                LastTalkGameTime = restoredLastTalkGameTime,
            };
        }
    }

    /// <summary>
    /// NPC 부탁 칸 (NPC_NARRATIVE_CONTENT_DESIGN §4.1·§7.2). 플레이어에게 맡긴 던전 소문 인스턴스와 부탁한 게임 시각.
    /// 보고·미해결 종료·칸 주인 사망 때 비운다. 이주해도 유지한다.
    /// </summary>
    [Serializable]
    public sealed class NpcNarrativeRequest
    {
        public string InstanceId = string.Empty;
        public float RequestedGameTime;
    }
}
