#nullable enable
using System.Collections.Generic;
using System.Text;
using ARPG.Component;
using ARPG.Data;
using ARPG.Npc;
using ARPG.Village;
using UnityEditor;
using UnityEngine;

namespace ARPG.EditorTools
{
    /// <summary>
    /// NPC 야망·모험가 경제 P1b 검증용 디버그 메뉴 (플레이 모드 전용).
    /// 거래는 P1c 활동 편입 전이라 이 메뉴(또는 execute_code에서 public static 메서드)로만 호출한다.
    /// 모든 거래 메뉴는 전후의 (마을 Gold 합 + NPC Gold 합)을 찍어 닫힌 경제 보존을 확인한다.
    /// </summary>
    public static class NpcAmbitionDebugMenu
    {
        [MenuItem("ARPG/Npc/Debug/Ambition/Trade Nearest NPC (Own Village Shop)", false, 460)]
        public static void TradeNearestNpc()
        {
            int npcId = FindNearestActiveNpc();
            if (npcId < 0)
                return;
            TradeNpc(npcId);
        }

        [MenuItem("ARPG/Npc/Debug/Ambition/Trade All NPCs (Own Village Shop)", false, 461)]
        public static void TradeAllNpcs()
        {
            long before = GetWorldGoldTotal();
            List<int> ids = new List<int>(AR.s.Npc.SaveDataView.Keys);
            ids.Sort();
            int traded = 0;
            int sold = 0;
            int rejected = 0;
            int bought = 0;
            for (int i = 0; i < ids.Count; i++)
            {
                NpcSaveData saveData = AR.s.Npc.SaveDataView[ids[i]];
                if (saveData.VillageId < 0)
                    continue;
                NpcTradeReport report = AR.s.Npc.TradeAtShop(ids[i], saveData.VillageId);
                if (report.ShopAvailable == false)
                    continue;
                traded++;
                sold += report.SoldCount;
                rejected += report.SellRejectedCount;
                if (report.Bought == true)
                    bought++;
            }
            long after = GetWorldGoldTotal();
            Debug.Log($"[NpcAmbitionDebug] Trade All — traded={traded} sold={sold} sellRejected={rejected} bought={bought} " +
                      $"worldGold={before}->{after} conserved={before == after}");
        }

        [MenuItem("ARPG/Npc/Debug/Ambition/Log Nearest NPC Economy", false, 462)]
        public static void LogNearestNpcEconomy()
        {
            int npcId = FindNearestActiveNpc();
            if (npcId < 0)
                return;
            LogNpcEconomy(npcId);
        }

        [MenuItem("ARPG/Npc/Debug/Ambition/Log Nearest NPC Village Shop", false, 463)]
        public static void LogNearestVillageShop()
        {
            int npcId = FindNearestActiveNpc();
            if (npcId < 0)
                return;
            NpcSaveData? saveData = AR.s.Npc.GetSaveData(npcId);
            if (saveData != null)
                LogVillageShop(saveData.VillageId);
        }

        [MenuItem("ARPG/Npc/Debug/Ambition/Force Wound + Treatment (First Offscreen Idle NPC)", false, 464)]
        public static void ForceWoundFirstOffscreenNpc()
        {
            List<int> ids = new List<int>(AR.s.Npc.SaveDataView.Keys);
            ids.Sort();
            for (int i = 0; i < ids.Count; i++)
            {
                NpcSaveData saveData = AR.s.Npc.SaveDataView[ids[i]];
                if (saveData.IsActive == true || saveData.IsSpawning == true || saveData.VillageId < 0)
                    continue;
                if (saveData.Activity.Phase != NpcActivityPhase.Idle)
                    continue;
                ForceWound(ids[i]);
                return;
            }
            Debug.LogWarning("[NpcAmbitionDebug] 오프스크린 Idle NPC 없음");
        }

        [MenuItem("ARPG/Npc/Debug/Ambition/Log World Gold Total", false, 465)]
        public static void LogWorldGoldTotal()
        {
            Debug.Log($"[NpcAmbitionDebug] worldGold(village+npc)={GetWorldGoldTotal()}");
        }

        [MenuItem("ARPG/Npc/Debug/Ambition/Trade Nearest NPC (Own Village Shop)", true)]
        [MenuItem("ARPG/Npc/Debug/Ambition/Trade All NPCs (Own Village Shop)", true)]
        [MenuItem("ARPG/Npc/Debug/Ambition/Log Nearest NPC Economy", true)]
        [MenuItem("ARPG/Npc/Debug/Ambition/Log Nearest NPC Village Shop", true)]
        [MenuItem("ARPG/Npc/Debug/Ambition/Force Wound + Treatment (First Offscreen Idle NPC)", true)]
        [MenuItem("ARPG/Npc/Debug/Ambition/Log World Gold Total", true)]
        private static bool ValidatePlayMode()
        {
            return Application.isPlaying;
        }

