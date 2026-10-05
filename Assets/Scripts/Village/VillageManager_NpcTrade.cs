#nullable enable
using ARPG.Data;
using UnityEngine;

namespace ARPG.Village
{
    /// <summary>NPC 구매가 상점 보호 규칙에 막힌 사유 (NPC_AMBITION_DESIGN §7.2). None = 구매 가능.</summary>
    public enum NpcShopPurchaseBlock
    {
        None = 0,
        CycleLimit = 1,     // 재입고 회차당 매물 종류의 25%(1~3) 소진
        Interval = 2,       // 상점당 게임시간 6시간 1건
    }

    /// <summary>
    /// NPC 야망·모험가 경제 P1b — 상점 쪽 NPC 거래 트랜잭션 (NPC_AMBITION_DESIGN §7.2, D1, D34, D41, D42).
    /// 플레이어 거래 API(SellItemToMerchant/BuyItemFromMerchant)와 분리한다. 플레이어 Gold·알림·즉시 저장을 건드리지 않고,
    /// 대금은 전부 마을 Gold(VillageData.Resources[Gold])로만 오간다 (닫힌 경제).
    /// NPC 쪽 판단(무엇을 팔고 살지, NPC Gold 증감, 장착)은 NpcManager가 맡는다.
    /// </summary>
    public partial class VillageManager
    {
        // 상점 보호 (§7.2). 튜닝 대상 (§14 Q5).
        public const float NpcPurchaseStockShare = 0.25f;
        public const int NpcPurchaseMinPerCycle = 1;
        public const int NpcPurchaseMaxPerCycle = 3;
        public const float NpcPurchaseIntervalHours = 6f;
        // 모험가 매입품 슬롯 (D42). 회차당 최대 2개.
        public const int MaxAdventurerConsignmentSlots = 2;

        /// <summary>
        /// 사라진 터인가 — 연결된 연대기 정착지가 폐허·합병(운영하지 않는 Fate)인 마을 (NPC_AMBITION_DESIGN D138).
        /// 손제작 마을(SettlementId &lt; 0, BaseTown)은 정착지가 아니므로 터가 아니다.
        /// </summary>
        public bool IsAbandonedSite(VillageData village)
        {
            if (village.SettlementId < 0)
                return false;
            if (AR.s.History.TryGetLiveSettlement(village.SettlementId, out ARPG.History.SettlementLiveState settlement) == false)
                return false;
            return VillageDevelopmentBootstrap.IsOperationalFate(settlement.Fate) == false;
        }

        /// <summary>
        /// 이주 목적지 (D140): from에서 가장 가까운, 사라진 터가 아니고 상점(Shop 서비스)이 있는 마을.
        /// 거리가 같으면 VillageId가 작은 쪽. 같은 터 출신은 같은 기준점(터 위치)으로 고르므로 함께 간다. 없으면 null.
        /// </summary>
        public VillageData? FindNearestLivingShopVillage(Vector2 from, int excludeVillageId)
        {
            VillageData? best = null;
            float bestSqr = float.MaxValue;
            for (int i = 0; i < _villageList.Count; i++)
            {
                VillageData candidate = _villageList[i];
                if (candidate.VillageId == excludeVillageId || IsAbandonedSite(candidate) == true)
                    continue;
                if (CountByService(candidate.VillageId, ProvidedService.Shop) <= 0)
                    continue;
                float sqr = Vector2.SqrMagnitude(candidate.Position - from);
                if (sqr < bestSqr || (sqr == bestSqr && best != null && candidate.VillageId < best.VillageId))
                {
                    best = candidate;
                    bestSqr = sqr;
                }
            }
            return best;
        }

        /// <summary>
        /// NPC 거래·치료 대금을 마을 Gold에 넣는다 (D41). 상한 없이 더하고 알림을 띄우지 않는다.
        /// 마을이 없으면 false — 호출자는 NPC Gold를 빼지 않아야 한다.
        /// </summary>
        public bool DepositNpcGold(int villageId, int amount)
        {
            if (amount < 0)
                return false;
            if (_villages.TryGetValue(villageId, out VillageData? data) == false)
                return false;

            MarkSaveDirty(villageId, ARPG.Data.VillageSaveGroup.Resources);
            VillageResourceSimulation.DepositNpcGold(data, amount);
            SyncStorageFromData(data);
            return true;
        }

