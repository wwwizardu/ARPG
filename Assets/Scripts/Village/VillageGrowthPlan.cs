#nullable enable
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace ARPG.Village
{
    /// <summary>V3 (VILLAGE_REWORK_PLAN §3.5) 초기 Stage 누적 보상 타입.</summary>
    public enum VillageGrowthRewardType
    {
        AddCoreFacility = 0,
        AddSpecialFacility = 1,
        UpgradeFacility = 2,
        UnlockShopTier = 3,
        ApplyStageCaps = 4,
    }

    public enum VillageFacilityRole
    {
        Core = 0,
        Shop = 1,
        Rest = 2,
        Special = 3,
    }

    /// <summary>
    /// 초기 생성이 허용된 상호작용 시설의 계약 데이터 (V0 분류표 정본, VILLAGE_REWORK_PLAN §5.4/§6).
    /// 여기 없는 TableId는 건물형 비상호작용 오브젝트로 간주하고 초기 생성 검증에서 실패 처리한다.
    /// </summary>
    public sealed class VillageInitialFacilityDefinition
    {
        public int TableId { get; }
        public VillageFacilityRole Role { get; }
        /// <summary>플레이어 상호작용 진입점 식별자.</summary>
        public string InteractionId { get; }
        /// <summary>플레이어에게 표시되는 상호작용 프롬프트 (V0 계약).</summary>
        public string Prompt { get; }
        /// <summary>상호작용으로 실행되는 제공 기능 요약 (검증용 — 비어 있으면 데이터 오류).</summary>
        public string ProvidedFunction { get; }
        /// <summary>ServiceUIRouter가 라우팅하는 서비스 플래그. None이면 라우팅 계약이 테이블 밖(V4)에 있다.</summary>
        public ProvidedService RoutedService { get; }
        /// <summary>true면 BuildableItemTable.ProvidedService에 RoutedService 비트가 있어야 검증 통과.</summary>
        public bool RequiresTableServiceRouting { get; }
        /// <summary>Tier 교체(UpgradeFacility)가 같은 시설로 취급하는 그룹 키. 빈 문자열 = 교체 없음.</summary>
        public string UpgradeGroup { get; }

        public VillageInitialFacilityDefinition(int tableId, VillageFacilityRole role,
            string interactionId, string prompt, string providedFunction,
            ProvidedService routedService, bool requiresTableServiceRouting,
            string upgradeGroup)
        {
            TableId = tableId;
            Role = role;
            InteractionId = interactionId;
            Prompt = prompt;
            ProvidedFunction = providedFunction;
            RoutedService = routedService;
            RequiresTableServiceRouting = requiresTableServiceRouting;
            UpgradeGroup = upgradeGroup ?? string.Empty;
        }
    }

    /// <summary>
    /// V0 분류표에서 InteractiveFacility로 확정된 5종만 초기 생성 후보다.
    /// VillageHub(100)의 UI 라우팅은 V4 범위라 RequiresTableServiceRouting=false로 계약만 보관한다.
    /// </summary>
    public static class VillageInitialFacilityCatalog
    {
        public const int VILLAGE_HUB_TABLE_ID = 100;     // Campfire → VillageHub 전환 기반
        public const int MERCHANT_STALL_TABLE_ID = 151;
        public const int INN_TABLE_ID = 153;             // InnBed → 단일 여관/휴식소 전환 기반
        public const int FORGE_TABLE_ID = 160;           // Furnace → Dwarf 단일 제련소 전환 기반
        public const int SHRINE_TABLE_ID = 170;

        public const string DWARF_FORGE_UPGRADE_GROUP = "dwarf_forge";

        private static readonly Dictionary<int, VillageInitialFacilityDefinition> _byId =
            BuildCatalog();
        private static readonly IReadOnlyList<VillageInitialFacilityDefinition> _all =
            CreateAllList(_byId);

        public static IReadOnlyList<VillageInitialFacilityDefinition> All => _all;

        public static bool TryGet(int tableId,
            out VillageInitialFacilityDefinition definition)
        {
            return _byId.TryGetValue(tableId, out definition!);
        }

        public static bool IsInteractiveFacility(int tableId)
        {
            return _byId.ContainsKey(tableId);
        }

        private static Dictionary<int, VillageInitialFacilityDefinition> BuildCatalog()
        {
            Dictionary<int, VillageInitialFacilityDefinition> byId =
                new Dictionary<int, VillageInitialFacilityDefinition>();

            Add(byId, new VillageInitialFacilityDefinition(
                VILLAGE_HUB_TABLE_ID, VillageFacilityRole.Core,
                "VillageHub", "마을 거점 이용",
                "마을 중심 거점 — 체크포인트·세이브·귀환 중 확정 기능 (UI 라우팅은 V4 구현)",
                ProvidedService.None, false, string.Empty));
            Add(byId, new VillageInitialFacilityDefinition(
                MERCHANT_STALL_TABLE_ID, VillageFacilityRole.Shop,
                "Shop", "거래",
                "상점 UI(UIShopMerchant) — TradeProfile 연결은 V5",
                ProvidedService.Shop, true, string.Empty));
            Add(byId, new VillageInitialFacilityDefinition(
                INN_TABLE_ID, VillageFacilityRole.Rest,
                "Inn", "휴식",
                "여관/휴식소 UI(UIInn) — 휴식·세이브",
                ProvidedService.Inn, true, string.Empty));
            Add(byId, new VillageInitialFacilityDefinition(
                FORGE_TABLE_ID, VillageFacilityRole.Special,
                "Forge", "제련·수리",
                "제련소 UI(UIForge) — Dwarf 주 특수 시설, Tier 교체 대상",
                ProvidedService.Forge, true, DWARF_FORGE_UPGRADE_GROUP));
            Add(byId, new VillageInitialFacilityDefinition(
                SHRINE_TABLE_ID, VillageFacilityRole.Special,
                "Shrine", "기도",
                "제단 UI(UIShrine) — Shrine 콘텐츠가 확정된 Profile 전용",
                ProvidedService.Shrine, true, string.Empty));

            return byId;
        }

        private static IReadOnlyList<VillageInitialFacilityDefinition> CreateAllList(
            Dictionary<int, VillageInitialFacilityDefinition> byId)
        {
            List<VillageInitialFacilityDefinition> all =
                new List<VillageInitialFacilityDefinition>(byId.Values);
            all.Sort((a, b) => a.TableId.CompareTo(b.TableId));
            return new ReadOnlyCollection<VillageInitialFacilityDefinition>(all);
        }

        private static void Add(Dictionary<int, VillageInitialFacilityDefinition> byId,
            VillageInitialFacilityDefinition definition)
        {
            if (byId.ContainsKey(definition.TableId))
                throw new InvalidOperationException(
                    $"Duplicate initial facility TableId: {definition.TableId}");
            byId.Add(definition.TableId, definition);
        }
    }

    /// <summary>
    /// Stage 누적 생성 명세의 단일 항목. 필요도 점수 선택이 아니라 명시적 목록이며,
    /// MilestoneKey가 VillageData.AppliedGrowthMilestones의 중복 방지 정본 키다.
    /// </summary>
    public sealed class VillageGrowthMilestone
    {
        public string MilestoneKey { get; }
        public VillageStage Stage { get; }
        public VillageGrowthRewardType Type { get; }
        /// <summary>시설 보상의 BuildableItemTable Id. 시설 보상이 아니면 0.</summary>
        public int FacilityTableId { get; }
        /// <summary>시설 보상 결과 Tier (Add=1, Upgrade=2/3).</summary>
        public int FacilityTier { get; }
        /// <summary>UnlockShopTier 결과 Tier.</summary>
        public int ShopTier { get; }
        /// <summary>ApplyStageCaps의 이전 Stage 대비 Cap 증가분 (StageBase+ProfileModifier 차분).</summary>
        public int CapAddFood { get; }
        public int CapAddWood { get; }
        public int CapAddStone { get; }

        public VillageGrowthMilestone(VillageStage stage, VillageGrowthRewardType type,
            int facilityTableId, int facilityTier, int shopTier,
            int capAddFood, int capAddWood, int capAddStone)
        {
            Stage = stage;
            Type = type;
            FacilityTableId = facilityTableId;
            FacilityTier = facilityTier;
            ShopTier = shopTier;
            CapAddFood = capAddFood;
            CapAddWood = capAddWood;
            CapAddStone = capAddStone;
            MilestoneKey = CreateKey(stage, type, facilityTableId, facilityTier, shopTier);
        }

        public static string CreateKey(VillageStage stage, VillageGrowthRewardType type,
            int facilityTableId, int facilityTier, int shopTier)
        {
            switch (type)
            {
                case VillageGrowthRewardType.AddCoreFacility:
                case VillageGrowthRewardType.AddSpecialFacility:
                    return $"{stage}:{type}:{facilityTableId}";
                case VillageGrowthRewardType.UpgradeFacility:
                    return $"{stage}:{type}:{facilityTableId}:{facilityTier}";
                case VillageGrowthRewardType.UnlockShopTier:
                    return $"{stage}:{type}:{shopTier}";
                default:
                    return $"{stage}:{type}";
            }
        }
    }

    /// <summary>
    /// 마을 하나가 생성 시 확정하는 누적 생성 명세 (§3.5). Stage 오름차순으로 정렬돼 있어
    /// Settlement→InitialStage 순회만으로 하위 Stage 결과가 빠짐없이 포함된다.
    /// </summary>
    public sealed class VillageGrowthPlan
    {
        public string GrowthPlanId { get; }
        public IReadOnlyList<VillageGrowthMilestone> Milestones { get; }

        public VillageGrowthPlan(string growthPlanId,
            IReadOnlyList<VillageGrowthMilestone> milestones)
        {
            GrowthPlanId = growthPlanId;
            List<VillageGrowthMilestone> copy = new List<VillageGrowthMilestone>();
            if (milestones != null)
            {
                for (int i = 0; i < milestones.Count; i++)
                    copy.Add(milestones[i]);
            }
            Milestones = new ReadOnlyCollection<VillageGrowthMilestone>(copy);
        }
    }

    /// <summary>
    /// V3 성장 명세 카탈로그. VillageStageTable은 Google Sheets에서 재생성되므로
    /// V2 임계값과 같은 정책으로 Stage 기본 Cap·상점 Tier·시설 목록을 코드 상수로 정의한다.
    /// VillageProfileCatalog의 GrowthPlanId별로 명시적 마일스톤 목록을 1회 생성한다.
    /// </summary>
    public static class VillageGrowthPlanCatalog
    {
        // Stage 기본 자원 Cap 초안 (§4.3 최종 Cap = StageBaseCap + ProfileModifier + FacilityBonus).
        // 기존 기본 Cap 50(VillageResourceSimulation.DefaultResourceCap)에서 시작하는 밸런스 초안 —
        // V5 상권/V7 통합 검증에서 조정한다.
        public const int STAGE_BASE_CAP_SETTLEMENT = 50;
        public const int STAGE_BASE_CAP_HAMLET = 75;
        public const int STAGE_BASE_CAP_VILLAGE = 100;
        public const int STAGE_BASE_CAP_TOWN = 150;
        public const int STAGE_BASE_CAP_CITY = 200;

        // 상점 Tier 초안 (§3.3): Settlement 기본 시장 1, Village 지역 프로필 2, Town 확장 3, City 최고 상권 4.
        // 슬롯 수·재고 풀 해금은 V5 TradeProfile이 이 Tier를 소비해 정의한다.
        public const int SHOP_TIER_SETTLEMENT = 1;
        public const int SHOP_TIER_VILLAGE = 2;
        public const int SHOP_TIER_TOWN = 3;
        public const int SHOP_TIER_CITY = 4;

        private static readonly Dictionary<string, VillageGrowthPlan> _byGrowthPlanId =
            BuildCatalog();

        public static bool TryGet(string growthPlanId, out VillageGrowthPlan plan)
        {
            if (string.IsNullOrWhiteSpace(growthPlanId))
            {
                plan = null!;
                return false;
            }
            return _byGrowthPlanId.TryGetValue(growthPlanId, out plan!);
        }

        public static VillageGrowthPlan DefaultPlan =>
            _byGrowthPlanId[VillageProfileCatalog.DefaultProceduralProfile.GrowthPlanId];

        public static IReadOnlyCollection<string> AllGrowthPlanIds => _byGrowthPlanId.Keys;

        public static int GetStageBaseCap(VillageStage stage)
        {
            switch (stage)
            {
                case VillageStage.Settlement: return STAGE_BASE_CAP_SETTLEMENT;
                case VillageStage.Hamlet: return STAGE_BASE_CAP_HAMLET;
                case VillageStage.Village: return STAGE_BASE_CAP_VILLAGE;
                case VillageStage.Town: return STAGE_BASE_CAP_TOWN;
                case VillageStage.City: return STAGE_BASE_CAP_CITY;
                default: return STAGE_BASE_CAP_SETTLEMENT;
            }
        }

        private static Dictionary<string, VillageGrowthPlan> BuildCatalog()
        {
            Dictionary<string, VillageGrowthPlan> byId =
                new Dictionary<string, VillageGrowthPlan>(StringComparer.Ordinal);

            IReadOnlyList<VillageProfile> profiles = VillageProfileCatalog.AllProfiles;
            for (int i = 0; i < profiles.Count; i++)
            {
                VillageProfile profile = profiles[i];
                if (byId.ContainsKey(profile.GrowthPlanId))
                    throw new InvalidOperationException(
                        $"Duplicate GrowthPlanId: {profile.GrowthPlanId}");
                byId.Add(profile.GrowthPlanId, CreatePlan(profile));
            }

            return byId;
        }

        /// <summary>
        /// Profile 데이터(주 특수 시설·보조 시설·Stage Cap 보정)에서 결정론적으로 명시 목록을 만든다.
        /// 같은 Profile은 항상 같은 마일스톤 목록을 얻는다 — RNG·필요도 점수를 사용하지 않는다.
        /// </summary>
        private static VillageGrowthPlan CreatePlan(VillageProfile profile)
        {
            List<VillageGrowthMilestone> milestones = new List<VillageGrowthMilestone>();

            // Settlement (§3.3): 마을 중심 거점 + 기본 시장 + 여관/휴식소.
            // 여관은 원정 부상자가 회복하는 곳이라 가장 작은 마을부터 둔다 (NPC_SIMULATION_DESIGN §3.1 D15, 2026-10-25 Hamlet에서 내림).
            // 마일스톤 적용은 새 게임 때만 돌아서, 키가 바뀌어도 기존 세이브에 여관이 겹쳐 생기지 않는다.
            AddStageCaps(milestones, profile, VillageStage.Settlement);
            AddShopTier(milestones, VillageStage.Settlement, SHOP_TIER_SETTLEMENT);
            milestones.Add(new VillageGrowthMilestone(VillageStage.Settlement,
                VillageGrowthRewardType.AddCoreFacility,
                VillageInitialFacilityCatalog.VILLAGE_HUB_TABLE_ID, 1, 0, 0, 0, 0));
            milestones.Add(new VillageGrowthMilestone(VillageStage.Settlement,
                VillageGrowthRewardType.AddCoreFacility,
                VillageInitialFacilityCatalog.MERCHANT_STALL_TABLE_ID, 1, 0, 0, 0, 0));
            milestones.Add(new VillageGrowthMilestone(VillageStage.Settlement,
                VillageGrowthRewardType.AddCoreFacility,
                VillageInitialFacilityCatalog.INN_TABLE_ID, 1, 0, 0, 0, 0));

            // Hamlet: Cap만 늘린다. (중심부 간격 축소는 V4 Layout 범위)
            AddStageCaps(milestones, profile, VillageStage.Hamlet);

            // Village: 주 특수 시설 Tier 1 + 지역 상점 프로필 활성화.
            AddStageCaps(milestones, profile, VillageStage.Village);
            AddShopTier(milestones, VillageStage.Village, SHOP_TIER_VILLAGE);
            if (profile.PrimarySpecialFacilityId > 0)
            {
                milestones.Add(new VillageGrowthMilestone(VillageStage.Village,
                    VillageGrowthRewardType.AddSpecialFacility,
                    profile.PrimarySpecialFacilityId, 1, 0, 0, 0, 0));
            }

            // Town: 같은 특수 시설 Tier 2 + 선택적 보조 시설 + 상점 확장.
            AddStageCaps(milestones, profile, VillageStage.Town);
            AddShopTier(milestones, VillageStage.Town, SHOP_TIER_TOWN);
            if (profile.PrimarySpecialFacilityId > 0)
            {
                milestones.Add(new VillageGrowthMilestone(VillageStage.Town,
                    VillageGrowthRewardType.UpgradeFacility,
                    profile.PrimarySpecialFacilityId, 2, 0, 0, 0, 0));
            }
            if (profile.SecondaryFacilityIds.Count > 0)
            {
                milestones.Add(new VillageGrowthMilestone(VillageStage.Town,
                    VillageGrowthRewardType.AddSpecialFacility,
                    profile.SecondaryFacilityIds[0], 1, 0, 0, 0, 0));
            }

            // City: 같은 특수 시설 Tier 3 랜드마크 + 도시 역할 시설 + 최고 상권.
            AddStageCaps(milestones, profile, VillageStage.City);
            AddShopTier(milestones, VillageStage.City, SHOP_TIER_CITY);
            if (profile.PrimarySpecialFacilityId > 0)
            {
                milestones.Add(new VillageGrowthMilestone(VillageStage.City,
                    VillageGrowthRewardType.UpgradeFacility,
                    profile.PrimarySpecialFacilityId, 3, 0, 0, 0, 0));
            }
            if (profile.SecondaryFacilityIds.Count > 1)
            {
                milestones.Add(new VillageGrowthMilestone(VillageStage.City,
                    VillageGrowthRewardType.AddSpecialFacility,
                    profile.SecondaryFacilityIds[1], 1, 0, 0, 0, 0));
            }

            return new VillageGrowthPlan(profile.GrowthPlanId, milestones);
        }

        private static void AddShopTier(List<VillageGrowthMilestone> milestones,
            VillageStage stage, int shopTier)
        {
            milestones.Add(new VillageGrowthMilestone(stage,
                VillageGrowthRewardType.UnlockShopTier, 0, 0, shopTier, 0, 0, 0));
        }

        /// <summary>
        /// 이전 Stage 대비 Cap 증가분을 마일스톤에 기록한다. 초기 Stage까지 누적 적용하면
        /// 최종 Cap = 기본 50 + Σ증가분 = StageBaseCap + ProfileModifier가 된다 (§4.3).
        /// </summary>
        private static void AddStageCaps(List<VillageGrowthMilestone> milestones,
            VillageProfile profile, VillageStage stage)
        {
            int previousBase = stage == VillageStage.Settlement
                ? VillageResourceSimulation.DefaultResourceCap
                : GetStageBaseCap(stage - 1);
            int baseDelta = GetStageBaseCap(stage) - previousBase;

            GetProfileCapModifier(profile, stage, out int food, out int wood, out int stone);
            int previousFood = 0;
            int previousWood = 0;
            int previousStone = 0;
            if (stage > VillageStage.Settlement)
            {
                GetProfileCapModifier(profile, stage - 1, out previousFood,
                    out previousWood, out previousStone);
            }

            milestones.Add(new VillageGrowthMilestone(stage,
                VillageGrowthRewardType.ApplyStageCaps, 0, 0, 0,
                baseDelta + food - previousFood,
                baseDelta + wood - previousWood,
                baseDelta + stone - previousStone));
        }

        private static void GetProfileCapModifier(VillageProfile profile,
            VillageStage stage, out int food, out int wood, out int stone)
        {
            food = 0;
            wood = 0;
            stone = 0;
            for (int i = 0; i < profile.StageCapModifiers.Count; i++)
            {
                if (profile.StageCapModifiers[i].Stage != stage)
                    continue;
                food = profile.StageCapModifiers[i].Food;
                wood = profile.StageCapModifiers[i].Wood;
                stone = profile.StageCapModifiers[i].Stone;
                return;
            }
        }
    }
}
