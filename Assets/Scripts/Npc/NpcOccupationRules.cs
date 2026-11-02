#nullable enable
using ARPG.History;
using ARPG.OreVein;
using UnityEngine;

namespace ARPG.Npc
{
    /// <summary>NPC 생활 숙련 종류 (NPC_OCCUPATIONS_PERSONAL_STORIES_PLAN §3.1). 직업이 아니라 각자 따로 오르는 수치다.</summary>
    public enum NpcLifeSkill
    {
        Mining = 0,
        Smithing = 1,
    }

    /// <summary>대장 기술 레시피 한 줄 (§6.3·§6.3.1). 결과는 그 장비의 기본 아이템 변형이고, 재료는 광석 한 종류다.</summary>
    public readonly struct SmithingRecipe
    {
        public readonly int ItemId;
        public readonly int OreItemId;
        public readonly int RequiredSkill;

        public SmithingRecipe(int itemId, int oreItemId, int requiredSkill)
        {
            ItemId = itemId;
            OreItemId = oreItemId;
            RequiredSkill = requiredSkill;
        }
    }

    /// <summary>
    /// NPC 채광·제작·상점 위탁·장비 분실의 규칙 상수와 순수 계산 (NPC_OCCUPATIONS_PERSONAL_STORIES_PLAN 1단계).
    /// 상태는 NpcSaveData·VillageData가 가지며 여기서는 수치와 판정식만 둔다. 수치는 1단계 진단으로 조정하는 시작안이다.
    /// </summary>
    public static class NpcOccupationRules
    {
        // ===== 활동 (§5.5·§6.1) =====
        public const float MiningDurationHours = 8f;
        public const float CraftingDurationHours = 2f;
        public const int MiningYieldMin = 3;
        public const int MiningYieldMax = 5;
        public const int OresPerCraft = 3;

        // ===== 숙련 (§3.1) =====
        public const int SkillMax = 100;
        // 플레이어 상승 확률에 곱하는 NPC 배율 (2026-11-01 결정)
        public const float NpcSkillGainMultiplier = 0.10f;
        public const int LifeSkillCapMin = 60;
        public const int LifeSkillCapMax = 120;
        public const int InitialSkillMin = 1;
        public const int InitialSkillMax = 15;
        // 구역 5~9 / 10 이상에서 한 숙련이 주석·철 단계 안팎인 NPC 비율과 그 범위 (중심 ± InitialTierSpread)
        public const float AdvancedSkillChance = 0.30f;
        public const int InitialTierSpread = 3;

        // ===== 광석 (§5.3, WORLD_ORE_VEIN_PLAN §4.1) =====
        public const int CopperOreItemId = OreVeinRules.CopperItemId;
        public const int TinOreItemId = OreVeinRules.TinItemId;
        public const int IronOreItemId = OreVeinRules.IronItemId;
        public const int CopperRequiredSkill = 1;
        public const int TinRequiredSkill = 25;
        public const int IronRequiredSkill = 50;

        // ===== 원정 장비 분실 (§4.3) =====
        // 중상 중 장착 금속 장비 1개를 잃을 확률 (2026-11-01 결정, 사연이 부족하면 0.5)
        public const float EquipmentLossChance = 0.30f;
        // 빈 칸으로 이만큼 지내면 고생담이 된다 (§9.2 ④)
        public const float HardshipHours = 7f * 24f;

        // ===== 상점 광석 재고 (§7.1) =====
        public const int OreStockCap = 20;
        public const float OreStockDecayFraction = 0.10f;
        // 재고 0개 → 상한으로 갈수록 곧게 싸진다. 상점이 살 때 / 팔 때의 지역 기준값 배율
        public const float OreShopBuyMulAtEmpty = 1.2f;
        public const float OreShopBuyMulAtCap = 0.6f;
        public const float OreShopSellMulAtEmpty = 1.6f;
        public const float OreShopSellMulAtCap = 0.8f;
        // 광석이 특산(매입 비율이 기본보다 낮은) 지역의 기준값 배율 — 드워프 광산 상권
        public const float OreSpecialtyBaseMul = 0.8f;

        // ===== 상점 위탁 (§7.2) =====
        public const int ConsignmentSlotsPerVillage = 8;
        public const int ConsignmentsPerMaker = 2;
        public const int ConsignmentStartPercent = 80;
        public const int ConsignmentDailyDropPercent = 5;
        public const int ConsignmentFloorPercent = 60;
        public const int ConsignmentDays = 7;
        public const int ConsignmentBuyoutPercent = 20;
        public const int ConsignmentFeePercent = 10;