        /// <summary>
        /// NPC에게 줄 대금을 마을 Gold에서 뺀다 (D34, D41). 부족하면 빼지 않고 false. 알림 없음.
        /// </summary>
        public bool TryWithdrawNpcGold(int villageId, int amount)
        {
            if (amount < 0)
                return false;
            if (_villages.TryGetValue(villageId, out VillageData? data) == false)
                return false;
            MarkSaveDirty(villageId, ARPG.Data.VillageSaveGroup.Resources);
            if (VillageResourceSimulation.TryWithdrawNpcGold(data, amount) == false)
                return false;

            SyncStorageFromData(data);
            return true;
        }

        /// <summary>
        /// NPC 거래 준비. 상점(Shop 서비스)이 없는 마을은 null.
        /// 현재 회차 재고가 아직 롤되지 않았으면 플레이어가 상점을 열 때와 같은 결정론 롤을 먼저 수행하고 (§7.2 LOD),
        /// 회차가 바뀌었으면 NPC 구매 카운트를 새 회차로 넘긴다.
        /// </summary>
        public VillageData? PrepareNpcTrade(int villageId)
        {
            if (_villages.TryGetValue(villageId, out VillageData? v) == false)
                return null;
            if (CountByService(villageId, ProvidedService.Shop) <= 0)
                return null;

            EnsureMerchantStockFresh(villageId);

            int cycle = GetCurrentRestockCycle(v);
            if (v.NpcPurchaseCycle != cycle)
            {
                MarkSaveDirty(villageId, ARPG.Data.VillageSaveGroup.Shop);
                v.NpcPurchaseCycle = cycle;
                v.NpcPurchaseCount = 0;
            }
            return v;
        }

        /// <summary>상점 쪽 빈도 제한 판정 (§7.2). PrepareNpcTrade 이후 호출한다.</summary>
        public NpcShopPurchaseBlock GetNpcPurchaseBlock(VillageData v)
        {
            if (v.NpcPurchaseCount >= GetNpcPurchaseLimitPerCycle(v))
                return NpcShopPurchaseBlock.CycleLimit;

            float now = AR.s.Time.CurrentGameTime;
            if (v.LastNpcTradeGameTime >= 0f && now - v.LastNpcTradeGameTime < NpcPurchaseIntervalHours)
                return NpcShopPurchaseBlock.Interval;
            return NpcShopPurchaseBlock.None;
        }

        /// <summary>
        /// 회차당 NPC 구매 한도 = 롤된 매물 종류 수 × 25% (내림), 최소 1·최대 3.
        /// 모험가 매입품과 기본 장비 칸은 세지 않는다 — 기본 장비 칸 추가(D117) 전과 같은 한도를 유지한다.
        /// </summary>
        public int GetNpcPurchaseLimitPerCycle(VillageData v)
        {
            int kinds = 0;
            for (int i = 0; i < v.MerchantStock.Count; i++)
            {
                TradeStockSlotKind slotKind = v.MerchantStock[i].SlotKind;
                if (slotKind != TradeStockSlotKind.AdventurerConsignment && slotKind != TradeStockSlotKind.BasicEquipment)
                    kinds++;
            }
            int limit = Mathf.FloorToInt(kinds * NpcPurchaseStockShare);
            return Mathf.Clamp(limit, NpcPurchaseMinPerCycle, NpcPurchaseMaxPerCycle);
        }

