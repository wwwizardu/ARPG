#nullable enable
using System.Globalization;
using ARPG.Creature;
using ARPG.Data;
using ARPG.History;
using ARPG.Tables;
using ARPG.Village;
using UnityEngine;

namespace ARPG.Npc
{
    /// <summary>
    /// NPC 채광·제작 (NPC_OCCUPATIONS_PERSONAL_STORIES_PLAN 1단계 §3·§5.5·§6). 직업 없이 숙련과 여건으로 활동을 고른다.
    /// - 숙련: 채광·대장 숙련과 합산 상한을 첫 활동 선택 때 한 번 정한다 (InitialPower와 같은 방식, 소속 마을 구역 기준).
    /// - 채광: 8시간 마을 밖 추상 채광, 종료 때 광석 3~5개. 플레이어 광맥은 읽거나 바꾸지 않는다. 판매는 기존 상점 거래가 한다.
    /// - 제작: 같은 마을에 장비를 잃은 NPC가 있고 그 장비가 상점에 없을 때만, 광석 3개로 그 장비의 기본 아이템을 만든다(제작자 기록).
    /// 게임(System_NpcSim)과 진단(Ambition Sim)이 같은 공용 메서드를 부른다. 상태는 NpcSaveData에 있고 여기 인스턴스 필드는 진단 카운터뿐이다.
    /// </summary>
    public partial class NpcManager
    {
        // 진단 카운터 (휘발성). Ambition Sim이 새 인스턴스로 바꿔 끼운다.
        private NpcOccupationCounters _occupationCounters = new();

        public NpcOccupationCounters OccupationCounters => _occupationCounters;

        // ================= 숙련 =================

        /// <summary>
        /// 채광·대장 숙련과 합산 상한을 아직 정하지 않았으면 한 번 정한다 (§3.1). 기존 세이브의 NPC도 같은 규칙으로 처음 고를 때 정해지고,
        /// 이주민은 도착한 마을에서 처음 고를 때라 그 마을 구역 기준이다. 로드·이주만으로 다시 굴리지 않는다.
        /// </summary>
        public void EnsureLifeSkillsInitialized(int entityId, NpcSaveData saveData)
        {
            if (saveData.LifeSkillsInitialized == true)
                return;
            MarkSaveDirty(entityId, NpcSaveGroup.Identity);
            int zone = GetNpcLootZoneLevel(saveData);
            HistoryRng rng = new HistoryRng(DerivedSeed.Derive(entityId, DerivedSeed.ChannelNpcLifeSkill));
            NpcOccupationRules.RollInitialSkills(zone, rng, out int mining, out int smithing, out int cap);
            saveData.MiningSkill = mining;
            saveData.SmithingSkill = smithing;
            saveData.LifeSkillCap = cap;
        }

        /// <summary>성공한 작업 1회의 숙련 상승 판정 (§3.1). 플레이어 확률 × 10%, 합산 상한 안에서만. 올랐으면 true.</summary>
        private bool RollLifeSkillGain(int entityId, NpcSaveData saveData, NpcLifeSkill skill, int requiredSkill,
            float seedGameTime, int worldSeed, int salt)
        {
            float chance = NpcOccupationRules.GetNpcSkillGainChance(saveData.GetLifeSkill(skill), requiredSkill);
            if (chance <= 0f)
                return false;
            HistoryRng rng = NpcActivityResolver.CreateRng(entityId, seedGameTime, worldSeed, salt);
            if (rng.NextDouble() >= chance)
                return false;
            MarkSaveDirty(entityId, NpcSaveGroup.Identity);
            if (saveData.TryRaiseLifeSkill(skill) == false)
                return false;
            if (skill == NpcLifeSkill.Mining)
                _occupationCounters.MiningSkillGains++;
            else
                _occupationCounters.SmithingSkillGains++;
            return true;
        }

        // ================= 활동 기회 (§3·§5.5·§6.2) =================

