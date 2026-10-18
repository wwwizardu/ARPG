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
        public ushort Version = 16;
        // Legacy worlds used 12345. Field initializer deliberately preserves that map.
        public int WorldSeed = 12345;
        public float ElapsedWorldGameHours;
        public DungeonSaveRoot Dungeons = new();
        // 공통 콘텐츠 실행 원장 — narrative 파트 (NPC_NARRATIVE_CONTENT_DESIGN §7). WorldDataVersion 21 미만은 빈 원장으로 시작한다.
        public NarrativeSaveRoot Narrative = new();

        public List<WorldDropItemData> WorldDropItemDatas = new();
        public List<VillageData> VillageDatas = new();
        public Dictionary<int, NpcSaveData> NpcSaveDatas = new();
        public List<NpcAffinityEntry> NpcAffinityList = new();
        public List<PlayerNpcRelationEntry> PlayerNpcRelationList = new();
        public Dictionary<int, BuildingSaveData> BuildingSaveDatas = new();
        public List<ChunkModificationData> TileModifications = new();

        // WORLD_HISTORY_PLAN.md R0-B — 정착지 연대기(L1) + NPC 관계(L2)
        public ChronicleSaveData Chronicle = new();
        public List<NpcRelationEntry> NpcRelationList = new();
        public List<NpcPartyState> NpcPartyStates = new();
        // 물리 모험가의 전역 목표 수와 보충 시각. 경제 인구와 무관하다.
        public NpcPopulationState NpcPopulation = new();
        // NPC_COMBAT_DESIGN §4.5 — 임시 동행 예약·진행 상태 (World 파티션)
        public NpcCompanionState NpcCompanion = new();
        // GOLD_BALANCE_DESIGN D126·D130 — 세계 물가 지수 (World 파티션). 기준값 0 = 아직 정하지 않음.
        public GoldPriceIndexState GoldPrice = new();
        // NAMED_THREAT_DESIGN §10.1 — 네임드 위협 기록 (World 파티션). 구 세이브는 빈 상태로 연다.
        public NamedThreatWorldState NamedThreats = new();

        // PROCEDURAL_HISTORY §0 런타임 LOD — 게임 중 변동하는 정착지 라이브 추상 층.
        // Chronicle(정지 스냅샷)과 분리. 최초/구세이브는 HistoryManager가 Chronicle에서 초기화.
        public WorldSimState WorldSim = new();

        public void Initialize()
        {
            WorldDropItemDatas.Clear();
            TileModifications.Clear();
        }

        public void LoadCompleted()
        {
            if (float.IsNaN(ElapsedWorldGameHours) || float.IsInfinity(ElapsedWorldGameHours) ||
                ElapsedWorldGameHours < 0f) ElapsedWorldGameHours = 0f;
            if (Dungeons == null) Dungeons = new();
            if (Narrative == null) Narrative = new();
            Narrative.Instances ??= new();
            Narrative.Definitions ??= new();
            Narrative.Policies ??= new();
            Narrative.Selections ??= new();
            for (int d = 0; d < Dungeons.Dungeons.Count; d++)
                for (int i = 0; i < Dungeons.Dungeons[d].Drops.Count; i++)
                    Dungeons.Dungeons[d].Drops[i].ItemData?.OnLoadCompleted();
            if (WorldDropItemDatas == null)
                WorldDropItemDatas = new();
            for (int i = 0; i < WorldDropItemDatas.Count; i++)
            {
                WorldDropItemData drop = WorldDropItemDatas[i];
                drop?.ItemData?.OnLoadCompleted();
            }

            // 구버전 세이브(신규 필드 없는 JSON) 로드 시 null 방어 — 기본값 채우기 (R0-B 마이그레이션)
            if (NpcAffinityList == null)
                NpcAffinityList = new();
            if (PlayerNpcRelationList == null)
                PlayerNpcRelationList = new();
            if (NpcRelationList == null)
                NpcRelationList = new();
            if (NpcPartyStates == null)
                NpcPartyStates = new();
            if (NpcPopulation == null)
                NpcPopulation = new();
            if (NpcCompanion == null)
                NpcCompanion = new();
            if (GoldPrice == null)
                GoldPrice = new();
            GoldPrice.Sanitize();
            if (NamedThreats == null)
                NamedThreats = new();
            NamedThreats.Sanitize();
            if (Chronicle == null)
                Chronicle = new();
            if (Chronicle.Events == null)
                Chronicle.Events = new();
            if (Chronicle.Settlements == null)
                Chronicle.Settlements = new();
            if (Chronicle.Kingdoms == null)
                Chronicle.Kingdoms = new();

            // 런타임 LOD (PROCEDURAL §0): 구세이브엔 WorldSim이 없음 → 빈 컨테이너 보장.
            // 실제 채우기(Chronicle → LiveState)는 HistoryManager.Initialize가 담당.
            if (WorldSim == null)
                WorldSim = new();
            if (WorldSim.Settlements == null)
                WorldSim.Settlements = System.Array.Empty<SettlementLiveState>();
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