        // ===== 결정론 난수 salt (NpcActivityResolver.CreateRng 채널, 240~249) =====
        // 240 채광 산출(광종 → 개수), 241 채광 숙련 상승, 242 제작 변형 선택, 243 대장 숙련 상승, 244 장비 분실(확률 → 장비 고르기).
        // 245~249는 예비 — 재사용 금지.
        public const int MiningYieldSalt = 240;
        public const int MiningSkillGainSalt = 241;
        public const int CraftVariantSalt = 242;
        public const int SmithingSkillGainSalt = 243;
        public const int EquipmentLossSalt = 244;

        // 대장 숙련 1: 구리 등급 금속 장비 8종, 대장 숙련 50: 철 장비 8종 (§6.3, 2026-11-01 결정)
        private static readonly SmithingRecipe[] Recipes =
        {
            new SmithingRecipe(3, CopperOreItemId, CopperRequiredSkill),
            new SmithingRecipe(19, CopperOreItemId, CopperRequiredSkill),
            new SmithingRecipe(20, CopperOreItemId, CopperRequiredSkill),
            new SmithingRecipe(21, CopperOreItemId, CopperRequiredSkill),
            new SmithingRecipe(4, CopperOreItemId, CopperRequiredSkill),
            new SmithingRecipe(6, CopperOreItemId, CopperRequiredSkill),
            new SmithingRecipe(16, CopperOreItemId, CopperRequiredSkill),
            new SmithingRecipe(17, CopperOreItemId, CopperRequiredSkill),
            new SmithingRecipe(26, IronOreItemId, IronRequiredSkill),
            new SmithingRecipe(27, IronOreItemId, IronRequiredSkill),
            new SmithingRecipe(29, IronOreItemId, IronRequiredSkill),
            new SmithingRecipe(34, IronOreItemId, IronRequiredSkill),
            new SmithingRecipe(35, IronOreItemId, IronRequiredSkill),
            new SmithingRecipe(36, IronOreItemId, IronRequiredSkill),
            new SmithingRecipe(37, IronOreItemId, IronRequiredSkill),
            new SmithingRecipe(40, IronOreItemId, IronRequiredSkill),
        };

        /// <summary>이 장비의 제작법. 금속 장비 = 제작법이 있는 장비다 (분실 대상도 같다, §4.3).</summary>
        public static bool TryGetRecipe(int itemId, out SmithingRecipe recipe)
        {
            for (int i = 0; i < Recipes.Length; i++)
            {
                if (Recipes[i].ItemId == itemId)
                {
                    recipe = Recipes[i];
                    return true;
                }
            }
            recipe = default;
            return false;
        }

        public static bool IsMetalEquipment(int itemId) => TryGetRecipe(itemId, out _);

        public static bool IsOre(int itemId) =>
            itemId == CopperOreItemId || itemId == TinOreItemId || itemId == IronOreItemId;

        public static int GetOreRequiredSkill(int oreItemId) => oreItemId switch
        {
            TinOreItemId => TinRequiredSkill,
            IronOreItemId => IronRequiredSkill,
            _ => CopperRequiredSkill,
        };

        /// <summary>대장 숙련으로 이 광석을 쓰는 제작법이 하나라도 있는가 (자기 제작용 광석 남기기·광석 구매 판단).</summary>
        public static bool CanCraftWithOre(int smithingSkill, int oreItemId)
        {
            for (int i = 0; i < Recipes.Length; i++)
            {
                if (Recipes[i].OreItemId == oreItemId && smithingSkill >= Recipes[i].RequiredSkill)
                    return true;
            }
            return false;
        }

        /// <summary>
        /// 플레이어 상승 확률 (§3.1): 숙련 − 요구 숙련이 0~9면 100%, 10~24면 50%, 25 이상(또는 음수)면 0. NPC는 여기에 10%를 곱한다.
        /// </summary>
        public static float GetPlayerSkillGainChance(int skill, int requiredSkill)
        {
            int diff = skill - requiredSkill;
            if (diff < 0 || diff >= 25 || skill >= SkillMax)
                return 0f;
            return diff < 10 ? 1f : 0.5f;
        }

        public static float GetNpcSkillGainChance(int skill, int requiredSkill)
            => GetPlayerSkillGainChance(skill, requiredSkill) * NpcSkillGainMultiplier;

