#nullable enable
using System.Collections.Generic;
using ARPG.Creature;
using ARPG.Data;
using ARPG.Village;
using UnityEngine;
using GE = GlobalEnum;

namespace ARPG.Npc
{
    /// <summary>NPC 상점 거래 1회의 결과 요약. 디버그 메뉴와 P1c 진단이 읽는다.</summary>
    public struct NpcTradeReport
    {
        public bool ShopAvailable;
        public int SoldCount;
        public int SoldGold;
        public int SellRejectedCount;     // 마을 Gold 부족으로 매입 거절
        public int KeptUnsellableCount;   // 매각 불가(오브 등) 보유
        public int ConsignedCount;        // 모험가 매입품으로 진열
        public bool Bought;
        public bool BuyDeferred;          // 실체화 중이라 장착을 비실체화 시점으로 미룸
        public int BoughtItemId;
        public int BoughtBasicItemId;     // 산 장비의 BasicItemId (D119 — 0이면 기본 아이템이 아닌 구매, 진단이 0건을 확인)
        public int BoughtPrice;
        public string BuySkipReason;
    }

    /// <summary>
    /// NPC 야망·모험가 경제 P1b — NPC 쪽 상점 거래와 추상 치료 (NPC_AMBITION_DESIGN §7.2, D1, D34, D36, D41, D45).
    /// 상점 쪽 원장(재고·마을 Gold·상점 보호)은 VillageManager의 NPC 전용 API가 맡고, 여기서는 무엇을 팔고 살지와
    /// NPC Gold·장착을 처리한다. 모든 대금은 NPC Gold ↔ 마을 Gold 사이에서만 움직인다 (닫힌 경제 — 합계 보존).
    /// P1c부터 System_NpcSim이 Trade 활동(1시간) 종료 시점에 TradeAtShop을 호출한다. 활동 후보 여부는 HasTradeOpportunity.
    /// </summary>
    public partial class NpcManager
    {
        // NPC별 쇼핑 쿨다운 (§7.2). 판매는 쿨다운 없이 매번 시도한다.
        public const float TradeCooldownHours = 48f;

        /// <summary>
        /// 지정 마을 상점에서 판매 → 구매 순으로 1회 거래한다.
        /// 판매: 매각 가능한 인벤토리 아이템을 플레이어와 같은 매입가로 판다. 마을 Gold가 모자라면 그 아이템은 매입 거절.
        /// 구매: D45 문턱(전체 전투력 +15%, 상한 80% 초과 시 +30%)·개인 상한·치료비 예비금·48h 쿨다운·상점 보호를
        /// 모두 통과한 기본 아이템 장비 매물(D119) 중 교체 후 전투력이 가장 높은 1개만 산다.
        /// </summary>
        public NpcTradeReport TradeAtShop(int entityId, int villageId)
        {
            NpcTradeReport report = new NpcTradeReport { BuySkipReason = string.Empty };
            if (_npcSaveDict.TryGetValue(entityId, out NpcSaveData? saveData) == false ||
                saveData.Condition == CharacterConditions.Dead)
            {
                report.BuySkipReason = "NoNpc";
                return report;
            }

            VillageData? village = AR.s.Village.PrepareNpcTrade(villageId);
            if (village == null)
            {
                report.BuySkipReason = "NoShop";
                Debug.Log($"[NpcAmbition] Trade Skip npc={entityId} v={villageId} reason=NoShop");
                return report;
            }

            report.ShopAvailable = true;
            saveData.EnsureSlotsInitialized();
            EnsureAmbitionInitialized(entityId, saveData);

            SellInventoryToShop(entityId, saveData, villageId, ref report);
            BuyUpgradeFromShop(entityId, saveData, village, ref report);
            return report;
        }

