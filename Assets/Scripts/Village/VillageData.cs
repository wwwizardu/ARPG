#nullable enable
using System.Collections.Generic;
using Newtonsoft.Json;
using UnityEngine;

namespace ARPG.Village
{
    public class VillageData
    {
        public int VillageId;
        public Dictionary<GlobalEnum.ItemType, int> Resources = new();
        public VillageStage Stage;
        public int Population;
        public float PositionX;
        public float PositionY;
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
        }
    }
}
