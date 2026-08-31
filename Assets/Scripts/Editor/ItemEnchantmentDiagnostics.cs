#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using ARPG.Data;
using ARPG.Item;
using ARPG.Tables;
using ARPG.Village;
using Newtonsoft.Json;
using UnityEditor;
using UnityEngine;

namespace ARPG.Editor
{
    /// <summary>
    /// ITEM_ENCHANTMENT_DESIGN MVP의 순수 규칙·메모리 트랜잭션·JSON 왕복과
    /// 신규 게임 공통 Forge 후조건을 실제 로컬 bytes 데이터로 검증한다.
    /// 사용자 세이브와 PlayMode singleton은 변경하지 않는다.
    /// </summary>
    public static class ItemEnchantmentDiagnostics
    {
        private const string MENU_PATH = "ARPG/Diagnostics/Item Enchantment MVP";

        [MenuItem(MENU_PATH)]
        public static void Run()
        {
            bool passed = RunAtPath(Application.dataPath, out string report);
            if (passed == true)
                Debug.Log(report);
            else
                Debug.LogError(report);
        }

        /// <summary>
        /// Unity 사용자 세이브나 singleton 없이 실행되는 진단 코어.
        /// 외부 컴파일 검증기도 같은 코드를 호출할 수 있도록 Assets 절대 경로를 받는다.
        /// </summary>
        public static bool RunAtPath(string assetsPath, out string report)
        {
            List<string> errors = new List<string>();
            int assertions = 0;
            int villageCases = 0;

            try
            {
                List<ItemTable> items = LoadTable<ItemTable>(assetsPath, "ItemTable.bytes");
                List<ModTable> mods = LoadTable<ModTable>(assetsPath, "ModTable.bytes");
                List<ModTierTable> tiers = LoadTable<ModTierTable>(assetsPath,
                    "ModTierTable.bytes");
                List<ItemEnchantmentCostTable> costs =
                    LoadTable<ItemEnchantmentCostTable>(assetsPath,
                        "ItemEnchantmentCostTable.bytes");
                List<BuildableItemTable> buildables =
                    LoadTable<BuildableItemTable>(assetsPath, "BuildableItemTable.bytes");

                Dictionary<int, ItemTable> itemById = IndexById(items, errors);
                Dictionary<int, ModTable> modById = IndexById(mods, errors);
                Dictionary<int, BuildableItemTable> buildableById =
                    IndexById(buildables, errors);

                VerifyCostAndTierData(itemById, modById, tiers, costs,
                    errors, ref assertions);
                VerifyNewEquipmentAffixes(modById, tiers, errors, ref assertions);
                VerifyEnchantmentRules(modById, tiers, errors, ref assertions);
                VerifyTransactionsAndSaveRoundTrip(modById, tiers, costs, itemById,
                    errors, ref assertions);
                villageCases = VerifyVillageForgeGuarantee(buildableById,
                    errors, ref assertions);
            }
            catch (Exception ex)
            {
                errors.Add($"진단 예외: {ex}");
            }

            if (errors.Count == 0)
            {
                report = $"[ItemEnchantmentDiagnostics] PASS — assertions={assertions}, " +
                    $"villageCases={villageCases}. 사용자 세이브 쓰기 0회.";
                return true;
            }

            report = $"[ItemEnchantmentDiagnostics] FAIL ({errors.Count})\n- " +
                string.Join("\n- ", errors);
            return false;
        }

        private static void VerifyCostAndTierData(Dictionary<int, ItemTable> itemById,
            Dictionary<int, ModTable> modById, List<ModTierTable> tiers,
            List<ItemEnchantmentCostTable> costs, List<string> errors,
            ref int assertions)
        {
            ItemEnchantmentCostTable? replace = FindCost(costs,
                GlobalEnum.ItemEnchantmentOperation.ReplaceAffix);
            ItemEnchantmentCostTable? add = FindCost(costs,
                GlobalEnum.ItemEnchantmentOperation.AddAffix);
            Require(replace != null, "ReplaceAffix 비용 행 누락", errors, ref assertions);
            Require(add != null, "AddAffix 비용 행 누락", errors, ref assertions);
            if (replace == null || add == null)
                return;

            Require(replace.MaterialItemId == 1001 && replace.MaterialCount == 1
                && replace.GoldCost == 25
                && replace.RequiredForgeTier == GlobalEnum.ForgeTier.Basic,
                "ReplaceAffix 비용은 1001 x1 + 25 Gold / Basic이어야 함",
                errors, ref assertions);
            Require(add.MaterialItemId == 1002 && add.MaterialCount == 1
                && add.GoldCost == 40
                && add.RequiredForgeTier == GlobalEnum.ForgeTier.Basic,
                "AddAffix 비용은 1002 x1 + 40 Gold / Basic이어야 함",
                errors, ref assertions);
            Require(itemById.TryGetValue(1001, out ItemTable alteration)
                && alteration.ItemType == GlobalEnum.ItemType.Currency,
                "Item 1001은 Currency여야 함", errors, ref assertions);
            Require(itemById.TryGetValue(1002, out ItemTable exalted)
                && exalted.ItemType == GlobalEnum.ItemType.Currency,
                "Item 1002는 Currency여야 함", errors, ref assertions);

            string? candidateSignature = null;
            for (int forgeValue = (int)GlobalEnum.ForgeTier.Basic;
                forgeValue <= (int)GlobalEnum.ForgeTier.Premium; forgeValue++)
            {
                GlobalEnum.ForgeTier forgeTier = (GlobalEnum.ForgeTier)forgeValue;
                Require(forgeTier >= replace.RequiredForgeTier
                    && forgeTier >= add.RequiredForgeTier,
                    $"Forge {forgeTier}에서 두 비용 행을 사용할 수 없음",
                    errors, ref assertions);

                string signature = BuildWeaponCandidateSignature(modById, tiers, 60, 3);
                if (candidateSignature == null)
                    candidateSignature = signature;
                else
                    Require(signature == candidateSignature,
                        $"Forge {forgeTier} 후보가 Tier 1 후보와 다름",
                        errors, ref assertions);
            }

            RequireCurveMatches(tiers, 1100, 1103, errors, ref assertions);
            RequireCurveMatches(tiers, 1100, 1104, errors, ref assertions);
            RequireCurveMatches(tiers, 1203, 1204, errors, ref assertions);
            RequireCurveMatches(tiers, 1203, 1205, errors, ref assertions);
            RequireCurveMatches(tiers, 1203, 1206, errors, ref assertions);

            for (int modId = 1200; modId <= 1208; modId++)
                RequireTierCount(tiers, modId, 3, errors, ref assertions);

            int[] supportedMods =
            {
                1000, 1001, 1002, 1003, 1004, 1005, 1006,
                1100, 1101, 1102, 1103, 1104,
                1200, 1201, 1202, 1203, 1204, 1205, 1206, 1207, 1208,
            };
            for (int i = 0; i < supportedMods.Length; i++)
            {
                int modId = supportedMods[i];
                Require(modById.TryGetValue(modId, out ModTable supported)
                    && ItemEnchantmentRules.HasVerifiedRuntimeConsumer(supported) == true,
                    $"Mod {modId}는 검증된 장착/전투 소비 후보여야 함",
                    errors, ref assertions);
            }

            Require(modById.TryGetValue(1007, out ModTable poisonChance)
                && ItemEnchantmentRules.HasVerifiedRuntimeConsumer(poisonChance) == false,
                "소비되지 않는 PoisonRate Mod 1007은 Forge 후보에서 제외해야 함",
                errors, ref assertions);
            Require(modById.TryGetValue(2000, out ModTable lifeOnKill)
                && ItemEnchantmentRules.HasVerifiedRuntimeConsumer(lifeOnKill) == false,
                "소비 처리기가 없는 LifeOnKill Mod 2000은 Forge 후보에서 제외해야 함",
                errors, ref assertions);
        }

