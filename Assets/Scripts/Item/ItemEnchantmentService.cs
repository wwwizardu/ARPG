#nullable enable
using System;
using System.Collections.Generic;
using ARPG.Component;
using ARPG.Data;
using ARPG.Tables;
using ARPG.Village;
using UnityEngine;

namespace ARPG.Item
{
    public enum ItemEnchantmentFailureReason
    {
        None,
        ForgeNotFound,
        ForgeTierInsufficient,
        TargetNotFound,
        TargetChanged,
        TargetQuantityInvalid,
        NotEquipment,
        ImmutableSlot,
        AmbiguousExistingGroup,
        AffixLimitReached,
        NoAffixAvailable,
        NoRandomCandidate,
        MaterialMissing,
        GoldInsufficient,
        IncompatibleMod,
        EffectNotImplemented,
        DuplicateGroup,
        TierUnavailable,
        UnsupportedOperation,
        CostDataInvalid,
        CommitFailed,
        RequiresNormalGrade,    // 진화의 오브·품질 다시 굴리기는 일반 장비에만 (ITEM_DROP_BALANCE_DESIGN §5·§6.1)
        RequiresAffixedGrade,   // 엑잘티드 오브는 마법·희귀 장비에만 — 일반은 진화의 오브부터 (§6.1)
    }

    public sealed class ItemEnchantmentRequest
    {
        public int ForgeEntityId;
        public int TargetInventorySlot;
        public int TargetItemInstanceId;
        public GlobalEnum.ItemEnchantmentOperation Operation;
    }

    public sealed class EquipmentOptionSnapshot
    {
        public int ModTableId;
        public GlobalEnum.ModSlot Slot;
        public int Tier;
        public ushort Value1;
        public ushort Value2;
        public string DisplayText = string.Empty;
    }

    public sealed class ItemEnchantmentPreview
    {
        public bool CanApply;
        /// <summary>재료와 Gold를 제외한 대상·작업·랜덤 후보가 유효한 상태.</summary>
        public bool IsEligible;
        public ItemEnchantmentFailureReason FailureReason;
        public GlobalEnum.ForgeTier ForgeTier;
        public GlobalEnum.ItemEnchantmentOperation ResolvedOperation;
        public int MaterialItemId;
        public ItemTable? MaterialItem;
        public int RequiredMaterialCount;
        public int OwnedMaterialCount;
        public int BaseGoldCost;
        public int FinalGoldCost;
        public int OwnedGold;
        public int PrefixCount;
        public int PostfixCount;
        public int ReplaceableAffixCount;
        public int RandomCandidateCount;
        public int SlotLimit = ItemEnchantmentRules.AFFIX_LIMIT;
        public GlobalEnum.EquipmentGrade Grade;
        public int CurrentQuality;
        /// <summary>결과 옵션 티어에 마법 규칙(위 2단계 가중치 2배)을 쓰는지.</summary>
        public bool UsesMagicRule;
    }

    public sealed class ItemEnchantmentResult
    {
        public bool Success;
        public bool SaveRequested;
        public ItemEnchantmentFailureReason FailureReason;
        public GlobalEnum.ItemEnchantmentOperation ResolvedOperation;
        public EquipmentOptionSnapshot? ReplacedMod;
        public EquipmentOptionSnapshot? AppliedMod;
        public int ConsumedMaterialItemId;
        public int ConsumedMaterialCount;
        public int ConsumedGold;
        public int PreviousQuality;
        public int NewQuality;
        public readonly List<EquipmentOptionSnapshot> BeforeOptions = new();
        public readonly List<EquipmentOptionSnapshot> AfterOptions = new();
    }

    /// <summary>
    /// 랜덤 교체/추가 후보와 자격 티어를 서비스와 Editor 진단이
    /// 같은 경로로 소비하게 하는 순수 규칙.
    /// </summary>
    public static class ItemEnchantmentRules
    {
        public const int AFFIX_LIMIT = 3;

        /// <summary>
        /// Forge 후보는 장착 후 실제 Stat/공격/전투 경로가 확인된 효과만 노출한다.
        /// 새 효과는 소비 코드를 구현·검증한 뒤 이 allowlist에 명시적으로 추가한다.
        /// </summary>
        public static bool HasVerifiedRuntimeConsumer(ModTable mod)
        {
            if (mod.ApplyType != GlobalEnum.ModApplyType.Passive)
                return false;

            switch (mod.EffectType)
            {
                case GlobalEnum.ModEffectType.AddedPhysDamage:
                case GlobalEnum.ModEffectType.AddedFireDamage:
                case GlobalEnum.ModEffectType.AddedIceDamage:
                case GlobalEnum.ModEffectType.AddedLightningDamage:
                case GlobalEnum.ModEffectType.AddedPoisonDamage:
                    return true;

                case GlobalEnum.ModEffectType.FlatStat:
                    return mod.TargetStat == GlobalEnum.Stat.Hp
                        || mod.TargetStat == GlobalEnum.Stat.Mp
                        || mod.TargetStat == GlobalEnum.Stat.HpGeneration
                        || mod.TargetStat == GlobalEnum.Stat.MpGeneration
                        || mod.TargetStat == GlobalEnum.Stat.Defense
                        || mod.TargetStat == GlobalEnum.Stat.CriDamageMul
                        || mod.TargetStat == GlobalEnum.Stat.FireResist
                        || mod.TargetStat == GlobalEnum.Stat.IceResist
                        || mod.TargetStat == GlobalEnum.Stat.LightningResist
                        || mod.TargetStat == GlobalEnum.Stat.PoisonResist
                        || mod.TargetStat == GlobalEnum.Stat.BloodingRate
                        || mod.TargetStat == GlobalEnum.Stat.IgniteRate
                        // 드롭 보너스 (ITEM_DROP_BALANCE_DESIGN §8.3 ① 묶음) — 증가율 묶음이라 Add·Multiply 모두 %p
                        || mod.TargetStat == GlobalEnum.Stat.ItemQuantity
                        || mod.TargetStat == GlobalEnum.Stat.ItemRarity
                        // 정확도 (PLAYER_LEVEL_CHARM_DESIGN §5.4) — 스킬 공격 프로필이 Accuracy로 소비한다
                        || mod.TargetStat == GlobalEnum.Stat.Accuracy;

                case GlobalEnum.ModEffectType.IncreasedStat:
                    return mod.TargetStat == GlobalEnum.Stat.AttackSpeedMul
                        || mod.TargetStat == GlobalEnum.Stat.CastSpeedMul
                        || mod.TargetStat == GlobalEnum.Stat.CriRateMul
                        || mod.TargetStat == GlobalEnum.Stat.MoveSpeedMul
                        || mod.TargetStat == GlobalEnum.Stat.ItemQuantity
                        || mod.TargetStat == GlobalEnum.Stat.ItemRarity;

                default:
                    return false;
            }
        }

