#nullable enable
using System;
using System.Collections;
using System.Collections.Generic;
using Newtonsoft.Json;
using GE = GlobalEnum;


namespace ARPG.Tables
{
    [Serializable]
    public class TableBase
    {
        [JsonProperty("Id")] public int Id;

        public virtual void LoadLate()
        {

        }
    }

    [Serializable]
    public class CreatureTable : TableBase
    {
        [JsonProperty("Name")] public string Name = string.Empty;

        [JsonProperty("Stat")] public int StatId;

        [JsonProperty("PrefabName")] public string PrefabName = string.Empty;

        [JsonProperty("AnimationId")] public int AnimationId;

        [JsonIgnore] public StatTable Stat = null!;
        [JsonIgnore] public AnimationTable? AnimationData;

        public override void LoadLate()
        {
            StatTable? statTable = AR.s.Data?.GetStat(StatId);
            if (statTable != null)
            {
                Stat = statTable;
            }
            else
            {
                throw new Exception($"CreatureTable LoadLate - StatTable not found for Id: {StatId}");
            }

            AnimationTable? animTable = AR.s.Data?.GetAnimation(AnimationId);
            if (animTable != null)
            {
                AnimationData = animTable;
            }
        }
    }

    [Serializable]
    public class StatTable : TableBase
    {
        [JsonProperty("Str")] public int Str;
        [JsonProperty("Dex")] public int Dex;
        [JsonProperty("Int")] public int Int;
        [JsonProperty("MaxHp")] public int MaxHp;
        [JsonProperty("MaxMp")] public int MaxMp;
        [JsonProperty("HpGeneration")] public int HpGeneration;
        [JsonProperty("MpGeneration")] public int MpGeneration;
        [JsonProperty("AttackMin")] public int AttackMin;
        [JsonProperty("AttackMax")] public int AttackMax;
        [JsonProperty("CriRate")] public int CriRate;
        [JsonProperty("CriDamage")] public int CriDamage;
        [JsonProperty("MoveSpeed")] public int MoveSpeed;
        [JsonProperty("AttackSpeed")] public int AttackSpeed;
        [JsonProperty("CastSpeed")] public int CastSpeed;
        [JsonProperty("Defense")] public int Defense;
        [JsonProperty("FireResist")] public int FireResist;
        [JsonProperty("IceResist")] public int IceResist;
        [JsonProperty("LightningResist")] public int LightningResist;
        [JsonProperty("PoisonResist")] public int PoisonResist;
        [JsonProperty("Luck")] public int Luck;
        [JsonProperty("BloodingRate")] public int BloodingRate;
        [JsonProperty("IgniteRate")] public int IgniteRate;

        // 전투 시스템 확장 스탯 (2026-04-01 추가)
        [JsonProperty("Evasion")] public int Evasion;                     // 회피율 (%)
        [JsonProperty("BlockChance")] public int BlockChance;             // 막기 확률 (%)
        [JsonProperty("BlockReduction")] public int BlockReduction;       // 막기 데미지 감소 (%)
        [JsonProperty("SkillDamage")] public int SkillDamage;             // 스킬 데미지 배율 (%)
        [JsonProperty("CooldownReduction")] public int CooldownReduction; // 쿨타임 감소 (%)
        [JsonProperty("LifeSteal")] public int LifeSteal;                 // 생명력 흡수 (%)
        [JsonProperty("Thorns")] public int Thorns;                       // 반사 데미지 (고정값)
    }

    [Serializable]
    public class MonsterTable : CreatureTable
    {
        [JsonProperty("WeaponId")] public int WeaponId;
        [JsonProperty("AiTableId")] public int AiTableId;
        [JsonProperty("DropId")] public int DropId;
        [JsonProperty("DropRateBonus")] public int DropRateBonus;
        [JsonProperty("DropRarityBonus")] public int DropRarityBonus;
        [JsonProperty("Level")] public int Level;

        [JsonIgnore] public WeaponBaseStatTable? Weapon;
        [JsonIgnore] public AiTable? AiTable;


        public override void LoadLate()
        {
            base.LoadLate();

            WeaponBaseStatTable? equipmentTable = AR.s.Data?.WeaponBaseStatTable(WeaponId);
            if (equipmentTable != null)
            {
                Weapon = equipmentTable;
            }
            else
            {
                Weapon = null;
            }

            AiTable? aiTable = AR.s.Data?.GetAiTable(AiTableId);
            if (aiTable != null)
            {
                AiTable = aiTable;
            }
            else
            {
                AiTable = null;
            }
        }
    }