        /// <summary>
        /// 원정(Hunt·Delve) 부상 처리 — 솔로·파티원 공통 (NPC_AMBITION_DESIGN D36·D91).
        /// ① 중상 판정(별도 salt, 10%) → ② 기존 치료 판정·치료비 → ③ Wounded 상태 전이(회복 24h/8h, 중상 72h/24h)
        /// → ④ 누적 부상 요약 갱신, 중상이면 SevereWound Deed. 가벼운 부상은 Deed를 남기지 않는다.
        /// 각 판정은 따로 만든 RNG를 써서 기존 결과·치료·전리품 난수열에 영향이 없다. 회복 시간을 돌려준다.
        /// </summary>
        /// <param name="expedition">Hunt 또는 DungeonDelve</param>
        /// <param name="rngOwnerId">솔로 = NPC EntityId, 파티원 = PartyId</param>
        public float ApplyExpeditionWound(int entityId, NpcSaveData saveData, NpcActivityKind expedition, int rngOwnerId,
            float seedGameTime, int worldSeed, int treatmentSalt, int severeSalt, float outcomeGameTime,
            out bool treated, out bool severe)
        {
            severe = NpcActivityResolver.RollSevereWound(rngOwnerId, seedGameTime, worldSeed, severeSalt);
            float recoveryHours = ResolveWoundedTreatment(entityId, saveData, rngOwnerId, seedGameTime, worldSeed,
                treatmentSalt, severe, out treated);

            NpcActivityState activity = saveData.Activity;
            activity.Phase = NpcActivityPhase.Wounded;
            activity.Kind = NpcActivityKind.None;
            activity.PhaseEndGameTime = outcomeGameTime + recoveryHours;

            saveData.RecordExpeditionWound(outcomeGameTime, expedition);
            if (severe == true)
                AddSevereWoundDeed(entityId, outcomeGameTime);
            else
                _narrative.InvalidateDialogues(entityId);   // 부상 토픽(누적 요약)이 바뀐다
            return recoveryHours;
        }

        /// <summary>
        /// 부상 상태가 되는 순간 1회 호출한다 (솔로·파티). 소속 마을이 있고 Gold ≥ 치료비이며 결정론 판정을 통과하면
        /// 치료비를 소속 마을 Gold로 내고 치료 회복 시간, 아니면 기본 회복 시간을 돌려준다 (D36).
        /// 가벼운 부상 24h / 치료 8h, 중상 72h / 치료 24h (D91). 치료 확률·치료비는 등급과 무관하다.
        /// </summary>
        public float ResolveWoundedTreatment(int entityId, NpcSaveData saveData, int rngOwnerId, float seedGameTime,
            int worldSeed, int salt, bool severe, out bool treated)
        {
            treated = false;
            int fee = NpcActivityResolver.TreatmentFee;
            float untreatedHours = NpcActivityResolver.GetWoundRecoveryHours(severe, false);
            if (saveData.VillageId < 0 || AR.s.Village.GetVillage(saveData.VillageId) == null)
            {
                Debug.Log($"[NpcAmbition] Treatment npc={entityId} result=Skip reason=NoVillage gold={saveData.Gold} " +
                          $"severe={severe}");
                return untreatedHours;
            }
            if (saveData.Gold < fee)
            {
                Debug.Log($"[NpcAmbition] Treatment npc={entityId} v={saveData.VillageId} result=Skip reason=NoGold " +
                          $"gold={saveData.Gold} fee={fee} severe={severe}");
                return untreatedHours;
            }

            float buyChance = NpcActivityResolver.GetTreatmentBuyChance(saveData);
            if (NpcActivityResolver.RollTreatmentPurchase(buyChance, rngOwnerId, seedGameTime, worldSeed, salt) == false)
            {
                Debug.Log($"[NpcAmbition] Treatment npc={entityId} v={saveData.VillageId} result=Declined " +
                          $"chance={buyChance:F2} gold={saveData.Gold} severe={severe}");
                return untreatedHours;
            }

            int villageGoldBefore = AR.s.Village.GetVillageGold(saveData.VillageId);
            if (AR.s.Village.DepositNpcGold(saveData.VillageId, fee) == false)
                return untreatedHours;

            int npcGoldBefore = saveData.Gold;
            saveData.Gold -= fee;
            treated = true;
            float treatedHours = NpcActivityResolver.GetWoundRecoveryHours(severe, true);
            Debug.Log($"[NpcAmbition] Treatment npc={entityId} v={saveData.VillageId} result=Treated chance={buyChance:F2} " +
                      $"fee={fee} npcGold={npcGoldBefore}->{saveData.Gold} " +
                      $"villageGold={villageGoldBefore}->{AR.s.Village.GetVillageGold(saveData.VillageId)} " +
                      $"recovery={treatedHours:F0}h severe={severe}");
            return treatedHours;
        }

