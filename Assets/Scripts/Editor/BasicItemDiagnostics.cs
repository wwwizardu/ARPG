#nullable enable
using System;
using System.Collections.Generic;
using System.Text;
using ARPG.Data;
using ARPG.Item;
using ARPG.Npc;
using ARPG.Tables;
using ARPG.Village;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using GE = GlobalEnum;

namespace ARPG.EditorTools
{
    /// <summary>
    /// 상점 기본 아이템 검증 (NPC_SHOP_EQUIPMENT_DESIGN.md §6.1·§9). 플레이 모드 GameScene 로드 후 전용.
    /// 메모리에서 만든 아이템만 직렬화·역직렬화하며 라이브 세이브·NPC·마을 상태는 바꾸지 않는다
    /// (견본 생성으로 아이템 인스턴스 번호 카운터만 앞으로 간다).
    /// </summary>
    public static class BasicItemDiagnostics
    {
        [MenuItem("ARPG/Diagnostics/Basic Items/Verify Save Representation", false, 600)]
        private static void RunSaveRepresentationMenu()
        {
            string report = VerifySaveRepresentation(out int failures);
            if (failures == 0)
                Debug.Log(report);
            else
                Debug.LogError(report);
        }

        /// <summary>
        /// 기본 아이템 저장·로드 왕복(옵션·전투력·무기 능력 동일), 일반 장비 왕복, 옵션 변경 안전망,
        /// 마법부여 해제, 구 세이브 키 무시, 행 삭제 대체, 저장 크기 비교를 검사한다.
        /// </summary>
        public static string VerifySaveRepresentation(out int failures)
        {
            StringBuilder log = new StringBuilder();
            int failCount = 0;
            void Check(bool condition, string label)
            {
                if (condition == false)
                    failCount++;
                log.AppendLine((condition ? "  PASS " : "  FAIL ") + label);
            }

            log.AppendLine("[BasicItemDiag] Save representation");
            if (EditorApplication.isPlaying == false || AR.s == null || AR.s.Data == null || AR.s.Item == null)
            {
                failures = 1;
                return "[BasicItemDiag] 플레이 모드(GameScene 로드 후)에서만 실행할 수 있습니다.";
            }

            // A. 모든 유효 행: 생성 → JSON·스냅샷 생략 → 로드 재구성 동일
            int rowCount = 0;
            int roundTripFailures = 0;
            int omitFailures = 0;
            int snapshotFailures = 0;
            long basicBytes = 0;
            List<int> itemIds = CollectBasicItemIds();
            for (int i = 0; i < itemIds.Count; i++)
            {
                IReadOnlyList<BasicItemTable> variants = AR.s.Data.GetBasicItemVariants(itemIds[i]);
                for (int v = 0; v < variants.Count; v++)
                {
                    BasicItemTable row = variants[v];
                    rowCount++;
                    ItemData? item = AR.s.Item.CreateBasicItem(row.Id);
                    if (item?.Equipment == null)
                    {
                        roundTripFailures++;
                        log.AppendLine($"    생성 실패 BasicItemId={row.Id}");
                        continue;
                    }

                    string json = JsonConvert.SerializeObject(item, Formatting.None, JsonSettings.Default);
                    basicBytes += Encoding.UTF8.GetByteCount(json);
                    if (item.Equipment.IsIntactBasicItem() == false || json.Contains("\"Mods\"") == true ||
                        json.Contains("\"Quality\"") == true || json.Contains("\"WeaponStats\"") == true ||
                        json.Contains("\"EquipType\"") == true || json.Contains("\"BasicItemId\":" + row.Id) == false)
                    {
                        omitFailures++;
                        if (omitFailures <= 3)
                            log.AppendLine($"    생략 실패 BasicItemId={row.Id} json={json}");
                    }

                    if (CheckSnapshotOmitted(item, row.Id) == false)
                        snapshotFailures++;

                    ItemData? loaded = RoundTrip(json);
                    string reJson = loaded != null
                        ? JsonConvert.SerializeObject(loaded, Formatting.None, JsonSettings.Default)
                        : string.Empty;
                    if (loaded?.Equipment == null || SameEquipment(item, loaded) == false || reJson != json ||
                        loaded.Equipment.BasicItemId != row.Id || loaded.Equipment.Quality != EquipmentData.BasicItemQuality)
                    {
                        roundTripFailures++;
                        if (roundTripFailures <= 3)
                            log.AppendLine($"    왕복 실패 BasicItemId={row.Id} before={DescribeMods(item)} " +
                                           $"after={(loaded != null ? DescribeMods(loaded) : "null")}");
                    }
                }
            }
            Check(rowCount > 0, $"유효 기본 아이템 행 존재 (rows={rowCount}, items={itemIds.Count})");
            Check(omitFailures == 0, $"기본 아이템 JSON에서 Mods·Quality·WeaponStats·EquipType 생략, BasicItemId 저장 (실패 {omitFailures})");
            Check(snapshotFailures == 0, $"저장 복사기(SaveDtoSnapshot)도 같은 필드 생략 (실패 {snapshotFailures})");
            Check(roundTripFailures == 0, $"저장·로드 왕복 뒤 옵션·무기 능력·전투력·JSON 동일 (실패 {roundTripFailures})");

            // B. 일반(무작위 옵션) 장비: 옵션 전체 저장, 계산 캐시 생략, 왕복 동일
            int regularFailures = 0;
            long regularBytes = 0;
            int regularCount = 0;
            for (int i = 0; i < itemIds.Count; i++)
            {
                ItemData? regular = AR.s.Item.CreateInventoryItemData(itemIds[i]);
                if (regular?.Equipment == null)
                    continue;
                regularCount++;
                string json = JsonConvert.SerializeObject(regular, Formatting.None, JsonSettings.Default);
                regularBytes += Encoding.UTF8.GetByteCount(json);
                ItemData? loaded = RoundTrip(json);
                if (json.Contains("\"Mods\"") == false || json.Contains("\"Quality\"") == false ||
                    json.Contains("\"BasicItemId\"") == true || json.Contains("\"WeaponStats\"") == true ||
                    json.Contains("\"EquipType\"") == true || loaded == null || SameEquipment(regular, loaded) == false ||
                    loaded.Equipment!.Quality != regular.Equipment.Quality)
                {
                    regularFailures++;
                    if (regularFailures <= 3)
                        log.AppendLine($"    일반 장비 실패 ItemId={itemIds[i]} json={json}");
                }
            }
            Check(regularCount > 0 && regularFailures == 0,
                $"일반 장비는 옵션 전체 저장·계산 캐시 생략·왕복 동일 (장비 {regularCount}, 실패 {regularFailures})");

            ItemData? sample = CreateSampleBasicItem();
            if (sample?.Equipment != null)
            {
                // C. 안전망: BasicItemId를 지우지 않고 옵션을 바꾸면 전체 옵션이 저장되고 로드에서 일반 아이템이 된다.
                ItemData mutated = AR.s.Item.CreateBasicItem(sample.Equipment.BasicItemId)!;
                ModInstance last = mutated.Equipment!.Mods[mutated.Equipment.Mods.Count - 1];
                last.Value1 = (ushort)(last.Value1 + 1);
                mutated.Equipment.InvalidateWeaponStats();
                string mutatedJson = JsonConvert.SerializeObject(mutated, Formatting.None, JsonSettings.Default);
                ItemData? mutatedLoaded = RoundTrip(mutatedJson);
                Check(mutated.Equipment.IsIntactBasicItem() == false && mutatedJson.Contains("\"Mods\"") == true &&
                      mutatedJson.Contains("\"BasicItemId\"") == false && mutatedLoaded?.Equipment != null &&
                      mutatedLoaded.Equipment.BasicItemId == 0 && SameEquipment(mutated, mutatedLoaded) == true,
                    "안전망: 행과 다른 옵션은 전체 저장, 로드 뒤 일반 아이템");

                // D. 마법부여 커밋은 옵션을 바꾸기 전에 BasicItemId를 지운다.
                Check(VerifyEnchantmentClearsBasicItem(sample.Equipment.BasicItemId, log) == true,
                    "마법부여한 기본 아이템은 BasicItemId 해제 + 전체 옵션 저장·왕복 동일");

                // E. 구 세이브의 EquipType·WeaponStats 키는 무시되고 다시 계산된다.
                ItemData? regular = AR.s.Item.CreateInventoryItemData(sample.Id);
                if (regular?.Equipment != null)
                {
                    string legacyJson = JsonConvert.SerializeObject(regular, Formatting.None, JsonSettings.Default)
                        .Replace("\"Equipment\":{", "\"Equipment\":{\"EquipType\":\"Ring\",\"WeaponStats\":{\"CriRate\":999},");
                    ItemData? legacyLoaded = RoundTrip(legacyJson);
                    Check(legacyLoaded?.Equipment != null && legacyLoaded.Equipment.EquipType == regular.Equipment.EquipType &&
                          legacyLoaded.Equipment.WeaponStats.CriRate == regular.Equipment.WeaponStats.CriRate,
                        "구 세이브의 EquipType·WeaponStats 저장값 무시 + 재계산");
                }

                // F. 행이 사라진 기본 아이템은 같은 장비의 첫 변형으로 대체된다.
                string missingJson = JsonConvert.SerializeObject(sample, Formatting.None, JsonSettings.Default)
                    .Replace("\"BasicItemId\":" + sample.Equipment.BasicItemId, "\"BasicItemId\":987654321");
                ItemData? missingLoaded = RoundTrip(missingJson);
                BasicItemTable first = AR.s.Data.GetBasicItemVariants(sample.Id)[0];
                ItemData firstItem = AR.s.Item.CreateBasicItem(first.Id)!;
                Check(missingLoaded?.Equipment != null && missingLoaded.Equipment.BasicItemId == first.Id &&
                      SameEquipment(missingLoaded, firstItem) == true,
                    $"행 삭제 시 같은 장비 첫 변형({first.Id})으로 대체");

                // G. 이미 로드된 기본 아이템에 OnLoadCompleted를 다시 불러도 그대로다 (진단 복제·재로드 경로).
                ItemData again = AR.s.Item.CreateBasicItem(sample.Equipment.BasicItemId)!;
                string beforeAgain = DescribeMods(again);
                again.OnLoadCompleted();
                again.OnLoadCompleted();
                Check(again.Equipment!.BasicItemId == sample.Equipment.BasicItemId && DescribeMods(again) == beforeAgain,
                    "OnLoadCompleted 반복 호출에도 기본 아이템 유지");
            }
            else
            {
                Check(false, "견본 기본 아이템 생성");
            }

            VerifyDefaultOmission(log, Check);

            log.AppendLine($"  INFO 평균 JSON 크기: 기본 아이템 {(rowCount > 0 ? basicBytes / rowCount : 0)}B, " +
                           $"일반 장비 {(regularCount > 0 ? regularBytes / regularCount : 0)}B");
            log.AppendLine(failCount == 0 ? "[BasicItemDiag] RESULT PASS" : $"[BasicItemDiag] RESULT FAIL ({failCount})");
            failures = failCount;
            return log.ToString();
        }