        private static void VerifyNewEquipmentAffixes(
            Dictionary<int, ModTable> modById, List<ModTierTable> tiers,
            List<string> errors, ref int assertions)
        {
            if (RequireMods(modById, errors, ref assertions,
                1200, 1201, 1202, 1203, 1204, 1205, 1206, 1207, 1208) == false)
                return;

            GlobalEnum.EquipmentTypeMask nonWeapon =
                GlobalEnum.EquipmentTypeMask.AllArmor
                | GlobalEnum.EquipmentTypeMask.AllJewelry;
            GlobalEnum.EquipmentTypeMask criticalDamageEquipment =
                GlobalEnum.EquipmentTypeMask.Weapon
                | GlobalEnum.EquipmentTypeMask.Gloves
                | GlobalEnum.EquipmentTypeMask.AllJewelry;
            GlobalEnum.EquipmentTypeMask castSpeedEquipment =
                GlobalEnum.EquipmentTypeMask.Shield
                | GlobalEnum.EquipmentTypeMask.Gloves
                | GlobalEnum.EquipmentTypeMask.AllJewelry;

            RequireModContract(modById[1200], "최대 마나",
                GlobalEnum.ModEffectType.FlatStat, GlobalEnum.ModSlot.Prefix,
                "MaxMp", GlobalEnum.DamageType.Physics, GlobalEnum.Stat.Mp,
                nonWeapon, errors, ref assertions);
            RequireModContract(modById[1201], "체력 재생",
                GlobalEnum.ModEffectType.FlatStat, GlobalEnum.ModSlot.Prefix,
                "HpGeneration", GlobalEnum.DamageType.Physics,
                GlobalEnum.Stat.HpGeneration, nonWeapon, errors, ref assertions);
            RequireModContract(modById[1202], "마나 재생",
                GlobalEnum.ModEffectType.FlatStat, GlobalEnum.ModSlot.Prefix,
                "MpGeneration", GlobalEnum.DamageType.Physics,
                GlobalEnum.Stat.MpGeneration, nonWeapon, errors, ref assertions);
            RequireModContract(modById[1203], "화염 저항",
                GlobalEnum.ModEffectType.FlatStat, GlobalEnum.ModSlot.Postfix,
                "FireResist", GlobalEnum.DamageType.Fire,
                GlobalEnum.Stat.FireResist, nonWeapon, errors, ref assertions);
            RequireModContract(modById[1204], "냉기 저항",
                GlobalEnum.ModEffectType.FlatStat, GlobalEnum.ModSlot.Postfix,
                "IceResist", GlobalEnum.DamageType.Ice,
                GlobalEnum.Stat.IceResist, nonWeapon, errors, ref assertions);
            RequireModContract(modById[1205], "번개 저항",
                GlobalEnum.ModEffectType.FlatStat, GlobalEnum.ModSlot.Postfix,
                "LightningResist", GlobalEnum.DamageType.Lightning,
                GlobalEnum.Stat.LightningResist, nonWeapon, errors, ref assertions);
            RequireModContract(modById[1206], "독 저항",
                GlobalEnum.ModEffectType.FlatStat, GlobalEnum.ModSlot.Postfix,
                "PoisonResist", GlobalEnum.DamageType.Poison,
                GlobalEnum.Stat.PoisonResist, nonWeapon, errors, ref assertions);
            RequireModContract(modById[1207], "치명타 피해 증가",
                GlobalEnum.ModEffectType.FlatStat, GlobalEnum.ModSlot.Postfix,
                "PostfixCriDamage", GlobalEnum.DamageType.Physics,
                GlobalEnum.Stat.CriDamageMul, criticalDamageEquipment,
                errors, ref assertions);
            RequireModContract(modById[1208], "시전 속도 증가",
                GlobalEnum.ModEffectType.IncreasedStat, GlobalEnum.ModSlot.Postfix,
                "CastSpeedInc", GlobalEnum.DamageType.Physics,
                GlobalEnum.Stat.CastSpeedMul, castSpeedEquipment,
                errors, ref assertions);

            for (int modId = 1200; modId <= 1208; modId++)
                RequireStandardThreeTierCurve(tiers, modId, errors, ref assertions);

            Require(EquipmentOptionText.FormatModLine(
                    CreateMod(modById[1200], 1, 20, 0)) == "최대 마나 +20",
                "최대 마나 옵션 문구가 올바르지 않음", errors, ref assertions);
            Require(EquipmentOptionText.FormatModLine(
                    CreateMod(modById[1201], 1, 2, 0)) == "초당 체력 재생 +2",
                "체력 재생 옵션 문구가 올바르지 않음", errors, ref assertions);
            Require(EquipmentOptionText.FormatModLine(
                    CreateMod(modById[1202], 1, 3, 0)) == "초당 마나 재생 +3",
                "마나 재생 옵션 문구가 올바르지 않음", errors, ref assertions);
            Require(EquipmentOptionText.FormatModLine(
                    CreateMod(modById[1203], 1, 15, 0)) == "화염 저항 +15%",
                "저항 옵션 문구가 올바르지 않음", errors, ref assertions);
            Require(EquipmentOptionText.FormatModLine(
                    CreateMod(modById[1207], 1, 10, 0)) == "치명타 피해 +10%",
                "치명타 피해 옵션 문구가 올바르지 않음", errors, ref assertions);
            Require(EquipmentOptionText.FormatModLine(
                    CreateMod(modById[1208], 1, 10, 0)) == "시전속도 10% 증가",
                "시전속도 옵션 문구가 올바르지 않음", errors, ref assertions);

            GlobalEnum.EquipmentTypeMask[] equipmentTypes =
            {
                GlobalEnum.EquipmentTypeMask.Weapon,
                GlobalEnum.EquipmentTypeMask.Shield,
                GlobalEnum.EquipmentTypeMask.Helmet,
                GlobalEnum.EquipmentTypeMask.Armor,
                GlobalEnum.EquipmentTypeMask.Gloves,
                GlobalEnum.EquipmentTypeMask.Boots,
                GlobalEnum.EquipmentTypeMask.Necklace,
                GlobalEnum.EquipmentTypeMask.Ring,
                GlobalEnum.EquipmentTypeMask.Belt,
                GlobalEnum.EquipmentTypeMask.Earring,
            };
            for (int i = 0; i < equipmentTypes.Length; i++)
            {
                RequireVerifiedCandidateCount(modById, tiers, equipmentTypes[i],
                    GlobalEnum.ModSlot.Prefix, 3, errors, ref assertions);
                RequireVerifiedCandidateCount(modById, tiers, equipmentTypes[i],
                    GlobalEnum.ModSlot.Postfix, 3, errors, ref assertions);
            }
        }