        /// <summary>특정 NPC를 소속 마을 상점에서 1회 거래시킨다. 전후 경제 상태를 로그로 남긴다.</summary>
        public static NpcTradeReport TradeNpc(int npcId)
        {
            NpcSaveData? saveData = AR.s.Npc.GetSaveData(npcId);
            if (saveData == null)
            {
                Debug.LogWarning($"[NpcAmbitionDebug] NpcSaveData 없음 - EntityId: {npcId}");
                return default;
            }

            long before = GetWorldGoldTotal();
            NpcTradeReport report = AR.s.Npc.TradeAtShop(npcId, saveData.VillageId);
            long after = GetWorldGoldTotal();
            Debug.Log($"[NpcAmbitionDebug] Trade {AR.s.Npc.GetDisplayName(npcId)} (EntityId: {npcId}) v={saveData.VillageId} " +
                      $"shop={report.ShopAvailable} sold={report.SoldCount}(+{report.SoldGold}G) " +
                      $"sellRejected={report.SellRejectedCount} keptUnsellable={report.KeptUnsellableCount} " +
                      $"consigned={report.ConsignedCount} bought={report.Bought}(item {report.BoughtItemId}, " +
                      $"{report.BoughtPrice}G, deferred {report.BuyDeferred}) skip={report.BuySkipReason} " +
                      $"worldGold={before}->{after} conserved={before == after}");
            LogNpcEconomy(npcId);
            return report;
        }

        /// <summary>
        /// 오프스크린 NPC를 지금 부상 상태로 만들고 실제 솔로 Hunt 부상과 같은 중상·치료 판정·기록(D91)을 태운다.
        /// RNG 시드는 현재 게임시각이다 (같은 시각에 두 번 부르면 같은 결과).
        /// </summary>
        public static void ForceWound(int npcId)
        {
            AR.s.Npc.MarkSaveDirty(npcId);
            NpcSaveData? saveData = AR.s.Npc.GetSaveData(npcId);
            if (saveData == null)
                return;

            float now = AR.s.Time.CurrentGameTime;
            long before = GetWorldGoldTotal();
            saveData.Activity.PhaseStartedGameTime = now;
            float hours = AR.s.Npc.ApplyExpeditionWound(npcId, saveData, NpcActivityKind.Hunt, npcId, now,
                AR.s.Map.mapSeed, NpcActivityResolver.TreatmentSalt, NpcActivityResolver.SevereWoundHuntSalt, now,
                out bool treated, out bool severe);
            long after = GetWorldGoldTotal();
            Debug.Log($"[NpcAmbitionDebug] ForceWound {AR.s.Npc.GetDisplayName(npcId)} (EntityId: {npcId}) " +
                      $"severe={severe} treated={treated} recovery={hours:F0}h woundCount={saveData.WoundCount} " +
                      $"gold={saveData.Gold} " +
                      $"worldGold={before}->{after} conserved={before == after}");
        }

        public static void LogNpcEconomy(int npcId)
        {
            NpcSaveData? saveData = AR.s.Npc.GetSaveData(npcId);
            if (saveData == null)
                return;

            float now = AR.s.Time.CurrentGameTime;
            float cooldown = saveData.LastTradeGameTime >= 0f
                ? Mathf.Max(0f, NpcManager.TradeCooldownHours - (now - saveData.LastTradeGameTime))
                : 0f;
            StringBuilder builder = new StringBuilder();
            builder.Append($"[NpcAmbitionDebug] {AR.s.Npc.GetDisplayName(npcId)} (EntityId: {npcId}) v={saveData.VillageId} ")
                .Append($"gold={saveData.Gold} power={AR.s.Npc.GetNpcPower(npcId):F2} ")
                .Append($"cap={NpcManager.GetNpcPowerCap(saveData):F2} initial={saveData.InitialPower:F2} ")
                .Append($"lastTrade={saveData.LastTradeGameTime:F2} cooldownRemain={cooldown:F1}h ")
                .Append($"phase={saveData.Activity.Phase} active={saveData.IsActive}");
            for (int i = 0; i < saveData.Inventory.Count; i++)
            {
                ItemData? item = saveData.Inventory[i];
                builder.Append($"\n  inv[{i}] ").Append(item == null ? "(빈 칸)" : DescribeItem(item));
            }
            for (int i = 0; i < saveData.InventoryEquip.Length; i++)
            {
                ItemData? item = saveData.InventoryEquip[i];
                if (item != null)
                    builder.Append($"\n  equip[{(GlobalEnum.EquipSlotType)i}] ").Append(DescribeItem(item));
            }
            Debug.Log(builder.ToString());
        }

