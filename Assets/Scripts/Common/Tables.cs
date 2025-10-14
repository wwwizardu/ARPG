#nullable enable
using System;
using System.Collections;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Unity.VisualScripting;
using UnityEngine;

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
        [JsonProperty("Name")] public string Name;

        [JsonProperty("Stat")] public int StatId;

        [JsonProperty("PrefabName")] public string PrefabName;

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
    }

    [Serializable]
    public class MonsterTable : CreatureTable
    {
        [JsonProperty("DropId")] public int DropId;
        [JsonProperty("DropRateBonus")] public int DropRateBonus;
        [JsonProperty("DropRarityBonus")] public int DropRarityBonus;
    }

    [Serializable]
    public class NpcTable : CreatureTable
    {
        [JsonProperty("DropId")] public int DropId;
        [JsonProperty("DropRateBonus")] public int DropRateBonus;
        [JsonProperty("DropRarityBonus")] public int DropRarityBonus;
    }

    [Serializable]
    public class ItemTable : TableBase
    {
        [JsonProperty("Tier")] public int Tier;

        [JsonProperty("Name")] public string Name;

        [JsonProperty("ItemType")] public GlobalEnum.ItemType ItemType;

        [JsonProperty("Stackable")] public bool Stackable;

        [JsonProperty("Description")] public string Description;

        [JsonProperty("DropRate")] public int DropRate;

        [JsonProperty("EquipmentId")] public int EquipmentId;

        [JsonProperty("SpriteName")] public string SpriteName;

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
        [JsonProperty("EquipType")] public GlobalEnum.EquipSlotType EquipType;
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

        [JsonProperty("DropList")] public List<DropInfo> DropList;

    }

    [Serializable]
    public class DropEquipmentTable : TableBase
    {
        [JsonProperty("Tier")] public int Tier;                 // Drop 아이템 티어
        [JsonProperty("DropList")] public List<DropInfo> DropList;

    }

    [Serializable]
    public class SkillTable : TableBase
    {
        [JsonProperty("Name")] public string Name;                          // 이름
        [JsonProperty("Desctiption")] public string Desctiption;            // 설명
        [JsonProperty("SkillType")] public GlobalEnum.SkillType SkillType;  // 스킬 타입
        [JsonProperty("SubType")] public GlobalEnum.SkillSubType SubType;   // 스킬 서브 타입
        [JsonProperty("SkillRangeMin")] public float SkillRangeMin;         // 스킬 최소 사정 거리
        [JsonProperty("SkillRangeMax")] public float SkillRangeMax;         // 스킬 최대 사정 거리
        [JsonProperty("Cooltime")] public float Cooltime;                   // 쿨 타임
        [JsonProperty("Mana")] public int Mana;                             // 마나 소모량
        [JsonProperty("Damage")] public int Damage;                         // 데미지
        [JsonProperty("Duration")] public int Duration;                     // 지속 시간
        [JsonProperty("SkillTargetType")] public GlobalEnum.SkillTargetType SkillTargetType; // 스킬 타겟 타입
        [JsonProperty("SkillTargetRange1")] public float SkillTargetRange1; // 스킬 타겟 타입 범위 1
        [JsonProperty("SkillTargetRange2")] public float SkillTargetRange2; // 스킬 타겟 타입 범위 2
        [JsonProperty("AnimationName")] public string AnimationName;        // 애니메이션 이름
        
        

    }

    [Serializable]
    public class Stat
    {
        [JsonProperty("Type")] public GlobalEnum.Stat Type;
        [JsonProperty("Value")] public ushort Value;
    }

    [Serializable]
    public class DropInfo
    {
        [JsonProperty("Type")] public int Id;
        [JsonProperty("Value")] public int Rate;
    }

}