        private static void VerifyEnchantmentRules(Dictionary<int, ModTable> modById,
            List<ModTierTable> tiers, List<string> errors, ref int assertions)
        {
            if (RequireMods(modById, errors, ref assertions, 1100, 1101, 1102, 1103)
                == false)
                return;

            ModTable phys = modById[1100];
            EquipmentData equipment = CreateEquipmentWithImmutableOptions();
            equipment.Mods.Add(CreateMod(phys, 1, 1, 3));
            Dictionary<int, ModTable> resolver = CreateResolver(modById, equipment);

            ItemEnchantmentFailureReason failure =
                ItemEnchantmentRules.ValidateAffixState(equipment,
                    id => ResolveMod(resolver, id), out int prefixCount,
                    out int postfixCount);
            Require(failure == ItemEnchantmentFailureReason.None
                && prefixCount == 1 && postfixCount == 0,
                "정상 장비의 Prefix/Postfix 개수 검증이 잘못됨",
                errors, ref assertions);

            EquipmentData full = CreateEquipmentWithImmutableOptions();
            full.Mods.Add(CreateMod(modById[1100], 1, 1, 3));
            full.Mods.Add(CreateMod(modById[1101], 1, 1, 3));
            full.Mods.Add(CreateMod(modById[1102], 1, 1, 3));
            Dictionary<int, ModTable> fullResolver = CreateResolver(modById, full);
            failure = ItemEnchantmentRules.ValidateAffixState(full,
                id => ResolveMod(fullResolver, id), out prefixCount,
                out postfixCount);
            Require(failure == ItemEnchantmentFailureReason.None
                && prefixCount == 3 && postfixCount == 0,
                "Prefix 3개는 유효한 상한이어야 함",
                errors, ref assertions);

            EquipmentData overLimit = CreateEquipmentWithImmutableOptions();
            overLimit.Mods.Add(CreateMod(modById[1100], 1, 1, 3));
            overLimit.Mods.Add(CreateMod(modById[1101], 1, 1, 3));
            overLimit.Mods.Add(CreateMod(modById[1102], 1, 1, 3));
            overLimit.Mods.Add(CreateMod(modById[1103], 1, 1, 3));
            Dictionary<int, ModTable> overLimitResolver =
                CreateResolver(modById, overLimit);
            failure = ItemEnchantmentRules.ValidateAffixState(overLimit,
                id => ResolveMod(overLimitResolver, id), out _, out _);
            Require(failure == ItemEnchantmentFailureReason.AffixLimitReached,
                "이미 Prefix 4개인 손상 데이터는 거부해야 함",
                errors, ref assertions);

            EquipmentData ambiguous = CreateEquipmentWithImmutableOptions();
            ambiguous.Mods.Add(CreateMod(phys, 1, 1, 3));
            ambiguous.Mods.Add(CreateMod(phys, 1, 2, 4));
            Dictionary<int, ModTable> ambiguousResolver = CreateResolver(modById, ambiguous);
            failure = ItemEnchantmentRules.ValidateAffixState(ambiguous,
                id => ResolveMod(ambiguousResolver, id), out _, out _);
            Require(failure == ItemEnchantmentFailureReason.AmbiguousExistingGroup
                && ambiguous.Mods.Count == 4,
                "같은 Slot·Group이 여러 개인 구 데이터는 임의 선택 없이 거부해야 함",
                errors, ref assertions);

            List<ModTierTable> levelOne = ItemEnchantmentRules.GetEligibleTiers(
                FindTiers(tiers, phys.Id), phys.Id, 1, 1);
            Require(levelOne.Count == 1 && levelOne[0].Tier == 1,
                "Player Level 1 / Item Tier 1은 Mod Tier 1만 자격이어야 함",
                errors, ref assertions);

            List<ModTierTable> allEligible = ItemEnchantmentRules.GetEligibleTiers(
                FindTiers(tiers, phys.Id), phys.Id, 60, 3);
            Require(allEligible.Count == 3,
                "Level 60 / Item Tier 3에서 기존 Mod 1100의 3개 Tier가 모두 자격이어야 함",
                errors, ref assertions);
            Require(ItemEnchantmentRules.SelectWeightedTier(allEligible, _ => 0)?.Tier == 1
                && ItemEnchantmentRules.SelectWeightedTier(allEligible, _ => 100)?.Tier == 2
                && ItemEnchantmentRules.SelectWeightedTier(allEligible, _ => 160)?.Tier == 3,
                "ModTier.Weight 누적 경계 선택이 잘못됨", errors, ref assertions);
        }