        [MenuItem("ARPG/Diagnostics/Basic Items/Verify Live State (Shop · NPC)", false, 601)]
        private static void RunLiveStateMenu()
        {
            string report = VerifyLiveState(null, out int failures);
            if (failures == 0)
                Debug.Log(report);
            else
                Debug.LogError(report);
        }

        /// <summary>
        /// 라이브 상태 검증 (§9 필수 검증). 라이브 데이터는 읽기만 하고, 상점 재롤은 마을 복제본에서 한다.
        /// - NPC 장착 장비가 모두 온전한 기본 아이템인지
        /// - preMigrationSlotDir(마이그레이션 전 세이브의 Slot 폴더)가 있으면 슬롯별 ItemInstanceId·ItemId·PowerCapBonus 유지,
        ///   장비가 그대로인 NPC의 InitialPower = 현재 전투력
        /// - 일괄 교체 함수 단위 검사 (무작위 옵션 장비 → 기본 아이템, 인스턴스 번호·Gold·편차 유지)
        /// - 상점 재롤(복제본): 기본 장비 칸 수·기본 아이템·중복 없음·Tier 게이트, 기존 칸 무작위 옵션, 같은 회차 결정론,
        ///   이번 회차를 옛 코드로 롤한 라이브 재고와 기존 칸 결과 동일
        /// - NPC 구매 후보 필터·회차 한도
        /// </summary>
        public static string VerifyLiveState(string? preMigrationSlotDir, out int failures)
        {
            StringBuilder log = new StringBuilder();
            int failCount = 0;
            void Check(bool condition, string label)
            {
                if (condition == false)
                    failCount++;
                log.AppendLine((condition ? "  PASS " : "  FAIL ") + label);
            }

            log.AppendLine("[BasicItemDiag] Live state");
            if (EditorApplication.isPlaying == false || AR.s == null || AR.s.Npc == null || AR.s.Village == null)
            {
                failures = 1;
                return "[BasicItemDiag] 플레이 모드(GameScene 로드 후)에서만 실행할 수 있습니다.";
            }

            // 1. NPC 장착 장비 전수
            int equippedCount = 0, nonBasicEquipped = 0, livingNpcs = 0;
            foreach (KeyValuePair<int, NpcSaveData> pair in AR.s.Npc.SaveDataView)
            {
                NpcSaveData npc = pair.Value;
                if (npc.Condition != ARPG.Creature.CharacterConditions.Dead)
                    livingNpcs++;
                for (int s = 0; s < npc.InventoryEquip.Length; s++)
                {
                    ItemData? item = npc.InventoryEquip[s];
                    if (item?.Equipment == null)
                        continue;
                    equippedCount++;
                    if (item.Equipment.IsIntactBasicItem() == false)
                    {
                        nonBasicEquipped++;
                        if (nonBasicEquipped <= 3)
                            log.AppendLine($"    기본 아이템 아님 npc={pair.Key} slot={(GE.EquipSlotType)s} item={item.Id}#{item.ItemInstanceId}");
                    }
                }
            }
            Check(equippedCount > 0 && nonBasicEquipped == 0,
                $"NPC 장착 장비 전부 기본 아이템 (NPC {AR.s.Npc.SaveDataView.Count}, 생존 {livingNpcs}, 장착 {equippedCount}, 아님 {nonBasicEquipped})");

            // 2. 마이그레이션 전 세이브와 대조
            if (string.IsNullOrEmpty(preMigrationSlotDir) == false)
                VerifyAgainstPreMigrationSave(preMigrationSlotDir!, log, Check);

            // 3. 일괄 교체 함수 단위 검사
            VerifyReplacementFunction(log, Check);

            // 4. 상점 재롤 (복제본)
            VerifyShopRolls(log, Check);

            // 5. NPC 구매 필터
            VerifyPurchaseFilter(log, Check);

            log.AppendLine(failCount == 0 ? "[BasicItemDiag] RESULT PASS" : $"[BasicItemDiag] RESULT FAIL ({failCount})");
            failures = failCount;
            return log.ToString();
        }