        public static ItemEnchantmentFailureReason ValidateAffixState(
            EquipmentData equipment, Func<int, ModTable?> tableResolver,
            out int prefixCount, out int postfixCount)
        {
            prefixCount = 0;
            postfixCount = 0;
            Dictionary<string, int> groupCounts =
                new Dictionary<string, int>(StringComparer.Ordinal);

            for (int i = 0; i < equipment.Mods.Count; i++)
            {
                ModInstance existing = equipment.Mods[i];
                if (existing.Slot != GlobalEnum.ModSlot.Prefix
                    && existing.Slot != GlobalEnum.ModSlot.Postfix)
                    continue;

                ModTable? table = existing.Table ?? tableResolver(existing.ModTableId);
                if (table == null || table.Slot != existing.Slot)
                    return ItemEnchantmentFailureReason.IncompatibleMod;
                if (string.IsNullOrWhiteSpace(table.Group) == true)
                    return ItemEnchantmentFailureReason.DuplicateGroup;

                if (existing.Slot == GlobalEnum.ModSlot.Prefix)
                    prefixCount++;
                else
                    postfixCount++;

                string key = $"{(int)existing.Slot}:{table.Group}";
                groupCounts.TryGetValue(key, out int count);
                count++;
                groupCounts[key] = count;
                if (count > 1)
                    return ItemEnchantmentFailureReason.AmbiguousExistingGroup;
            }

            if (prefixCount > AFFIX_LIMIT || postfixCount > AFFIX_LIMIT)
                return ItemEnchantmentFailureReason.AffixLimitReached;
            return ItemEnchantmentFailureReason.None;
        }

        /// <summary>
        /// 작업 결과의 등급 기준으로 옵션을 새로 붙일 수 있는 슬롯과 마법 규칙 여부를 정한다 (§6.1).
        /// - 진화: 일반 → 마법. 접두·접미 모두 후보, 마법 규칙.
        /// - 엑잘티드: 마법은 비어 있는 반대쪽 마법 칸을 먼저 채운다(결과도 마법이라 마법 규칙).
        ///   접두 1 + 접미 1인 마법과 희귀는 각 슬롯 상한 3까지, 일반 규칙.
        /// - 변화: 같은 슬롯 교체라 등급이 그대로다. 마법이면 마법 규칙.
        /// </summary>
        public static void ResolveAffixTargets(GlobalEnum.ItemEnchantmentOperation operation,
            int prefixCount, int postfixCount, out bool allowPrefix, out bool allowPostfix, out bool magicRule)
        {
            GlobalEnum.EquipmentGrade grade = EquipmentData.ResolveGrade(prefixCount, postfixCount);
            switch (operation)
            {
                case GlobalEnum.ItemEnchantmentOperation.EvolveToMagic:
                    allowPrefix = true;
                    allowPostfix = true;
                    magicRule = true;
                    return;

                case GlobalEnum.ItemEnchantmentOperation.AddAffix:
                    if (grade == GlobalEnum.EquipmentGrade.Magic && prefixCount + postfixCount == 1)
                    {
                        allowPrefix = prefixCount == 0;
                        allowPostfix = postfixCount == 0;
                        magicRule = true;
                        return;
                    }
                    allowPrefix = prefixCount < AFFIX_LIMIT;
                    allowPostfix = postfixCount < AFFIX_LIMIT;
                    magicRule = false;
                    return;

                default:
                    allowPrefix = true;
                    allowPostfix = true;
                    magicRule = grade == GlobalEnum.EquipmentGrade.Magic;
                    return;
            }
        }

