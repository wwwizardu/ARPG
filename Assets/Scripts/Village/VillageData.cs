#nullable enable
using System.Collections.Generic;
using Newtonsoft.Json;
using UnityEngine;

namespace ARPG.Village
{
    public class VillageData
    {
        public int VillageId;
        public int TableId;                  // VillageTable 참조 (0이면 기본 스폰 로직 없음)
        public Dictionary<GlobalEnum.ItemType, int> Resources = new();
        public Dictionary<GlobalEnum.ItemType, int> ResourceCaps = new();  // 빈 경우 기본 50
        public VillageStage Stage;
        public int Population;
        public float PositionX;
        public float PositionY;

        // 스폰/쿨타임 상태 (세이브 대상)
        public bool HasBeenPopulated;        // 기본 NPC가 한번이라도 스폰된 적 있는지
        public float DepletedAt;             // 전멸 감지 시각 (게임 시간). 0이면 정상

        // Phase A: 자원 생산 상태
        public int StoneTimer;               // Stone 5시간 누적 카운터 (0~4)
        public int HungerHoursAccumulated;   // Food 0 유지 누적 시간 (24h 넘으면 경고 1회)
        public float RegisteredAt;           // 마을 등록 게임 시각

        // Phase A: 첫 Campfire 제작 상태
        public bool HasCampfire;
        public float FirstBuildStartedAt = -1f;   // -1 = 미착수
        public int FirstBuildTileX;
        public int FirstBuildTileY;

        [JsonIgnore]
        public List<int> NpcEntityIds = new();

        [JsonIgnore]
        public int EntityId = -1;             // Village 엔티티 ID (런타임 재발급)

        [JsonIgnore]
        public Vector2 Position
        {
            get => new Vector2(PositionX, PositionY);
            set { PositionX = value.x; PositionY = value.y; }
        }

        public VillageData(int villageId, Vector2 position)
        {
            VillageId = villageId;
            Position = position;
            Stage = VillageStage.Settlement;
            Population = 0;
            TableId = 0;
            HasBeenPopulated = false;
            DepletedAt = 0f;
            StoneTimer = 0;
            HungerHoursAccumulated = 0;
            RegisteredAt = 0f;
            HasCampfire = false;
            FirstBuildStartedAt = -1f;
        }
    }
}