        private static void VerifyAgainstPreMigrationSave(string slotDir, StringBuilder log, Action<bool, string> check)
        {
            string manifestPath = System.IO.Path.Combine(slotDir, "manifest.json");
            if (System.IO.File.Exists(manifestPath) == false)
            {
                check(false, $"마이그레이션 전 세이브 manifest 없음: {manifestPath}");
                return;
            }

            Newtonsoft.Json.Linq.JObject manifest = Newtonsoft.Json.Linq.JObject.Parse(System.IO.File.ReadAllText(manifestPath));
            Newtonsoft.Json.Linq.JObject? parts = manifest["Parts"] as Newtonsoft.Json.Linq.JObject;
            int compared = 0, instanceMismatch = 0, missing = 0, capBonusMismatch = 0;
            int powerChecked = 0, initialPowerMismatch = 0, preNonBasic = 0;
            if (parts != null)
            {
                foreach (KeyValuePair<string, Newtonsoft.Json.Linq.JToken?> part in parts)
                {
                    if (part.Key.StartsWith("npc_state_", StringComparison.Ordinal) == false)
                        continue;
                    string fileName = part.Value?["FileName"]?.Value<string>() ?? string.Empty;
                    string partPath = System.IO.Path.Combine(slotDir, "parts", fileName);
                    if (System.IO.File.Exists(partPath) == false)
                        continue;
                    Newtonsoft.Json.Linq.JObject shard = Newtonsoft.Json.Linq.JObject.Parse(System.IO.File.ReadAllText(partPath));
                    if (shard["Npcs"] is Newtonsoft.Json.Linq.JObject npcs == false)
                        continue;
                    foreach (KeyValuePair<string, Newtonsoft.Json.Linq.JToken?> npcToken in npcs)
                    {
                        int entityId = int.Parse(npcToken.Key);
                        if (AR.s.Npc.SaveDataView.TryGetValue(entityId, out NpcSaveData? live) == false)
                        {
                            missing++;
                            continue;
                        }
                        float preCapBonus = npcToken.Value?["PowerCapBonus"]?.Value<float>() ?? 0f;
                        if (Mathf.Abs(preCapBonus - live.PowerCapBonus) > 0.0001f)
                            capBonusMismatch++;

                        Newtonsoft.Json.Linq.JArray? equip = npcToken.Value?["InventoryEquip"] as Newtonsoft.Json.Linq.JArray;
                        bool sameEquipment = true;
                        for (int s = 0; equip != null && s < equip.Count && s < live.InventoryEquip.Length; s++)
                        {
                            Newtonsoft.Json.Linq.JToken token = equip[s];
                            if (token == null || token.Type == Newtonsoft.Json.Linq.JTokenType.Null)
                                continue;
                            compared++;
                            int preInstance = token["ItemInstanceId"]?.Value<int>() ?? 0;
                            int preItemId = token["Id"]?.Value<int>() ?? 0;
                            if ((token["Equipment"]?["BasicItemId"]?.Value<int>() ?? 0) <= 0)
                                preNonBasic++;
                            ItemData? now = live.InventoryEquip[s];
                            if (now == null || now.ItemInstanceId != preInstance || now.Id != preItemId)
                            {
                                instanceMismatch++;
                                sameEquipment = false;
                            }
                        }

                        float preInitialPower = npcToken.Value?["InitialPower"]?.Value<float>() ?? 0f;
                        if (sameEquipment == true && preInitialPower > 0f && live.Condition != ARPG.Creature.CharacterConditions.Dead)
                        {
                            powerChecked++;
                            float expected = Mathf.Max(NpcPowerEvaluator.Evaluate(live), 0.01f);
                            if (Mathf.Abs(expected - live.InitialPower) > 0.001f)
                                initialPowerMismatch++;
                        }
                    }
                }
            }

            log.AppendLine($"  INFO 마이그레이션 전 세이브 장착 장비 {compared}개 중 기본 아이템 아님 {preNonBasic}");
            // 로드 뒤 진행된 구매 업그레이드로 바뀐 슬롯은 불일치로 세므로, 소수는 정상이다 (구매 건수와 대조).
            check(compared > 0 && missing == 0 && instanceMismatch * 200 <= compared,
                $"일괄 교체 전후 슬롯별 ItemInstanceId·ItemId 유지 (비교 {compared}, 불일치 {instanceMismatch} — 로드 뒤 구매 교체분, 없어진 NPC {missing})");
            check(capBonusMismatch == 0, $"개인 상한 편차(PowerCapBonus) 유지 (불일치 {capBonusMismatch})");
            check(powerChecked > 0 && initialPowerMismatch == 0,
                $"장비가 그대로인 NPC의 InitialPower = 교체 뒤 전투력 (검사 {powerChecked}, 불일치 {initialPowerMismatch})");
        }

