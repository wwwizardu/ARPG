#nullable enable
using System.Collections.Generic;
using ARPG.Data;
using ARPG.History;
using ARPG.Tables;
using UnityEngine;
using GE = GlobalEnum;

namespace ARPG.Item
{
    /// <summary>
    /// 참 판·참 생성 (PLAYER_LEVEL_CHARM_DESIGN.md §6.2·§6.3). 규칙 값은 CharmRules, 난수는 다른 아이템과 같은 RollRange
    /// (rng가 있으면 결정론 — NPC 전리품·상점, 없으면 UnityEngine.Random — 플레이어 드롭).
    /// </summary>
    public partial class ItemManager
    {
        // 사용 직전 비우는 스크래치 (상태 없음)
        private readonly List<int> _charmCellScratch = new List<int>();
        private readonly List<int> _charmOptionWeightScratch = new List<int>();
        private readonly List<int> _charmOptionIdScratch = new List<int>();

        /// <summary>판 1개: 막힌 칸 수를 비중으로 고르고 위치를 무작위로 정한다. 박힌 참은 없다.</summary>
        public ItemData? CreateRandomCharmBoard(HistoryRng? inRng = null)
        {
            ItemTable? table = AR.s.Data?.GetItem(CharmRules.BOARD_ITEM_ID);
            if (table == null || table.ItemType != GE.ItemType.CharmBoard)
            {
                Debug.LogError($"[ItemManager] CreateRandomCharmBoard - ItemTable({CharmRules.BOARD_ITEM_ID}) is not a CharmBoard row");
                return null;
            }

            int blockedCount = CharmRules.SelectWeighted(CharmRules.BlockedCountWeights,
                RollRange(inRng, 0, CharmRules.SumWeights(CharmRules.BlockedCountWeights)));
            int mask = 0;
            for (int placed = 0; placed < blockedCount; placed++)
            {
                // 남은 빈칸 중 하나를 고른다 — 같은 칸을 두 번 막지 않는다
                int free = CharmRules.CELL_COUNT - placed;
                int pick = RollRange(inRng, 0, free);
                for (int cell = 0; cell < CharmRules.CELL_COUNT; cell++)
                {
                    if ((mask & (1 << cell)) != 0)
                        continue;
                    if (pick == 0)
                    {
                        mask |= 1 << cell;
                        break;
                    }
                    pick--;
                }
            }

            return new ItemData
            {
                Id = table.Id,
                ItemInstanceId = _instanceIdCounter++,
                Quantity = 1,
                Table = table,
                CharmBoard = new CharmBoardData { BlockedMask = mask },
            };
        }

        /// <summary>참 1개: 모양을 비중으로 고른다.</summary>
        public ItemData? CreateRandomCharm(int itemLevel, HistoryRng? inRng = null)
        {
            int shapeIndex = CharmRules.SelectWeighted(CharmRules.ShapeWeights,
                RollRange(inRng, 0, CharmRules.SumWeights(CharmRules.ShapeWeights)));
            return CreateCharm((GE.CharmShape)shapeIndex, itemLevel, inRng);
        }

        /// <summary>
        /// 모양을 정한 참 1개. 옵션 수(반반) → 같은 옵션 없이 가중치로 옵션 → 아이템 레벨 티어(T1~T10) →
        /// 장비 기준값 × 모양별 비율로 값을 굴린다 (§6.3).
        /// </summary>
        public ItemData? CreateCharm(GE.CharmShape shape, int itemLevel, HistoryRng? inRng = null)
        {
            int itemId = CharmRules.GetCharmItemId(shape);
            ItemTable? table = AR.s.Data?.GetItem(itemId);
            if (table == null || table.ItemType != GE.ItemType.Charm)
            {
                Debug.LogError($"[ItemManager] CreateCharm - ItemTable({itemId}) is not a Charm row");
                return null;
            }

            int level = ItemLevelModTierRules.ClampItemLevel(itemLevel);
            int optionCount = RollRange(inRng, CharmRules.GetOptionCountMin(shape), CharmRules.GetOptionCountMax(shape) + 1);
            int ratio = CharmRules.GetOptionRatioPercent(shape, optionCount);

            CharmData charm = new CharmData { Shape = shape, ItemLevel = level };
            _charmOptionIdScratch.Clear();
            _charmOptionWeightScratch.Clear();
            for (int i = 0; i < CharmRules.OptionModIds.Count; i++)
            {
                _charmOptionIdScratch.Add(CharmRules.OptionModIds[i]);
                _charmOptionWeightScratch.Add(CharmRules.OptionWeights[i]);
            }

            while (charm.Mods.Count < optionCount && _charmOptionIdScratch.Count > 0)
            {
                int pickIndex = CharmRules.SelectWeighted(_charmOptionWeightScratch,
                    RollRange(inRng, 0, CharmRules.SumWeights(_charmOptionWeightScratch)));
                int modId = _charmOptionIdScratch[pickIndex];
                _charmOptionIdScratch.RemoveAt(pickIndex);
                _charmOptionWeightScratch.RemoveAt(pickIndex);

                ModInstance? mod = RollCharmOption(modId, level, ratio, inRng);
                if (mod != null)
                    charm.Mods.Add(mod);
            }

            return new ItemData
            {
                Id = itemId,
                ItemInstanceId = _instanceIdCounter++,
                Quantity = 1,
                Table = table,
                Charm = charm,
            };
        }

        private ModInstance? RollCharmOption(int modId, int itemLevel, int ratioPercent, HistoryRng? inRng)
        {
            ModTable? modTable = AR.s.Data.GetMod(modId);
            List<ModTierTable> tiers = AR.s.Data.GetModTiers(modId);
            if (modTable == null || tiers == null || tiers.Count == 0)
            {
                Debug.LogWarning($"[ItemManager] RollCharmOption - Mod({modId}) 또는 티어 행이 없습니다");
                return null;
            }

            // 참은 T1~T10 일반 판정만 쓴다 (§6.3)
            if (ItemLevelModTierRules.BuildNormalTierCandidates(tiers, itemLevel,
                    _tierCandidateScratch, _tierWeightScratch, false) == 0)
                return null;
            for (int i = _tierCandidateScratch.Count - 1; i >= 0; i--)
            {
                if (_tierCandidateScratch[i].Tier > CharmRules.MAX_CHARM_TIER)
                {
                    _tierCandidateScratch.RemoveAt(i);
                    _tierWeightScratch.RemoveAt(i);
                }
            }
            if (_tierCandidateScratch.Count == 0)
                return null;

            int tierRoll = RollRange(inRng, 0, ItemLevelModTierRules.GetTotalWeight(_tierWeightScratch));
            ModTierTable tier = _tierCandidateScratch[ItemLevelModTierRules.SelectWeightedIndex(_tierWeightScratch, tierRoll)];
            CharmRules.GetOptionValueRange(tier, ratioPercent, out int min, out int max);

            return new ModInstance
            {
                ModTableId = modId,
                Slot = GE.ModSlot.Charm,
                Tier = tier.Tier,
                Value1 = (ushort)RollRange(inRng, min, max + 1),
                Table = modTable,
            };
        }
    }
}