        public static void LogVillageShop(int villageId)
        {
            VillageData? v = AR.s.Village.GetVillage(villageId);
            if (v == null)
            {
                Debug.LogWarning($"[NpcAmbitionDebug] 마을 없음 - VillageId: {villageId}");
                return;
            }

            float now = AR.s.Time.CurrentGameTime;
            StringBuilder builder = new StringBuilder();
            builder.Append($"[NpcAmbitionDebug] v{villageId} gold={AR.s.Village.GetVillageGold(villageId)} ")
                .Append($"shops={AR.s.Village.CountByService(villageId, ProvidedService.Shop)} ")
                .Append($"restockCycle={v.MerchantRestockCycle} npcPurchase={v.NpcPurchaseCount}/")
                .Append($"{AR.s.Village.GetNpcPurchaseLimitPerCycle(v)} (cycle {v.NpcPurchaseCycle}) ")
                .Append($"lastNpcTrade={v.LastNpcTradeGameTime:F2} now={now:F2}");
            for (int i = 0; i < v.MerchantStock.Count; i++)
            {
                MerchantStockEntry entry = v.MerchantStock[i];
                string item = entry.Item != null ? DescribeItem(entry.Item) : "(null)";
                builder.Append($"\n  [{i}] {item} x{entry.RemainingCount} {entry.UnitPrice}G {entry.SlotKind}");
                if (entry.Item?.Equipment != null)
                    builder.Append($" mods={entry.Item.Equipment.Mods.Count} basic={entry.Item.Equipment.BasicItemId}");
                if (entry.NpcBuyerEntityId >= 0)
                    builder.Append($" boughtBy={entry.NpcBuyerName}({entry.NpcBuyerEntityId})");
            }
            Debug.Log(builder.ToString());
        }

        /// <summary>닫힌 경제 검증용 — 모든 마을 Gold + 모든 NPC Gold.</summary>
        public static long GetWorldGoldTotal()
        {
            long total = 0;
            IReadOnlyList<VillageData> villages = AR.s.Village.GetAllVillages();
            for (int i = 0; i < villages.Count; i++)
                total += VillageResourceSimulation.GetAmount(villages[i], GlobalEnum.ItemType.Gold);
            List<int> ids = new List<int>(AR.s.Npc.SaveDataView.Keys);
            for (int i = 0; i < ids.Count; i++)
                total += AR.s.Npc.SaveDataView[ids[i]].Gold;
            return total;
        }

        private static string DescribeItem(ItemData item)
        {
            string name = item.Table != null ? item.Table.Name : $"Item {item.Id}";
            int basePrice = item.Table != null ? item.Table.BasePrice : 0;
            return $"{item.Id}:{name}#{item.ItemInstanceId} base={basePrice}";
        }

        private static int FindNearestActiveNpc()
        {
            if (Application.isPlaying == false)
            {
                Debug.LogWarning("[NpcAmbitionDebug] 플레이 모드에서만 동작합니다.");
                return -1;
            }

            ComponentManager cm = AR.s.Component;
            int playerId = AR.s.Data.CurrentPlayerEntityId;
            if (cm.TryGetComponent<TransformComponent>(playerId, out TransformComponent playerTransform) == false)
                return -1;

            SparseSet<NpcTag> npcPool = cm.GetComponentPool<NpcTag>();
            int bestId = -1;
            float bestSqrDistance = float.MaxValue;
            for (int i = 0; i < npcPool.Count; i++)
            {
                int npcId = npcPool.GetEntityId(i);
                if (cm.HasComponent<DestroyTag>(npcId) == true)
                    continue;
                if (cm.TryGetComponent<TransformComponent>(npcId, out TransformComponent npcTransform) == false)
                    continue;

                float sqrDistance = (npcTransform.Position - playerTransform.Position).sqrMagnitude;
                if (sqrDistance < bestSqrDistance)
                {
                    bestSqrDistance = sqrDistance;
                    bestId = npcId;
                }
            }

            if (bestId < 0)
                Debug.LogWarning("[NpcAmbitionDebug] 활성 NPC 없음");
            return bestId;
        }
    }
}
