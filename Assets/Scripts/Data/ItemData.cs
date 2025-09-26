using System;
using ARPG.Tables;
using UnityEngine;

namespace ARPG.Data
{
    [Serializable]
    public class ItemData
    {
        public int Id;

        public int ItemInstanceId;

        public int Quantity;

        [NonSerialized] public ItemTable Table;

    } 
}


