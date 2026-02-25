#nullable enable
using ARPG.Village;
using UnityEngine;

namespace ARPG.Systems
{
    /// <summary>
    /// 마을 인구 기반 자원 자동 생산 시스템
    /// 5초마다 각 마을의 Population에 비례하여 Food, Wood, Stone 생산
    /// </summary>
    public class System_VillageResource : IFixedUpdateSystem
    {
        public int Priority => 57;
        public float UpdateInterval => 5.0f;

        public void OnCreate() { }

        public void OnFixedUpdate(float inFixedDeltaTime)
        {
            var villages = AR.s.Village.GetAllVillages();

            foreach (VillageData village in villages)
            {
                if (village.Population <= 0)
                    continue;

                int amount = village.Population;

                AR.s.Village.ProduceResource(village.VillageId, GlobalEnum.ItemType.Food, amount);
                AR.s.Village.ProduceResource(village.VillageId, GlobalEnum.ItemType.Wood, amount);
                AR.s.Village.ProduceResource(village.VillageId, GlobalEnum.ItemType.Stone, amount);

                Debug.Log($"[VillageResource] Village {village.VillageId} produced - Pop: {village.Population}, Food: +{amount}, Wood: +{amount}, Stone: +{amount}");
            }
        }

        public void OnReset() { }
    }
}