        private static void VerifyTransactionsAndSaveRoundTrip(
            Dictionary<int, ModTable> modById, List<ModTierTable> tiers,
            List<ItemEnchantmentCostTable> costs, Dictionary<int, ItemTable> itemById,
            List<string> errors, ref int assertions)
        {
            if (RequireMods(modById, errors, ref assertions, 1100, 1101) == false)
                return;
            ItemEnchantmentCostTable? replaceCost = FindCost(costs,
                GlobalEnum.ItemEnchantmentOperation.ReplaceAffix);
            ItemEnchantmentCostTable? addCost = FindCost(costs,
                GlobalEnum.ItemEnchantmentOperation.AddAffix);
            if (replaceCost == null || addCost == null)
                return;

            ModTable phys = modById[1100];
            List<ModTable> modPool = new List<ModTable>(modById.Values);
            modPool.Sort((a, b) => a.Id.CompareTo(b.Id));

            EquipmentData missingEquipment = CreateEquipmentWithImmutableOptions();
            missingEquipment.Mods.Add(CreateMod(phys, 1, 1, 3));
            Inventory missingInventory = CreateInventory();
            PlayerData missingPlayer = new PlayerData { Level = 1, Gold = 100 };
            int missingSaveCalls = 0;
            ItemEnchantmentService missingService = new ItemEnchantmentService(_ => 0,
                () => missingSaveCalls++);
            Dictionary<int, ModTable> missingResolver =
                CreateResolver(modById, missingEquipment);
            string beforeFailure = CaptureState(missingEquipment, missingInventory,
                missingPlayer);
            ItemEnchantmentResult missingResult = missingService.ExecuteForDiagnostics(
                missingEquipment, missingInventory, missingPlayer,
                GlobalEnum.ItemEnchantmentOperation.ReplaceAffix, 1, modPool, tiers,
                replaceCost, id => ResolveMod(missingResolver, id));
            Require(missingResult.Success == false
                && missingResult.FailureReason == ItemEnchantmentFailureReason.MaterialMissing
                && CaptureState(missingEquipment, missingInventory, missingPlayer)
                    == beforeFailure && missingSaveCalls == 0,
                "재료 부족 실패는 옵션·재료·Gold·저장을 모두 변경하지 않아야 함",
                errors, ref assertions);

            EquipmentData poorEquipment = CreateEquipmentWithImmutableOptions();
            Inventory poorInventory = CreateInventory(
                CreateCurrency(1002, 1, itemById));
            PlayerData poorPlayer = new PlayerData { Level = 1, Gold = 39 };
            int poorSaveCalls = 0;
            ItemEnchantmentService poorService = new ItemEnchantmentService(_ => 0,
                () => poorSaveCalls++);
            Dictionary<int, ModTable> poorResolver = CreateResolver(modById, poorEquipment);
            string beforePoor = CaptureState(poorEquipment, poorInventory, poorPlayer);
            ItemEnchantmentResult poorResult = poorService.ExecuteForDiagnostics(
                poorEquipment, poorInventory, poorPlayer,
                GlobalEnum.ItemEnchantmentOperation.AddAffix, 1, modPool, tiers,
                addCost, id => ResolveMod(poorResolver, id));
            Require(poorResult.Success == false
                && poorResult.FailureReason == ItemEnchantmentFailureReason.GoldInsufficient
                && CaptureState(poorEquipment, poorInventory, poorPlayer) == beforePoor
                && poorSaveCalls == 0,
                "Gold 부족 실패는 옵션·재료·Gold·저장을 모두 변경하지 않아야 함",
                errors, ref assertions);

            EquipmentData replaceEquipment = CreateEquipmentWithImmutableOptions();
            replaceEquipment.Mods.Add(CreateMod(phys, 1, 1, 3));
            replaceEquipment.WeaponLocalEnhancements.Add(
                new WeaponLocalEnhancementInstance
                {
                    StatType = GlobalEnum.WeaponLocalStatType.PhysicsDamage,
                    Value = 12,
                });
            string immutableBefore = CaptureImmutableOptions(replaceEquipment);
            Inventory replaceInventory = CreateInventory(
                CreateCurrency(1001, 2, itemById));
            PlayerData replacePlayer = new PlayerData { Level = 1, Gold = 100 };
            int replaceSaveCalls = 0;
            ItemEnchantmentService replaceService = new ItemEnchantmentService(
                maxExclusive => maxExclusive - 1, () => replaceSaveCalls++);
            Dictionary<int, ModTable> replaceResolver =
                CreateResolver(modById, replaceEquipment);
            ItemEnchantmentResult replaceResult = replaceService.ExecuteForDiagnostics(
                replaceEquipment, replaceInventory, replacePlayer,
                GlobalEnum.ItemEnchantmentOperation.ReplaceAffix, 1, modPool, tiers,
                replaceCost, id => ResolveMod(replaceResolver, id));
            Require(replaceResult.Success == true && replaceResult.SaveRequested == true
                && replaceResult.ResolvedOperation
                    == GlobalEnum.ItemEnchantmentOperation.ReplaceAffix
                && replaceResult.ConsumedMaterialItemId == 1001
                && replaceResult.ConsumedMaterialCount == 1
                && replaceResult.ConsumedGold == 25
                && replaceInventory.GetTotalQuantity(1001) == 1
                && replacePlayer.Gold == 75 && replaceSaveCalls == 1,
                "Tier 1 교체는 변화의 오브 1개 + 25 Gold를 소비하고 저장 1회여야 함",
                errors, ref assertions);
            Require(replaceResult.BeforeOptions.Count == 3
                && replaceResult.AfterOptions.Count == 3
                && replaceEquipment.Mods.Count == 3
                && replaceResult.ReplacedMod?.ModTableId == phys.Id
                && replaceResult.AppliedMod?.ModTableId != phys.Id
                && replaceResult.AppliedMod?.Slot == GlobalEnum.ModSlot.Prefix
                && replaceResult.AppliedMod?.Tier == 1
                && replaceEquipment.Mods[2].ModTableId
                    == replaceResult.AppliedMod.ModTableId,
                "변화의 오브는 기존 옵션 하나를 같은 슬롯의 다른 랜덤 옵션으로 교체해야 함",
                errors, ref assertions);
            Require(CaptureImmutableOptions(replaceEquipment) == immutableBefore,
                "교체가 Base·Implicit·WeaponLocalEnhancements를 변경함",
                errors, ref assertions);

            EquipmentData addEquipment = CreateEquipmentWithImmutableOptions();
            string addImmutableBefore = CaptureImmutableOptions(addEquipment);
            Inventory addInventory = CreateInventory(
                CreateCurrency(1002, 2, itemById));
            PlayerData addPlayer = new PlayerData { Level = 1, Gold = 100 };
            int addSaveCalls = 0;
            ItemEnchantmentService addService = new ItemEnchantmentService(
                maxExclusive => maxExclusive - 1, () => addSaveCalls++);
            Dictionary<int, ModTable> addResolver = CreateResolver(modById, addEquipment);
            ItemEnchantmentResult addResult = addService.ExecuteForDiagnostics(
                addEquipment, addInventory, addPlayer,
                GlobalEnum.ItemEnchantmentOperation.AddAffix, 1, modPool, tiers,
                addCost, id => ResolveMod(addResolver, id));
            Require(addResult.Success == true && addResult.SaveRequested == true
                && addResult.ResolvedOperation == GlobalEnum.ItemEnchantmentOperation.AddAffix
                && addResult.ConsumedMaterialItemId == 1002
                && addResult.ConsumedMaterialCount == 1 && addResult.ConsumedGold == 40
                && addInventory.GetTotalQuantity(1002) == 1
                && addPlayer.Gold == 60 && addSaveCalls == 1,
                "Tier 1 추가는 엑잘티드 오브 1개 + 40 Gold를 소비하고 저장 1회여야 함",
                errors, ref assertions);
            Require(addResult.BeforeOptions.Count == 2 && addResult.AfterOptions.Count == 3
                && addEquipment.Mods.Count == 3 && addResult.AppliedMod != null
                && addEquipment.Mods[2].ModTableId == addResult.AppliedMod.ModTableId,
                "엑잘티드 오브는 유효 후보 중 랜덤 옵션 하나를 추가해야 함",
                errors, ref assertions);
            Require(CaptureImmutableOptions(addEquipment) == addImmutableBefore,
                "신규 추가가 Base·Implicit·WeaponLocalEnhancements를 변경함",
                errors, ref assertions);

            ItemData persistedEquipment = new ItemData
            {
                Id = 1,
                ItemInstanceId = 7001,
                Quantity = 1,
                Equipment = addEquipment,
                Table = itemById.TryGetValue(1, out ItemTable weaponItem)
                    ? weaponItem : null,
            };
            PlayerData savedPlayer = new PlayerData
            {
                Level = 1,
                Gold = addPlayer.Gold,
                _inventory = new List<ItemData?>
                {
                    persistedEquipment,
                    CreateCurrency(1002, addInventory.GetTotalQuantity(1002), itemById),
                },
            };
            string json = JsonConvert.SerializeObject(savedPlayer, Formatting.None,
                JsonSettings.Default);
            PlayerData? loaded = JsonConvert.DeserializeObject<PlayerData>(json,
                JsonSettings.Default);
            Require(loaded != null && loaded.Gold == 60 && loaded._inventory.Count == 2
                && loaded._inventory[0]?.Equipment?.Mods.Count == 3
                && loaded._inventory[0]!.Equipment!.Mods[2].ModTableId
                    == addResult.AppliedMod!.ModTableId
                && loaded._inventory[0]!.Equipment!.Mods[2].Tier == 1
                && loaded._inventory[1]?.Id == 1002
                && loaded._inventory[1]?.Quantity == 1,
                "마법부여 결과·재료·Gold JSON 저장 왕복이 유지되지 않음",
                errors, ref assertions);
        }