    [Serializable]
    public class NpcTable : CreatureTable
    {
        [JsonProperty("JobType")] public GE.JobType JobType;
        [JsonProperty("WeaponId")] public int WeaponId;
        [JsonProperty("AiTableId")] public int AiTableId;
        [JsonProperty("DropId")] public int DropId;
        [JsonProperty("DropRateBonus")] public int DropRateBonus;
        [JsonProperty("DropRarityBonus")] public int DropRarityBonus;

        [JsonIgnore] public WeaponBaseStatTable? Weapon;
        [JsonIgnore] public AiTable? AiTable;

        public override void LoadLate()
        {
            base.LoadLate();

            WeaponBaseStatTable? equipmentTable = AR.s.Data?.WeaponBaseStatTable(WeaponId);
            if (equipmentTable != null)
            {
                Weapon = equipmentTable;
            }
            else
            {
                Weapon = null;
            }

            AiTable? aiTable = AR.s.Data?.GetAiTable(AiTableId);
            if (aiTable != null)
            {
                AiTable = aiTable;
            }
            else
            {
                AiTable = null;
            }
        }
    }

    [Serializable]
    public class ItemTable : TableBase
    {
        [JsonProperty("Tier")] public int Tier;

        [JsonProperty("Name")] public string Name = string.Empty;

        [JsonProperty("ItemType")] public GE.ItemType ItemType;
        [JsonProperty("Category")] public GE.ItemCategory Category;

        [JsonProperty("Stackable")] public bool Stackable;
        [JsonProperty("MaxStack")] public int MaxStack;

        [JsonProperty("Description")] public string Description = string.Empty;

        [JsonProperty("DropRate")] public int DropRate;
        [JsonProperty("DropLevel")] public int DropLevel;

        [JsonProperty("BuildableItemId")] public int BuildableItemId;
        [JsonProperty("EquipmentFixedStatId")] public int EquipmentBastStatId;
        [JsonProperty("EquipmentStatId")] public int EquipmentStatId;
        [JsonProperty("SpriteName")] public string SpriteName = string.Empty;
    }

     [Serializable]
    public class BuildableItemTable : TableBase
    {
        [JsonProperty("Name")] public string Name = string.Empty;
        [JsonProperty("Tooltip")] public string Tooltip = string.Empty;
        [JsonProperty("IsBreakable")] public bool IsBreakable = false;
        [JsonProperty("HP")] public int HP = 1;
        [JsonProperty("DropItemId")] public int DropItemId = 0;
        [JsonProperty("Size_Width")] public int Size_Width = 1;
        [JsonProperty("Size_Height")] public int Size_Height = 1;
        [JsonProperty("Recipe")] public int Recipe = 0;
        [JsonProperty("Function")] public int Function = 0;
        [JsonProperty("ResourceName")] public string ResourceName = string.Empty;
    }

    // EquipmentBaseStatTable 제거됨 → ModTable + ItemImplicitTable로 대체

    [Serializable]
    public class WeaponBaseStatTable : TableBase
    {
        [JsonProperty("EquipType")] public GE.EquipmentType EquipType;
        [JsonProperty("AttackSpeed")] public float AttackSpeed;
        [JsonProperty("Critical")] public int Critical;
        [JsonProperty("DamageMin")] public int DamageMin;
        [JsonProperty("DamageMax")] public int DamageMax;

        public WeaponBaseStatTable()
        {

        }
    }


    // EquipmentStatTable 제거됨 → ModTable (Prefix/Postfix)로 대체

    [Serializable]
    public class DropTable : TableBase
    {
        [JsonProperty("Tier")] public int Tier;                             // Drop 아이템 티어
        [JsonProperty("DropRate")] public int NothingRate;                  // 아무것도 안떨어질 확률
        [JsonProperty("CurrencyRate")] public int CurrencyRate;             // Drop 화폐 확률
        [JsonProperty("CurrencyId")] public int CurrencyId;                 // Drop 화폐 테이블 Id
        [JsonProperty("CurrencyPoolMode")] public int CurrencyPoolMode;     // 0=Explicit, 1=Pool
        [JsonProperty("EquipmentRate")] public int EquipmentRate;           // Drop 장비 확률
        [JsonProperty("EquipmentId")] public int EquipmentId;               // Drop 장비 테이블 Id
        [JsonProperty("EquipmentPoolMode")] public int EquipmentPoolMode;   // 0=Explicit, 1=Pool
    }

    [Serializable]
    public class DropCurrencyTable : TableBase
    {
        [JsonProperty("Tier")] public int Tier;                 // Drop 아이템 티어

        [JsonProperty("DropList")] public List<DropInfo>? DropList;

    }

    [Serializable]
    public class DropEquipmentTable : TableBase
    {
        [JsonProperty("Tier")] public int Tier;                 // Drop 아이템 티어
        [JsonProperty("DropList")] public List<DropInfo>? DropList;

    }

