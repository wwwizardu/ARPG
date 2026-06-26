using System;
using System.Collections.Generic;
using ARPG.History;
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
        public List<NpcAffinityEntry> NpcAffinityList = new();
        public Dictionary<int, BuildingSaveData> BuildingSaveDatas = new();
        public List<ChunkModificationData> TileModifications = new();

        // WORLD_HISTORY_PLAN.md R0-B — 정착지 연대기(L1) + NPC 관계(L2)
        public ChronicleSaveData Chronicle = new();
        public List<NpcRelationEntry> NpcRelationList = new();

        public void Initialize()
        {
            WorldDropItemDatas.Clear();
            TileModifications.Clear();
        }

        public void LoadCompleted()
        {
            // 구버전 세이브(신규 필드 없는 JSON) 로드 시 null 방어 — 기본값 채우기 (R0-B 마이그레이션)
            if (NpcAffinityList == null)
                NpcAffinityList = new();
            if (NpcRelationList == null)
                NpcRelationList = new();
            if (Chronicle == null)
                Chronicle = new();
            if (Chronicle.Events == null)
                Chronicle.Events = new();
            if (Chronicle.Settlements == null)
                Chronicle.Settlements = new();
            if (Chronicle.Kingdoms == null)
                Chronicle.Kingdoms = new();
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


