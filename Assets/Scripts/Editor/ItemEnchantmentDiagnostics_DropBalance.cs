#nullable enable
using System.Collections.Generic;
using ARPG.Data;
using ARPG.Item;
using ARPG.Tables;
using Newtonsoft.Json;

namespace ARPG.Editor
{
    /// <summary>
    /// ITEM_DROP_BALANCE_DESIGN.md의 장비 등급·품질·제작 순서 계약을 로컬 bytes로 검증한다.
    /// 진화의 오브(일반 → 마법), 품질 다시 굴리기(일반만, 0~20, 골드만), 엑잘티드의 일반 장비 금지,
    /// 등급 판정, 품질 반올림, 품질 저장(새 칸 BaseQuality, 0 생략, 옛 Quality 무시).
    /// </summary>
    public static partial class ItemEnchantmentDiagnostics
    {
        private const int EVOLVE_GOLD_COST = 15;
        private const int QUALITY_REROLL_GOLD_COST = 20;

        private static void VerifyDropBalanceEnchantment(Dictionary<int, ModTable> modById,
            List<ModTierTable> tiers, List<ItemEnchantmentCostTable> costs,
            Dictionary<int, ItemTable> itemById, List<string> errors, ref int assertions)
        {
            VerifyGradeAndQualityRules(errors, ref assertions);
            VerifyQualitySaveRoundTrip(errors, ref assertions);

            ItemEnchantmentCostTable? evolveCost = FindCost(costs, GlobalEnum.ItemEnchantmentOperation.EvolveToMagic);
            ItemEnchantmentCostTable? qualityCost = FindCost(costs, GlobalEnum.ItemEnchantmentOperation.RerollQuality);
            ItemEnchantmentCostTable? addCost = FindCost(costs, GlobalEnum.ItemEnchantmentOperation.AddAffix);
            Require(evolveCost != null && evolveCost.MaterialItemId == 1000 && evolveCost.MaterialCount == 1
                && evolveCost.GoldCost == EVOLVE_GOLD_COST && evolveCost.RequiredForgeTier == GlobalEnum.ForgeTier.Basic,
                $"EvolveToMagic 비용은 1000 x1 + {EVOLVE_GOLD_COST} Gold / Basic이어야 함", errors, ref assertions);
            Require(qualityCost != null && qualityCost.IsGoldOnly == true
                && qualityCost.GoldCost == QUALITY_REROLL_GOLD_COST && qualityCost.RequiredForgeTier == GlobalEnum.ForgeTier.Basic,
                $"RerollQuality 비용은 재료 없이 {QUALITY_REROLL_GOLD_COST} Gold / Basic이어야 함", errors, ref assertions);
            Require(itemById.TryGetValue(1000, out ItemTable evolveOrb) && evolveOrb.ItemType == GlobalEnum.ItemType.Currency,
                "Item 1000(진화의 오브)은 Currency여야 함", errors, ref assertions);
            if (evolveCost == null || qualityCost == null || addCost == null
                || RequireMods(modById, errors, ref assertions, 1100) == false)
                return;

            ModTable phys = modById[1100];
            List<ModTable> modPool = new List<ModTable>(modById.Values);
            modPool.Sort((a, b) => a.Id.CompareTo(b.Id));

            // 1. 진화: 일반 → 마법(옵션 1개), 오브 1개 + 15 Gold. 아이템 레벨 90이면 T6~T10 후보에서 마지막(T10)
            EquipmentData evolveEquipment = CreateEquipmentWithImmutableOptions();
            evolveEquipment.Quality = 12;
            Inventory evolveInventory = CreateInventory(CreateCurrency(1000, 1, itemById));
            PlayerData evolvePlayer = new PlayerData { Level = 1, Gold = 100 };
            int evolveSaves = 0;
            ItemEnchantmentService evolveService = new ItemEnchantmentService(max => max - 1, () => evolveSaves++);
            Dictionary<int, ModTable> evolveResolver = CreateResolver(modById, evolveEquipment);
            ItemEnchantmentResult evolveResult = evolveService.ExecuteForDiagnostics(evolveEquipment, evolveInventory,
                evolvePlayer, GlobalEnum.ItemEnchantmentOperation.EvolveToMagic, 90, modPool, tiers, evolveCost,
                id => ResolveMod(evolveResolver, id));
            Require(evolveResult.Success == true && evolveEquipment.GetGrade() == GlobalEnum.EquipmentGrade.Magic
                && evolveEquipment.Mods.Count == 3 && evolveResult.AppliedMod?.Tier == 10
                && evolveInventory.GetTotalQuantity(1000) == 0 && evolvePlayer.Gold == 100 - EVOLVE_GOLD_COST
                && evolveSaves == 1 && evolveEquipment.Quality == 12,
                $"진화의 오브는 일반 장비에 옵션 1개(아이템 레벨 90: T10까지)를 붙이고 오브 1개 + {EVOLVE_GOLD_COST} Gold를 쓰며 품질은 그대로여야 함 " +
                $"(success={evolveResult.Success}, reason={evolveResult.FailureReason}, tier={evolveResult.AppliedMod?.Tier})",
                errors, ref assertions);

            // 2. 진화는 마법·희귀에 쓸 수 없다
            EquipmentData magicEquipment = CreateEquipmentWithImmutableOptions();
            magicEquipment.Mods.Add(CreateMod(phys, 1, 1, 3));
            Inventory magicInventory = CreateInventory(CreateCurrency(1000, 1, itemById), CreateCurrency(1002, 1, itemById));
            PlayerData magicPlayer = new PlayerData { Level = 1, Gold = 100 };
            Dictionary<int, ModTable> magicResolver = CreateResolver(modById, magicEquipment);
            string magicBefore = CaptureState(magicEquipment, magicInventory, magicPlayer);
            ItemEnchantmentResult evolveMagic = new ItemEnchantmentService(_ => 0, () => { }).ExecuteForDiagnostics(
                magicEquipment, magicInventory, magicPlayer, GlobalEnum.ItemEnchantmentOperation.EvolveToMagic, 1,
                modPool, tiers, evolveCost, id => ResolveMod(magicResolver, id));
            ItemEnchantmentResult qualityMagic = new ItemEnchantmentService(_ => 0, () => { }).ExecuteForDiagnostics(
                magicEquipment, magicInventory, magicPlayer, GlobalEnum.ItemEnchantmentOperation.RerollQuality, 1,
                modPool, tiers, qualityCost, id => ResolveMod(magicResolver, id));
            Require(evolveMagic.FailureReason == ItemEnchantmentFailureReason.RequiresNormalGrade
                && qualityMagic.FailureReason == ItemEnchantmentFailureReason.RequiresNormalGrade
                && CaptureState(magicEquipment, magicInventory, magicPlayer) == magicBefore,
                "진화·품질 다시 굴리기는 마법 장비에서 아무것도 바꾸지 않고 거부해야 함", errors, ref assertions);

            // 3. 엑잘티드는 일반 장비에 쓸 수 없다 (진화의 오브부터)
            EquipmentData normalEquipment = CreateEquipmentWithImmutableOptions();
            Inventory normalInventory = CreateInventory(CreateCurrency(1002, 1, itemById));
            PlayerData normalPlayer = new PlayerData { Level = 1, Gold = 100 };
            Dictionary<int, ModTable> normalResolver = CreateResolver(modById, normalEquipment);
            string normalBefore = CaptureState(normalEquipment, normalInventory, normalPlayer);
            ItemEnchantmentResult addNormal = new ItemEnchantmentService(_ => 0, () => { }).ExecuteForDiagnostics(
                normalEquipment, normalInventory, normalPlayer, GlobalEnum.ItemEnchantmentOperation.AddAffix, 1,
                modPool, tiers, addCost, id => ResolveMod(normalResolver, id));
            Require(addNormal.FailureReason == ItemEnchantmentFailureReason.RequiresAffixedGrade
                && CaptureState(normalEquipment, normalInventory, normalPlayer) == normalBefore,
                "엑잘티드 오브는 일반 장비에서 아무것도 바꾸지 않고 거부해야 함", errors, ref assertions);

            // 4. 품질 다시 굴리기: 일반만, 0~20 균등, 낮아질 수 있음, 골드만 20
            EquipmentData qualityEquipment = CreateEquipmentWithImmutableOptions();
            qualityEquipment.Quality = 15;
            Inventory qualityInventory = CreateInventory();
            PlayerData qualityPlayer = new PlayerData { Level = 1, Gold = 50 };
            Dictionary<int, ModTable> qualityResolver = CreateResolver(modById, qualityEquipment);
            ItemEnchantmentResult lowRoll = new ItemEnchantmentService(_ => 3, () => { }).ExecuteForDiagnostics(
                qualityEquipment, qualityInventory, qualityPlayer, GlobalEnum.ItemEnchantmentOperation.RerollQuality, 1,
                modPool, tiers, qualityCost, id => ResolveMod(qualityResolver, id));
            Require(lowRoll.Success == true && lowRoll.PreviousQuality == 15 && lowRoll.NewQuality == 3
                && qualityEquipment.Quality == 3 && qualityPlayer.Gold == 50 - QUALITY_REROLL_GOLD_COST
                && qualityEquipment.GetGrade() == GlobalEnum.EquipmentGrade.Normal,
                $"품질 다시 굴리기는 0~20 중 굴린 값으로 바꾸고(낮아질 수 있음) {QUALITY_REROLL_GOLD_COST} Gold만 써야 함",
                errors, ref assertions);
            ItemEnchantmentResult topRoll = new ItemEnchantmentService(max => max - 1, () => { }).ExecuteForDiagnostics(
                qualityEquipment, qualityInventory, qualityPlayer, GlobalEnum.ItemEnchantmentOperation.RerollQuality, 1,
                modPool, tiers, qualityCost, id => ResolveMod(qualityResolver, id));
            Require(topRoll.Success == true && qualityEquipment.Quality == EquipmentQualityRules.MAX_QUALITY
                && qualityPlayer.Gold == 50 - QUALITY_REROLL_GOLD_COST * 2,
                "품질 다시 굴리기의 최댓값은 20이어야 함", errors, ref assertions);
            ItemEnchantmentResult poorRoll = new ItemEnchantmentService(_ => 0, () => { }).ExecuteForDiagnostics(
                qualityEquipment, qualityInventory, qualityPlayer, GlobalEnum.ItemEnchantmentOperation.RerollQuality, 1,
                modPool, tiers, qualityCost, id => ResolveMod(qualityResolver, id));
            Require(poorRoll.FailureReason == ItemEnchantmentFailureReason.GoldInsufficient
                && qualityEquipment.Quality == EquipmentQualityRules.MAX_QUALITY,
                "골드가 모자라면 품질을 바꾸지 않아야 함", errors, ref assertions);
        }