    [Serializable]
    public class SkillTable : TableBase
    {
        [JsonProperty("Name")] public string Name = string.Empty;               // 이름
        [JsonProperty("Desctiption")] public string Desctiption = string.Empty; // 설명
        [JsonProperty("Tags")] public GE.SkillTag Tags;                           // 스킬 태그 (비트 플래그)
        [JsonProperty("SkillType")] public GE.SkillType SkillType;              // 스킬 타입
        [JsonProperty("SubType")] public GE.SkillSubType SubType;               // 스킬 서브 타입
        [JsonProperty("SkillRangeMin")] public float SkillRangeMin;             // 스킬 최소 사정 거리
        [JsonProperty("SkillRangeMax")] public float SkillRangeMax;             // 스킬 최대 사정 거리
        [JsonProperty("Cooltime")] public float Cooltime;                       // 쿨 타임
        [JsonProperty("Mana")] public int Mana;                                 // 마나 소모량
        [JsonProperty("DamageTime")] public float DamageTime;                   // 데미지 입히는 시간
        [JsonProperty("DamageType")] public GE.DamageType DamageType;           // 데미지 입히는 시간
        [JsonProperty("DamageMin")] public int DamageMin;                       // 데미지 최소값
        [JsonProperty("DamageMax")] public int DamageMax;                       // 데미지 최대값
        [JsonProperty("Duration")] public int Duration;                         // 지속 시간
        [JsonProperty("SkillTargetType")] public GE.SkillTargetType SkillTargetType; // 스킬 타겟 타입
        [JsonProperty("SkillTargetRange1")] public float SkillTargetRange1;     // 스킬 타겟 타입 범위 1
        [JsonProperty("SkillTargetRange2")] public float SkillTargetRange2;     // 스킬 타겟 타입 범위 2
        [JsonProperty("AnimationName")] public string AnimationName = string.Empty;     // 애니메이션 이름
        [JsonProperty("StartEffectName")] public string StartEffectName = string.Empty; // 애니메이션 이름
        [JsonProperty("ActivateName")] public string ActivateName = string.Empty;       // 애니메이션 이름
        [JsonProperty("HitEffect")] public string HitEffect = string.Empty;             // 애니메이션 이름
        [JsonProperty("ProjectileId")] public int ProjectileId;                          // 발사체 테이블 ID (0이면 즉발)


    }

    [Serializable]
    public class ProjectileTable : TableBase
    {
        [JsonProperty("Name")] public string Name = string.Empty;           // 이름
        [JsonProperty("Speed")] public float Speed;                         // 이동 속도
        [JsonProperty("LifeTime")] public float LifeTime;                   // 최대 수명 (초)
        [JsonProperty("HitRadius")] public float HitRadius;                 // 충돌 반경
        [JsonProperty("IsPiercing")] public bool IsPiercing;                // 관통 여부
        [JsonProperty("PrefabKey")] public string PrefabKey = string.Empty; // Addressable 프리팹 키
    }

    [Serializable]
    public class AiTable : TableBase
    {
        [JsonProperty("Name")] public string Name = string.Empty;   // 이름
        [JsonProperty("AiType")] public GE.AiType AiType;           // Ai 타입
        [JsonProperty("BehaviorType")] public ARPG.Component.AIBehaviorType BehaviorType; // 행동 타입 (Melee/Ranged 등)
        [JsonProperty("DetectionRange")] public float DetectionRange; // 감지 범위
        [JsonProperty("SkillId1")] public int SkillId1;             // 스킬 1
        [JsonProperty("SkillId2")] public int SkillId2;             // 스킬 2
        [JsonProperty("SkillId3")] public int SkillId3;             // 스킬 3
    }

    [Serializable]
    public class BuffTable : TableBase
    {
        [JsonProperty("Name")] public string Name = string.Empty;               // 버프 이름
        [JsonProperty("Description")] public string Description = string.Empty; // 버프 설명
        [JsonProperty("BuffType")] public GE.BuffType BuffType;                 // 버프 타입 (버프/디버프)
        [JsonProperty("Duration")] public float Duration;                       // 지속 시간 (초)
        [JsonProperty("TickInterval")] public float TickInterval;               // 틱 간격 (DoT/HoT용, 0이면 즉시 적용)
        [JsonProperty("MaxStack")] public int MaxStack;                         // 최대 스택 수
        [JsonProperty("IsDispellable")] public bool IsDispellable;              // 해제 가능 여부
        [JsonProperty("BuffEffectId")] public GE.BuffEffectType EffectType;     // 버프 이팩트 타입
        [JsonProperty("EffectValue")] public int EffectValue;                   // 버프 이팩트 값
        
        //[JsonIgnore] public BuffEffectTable? BuffEffectTable;