        /// <summary>
        /// 초기 숙련과 합산 상한을 정한다 (§3.1, 2026-11-01 결정). 구역 1~4는 각 1~15, 5~9는 약 30%가 한 숙련 25 안팎,
        /// 10 이상은 약 30%가 한 숙련 50 안팎. 합산 상한은 60~120이고, 초기 합계보다 최소 1 크다.
        /// </summary>
        public static void RollInitialSkills(int zoneLevel, HistoryRng rng, out int mining, out int smithing, out int cap)
        {
            mining = rng.Range(InitialSkillMin, InitialSkillMax + 1);
            smithing = rng.Range(InitialSkillMin, InitialSkillMax + 1);
            double advancedRoll = rng.NextDouble();
            bool advancedMining = rng.NextDouble() < 0.5d;
            int tierCenter = zoneLevel >= OreVeinRules.IronMinZone ? IronRequiredSkill
                : zoneLevel >= OreVeinRules.TinMinZone ? TinRequiredSkill : 0;
            int tierSkill = tierCenter + rng.Range(-InitialTierSpread, InitialTierSpread + 1);
            if (tierCenter > 0 && advancedRoll < AdvancedSkillChance)
            {
                if (advancedMining == true)
                    mining = tierSkill;
                else
                    smithing = tierSkill;
            }
            cap = rng.Range(LifeSkillCapMin, LifeSkillCapMax + 1);
            cap = Mathf.Max(cap, mining + smithing + 1);
        }

        /// <summary>
        /// 이 구역에서 캘 수 있는 광종 중 채광 숙련으로 가능한 것을 플레이어 광맥과 같은 비율로 고른다 (§5.5, OreVeinRules.SelectType).
        /// 가장 상위 광종 50%, 나머지가 남은 몫을 나눈다. 숙련이 모자란 광종은 빼고 비율을 다시 나눈다.
        /// </summary>
        public static int SelectMiningOre(int zoneLevel, int miningSkill, double roll01)
        {
            int available = zoneLevel >= OreVeinRules.IronMinZone ? 3 : zoneLevel >= OreVeinRules.TinMinZone ? 2 : 1;
            bool tin = available >= 2 && miningSkill >= TinRequiredSkill;
            bool iron = available >= 3 && miningSkill >= IronRequiredSkill;
            // 광맥 비율: 1~4 구리 100, 5~9 구리 50·주석 50, 10+ 구리 25·주석 25·철 50
            int copperWeight = available == 1 ? 100 : available == 2 ? 50 : 25;
            int tinWeight = tin == true ? (available == 2 ? 50 : 25) : 0;
            int ironWeight = iron == true ? 50 : 0;
            int total = copperWeight + tinWeight + ironWeight;
            int pick = Mathf.Clamp((int)(roll01 * total), 0, total - 1);
            if (pick < ironWeight)
                return IronOreItemId;
            if (pick < ironWeight + tinWeight)
                return TinOreItemId;
            return CopperOreItemId;
        }

        /// <summary>이 구역·숙련으로 캘 수 있는 광종인가 (구역 광종 규칙 + 요구 숙련).</summary>
        public static bool CanMineOre(int zoneLevel, int miningSkill, int oreItemId)
        {
            if (miningSkill < GetOreRequiredSkill(oreItemId))
                return false;
            return oreItemId switch
            {
                TinOreItemId => zoneLevel >= OreVeinRules.TinMinZone,
                IronOreItemId => zoneLevel >= OreVeinRules.IronMinZone,
                _ => true,
            };
        }

        /// <summary>재고 기반 광석 가격 배율 (§7.1). 재고 0 → 상한으로 곧게 줄어든다. shopBuys=true면 상점이 살 때.</summary>
        public static float GetOreStockMultiplier(int stock, bool shopBuys)
        {
            float t = Mathf.Clamp01(stock / (float)OreStockCap);
            return shopBuys == true
                ? Mathf.Lerp(OreShopBuyMulAtEmpty, OreShopBuyMulAtCap, t)
                : Mathf.Lerp(OreShopSellMulAtEmpty, OreShopSellMulAtCap, t);
        }

        /// <summary>재입고 1회의 재고 감소량 (§7.1). 적은 재고도 줄어들도록 올림한다.</summary>
        public static int GetOreStockDecay(int stock)
        {
            if (stock <= 0)
                return 0;
            return Mathf.Min(stock, Mathf.CeilToInt(stock * OreStockDecayFraction));
        }

        /// <summary>위탁 가격 비율 (§7.2): 80%에서 하루 5%씩 4일 내리고 하한 60%. 맡긴 날 = 0일.</summary>
        public static int GetConsignmentPricePercent(int daysListed)
        {
            int percent = ConsignmentStartPercent - ConsignmentDailyDropPercent * Mathf.Max(0, daysListed);
            return Mathf.Max(ConsignmentFloorPercent, percent);
        }

        public static int GetDaysListed(float consignedGameTime, float now)
            => Mathf.Max(0, Mathf.FloorToInt((now - consignedGameTime) / 24f));
    }
}
