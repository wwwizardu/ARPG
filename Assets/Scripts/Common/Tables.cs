using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using UnityEngine;

namespace ARPG.Tables
{
    [Serializable]
    public class TableBase
    {
        [JsonProperty("Id")] public int Id;
    }

    [Serializable]
    public class CreatureTable : TableBase
    {
        [JsonProperty("Name")] public string Name;
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
    public class ItemTable : TableBase
    {
        [JsonProperty("Name")] public string Name;

        [JsonProperty("EquipmentId")]public EquipmentTable Equipment;

        [JsonProperty("SpriteName")] public string SpriteName;
    }

    [Serializable]
    public class EquipmentTable : TableBase
    {
        [JsonProperty("Prefix")] public List<Stat> Prefix;
        [JsonProperty("Postfix")] public List<Stat> Postfix;
    }

    [Serializable]
    public class Stat
    {
        [JsonProperty("Type")] public GlobalEnum.Stat Type;
        [JsonProperty("Value")] public int Value;
    }

}