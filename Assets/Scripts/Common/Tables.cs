using System;
using System.Collections;
using System.Collections.Generic;
using Newtonsoft.Json;
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

        [JsonProperty("EquipmentId")] public int EquipmentId;

        [JsonProperty("SpriteName")] public string SpriteName;

        [JsonIgnore] public EquipmentTable Equipment;

        public override void LoadLate()
        {
            EquipmentTable equipmentTable = AR.s.Data.GetEquipment(EquipmentId);
            if (equipmentTable == null)
            {
                Debug.LogError($"[ItemTable] LoadLate() - equipmentTable is null, EquipmentId({EquipmentId})");
                return;
            }

            Equipment = equipmentTable;
        }
    }

    [Serializable]
    public class EquipmentTable : TableBase
    {
        [JsonProperty("Prefix")] public List<Stat> Prefix;
        [JsonProperty("Postfix")] public List<Stat> Postfix;

        public EquipmentTable()
        {

        }

        public EquipmentTable(EquipmentTable inTable)
        {
            Id = inTable.Id;
            Prefix = new List<Stat>(inTable.Prefix);
            Postfix = new List<Stat>(inTable.Postfix);
        }

    }

    [Serializable]
    public class Stat
    {
        [JsonProperty("Type")] public GlobalEnum.Stat Type;
        [JsonProperty("Value")] public int Value;
    }

}