        /// <summary>
        /// 채광·제작 후보를 활동 선택 입력에 채운다. 채광 = 소속 마을 상점이 사 줄 광석이 있고(매입가 배율 > 0) 가방에 빈 칸이 있음.
        /// 제작 = 제작 수요(장비를 잃은 이웃)가 있고 가방에 그 광석이 3개 있음.
        /// </summary>
        private void FillOccupationChoiceContext(int entityId, NpcSaveData saveData, ref NpcActivityChoiceContext context)
        {
            if (saveData.VillageId < 0 || saveData.Condition == CharacterConditions.Dead)
                return;
            VillageData? village = AR.s.Village.GetVillage(saveData.VillageId);
            if (village == null)
                return;

            bool hasDemand = TryGetCraftDemand(entityId, saveData, village, out SmithingRecipe recipe, out _);
            int demandOreInBag = hasDemand == true ? CountBagItem(saveData, recipe.OreItemId) : 0;
            context.CraftingAvailable = hasDemand == true && demandOreInBag >= NpcOccupationRules.OresPerCraft;

            if (FindEmptyInventorySlot(saveData) < 0)
                return;
            int zone = GetNpcLootZoneLevel(saveData);
            bool hasShop = AR.s.Village.PrepareNpcTrade(village.VillageId) != null;
            float factor = 0f;
            if (hasShop == true)
            {
                factor = Mathf.Max(factor, MiningPriceFactorFor(village, zone, saveData.MiningSkill, NpcOccupationRules.CopperOreItemId));
                factor = Mathf.Max(factor, MiningPriceFactorFor(village, zone, saveData.MiningSkill, NpcOccupationRules.TinOreItemId));
                factor = Mathf.Max(factor, MiningPriceFactorFor(village, zone, saveData.MiningSkill, NpcOccupationRules.IronOreItemId));
            }
            // 자기 제작에 필요한 광석이 상점에 모자라고 직접 캘 수 있으면 캐러 간다 (§6.2 재료 확보 — 상점에 없으면 직접 채광)
            if (hasDemand == true && demandOreInBag < NpcOccupationRules.OresPerCraft &&
                NpcOccupationRules.CanMineOre(zone, saveData.MiningSkill, recipe.OreItemId) == true &&
                VillageManager.GetOreStock(village, recipe.OreItemId) < NpcOccupationRules.OresPerCraft - demandOreInBag)
                factor = Mathf.Max(factor, 1f) * 3f;
            context.MiningAvailable = factor > 0f;
            context.MiningPriceFactor = factor;
        }

        private static float MiningPriceFactorFor(VillageData village, int zone, int miningSkill, int oreItemId)
        {
            if (NpcOccupationRules.CanMineOre(zone, miningSkill, oreItemId) == false)
                return 0f;
            return AR.s.Village.GetOreBuyPriceFactor(village, oreItemId);
        }

        /// <summary>
        /// 제작 수요 (§4.3·§6.2): 대장 숙련으로 만들 수 있고, 같은 마을에 그 장비를 잃어 칸이 빈 NPC가 있으며, 상점(기본 장비 칸·위탁 칸)에
        /// 그 장비가 없을 때. 마을 Forge가 있어야 하고 자기 위탁품(진열 + 가방의 위탁 전 제작품)이 2개 미만이어야 한다.
        /// 여럿이면 가장 먼저 잃은 NPC부터 (같으면 EntityId가 작은 쪽).
        /// </summary>
        public bool TryGetCraftDemand(int entityId, NpcSaveData saveData, VillageData village, out SmithingRecipe recipe,
            out int targetNpcId)
        {
            recipe = default;
            targetNpcId = -1;
            if (saveData.SmithingSkill <= 0)
                return false;
            if (AR.s.Village.CountByService(village.VillageId, ProvidedService.Forge) <= 0)
                return false;
            int own = VillageManager.CountConsignments(village, entityId) + CountUnconsignedCrafts(entityId, saveData);
            if (own >= NpcOccupationRules.ConsignmentsPerMaker)
                return false;

            float bestLostTime = float.MaxValue;
            for (int i = 0; i < village.NpcEntityIds.Count; i++)
            {
                int otherId = village.NpcEntityIds[i];
                if (otherId == entityId || _npcSaveDict.TryGetValue(otherId, out NpcSaveData? other) == false)
                    continue;
                if (other.Condition == CharacterConditions.Dead || other.HasPendingEquipmentLoss(out NpcEquipmentLoss? loss) == false ||
                    loss == null)
                    continue;
                if (NpcOccupationRules.TryGetRecipe(loss.ItemId, out SmithingRecipe candidate) == false ||
                    saveData.SmithingSkill < candidate.RequiredSkill)
                    continue;
                if (loss.LostGameTime > bestLostTime || (loss.LostGameTime == bestLostTime && otherId > targetNpcId))
                    continue;
                if (AR.s.Village.HasShopEquipment(village, loss.ItemId) == true)
                    continue;
                bestLostTime = loss.LostGameTime;
                targetNpcId = otherId;
                recipe = candidate;
            }
            return targetNpcId >= 0;
        }

