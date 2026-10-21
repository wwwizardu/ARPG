#nullable enable
using System.Collections.Generic;
using ARPG.Component;
using ARPG.Data;
using ARPG.Item;
using ARPG.Tables;
using UnityEngine;
using GE = GlobalEnum;

namespace ARPG.Utility
{
    /// <summary>
    /// 플레이어가 받는 드롭(몬스터 사망·던전 상자) 처리 (ITEM_DROP_BALANCE_DESIGN.md §7·§8).
    /// 수량·희귀도는 DropBonusResolver가 계산한 결과만 받는다. NPC 전리품·상점 매물은 이 경로를 쓰지 않는다(D12).
    /// </summary>
    public static class DropHelper
    {
        // 판정 2회 이상일 때 아이템을 흩는 반경 (타일)
        private const float DROP_SCATTER_RADIUS = 0.8f;
        // 마법 장비 옵션 수 (§4.3): 1개 50%, 2개 50%
        private const int MAGIC_AFFIX_MIN = 1;
        private const int MAGIC_AFFIX_MAX = 2;

        // 화폐 가중치 scratch — 쓸 때마다 비운다 (상태를 남기지 않는다)
        private static readonly List<float> _currencyWeightScratch = new List<float>();

        /// <summary>
        /// 몬스터 1마리의 드롭. 판정 횟수 = 기본 판정 × max(0, 1 + Q/100) (소수 부분은 확률, 상한 40회).
        /// 2회 이상이면 아이템이 한 점에 겹치지 않게 판정마다 위치를 흩는다. 흩은 칸이 막혀 있으면 원래 위치에 떨군다.
        /// </summary>
        public static void ProcessDrops(int monsterEntityId, DropComponent drop, Vector3 position)
        {
            DropBonus bonus = DropBonusResolver.ResolveForMonster(monsterEntityId, drop);
            int rolls = DropBonusResolver.ResolveRollCount(Mathf.Max(1, drop.DropRolls), bonus.QuantityPercent, Random.value);
            for (int i = 0; i < rolls; i++)
            {
                Vector3 rollPosition = rolls > 1 ? GetScatteredPosition(position) : position;
                ProcessDrop(drop.DropId, rollPosition, drop.MonsterLevel, bonus,
                    drop.ModTier11ChanceBp, drop.ModTier12ChanceBp);
            }
        }

        /// <summary>
        /// 던전 상자 드롭 (§7.6). 몬스터 드롭과 같은 드롭 1번 행 판정을 쓰고, 플레이어·장소 묶음만 적용한다.
        /// 상자 장비는 T11·T12 특수 판정을 하지 않는다.
        /// </summary>
        public static void ProcessChestDrops(int baseRolls, int itemLevel, Vector3 position)
        {
            DropBonus bonus = DropBonusResolver.ResolveForChest(itemLevel);
            int rolls = DropBonusResolver.ResolveRollCount(baseRolls, bonus.QuantityPercent, Random.value);
            for (int i = 0; i < rolls; i++)
                ProcessDrop(PlayerDropTableId, GetScatteredPosition(position), itemLevel, bonus, 0, 0);
        }

        /// <summary>몬스터·상자가 함께 쓰는 드롭표 행. 모든 몬스터가 1번 행을 쓴다 (§3).</summary>
        public const int PlayerDropTableId = 1;

        /// <summary>
        /// 드롭표 판정 1번. 구성(꽝·화폐·장비·책·페이지)은 표 그대로 쓰고 꽝 칸을 줄이지 않는다 (§8.2).
        /// </summary>
        /// <param name="monsterLevel">몬스터 레벨 (구간표 행·장비 구간 기준, 드랍 장비의 아이템 레벨)</param>
        public static void ProcessDrop(int dropId, Vector3 position, int monsterLevel, DropBonus bonus,
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

            int nothingRate = Mathf.Max(0, dropTable.NothingRate);
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
                int dropItemId = SelectCurrencyItem(dropTable, monsterLevel, bonus);
                if (dropItemId > 0) CreateDropItemAsync(dropItemId, position, default);
                return;
            }

            // 장비 — 아이템 레벨 = 몬스터 레벨. 등급·품질은 구간표와 희귀도로 정하고, T11·T12 특수 판정은 몬스터 드랍만 한다
            if (randomValue < nothingRate + dropTable.CurrencyRate + dropTable.EquipmentRate)
            {
                int dropItemId = SelectEquipmentItem(dropTable, monsterLevel, bonus.Band);
                if (dropItemId > 0)
                    CreateDropItemAsync(dropItemId, position,
                        RollEquipmentOptions(monsterLevel, bonus, modTier11ChanceBp, modTier12ChanceBp));
                return;
            }

            // 스킬북 — DropTable.Tier에 매칭되는 책 + 같은 Tier의 DropEnabled 스킬 풀. 책 보너스 범위는 구간표 (§7.4)
            if (randomValue < nothingRate + dropTable.CurrencyRate + dropTable.EquipmentRate + dropTable.SkillBookRate)
            {
                CreateSkillBookDropAsync(dropTable.Tier, position, bonus.Band);
                return;
            }

