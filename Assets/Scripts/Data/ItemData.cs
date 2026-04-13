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

        /// <summary>
        /// 모든 Mod (Implicit + Prefix + Postfix) - 저장 대상
        /// </summary>
        public List<ModInstance> Mods = new();

        public GE.EquipmentType EquipType { get; set; }

        public void OnLoadCompleted(ItemTable? inItemTable)
        {
            if (inItemTable == null)
            {
                Debug.LogError("[EquipmentData] OnLoadCompleted() - ItemTable is null");
                return;
            }

            EquipType = Utils.CategoryToEquipmentType(inItemTable.Category);

            // ModInstance의 테이블 참조 연결
            for (int i = 0; i < Mods.Count; i++)
            {
                Mods[i].OnLoadCompleted();
            }
        }

        /// <summary>
        /// ItemImplicitTable 기반으로 Implicit Mod 초기화
        /// </summary>
        public void InitImplicitMods(int itemId)
        {
            var implicits = AR.s.Data.GetItemImplicits(itemId);

            for (int i = 0; i < implicits.Count; i++)
            {
                var imp = implicits[i];
                if (imp.TierData == null)
                    continue;

                ushort value1 = (ushort)UnityEngine.Random.Range(imp.TierData.Min1, imp.TierData.Max1 + 1);
                ushort value2 = (ushort)UnityEngine.Random.Range(imp.TierData.Min2, imp.TierData.Max2 + 1);

                Mods.Add(new ModInstance
                {
                    ModTableId = imp.ModId,
                    Slot = GE.ModSlot.Implicit,
                    Tier = imp.Tier,
                    Value1 = value1,
                    Value2 = value2,
                });
            }
        }

        /// <summary>
        /// 특정 EffectType의 Mod 검색 (Value1 합산)
        /// </summary>
        public ushort GetModValue(GE.ModEffectType effectType)
        {
            ushort total = 0;
            for (int i = 0; i < Mods.Count; i++)
            {
                if (Mods[i].Table != null && Mods[i].Table.EffectType == effectType)
                    total += Mods[i].Value1;
            }
            return total;
        }

        /// <summary>
        /// 특정 EffectType의 Mod 데미지 범위 합산
        /// </summary>
        public (int min, int max) GetDamageRange(GE.ModEffectType effectType)
        {
            int min = 0, max = 0;
            for (int i = 0; i < Mods.Count; i++)
            {
                if (Mods[i].Table != null && Mods[i].Table.EffectType == effectType)
                {
                    min += Mods[i].Value1;
                    max += Mods[i].Value2;
                }
            }
            return (min, max);
        }

        public (int, int) GetPhysicsDamage()
        {
            return GetDamageRange(GE.ModEffectType.AddedPhysDamage);
        }

        public bool IsPhysicsDamage()
        {
            var (min, max) = GetPhysicsDamage();
            return min > 0 || max > 0;
        }

        public bool IsFireDamage()
        {
            var (min, max) = GetDamageRange(GE.ModEffectType.AddedFireDamage);
            return min > 0 || max > 0;
        }

        public bool IsIceDamage()
        {
            var (min, max) = GetDamageRange(GE.ModEffectType.AddedIceDamage);
            return min > 0 || max > 0;
        }

        public bool IsLightningDamage()
        {
            var (min, max) = GetDamageRange(GE.ModEffectType.AddedLightningDamage);
            return min > 0 || max > 0;
        }

        public bool IsPoisonDamage()
        {
            var (min, max) = GetDamageRange(GE.ModEffectType.AddedPoisonDamage);
            return min > 0 || max > 0;
        }

        public int GetCriticalRate()
        {
            int val = 0;
            for (int i = 0; i < Mods.Count; i++)
            {
                if (Mods[i].Table != null
                    && Mods[i].Table.EffectType == GE.ModEffectType.FlatStat
                    && Mods[i].Table.TargetStat == GE.Stat.CriRate)
                {
                    val += Mods[i].Value1;
                }
            }
            return val;
        }

        public float GetAttackSpeed()
        {
            ushort val = 0;
            for (int i = 0; i < Mods.Count; i++)
            {
                if (Mods[i].Table != null
                    && Mods[i].Table.EffectType == GE.ModEffectType.FlatStat
                    && Mods[i].Table.TargetStat == GE.Stat.AttackSpeed)
                {
                    val += Mods[i].Value1;
                }
            }
            return val * 0.01f;
        }
    }
}