        private void SellInventoryToShop(int entityId, NpcSaveData saveData, int villageId, ref NpcTradeReport report)
        {
            // 실체화 중 장착을 미뤄 둔 업그레이드 후보는 팔지 않는다.
            _deferredPurchaseUpgradeItemIds.TryGetValue(entityId, out List<int>? deferred);

            for (int i = 0; i < saveData.Inventory.Count; i++)
            {
                ItemData? item = saveData.Inventory[i];
                if (item == null)
                    continue;
                if (deferred != null && deferred.Contains(item.ItemInstanceId) == true)
                    continue;

                int price = AR.s.Village.GetNpcSellPrice(villageId, item);
                if (price < 0)
                {
                    report.KeptUnsellableCount++;
                    continue;
                }

                int villageGoldBefore = AR.s.Village.GetVillageGold(villageId);
                if (AR.s.Village.TryCommitNpcSale(villageId, item, price, out bool consigned) == false)
                {
                    report.SellRejectedCount++;
                    Debug.Log($"[NpcAmbition] Trade SellRejected npc={entityId} v={villageId} item={DescribeItem(item)} " +
                              $"price={price} villageGold={villageGoldBefore} reason=VillageGoldShort");
                    continue;
                }

                int npcGoldBefore = saveData.Gold;
                saveData.Inventory[i] = null;
                saveData.Gold += price;
                report.SoldCount++;
                report.SoldGold += price;
                if (consigned == true)
                    report.ConsignedCount++;
                Debug.Log($"[NpcAmbition] Trade Sell npc={entityId} v={villageId} item={DescribeItem(item)} price={price} " +
                          $"npcGold={npcGoldBefore}->{saveData.Gold} " +
                          $"villageGold={villageGoldBefore}->{AR.s.Village.GetVillageGold(villageId)} consigned={consigned}");
                // 훔친 물건이면 판매 소문 (D104, NpcManager_Theft). 일반 판매는 아무것도 하지 않는다.
                NotifyNpcItemSold(entityId, saveData, villageId, item, AR.s.Time.CurrentGameTime);
            }
        }