        private static void VerifyGradeAndQualityRules(List<string> errors, ref int assertions)
        {
            Require(EquipmentData.ResolveGrade(0, 0) == GlobalEnum.EquipmentGrade.Normal
                && EquipmentData.ResolveGrade(1, 0) == GlobalEnum.EquipmentGrade.Magic
                && EquipmentData.ResolveGrade(0, 1) == GlobalEnum.EquipmentGrade.Magic
                && EquipmentData.ResolveGrade(1, 1) == GlobalEnum.EquipmentGrade.Magic
                && EquipmentData.ResolveGrade(2, 1) == GlobalEnum.EquipmentGrade.Rare
                && EquipmentData.ResolveGrade(0, 3) == GlobalEnum.EquipmentGrade.Rare
                && EquipmentData.ResolveGrade(3, 3) == GlobalEnum.EquipmentGrade.Rare,
                "장비 등급 판정(일반 0개, 마법 각 1개 이하, 그 밖 희귀)이 잘못됨", errors, ref assertions);

            // 문서 §5 예시: 방어도 90·체력 444 → 품질 20이면 108·533. 작은 값은 반올림으로 효과가 없을 수 있다
            Require(EquipmentQualityRules.ApplyToBaseValue(90, 20) == 108
                && EquipmentQualityRules.ApplyToBaseValue(444, 20) == 533
                && EquipmentQualityRules.ApplyToBaseValue(2, 5) == 2
                && EquipmentQualityRules.ApplyToBaseValue(10, 5) == 11
                && EquipmentQualityRules.ApplyToBaseValue(50, 0) == 50
                && EquipmentQualityRules.ApplyToBaseValue(100, 99) == 120,
                "품질 반올림(정확히 .5면 올림)·상한 20 적용이 잘못됨", errors, ref assertions);
        }

