#nullable enable
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace ARPG.Village
{
    public enum VillageSettlementRole
    {
        Standard = 0,
        Capital = 1,
        Frontier = 2,
        TradeHub = 3,
        Conquered = 4,
        BaseTown = 100,
    }

    [Serializable]
    public struct VillageStageCapModifier
    {
        public VillageStage Stage;
        public int Food;
        public int Wood;
        public int Stone;

        public VillageStageCapModifier(VillageStage stage, int food, int wood, int stone)
        {
            Stage = stage;
            Food = food;
            Wood = wood;
            Stone = stone;
        }
    }

    /// <summary>
    /// 마을 성장·배치·상권 데이터의 저장 가능한 연결점.
    /// 프로필 정의는 읽기 전용이며, 마을별 가변 상태는 VillageData가 소유한다.
    /// </summary>
    public sealed class VillageProfile
    {
        public const int MIN_ECONOMIC_POPULATION = 1;
        public const int MAX_ECONOMIC_POPULATION = 25;

        public string ProfileId { get; }
        public GlobalEnum.RaceType Race { get; }
        public GlobalEnum.BiomeType Biome { get; }
        public VillageSettlementRole Role { get; }
        public string HandcraftedVillageKey { get; }
        public string GrowthPlanId { get; }
        public string LayoutProfileId { get; }
        public string TradeProfileId { get; }
        public int PrimarySpecialFacilityId { get; }
        public IReadOnlyList<int> SecondaryFacilityIds { get; }
        public IReadOnlyList<VillageStageCapModifier> StageCapModifiers { get; }
        public VillageStage MaxStage { get; }
        public int DefaultEconomicPopulation { get; }

        public VillageProfile(string profileId, GlobalEnum.RaceType race,
            GlobalEnum.BiomeType biome, VillageSettlementRole role,
            string handcraftedVillageKey, string growthPlanId,
            string layoutProfileId, string tradeProfileId,
            int primarySpecialFacilityId, IReadOnlyList<int> secondaryFacilityIds,
            IReadOnlyList<VillageStageCapModifier> stageCapModifiers,
            VillageStage maxStage, int defaultEconomicPopulation)
        {
            if (string.IsNullOrWhiteSpace(profileId))
                throw new ArgumentException("ProfileId is required.", nameof(profileId));
            if (string.IsNullOrWhiteSpace(growthPlanId))
                throw new ArgumentException("GrowthPlanId is required.", nameof(growthPlanId));
            if (string.IsNullOrWhiteSpace(layoutProfileId))
                throw new ArgumentException("LayoutProfileId is required.", nameof(layoutProfileId));
            if (string.IsNullOrWhiteSpace(tradeProfileId))
                throw new ArgumentException("TradeProfileId is required.", nameof(tradeProfileId));
            if (defaultEconomicPopulation < MIN_ECONOMIC_POPULATION
                || defaultEconomicPopulation > MAX_ECONOMIC_POPULATION)
            {
                throw new ArgumentOutOfRangeException(nameof(defaultEconomicPopulation),
                    $"Economic population must be {MIN_ECONOMIC_POPULATION}~{MAX_ECONOMIC_POPULATION}.");
            }

            ProfileId = profileId;
            Race = race;
            Biome = biome;
            Role = role;
            HandcraftedVillageKey = handcraftedVillageKey ?? string.Empty;
            GrowthPlanId = growthPlanId;
            LayoutProfileId = layoutProfileId;
            TradeProfileId = tradeProfileId;
            PrimarySpecialFacilityId = primarySpecialFacilityId;
            SecondaryFacilityIds = CopySecondaryFacilityIds(secondaryFacilityIds);
            StageCapModifiers = CopyStageCapModifiers(stageCapModifiers);
            MaxStage = maxStage;
            DefaultEconomicPopulation = defaultEconomicPopulation;
        }

        private static IReadOnlyList<int> CopySecondaryFacilityIds(
            IReadOnlyList<int> source)
        {
            List<int> copy = new List<int>();
            if (source != null)
            {
                for (int i = 0; i < source.Count; i++)
                    copy.Add(source[i]);
            }
            return new ReadOnlyCollection<int>(copy);
        }

        private static IReadOnlyList<VillageStageCapModifier> CopyStageCapModifiers(
            IReadOnlyList<VillageStageCapModifier> source)
        {
            List<VillageStageCapModifier> copy = new List<VillageStageCapModifier>();
            if (source != null)
            {
                for (int i = 0; i < source.Count; i++)
                    copy.Add(source[i]);
            }
            return new ReadOnlyCollection<VillageStageCapModifier>(copy);
        }
    }

    public readonly struct VillageProfileContext
    {
        public GlobalEnum.RaceType Race { get; }
        public GlobalEnum.BiomeType Biome { get; }
        public VillageSettlementRole Role { get; }

        public VillageProfileContext(GlobalEnum.RaceType race,
            GlobalEnum.BiomeType biome, VillageSettlementRole role)
        {
            Race = race;
            Biome = biome;
            Role = role;
        }
    }

    public readonly struct VillageProfileResolution
    {
        public VillageProfile Profile { get; }
        public bool UsedFallback { get; }
        public string Warning { get; }

        public VillageProfileResolution(VillageProfile profile, bool usedFallback,
            string warning)
        {
            Profile = profile;
            UsedFallback = usedFallback;
            Warning = warning;
        }
    }

    /// <summary>
    /// V1 프로필 정의 카탈로그. 후속 Phase가 실제 성장·배치·상권 데이터를 추가할 때
    /// 저장된 ID를 바꾸지 않고 각 ID의 정의만 확장한다.
    /// </summary>
    public static class VillageProfileCatalog
    {
        public const string BASE_TOWN_PROFILE_ID = "base_town.dwarf_mining";
        public const string DEFAULT_PROCEDURAL_PROFILE_ID = "village.human.plains.standard";

        private readonly struct RaceBiomePair
        {
            public GlobalEnum.RaceType Race { get; }
            public GlobalEnum.BiomeType Biome { get; }

            public RaceBiomePair(GlobalEnum.RaceType race, GlobalEnum.BiomeType biome)
            {
                Race = race;
                Biome = biome;
            }
        }

        private sealed class CatalogData
        {
            public Dictionary<string, VillageProfile> ById { get; }
            public IReadOnlyList<VillageProfile> All { get; }

            public CatalogData(Dictionary<string, VillageProfile> byId,
                List<VillageProfile> all)
            {
                ById = byId;
                All = new ReadOnlyCollection<VillageProfile>(all);
            }
        }

        private static readonly CatalogData _catalog = BuildCatalog();

        public static IReadOnlyList<VillageProfile> AllProfiles => _catalog.All;

        public static VillageProfile BaseTownProfile =>
            _catalog.ById[BASE_TOWN_PROFILE_ID];

        public static VillageProfile DefaultProceduralProfile =>
            _catalog.ById[DEFAULT_PROCEDURAL_PROFILE_ID];

        public static bool TryGet(string profileId, out VillageProfile profile)
        {
            if (string.IsNullOrWhiteSpace(profileId))
            {
                profile = null!;
                return false;
            }
            return _catalog.ById.TryGetValue(profileId, out profile!);
        }

        public static string CreateProceduralProfileId(GlobalEnum.RaceType race,
            GlobalEnum.BiomeType biome, VillageSettlementRole role)
        {
            return $"village.{race.ToString().ToLowerInvariant()}." +
                $"{biome.ToString().ToLowerInvariant()}." +
                role.ToString().ToLowerInvariant();
        }

        public static bool TryGetDefaultBiome(GlobalEnum.RaceType race,
            out GlobalEnum.BiomeType biome)
        {
            switch (race)
            {
                case GlobalEnum.RaceType.Human:
                    biome = GlobalEnum.BiomeType.Plains;
                    return true;
                case GlobalEnum.RaceType.Elf:
                    biome = GlobalEnum.BiomeType.Forest;
                    return true;
                case GlobalEnum.RaceType.Dwarf:
                    biome = GlobalEnum.BiomeType.Mountain;
                    return true;
                case GlobalEnum.RaceType.Orc:
                    biome = GlobalEnum.BiomeType.Steppe;
                    return true;
                case GlobalEnum.RaceType.Nomad:
                    biome = GlobalEnum.BiomeType.Desert;
                    return true;
                case GlobalEnum.RaceType.Lizardman:
                    biome = GlobalEnum.BiomeType.Swamp;
                    return true;
                case GlobalEnum.RaceType.Yeti:
                    biome = GlobalEnum.BiomeType.Tundra;
                    return true;
                default:
                    biome = GlobalEnum.BiomeType.Plains;
                    return false;
            }
        }

        private static CatalogData BuildCatalog()
        {
            Dictionary<string, VillageProfile> byId =
                new Dictionary<string, VillageProfile>(StringComparer.Ordinal);
            List<VillageProfile> all = new List<VillageProfile>();

            AddProfile(byId, all, new VillageProfile(
                BASE_TOWN_PROFILE_ID,
                GlobalEnum.RaceType.Dwarf,
                GlobalEnum.BiomeType.Mountain,
                VillageSettlementRole.BaseTown,
                "BaseTown",
                "growth.dwarf.mountain.base_town",
                "layout.base_town.dwarf",
                "trade.dwarf.mountain.base_town",
                0,
                Array.Empty<int>(),
                CreateZeroCapModifiers(),
                VillageStage.City,
                1));

            RaceBiomePair[] pairs =
            {
                new RaceBiomePair(GlobalEnum.RaceType.Human, GlobalEnum.BiomeType.Plains),
                new RaceBiomePair(GlobalEnum.RaceType.Human, GlobalEnum.BiomeType.Forest),
                new RaceBiomePair(GlobalEnum.RaceType.Human, GlobalEnum.BiomeType.Desert),
                new RaceBiomePair(GlobalEnum.RaceType.Elf, GlobalEnum.BiomeType.Forest),
                new RaceBiomePair(GlobalEnum.RaceType.Dwarf, GlobalEnum.BiomeType.Mountain),
                new RaceBiomePair(GlobalEnum.RaceType.Dwarf, GlobalEnum.BiomeType.Snow),
                new RaceBiomePair(GlobalEnum.RaceType.Orc, GlobalEnum.BiomeType.Steppe),
                new RaceBiomePair(GlobalEnum.RaceType.Nomad, GlobalEnum.BiomeType.Desert),
                new RaceBiomePair(GlobalEnum.RaceType.Nomad, GlobalEnum.BiomeType.Steppe),
                new RaceBiomePair(GlobalEnum.RaceType.Nomad, GlobalEnum.BiomeType.Tundra),
                new RaceBiomePair(GlobalEnum.RaceType.Lizardman, GlobalEnum.BiomeType.Swamp),
                new RaceBiomePair(GlobalEnum.RaceType.Yeti, GlobalEnum.BiomeType.Tundra),
                new RaceBiomePair(GlobalEnum.RaceType.Yeti, GlobalEnum.BiomeType.Snow),
            };
            VillageSettlementRole[] roles =
            {
                VillageSettlementRole.Standard,
                VillageSettlementRole.Capital,
                VillageSettlementRole.Frontier,
                VillageSettlementRole.TradeHub,
                VillageSettlementRole.Conquered,
            };

            for (int pairIndex = 0; pairIndex < pairs.Length; pairIndex++)
            {
                RaceBiomePair pair = pairs[pairIndex];
                for (int roleIndex = 0; roleIndex < roles.Length; roleIndex++)
                {
                    VillageSettlementRole role = roles[roleIndex];
                    string profileId = CreateProceduralProfileId(pair.Race,
                        pair.Biome, role);
                    string dataKey = $"{pair.Race.ToString().ToLowerInvariant()}." +
                        $"{pair.Biome.ToString().ToLowerInvariant()}." +
                        role.ToString().ToLowerInvariant();
                    AddProfile(byId, all, new VillageProfile(
                        profileId,
                        pair.Race,
                        pair.Biome,
                        role,
                        string.Empty,
                        $"growth.{dataKey}",
                        $"layout.{dataKey}",
                        $"trade.{dataKey}",
                        GetPrimarySpecialFacilityId(pair.Race),
                        Array.Empty<int>(),
                        CreateCapModifiers(role),
                        VillageStage.City,
                        1));
                }
            }

            return new CatalogData(byId, all);
        }

        /// <summary>
        /// 주 특수 시설 ID. V6 사용자 결정: **Dwarf(단일 제련소 160)만 확정**한다.
        /// 다른 Race는 신규 UI/서비스 없이 재사용 가능한 시설 서비스가 Forge·Shrine뿐이고,
        /// "기능 미확정 시설은 외형만 먼저 만들지 않는다" 원칙에 따라 0(없음)으로 남긴다 —
        /// 상권(TradeProfile)과 배치(Layout)로 차별화한다.
        /// 확장점: 신규 특수 시설 UI+BuildableItemTable 행이 확보되면 여기 Race→ID 분기를 추가하고
        /// VillageInitialFacilityCatalog에 상호작용 계약을 등록하면 GrowthPlan이 자동으로 마일스톤을
        /// 생성한다(V3 로직 불변). 신규 UI·시트 행·건물은 이번 범위(V6)에서 만들지 않는다.
        /// </summary>
        private static int GetPrimarySpecialFacilityId(GlobalEnum.RaceType race)
        {
            return race == GlobalEnum.RaceType.Dwarf
                ? VillageInitialFacilityCatalog.FORGE_TABLE_ID
                : 0;
        }

        /// <summary>
        /// V6 역할 결합 규칙 초안 (VILLAGE_REWORK_PLAN V6 체크리스트, "가벼운 실제 효과 + 확장점"):
        /// - Capital: 행정 중심 — Village Stage부터 자원 Cap을 상향한다(아래 데이터, GrowthPlan 자동 반영).
        /// - TradeHub: 상권 강화 — VillageTradeProfileCatalog가 수입품 유입·광물 매입 프리미엄으로 처리.
        /// - Frontier/Conquered: 이번 범위는 확장점만. 실효과(방어 벽·상점 Tier 강등 등)는 후속 Phase.
        ///   같은 Race라도 역할 Profile을 추가·조정할 수 있는 지점이다.
        /// </summary>
        private static VillageStageCapModifier[] CreateCapModifiers(
            VillageSettlementRole role)
        {
            return role == VillageSettlementRole.Capital
                ? CreateCapitalCapModifiers()
                : CreateZeroCapModifiers();
        }

        private static VillageStageCapModifier[] CreateCapitalCapModifiers()
        {
            // Village부터 누적 상향 — GrowthPlan.AddStageCaps가 이전 Stage 대비 차분으로 적용한다.
            return new[]
            {
                new VillageStageCapModifier(VillageStage.Settlement, 0, 0, 0),
                new VillageStageCapModifier(VillageStage.Hamlet, 0, 0, 0),
                new VillageStageCapModifier(VillageStage.Village, 20, 20, 20),
                new VillageStageCapModifier(VillageStage.Town, 35, 35, 35),
                new VillageStageCapModifier(VillageStage.City, 50, 50, 50),
            };
        }

        private static VillageStageCapModifier[] CreateZeroCapModifiers()
        {
            return new[]
            {
                new VillageStageCapModifier(VillageStage.Settlement, 0, 0, 0),
                new VillageStageCapModifier(VillageStage.Hamlet, 0, 0, 0),
                new VillageStageCapModifier(VillageStage.Village, 0, 0, 0),
                new VillageStageCapModifier(VillageStage.Town, 0, 0, 0),
                new VillageStageCapModifier(VillageStage.City, 0, 0, 0),
            };
        }

        private static void AddProfile(Dictionary<string, VillageProfile> byId,
            List<VillageProfile> all, VillageProfile profile)
        {
            if (byId.ContainsKey(profile.ProfileId))
                throw new InvalidOperationException(
                    $"Duplicate VillageProfileId: {profile.ProfileId}");
            byId.Add(profile.ProfileId, profile);
            all.Add(profile);
        }
    }

    public static class VillageProfileResolver
    {
        public static VillageProfileResolution Resolve(VillageProfileContext context)
        {
            if (context.Role == VillageSettlementRole.BaseTown)
                return new VillageProfileResolution(
                    VillageProfileCatalog.BaseTownProfile, false, string.Empty);

            VillageSettlementRole role = context.Role;
            bool invalidRole = Enum.IsDefined(typeof(VillageSettlementRole), role) == false;
            if (invalidRole == true)
                role = VillageSettlementRole.Standard;

            string exactId = VillageProfileCatalog.CreateProceduralProfileId(
                context.Race, context.Biome, role);
            if (VillageProfileCatalog.TryGet(exactId, out VillageProfile exact))
            {
                if (invalidRole == false)
                    return new VillageProfileResolution(exact, false, string.Empty);
                return new VillageProfileResolution(exact, true,
                    $"Unknown settlement role {(int)context.Role}; Standard role was used.");
            }

            string sameBiomeStandardId =
                VillageProfileCatalog.CreateProceduralProfileId(context.Race,
                    context.Biome, VillageSettlementRole.Standard);
            if (VillageProfileCatalog.TryGet(sameBiomeStandardId,
                    out VillageProfile sameBiomeStandard))
            {
                return new VillageProfileResolution(sameBiomeStandard, true,
                    $"Profile {exactId} is missing; {sameBiomeStandardId} was used.");
            }

            if (VillageProfileCatalog.TryGetDefaultBiome(context.Race,
                    out GlobalEnum.BiomeType defaultBiome))
            {
                string raceDefaultId =
                    VillageProfileCatalog.CreateProceduralProfileId(context.Race,
                        defaultBiome, role);
                if (VillageProfileCatalog.TryGet(raceDefaultId,
                        out VillageProfile raceDefault))
                {
                    return new VillageProfileResolution(raceDefault, true,
                        $"Profile {exactId} is missing; race default {raceDefaultId} was used.");
                }

                string raceStandardId =
                    VillageProfileCatalog.CreateProceduralProfileId(context.Race,
                        defaultBiome, VillageSettlementRole.Standard);
                if (VillageProfileCatalog.TryGet(raceStandardId,
                        out VillageProfile raceStandard))
                {
                    return new VillageProfileResolution(raceStandard, true,
                        $"Profile {exactId} is missing; race default {raceStandardId} was used.");
                }
            }

            return new VillageProfileResolution(
                VillageProfileCatalog.DefaultProceduralProfile, true,
                $"Profile {exactId} and its race defaults are missing; " +
                $"{VillageProfileCatalog.DEFAULT_PROCEDURAL_PROFILE_ID} was used.");
        }
    }

    public enum BaseTownPopulationSource
    {
        SavedFixedPopulation = 0,
        LegacyPopulation = 1,
        ProfileDefault = 2,
    }

    public static class VillageProfilePopulationPolicy
    {
        public static int ResolveBaseTownFixedPopulation(int savedFixedPopulation,
            int legacyPopulation, VillageProfile profile,
            out BaseTownPopulationSource source)
        {
            if (IsValidEconomicPopulation(savedFixedPopulation))
            {
                source = BaseTownPopulationSource.SavedFixedPopulation;
                return savedFixedPopulation;
            }
            if (IsValidEconomicPopulation(legacyPopulation))
            {
                source = BaseTownPopulationSource.LegacyPopulation;
                return legacyPopulation;
            }

            source = BaseTownPopulationSource.ProfileDefault;
            return profile.DefaultEconomicPopulation;
        }

        private static bool IsValidEconomicPopulation(int population)
        {
            return population >= VillageProfile.MIN_ECONOMIC_POPULATION
                && population <= VillageProfile.MAX_ECONOMIC_POPULATION;
        }
    }
}