        /// <summary>
        /// NPC가 살 수 있는 매물인지 (재고 쪽 조건만). 기본 아이템 장비만 산다 (NPC_SHOP_EQUIPMENT D119) —
        /// 칸 종류가 아니라 아이템으로 걸러 무작위 옵션 장비는 어느 칸에 있어도 빠진다.
        /// 모험가 매입품은 기본 아이템이어도 제외한다 (NPC가 판 물건을 되사지 않게 해 플레이어 몫으로 남긴다).
        /// </summary>
        public bool IsNpcPurchasableEntry(VillageData v, MerchantStockEntry entry)
        {
            if (entry.RemainingCount <= 0)
                return false;
            if (entry.Item == null || entry.Item.Table == null || entry.Item.Equipment == null)
                return false;
            if (entry.Item.Equipment.BasicItemId <= 0)
                return false;
            if (entry.Item.Table.BasePrice <= 0)
                return false;
            if (entry.SlotKind == TradeStockSlotKind.AdventurerConsignment)
                return false;
            return true;
        }

        /// <summary>NPC 구매가 = 플레이어와 같은 매물 확정가 스냅샷 (0이면 BasePrice).</summary>
        public static int GetNpcPurchasePrice(MerchantStockEntry entry)
        {
            if (entry.UnitPrice > 0)
                return entry.UnitPrice;
            return entry.Item?.Table != null ? entry.Item.Table.BasePrice : 0;
        }

        /// <summary>
        /// NPC 구매 확정. 호출자가 NPC Gold 충분·업그레이드 조건을 이미 확인했다고 가정한다.
        /// 대금 전액을 마을 Gold에 넣고, 잔량을 줄이고, 구매자를 품절 표시용으로 기록한다. 매물 인스턴스를 돌려준다.
        /// </summary>
        public ItemData? CommitNpcPurchase(VillageData v, int stockIndex, int buyerEntityId, string buyerName,
            int price)
        {
            if (stockIndex < 0 || stockIndex >= v.MerchantStock.Count)
                return null;
            MerchantStockEntry entry = v.MerchantStock[stockIndex];
            if (IsNpcPurchasableEntry(v, entry) == false)
                return null;
            if (DepositNpcGold(v.VillageId, price) == false)
                return null;

            MarkSaveDirty(v.VillageId, ARPG.Data.VillageSaveGroup.Shop | ARPG.Data.VillageSaveGroup.Resources);
            entry.RemainingCount -= 1;
            entry.NpcBuyerEntityId = buyerEntityId;
            entry.NpcBuyerName = buyerName ?? string.Empty;
            v.NpcPurchaseCount++;
            v.LastNpcTradeGameTime = AR.s.Time.CurrentGameTime;
            return entry.Item;
        }

        /// <summary>
        /// NPC 판매 대금 = 플레이어 매각과 같은 상인 매입가 × 수량. 매각 불가(BasePrice 0 또는 SellRatioBp 0)면 -1.
        /// </summary>
        public int GetNpcSellPrice(int villageId, ItemData item)
        {
            Tables.ItemTable? table = item.Table ?? AR.s.Data.GetItem(item.Id);
            if (table == null || table.BasePrice <= 0 || table.SellRatioBp <= 0)
                return -1;
            int unitPrice = GetMerchantBuyPrice(villageId, table);
            if (unitPrice < 0)
                return -1;
            return unitPrice * Mathf.Max(1, item.Quantity);
        }

        /// <summary>
        /// NPC 판매 확정. 마을 Gold가 대금보다 적으면 매입하지 않고 false (NPC가 계속 보유).
        /// 매입하면 가치가 높은 장비·스킬 계열은 모험가 매입품 슬롯에 진열될 수 있다 (D42).
        /// 플레이어 매각과 달리 마을 자원 부분 환원(ReturnResourceType)은 하지 않는다 — NPC는 Gold로만 합류 (경계 §8).
        /// </summary>
        public bool TryCommitNpcSale(int villageId, ItemData item, int price, out bool consigned)
        {
            consigned = false;
            if (price < 0)
                return false;
            if (TryWithdrawNpcGold(villageId, price) == false)
                return false;

            if (_villages.TryGetValue(villageId, out VillageData? v) == true)
                consigned = TryConsignNpcSale(v, item);
            return true;
        }

