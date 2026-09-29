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
    /// <summary>
    /// NPC의 영구 저장 데이터.
    /// 청크 비활성화 시 ECS 컴포넌트에서 스냅샷하여 보관하고,
    /// 청크 재활성화 시 이 데이터로 엔티티를 복원한다.
    /// </summary>
    public class NpcSaveData
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

        // 대사 회고와 지식 거리의 입력. 평범한 사냥 성공은 기록하지 않고 의미 있는 사건만 보존한다.
        public List<NpcDeedEntry> Deeds = new();

        // 사망 NPC의 관계를 현재 관계 그래프와 분리해 보존하는 회고용 스냅샷.
        public List<DeceasedRelationMemory> DeceasedRelations = new();

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
            if (Rumors == null)
                Rumors = new List<NpcRumor>();
            for (int i = Rumors.Count - 1; i >= 0; i--)
            {
                if (Rumors[i] == null)
                    Rumors.RemoveAt(i);
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
        }

        /// <summary>
        /// 원정 부상 1건을 누적 요약에 반영한다 (D91 ①·②, 가벼운 부상·중상 공통).
        /// </summary>
        public void RecordExpeditionWound(float when, NpcActivityKind activity)
        {
            WoundCount++;
            if (when >= LastWoundGameTime)
            {
                LastWoundGameTime = when;
                LastWoundActivity = activity == NpcActivityKind.DungeonDelve
                    ? NpcActivityKind.DungeonDelve
                    : NpcActivityKind.Hunt;
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
        /// 로드 직후 ItemData 내부 테이블 참조 복원. PlayerData.LoadCompleted와 동일 패턴.
        /// </summary>
        public void OnLoadCompleted()
        {
            EnsureSlotsInitialized();
            MigrateLegacyWoundedDeeds();

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
    /// 런타임에서는 NpcManager가 Dictionary&lt;NpcPairKey, int&gt;로 변환하여 보관.
    /// </summary>
    [Serializable]
    public class NpcAffinityEntry
    {
        public int NpcA;
        public int NpcB;
        public int Affinity;

        public NpcAffinityEntry() { }

        public NpcAffinityEntry(int npcA, int npcB, int affinity)
        {
            NpcA = npcA;
            NpcB = npcB;
            Affinity = affinity;
        }
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
}