        /// <summary>제작 활동의 TargetKey ("craft:{ItemId}:{빈 칸 NPC}"). 활동을 시작할 때 수요를 고정한다.</summary>
        public string GetCraftingTargetKey(int entityId, NpcSaveData saveData)
        {
            VillageData? village = saveData.VillageId >= 0 ? AR.s.Village.GetVillage(saveData.VillageId) : null;
            if (village == null || TryGetCraftDemand(entityId, saveData, village, out SmithingRecipe recipe, out int targetNpcId) == false)
                return "craft";
            return "craft:" + recipe.ItemId.ToString(CultureInfo.InvariantCulture) + ":" +
                   targetNpcId.ToString(CultureInfo.InvariantCulture);
        }

        // ================= 채광 종료 (§5.5) =================

        /// <summary>
        /// 채광 종료 1회 판정 (§5.5). 소속 마을 구역·채광 숙련으로 캘 수 있는 광종을 플레이어 광맥과 같은 비율로 고르고(자기 제작에
        /// 필요한 광석이 있으면 그것) 3~5개를 가방에 넣는다. 들어가는 만큼만 받는다. 숙련 상승·첫 주석·철 이정표를 기록하고 Idle로 돌린다.
        /// 위험은 없다 (첫 버전은 채광 중 부상 없음).
        /// </summary>
        public void ResolveMiningEnd(int entityId, NpcSaveData saveData, float outcomeGameTime, int worldSeed)
        {
            MarkSaveDirty(entityId, NpcSaveGroup.ActivityTransition | NpcSaveGroup.Inventory | NpcSaveGroup.Identity);
            NpcActivityState activity = saveData.Activity;
            float seed = activity.OutcomeSeedGameTime > 0f ? activity.OutcomeSeedGameTime : activity.PhaseStartedGameTime;
            SetActivityIdle(saveData, outcomeGameTime);
            if (saveData.Condition == CharacterConditions.Dead)
                return;

            EnsureLifeSkillsInitialized(entityId, saveData);
            int zone = GetNpcLootZoneLevel(saveData);
            HistoryRng rng = NpcActivityResolver.CreateRng(entityId, seed, worldSeed, NpcOccupationRules.MiningYieldSalt);
            int ore = NpcOccupationRules.SelectMiningOre(zone, saveData.MiningSkill, rng.NextDouble());
            int count = rng.Range(NpcOccupationRules.MiningYieldMin, NpcOccupationRules.MiningYieldMax + 1);
            VillageData? village = saveData.VillageId >= 0 ? AR.s.Village.GetVillage(saveData.VillageId) : null;
            if (village != null && TryGetCraftDemand(entityId, saveData, village, out SmithingRecipe recipe, out _) == true &&
                NpcOccupationRules.CanMineOre(zone, saveData.MiningSkill, recipe.OreItemId) == true)
                ore = recipe.OreItemId;

            int stored = 0;
            ItemData? oreItem = AR.s.Item.CreateInventoryItemData(ore, count);
            if (oreItem != null && CreateNpcInventoryView(saveData).AddItem(oreItem) >= 0)
                stored = count;
            _occupationCounters.MiningTrips++;
            _occupationCounters.OreMined += stored;
            if (stored < count)
                _occupationCounters.OreMinedNoRoom += count - stored;
            if (ore == NpcOccupationRules.TinOreItemId) _occupationCounters.TinMined += stored;
            else if (ore == NpcOccupationRules.IronOreItemId) _occupationCounters.IronMined += stored;

            NpcWorkRecord work = saveData.Work ??= new NpcWorkRecord();
            work.PreviousMiningGameTime = work.LastMiningGameTime;
            work.LastMiningGameTime = outcomeGameTime;
            work.MiningCount++;
            if (stored > 0)
            {
                NpcWorkRecord.Milestone milestone = ore == NpcOccupationRules.TinOreItemId ? NpcWorkRecord.Milestone.MinedTin
                    : ore == NpcOccupationRules.IronOreItemId ? NpcWorkRecord.Milestone.MinedIron : NpcWorkRecord.Milestone.None;
                if (milestone != NpcWorkRecord.Milestone.None && (work.Milestones & milestone) == 0)
                {
                    work.Milestones |= milestone;
                    RecordLifeStory(entityId, saveData, new NpcLifeStory
                    {
                        Kind = NpcLifeStoryKind.MiningMilestone,
                        When = outcomeGameTime,
                        OreItemId = ore,
                    });
                }
                RollLifeSkillGain(entityId, saveData, NpcLifeSkill.Mining, NpcOccupationRules.GetOreRequiredSkill(ore), seed, worldSeed,
                    NpcOccupationRules.MiningSkillGainSalt);
            }
        }