        private static void VerifyReplacementFunction(StringBuilder log, Action<bool, string> check)
        {
            NpcSaveData? source = null;
            foreach (KeyValuePair<int, NpcSaveData> pair in AR.s.Npc.SaveDataView)
            {
                if (pair.Value.Condition == ARPG.Creature.CharacterConditions.Dead)
                    continue;
                for (int s = 0; s < pair.Value.InventoryEquip.Length && source == null; s++)
                {
                    if (pair.Value.InventoryEquip[s]?.Equipment != null)
                        source = pair.Value;
                }
                if (source != null)
                    break;
            }
            if (source == null)
            {
                check(false, "일괄 교체 단위 검사: 장비를 가진 NPC 없음");
                return;
            }

            NpcSaveData clone = JsonConvert.DeserializeObject<NpcSaveData>(
                JsonConvert.SerializeObject(source, JsonSettings.Default), JsonSettings.Default)!;
            clone.OnLoadCompleted();
            List<int> instanceIds = new List<int>();
            List<int> itemIds = new List<int>();
            for (int s = 0; s < clone.InventoryEquip.Length; s++)
            {
                ItemData? item = clone.InventoryEquip[s];
                if (item == null)
                    continue;
                // 옛 생성 규칙(무작위 옵션)으로 되돌린 장비. 인스턴스 번호는 원래 것을 쓴다.
                ItemData legacy = AR.s.Item.CreateInventoryItemData(item.Id)!;
                legacy.ItemInstanceId = item.ItemInstanceId;
                clone.InventoryEquip[s] = legacy;
                instanceIds.Add(legacy.ItemInstanceId);
                itemIds.Add(legacy.Id);
            }
            const int fakeEntityId = -987654;
            clone.InitialPower = 999f;
            clone.Gold = 77;
            clone.PowerCapBonus = 0.37f;
            string inventoryBefore = JsonConvert.SerializeObject(clone.Inventory, JsonSettings.Default);

            System.Reflection.MethodInfo? method = typeof(NpcManager).GetMethod("ReplaceEquipmentWithBasicItems",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            object[] args = { fakeEntityId, clone, 0 };
            int replaced = method != null ? (int)method.Invoke(AR.s.Npc, args)! : -1;
            AR.s.Npc.InvalidateNpcPower(fakeEntityId);

            bool idsKept = true;
            bool allBasic = true;
            int index = 0;
            for (int s = 0; s < clone.InventoryEquip.Length; s++)
            {
                ItemData? item = clone.InventoryEquip[s];
                if (item == null)
                    continue;
                idsKept &= index < instanceIds.Count && item.ItemInstanceId == instanceIds[index] && item.Id == itemIds[index];
                allBasic &= item.Equipment != null && item.Equipment.IsIntactBasicItem() == true;
                index++;
            }
            float power = NpcPowerEvaluator.Evaluate(clone);
            check(method != null && replaced == instanceIds.Count && idsKept == true && allBasic == true &&
                  Mathf.Abs(clone.InitialPower - Mathf.Max(power, 0.01f)) <= 0.0001f && clone.Gold == 77 &&
                  Mathf.Abs(clone.PowerCapBonus - 0.37f) <= 0.0001f &&
                  JsonConvert.SerializeObject(clone.Inventory, JsonSettings.Default) == inventoryBefore,
                $"일괄 교체 함수: 무작위 옵션 장비 {instanceIds.Count}개 → 기본 아이템, 인스턴스·ItemId 유지, " +
                $"InitialPower만 재계산({clone.InitialPower:F2}), Gold·편차·가방 그대로");
        }

        private static void VerifyShopRolls(StringBuilder log, Action<bool, string> check)
        {
            System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
            System.Reflection.MethodInfo? roll = typeof(VillageManager).GetMethod("RollMerchantStock", flags);
            System.Reflection.MethodInfo? resolve = typeof(VillageManager).GetMethod("ResolveTradeProfile", flags);
            if (roll == null || resolve == null)
            {
                check(false, "상점 검사: RollMerchantStock/ResolveTradeProfile을 찾지 못함");
                return;
            }

            int shops = 0, slotCountFailures = 0, nonBasicInBasic = 0, duplicateInBasic = 0, tierFailures = 0;
            int basicOutsideBasic = 0, determinismFailures = 0, comparedLegacy = 0, legacyMismatch = 0, limitMismatch = 0;
            float now = AR.s.Time.CurrentGameTime;
            IReadOnlyList<VillageData> villages = AR.s.Village.GetAllVillages();
            bool logEnabled = Debug.unityLogger.logEnabled;
            Debug.unityLogger.logEnabled = false;   // 재롤 로그(마을당 1줄)를 막는다
            try
            {
                for (int i = 0; i < villages.Count; i++)
                {
                    VillageData live = villages[i];
                    if (AR.s.Village.CountByService(live.VillageId, ProvidedService.Shop) <= 0)
                        continue;
                    shops++;
                    VillageTradeProfile trade = (VillageTradeProfile)resolve.Invoke(AR.s.Village, new object[] { live })!;
                    int cycle = VillageTradeStockRoller.GetRestockCycle(now, trade);

                    VillageData first = CloneVillage(live);
                    roll.Invoke(AR.s.Village, new object[] { first, trade, cycle });
                    VillageData second = CloneVillage(live);
                    roll.Invoke(AR.s.Village, new object[] { second, trade, cycle });
                    if (DescribeStock(first.MerchantStock, false) != DescribeStock(second.MerchantStock, false))
                        determinismFailures++;

                    int basicCount = 0;
                    int maxTier = trade.GetMaxItemTier(live.ShopTier);
                    HashSet<int> basicItemIds = new HashSet<int>();
                    for (int s = 0; s < first.MerchantStock.Count; s++)
                    {
                        MerchantStockEntry entry = first.MerchantStock[s];
                        if (entry.SlotKind == TradeStockSlotKind.BasicEquipment)
                        {
                            basicCount++;
                            if (entry.Item?.Equipment == null || entry.Item.Equipment.IsIntactBasicItem() == false)
                                nonBasicInBasic++;
                            if (entry.Item != null && basicItemIds.Add(entry.Item.Id) == false)
                                duplicateInBasic++;
                            if (entry.Item?.Table == null || entry.Item.Table.Tier > maxTier)
                                tierFailures++;
                        }
                        else if (entry.Item?.Equipment != null && entry.Item.Equipment.BasicItemId > 0)
                        {
                            basicOutsideBasic++;
                        }
                    }
                    if (basicCount != trade.GetBasicEquipmentSlotCount(live.ShopTier))
                        slotCountFailures++;

                    int kinds = 0;
                    for (int s = 0; s < first.MerchantStock.Count; s++)
                    {
                        if (first.MerchantStock[s].SlotKind != TradeStockSlotKind.BasicEquipment)
                            kinds++;
                    }
                    int expectedLimit = Mathf.Clamp(Mathf.FloorToInt(kinds * VillageManager.NpcPurchaseStockShare),
                        VillageManager.NpcPurchaseMinPerCycle, VillageManager.NpcPurchaseMaxPerCycle);
                    if (AR.s.Village.GetNpcPurchaseLimitPerCycle(first) != expectedLimit)
                        limitMismatch++;

                    // 이번 회차를 옛 코드(기본 장비 칸 없음)로 롤한 라이브 재고가 있으면 기존 칸 결과가 같아야 한다.
                    if (live.MerchantRestockCycle == cycle && HasSlotKind(live.MerchantStock, TradeStockSlotKind.BasicEquipment) == false)
                    {
                        comparedLegacy++;
                        if (DescribeStock(live.MerchantStock, true) != DescribeStock(first.MerchantStock, true))
                            legacyMismatch++;
                    }
                }
            }
            finally
            {
                Debug.unityLogger.logEnabled = logEnabled;
            }

            check(shops > 0 && slotCountFailures == 0,
                $"기본 장비 칸 수 = 상점 Tier 규칙 3/3/4/4 (상점 {shops}, 실패 {slotCountFailures})");
            check(nonBasicInBasic == 0 && duplicateInBasic == 0 && tierFailures == 0,
                $"기본 장비 칸은 전부 기본 아이템·장비 중복 없음·상점 Tier 게이트 안 (비기본 {nonBasicInBasic}, 중복 {duplicateInBasic}, Tier {tierFailures})");
            check(basicOutsideBasic == 0, $"기존 칸 장비는 무작위 옵션 그대로 (기본 아이템 섞임 {basicOutsideBasic})");
            check(determinismFailures == 0, $"같은 (마을, 회차) 재롤은 같은 재고 (실패 {determinismFailures})");
            check(comparedLegacy == 0 || legacyMismatch == 0,
                $"옛 코드로 롤한 이번 회차 라이브 재고와 기존 칸 결과 동일 (비교 마을 {comparedLegacy}, 불일치 {legacyMismatch})");
            check(limitMismatch == 0, $"NPC 회차 한도는 기본 장비 칸을 빼고 센다 (불일치 {limitMismatch})");
        }

        private static void VerifyPurchaseFilter(StringBuilder log, Action<bool, string> check)
        {
            IReadOnlyList<VillageData> villages = AR.s.Village.GetAllVillages();
            if (villages.Count == 0)
            {
                check(false, "구매 필터: 마을 없음");
                return;
            }
            ItemData? basic = CreateSampleBasicItem();
            ItemData? regular = basic != null ? AR.s.Item.CreateInventoryItemData(basic.Id) : null;
            if (basic == null || regular == null)
            {
                check(false, "구매 필터: 견본 생성 실패");
                return;
            }
            VillageData village = villages[0];
            MerchantStockEntry MakeEntry(ItemData item, TradeStockSlotKind kind) => new MerchantStockEntry
            {
                Item = item, RemainingCount = 1, UnitPrice = 10, SlotKind = kind,
            };
            bool basicSlot = AR.s.Village.IsNpcPurchasableEntry(village, MakeEntry(basic, TradeStockSlotKind.BasicEquipment));
            bool basicInRare = AR.s.Village.IsNpcPurchasableEntry(village, MakeEntry(basic, TradeStockSlotKind.RareImport));
            bool basicConsigned = AR.s.Village.IsNpcPurchasableEntry(village, MakeEntry(basic, TradeStockSlotKind.AdventurerConsignment));
            bool regularGuaranteed = AR.s.Village.IsNpcPurchasableEntry(village, MakeEntry(regular, TradeStockSlotKind.Guaranteed));
            bool regularSpecialty = AR.s.Village.IsNpcPurchasableEntry(village, MakeEntry(regular, TradeStockSlotKind.Specialty));
            check(basicSlot == true && basicInRare == true && basicConsigned == false && regularGuaranteed == false &&
                  regularSpecialty == false,
                "NPC 구매 후보 = 기본 아이템만(칸 무관), 모험가 매입품은 기본 아이템이어도 제외, 무작위 옵션 장비 제외");
        }

        private static VillageData CloneVillage(VillageData live)
        {
            VillageData clone = JsonConvert.DeserializeObject<VillageData>(
                JsonConvert.SerializeObject(live, JsonSettings.Default), JsonSettings.Default)!;
            clone.EntityId = -1;
            clone.NpcEntityIds = new List<int>(live.NpcEntityIds);
            return clone;
        }

        private static bool HasSlotKind(List<MerchantStockEntry> stock, TradeStockSlotKind kind)
        {
            for (int i = 0; i < stock.Count; i++)
            {
                if (stock[i].SlotKind == kind)
                    return true;
            }
            return false;
        }

        /// <summary>재고 서명. legacyOnly면 롤러가 만드는 기존 칸(보장·특산·희귀·특수)만 남긴다. 인스턴스 번호·잔량은 뺀다.</summary>
        private static string DescribeStock(List<MerchantStockEntry> stock, bool legacyOnly)
        {
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < stock.Count; i++)
            {
                MerchantStockEntry entry = stock[i];
                if (legacyOnly == true && (entry.SlotKind == TradeStockSlotKind.BasicEquipment ||
                                           entry.SlotKind == TradeStockSlotKind.AdventurerConsignment))
                    continue;
                sb.Append(entry.SlotKind).Append(':').Append(entry.Item != null ? entry.Item.Id : 0).Append(':')
                    .Append(entry.UnitPrice).Append(':').Append(entry.PriceReason).Append(':');
                if (entry.Item?.Equipment != null)
                    sb.Append(entry.Item.Equipment.Quality).Append(':').Append(entry.Item.Equipment.BasicItemId).Append(':')
                        .Append(DescribeMods(entry.Item));
                if (entry.Item?.SkillBook != null)
                    sb.Append("book").Append(entry.Item.SkillBook.SkillId).Append('/').Append(entry.Item.SkillBook.PageCapacityBonus)
                        .Append('/').Append(entry.Item.SkillBook.PageSlotsBonus);
                if (entry.Item?.SkillPage != null)
                    sb.Append("page").Append(entry.Item.SkillPage.SkillEffectId);
                sb.Append('|');
            }
            return sb.ToString();
        }

