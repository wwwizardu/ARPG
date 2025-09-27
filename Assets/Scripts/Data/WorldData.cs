using System;
using System.Collections.Generic;
using UnityEngine;

namespace ARPG.Data
{
    [Serializable]
    public class WorldData
    {
        public ushort Version = 1;

        public List<WorldDropItemData> WorldDropItemDatas = new();

        public void Initialize()
        {
            WorldDropItemDatas.Clear();
        }

        public bool AddDropItem(float inPosX, float inPosZ, ItemData inItem)
        {
            if (inItem == null)
                return false;

            WorldDropItemDatas.Add(new WorldDropItemData()
            {
                PositionX = inPosX,
                PositionZ = inPosZ,
                ItemData = inItem,
            });

            return true;
        }
    }

    [Serializable]
    public class WorldDropItemData
    {
        public float PositionX;
        public float PositionZ;
        public ItemData ItemData;
    }
}


