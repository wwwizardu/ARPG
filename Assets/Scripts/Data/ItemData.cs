#nullable enable
using System;
using System.Collections.Generic;
using ARPG.Tables;
using UnityEngine;
using GE = GlobalEnum;

namespace ARPG.Data
{
    [Serializable]
    public class ItemData
    {
        public int Id;

        public int ItemInstanceId;

        public EquipmentData? Equipment;

        public int Quantity;

        [NonSerialized] public ItemTable? Table;

        public void OnLoadCompleted()
        {
            if (Table == null)
            {
                Table = AR.s.Data.GetItem(Id);
            }

            if (Equipment != null)
            {
                Equipment.OnLoadCompleted(Table);
            }
        }
    }

    [Serializable]
    public class EquipmentData
    {
        public int Id;

        public int Quality;

        public List<Stat> BaseStats = new();

        public EquipmentStatData? StatData;

        public GE.EquipmentType EquipType { get; set; }

        [NonSerialized] public List<Stat> ComputedStats = new();

        public void OnLoadCompleted(ItemTable? inItemTable)
        {
            if (inItemTable == null)
            {
                Debug.LogError("[EquipmentData] OnLoadCompleted() - ItemTable is null");
                return;
            }

            EquipType = Utils.CategoryToEquipmentType(inItemTable.Category);

            // 기존 세이브 데이터 마이그레이션: BaseStats가 비어있으면 테이블에서 생성
            if (BaseStats.Count == 0)
            {
                InitBaseStats(inItemTable);
            }

            ComputeStats();
        }

        public void InitBaseStats(ItemTable inItemTable)
        {
            BaseStats.Clear();

            var baseStatTable = inItemTable.EquipmentBaseStat;
            if (baseStatTable == null)
                return;

            for (int i = 0; i < baseStatTable.Stats.Count; i++)
            {
                AddBaseStat(baseStatTable.Stats[i].Type, baseStatTable.Stats[i].Value);
            }
        }

        public void ComputeStats()
        {
            if (ComputedStats == null)
                ComputedStats = new List<Stat>();

            ComputedStats.Clear();

            // 1. BaseStats 복사
            for (int i = 0; i < BaseStats.Count; i++)
            {
                AddComputedStat(BaseStats[i].Type, BaseStats[i].Value);
            }

            // 2. Prefix/Postfix 합산
            if (StatData != null)
            {
                for (int i = 0; i < StatData.Prefix.Count; i++)
                {
                    AddComputedStat(StatData.Prefix[i].Type, StatData.Prefix[i].Value);
                }

                for (int i = 0; i < StatData.Postfix.Count; i++)
                {
                    AddComputedStat(StatData.Postfix[i].Type, StatData.Postfix[i].Value);
                }
            }

            // 배율(Mul) 스탯은 캐릭터 스탯 시스템(System_StatCalculation)에서 처리
        }

        public ushort GetComputedStatValue(GE.Stat inStatType)
        {
            for (int i = 0; i < ComputedStats.Count; i++)
            {
                if (ComputedStats[i].Type == inStatType)
                    return ComputedStats[i].Value;
            }
            return 0;
        }

        public (int, int) GetPhysicsDamage()
        {
            ushort min = GetComputedStatValue(GE.Stat.AttackMin);
            ushort max = GetComputedStatValue(GE.Stat.AttackMax);
            return (min, max);
        }

        public bool IsPhysicsDamage()
        {
            return GetComputedStatValue(GE.Stat.AttackMin) > 0 || GetComputedStatValue(GE.Stat.AttackMax) > 0;
        }

        public bool IsFireDamage()
        {
            return GetComputedStatValue(GE.Stat.FireAttackMin) > 0 || GetComputedStatValue(GE.Stat.FireAttackMax) > 0;
        }

        public bool IsIceDamage()
        {
            return GetComputedStatValue(GE.Stat.IceAttackMin) > 0 || GetComputedStatValue(GE.Stat.IceAttackMax) > 0;
        }

        public bool IsLightningDamage()
        {
            return GetComputedStatValue(GE.Stat.LightningAttackMin) > 0 || GetComputedStatValue(GE.Stat.LightningAttackMax) > 0;
        }

        public bool IsPoisonDamage()
        {
            return GetComputedStatValue(GE.Stat.PoisonAttackMin) > 0 || GetComputedStatValue(GE.Stat.PoisonAttackMax) > 0;
        }

        public float GetCriticalRate()
        {
            return GetComputedStatValue(GE.Stat.CriRate);
        }

        public float GetAttackSpeed()
        {
            return GetComputedStatValue(GE.Stat.AttackSpeed) * 0.01f;
        }

        private void AddBaseStat(GE.Stat inType, ushort inValue)
        {
            if (inValue == 0)
                return;

            BaseStats.Add(new Stat() { Type = inType, Value = inValue });
        }

        private void AddComputedStat(GE.Stat inType, ushort inValue)
        {
            // 기존 스탯이 있으면 합산
            for (int i = 0; i < ComputedStats.Count; i++)
            {
                if (ComputedStats[i].Type == inType)
                {
                    ComputedStats[i] = new Stat()
                    {
                        Type = inType,
                        Value = (ushort)(ComputedStats[i].Value + inValue)
                    };
                    return;
                }
            }

            ComputedStats.Add(new Stat() { Type = inType, Value = inValue });
        }

    }

    [Serializable]
    public class EquipmentStatData
    {
        public List<Stat> Prefix = new();

        public List<Stat> Postfix = new();
    }
}