        /// <summary>
        /// 기본값 생략 검사 (A: 아이템 빈 필드, B: 활동 상태, 저장 복사기의 DefaultValueHandling.Ignore 지원).
        /// 실제 저장과 같은 경로(SaveDtoSnapshot → 작성기 설정 JSON → JsonSettings.Default 로드)로 왕복한다.
        /// </summary>
        private static void VerifyDefaultOmission(StringBuilder log, Action<bool, string> check)
        {
            _snapshot = new SaveDtoSnapshot();
            _writerSettings = WriterSettings();
            // A. 기본 아이템은 Id·인스턴스 번호·행 번호만 남는다.
            ItemData? basic = CreateSampleBasicItem();
            if (basic?.Equipment != null)
            {
                string expected = "{\"Id\":" + basic.Id + ",\"ItemInstanceId\":" + basic.ItemInstanceId +
                                  ",\"Equipment\":{\"BasicItemId\":" + basic.Equipment.BasicItemId + "}}";
                string viaSnapshot = SnapshotJson(basic);
                string viaNewtonsoft = JsonConvert.SerializeObject(basic, Formatting.None, JsonSettings.Default);
                ItemData? loaded = JsonConvert.DeserializeObject<ItemData>(viaSnapshot, JsonSettings.Default);
                loaded?.OnLoadCompleted();
                check(viaSnapshot == expected && viaNewtonsoft == expected && loaded?.Equipment != null &&
                      loaded.Quantity == 1 && loaded.Equipment.Id == basic.Id && loaded.SkillBook == null &&
                      loaded.SkillPage == null && loaded.Equipment.WeaponLocalEnhancements != null &&
                      SameEquipment(basic, loaded) == true,
                    $"기본 아이템 저장 = Id·인스턴스·행 번호만 ({Encoding.UTF8.GetByteCount(viaSnapshot)}B), 로드 뒤 Quantity 1·장비 Id 복원");
            }
            else
            {
                check(false, "기본값 생략 검사: 견본 기본 아이템 생성 실패");
            }

            // Quantity: 1만 생략하고 0·7은 그대로 저장된다.
            bool quantityOk = true;
            int[] quantities = { 0, 1, 7 };
            for (int i = 0; i < quantities.Length; i++)
            {
                ItemData currency = new ItemData { Id = 1002, ItemInstanceId = 0, Quantity = quantities[i] };
                ItemData? back = JsonConvert.DeserializeObject<ItemData>(SnapshotJson(currency), JsonSettings.Default);
                quantityOk &= back != null && back.Quantity == quantities[i];
            }
            check(quantityOk == true, "Quantity 0·1·7 저장·로드 왕복 동일 (1만 생략)");

            // B. 활동 상태: 라이브 NPC·파티 활동을 실제 저장 경로로 왕복해 모든 필드가 같다. 빈 상태는 "{}".
            int activityCount = 0, activityMismatch = 0;
            long activityBytes = 0;
            foreach (KeyValuePair<int, NpcSaveData> pair in AR.s.Npc.SaveDataView)
            {
                NpcActivityState source = pair.Value.Activity;
                string json = SnapshotJson(source);
                activityBytes += Encoding.UTF8.GetByteCount(json);
                NpcActivityState? back = JsonConvert.DeserializeObject<NpcActivityState>(json, JsonSettings.Default);
                activityCount++;
                if (back == null || FieldsEqual(source, back) == false)
                {
                    activityMismatch++;
                    if (activityMismatch <= 3)
                        log.AppendLine($"    활동 상태 불일치 npc={pair.Key} json={json}");
                }
            }
            check(activityCount > 0 && activityMismatch == 0 && SnapshotJson(new NpcActivityState()) == "{}",
                $"활동 상태 기본값 생략 왕복 동일 (NPC {activityCount}, 불일치 {activityMismatch}, 평균 {(activityCount > 0 ? activityBytes / activityCount : 0)}B), 빈 상태 = {{}}");

            // 저장 복사기의 DefaultValueHandling.Ignore 지원: Deed·NPC 필드의 기존 생략 규칙(D93 등)이 실제 저장에도 적용된다.
            int deedCount = 0, deedMismatch = 0, npcMismatch = 0, checkedNpcs = 0;
            foreach (KeyValuePair<int, NpcSaveData> pair in AR.s.Npc.SaveDataView)
            {
                List<NpcDeedEntry> deeds = pair.Value.Deeds;
                for (int d = 0; deeds != null && d < deeds.Count && deedCount < 2000; d++)
                {
                    deedCount++;
                    string viaSnapshot = SnapshotJson(deeds[d]);
                    string viaNewtonsoft = JsonConvert.SerializeObject(deeds[d], Formatting.None, _writerSettings);
                    NpcDeedEntry? back = JsonConvert.DeserializeObject<NpcDeedEntry>(viaSnapshot, JsonSettings.Default);
                    if (viaSnapshot != viaNewtonsoft || back == null || FieldsEqual(deeds[d], back) == false)
                        deedMismatch++;
                }
                if (checkedNpcs < 300)
                {
                    checkedNpcs++;
                    string npcSnapshot = SnapshotJson(pair.Value);
                    string npcNewtonsoft = JsonConvert.SerializeObject(pair.Value, Formatting.None, _writerSettings);
                    if (npcSnapshot != npcNewtonsoft)
                        npcMismatch++;
                }
            }
            check(deedCount > 0 && deedMismatch == 0 && npcMismatch == 0,
                $"저장 복사기 = Newtonsoft 직렬화 결과 (Deed {deedCount} 불일치 {deedMismatch}, NPC {checkedNpcs} 불일치 {npcMismatch}), Deed 왕복 동일");
        }