        // ================= 제작 종료 (§6.1·§6.3) =================

        /// <summary>
        /// 제작 종료 1회 판정 (§6.1). 시작할 때 고정한 수요(TargetKey)의 장비를 마을 Forge에서 만든다: 광석 3개를 완료 때 쓰고,
        /// 기본 아이템 변형은 가중치 결정론 무작위(§6.3.1), 제작자 기록을 붙여 가방에 둔다 (위탁 전까지 버리지 않는다).
        /// 조건이 깨졌으면(광석·Forge·숙련·자리) 아무것도 쓰지 않고 끝난다. 숙련 상승·첫 철 장비 이정표를 기록한다.
        /// </summary>
        public void ResolveCraftingEnd(int entityId, NpcSaveData saveData, float outcomeGameTime, int worldSeed)
        {
            MarkSaveDirty(entityId, NpcSaveGroup.ActivityTransition | NpcSaveGroup.Inventory | NpcSaveGroup.Identity);
            NpcActivityState activity = saveData.Activity;
            float seed = activity.PhaseStartedGameTime;
            string targetKey = activity.TargetKey ?? string.Empty;
            SetActivityIdle(saveData, outcomeGameTime);
            if (saveData.Condition == CharacterConditions.Dead)
                return;

            if (TryParseCraftingKey(targetKey, out int itemId, out int targetNpcId) == false ||
                NpcOccupationRules.TryGetRecipe(itemId, out SmithingRecipe recipe) == false)
            {
                _occupationCounters.CraftsAborted++;
                return;
            }
            VillageData? village = saveData.VillageId >= 0 ? AR.s.Village.GetVillage(saveData.VillageId) : null;
            int own = village != null ? VillageManager.CountConsignments(village, entityId) + CountUnconsignedCrafts(entityId, saveData) : 0;
            if (village == null || saveData.SmithingSkill < recipe.RequiredSkill ||
                AR.s.Village.CountByService(village.VillageId, ProvidedService.Forge) <= 0 ||
                own >= NpcOccupationRules.ConsignmentsPerMaker)
            {
                _occupationCounters.CraftsAborted++;
                return;
            }
            if (CountBagItem(saveData, recipe.OreItemId) < NpcOccupationRules.OresPerCraft)
            {
                NpcWorkRecord blocked = saveData.Work ??= new NpcWorkRecord();
                blocked.CraftBlockedGameTime = outcomeGameTime;
                blocked.CraftBlockedOreItemId = recipe.OreItemId;
                _occupationCounters.CraftsAborted++;
                return;
            }

            HistoryRng rng = NpcActivityResolver.CreateRng(entityId, seed, worldSeed, NpcOccupationRules.CraftVariantSalt);
            BasicItemTable? variant = AR.s.Item.PickBasicItemVariant(itemId, rng.NextDouble());
            ItemData? crafted = variant != null ? AR.s.Item.CreateBasicItem(variant.Id) : null;
            if (crafted?.Equipment == null)
            {
                _occupationCounters.CraftsAborted++;
                return;
            }
            crafted.Equipment.MakerNpcId = entityId;
            crafted.Equipment.MakerName = GetDisplayName(entityId);
            ConsumeBagItem(saveData, recipe.OreItemId, NpcOccupationRules.OresPerCraft);
            StoreInInventory(entityId, saveData, crafted);
            _occupationCounters.CraftsCompleted++;
            if (targetNpcId >= 0)
                _occupationCounters.CraftsForLoss++;

            NpcWorkRecord work = saveData.Work ??= new NpcWorkRecord();
            work.PreviousCraftGameTime = work.LastCraftGameTime;
            work.LastCraftGameTime = outcomeGameTime;
            work.CraftCount++;
            work.LastCraftItemInstanceId = crafted.ItemInstanceId;
            work.LastCraftForNpcId = targetNpcId;
            work.CraftBlockedGameTime = -1f;
            work.CraftBlockedOreItemId = 0;
            if (recipe.OreItemId == NpcOccupationRules.IronOreItemId && (work.Milestones & NpcWorkRecord.Milestone.CraftedIron) == 0)
            {
                work.Milestones |= NpcWorkRecord.Milestone.CraftedIron;
                RecordLifeStory(entityId, saveData, new NpcLifeStory
                {
                    Kind = NpcLifeStoryKind.SmithingMilestone,
                    When = outcomeGameTime,
                    ItemId = itemId,
                    ItemName = crafted.Table != null ? crafted.Table.Name : string.Empty,
                    OreItemId = recipe.OreItemId,
                });
            }
            RollLifeSkillGain(entityId, saveData, NpcLifeSkill.Smithing, recipe.RequiredSkill, seed, worldSeed,
                NpcOccupationRules.SmithingSkillGainSalt);
        }

