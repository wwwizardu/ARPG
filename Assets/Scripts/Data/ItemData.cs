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
    }

    [Serializable]
    public class EquipmentData
    {
        public int Id;

        public List<Stat> Prefix = new();

        public List<Stat> Postfix = new();

    }
}