        // 진단 1회 동안 재사용하는 복사기·작성기 설정 (계약 해석을 매번 다시 하지 않게). 사용 전 VerifyDefaultOmission이 새로 만든다.
        private static SaveDtoSnapshot _snapshot = new SaveDtoSnapshot();
        private static JsonSerializerSettings _writerSettings = WriterSettings();

        /// <summary>실제 저장과 같은 경로: SaveDtoSnapshot 사본을 작성기 설정(Formatting.None + StringEnumConverter)으로 JSON화.</summary>
        private static string SnapshotJson(object source)
        {
            return JsonConvert.SerializeObject(_snapshot.Capture(source), Formatting.None, _writerSettings);
        }

        private static JsonSerializerSettings WriterSettings()
        {
            JsonSerializerSettings settings = new JsonSerializerSettings();
            settings.Converters.Add(new Newtonsoft.Json.Converters.StringEnumConverter());
            return settings;
        }

        /// <summary>공개 인스턴스 필드 비교 (목록·객체는 JSON으로 비교).</summary>
        private static bool FieldsEqual(object a, object b)
        {
            System.Reflection.FieldInfo[] fields = a.GetType().GetFields(
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public);
            for (int i = 0; i < fields.Length; i++)
            {
                if (fields[i].IsNotSerialized == true)
                    continue;
                object? va = fields[i].GetValue(a);
                object? vb = fields[i].GetValue(b);
                if (va == null || vb == null)
                {
                    if (va != vb)
                        return false;
                    continue;
                }
                if (va.GetType().IsPrimitive == true || va.GetType().IsEnum == true || va is string)
                {
                    if (va.Equals(vb) == false)
                        return false;
                    continue;
                }
                if (JsonConvert.SerializeObject(va, JsonSettings.Default) != JsonConvert.SerializeObject(vb, JsonSettings.Default))
                    return false;
            }
            return true;
        }

