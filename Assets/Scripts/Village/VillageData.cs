#nullable enable
using System.Collections.Generic;
using UnityEngine;

namespace ARPG.Village
{
    public class VillageData
    {
        public int VillageId;
        public Dictionary<GlobalEnum.ItemType, int> Resources = new();
        public VillageStage Stage;
        public int Population;
        public Vector2 Position;

        public VillageData(int villageId, Vector2 position)
        {
            VillageId = villageId;
            Position = position;
            Stage = VillageStage.Settlement;
            Population = 1;
        }
    }
}