        private static int VerifyVillageForgeGuarantee(
            Dictionary<int, BuildableItemTable> buildableById,
            List<string> errors, ref int assertions)
        {
            BuildableItemTable? ResolveBuildable(int id)
            {
                return buildableById.TryGetValue(id, out BuildableItemTable table)
                    ? table : null;
            }

            int caseCount = 0;
            HashSet<GlobalEnum.RaceType> races = new HashSet<GlobalEnum.RaceType>();
            IReadOnlyList<VillageProfile> profiles = VillageProfileCatalog.AllProfiles;
            for (int profileIndex = 0; profileIndex < profiles.Count; profileIndex++)
            {
                VillageProfile profile = profiles[profileIndex];
                races.Add(profile.Race);
                for (int stageValue = (int)VillageStage.Settlement;
                    stageValue <= (int)VillageStage.City; stageValue++)
                {
                    VillageStage stage = (VillageStage)stageValue;
                    VillageData village = new VillageData(10000 + caseCount, Vector2.zero)
                    {
                        VillageProfileId = profile.ProfileId,
                        Stage = stage,
                    };
                    VillageGrowthPlan plan = VillageInitialDevelopment.ResolvePlan(profile,
                        out string warning);
                    VillageInitialDevelopmentResult milestones =
                        VillageInitialDevelopment.ApplyInitialMilestones(village, plan, stage,
                            ResolveBuildable);
                    Require(milestones.HasErrors == false,
                        $"{profile.ProfileId}/{stage} 초기 마일스톤 오류: " +
                        string.Join(" | ", milestones.Errors), errors, ref assertions);

                    MinimumForgeEnsureResult ensure =
                        VillageInitialDevelopment.EnsureMinimumForge(village,
                            ResolveBuildable, out string ensureError);
                    Require(ensure != MinimumForgeEnsureResult.DataError,
                        $"{profile.ProfileId}/{stage} Forge 보장 실패: {ensureError}",
                        errors, ref assertions);
                    int expectedTier = 1;
                    if (profile.PrimarySpecialFacilityId
                        == VillageInitialFacilityCatalog.FORGE_TABLE_ID)
                    {
                        if (stage == VillageStage.Town)
                            expectedTier = 2;
                        else if (stage == VillageStage.City)
                            expectedTier = 3;
                    }
                    RequireForgeState(village, expectedTier,
                        $"{profile.ProfileId}/{stage}", errors, ref assertions);

                    MinimumForgeEnsureResult repeated =
                        VillageInitialDevelopment.EnsureMinimumForge(village,
                            ResolveBuildable, out string repeatedError);
                    Require(repeated == MinimumForgeEnsureResult.Unchanged,
                        $"{profile.ProfileId}/{stage} 반복 보장이 멱등이 아님: " +
                        $"{repeated}/{repeatedError}", errors, ref assertions);
                    RequireForgeState(village, expectedTier,
                        $"{profile.ProfileId}/{stage}/repeat", errors, ref assertions);
                    caseCount++;
                }
            }

            Require(races.Count == Enum.GetValues(typeof(GlobalEnum.RaceType)).Length,
                "VillageProfileCatalog가 모든 Race를 포함하지 않음",
                errors, ref assertions);

            VillageData baseTown = new VillageData(90001, Vector2.zero)
            {
                VillageProfileId = VillageProfileCatalog.BASE_TOWN_PROFILE_ID,
                Stage = VillageStage.City,
                SettlementId = -1,
            };
            MinimumForgeEnsureResult baseTownResult =
                VillageInitialDevelopment.EnsureMinimumForge(baseTown, ResolveBuildable,
                    out string baseTownError);
            Require(baseTownResult == MinimumForgeEnsureResult.Created,
                $"BaseTown Forge 생성 실패: {baseTownResult}/{baseTownError}",
                errors, ref assertions);
            RequireForgeState(baseTown, 1, "BaseTown", errors, ref assertions);
            caseCount++;

            for (int preservedTier = 2; preservedTier <= 3; preservedTier++)
            {
                VillageData existing = new VillageData(91000 + preservedTier, Vector2.zero);
                AddForge(existing, ResolveBuildable, preservedTier);
                MinimumForgeEnsureResult result =
                    VillageInitialDevelopment.EnsureMinimumForge(existing,
                        ResolveBuildable, out string existingError);
                Require(result == MinimumForgeEnsureResult.Unchanged,
                    $"기존 Tier {preservedTier} Forge 처리 오류: {result}/{existingError}",
                    errors, ref assertions);
                RequireForgeState(existing, preservedTier,
                    $"preserve-tier-{preservedTier}", errors, ref assertions);
                caseCount++;
            }

            VillageData legacyTierZero = new VillageData(92000, Vector2.zero);
            AddForge(legacyTierZero, ResolveBuildable, 0);
            MinimumForgeEnsureResult normalized =
                VillageInitialDevelopment.EnsureMinimumForge(legacyTierZero,
                    ResolveBuildable, out string normalizedError);
            Require(normalized == MinimumForgeEnsureResult.TierNormalized,
                $"Tier 0 Forge 최소 Tier 정규화 실패: {normalized}/{normalizedError}",
                errors, ref assertions);
            RequireForgeState(legacyTierZero, 1, "normalize-tier-zero",
                errors, ref assertions);
            caseCount++;

            VillageData duplicate = new VillageData(93000, Vector2.zero);
            AddForge(duplicate, ResolveBuildable, 2);
            AddForge(duplicate, ResolveBuildable, 3);
            MinimumForgeEnsureResult duplicateResult =
                VillageInitialDevelopment.EnsureMinimumForge(duplicate,
                    ResolveBuildable, out string duplicateError);
            Require(duplicateResult == MinimumForgeEnsureResult.DataError
                && string.IsNullOrEmpty(duplicateError) == false
                && CountForgeRecords(duplicate) == 2 && CountForgeTypeIds(duplicate) == 2,
                "중복 Forge 데이터는 임의 선택/삭제 없이 오류로 거부해야 함",
                errors, ref assertions);
            caseCount++;

            return caseCount;
        }