        /// <summary>
        /// 한 Mod의 티어 후보와 가중치. 아이템 레벨 규칙(ItemLevelModTierRules, D9)을 쓰고, 아이템 레벨 0은 1로 본다.
        /// 두 목록은 비운 뒤 채운다. 후보 수를 돌려준다.
        /// </summary>
        public static int BuildEligibleTiers(IReadOnlyList<ModTierTable> tiers, int modId, int itemLevel,
            bool magicRule, List<ModTierTable> outTiers, List<int> outWeights)
        {
            List<ModTierTable> modTiers = new List<ModTierTable>();
            for (int i = 0; i < tiers.Count; i++)
            {
                if (tiers[i].ModId == modId)
                    modTiers.Add(tiers[i]);
            }
            return ItemLevelModTierRules.BuildNormalTierCandidates(modTiers, itemLevel, outTiers, outWeights, magicRule);
        }
    }

    /// <summary>
    /// 서비스의 전체 검증 후 옵션·품질·재료·Gold를 한 번에 커밋하는 메모리 트랜잭션.
    /// </summary>
    public static class ItemEnchantmentTransaction
    {
        public static bool TryCommit(EquipmentData equipment,
            GlobalEnum.ItemEnchantmentOperation operation, int replacementIndex,
            ModInstance? appliedMod, int newQuality, Inventory inventory, PlayerData playerData,
            ItemEnchantmentCostTable cost,
            out ItemEnchantmentFailureReason failureReason)
        {
            failureReason = ItemEnchantmentFailureReason.None;
            bool isQuality = operation == GlobalEnum.ItemEnchantmentOperation.RerollQuality;
            if (operation != GlobalEnum.ItemEnchantmentOperation.ReplaceAffix
                && operation != GlobalEnum.ItemEnchantmentOperation.AddAffix
                && operation != GlobalEnum.ItemEnchantmentOperation.EvolveToMagic
                && isQuality == false)
            {
                failureReason = ItemEnchantmentFailureReason.UnsupportedOperation;
                return false;
            }
            if (cost.Operation != operation || cost.GoldCost < 0
                || (cost.IsGoldOnly == false && (cost.MaterialItemId <= 0 || cost.MaterialCount <= 0))
                || cost.IsGoldOnly != isQuality)
            {
                failureReason = ItemEnchantmentFailureReason.CostDataInvalid;
                return false;
            }

            if (isQuality == true)
            {
                if (equipment.GetGrade() != GlobalEnum.EquipmentGrade.Normal)
                {
                    failureReason = ItemEnchantmentFailureReason.RequiresNormalGrade;
                    return false;
                }
            }
            else
            {
                if (appliedMod == null
                    || (appliedMod.Slot != GlobalEnum.ModSlot.Prefix && appliedMod.Slot != GlobalEnum.ModSlot.Postfix))
                {
                    failureReason = ItemEnchantmentFailureReason.ImmutableSlot;
                    return false;
                }
                if (operation == GlobalEnum.ItemEnchantmentOperation.ReplaceAffix
                    && (replacementIndex < 0 || replacementIndex >= equipment.Mods.Count
                        || equipment.Mods[replacementIndex].Slot != appliedMod.Slot))
                {
                    failureReason = ItemEnchantmentFailureReason.TargetChanged;
                    return false;
                }
                if (operation != GlobalEnum.ItemEnchantmentOperation.ReplaceAffix)
                {
                    int slotCount = 0;
                    for (int i = 0; i < equipment.Mods.Count; i++)
                    {
                        if (equipment.Mods[i].Slot == appliedMod.Slot)
                            slotCount++;
                    }
                    if (slotCount >= ItemEnchantmentRules.AFFIX_LIMIT)
                    {
                        failureReason = ItemEnchantmentFailureReason.AffixLimitReached;
                        return false;
                    }
                }
            }

            if (cost.IsGoldOnly == false
                && inventory.CanConsumeItem(cost.MaterialItemId, cost.MaterialCount) == false)
            {
                failureReason = ItemEnchantmentFailureReason.MaterialMissing;
                return false;
            }
            if (playerData.Gold < cost.GoldCost)
            {
                failureReason = ItemEnchantmentFailureReason.GoldInsufficient;
                return false;
            }
            if (cost.IsGoldOnly == false
                && inventory.TryConsumeItem(cost.MaterialItemId, cost.MaterialCount) == false)
            {
                failureReason = ItemEnchantmentFailureReason.CommitFailed;
                return false;
            }

            // 기본 아이템은 옵션이 바뀌는 순간 일반 아이템이 된다 — 이후 전체 옵션이 저장된다 (D122 안전망).
            equipment.BasicItemId = 0;
            if (isQuality == true)
                equipment.Quality = EquipmentQualityRules.ClampQuality(newQuality);
            else if (operation == GlobalEnum.ItemEnchantmentOperation.ReplaceAffix)
                equipment.Mods[replacementIndex] = appliedMod!;
            else
                equipment.Mods.Add(appliedMod!);
            AR.s?.Data?.MarkPlayerSaveDirty(playerData, PlayerSaveGroup.Progress | PlayerSaveGroup.Inventory | PlayerSaveGroup.Equipment);
            playerData.Gold -= cost.GoldCost;
            equipment.InvalidateWeaponStats();
            return true;
        }
    }

    /// <summary>
    /// Forge UI가 대상과 작업 종류만 넘기면 실제 제련소·장비·랜덤 후보·비용을
    /// 다시 검증하고 실행 시에만 옵션 종류·티어·수치·품질을 롤한다.
    /// 옵션 티어는 장비의 아이템 레벨로 정한다(ITEM_DROP_BALANCE_DESIGN D9). 마법 상태 결과는 마법 규칙을 쓴다.
    /// </summary>
    public sealed class ItemEnchantmentService
    {
        private sealed class RandomModCandidate
        {
            public ModTable Mod = null!;
            public readonly List<ModTierTable> EligibleTiers = new();
            public readonly List<int> TierWeights = new();
        }