        private void BuyUpgradeFromShop(int entityId, NpcSaveData saveData, VillageData village,
            ref NpcTradeReport report)
        {
            float now = AR.s.Time.CurrentGameTime;
            int villageId = village.VillageId;

            if (saveData.LastTradeGameTime >= 0f && now - saveData.LastTradeGameTime < TradeCooldownHours)
            {
                LogBuySkip(entityId, villageId, "NpcCooldown",
                    $"remain={TradeCooldownHours - (now - saveData.LastTradeGameTime):F1}h", ref report);
                return;
            }

            NpcShopPurchaseBlock block = AR.s.Village.GetNpcPurchaseBlock(village);
            if (block == NpcShopPurchaseBlock.CycleLimit)
            {
                LogBuySkip(entityId, villageId, "ShopCycleLimit",
                    $"count={village.NpcPurchaseCount}/{AR.s.Village.GetNpcPurchaseLimitPerCycle(village)} " +
                    $"cycle={village.NpcPurchaseCycle}", ref report);
                return;
            }
            if (block == NpcShopPurchaseBlock.Interval)
            {
                LogBuySkip(entityId, villageId, "ShopInterval",
                    $"since={now - village.LastNpcTradeGameTime:F1}h/{VillageManager.NpcPurchaseIntervalHours:F0}h",
                    ref report);
                return;
            }

            if (GetNpcPowerCap(saveData) <= 0f)
            {
                LogBuySkip(entityId, villageId, "NoCap", string.Empty, ref report);
                return;
            }

            // 실체화 중이면 장착을 미루고 빈 칸에 보관한다. 빈 칸이 없으면 산 물건이 버림 규칙에 걸릴 수 있어 사지 않는다.
            bool isMaterialized = saveData.IsActive == true || saveData.IsSpawning == true;
            int emptySlot = FindEmptyInventorySlot(saveData);
            if (isMaterialized == true && emptySlot < 0)
            {
                LogBuySkip(entityId, villageId, "MaterializedBagFull", string.Empty, ref report);
                return;
            }

            float currentPower = GetNpcPower(entityId);
            int bestIndex = FindBestShopUpgrade(entityId, saveData, village, out int bestPrice, out float bestPower,
                out GE.EquipSlotType bestSlot, out int eligibleCount, out int overBudgetCount);

            if (bestIndex < 0)
            {
                LogBuySkip(entityId, villageId, "NoCandidate",
                    $"eligible={eligibleCount} overBudget={overBudgetCount} gold={saveData.Gold}", ref report);
                return;
            }

            int npcGoldBefore = saveData.Gold;
            int villageGoldBefore = AR.s.Village.GetVillageGold(villageId);
            ItemData? bought = AR.s.Village.CommitNpcPurchase(village, bestIndex, entityId, GetDisplayName(entityId),
                bestPrice);
            if (bought == null)
            {
                LogBuySkip(entityId, villageId, "CommitFailed", string.Empty, ref report);
                return;
            }

            saveData.Gold -= bestPrice;
            saveData.LastTradeGameTime = now;
            report.Bought = true;
            report.BoughtItemId = bought.Id;
            report.BoughtBasicItemId = bought.Equipment != null ? bought.Equipment.BasicItemId : 0;
            report.BoughtPrice = bestPrice;

            Debug.Log($"[NpcAmbition] Trade Buy npc={entityId} v={villageId} item={DescribeItem(bought)} price={bestPrice} " +
                      $"npcGold={npcGoldBefore}->{saveData.Gold} " +
                      $"villageGold={villageGoldBefore}->{AR.s.Village.GetVillageGold(villageId)} " +
                      $"shopCount={village.NpcPurchaseCount}/{AR.s.Village.GetNpcPurchaseLimitPerCycle(village)} " +
                      $"materialized={isMaterialized}");

            if (isMaterialized == false)
            {
                if (ApplyUpgrade(entityId, saveData, bought, bestSlot, currentPower, bestPower, now, "Trade") == false)
                    StoreInInventory(entityId, saveData, bought);
                return;
            }

            // 실체화 중: 비실체화 시점에 다시 평가해 장착한다 (D45). 전리품은 장착하지 않으므로(D119) 지연 장착은 구매뿐이다.
            saveData.Inventory[emptySlot] = bought;
            if (_deferredPurchaseUpgradeItemIds.TryGetValue(entityId, out List<int>? pending) == false)
            {
                pending = new List<int>();
                _deferredPurchaseUpgradeItemIds.Add(entityId, pending);
            }
            pending.Add(bought.ItemInstanceId);
            report.BuyDeferred = true;
            Debug.Log($"[NpcAmbition] UpgradeDeferred npc={entityId} item={DescribeItem(bought)} (materialized, trade)");
        }

        /// <summary>
        /// 구매 후보 탐색. 재고 쪽 조건(IsNpcPurchasableEntry)과 예산(가격 ≤ Gold − 치료비 예비금, D36)을 통과한 장비 중
        /// D45 문턱·개인 상한을 지키는 업그레이드에서 교체 후 전투력이 가장 높은 1개(동점이면 싼 것)의 재고 인덱스. 없으면 -1.
        /// 실제 구매(BuyUpgradeFromShop)와 Trade 기회 판정(HasTradeOpportunity)이 같은 규칙을 쓰도록 한곳에 둔다.
        /// </summary>
        private int FindBestShopUpgrade(int entityId, NpcSaveData saveData, VillageData village, out int bestPrice,
            out float bestPower, out GE.EquipSlotType bestSlot, out int eligibleCount, out int overBudgetCount)
        {
            int budget = saveData.Gold - NpcActivityResolver.TreatmentFee;
            int bestIndex = -1;
            bestPrice = int.MaxValue;
            bestPower = 0f;
            bestSlot = GE.EquipSlotType.Max;
            eligibleCount = 0;
            overBudgetCount = 0;
            for (int i = 0; i < village.MerchantStock.Count; i++)
            {
                MerchantStockEntry entry = village.MerchantStock[i];
                if (AR.s.Village.IsNpcPurchasableEntry(village, entry) == false)
                    continue;
                eligibleCount++;

                int price = VillageManager.GetNpcPurchasePrice(entry);
                if (price <= 0 || price > budget)
                {
                    overBudgetCount++;
                    continue;
                }
                if (EvaluateBestUpgrade(entityId, saveData, entry.Item, out GE.EquipSlotType slot, out _,
                        out float newPower) == false)
                    continue;

                if (bestIndex < 0 || newPower > bestPower || (newPower == bestPower && price < bestPrice))
                {
                    bestIndex = i;
                    bestPrice = price;
                    bestPower = newPower;
                    bestSlot = slot;
                }
            }
            return bestIndex;
        }

