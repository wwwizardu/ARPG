#nullable enable
using System;
using System.Collections.Generic;
using ARPG.Tables;
using UnityEngine;

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
                Equipment.OnLoadCompleted();
            }
        }
    }

    [Serializable]
    public class EquipmentData
    {
        public int Id;

        public int Quality;

        public EquipmentStatData? StatData;

        [NonSerialized] public WeaponBaseStatTable? Table;

        [NonSerialized] public WeaponData? WeaponData;
        

        public void OnLoadCompleted()
        {
            if (Table == null)
            {
                Table = AR.s.Data.WeaponBaseStatTable(Id);
            }

            Update();            
        }

        public (int, int) GetPhysicsDamage()
        {
            if (WeaponData == null)
                return (0, 0);

            return (WeaponData.PhysicsDamage.DamageMin, WeaponData.PhysicsDamage.DamageMax);
        }

        public bool IsPhysicsDamage()
        {
            if (WeaponData == null)
                return false;

            if (WeaponData.PhysicsDamage.DamageMin == 0 && WeaponData.PhysicsDamage.DamageMax == 0)
                return false;

            return true;
        }

        public bool IsFireDamage()
        {
            if (WeaponData == null)
                return false;

            if (WeaponData.FireDamage.DamageMin == 0 && WeaponData.FireDamage.DamageMax == 0)
                return false;

            return true;
        }

        public bool IsIceDamage()
        {
            if (WeaponData == null)
                return false;

            if (WeaponData.IceDamage.DamageMin == 0 && WeaponData.IceDamage.DamageMax == 0)
                return false;

            return true;
        }

        public bool IsLightningDamage()
        {
            if (WeaponData == null)
                return false;

            if (WeaponData.LightningDamage.DamageMin == 0 && WeaponData.LightningDamage.DamageMax == 0)
                return false;

            return true;
        }

        public bool IsPoisonDamage()
        {
            if (WeaponData == null)
                return false;

            if (WeaponData.PoisonDamage.DamageMin == 0 && WeaponData.PoisonDamage.DamageMax == 0)
                return false;

            return true;
        }

        public float GetCriticalRate()
        {
            if (WeaponData == null)
                return 0;

            return WeaponData.CriticalRate;
        }
        
        public float GetAttackSpeed()
        {
            if (WeaponData == null)
                return 0;

            return WeaponData.AttackSpeed;
        }

        public void Update()
        {
            if (Table == null)
            {
                Debug.LogError("[EquipmentData] Update() - Table is null");
                return;
            }

            if (Table.EquipType == GlobalEnum.EquipmentType.Weapon)
            {
                if (WeaponData == null)
                {
                    WeaponData = new WeaponData();
                }

                // 물리 데미지 계산
                var physicsDamageFromStat = GetPhysicsDamageFromStat();
                float physicsDamageMultiplierFromStat = 1f + (GetPhysicsDamageMultiplierFromStat() * 0.01f);

                int physicsDamageMin = Mathf.FloorToInt((Table.DamageMin + physicsDamageFromStat.Item1) * physicsDamageMultiplierFromStat);
                int physicsDamageMax = Mathf.FloorToInt((Table.DamageMax + physicsDamageFromStat.Item2) * physicsDamageMultiplierFromStat);

                WeaponData.PhysicsDamage.SetDamage(physicsDamageMin, physicsDamageMax);

                // 속성 데미지 계산 (Prefix/Postfix 스탯에서 가져옴)
                int fireMin = GetStatValue(GlobalEnum.Stat.FireAttackMin);
                int fireMax = GetStatValue(GlobalEnum.Stat.FireAttackMax);
                WeaponData.FireDamage.SetDamage(fireMin, fireMax);

                int iceMin = GetStatValue(GlobalEnum.Stat.IceAttackMin);
                int iceMax = GetStatValue(GlobalEnum.Stat.IceAttackMax);
                WeaponData.IceDamage.SetDamage(iceMin, iceMax);

                int lightningMin = GetStatValue(GlobalEnum.Stat.LightningAttackMin);
                int lightningMax = GetStatValue(GlobalEnum.Stat.LightningAttackMax);
                WeaponData.LightningDamage.SetDamage(lightningMin, lightningMax);

                int poisonMin = GetStatValue(GlobalEnum.Stat.PoisonAttackMin);
                int poisonMax = GetStatValue(GlobalEnum.Stat.PoisonAttackMax);
                WeaponData.PoisonDamage.SetDamage(poisonMin, poisonMax);

                // 공격 속도 계산
                float attackSpeedMultiplier = 1f + (GetStatValue(GlobalEnum.Stat.AttackSpeedMul) * 0.01f);
                WeaponData.AttackSpeed = Table.AttackSpeed / attackSpeedMultiplier;

                // 크리티컬 확률 계산
                WeaponData.CriticalRate = Table.Critical + GetStatValue(GlobalEnum.Stat.CriRate);
            }
            else
            {

            }
        }

        private int GetStatValue(GlobalEnum.Stat statType)
        {
            if (StatData == null)
                return 0;

            int value = 0;

            // Prefix 스탯에서 찾기
            for (int i = 0; i < StatData.Prefix.Count; i++)
            {
                if (StatData.Prefix[i].Type == statType)
                    value += StatData.Prefix[i].Value;
            }

            // Postfix 스탯에서 찾기
            for (int i = 0; i < StatData.Postfix.Count; i++)
            {
                if (StatData.Postfix[i].Type == statType)
                    value += StatData.Postfix[i].Value;
            }

            return value;
        }

        private (int, int) GetPhysicsDamageFromStat()
        {
            int attackMin = GetStatValue(GlobalEnum.Stat.AttackMin);
            int attackMax = GetStatValue(GlobalEnum.Stat.AttackMax);

            return (attackMin, attackMax);
        }

        private float GetPhysicsDamageMultiplierFromStat()
        {
            if (StatData == null)
                return 1;

            float damageMultiplier = 1;

            return damageMultiplier;
        }


    }

    [Serializable]
    public class EquipmentStatData
    {
        public List<Stat> Prefix = new();

        public List<Stat> Postfix = new();
    }

    public class WeaponData
    {
        public DamageData PhysicsDamage = new DamageData(0, 0);
        public DamageData FireDamage = new DamageData(0, 0);
        public DamageData IceDamage = new DamageData(0, 0);
        public DamageData LightningDamage = new DamageData(0, 0);
        public DamageData PoisonDamage = new DamageData(0, 0);

        public float AttackSpeed;
        public int CriticalRate;
    }

    public class DamageData
    {
        public int DamageMin;
        public int DamageMax;

        public DamageData(int inMin, int inMax)
        {
            SetDamage(inMin, inMax);
        }

        public void SetDamage(int inMin, int inMax)
        {
            DamageMin = inMin;
            DamageMax = inMax;
        }

        public void SetDamageMin(int inDamage)
        {
            DamageMin = inDamage;
        }
        
        public void SetDamageMax(int inDamage)
        {
            DamageMax = inDamage;
        }
        
    }
}


