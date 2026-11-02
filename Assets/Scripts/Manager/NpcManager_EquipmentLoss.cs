#nullable enable
using System.Collections.Generic;
using ARPG.Data;
using ARPG.History;
using ARPG.Npc.Narrative;
using ARPG.Utility;
using ARPG.Village;
using UnityEngine;
using GE = GlobalEnum;

namespace ARPG.Npc
{
    /// <summary>
    /// 원정 중 장비 분실과 빈 칸 보충 (NPC_OCCUPATIONS_PERSONAL_STORIES_PLAN §4.3·§9.2 ①④, 1단계).
    /// - 원정(사냥·던전) 중상이면 30% 확률로 장착한 금속 장비(대장 기술 제작 목록) 중 무작위 1개를 잃는다.
    ///   빈 칸이 있는 동안은 더 잃지 않는다. 화면 안에 실체화된 NPC는 잃지 않는다(장비 스탯을 다시 적용하지 않도록).
    ///   플레이어 동행 던전 전투는 이 경로를 거치지 않으므로 잃지 않는다.
    /// - 잃은 사실은 부상 사연 칸(Loss)과 장비 분실 소문으로 남고, 그 칸을 채우면(상점 기본 장비·위탁 제작품) 같은 사연에 붙는다.
    ///   빈 칸으로 7일 이상 지냈으면 생활 칸에 고생담을 남긴다.
    /// </summary>
    public partial class NpcManager
    {
        // 실체화 중에 산 빈 칸 보충품 (EntityId → ItemInstanceId). 비실체화 때 그 칸에 끼운다. 휘발성.
        private readonly Dictionary<int, int> _deferredRefillItemIds = new();

        /// <summary>
        /// 원정 중상 직후 장비 분실 판정 (§4.3). 결정론 난수(NPC EntityId, 원정 시작 시각, salt 244)로 확률 → 장비 고르기 순서.
        /// </summary>
        private void TryApplyExpeditionEquipmentLoss(int entityId, NpcSaveData saveData, float seedGameTime, int worldSeed,
            float outcomeGameTime)
        {
            _occupationCounters.SevereWounds++;
            if (saveData.HasPendingEquipmentLoss(out _) == true)
            {
                _occupationCounters.LossSkippedPending++;
                return;
            }
            if (saveData.IsActive == true || saveData.IsSpawning == true)
            {
                _occupationCounters.LossSkippedMaterialized++;
                return;
            }

            int metalCount = 0;
            for (int s = 0; s < saveData.InventoryEquip.Length; s++)
            {
                ItemData? equipped = saveData.InventoryEquip[s];
                if (equipped != null && NpcOccupationRules.IsMetalEquipment(equipped.Id) == true)
                    metalCount++;
            }
            if (metalCount == 0)
            {
                _occupationCounters.LossSkippedNoMetal++;
                return;
            }

            _occupationCounters.LossRolls++;
            HistoryRng rng = NpcActivityResolver.CreateRng(entityId, seedGameTime, worldSeed, NpcOccupationRules.EquipmentLossSalt);
            if (rng.NextDouble() >= NpcOccupationRules.EquipmentLossChance)
                return;
            int pick = rng.Range(0, metalCount);
            int slot = -1;
            for (int s = 0; s < saveData.InventoryEquip.Length; s++)
            {
                ItemData? equipped = saveData.InventoryEquip[s];
                if (equipped == null || NpcOccupationRules.IsMetalEquipment(equipped.Id) == false)
                    continue;
                if (pick == 0)
                {
                    slot = s;
                    break;
                }
                pick--;
            }
            if (slot < 0)
                return;

            MarkSaveDirty(entityId, NpcSaveGroup.Equipment | NpcSaveGroup.Stories);
            ItemData lost = saveData.InventoryEquip[slot]!;
            saveData.InventoryEquip[slot] = null;
            InvalidateNpcPower(entityId);
            string lostName = lost.Table != null ? lost.Table.Name : NpcRumorPhrasing.ItemName(lost.Id);
            (saveData.PersonalStories ??= new()).Loss = new NpcEquipmentLoss
            {
                Slot = slot,
                ItemId = lost.Id,
                ItemName = lostName,
                LostGameTime = outcomeGameTime,
            };
            _occupationCounters.Losses++;
            // 그 장비의 장비 소문은 무효가 된다 — 마을 명단에서 정리하고, 잃은 사실을 직접 겪은 소문으로 남긴다 (§9.2 ① 이웃이 듣는 말)
            PurgeInvalidRumorsInVillage(saveData.VillageId);
            if (saveData.Rumors.Count > 0)
                PruneInvalidRumors(saveData);
            NpcRumor rumor = new NpcRumor
            {
                Kind = NpcRumorKind.EquipmentLost,
                SubjectNpcId = entityId,
                ItemId = lost.Id,
                Value = GetItemValue(lost),
                EventGameTime = outcomeGameTime,
            };
            AddRumor(saveData, rumor, outcomeGameTime);
            _occupationCounters.LossRumors++;
            RumorCreatedObserver?.Invoke(entityId, rumor);
            _narrative.InvalidateDialogues(entityId);
            Debug.Log($"[NpcOccupation] EquipmentLost npc={entityId} slot={(GE.EquipSlotType)slot} item={DescribeItem(lost)} t={outcomeGameTime:F1}");
        }