        /// <summary>
        /// Trade 활동 기회 판정 (NPC_AMBITION_DESIGN §6.3 Opportunity, D39). 소속 마을에 Shop이 있고 할 일이 있을 때만 true.
        /// 할 일 = (매각 가능 아이템이 있고 마을 Gold ≥ 그중 최저 매입가) 또는
        ///         (구매 쿨다운 끝 + 상점 보호 통과 + 예산 안의 업그레이드 후보 존재).
        /// 실제 거래 결과는 활동 종료 시 TradeAtShop이 확정한다 — 그 사이 마을 Gold·재고가 바뀌면 빈손으로 끝날 수 있다.
        /// 부수효과: PrepareNpcTrade가 현재 회차 재고를 아직 롤하지 않았으면 결정론 롤을 수행한다 (플레이어 상점 열기와 같은 롤).
        /// </summary>
        public bool HasTradeOpportunity(int entityId, NpcSaveData saveData)
        {
            if (saveData.VillageId < 0 || saveData.Condition == CharacterConditions.Dead)
                return false;
            VillageData? village = AR.s.Village.PrepareNpcTrade(saveData.VillageId);
            if (village == null)
                return false;

            saveData.EnsureSlotsInitialized();
            if (HasSellableForShop(entityId, saveData, village.VillageId) == true)
                return true;
            return HasPurchasableUpgrade(entityId, saveData, village);
        }

        private bool HasSellableForShop(int entityId, NpcSaveData saveData, int villageId)
        {
            _deferredPurchaseUpgradeItemIds.TryGetValue(entityId, out List<int>? deferred);
            int villageGold = AR.s.Village.GetVillageGold(villageId);
            for (int i = 0; i < saveData.Inventory.Count; i++)
            {
                ItemData? item = saveData.Inventory[i];
                if (item == null)
                    continue;
                if (deferred != null && deferred.Contains(item.ItemInstanceId) == true)
                    continue;

                int price = AR.s.Village.GetNpcSellPrice(villageId, item);
                if (price >= 0 && price <= villageGold)
                    return true;
            }
            return false;
        }

        private bool HasPurchasableUpgrade(int entityId, NpcSaveData saveData, VillageData village)
        {
            // 싼 조건부터 거른다. 재고 순회(업그레이드 평가)는 마지막에만 한다.
            if (saveData.Gold - NpcActivityResolver.TreatmentFee <= 0)
                return false;
            float now = AR.s.Time.CurrentGameTime;
            if (saveData.LastTradeGameTime >= 0f && now - saveData.LastTradeGameTime < TradeCooldownHours)
                return false;
            if (AR.s.Village.GetNpcPurchaseBlock(village) != NpcShopPurchaseBlock.None)
                return false;
            if (GetNpcPowerCap(saveData) <= 0f)
                return false;
            bool isMaterialized = saveData.IsActive == true || saveData.IsSpawning == true;
            if (isMaterialized == true && FindEmptyInventorySlot(saveData) < 0)
                return false;

            return FindBestShopUpgrade(entityId, saveData, village, out _, out _, out _, out _, out _) >= 0;
        }

        private static void LogBuySkip(int entityId, int villageId, string reason, string detail,
            ref NpcTradeReport report)
        {
            report.BuySkipReason = reason;
            Debug.Log($"[NpcAmbition] Trade BuySkip npc={entityId} v={villageId} reason={reason} {detail}");
        }

        private static int FindEmptyInventorySlot(NpcSaveData saveData)
        {
            for (int i = 0; i < saveData.Inventory.Count; i++)
            {
                if (saveData.Inventory[i] == null)
                    return i;
            }
            return -1;
        }
    }
}
