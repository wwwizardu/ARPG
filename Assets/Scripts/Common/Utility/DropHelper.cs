#nullable enable
using System.Collections.Generic;
using ARPG.Component;
using ARPG.Data;
using ARPG.Item;
using ARPG.Tables;
using UnityEngine;

namespace ARPG.Utility
{
    /// <summary>
    /// DropComponent 기반 아이템 드랍 처리
    /// </summary>
    public static class DropHelper
    {
        // 판정 2회 이상일 때 아이템을 흩는 반경 (타일)
        private const float DROP_SCATTER_RADIUS = 0.8f;

        /// <summary>
        /// DropComponent의 판정 횟수만큼 ProcessDrop을 반복한다 (MONSTER_RARITY_DESIGN.md §3.2).
        /// 2회 이상이면 아이템이 한 점에 겹치지 않게 판정마다 위치를 흩는다. 흩은 칸이 막혀 있으면 원래 위치에 떨군다.
        /// </summary>
        public static void ProcessDrops(DropComponent drop, Vector3 position)
        {
            int rolls = Mathf.Max(1, drop.DropRolls);
            for (int i = 0; i < rolls; i++)
            {
                Vector3 rollPosition = rolls > 1 ? GetScatteredPosition(position) : position;
                ProcessDrop(drop.DropId, rollPosition, drop.MonsterLevel, drop.DropRateBonus, drop.DropRarityBonus,
                    drop.ModTier11ChanceBp, drop.ModTier12ChanceBp);
            }
        }

        /// <summary>
        /// DropTable을 기반으로 아이템을 드랍
        /// </summary>
        /// <param name="dropId">DropTable ID</param>
        /// <param name="position">드랍 위치 (월드 좌표)</param>
        /// <param name="monsterLevel">몬스터 레벨 (Pool 모드 필터링용, 드랍 장비의 아이템 레벨)</param>
        /// <param name="dropRateBonus">드롭률 보너스 (%) - NothingRate 감소</param>
        /// <param name="dropRarityBonus">드롭 희귀도 보너스 (%) - 높은 Tier 가중치 증가</param>
        /// <param name="modTier11ChanceBp">드랍 장비의 옵션 T11 특수 판정 확률 (만분율)</param>
        /// <param name="modTier12ChanceBp">드랍 장비의 옵션 T12 특수 판정 확률 (만분율)</param>
        public static void ProcessDrop(int dropId, Vector3 position, int monsterLevel, int dropRateBonus, int dropRarityBonus,
            int modTier11ChanceBp, int modTier12ChanceBp)
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

            // 드랍 카테고리 결정 (SkillBook + SkillPage 가중치 포함, SKILLBOOK_DESIGN.md §10 / SKILL_RUNE_DESIGN.md §8.1)
            int totalRate = nothingRate + dropTable.CurrencyRate + dropTable.EquipmentRate + dropTable.SkillBookRate + dropTable.SkillPageRate;
            if (totalRate <= 0)
            {
                Debug.Log($"[DropHelper] DropId({dropId}) totalRate=0 → 드랍 스킵. (의도적 무드랍 행이거나 데이터 누락)");
                return;
            }
            int randomValue = Random.Range(0, totalRate);

            // 아무것도 안 떨어짐
            if (randomValue < nothingRate)
                return;

            // 화폐
            if (randomValue < nothingRate + dropTable.CurrencyRate)
            {
                int dropItemId = SelectCurrencyItem(dropTable, monsterLevel, dropRarityBonus);
                if (dropItemId > 0) CreateDropItemAsync(dropItemId, position, default);
                return;
            }

            // 장비 — 아이템 레벨 = 몬스터 레벨. T11·T12 특수 판정은 몬스터 드랍만 한다
            if (randomValue < nothingRate + dropTable.CurrencyRate + dropTable.EquipmentRate)
            {
                int dropItemId = SelectEquipmentItem(dropTable, monsterLevel, dropRarityBonus);
                if (dropItemId > 0)
                    CreateDropItemAsync(dropItemId, position,
                        new EquipmentRollOptions(monsterLevel, modTier11ChanceBp, modTier12ChanceBp));
                return;
            }

            // 스킬북 — DropTable.Tier에 매칭되는 책 + 같은 Tier의 DropEnabled 스킬 풀에서 랜덤 픽
            if (randomValue < nothingRate + dropTable.CurrencyRate + dropTable.EquipmentRate + dropTable.SkillBookRate)
            {
                CreateSkillBookDropAsync(dropTable.Tier, position);
                return;
            }

            // 스킬 페이지 — DropTable.Tier에 매칭되는 등급 페이지 ItemId + PageCost 범위 매칭 SkillEffect 풀에서 랜덤 픽
            CreateSkillPageDropAsync(dropTable.Tier, position);
        }

        private static Vector3 GetScatteredPosition(Vector3 origin)
        {
            Vector2 offset = Random.insideUnitCircle * DROP_SCATTER_RADIUS;
            Vector3 candidate = new Vector3(origin.x + offset.x, origin.y + offset.y, origin.z);
            if (AR.s.Map != null &&
                AR.s.Map.IsTileBlocked(Mathf.FloorToInt(candidate.x), Mathf.FloorToInt(candidate.y)) == true)
            {
                return origin;
            }

            return candidate;
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

        private static async void CreateDropItemAsync(int itemId, Vector3 position, EquipmentRollOptions rollOptions)
        {
            if (await AR.s.Item.CreateItem(itemId, 1, position, rollOptions) == false)
            {
                Debug.LogError($"[DropHelper] Failed to create item with Id({itemId})");
            }
        }

        /// <summary>
        /// 스킬북 드랍 — DropTable.Tier에 매칭되는 등급 책 + 같은 Tier의 DropEnabled 스킬 풀에서 랜덤 SkillId.
        /// 책 또는 드롭 가능한 스킬이 없으면 드랍 스킵.
        /// </summary>
        private static async void CreateSkillBookDropAsync(int tier, Vector3 position)
        {
            ItemData? book = AR.s.Item.CreateRandomSkillBookOfTier(tier);
            if (book == null)
            {
                Debug.LogWarning($"[DropHelper] SkillBook drop skipped — Tier({tier})에 매칭되는 책 또는 스킬 풀이 비어있음");
                return;
            }

            if (await AR.s.Item.CreateItemFromData(book, position) == false)
            {
                Debug.LogError($"[DropHelper] Failed to spawn SkillBook drop. Tier({tier}), ItemId({book.Id}), SkillId({book.SkillBook?.SkillId})");
            }
        }

        /// <summary>
        /// 스킬 페이지 드랍 (SKILL_RUNE_DESIGN.md §8.1) — DropTable.Tier에 매칭되는 등급 페이지 + PageCost 범위로 SkillEffect 랜덤 픽.
        /// 풀이 비어 있으면 (페이지 ItemTable 행 없음 또는 PageCost 매칭 SkillEffect 없음) 조용히 드랍 스킵.
        /// </summary>
        private static async void CreateSkillPageDropAsync(int tier, Vector3 position)
        {
            ItemData? page = AR.s.Item.CreateRandomSkillPageOfTier(tier);
            if (page == null)
            {
                Debug.LogWarning($"[DropHelper] SkillPage drop skipped — Tier({tier})에 매칭되는 페이지 또는 SkillEffect 풀이 비어있음");
                return;
            }

            if (await AR.s.Item.CreateItemFromData(page, position) == false)
            {
                Debug.LogError($"[DropHelper] Failed to spawn SkillPage drop. Tier({tier}), ItemId({page.Id}), SkillEffectId({page.SkillPage?.SkillEffectId})");
            }
        }
    }
}