        /// <summary>
        /// 빈 칸에 장비가 들어갔을 때 (ApplyUpgrade 공용): 그 칸이 잃은 장비의 칸이면 보충 결과를 분실 사연에 붙이고,
        /// 7일 이상 빈 칸이었으면 고생담을 남긴다 (§9.2 ①④).
        /// </summary>
        private void NotifyLostSlotFilled(int entityId, NpcSaveData saveData, GE.EquipSlotType slot, ItemData item, float gameTime)
        {
            if (saveData.HasPendingEquipmentLoss(out NpcEquipmentLoss? loss) == false || loss == null || loss.Slot != (int)slot)
                return;
            MarkSaveDirty(entityId, NpcSaveGroup.Stories);
            bool crafted = item.Equipment != null && item.Equipment.HasMaker == true && item.Equipment.MakerNpcId != entityId;
            loss.RefillGameTime = gameTime;
            loss.RefillItemId = item.Id;
            loss.RefillItemName = item.Table != null ? item.Table.Name : string.Empty;
            loss.RefillMakerId = crafted == true ? item.Equipment!.MakerNpcId : 0;
            loss.RefillMakerName = crafted == true ? item.Equipment!.MakerName ?? string.Empty : string.Empty;
            loss.LastRefillBlock = NpcRefillBlock.None;
            _occupationCounters.Refills++;
            if (crafted == true)
                _occupationCounters.RefillsFromConsignment++;
            else
                _occupationCounters.RefillsFromShop++;
            float emptyHours = gameTime - loss.LostGameTime;
            _occupationCounters.RefillHoursSum += Mathf.RoundToInt(emptyHours);
            if (emptyHours >= NpcOccupationRules.HardshipHours)
            {
                RecordLifeStory(entityId, saveData, new NpcLifeStory
                {
                    Kind = NpcLifeStoryKind.Hardship,
                    When = gameTime,
                    ItemId = item.Id,
                    ItemName = loss.RefillItemName,
                    OtherNpcId = crafted == true ? loss.RefillMakerId : -1,
                    OtherName = loss.RefillMakerName,
                    Days = Mathf.FloorToInt(emptyHours / 24f),
                    LostItemName = loss.ItemName,
                });
                _occupationCounters.HardshipStories++;
            }
            _narrative.InvalidateDialogues(entityId);
        }