        private sealed class ReplacementTarget
        {
            public int ModIndex;
            public readonly List<RandomModCandidate> Candidates = new();
        }

        private readonly Func<int, int> _randomIndex;
        private readonly Action _savePlayer;

        public ItemEnchantmentService(Func<int, int>? randomIndex = null,
            Action? savePlayer = null)
        {
            _randomIndex = randomIndex ??
                (maxExclusive => UnityEngine.Random.Range(0, maxExclusive));
            _savePlayer = savePlayer ?? (() => AR.s.Data.SavePlayer());
        }

        public ItemEnchantmentPreview Inspect(ItemEnchantmentRequest request)
        {
            return InspectInternal(request, null);
        }

        public ItemEnchantmentResult Execute(ItemEnchantmentRequest request)
        {
            return ExecuteInternal(request, null);
        }

        public bool TryResolveForgeTier(int forgeEntityId, out GlobalEnum.ForgeTier tier)
        {
            tier = GlobalEnum.ForgeTier.None;
            if (forgeEntityId < 0
                || AR.s.Component.TryGetComponent<PlacedObjectComponent>(forgeEntityId,
                    out var placed) == false)
                return false;
            if (placed.TableId != VillageInitialFacilityCatalog.FORGE_TABLE_ID
                && (placed.Service & ProvidedService.Forge) == 0)
                return false;

            if (placed.FacilityTier >= 3)
                tier = GlobalEnum.ForgeTier.Premium;
            else if (placed.FacilityTier == 2)
                tier = GlobalEnum.ForgeTier.Standard;
            else if (placed.FacilityTier == 1)
                tier = GlobalEnum.ForgeTier.Basic;
            else
            {
                Vector2Int anchor = new Vector2Int(placed.TileX, placed.TileY);
                if (AR.s.Village.HasObjectSet(placed.VillageId,
                        ObjectSetType.ForgePremium, anchor) == true)
                    tier = GlobalEnum.ForgeTier.Premium;
                else if (AR.s.Village.HasObjectSet(placed.VillageId,
                        ObjectSetType.ForgeStandard, anchor) == true)
                    tier = GlobalEnum.ForgeTier.Standard;
                else if (AR.s.Village.HasObjectSet(placed.VillageId,
                        ObjectSetType.ForgeBasic, anchor) == true)
                    tier = GlobalEnum.ForgeTier.Basic;
            }
            return true;
        }

#if UNITY_EDITOR
        public ItemEnchantmentPreview InspectForTest(ItemEnchantmentRequest request,
            GlobalEnum.ForgeTier forcedTier)
        {
            return InspectInternal(request, forcedTier);
        }

        public ItemEnchantmentResult ExecuteForTest(ItemEnchantmentRequest request,
            GlobalEnum.ForgeTier forcedTier)
        {
            return ExecuteInternal(request, forcedTier);
        }

        /// <summary>
        /// 실제 랜덤 선택·커밋·저장 콜백 경로를 AR singleton 없이 검증한다. itemLevel은 장비의 아이템 레벨 자리다.
        /// </summary>
        public ItemEnchantmentResult ExecuteForDiagnostics(EquipmentData equipment,
            Inventory inventory, PlayerData playerData,
            GlobalEnum.ItemEnchantmentOperation operation, int itemLevel,
            IReadOnlyList<ModTable> modPool, IReadOnlyList<ModTierTable> tiers,
            ItemEnchantmentCostTable cost, Func<int, ModTable?> tableResolver)
        {
            return ExecuteValidated(equipment, inventory, playerData, operation,
                itemLevel, modPool, tiers, cost, tableResolver);
        }
#endif

        public List<EquipmentOptionSnapshot> GetOptionSnapshots(ItemData? item)
        {
            if (item == null || item.Equipment == null)
                return new List<EquipmentOptionSnapshot>();
            return CreateOptionSnapshots(item.Equipment, AR.s.Data.GetMod);
        }

        public static List<EquipmentOptionSnapshot> CreateOptionSnapshots(
            EquipmentData equipment, Func<int, ModTable?> tableResolver)
        {
            List<EquipmentOptionSnapshot> snapshots = new List<EquipmentOptionSnapshot>();
            for (int i = 0; i < equipment.Mods.Count; i++)
            {
                ModInstance mod = equipment.Mods[i];
                mod.Table ??= tableResolver(mod.ModTableId);
                snapshots.Add(CreateSnapshot(mod));
            }
            return snapshots;
        }