        private static List<int> CollectBasicItemIds()
        {
            List<int> result = new List<int>();
            List<ItemTable> all = AR.s.Data.GetAllItems();
            for (int i = 0; i < all.Count; i++)
            {
                if (AR.s.Data.GetBasicItemVariants(all[i].Id).Count > 0)
                    result.Add(all[i].Id);
            }
            result.Sort();
            return result;
        }

        /// <summary>무기 기본 아이템 하나 (무기 능력·Postfix 추가까지 확인하려고 무기를 우선한다).</summary>
        private static ItemData? CreateSampleBasicItem()
        {
            List<int> itemIds = CollectBasicItemIds();
            for (int i = 0; i < itemIds.Count; i++)
            {
                ItemTable? table = AR.s.Data.GetItem(itemIds[i]);
                if (table != null && Utils.CategoryToEquipmentTypeMask(table.Category) == GE.EquipmentTypeMask.Weapon)
                    return AR.s.Item.CreateBasicItem(AR.s.Data.GetBasicItemVariants(itemIds[i])[0].Id);
            }
            return itemIds.Count > 0 ? AR.s.Item.CreateBasicItem(AR.s.Data.GetBasicItemVariants(itemIds[0])[0].Id) : null;
        }

        private static bool VerifyEnchantmentClearsBasicItem(int basicItemId, StringBuilder log)
        {
            ItemData item = AR.s.Item.CreateBasicItem(basicItemId)!;
            EquipmentData equipment = item.Equipment!;

            int currencyId = 0;
            List<ItemTable> all = AR.s.Data.GetAllItems();
            for (int i = 0; i < all.Count && currencyId == 0; i++)
            {
                if (all[i].ItemType == GE.ItemType.Currency)
                    currencyId = all[i].Id;
            }
            ModTable? addMod = null;
            List<ModTable> pool = AR.s.Data.GetModPool(GE.ModSlot.Postfix, equipment.EquipType);
            for (int i = 0; i < pool.Count && addMod == null; i++)
            {
                if (equipment.HasModGroupInSlot(GE.ModSlot.Postfix, pool[i].Group) == false)
                    addMod = pool[i];
            }
            if (currencyId == 0 || addMod == null)
            {
                log.AppendLine("    마법부여 견본 준비 실패 (재료 화폐 또는 Postfix 후보 없음)");
                return false;
            }

            List<ItemData?> slots = new List<ItemData?>();
            for (int i = 0; i < 8; i++)
                slots.Add(null);
            slots[0] = new ItemData { Id = currencyId, ItemInstanceId = 0, Quantity = 1, Table = AR.s.Data.GetItem(currencyId) };
            Inventory inventory = new Inventory();
            inventory.Initialize(slots, slots.Count);
            PlayerData player = new PlayerData { Level = 1, Gold = 100 };
            ItemEnchantmentCostTable cost = new ItemEnchantmentCostTable
            {
                Operation = GE.ItemEnchantmentOperation.AddAffix,
                MaterialItemId = currencyId,
                MaterialCount = 1,
                GoldCost = 0,
            };
            ModInstance applied = new ModInstance
            {
                ModTableId = addMod.Id,
                Slot = GE.ModSlot.Postfix,
                Tier = 1,
                Value1 = 1,
                Table = addMod,
            };

            bool committed = ItemEnchantmentTransaction.TryCommit(equipment, GE.ItemEnchantmentOperation.AddAffix, -1,
                applied, inventory, player, cost, out ItemEnchantmentFailureReason reason);
            string json = JsonConvert.SerializeObject(item, Formatting.None, JsonSettings.Default);
            ItemData? loaded = RoundTrip(json);
            bool ok = committed == true && equipment.BasicItemId == 0 && json.Contains("\"Mods\"") == true &&
                      json.Contains("\"BasicItemId\"") == false && loaded?.Equipment != null &&
                      loaded.Equipment.BasicItemId == 0 && SameEquipment(item, loaded) == true;
            if (ok == false)
                log.AppendLine($"    마법부여 실패 committed={committed} reason={reason} json={json}");
            return ok;
        }