            // 스킬 페이지 — DropTable.Tier에 매칭되는 등급 페이지 ItemId + PageCost 범위 매칭 SkillEffect 풀에서 랜덤 픽
            CreateSkillPageDropAsync(dropTable.Tier, position);
        }

        /// <summary>
        /// 장비 등급·옵션 수·품질을 정한다 (§4.3·§5·§8.2). 구간표 행이 없으면 희귀(옛 규칙에 가까운 3~6개)로 만든다.
        /// </summary>
        public static EquipmentRollOptions RollEquipmentOptions(int itemLevel, DropBonus bonus,
            int modTier11ChanceBp, int modTier12ChanceBp)
        {
            DropBandTable? band = bonus.Band;
            if (band == null)
            {
                return EquipmentRollOptions.ForPlayerDrop(itemLevel, modTier11ChanceBp, modTier12ChanceBp,
                    GE.EquipmentGrade.Rare, Random.Range(3, 7), 0);
            }

            GE.EquipmentGrade grade = DropBonusResolver.ResolveGrade(band, bonus.RarityMultiplier, Random.value, Random.value);
            int affixCount = 0;
            int quality = 0;
            switch (grade)
            {
                case GE.EquipmentGrade.Rare:
                    affixCount = Random.Range(band.RareModMin, band.RareModMax + 1);
                    break;
                case GE.EquipmentGrade.Magic:
                    affixCount = Random.Range(MAGIC_AFFIX_MIN, MAGIC_AFFIX_MAX + 1);
                    break;
                default:
                    quality = Random.Range(band.NormalQualityMin, band.NormalQualityMax + 1);
                    break;
            }
            return EquipmentRollOptions.ForPlayerDrop(itemLevel, modTier11ChanceBp, modTier12ChanceBp, grade, affixCount, quality);
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

        private static int SelectCurrencyItem(DropTable dropTable, int monsterLevel, DropBonus bonus)
        {
            // Pool 모드: ItemTable 기반 레벨 풀. Tier 2 이상은 구간표 상위 화폐 배율 × 희귀도 배율 (§7.3)
            if (dropTable.CurrencyPoolMode == 1)
                return SelectWeightedCurrency(DropPoolBuilder.GetCurrencyPool(monsterLevel), bonus);

            // Explicit 모드: 기존 DropCurrencyTable 사용
            return SelectRandomCurrencyItem(dropTable.CurrencyId);
        }

        private static int SelectWeightedCurrency(List<DropPoolEntry> pool, DropBonus bonus)
        {
            _currencyWeightScratch.Clear();
            float total = 0f;
            for (int i = 0; i < pool.Count; i++)
            {
                float weight = DropBonusResolver.GetCurrencyWeight(pool[i].Weight, pool[i].Tier, bonus.Band, bonus.RarityMultiplier);
                _currencyWeightScratch.Add(weight);
                total += weight;
            }
            if (total <= 0f)
                return 0;

            float roll = Random.value * total;
            float cumulative = 0f;
            for (int i = 0; i < pool.Count; i++)
            {
                cumulative += _currencyWeightScratch[i];
                if (roll < cumulative)
                    return pool[i].ItemId;
            }
            // 부동소수 끝자락 — 가중치가 있는 마지막 항목
            for (int i = pool.Count - 1; i >= 0; i--)
            {
                if (_currencyWeightScratch[i] > 0f)
                    return pool[i].ItemId;
            }
            return 0;
        }

        private static int SelectEquipmentItem(DropTable dropTable, int monsterLevel, DropBandTable? band)
        {
            // Pool 모드: 플레이어 드롭 전용 선택 순서 (부위 몫 → 구간 80/20 → DropRate, §7.2).
            // 공유 풀(DropPoolBuilder.GetEquipmentPool)은 NPC 전리품·상점 매물용으로 남는다
            if (dropTable.EquipmentPoolMode == 1)
            {
                int currentBandShare = band != null ? band.CurrentBandShare : PlayerEquipmentDropSelector.DEFAULT_CURRENT_BAND_SHARE;
                return PlayerEquipmentDropSelector.Select(monsterLevel, currentBandShare, Random.value, Random.value, Random.value);
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
        /// 페이지 용량 보너스 범위와 슬롯 +1 확률은 구간표 값이다. 책 또는 드롭 가능한 스킬이 없으면 드랍 스킵.
        /// </summary>
        private static async void CreateSkillBookDropAsync(int tier, Vector3 position, DropBandTable? band)
        {
            SkillBookBonusRange? range = band != null
                ? new SkillBookBonusRange(band.BookCapacityMin, band.BookCapacityMax, band.BookSlotChance)
                : (SkillBookBonusRange?)null;
            ItemData? book = AR.s.Item.CreateRandomSkillBookOfTier(tier, null, range);
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