        private static void VerifyQualitySaveRoundTrip(List<string> errors, ref int assertions)
        {
            ItemData quality = new ItemData
            {
                Id = 1,
                ItemInstanceId = 7201,
                Quantity = 1,
                Equipment = new EquipmentData { Id = 1, Quality = 17, ItemLevel = 40 },
            };
            ItemData zero = new ItemData
            {
                Id = 1,
                ItemInstanceId = 7202,
                Quantity = 1,
                Equipment = new EquipmentData { Id = 1, Quality = 0 },
            };
            string qualityJson = JsonConvert.SerializeObject(quality, Formatting.None, JsonSettings.Default);
            string zeroJson = JsonConvert.SerializeObject(zero, Formatting.None, JsonSettings.Default);
            ItemData? loadedQuality = JsonConvert.DeserializeObject<ItemData>(qualityJson, JsonSettings.Default);
            ItemData? loadedZero = JsonConvert.DeserializeObject<ItemData>(zeroJson, JsonSettings.Default);
            // 옛 세이브의 Quality(1~100, 효과 없던 값)는 읽지 않는다 (D14)
            ItemData? loadedLegacy = JsonConvert.DeserializeObject<ItemData>(
                "{\"Id\":1,\"ItemInstanceId\":7203,\"Equipment\":{\"Quality\":73,\"Mods\":[]}}", JsonSettings.Default);
            Require(qualityJson.Contains("\"BaseQuality\":17") && loadedQuality?.Equipment?.Quality == 17,
                "품질이 새 칸 BaseQuality로 저장 왕복되지 않음", errors, ref assertions);
            Require(zeroJson.Contains("Quality") == false && loadedZero?.Equipment?.Quality == 0,
                "품질 0은 저장하지 않고 0으로 읽혀야 함", errors, ref assertions);
            Require(loadedLegacy?.Equipment != null && loadedLegacy.Equipment.Quality == 0,
                "옛 Quality 칸은 읽지 않아 품질 0이어야 함", errors, ref assertions);
        }
    }
}