        private ItemEnchantmentPreview InspectInternal(ItemEnchantmentRequest request,
            GlobalEnum.ForgeTier? forcedTier)
        {
            ItemEnchantmentPreview preview = new ItemEnchantmentPreview
            {
                ResolvedOperation = request.Operation,
            };
            if (IsSupportedOperation(request.Operation) == false)
            {
                preview.FailureReason = ItemEnchantmentFailureReason.UnsupportedOperation;
                return preview;
            }

            GlobalEnum.ForgeTier forgeTier;
            if (forcedTier.HasValue == true)
                forgeTier = forcedTier.Value;
            else if (TryResolveForgeTier(request.ForgeEntityId, out forgeTier) == false)
            {
                preview.FailureReason = ItemEnchantmentFailureReason.ForgeNotFound;
                return preview;
            }
            preview.ForgeTier = forgeTier;
            if (forgeTier < GlobalEnum.ForgeTier.Basic)
            {
                preview.FailureReason = ItemEnchantmentFailureReason.ForgeTierInsufficient;
                return preview;
            }

            AR? runtime = AR.s;
            if (runtime == null || runtime.Data == null)
            {
                preview.FailureReason = ItemEnchantmentFailureReason.TargetNotFound;
                return preview;
            }
            Inventory? inventory = runtime.Player?.Inventory;
            PlayerData? playerData = runtime.Data.Player;
            if (inventory == null || playerData == null)
            {
                preview.FailureReason = ItemEnchantmentFailureReason.TargetNotFound;
                return preview;
            }

            ItemData? targetItem = inventory.GetItemBySlotIndex(request.TargetInventorySlot);
            ItemEnchantmentFailureReason targetFailure = ValidateTarget(targetItem, request);
            if (targetFailure != ItemEnchantmentFailureReason.None)
            {
                preview.FailureReason = targetFailure;
                return preview;
            }

            ItemEnchantmentCostTable? cost = runtime.Data.GetItemEnchantmentCost(
                request.Operation);
            if (cost == null || cost.IsValid == false
                || (cost.IsGoldOnly == false && cost.MaterialItem == null)
                || cost.Operation != request.Operation)
            {
                preview.FailureReason = ItemEnchantmentFailureReason.CostDataInvalid;
                return preview;
            }
            preview.MaterialItemId = cost.MaterialItemId;
            preview.MaterialItem = cost.MaterialItem;
            preview.RequiredMaterialCount = cost.MaterialCount;
            preview.BaseGoldCost = cost.GoldCost;
            preview.FinalGoldCost = cost.GoldCost;
            preview.OwnedMaterialCount = cost.IsGoldOnly == true
                ? 0 : inventory.GetTotalQuantity(cost.MaterialItemId);
            preview.OwnedGold = playerData.Gold;
            if (forgeTier < cost.RequiredForgeTier)
            {
                preview.FailureReason = ItemEnchantmentFailureReason.ForgeTierInsufficient;
                return preview;
            }

            EquipmentData equipment = targetItem!.Equipment!;
            ItemEnchantmentFailureReason stateFailure =
                ItemEnchantmentRules.ValidateAffixState(equipment, runtime.Data.GetMod,
                    out int prefixCount, out int postfixCount);
            preview.PrefixCount = prefixCount;
            preview.PostfixCount = postfixCount;
            preview.Grade = EquipmentData.ResolveGrade(prefixCount, postfixCount);
            preview.CurrentQuality = equipment.Quality;
            if (stateFailure != ItemEnchantmentFailureReason.None)
            {
                preview.FailureReason = stateFailure;
                return preview;
            }

            ItemEnchantmentFailureReason gradeFailure = ValidateGrade(request.Operation, preview.Grade);
            if (gradeFailure != ItemEnchantmentFailureReason.None)
            {
                preview.FailureReason = gradeFailure;
                return preview;
            }

            if (request.Operation != GlobalEnum.ItemEnchantmentOperation.RerollQuality)
            {
                ItemEnchantmentRules.ResolveAffixTargets(request.Operation, prefixCount, postfixCount,
                    out bool allowPrefix, out bool allowPostfix, out bool magicRule);
                preview.UsesMagicRule = magicRule;
                List<ModTable> modPool = GetRuntimeModPool(equipment);
                List<ModTierTable> tiers = GetRuntimeTiers(modPool);
                if (request.Operation == GlobalEnum.ItemEnchantmentOperation.ReplaceAffix)
                {
                    List<ReplacementTarget> targets = BuildReplacementTargets(equipment,
                        modPool, tiers, equipment.ItemLevel, magicRule, runtime.Data.GetMod);
                    preview.ReplaceableAffixCount = targets.Count;
                    preview.RandomCandidateCount = CountReplacementCandidates(targets);
                    if (prefixCount + postfixCount == 0)
                    {
                        preview.FailureReason = ItemEnchantmentFailureReason.NoAffixAvailable;
                        return preview;
                    }
                    if (targets.Count == 0)
                    {
                        preview.FailureReason = ItemEnchantmentFailureReason.NoRandomCandidate;
                        return preview;
                    }
                }
                else
                {
                    if (allowPrefix == false && allowPostfix == false)
                    {
                        preview.FailureReason = ItemEnchantmentFailureReason.AffixLimitReached;
                        return preview;
                    }
                    List<RandomModCandidate> candidates = BuildAdditionCandidates(equipment,
                        modPool, tiers, equipment.ItemLevel, magicRule, runtime.Data.GetMod,
                        allowPrefix, allowPostfix);
                    preview.RandomCandidateCount = candidates.Count;
                    if (candidates.Count == 0)
                    {
                        preview.FailureReason = ItemEnchantmentFailureReason.NoRandomCandidate;
                        return preview;
                    }
                }
            }

            preview.IsEligible = true;
            if (cost.IsGoldOnly == false && preview.OwnedMaterialCount < cost.MaterialCount)
            {
                preview.FailureReason = ItemEnchantmentFailureReason.MaterialMissing;
                return preview;
            }
            if (playerData.Gold < cost.GoldCost)
            {
                preview.FailureReason = ItemEnchantmentFailureReason.GoldInsufficient;
                return preview;
            }

            preview.FailureReason = ItemEnchantmentFailureReason.None;
            preview.CanApply = true;
            return preview;
        }