        private static bool CheckSnapshotOmitted(ItemData item, int basicItemId)
        {
            object? captured = new SaveDtoSnapshot().Capture(item);
            if (captured is Dictionary<string, object?> itemFields == false)
                return false;
            if (itemFields.TryGetValue("Equipment", out object? equipmentObject) == false ||
                equipmentObject is Dictionary<string, object?> equipmentFields == false)
                return false;
            return equipmentFields.ContainsKey("Mods") == false && equipmentFields.ContainsKey("Quality") == false &&
                   equipmentFields.ContainsKey("WeaponStats") == false && equipmentFields.ContainsKey("EquipType") == false &&
                   equipmentFields.TryGetValue("BasicItemId", out object? id) == true && id is int value && value == basicItemId;
        }

        private static ItemData? RoundTrip(string json)
        {
            ItemData? loaded = JsonConvert.DeserializeObject<ItemData>(json, JsonSettings.Default);
            loaded?.OnLoadCompleted();
            return loaded;
        }

        private static bool SameEquipment(ItemData a, ItemData b)
        {
            if (a.Equipment == null || b.Equipment == null)
                return false;
            if (a.Id != b.Id || DescribeMods(a) != DescribeMods(b))
                return false;
            if (a.Equipment.EquipType != b.Equipment.EquipType)
                return false;
            if (JsonConvert.SerializeObject(a.Equipment.WeaponStats) != JsonConvert.SerializeObject(b.Equipment.WeaponStats))
                return false;
            float powerA = NpcPowerEvaluator.EvaluateBreakdown(new ItemData?[] { a }, 0).Total;
            float powerB = NpcPowerEvaluator.EvaluateBreakdown(new ItemData?[] { b }, 0).Total;
            return Mathf.Approximately(powerA, powerB);
        }

        private static string DescribeMods(ItemData item)
        {
            if (item.Equipment == null)
                return "none";
            StringBuilder sb = new StringBuilder();
            List<ModInstance> mods = item.Equipment.Mods;
            for (int i = 0; i < mods.Count; i++)
            {
                ModInstance mod = mods[i];
                sb.Append(mod.ModTableId).Append(':').Append(mod.Slot).Append(":T").Append(mod.Tier).Append(':')
                    .Append(mod.Value1).Append('/').Append(mod.Value2).Append(mod.Table != null ? "" : "(no table)").Append(';');
            }
            return sb.ToString();
        }
    }
}
