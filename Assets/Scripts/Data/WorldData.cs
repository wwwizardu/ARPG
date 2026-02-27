using System;
using System.Collections.Generic;
using ARPG.Map;
using ARPG.Npc;
using ARPG.Village;
using UnityEngine;

namespace ARPG.Data
{
    [Serializable]
    public class WorldData
    {
        public ushort Version = 1;

        public List<WorldDropItemData> WorldDropItemDatas = new();
        public List<VillageData> VillageDatas = new();
        public Dictionary<int, NpcSaveData> NpcSaveDatas = new();
        public List<ChunkModificationData> TileModifications = new();

        public void Initialize()
        {
            WorldDropItemDatas.Clear();
            TileModifications.Clear();
        }

        public void LoadCompleted()
        {
            // 로드 완료 후 처리할 내용이 있으면 여기에 작성
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