        private ItemEnchantmentResult ExecuteInternal(ItemEnchantmentRequest request,
            GlobalEnum.ForgeTier? forcedTier)
        {
            ItemEnchantmentPreview preview = InspectInternal(request, forcedTier);
            if (preview.CanApply == false)
                return Failure(preview.FailureReason);

            Inventory inventory = AR.s.Player.Inventory;
            PlayerData playerData = AR.s.Data.Player;
            ItemData? targetItem = inventory.GetItemBySlotIndex(request.TargetInventorySlot);
            ItemEnchantmentCostTable? cost = AR.s.Data.GetItemEnchantmentCost(
                request.Operation);
            if (targetItem == null || targetItem.Equipment == null || targetItem.Table == null
                || targetItem.ItemInstanceId != request.TargetItemInstanceId || cost == null)
                return Failure(ItemEnchantmentFailureReason.TargetChanged);

            List<ModTable> modPool = GetRuntimeModPool(targetItem.Equipment);
            List<ModTierTable> tiers = GetRuntimeTiers(modPool);
            return ExecuteValidated(targetItem.Equipment, inventory, playerData,
                request.Operation, targetItem.Equipment.ItemLevel, modPool, tiers, cost,
                AR.s.Data.GetMod);
        }

        private ItemEnchantmentResult ExecuteValidated(EquipmentData equipment,
            Inventory inventory, PlayerData playerData,
            GlobalEnum.ItemEnchantmentOperation operation, int itemLevel,
            IReadOnlyList<ModTable> modPool, IReadOnlyList<ModTierTable> tiers,
            ItemEnchantmentCostTable cost, Func<int, ModTable?> tableResolver)
        {
            if (IsSupportedOperation(operation) == false)
                return Failure(ItemEnchantmentFailureReason.UnsupportedOperation);
            bool isQuality = operation == GlobalEnum.ItemEnchantmentOperation.RerollQuality;
            if (cost.Operation != operation || cost.GoldCost < 0 || cost.IsGoldOnly != isQuality
                || (cost.IsGoldOnly == false && (cost.MaterialItemId <= 0 || cost.MaterialCount <= 0)))
                return Failure(ItemEnchantmentFailureReason.CostDataInvalid);

            ItemEnchantmentFailureReason stateFailure =
                ItemEnchantmentRules.ValidateAffixState(equipment, tableResolver,
                    out int prefixCount, out int postfixCount);
            if (stateFailure != ItemEnchantmentFailureReason.None)
                return Failure(stateFailure);
            ItemEnchantmentFailureReason gradeFailure = ValidateGrade(operation,
                EquipmentData.ResolveGrade(prefixCount, postfixCount));
            if (gradeFailure != ItemEnchantmentFailureReason.None)
                return Failure(gradeFailure);

            List<EquipmentOptionSnapshot> before = CreateOptionSnapshots(
                equipment, tableResolver);
            int previousQuality = equipment.Quality;
            int replacementIndex = -1;
            ModInstance? appliedMod = null;
            int newQuality = previousQuality;
            if (isQuality == true)
            {
                // 품질 다시 굴리기: 0~20에서 균등하게 새로 정한다. 지금보다 낮아질 수 있다 (§5)
                newQuality = EquipmentQualityRules.MIN_QUALITY
                    + _randomIndex(EquipmentQualityRules.MAX_QUALITY - EquipmentQualityRules.MIN_QUALITY + 1);
            }
            else
            {
                ItemEnchantmentRules.ResolveAffixTargets(operation, prefixCount, postfixCount,
                    out bool allowPrefix, out bool allowPostfix, out bool magicRule);
                RandomModCandidate? selectedCandidate;
                if (operation == GlobalEnum.ItemEnchantmentOperation.ReplaceAffix)
                {
                    List<ReplacementTarget> targets = BuildReplacementTargets(equipment,
                        modPool, tiers, itemLevel, magicRule, tableResolver);
                    if (prefixCount + postfixCount == 0)
                        return Failure(ItemEnchantmentFailureReason.NoAffixAvailable);
                    ReplacementTarget? target = SelectRandom(targets);
                    if (target == null)
                        return Failure(ItemEnchantmentFailureReason.NoRandomCandidate);
                    replacementIndex = target.ModIndex;
                    selectedCandidate = SelectRandom(target.Candidates);
                }
                else
                {
                    if (allowPrefix == false && allowPostfix == false)
                        return Failure(ItemEnchantmentFailureReason.AffixLimitReached);
                    List<RandomModCandidate> candidates = BuildAdditionCandidates(equipment,
                        modPool, tiers, itemLevel, magicRule, tableResolver, allowPrefix, allowPostfix);
                    selectedCandidate = SelectRandom(candidates);
                }
                if (selectedCandidate == null)
                    return Failure(ItemEnchantmentFailureReason.NoRandomCandidate);

                ModTierTable? selectedTier = SelectWeightedTier(selectedCandidate);
                if (selectedTier == null)
                    return Failure(ItemEnchantmentFailureReason.TierUnavailable);

                appliedMod = new ModInstance
                {
                    ModTableId = selectedCandidate.Mod.Id,
                    Slot = selectedCandidate.Mod.Slot,
                    Tier = selectedTier.Tier,
                    Value1 = (ushort)RollInclusive(selectedTier.Min1, selectedTier.Max1),
                    Value2 = (ushort)RollInclusive(selectedTier.Min2, selectedTier.Max2),
                    Table = selectedCandidate.Mod,
                };
            }

            EquipmentOptionSnapshot? replaced = replacementIndex >= 0
                ? CreateSnapshot(equipment.Mods[replacementIndex]) : null;
            if (ItemEnchantmentTransaction.TryCommit(equipment, operation,
                    replacementIndex, appliedMod, newQuality, inventory, playerData, cost,
                    out ItemEnchantmentFailureReason commitFailure) == false)
                return Failure(commitFailure);

            List<EquipmentOptionSnapshot> after = CreateOptionSnapshots(
                equipment, tableResolver);
            ItemEnchantmentResult result = new ItemEnchantmentResult
            {
                Success = true,
                FailureReason = ItemEnchantmentFailureReason.None,
                ResolvedOperation = operation,
                ReplacedMod = replaced,
                AppliedMod = appliedMod != null ? CreateSnapshot(appliedMod) : null,
                ConsumedMaterialItemId = cost.MaterialItemId,
                ConsumedMaterialCount = cost.MaterialCount,
                ConsumedGold = cost.GoldCost,
                PreviousQuality = previousQuality,
                NewQuality = equipment.Quality,
            };
            result.BeforeOptions.AddRange(before);
            result.AfterOptions.AddRange(after);

            try
            {
                _savePlayer();
                result.SaveRequested = true;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[ItemEnchantmentService] SavePlayer request failed: {ex}");
            }
            return result;
        }