        public int GetVillageGold(int villageId)
        {
            if (_villages.TryGetValue(villageId, out VillageData? data) == false)
                return 0;
            return VillageResourceSimulation.GetAmount(data, GlobalEnum.ItemType.Gold);
        }

        /// <summary>
        /// 모험가 매입품 진열 (D42, 관리 세션 결정: 해당 회차 NPC 판매품 중 장비·스킬북 계열에서 BasePrice가 높은 순, 최대 2개).
        /// 슬롯이 남으면 추가하고, 가득 찼으면 아직 팔리지 않은 매입품 중 가장 싼 것보다 비쌀 때만 교체한다.
        /// 실제 ItemData 인스턴스(모드 유지)를 그대로 진열하므로 플레이어는 기존 구매 흐름으로 산다. 재입고 롤 때 사라진다.
        /// </summary>
        private bool TryConsignNpcSale(VillageData v, ItemData item)
        {
            if (item.Equipment == null && item.SkillBook == null && item.SkillPage == null)
                return false;
            Tables.ItemTable? table = item.Table ?? AR.s.Data.GetItem(item.Id);
            if (table == null || table.BasePrice <= 0 || table.Stackable == true)
                return false;

            int consignedCount = 0;
            int replaceIndex = -1;
            int replaceBasePrice = int.MaxValue;
            for (int i = 0; i < v.MerchantStock.Count; i++)
            {
                MerchantStockEntry existing = v.MerchantStock[i];
                if (existing.SlotKind != TradeStockSlotKind.AdventurerConsignment)
                    continue;
                consignedCount++;
                if (existing.RemainingCount <= 0 || existing.Item?.Table == null)
                    continue;
                if (existing.Item.Table.BasePrice < replaceBasePrice)
                {
                    replaceBasePrice = existing.Item.Table.BasePrice;
                    replaceIndex = i;
                }
            }

            VillageTradeProfile trade = ResolveTradeProfile(v);
            TradeItemInfo info = CreateTradeItemInfo(table);
            MerchantStockEntry entry = new MerchantStockEntry
            {
                Item = item,
                RemainingCount = 1,
                UnitPrice = ApplyItemPriceIndex(VillageTradeStockRoller.GetPlayerBuyPrice(trade, info), table, 1),
                RestockCycle = GetCurrentRestockCycle(v),
                TradeProfileId = trade.TradeProfileId,
                SlotKind = TradeStockSlotKind.AdventurerConsignment,
                PriceReason = VillageTradeStockRoller.GetPriceReason(trade, info,
                    TradeStockSlotKind.AdventurerConsignment),
            };

            if (consignedCount < MaxAdventurerConsignmentSlots)
            {
                MarkSaveDirty(v.VillageId, ARPG.Data.VillageSaveGroup.Shop);
                v.MerchantStock.Add(entry);
                Debug.Log($"[NpcAmbition] Trade Consign v={v.VillageId} item={item.Id}:{table.Name}#{item.ItemInstanceId} " +
                          $"base={table.BasePrice} price={entry.UnitPrice} slot={consignedCount + 1}/{MaxAdventurerConsignmentSlots}");
                return true;
            }

            if (replaceIndex >= 0 && table.BasePrice > replaceBasePrice)
            {
                ItemData? dropped = v.MerchantStock[replaceIndex].Item;
                MarkSaveDirty(v.VillageId, ARPG.Data.VillageSaveGroup.Shop);
                v.MerchantStock[replaceIndex] = entry;
                Debug.Log($"[NpcAmbition] Trade Consign v={v.VillageId} item={item.Id}:{table.Name}#{item.ItemInstanceId} " +
                          $"base={table.BasePrice} price={entry.UnitPrice} replaced={dropped?.Id}#{dropped?.ItemInstanceId} " +
                          $"(base {replaceBasePrice})");
                return true;
            }
            return false;
        }

        private int GetCurrentRestockCycle(VillageData v)
        {
            return VillageTradeStockRoller.GetRestockCycle(AR.s.Time.CurrentGameTime, ResolveTradeProfile(v));
        }
    }
}