        // ================= 공용 =================

        /// <summary>생활 칸 갱신 + 대화 캐시 무효화 (§9.2 ②③④).</summary>
        private void RecordLifeStory(int entityId, NpcSaveData saveData, NpcLifeStory story)
        {
            MarkSaveDirty(entityId, NpcSaveGroup.Stories);
            saveData.RecordLifeStory(story);
            _occupationCounters.LifeStories++;
            _narrative.InvalidateDialogues(entityId);
        }

        private static void SetActivityIdle(NpcSaveData saveData, float gameTime)
        {
            NpcActivityState activity = saveData.Activity;
            activity.Phase = NpcActivityPhase.Idle;
            activity.Kind = NpcActivityKind.None;
            activity.TargetKey = string.Empty;
            activity.HasMovementPath = false;
            activity.PhaseStartedGameTime = gameTime;
            activity.PhaseEndGameTime = 0f;
            saveData.LastSimGameTime = gameTime;
        }

        private static bool TryParseCraftingKey(string key, out int itemId, out int targetNpcId)
        {
            itemId = 0;
            targetNpcId = -1;
            string[] parts = key.Split(':');
            return parts.Length == 3 && parts[0] == "craft" &&
                   int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out itemId) &&
                   int.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out targetNpcId);
        }

        /// <summary>가방에 있는 이 아이템의 총 수량.</summary>
        private static int CountBagItem(NpcSaveData saveData, int itemId)
        {
            int count = 0;
            for (int i = 0; i < saveData.Inventory.Count; i++)
            {
                ItemData? item = saveData.Inventory[i];
                if (item != null && item.Id == itemId)
                    count += Mathf.Max(1, item.Quantity);
            }
            return count;
        }

        /// <summary>가방에서 이 아이템을 count개 뺀다 (뒤 칸부터). 호출자가 수량을 먼저 확인한다.</summary>
        private static void ConsumeBagItem(NpcSaveData saveData, int itemId, int count)
        {
            for (int i = saveData.Inventory.Count - 1; i >= 0 && count > 0; i--)
            {
                ItemData? item = saveData.Inventory[i];
                if (item == null || item.Id != itemId)
                    continue;
                int quantity = Mathf.Max(1, item.Quantity);
                int take = Mathf.Min(quantity, count);
                count -= take;
                if (take >= quantity)
                    saveData.Inventory[i] = null;
                else
                    item.Quantity = quantity - take;
            }
        }

        /// <summary>가방에 있는 내 제작품(위탁 전) 수. 위탁품 2개 제한에 함께 센다.</summary>
        private static int CountUnconsignedCrafts(int entityId, NpcSaveData saveData)
        {
            int count = 0;
            for (int i = 0; i < saveData.Inventory.Count; i++)
            {
                if (IsOwnUnconsignedCraft(entityId, saveData.Inventory[i]) == true)
                    count++;
            }
            return count;
        }

        /// <summary>내가 만들어 아직 위탁하지 않은 장비인가. 일반 매각 대신 위탁으로 나가고 가치순 버림에서 빠진다 (§2.3).</summary>
        private static bool IsOwnUnconsignedCraft(int entityId, ItemData? item)
            => item?.Equipment != null && item.Equipment.MakerNpcId == entityId;

        /// <summary>
        /// 버리지 않는 가방 물건 (§2.3, 2026-11-01 결정): 위탁 전 내 제작품, 지금 제작 수요가 있을 때 그 광석(다음 제작 1회분).
        /// </summary>
        private bool IsDiscardProtected(int entityId, NpcSaveData saveData, ItemData item)
        {
            if (IsOwnUnconsignedCraft(entityId, item) == true)
                return true;
            if (NpcOccupationRules.IsOre(item.Id) == false || saveData.VillageId < 0)
                return false;
            VillageData? village = AR.s.Village.GetVillage(saveData.VillageId);
            return village != null && TryGetCraftDemand(entityId, saveData, village, out SmithingRecipe recipe, out _) == true &&
                   recipe.OreItemId == item.Id;
        }
    }

    /// <summary>NPC 채광·제작·분실·위탁 진단 카운터 (휘발성 — 저장하지 않는다). Ambition Sim이 새 인스턴스로 바꿔 끼운다.</summary>
    public sealed class NpcOccupationCounters
    {
        public long MiningTrips, OreMined, OreMinedNoRoom, TinMined, IronMined;
        public long MiningSkillGains, SmithingSkillGains;
        public long CraftsCompleted, CraftsForLoss, CraftsAborted;
        public long OreSold, OreSoldGold, OreSellRejectedCap, OreSellRejectedGold, OreReserved;
        public long OreBought, OreBoughtGold, OreBuyNoStock, OreBuyNoMoney;
        public long Consigned, ConsignNoSlot, ConsignmentSoldToNpc, ConsignmentSoldGold, ProceedsSettled, ProceedsSettledGold;
        public long SevereWounds, LossRolls, Losses, LossSkippedPending, LossSkippedNoMetal, LossSkippedMaterialized;
        public long Refills, RefillsFromConsignment, RefillsFromShop, RefillHoursSum, RefillsDeferred;
        public long RefillBlockedNoCandidate, RefillBlockedNoMoney;
        public long ConsignmentUpgrades;
        public long LifeStories, HardshipStories, CraftSoldStories, LossRumors;
    }
}
