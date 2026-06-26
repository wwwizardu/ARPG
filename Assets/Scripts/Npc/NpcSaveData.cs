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
    /// Inn 시스템의 NPC 상태.
    /// Resident: 마을의 정식 거주자 (기본값, 기존 동작)
    /// InnVisitor: 여관에 머무는 방문자 — 플레이어가 고용해야 Resident로 승격
    /// </summary>
    public enum NpcStatus
    {
        Resident = 0,
        InnVisitor = 1,
    }

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

        // 마을 시스템 데이터
        public int VillageId;
        public GlobalEnum.JobType JobType;
        public int SkillLevel;

        // Inn 고용 시스템 (INN_HIRING_DESIGN.md §2.2)
        public NpcStatus Status;
        public int StayingAtVillageId;   // InnVisitor일 때만 의미 있음 (어느 마을 여관에 머무는가)
        public float ArrivedGameTime;    // InnVisitor일 때만 의미 있음 (만료 계산 기준)

        // 인스턴스별 flavor — 생성 시 직업 기반 풀에서 랜덤 선택, 이후 고정.
        // 같은 NpcTableId라도 NPC마다 다른 소개가 보이도록 데이터를 인스턴스에 둔다.
        public string Description = string.Empty;

        // NPC 고유 성향 (8가지 성격 스탯). 신규 생성 시 랜덤 부여 후 영속화.
        public NpcStatComponent NpcStat;

        // NPC → 정착지 단위 감정 (피정복 반감/왕국 자부심 등). 관계 backfill(L2)이 시드. 보통 0~1개.
        // 설계: NPC_RELATION_HISTORY_DESIGN.md §4.2
        public List<NpcSentiment> Sentiments = new();

        // 장비 슬롯 (EquipSlotType.Max 길이) — null은 빈 슬롯.
        // 신규 NPC는 NpcEquipTable 기반으로 시드, 재로드는 기존 ItemData 그대로 복원.
        public ItemData?[] InventoryEquip = new ItemData?[(int)GlobalEnum.EquipSlotType.Max];

        // NPC 인벤토리 (NPC_INVENTORY_SLOTCOUNT_MAX 칸). 슬롯 인덱스가 의미를 가지므로 null 슬롯 유지.
        public List<ItemData?> Inventory = new List<ItemData?>();

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
            Status = NpcStatus.Resident;
            StayingAtVillageId = 0;
            ArrivedGameTime = 0f;
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
        }

        /// <summary>
        /// 로드 직후 ItemData 내부 테이블 참조 복원. PlayerData.LoadCompleted와 동일 패턴.
        /// </summary>
        public void OnLoadCompleted()
        {
            EnsureSlotsInitialized();

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
}
