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

        [JsonIgnore] public StatTable Stat = null!;

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
    }

    [Serializable]
    public class MonsterTable : CreatureTable
    {
        [JsonProperty("WeaponId")] public int WeaponId;
        [JsonProperty("AiTableId")] public int AiTableId;
        [JsonProperty("DropId")] public int DropId;
        [JsonProperty("DropRateBonus")] public int DropRateBonus;
        [JsonProperty("DropRarityBonus")] public int DropRarityBonus;

        [JsonIgnore] public EquipmentTable? Weapon;
        [JsonIgnore] public AiTable? AiTable;


        public override void LoadLate()
        {
            base.LoadLate();

            EquipmentTable? equipmentTable = AR.s.Data?.GetEquipment(WeaponId);
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
        [JsonProperty("AiTableId")] public int AiTableId;
        [JsonProperty("DropId")] public int DropId;
        [JsonProperty("DropRateBonus")] public int DropRateBonus;
        [JsonProperty("DropRarityBonus")] public int DropRarityBonus;

        [JsonIgnore] public AiTable? AiTable;

        public override void LoadLate()
        {
            base.LoadLate();

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

        [JsonProperty("Stackable")] public bool Stackable;
        [JsonProperty("MaxStack")] public int MaxStack;

        [JsonProperty("Description")] public string Description = string.Empty;

        [JsonProperty("DropRate")] public int DropRate;

        [JsonProperty("EquipmentId")] public int EquipmentId;

        [JsonProperty("SpriteName")] public string SpriteName = string.Empty;

        [JsonIgnore] public EquipmentTable? Equipment;

        public override void LoadLate()
        {
            EquipmentTable? equipmentTable = AR.s.Data?.GetEquipment(EquipmentId);
            if (equipmentTable != null)
            {
                Equipment = equipmentTable;
            }
            else
            {
                Equipment = null;
            }
        }
    }

    [Serializable]
    public class EquipmentTable : TableBase
    {
        [JsonProperty("EquipType")] public GE.EquipSlotType EquipType;
        [JsonProperty("AttackSpeed")] public float AttackSpeed;
        [JsonProperty("Critical")] public int Critical;
        [JsonProperty("DamageMin")] public int DamageMin;
        [JsonProperty("DamageMax")] public int DamageMax;
        [JsonProperty("EquipmentStatId")] public int EquipmentStatId;

        [JsonIgnore] public EquipmentStatTable? EquipmentStat;

        public EquipmentTable()
        {

        }

        public override void LoadLate()
        {
            EquipmentStatTable? equipmentStatTable = AR.s.Data.GetEquipmentStat(EquipmentStatId);
            if (equipmentStatTable != null)
            {
                EquipmentStat = equipmentStatTable;
            }
            else
            {
                EquipmentStat = null;
            }
        }

    }

    [Serializable]
    public class EquipmentStatTable : TableBase
    {
        [JsonProperty("Prefix")] public List<Stat>? Prefix;
        [JsonProperty("Postfix")] public List<Stat>? Postfix;

    }

    [Serializable]
    public class DropTable : TableBase
    {
        [JsonProperty("Tier")] public int Tier;                     // Drop 아이템 티어
        [JsonProperty("DropRate")] public int NothingRate;          // 아무것도 안떨어질 확률
        [JsonProperty("CurrencyRate")] public int CurrencyRate;     // Drop 화폐 확률
        [JsonProperty("CurrencyId")] public int CurrencyId;         // Drop 화폐 테이블 Id
        [JsonProperty("EquipmentRate")] public int EquipmentRate;   // Drop 장비 확률
        [JsonProperty("EquipmentId")] public int EquipmentId;       // Drop 장비 테이블 Id
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
        
        
    }

    [Serializable]
    public class AiTable : TableBase
    {
        [JsonProperty("Name")] public string Name = string.Empty;   // 이름
        [JsonProperty("AiType")] public GE.AiType AiType;           // Ai 타입
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

    [Serializable]
    public class Stat
    {
        [JsonProperty("Type")] public GE.Stat Type;
        [JsonProperty("Value")] public ushort Value;
    }

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

}