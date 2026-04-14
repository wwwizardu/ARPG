#nullable enable
using System.Collections.Generic;
using ARPG.Tables;
using UnityEngine;

namespace ARPG.Utility
{
    /// <summary>
    /// DropComponent 기반 아이템 드랍 처리
    /// </summary>
    public static class DropHelper
    {
        /// <summary>
        /// DropTable을 기반으로 아이템을 드랍
        /// </summary>
        /// <param name="dropId">DropTable ID</param>
        /// <param name="position">드랍 위치 (월드 좌표)</param>
        /// <param name="monsterLevel">몬스터 레벨 (Pool 모드 필터링용)</param>
        /// <param name="dropRateBonus">드롭률 보너스 (%) - NothingRate 감소</param>
        /// <param name="dropRarityBonus">드롭 희귀도 보너스 (%) - 높은 Tier 가중치 증가</param>
        public static void ProcessDrop(int dropId, Vector3 position, int monsterLevel, int dropRateBonus, int dropRarityBonus)
        {
            if (dropId <= 0)
                return;

            var dropTable = AR.s.Data?.GetDrop(dropId);
            if (dropTable == null)
            {
                Debug.LogError($"[DropHelper] DropTable not found. DropId: {dropId}");
                return;
            }

            // DropRateBonus 적용: NothingRate를 줄여서 드롭 확률 증가
            int nothingRate = dropTable.NothingRate;
            if (dropRateBonus > 0 && nothingRate > 0)
            {
                nothingRate = nothingRate * 100 / (100 + dropRateBonus);
            }

            // 드랍 아이템 결정
            int totalRate = nothingRate + dropTable.CurrencyRate + dropTable.EquipmentRate;
            int randomValue = Random.Range(0, totalRate);

            // 아무것도 안 떨어짐
            if (randomValue < nothingRate)
                return;

            int dropItemId = 0;

            // 화폐 vs 장비 결정
            if (randomValue < nothingRate + dropTable.CurrencyRate)
            {
                dropItemId = SelectCurrencyItem(dropTable, monsterLevel, dropRarityBonus);
            }
            else
            {
                dropItemId = SelectEquipmentItem(dropTable, monsterLevel, dropRarityBonus);
            }

            if (dropItemId <= 0)
                return;

            // 비동기로 아이템 GameObject 생성
            CreateDropItemAsync(dropItemId, position);
        }

        private static int SelectCurrencyItem(DropTable dropTable, int monsterLevel, int dropRarityBonus)
        {
            // Pool 모드: ItemTable 기반 자동 풀
            if (dropTable.CurrencyPoolMode == 1)
            {
                var pool = DropPoolBuilder.GetCurrencyPool(monsterLevel);
                return DropPoolBuilder.SelectWeightedRandom(pool, dropRarityBonus);
            }

            // Explicit 모드: 기존 DropCurrencyTable 사용
            return SelectRandomCurrencyItem(dropTable.CurrencyId);
        }

        private static int SelectEquipmentItem(DropTable dropTable, int monsterLevel, int dropRarityBonus)
        {
            // Pool 모드: ItemTable 기반 자동 풀
            if (dropTable.EquipmentPoolMode == 1)
            {
                var pool = DropPoolBuilder.GetEquipmentPool(monsterLevel);
                return DropPoolBuilder.SelectWeightedRandom(pool, dropRarityBonus);
            }

            // Explicit 모드: 기존 DropEquipmentTable 사용
            return SelectRandomEquipmentItem(dropTable.EquipmentId);
        }

        private static int SelectRandomCurrencyItem(int currencyTableId)
        {
            var currencyTable = AR.s.Data?.GetDropCurrency(currencyTableId);
            if (currencyTable == null || currencyTable.DropList == null)
                return 0;

            return SelectRandomIdFromDropInfoList(currencyTable.DropList, "currency");
        }

        private static int SelectRandomEquipmentItem(int equipmentTableId)
        {
            var equipmentTable = AR.s.Data?.GetDropEquipment(equipmentTableId);
            if (equipmentTable == null || equipmentTable.DropList == null)
                return 0;

            return SelectRandomIdFromDropInfoList(equipmentTable.DropList, "equipment");
        }

        private static int SelectRandomIdFromDropInfoList(List<DropInfo> dropList, string dropType)
        {
            if (dropList == null || dropList.Count == 0)
                return 0;

            int totalDropRate = 0;
            for (int i = 0; i < dropList.Count; i++)
            {
                totalDropRate += dropList[i].Rate;
            }

            int randomDropValue = Random.Range(0, totalDropRate);
            int cumulativeRate = 0;

            for (int i = 0; i < dropList.Count; i++)
            {
                cumulativeRate += dropList[i].Rate;
                if (randomDropValue < cumulativeRate)
                {
                    Debug.Log($"[DropHelper] Dropping {dropType}: ItemId={dropList[i].Id}, RandomValue({randomDropValue}), Rate={dropList[i].Rate}/{totalDropRate}");
                    return dropList[i].Id;
                }
            }

            return 0;
        }

        private static async void CreateDropItemAsync(int itemId, Vector3 position)
        {
            if (await AR.s.Item.CreateItem(itemId, 1, position) == false)
            {
                Debug.LogError($"[DropHelper] Failed to create item with Id({itemId})");
            }
        }
    }
}
