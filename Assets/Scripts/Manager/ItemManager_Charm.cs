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
        private readonly List<int> _charmOptionWeightScratch = new List<int>();
        private readonly List<int> _charmOptionIndexScratch = new List<int>();

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

        /// <summary>
        /// 참 1개: 모양을 비중으로 고르고 등급을 굴린다. rarityMultiplier는 아이템 희귀도 배율로,
        /// 플레이어 드롭만 1보다 크고 NPC 전리품·상점은 1이다 (§6.3).
        /// </summary>
        public ItemData? CreateRandomCharm(int itemLevel, float rarityMultiplier = 1f, HistoryRng? inRng = null)
        {
            int shapeIndex = CharmRules.SelectWeighted(CharmRules.ShapeWeights,
                RollRange(inRng, 0, CharmRules.SumWeights(CharmRules.ShapeWeights)));
            return CreateCharm((GE.CharmShape)shapeIndex, RollCharmGrade(rarityMultiplier, inRng), itemLevel, inRng);
        }

        /// <summary>
        /// 모양·등급을 정한 참 1개 (§6.3). 옵션은 장비처럼 접두·접미로 나눠 붙인다.
        /// - 일반: 옵션 없음.
        /// - 마법: 1개면 접두·접미 후보 전체에서, 2개면 접두 1 + 접미 1. 티어는 마법 규칙(위 2단계 가중치 2배).
        /// - 희귀: 3~4개를 접두·접미에 각 2개 이하로 나눈다. 한쪽 후보가 모자라면 다른 쪽으로 채운다.
        /// 후보는 모양 크기 묶음이 허용하는 옵션이고, 값은 참 값 표(CharmModTierTable)에 모양별로 적힌 범위다.
        /// </summary>
        public ItemData? CreateCharm(GE.CharmShape shape, GE.EquipmentGrade grade, int itemLevel, HistoryRng? inRng = null)
        {
            if (CharmRules.IsValidShape(shape) == false)
            {
                Debug.LogError($"[ItemManager] CreateCharm - 만들 수 없는 모양 {shape}");
                return null;
            }

            int itemId = CharmRules.GetCharmItemId(shape);
            ItemTable? table = AR.s.Data?.GetItem(itemId);
            if (table == null || table.ItemType != GE.ItemType.Charm)
            {
                Debug.LogError($"[ItemManager] CreateCharm - ItemTable({itemId}) is not a Charm row");
                return null;
            }

            CharmData charm = new CharmData { Shape = shape, ItemLevel = ItemLevelModTierRules.ClampItemLevel(itemLevel) };
            switch (grade)
            {
                case GE.EquipmentGrade.Magic:
                    if (RollRange(inRng, CharmRules.MAGIC_OPTION_MIN, CharmRules.MAGIC_OPTION_MAX + 1) <= 1)
                    {
                        RollCharmOptions(charm, false, GE.ModSlot.Prefix, 1, true, inRng);
                    }
                    else
                    {
                        RollCharmOptions(charm, true, GE.ModSlot.Prefix, 1, true, inRng);
                        RollCharmOptions(charm, true, GE.ModSlot.Postfix, 1, true, inRng);
                    }
                    break;

                case GE.EquipmentGrade.Rare:
                {
                    int limit = CharmRules.AFFIX_LIMIT;
                    int total = RollRange(inRng, CharmRules.RARE_OPTION_MIN, CharmRules.RARE_OPTION_MAX + 1);
                    int prefixTarget = RollRange(inRng, Mathf.Max(0, total - limit), Mathf.Min(limit, total) + 1);
                    int prefixAdded = RollCharmOptions(charm, true, GE.ModSlot.Prefix, prefixTarget, false, inRng);
                    int postfixAdded = RollCharmOptions(charm, true, GE.ModSlot.Postfix,
                        Mathf.Min(limit, total - prefixAdded), false, inRng);
                    int remaining = total - prefixAdded - postfixAdded;
                    if (remaining > 0 && prefixAdded < limit)
                        RollCharmOptions(charm, true, GE.ModSlot.Prefix, Mathf.Min(remaining, limit - prefixAdded), false, inRng);
                    break;
                }
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

        /// <summary>등급 1번 굴리기 (CharmRules.ResolveGrade). 난수 2회.</summary>
        private GE.EquipmentGrade RollCharmGrade(float rarityMultiplier, HistoryRng? inRng)
        {
            int rareRoll = RollRange(inRng, 0, CharmRules.GRADE_ROLL_SCALE);
            int magicRoll = RollRange(inRng, 0, CharmRules.GRADE_ROLL_SCALE);
            return CharmRules.ResolveGrade(rarityMultiplier, rareRoll, magicRoll);
        }

        /// <summary>
        /// 모양 옵션 풀에서 count개를 가중치로 골라 붙인다. filterSlot이면 slot 칸 옵션만, 아니면 접두·접미 모두가 후보다.
        /// 이미 붙은 옵션은 다시 붙지 않는다. 아이템 레벨에 열린 티어가 없는 옵션은 건너뛴다. 실제로 붙인 개수를 돌려준다.
        /// </summary>
        private int RollCharmOptions(CharmData charm, bool filterSlot, GE.ModSlot slot, int count, bool magicRule,
            HistoryRng? inRng)
        {
            if (count <= 0)
                return 0;

            _charmOptionIndexScratch.Clear();
            _charmOptionWeightScratch.Clear();
            IReadOnlyList<int> modIds = CharmRules.OptionModIds;
            for (int i = 0; i < modIds.Count; i++)
            {
                if (CharmRules.IsOptionAllowed(i, charm.Shape) == false || HasCharmMod(charm, modIds[i]) == true)
                    continue;
                ModTable? modTable = AR.s.Data.GetMod(modIds[i]);
                if (modTable == null)
                    continue;
                if (modTable.Slot != GE.ModSlot.Prefix && modTable.Slot != GE.ModSlot.Postfix)
                    continue;
                if (filterSlot == true && modTable.Slot != slot)
                    continue;
                _charmOptionIndexScratch.Add(i);
                _charmOptionWeightScratch.Add(CharmRules.OptionWeights[i]);
            }

            int added = 0;
            while (added < count && _charmOptionIndexScratch.Count > 0)
            {
                int pickIndex = CharmRules.SelectWeighted(_charmOptionWeightScratch,
                    RollRange(inRng, 0, CharmRules.SumWeights(_charmOptionWeightScratch)));
                int modId = modIds[_charmOptionIndexScratch[pickIndex]];
                _charmOptionIndexScratch.RemoveAt(pickIndex);
                _charmOptionWeightScratch.RemoveAt(pickIndex);

                ModInstance? mod = RollCharmOption(modId, charm.Shape, charm.ItemLevel, magicRule, inRng);
                if (mod == null)
                    continue;
                charm.Mods.Add(mod);
                added++;
            }
            return added;
        }

        private static bool HasCharmMod(CharmData charm, int modId)
        {
            for (int i = 0; i < charm.Mods.Count; i++)
            {
                if (charm.Mods[i].ModTableId == modId)
                    return true;
            }
            return false;
        }

        /// <summary>
        /// 옵션 1개: 티어는 장비처럼 ModTierTable(열리는 레벨·가중치)에서 고르고, 값은 참 값 표(CharmModTierTable)의 그 티어·모양 칸 범위다.
        /// 그 칸이 없으면 붙이지 않는다.
        /// </summary>
        private ModInstance? RollCharmOption(int modId, GE.CharmShape shape, int itemLevel, bool magicRule, HistoryRng? inRng)
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
                    _tierCandidateScratch, _tierWeightScratch, magicRule) == 0)
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
            CharmModTierTable? valueRow = AR.s.Data.GetCharmModTier(modId, tier.Tier);
            if (valueRow == null || CharmRules.TryGetOptionValueRange(valueRow, shape, out int min, out int max) == false)
            {
                Debug.LogWarning($"[ItemManager] RollCharmOption - CharmModTier(Mod {modId}, T{tier.Tier})에 {shape} 값이 없습니다");
                return null;
            }

            return new ModInstance
            {
                ModTableId = modId,
                Slot = modTable.Slot,
                Tier = tier.Tier,
                Value1 = (ushort)RollRange(inRng, min, max + 1),
                Table = modTable,
            };
        }
    }
}
