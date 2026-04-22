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
        public VillageStage Stage;
        public int Population;
        public float PositionX;
        public float PositionY;

        // 스폰/쿨타임 상태 (세이브 대상)
        public bool HasBeenPopulated;        // 기본 NPC가 한번이라도 스폰된 적 있는지
        public float DepletedAt;             // 전멸 감지 시각 (게임 시간). 0이면 정상

        [JsonIgnore]
        public List<int> NpcEntityIds = new();

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
        }
    }
}