        // public override void LoadLate()
        // {
        //     if (AR.s.Data != null)
        //     {
        //         BuffEffectTable? buffEffectTable = AR.s.Data.GetBuffEffect(BuffEffectId);
        //         if (buffEffectTable != null)
        //         {
        //             BuffEffectTable = buffEffectTable;
        //         }
        //         else
        //         {
        //             BuffEffectTable = null;
        //         }
        //     }
        //}
    }

    [Serializable]
    public class BuffEffectTable : TableBase
    {
        [JsonProperty("Name")] public string Name = string.Empty;               // 버프 이름
        [JsonProperty("BuffEffectList")] public List<BuffEffect>? BuffEffectList = null;          // 스탯 리스트
    }

    // Stat 구조체 제거됨 → ModInstance로 대체

    [Serializable]
    public class DropInfo
    {
        [JsonProperty("Type")] public int Id;
        [JsonProperty("Value")] public int Rate;
    }

    [Serializable]
    public class BuffEffect
    {
        [JsonProperty("Type")] public GE.BuffEffectType Type;
        [JsonProperty("Value")] public ushort Value;
    }

    /// <summary>
    /// Mod 정의 테이블 - 모든 장비 옵션의 기본 정보
    /// </summary>
    [Serializable]
    public class ModTable : TableBase
    {
        [JsonProperty("Name")] public string Name = string.Empty;
        [JsonProperty("EffectType")] public GE.ModEffectType EffectType;
        [JsonProperty("ApplyType")] public GE.ModApplyType ApplyType;
        [JsonProperty("Slot")] public GE.ModSlot Slot;
        [JsonProperty("Group")] public string Group = string.Empty;         // 같은 그룹 중복 불가
        [JsonProperty("Element")] public GE.DamageType Element;             // 속성 (데미지 계열)
        [JsonProperty("Tags")] public GE.SkillTag Tags;                     // 적용 조건 (Attack, Spell 등)
        [JsonProperty("TargetStat")] public GE.Stat TargetStat;             // FlatStat/IncreasedStat 대상
    }

    /// <summary>
    /// Mod 티어 테이블 - Mod별 값 범위 정의
    /// </summary>
    [Serializable]
    public class ModTierTable : TableBase
    {
        [JsonProperty("ModId")] public int ModId;
        [JsonProperty("Tier")] public int Tier;
        [JsonProperty("Min1")] public int Min1;               // Value1 최소
        [JsonProperty("Max1")] public int Max1;               // Value1 최대
        [JsonProperty("Min2")] public int Min2;               // Value2 최소 (데미지 Max 등)
        [JsonProperty("Max2")] public int Max2;               // Value2 최대
        [JsonProperty("RequiredLevel")] public int RequiredLevel;
        [JsonProperty("Weight")] public int Weight;           // 롤링 가중치

        [JsonIgnore] public ModTable? Mod;

        public override void LoadLate()
        {
            Mod = AR.s.Data?.GetMod(ModId);
        }
    }

    /// <summary>
    /// 아이템별 기본 내장(Implicit) Mod 매핑
    /// </summary>
    [Serializable]
    public class ItemImplicitTable : TableBase
    {
        [JsonProperty("ItemId")] public int ItemId;
        [JsonProperty("ModId")] public int ModId;
        [JsonProperty("Tier")] public int Tier;

        [JsonIgnore] public ModTable? Mod;
        [JsonIgnore] public ModTierTable? TierData;

        public override void LoadLate()
        {
            Mod = AR.s.Data?.GetMod(ModId);
            TierData = AR.s.Data?.GetModTier(ModId, Tier);
        }
    }

    /// <summary>
    /// 아이템에 실제로 부여된 Mod 인스턴스 (저장 대상)
    /// </summary>
    [Serializable]
    public class ModInstance
    {
        [JsonProperty("ModTableId")] public int ModTableId;
        [JsonProperty("Slot")] public GE.ModSlot Slot;
        [JsonProperty("Tier")] public int Tier;
        [JsonProperty("Value1")] public ushort Value1;
        [JsonProperty("Value2")] public ushort Value2;

        [JsonIgnore] public ModTable? Table;

        public void OnLoadCompleted()
        {
            Table = AR.s.Data?.GetMod(ModTableId);
        }
    }

    [Serializable]
    public class AnimationTable : TableBase
    {
        [JsonProperty("Name")] public string Name = string.Empty;
        [JsonProperty("SpriteLibraryPath")] public string SpriteLibraryPath = string.Empty;
        [JsonProperty("AnimClipPath")] public string AnimClipPath = string.Empty;
        [JsonProperty("ClipNames")] public string ClipNames = string.Empty;

        [JsonIgnore] public string[] ClipNameArray = Array.Empty<string>();

        public override void LoadLate()
        {
            if (string.IsNullOrEmpty(ClipNames) == false)
            {
                ClipNameArray = ClipNames.Split('|');
            }
        }
    }

}