        private List<ReplacementTarget> BuildReplacementTargets(
            EquipmentData equipment, IReadOnlyList<ModTable> modPool,
            IReadOnlyList<ModTierTable> tiers, int itemLevel, bool magicRule,
            Func<int, ModTable?> tableResolver)
        {
            List<ReplacementTarget> targets = new List<ReplacementTarget>();
            for (int modIndex = 0; modIndex < equipment.Mods.Count; modIndex++)
            {
                ModInstance existing = equipment.Mods[modIndex];
                if (existing.Slot != GlobalEnum.ModSlot.Prefix
                    && existing.Slot != GlobalEnum.ModSlot.Postfix)
                    continue;

                ReplacementTarget target = new ReplacementTarget { ModIndex = modIndex };
                HashSet<int> addedModIds = new HashSet<int>();
                for (int poolIndex = 0; poolIndex < modPool.Count; poolIndex++)
                {
                    ModTable candidate = modPool[poolIndex];
                    if (candidate.Id == existing.ModTableId || candidate.Slot != existing.Slot
                        || addedModIds.Add(candidate.Id) == false
                        || IsValidRandomMod(candidate, equipment) == false
                        || HasGroupCollision(equipment, candidate, modIndex,
                            tableResolver) == true)
                        continue;

                    RandomModCandidate randomCandidate = new RandomModCandidate
                    {
                        Mod = candidate,
                    };
                    if (ItemEnchantmentRules.BuildEligibleTiers(tiers, candidate.Id, itemLevel, magicRule,
                            randomCandidate.EligibleTiers, randomCandidate.TierWeights) == 0)
                        continue;
                    target.Candidates.Add(randomCandidate);
                }
                if (target.Candidates.Count > 0)
                    targets.Add(target);
            }
            return targets;
        }

        private List<RandomModCandidate> BuildAdditionCandidates(
            EquipmentData equipment, IReadOnlyList<ModTable> modPool,
            IReadOnlyList<ModTierTable> tiers, int itemLevel, bool magicRule,
            Func<int, ModTable?> tableResolver, bool allowPrefix, bool allowPostfix)
        {
            List<RandomModCandidate> result = new List<RandomModCandidate>();
            HashSet<int> addedModIds = new HashSet<int>();
            for (int i = 0; i < modPool.Count; i++)
            {
                ModTable candidate = modPool[i];
                if (addedModIds.Add(candidate.Id) == false
                    || IsValidRandomMod(candidate, equipment) == false)
                    continue;
                if (candidate.Slot == GlobalEnum.ModSlot.Prefix && allowPrefix == false)
                    continue;
                if (candidate.Slot == GlobalEnum.ModSlot.Postfix && allowPostfix == false)
                    continue;
                if (HasGroupCollision(equipment, candidate, -1, tableResolver) == true)
                    continue;

                RandomModCandidate randomCandidate = new RandomModCandidate
                {
                    Mod = candidate,
                };
                if (ItemEnchantmentRules.BuildEligibleTiers(tiers, candidate.Id, itemLevel, magicRule,
                        randomCandidate.EligibleTiers, randomCandidate.TierWeights) == 0)
                    continue;
                result.Add(randomCandidate);
            }
            return result;
        }

        private ModTierTable? SelectWeightedTier(RandomModCandidate candidate)
        {
            int totalWeight = ItemLevelModTierRules.GetTotalWeight(candidate.TierWeights);
            if (totalWeight <= 0)
                return null;
            int roll = _randomIndex(totalWeight);
            if (roll < 0 || roll >= totalWeight)
                return null;
            return candidate.EligibleTiers[ItemLevelModTierRules.SelectWeightedIndex(candidate.TierWeights, roll)];
        }