        /// <summary>
        /// 빈 칸 보충 구매 (§4.3, 2026-11-01 결정): 잃은 칸에 맞는 장비를 상점 기본 장비 칸과 위탁 칸(다른 제작자)에서 찾는다.
        /// +15% 문턱 없이 호환 칸·개인 상한·소지금(치료비 예비금 제외)만 본다. 회차당 구매 한도·NPC 쿨다운에서 뺀다.
        /// 교체 후 전투력이 가장 높은 것(같으면 싼 것). 못 사면 이유를 분실 기록에 남긴다 (빈 칸 동안의 대사).
        /// </summary>
        private void TryRefillLostSlot(int entityId, NpcSaveData saveData, VillageData village, ref NpcTradeReport report)
        {
            if (saveData.HasPendingEquipmentLoss(out NpcEquipmentLoss? loss) == false || loss == null)
                return;
            if (loss.Slot < 0 || loss.Slot >= saveData.InventoryEquip.Length)
                return;
            MarkSaveDirty(entityId, NpcSaveGroup.Stories | NpcSaveGroup.Wallet | NpcSaveGroup.Inventory | NpcSaveGroup.Equipment);
            GE.EquipSlotType slot = (GE.EquipSlotType)loss.Slot;
            if (saveData.InventoryEquip[loss.Slot] != null)
            {
                // 다른 경로로 이미 채워졌다 — 그 장비로 마무리한다
                NotifyLostSlotFilled(entityId, saveData, slot, saveData.InventoryEquip[loss.Slot]!, AR.s.Time.CurrentGameTime);
                return;
            }
            bool isMaterialized = saveData.IsActive == true || saveData.IsSpawning == true;
            if (isMaterialized == true && FindEmptyInventorySlot(saveData) < 0)
                return;

            float cap = GetNpcPowerCap(saveData);
            int budget = saveData.Gold - GetTreatmentFee();
            GE.EquipmentTypeMask allowed = EquipHelper.SlotToEquipmentTypeMask(slot);
            float currentPower = GetNpcPower(entityId);
            int bestStock = -1, bestConsignment = -1, bestPrice = int.MaxValue;
            float bestPower = float.MinValue;
            bool anyCandidate = false;

            for (int i = 0; i < village.MerchantStock.Count; i++)
            {
                MerchantStockEntry entry = village.MerchantStock[i];
                if (AR.s.Village.IsNpcPurchasableEntry(village, entry) == false ||
                    Utils.ContainsEquipmentType(allowed, entry.Item.Equipment!.EquipType) == false)
                    continue;
                float power = EvaluateWithSlotItem(saveData, loss.Slot, entry.Item);
                if (cap > 0f && power > cap)
                    continue;
                anyCandidate = true;
                int price = VillageManager.GetNpcPurchasePrice(entry);
                if (price <= 0 || price > budget)
                    continue;
                if (power > bestPower || (power == bestPower && price < bestPrice))
                {
                    bestPower = power;
                    bestPrice = price;
                    bestStock = i;
                    bestConsignment = -1;
                }
            }
            for (int i = 0; i < village.Consignments.Count; i++)
            {
                VillageConsignment consignment = village.Consignments[i];
                ItemData item = consignment.Item;
                if (consignment.MakerNpcId == entityId || item?.Equipment == null ||
                    Utils.ContainsEquipmentType(allowed, item.Equipment.EquipType) == false)
                    continue;
                float power = EvaluateWithSlotItem(saveData, loss.Slot, item);
                if (cap > 0f && power > cap)
                    continue;
                anyCandidate = true;
                int price = AR.s.Village.GetConsignmentPrice(village, consignment);
                if (price <= 0 || price > budget)
                    continue;
                if (power > bestPower || (power == bestPower && price < bestPrice))
                {
                    bestPower = power;
                    bestPrice = price;
                    bestConsignment = i;
                    bestStock = -1;
                }
            }

            if (bestStock < 0 && bestConsignment < 0)
            {
                loss.LastRefillBlock = anyCandidate == true ? NpcRefillBlock.NoMoney : NpcRefillBlock.NoCandidate;
                if (anyCandidate == true)
                    _occupationCounters.RefillBlockedNoMoney++;
                else
                    _occupationCounters.RefillBlockedNoCandidate++;
                return;
            }

            ItemData? bought;
            if (bestConsignment >= 0)
            {
                bought = AR.s.Village.CommitConsignmentPurchase(village, bestConsignment, entityId, GetDisplayName(entityId), bestPrice);
                if (bought != null)
                {
                    _occupationCounters.ConsignmentSoldToNpc++;
                    _occupationCounters.ConsignmentSoldGold += bestPrice;
                }
            }
            else
            {
                // 빈 칸 보충은 회차당 구매 한도·상점 간격에서 뺀다 (§4.3) — 한도 카운트와 간격 시각을 되돌린다
                int countBefore = village.NpcPurchaseCount;
                float lastTradeBefore = village.LastNpcTradeGameTime;
                bought = AR.s.Village.CommitNpcPurchase(village, bestStock, entityId, GetDisplayName(entityId), bestPrice);
                if (bought != null)
                {
                    AR.s.Village.MarkSaveDirty(village.VillageId, VillageSaveGroup.Shop);
                    village.NpcPurchaseCount = countBefore;
                    village.LastNpcTradeGameTime = lastTradeBefore;
                }
            }
            if (bought == null)
                return;

            saveData.Gold -= bestPrice;
            report.Bought = true;
            report.BoughtItemId = bought.Id;
            report.BoughtBasicItemId = bought.Equipment != null ? bought.Equipment.BasicItemId : 0;
            report.BoughtPrice = bestPrice;
            report.BoughtEquipSlot = loss.Slot;
            Debug.Log($"[NpcOccupation] Refill npc={entityId} v={village.VillageId} slot={slot} item={DescribeItem(bought)} " +
                      $"price={bestPrice} consignment={bestConsignment >= 0} materialized={isMaterialized}");

            if (isMaterialized == true)
            {
                int emptySlot = FindEmptyInventorySlot(saveData);
                saveData.Inventory[emptySlot] = bought;
                _deferredRefillItemIds[entityId] = bought.ItemInstanceId;
                _occupationCounters.RefillsDeferred++;
                return;
            }
            if (ApplyUpgrade(entityId, saveData, bought, slot, currentPower, bestPower, AR.s.Time.CurrentGameTime, "Refill") == false)
                StoreInInventory(entityId, saveData, bought);
        }