        private static void RequireCurveMatches(List<ModTierTable> tiers, int sourceModId,
            int targetModId, List<string> errors, ref int assertions)
        {
            List<ModTierTable> source = FindTiers(tiers, sourceModId);
            List<ModTierTable> target = FindTiers(tiers, targetModId);
            bool matches = source.Count == 3 && target.Count == 3;
            if (matches == true)
            {
                for (int i = 0; i < source.Count; i++)
                {
                    ModTierTable a = source[i];
                    ModTierTable b = target[i];
                    if (a.Tier != b.Tier || a.Min1 != b.Min1 || a.Max1 != b.Max1
                        || a.Min2 != b.Min2 || a.Max2 != b.Max2
                        || a.RequiredLevel != b.RequiredLevel || a.Weight != b.Weight)
                    {
                        matches = false;
                        break;
                    }
                }
            }
            Require(matches, $"Mod {targetModId}의 3개 Tier가 Mod {sourceModId} 기존 곡선과 다름",
                errors, ref assertions);
        }

        private static void RequireTierCount(List<ModTierTable> tiers, int modId,
            int expectedCount, List<string> errors, ref int assertions)
        {
            List<ModTierTable> found = FindTiers(tiers, modId);
            Require(found.Count == expectedCount,
                $"Mod {modId}의 Tier 수가 {expectedCount}개가 아님: {found.Count}",
                errors, ref assertions);
        }

