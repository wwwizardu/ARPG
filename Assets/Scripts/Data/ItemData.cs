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

        [NonSerialized] public EquipmentTable? Table;

        public void OnLoadCompleted()
        {
            if (Table == null)
            {
                Table = AR.s.Data.GetEquipment(Id);
            }
        }
    }

    [Serializable]
    public class EquipmentStatData
    {
        public List<Stat> Prefix = new();

        public List<Stat> Postfix = new();
    }
}