        private static ItemEnchantmentFailureReason ValidateGrade(
            GlobalEnum.ItemEnchantmentOperation operation, GlobalEnum.EquipmentGrade grade)
        {
            switch (operation)
            {
                case GlobalEnum.ItemEnchantmentOperation.EvolveToMagic:
                case GlobalEnum.ItemEnchantmentOperation.RerollQuality:
                    return grade == GlobalEnum.EquipmentGrade.Normal
                        ? ItemEnchantmentFailureReason.None
                        : ItemEnchantmentFailureReason.RequiresNormalGrade;
                case GlobalEnum.ItemEnchantmentOperation.AddAffix:
                    return grade == GlobalEnum.EquipmentGrade.Normal
                        ? ItemEnchantmentFailureReason.RequiresAffixedGrade
                        : ItemEnchantmentFailureReason.None;
                default:
                    return ItemEnchantmentFailureReason.None;
            }
        }

        private static bool IsValidRandomMod(ModTable mod, EquipmentData equipment)
        {
            return (mod.Slot == GlobalEnum.ModSlot.Prefix
                    || mod.Slot == GlobalEnum.ModSlot.Postfix)
                && string.IsNullOrWhiteSpace(mod.Group) == false
                && Utils.IsSingleEquipmentTypeMask(equipment.EquipType) == true
                && Utils.ContainsEquipmentType(mod.AllowedEquipTypes,
                    equipment.EquipType) == true
                && ItemEnchantmentRules.HasVerifiedRuntimeConsumer(mod) == true;
        }

        private static bool HasGroupCollision(EquipmentData equipment, ModTable candidate,
            int ignoredModIndex, Func<int, ModTable?> tableResolver)
        {
            for (int i = 0; i < equipment.Mods.Count; i++)
            {
                if (i == ignoredModIndex || equipment.Mods[i].Slot != candidate.Slot)
                    continue;
                ModTable? existing = equipment.Mods[i].Table
                    ?? tableResolver(equipment.Mods[i].ModTableId);
                if (existing != null && string.Equals(existing.Group, candidate.Group,
                        StringComparison.Ordinal) == true)
                    return true;
            }
            return false;
        }

        private static List<ModTable> GetRuntimeModPool(EquipmentData equipment)
        {
            List<ModTable> result = AR.s.Data.GetModPool(GlobalEnum.ModSlot.Prefix,
                equipment.EquipType);
            result.AddRange(AR.s.Data.GetModPool(GlobalEnum.ModSlot.Postfix,
                equipment.EquipType));
            return result;
        }

        private static List<ModTierTable> GetRuntimeTiers(IReadOnlyList<ModTable> modPool)
        {
            List<ModTierTable> result = new List<ModTierTable>();
            HashSet<int> visited = new HashSet<int>();
            for (int i = 0; i < modPool.Count; i++)
            {
                if (visited.Add(modPool[i].Id) == true)
                    result.AddRange(AR.s.Data.GetModTiers(modPool[i].Id));
            }
            return result;
        }

        private T? SelectRandom<T>(IReadOnlyList<T> values) where T : class
        {
            if (values.Count == 0)
                return null;
            int index = _randomIndex(values.Count);
            if (index < 0 || index >= values.Count)
                return null;
            return values[index];
        }

        private int RollInclusive(int minInclusive, int maxInclusive)
        {
            if (minInclusive == maxInclusive)
                return minInclusive;
            int length = maxInclusive - minInclusive + 1;
            int index = _randomIndex(length);
            if (index < 0 || index >= length)
                index = Mathf.Clamp(index, 0, length - 1);
            return minInclusive + index;
        }

        private static int CountReplacementCandidates(
            IReadOnlyList<ReplacementTarget> targets)
        {
            int count = 0;
            for (int i = 0; i < targets.Count; i++)
                count += targets[i].Candidates.Count;
            return count;
        }

        private static bool IsSupportedOperation(
            GlobalEnum.ItemEnchantmentOperation operation)
        {
            return operation == GlobalEnum.ItemEnchantmentOperation.ReplaceAffix
                || operation == GlobalEnum.ItemEnchantmentOperation.AddAffix
                || operation == GlobalEnum.ItemEnchantmentOperation.EvolveToMagic
                || operation == GlobalEnum.ItemEnchantmentOperation.RerollQuality;
        }

        private static ItemEnchantmentFailureReason ValidateTarget(ItemData? targetItem,
            ItemEnchantmentRequest request)
        {
            if (targetItem == null)
                return ItemEnchantmentFailureReason.TargetNotFound;
            if (targetItem.ItemInstanceId != request.TargetItemInstanceId)
                return ItemEnchantmentFailureReason.TargetChanged;
            if (targetItem.Quantity != 1)
                return ItemEnchantmentFailureReason.TargetQuantityInvalid;
            if (targetItem.Table == null
                || targetItem.Table.ItemType != GlobalEnum.ItemType.Equipment
                || targetItem.Equipment == null)
                return ItemEnchantmentFailureReason.NotEquipment;
            return ItemEnchantmentFailureReason.None;
        }

        private static EquipmentOptionSnapshot CreateSnapshot(ModInstance mod)
        {
            return new EquipmentOptionSnapshot
            {
                ModTableId = mod.ModTableId,
                Slot = mod.Slot,
                Tier = mod.Tier,
                Value1 = mod.Value1,
                Value2 = mod.Value2,
                DisplayText = EquipmentOptionText.FormatOptionLine(mod),
            };
        }

        private static ItemEnchantmentResult Failure(
            ItemEnchantmentFailureReason failureReason)
        {
            return new ItemEnchantmentResult
            {
                Success = false,
                FailureReason = failureReason,
            };
        }
    }
}