        private static void RequireModContract(ModTable mod, string name,
            GlobalEnum.ModEffectType effectType, GlobalEnum.ModSlot slot,
            string group, GlobalEnum.DamageType element, GlobalEnum.Stat targetStat,
            GlobalEnum.EquipmentTypeMask allowedEquipTypes,
            List<string> errors, ref int assertions)
        {
            Require(mod.Name == name
                && mod.EffectType == effectType
                && mod.ApplyType == GlobalEnum.ModApplyType.Passive
                && mod.Slot == slot
                && mod.Group == group
                && mod.Element == element
                && mod.Tags == GlobalEnum.SkillTag.None
                && mod.TargetStat == targetStat
                && mod.AllowedEquipTypes == allowedEquipTypes,
                $"Mod {mod.Id} 데이터 계약 불일치: {mod.Name}/{mod.EffectType}/" +
                $"{mod.ApplyType}/{mod.Slot}/{mod.Group}/{mod.Element}/{mod.Tags}/" +
                $"{mod.TargetStat}/{mod.AllowedEquipTypes}",
                errors, ref assertions);
        }

        private static void RequireStandardThreeTierCurve(List<ModTierTable> tiers,
            int modId, List<string> errors, ref int assertions)
        {
            List<ModTierTable> found = FindTiers(tiers, modId);
            bool valid = found.Count == 3;
            int[] requiredLevels = { 1, 20, 40 };
            int[] weights = { 100, 60, 30 };
            if (valid == true)
            {
                for (int i = 0; i < found.Count; i++)
                {
                    ModTierTable tier = found[i];
                    if (tier.Tier != i + 1
                        || tier.Min1 <= 0 || tier.Max1 < tier.Min1
                        || tier.Min2 != 0 || tier.Max2 != 0
                        || tier.RequiredLevel != requiredLevels[i]
                        || tier.Weight != weights[i])
                    {
                        valid = false;
                        break;
                    }
                    if (i > 0 && tier.Min1 < found[i - 1].Max1)
                    {
                        valid = false;
                        break;
                    }
                }
            }
            Require(valid, $"Mod {modId}의 T1~T3 값/레벨/가중치 곡선이 올바르지 않음",
                errors, ref assertions);
        }

        private static void RequireVerifiedCandidateCount(
            Dictionary<int, ModTable> modById, List<ModTierTable> tiers,
            GlobalEnum.EquipmentTypeMask equipmentType, GlobalEnum.ModSlot slot,
            int minimumCount, List<string> errors, ref int assertions)
        {
            int count = 0;
            List<ModTable> mods = new List<ModTable>(modById.Values);
            for (int i = 0; i < mods.Count; i++)
            {
                ModTable mod = mods[i];
                if (mod.Slot != slot
                    || Utils.ContainsEquipmentType(mod.AllowedEquipTypes,
                        equipmentType) == false
                    || ItemEnchantmentRules.HasVerifiedRuntimeConsumer(mod) == false
                    || FindTiers(tiers, mod.Id).Count == 0)
                    continue;
                count++;
            }
            Require(count >= minimumCount,
                $"{equipmentType} {slot}의 검증된 후보가 {minimumCount}개 미만: {count}",
                errors, ref assertions);
        }

        private static string BuildWeaponCandidateSignature(
            Dictionary<int, ModTable> modById, List<ModTierTable> tiers,
            int playerLevel, int itemTier)
        {
            List<int> ids = new List<int>();
            List<ModTable> mods = new List<ModTable>(modById.Values);
            mods.Sort((a, b) => a.Id.CompareTo(b.Id));
            for (int i = 0; i < mods.Count; i++)
            {
                ModTable mod = mods[i];
                if ((mod.Slot != GlobalEnum.ModSlot.Prefix
                    && mod.Slot != GlobalEnum.ModSlot.Postfix)
                    || Utils.ContainsEquipmentType(mod.AllowedEquipTypes,
                        GlobalEnum.EquipmentTypeMask.Weapon) == false
                    || ItemEnchantmentRules.HasVerifiedRuntimeConsumer(mod) == false)
                    continue;
                List<ModTierTable> eligible = ItemEnchantmentRules.GetEligibleTiers(
                    FindTiers(tiers, mod.Id), mod.Id, playerLevel, itemTier);
                if (eligible.Count > 0)
                    ids.Add(mod.Id);
            }
            return string.Join(",", ids);
        }

        private static void RequireForgeState(VillageData village, int expectedTier,
            string label, List<string> errors, ref int assertions)
        {
            int tier = -1;
            for (int i = 0; i < village.PlacedObjects.Count; i++)
            {
                if (village.PlacedObjects[i].TableId
                    == VillageInitialFacilityCatalog.FORGE_TABLE_ID)
                    tier = village.PlacedObjects[i].FacilityTier;
            }
            Require(CountForgeRecords(village) == 1 && CountForgeTypeIds(village) == 1
                && tier == expectedTier && tier >= 1,
                $"{label}: Forge는 정확히 1개, Tier {expectedTier}여야 함 " +
                $"(records={CountForgeRecords(village)}, " +
                $"typeIds={CountForgeTypeIds(village)}, tier={tier})",
                errors, ref assertions);
        }

        private static void AddForge(VillageData village,
            Func<int, BuildableItemTable?> resolver, int tier)
        {
            BuildableItemTable? table = resolver(
                VillageInitialFacilityCatalog.FORGE_TABLE_ID);
            if (table == null)
                throw new InvalidOperationException("BuildableItemTable 160 missing");
            VillageConstructionLedger.ApplyCompletedBuild(village, table,
                VillageConstructionPosition.UnassignedTile,
                VillageConstructionPosition.UnassignedTile);
            village.PlacedObjects[village.PlacedObjects.Count - 1].FacilityTier = tier;
        }

        private static int CountForgeRecords(VillageData village)
        {
            int count = 0;
            for (int i = 0; i < village.PlacedObjects.Count; i++)
            {
                if (village.PlacedObjects[i].TableId
                    == VillageInitialFacilityCatalog.FORGE_TABLE_ID)
                    count++;
            }
            return count;
        }

        private static int CountForgeTypeIds(VillageData village)
        {
            int count = 0;
            for (int i = 0; i < village.PlacedObjectTypeIds.Count; i++)
            {
                if (village.PlacedObjectTypeIds[i]
                    == VillageInitialFacilityCatalog.FORGE_TABLE_ID)
                    count++;
            }
            return count;
        }