        /// <summary>실체화 중에 산 빈 칸 보충품을 비실체화 때 그 칸에 끼운다.</summary>
        private void ApplyDeferredRefill(int entityId, NpcSaveData saveData)
        {
            if (_deferredRefillItemIds.TryGetValue(entityId, out int instanceId) == false)
                return;
            _deferredRefillItemIds.Remove(entityId);
            int bagSlot = FindInventorySlot(saveData, instanceId);
            if (bagSlot < 0 || saveData.HasPendingEquipmentLoss(out NpcEquipmentLoss? loss) == false || loss == null ||
                saveData.InventoryEquip[loss.Slot] != null)
                return;
            ItemData item = saveData.Inventory[bagSlot]!;
            saveData.Inventory[bagSlot] = null;
            float currentPower = GetNpcPower(entityId);
            float newPower = EvaluateWithSlotItem(saveData, loss.Slot, item);
            if (ApplyUpgrade(entityId, saveData, item, (GE.EquipSlotType)loss.Slot, currentPower, newPower,
                    AR.s.Time.CurrentGameTime, "Refill") == false)
                saveData.Inventory[bagSlot] = item;
        }

        /// <summary>이 칸에 이 장비를 넣었을 때의 전투력 (지금 장비 그대로 + 그 칸만 교체).</summary>
        private float EvaluateWithSlotItem(NpcSaveData saveData, int slot, ItemData item)
        {
            for (int i = 0; i < _upgradeScratchEquip.Length; i++)
                _upgradeScratchEquip[i] = saveData.InventoryEquip[i];
            _upgradeScratchEquip[slot] = item;
            float power = NpcPowerEvaluator.EvaluateBreakdown(saveData, _upgradeScratchEquip).Total;
            for (int i = 0; i < _upgradeScratchEquip.Length; i++)
                _upgradeScratchEquip[i] = null;
            return power;
        }
    }
}