        private static EquipmentData CreateEquipmentWithImmutableOptions()
        {
            ModTable baseMod = new ModTable
            {
                Id = 990001,
                Name = "진단 Base",
                Slot = GlobalEnum.ModSlot.Base,
                Group = "DiagnosticBase",
                AllowedEquipTypes = GlobalEnum.EquipmentTypeMask.Weapon,
            };
            ModTable implicitMod = new ModTable
            {
                Id = 990002,
                Name = "진단 Implicit",
                Slot = GlobalEnum.ModSlot.Implicit,
                Group = "DiagnosticImplicit",
                AllowedEquipTypes = GlobalEnum.EquipmentTypeMask.Weapon,
            };
            EquipmentData equipment = new EquipmentData
            {
                Id = 1,
                EquipType = GlobalEnum.EquipmentTypeMask.Weapon,
            };
            equipment.Mods.Add(CreateMod(baseMod, 1, 4, 7));
            equipment.Mods.Add(CreateMod(implicitMod, 1, 2, 0));
            return equipment;
        }

        private static ModInstance CreateMod(ModTable table, int tier,
            int value1, int value2)
        {
            return new ModInstance
            {
                ModTableId = table.Id,
                Slot = table.Slot,
                Tier = tier,
                Value1 = (ushort)value1,
                Value2 = (ushort)value2,
                Table = table,
            };
        }

        private static Dictionary<int, ModTable> CreateResolver(
            Dictionary<int, ModTable> source, EquipmentData equipment)
        {
            Dictionary<int, ModTable> resolver = new Dictionary<int, ModTable>(source);
            for (int i = 0; i < equipment.Mods.Count; i++)
            {
                ModInstance mod = equipment.Mods[i];
                if (mod.Table != null)
                    resolver[mod.ModTableId] = mod.Table;
            }
            return resolver;
        }

        private static ModTable? ResolveMod(Dictionary<int, ModTable> resolver, int id)
        {
            return resolver.TryGetValue(id, out ModTable table) ? table : null;
        }

        private static Inventory CreateInventory(params ItemData[] items)
        {
            List<ItemData?> slots = new List<ItemData?>();
            for (int i = 0; i < 8; i++)
                slots.Add(null);
            for (int i = 0; i < items.Length && i < slots.Count; i++)
                slots[i] = items[i];
            Inventory inventory = new Inventory();
            inventory.Initialize(slots, slots.Count);
            return inventory;
        }

        private static ItemData CreateCurrency(int itemId, int quantity,
            Dictionary<int, ItemTable> itemById)
        {
            if (itemById.TryGetValue(itemId, out ItemTable table) == false)
                throw new InvalidOperationException($"ItemTable {itemId} missing");
            return new ItemData
            {
                Id = itemId,
                ItemInstanceId = 800000 + itemId,
                Quantity = quantity,
                Table = table,
            };
        }

        private static string CaptureState(EquipmentData equipment, Inventory inventory,
            PlayerData playerData)
        {
            return JsonConvert.SerializeObject(new
            {
                Equipment = equipment,
                Inventory = inventory.Items,
                playerData.Gold,
            }, Formatting.None, JsonSettings.Default);
        }

        private static string CaptureImmutableOptions(EquipmentData equipment)
        {
            List<ModInstance> immutable = new List<ModInstance>();
            for (int i = 0; i < equipment.Mods.Count; i++)
            {
                if (equipment.Mods[i].Slot == GlobalEnum.ModSlot.Base
                    || equipment.Mods[i].Slot == GlobalEnum.ModSlot.Implicit)
                    immutable.Add(equipment.Mods[i]);
            }
            return JsonConvert.SerializeObject(new
            {
                ImmutableMods = immutable,
                equipment.WeaponLocalEnhancements,
            }, Formatting.None, JsonSettings.Default);
        }

        private static ItemEnchantmentCostTable? FindCost(
            List<ItemEnchantmentCostTable> costs,
            GlobalEnum.ItemEnchantmentOperation operation)
        {
            ItemEnchantmentCostTable? result = null;
            for (int i = 0; i < costs.Count; i++)
            {
                if (costs[i].Operation != operation)
                    continue;
                if (result != null)
                    throw new InvalidOperationException($"Duplicate cost operation {operation}");
                result = costs[i];
            }
            return result;
        }

        private static List<ModTierTable> FindTiers(List<ModTierTable> tiers, int modId)
        {
            List<ModTierTable> result = new List<ModTierTable>();
            for (int i = 0; i < tiers.Count; i++)
            {
                if (tiers[i].ModId == modId)
                    result.Add(tiers[i]);
            }
            result.Sort((a, b) =>
            {
                int tierCompare = a.Tier.CompareTo(b.Tier);
                return tierCompare != 0 ? tierCompare : a.Id.CompareTo(b.Id);
            });
            return result;
        }

        private static bool RequireMods(Dictionary<int, ModTable> modById,
            List<string> errors, ref int assertions, params int[] ids)
        {
            bool foundAll = true;
            for (int i = 0; i < ids.Length; i++)
            {
                bool found = modById.ContainsKey(ids[i]);
                Require(found, $"ModTable {ids[i]} missing", errors, ref assertions);
                if (found == false)
                    foundAll = false;
            }
            return foundAll;
        }

        private static List<T> LoadTable<T>(string assetsPath, string fileName)
        {
            string path = Path.Combine(assetsPath, "_BinaryData", "TableData",
                fileName);
            string json = File.ReadAllText(path);
            List<T>? rows = JsonConvert.DeserializeObject<List<T>>(json,
                JsonSettings.Default);
            return rows ?? throw new InvalidOperationException($"{fileName} deserialize null");
        }

        private static Dictionary<int, T> IndexById<T>(List<T> rows,
            List<string> errors) where T : TableBase
        {
            Dictionary<int, T> result = new Dictionary<int, T>();
            for (int i = 0; i < rows.Count; i++)
            {
                T row = rows[i];
                if (result.TryAdd(row.Id, row) == false)
                    errors.Add($"{typeof(T).Name} duplicate Id {row.Id}");
            }
            return result;
        }

        private static void Require(bool condition, string message,
            List<string> errors, ref int assertions)
        {
            assertions++;
            if (condition == false)
                errors.Add(message);
        }
    }
}
