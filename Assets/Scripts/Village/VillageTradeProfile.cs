#nullable enable
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace ARPG.Village
{
    /// <summary>
    /// V5 (VILLAGE_REWORK_PLAN §8.2) 아이템 거래 태그. ItemTable 컬럼이 아니라
    /// 코드 카탈로그(ItemTradeTagCatalog)로 정의한다 — TableData .bytes는 Google Sheets에서
    /// 재생성되므로 V2~V4 임계값·Cap·GrowthPlan과 같은 정책이다 (시트 컬럼 이전은 V6/V7 검토).
    /// </summary>
    [Flags]
    public enum TradeTag
    {
        None = 0,
        Ore = 1 << 0,
        HeavyArmor = 1 << 1,
        Arcane = 1 << 2,
        Ranged = 1 << 3,
        Herb = 1 << 4,
        Poison = 1 << 5,
        Cold = 1 << 6,
        TravelSupply = 1 << 7,
        TradeGood = 1 << 8,
        RareImport = 1 << 9,
    }

    /// <summary>재고 슬롯 종류 (§8.4 — 공통 2 + 특산 2 + 특수/희귀 1 초안).</summary>
    public enum TradeStockSlotKind
    {
        Guaranteed = 0,
        Specialty = 1,
        RareImport = 2,
        FacilityPool = 3,
    }

    /// <summary>상점 UI에 표시할 지역 가격 사유 (§8.5).</summary>
    public enum TradePriceReason
    {
        Standard = 0,
        LocalSpecialty = 1,   // 지역 특산 — 현지에서 싸고 풍부
        LocalShortage = 2,    // 현지 부족 — 구매가·매입가 프리미엄
        RareImport = 3,       // 희귀 수입
    }

    /// <summary>
    /// 순수 로직(VillageTradeStockRoller)에 넘기는 아이템 최소 정보.
    /// Tables/DataManager 의존 없이 정적 하니스에서 같은 입력을 만들 수 있다.
    /// </summary>
    public readonly struct TradeItemInfo
    {
        public int Id { get; }
        public int Tier { get; }
        public int BasePrice { get; }
        public bool Stackable { get; }
        public TradeTag Tags { get; }

        public TradeItemInfo(int id, int tier, int basePrice, bool stackable,
            TradeTag tags)
        {
            Id = id;
            Tier = tier;
            BasePrice = basePrice;
            Stackable = stackable;
            Tags = tags;
        }
    }

    /// <summary>
    /// ItemId → TradeTag 코드 카탈로그 (§8.2). ItemId 직접 나열은 보장 품목과
    /// 이 매핑에만 사용한다. 여기 없는 아이템은 태그 없음(일반 품목)이다.
    /// </summary>
    public static class ItemTradeTagCatalog
    {
        private static readonly Dictionary<int, TradeTag> _byItemId = BuildCatalog();

        public static TradeTag GetTags(int itemId)
        {
            return _byItemId.TryGetValue(itemId, out TradeTag tags) ? tags : TradeTag.None;
        }

        private static Dictionary<int, TradeTag> BuildCatalog()
        {
            Dictionary<int, TradeTag> map = new Dictionary<int, TradeTag>();

            // 자원 광물 — 지역 간 교역 차익은 TradeGood에만 허용한다 (§8.6).
            map[2002] = TradeTag.Ore | TradeTag.TradeGood;              // 구리
            map[2003] = TradeTag.Ore | TradeTag.TradeGood;              // 철

            // 중장구 (금속 투구/갑옷/건틀릿/방패 계열) — Dwarf 특산.
            map[26] = TradeTag.HeavyArmor;   // 철 투구
            map[27] = TradeTag.HeavyArmor;   // 철 갑옷
            map[29] = TradeTag.HeavyArmor;   // 철 건틀릿
            map[34] = TradeTag.HeavyArmor;   // 철 방패
            map[45] = TradeTag.HeavyArmor;   // 강철 전투 투구
            map[47] = TradeTag.HeavyArmor;   // 강철 판금갑옷
            map[49] = TradeTag.HeavyArmor;   // 미스릴 건틀릿
            map[51] = TradeTag.HeavyArmor;   // 미스릴 부츠
            map[57] = TradeTag.HeavyArmor;   // 강철 대방패

            // 비전 (지팡이/완드/마법서/마법사 방어구) — Dwarf 현지 부족.
            map[24] = TradeTag.Arcane;       // 나뭇가지 지팡이
            map[25] = TradeTag.Arcane;       // 낡은 완드
            map[42] = TradeTag.Arcane;       // 은빛 지팡이
            map[43] = TradeTag.Arcane;       // 마력 완드
            map[44] = TradeTag.Arcane;       // 초급 마법서
            map[46] = TradeTag.Arcane;       // 마법사 두건
            map[48] = TradeTag.Arcane;       // 마법사 로브
            map[50] = TradeTag.Arcane;       // 마력 장갑
            map[54] = TradeTag.Arcane;       // 마력 허리띠
            map[56] = TradeTag.Arcane;       // 마력석 반지
            map[58] = TradeTag.Arcane;       // 상급 마법서
            map[66] = TradeTag.Arcane;       // 대마법사 지팡이
            map[67] = TradeTag.Arcane;       // 마력 완드(T3)

            // 원거리 무기.
            map[22] = TradeTag.Ranged;       // 목재 단궁
            map[23] = TradeTag.Ranged;       // 투창
            map[39] = TradeTag.Ranged;       // 강화 장궁
            map[40] = TradeTag.Ranged;       // 철 석궁
            map[41] = TradeTag.Ranged;       // 철 표창
            map[63] = TradeTag.Ranged;       // 미스릴 장궁
            map[64] = TradeTag.Ranged;       // 강철 중석궁
            map[65] = TradeTag.Ranged;       // 마력 표창

            // 스킬북·스킬 페이지 — 희귀/수입 풀 (§8.4 특수/희귀 슬롯).
            map[5000] = TradeTag.Arcane | TradeTag.RareImport;   // 스킬 북 T1
            map[5001] = TradeTag.Arcane | TradeTag.RareImport;   // 스킬 북 T2
            map[5002] = TradeTag.Arcane | TradeTag.RareImport;   // 스킬 북 T3
            map[6001] = TradeTag.Arcane;                          // 일반 스킬 페이지
            map[6002] = TradeTag.Arcane | TradeTag.RareImport;   // 마법 스킬 페이지
            map[6003] = TradeTag.Arcane | TradeTag.RareImport;   // 전설 스킬 페이지

            return map;
        }
    }

    /// <summary>가중 태그 풀 항목. Tags == None은 "태그 없는 일반 품목" 풀을 뜻한다.</summary>
    public readonly struct TradeWeightedTagPool
    {
        public TradeTag Tags { get; }
        public int Weight { get; }

        public TradeWeightedTagPool(TradeTag tags, int weight)
        {
            Tags = tags;
            Weight = weight;
        }
    }

    /// <summary>
    /// 태그별 가격 배율 규칙 (§8.3). 목록 순서대로 첫 매칭이 이긴다.
    /// 단위는 10,000분율(bp) — 기존 ItemTable.SellRatioBp(÷100 퍼센트)와 의미를 분리한다.
    /// 플레이어 구매가 = BasePrice × SellToPlayerBp / 10000,
    /// 상인 매입가     = BasePrice × BuyFromPlayerBp / 10000.
    /// </summary>
    public readonly struct TradePriceRule
    {
        public TradeTag Tags { get; }
        public int SellToPlayerBp { get; }
        public int BuyFromPlayerBp { get; }

        public TradePriceRule(TradeTag tags, int sellToPlayerBp, int buyFromPlayerBp)
        {
            Tags = tags;
            SellToPlayerBp = sellToPlayerBp;
            BuyFromPlayerBp = buyFromPlayerBp;
        }
    }

    /// <summary>ShopTier별 슬롯 구성 (§8.4).</summary>
    public readonly struct TradeSlotPlan
    {
        public int Guaranteed { get; }
        public int Specialty { get; }
        public int RareImport { get; }

        public int Total => Guaranteed + Specialty + RareImport;

        public TradeSlotPlan(int guaranteed, int specialty, int rareImport)
        {
            Guaranteed = guaranteed;
            Specialty = specialty;
            RareImport = rareImport;
        }
    }

    /// <summary>
    /// V5 지역 상권 프로필 (§8.3). VillageProfile.TradeProfileId가 가리키는 읽기 전용 정의로,
    /// 마을별 가변 상태(재고·회차)는 VillageData가 소유한다.
    /// </summary>
    public sealed class VillageTradeProfile
    {
        public const int BP_DENOMINATOR = 10000;

        public string TradeProfileId { get; }
        /// <summary>보장 품목 그룹 — 우선순위 순 ItemId. Tier 게이트 통과분에서 앞에서부터 채운다.</summary>
        public IReadOnlyList<int> GuaranteedItemIds { get; }
        /// <summary>지역 특산 가중 풀.</summary>
        public IReadOnlyList<TradeWeightedTagPool> SpecialtyPools { get; }
        /// <summary>희귀/수입 가중 풀.</summary>
        public IReadOnlyList<TradeWeightedTagPool> RareImportPools { get; }
        /// <summary>특수 시설 Tier에 따른 추가 풀 (희귀 슬롯에 병합).</summary>
        public IReadOnlyList<TradeWeightedTagPool> FacilityPools { get; }
        /// <summary>FacilityPools가 열리는 최소 주 특수 시설 Tier.</summary>
        public int FacilityPoolMinTier { get; }
        public IReadOnlyList<TradePriceRule> PriceRules { get; }
        public int DefaultSellToPlayerBp { get; }
        public int DefaultBuyFromPlayerBp { get; }
        /// <summary>일반 Stackable 수량 범위.</summary>
        public int StackableCountMin { get; }
        public int StackableCountMax { get; }
        /// <summary>TradeGood 수량 범위 — 재고 수량으로 차익 규모를 제한한다 (§8.6).</summary>
        public int TradeGoodCountMin { get; }
        public int TradeGoodCountMax { get; }
        public float RestockIntervalHours { get; }

        public VillageTradeProfile(string tradeProfileId,
            IReadOnlyList<int> guaranteedItemIds,
            IReadOnlyList<TradeWeightedTagPool> specialtyPools,
            IReadOnlyList<TradeWeightedTagPool> rareImportPools,
            IReadOnlyList<TradeWeightedTagPool> facilityPools,
            int facilityPoolMinTier,
            IReadOnlyList<TradePriceRule> priceRules,
            int defaultSellToPlayerBp, int defaultBuyFromPlayerBp,
            int stackableCountMin, int stackableCountMax,
            int tradeGoodCountMin, int tradeGoodCountMax,
            float restockIntervalHours)
        {
            if (string.IsNullOrWhiteSpace(tradeProfileId))
                throw new ArgumentException("TradeProfileId is required.",
                    nameof(tradeProfileId));
            if (restockIntervalHours <= 0f)
                throw new ArgumentOutOfRangeException(nameof(restockIntervalHours));

            TradeProfileId = tradeProfileId;
            GuaranteedItemIds = CopyList(guaranteedItemIds);
            SpecialtyPools = CopyList(specialtyPools);
            RareImportPools = CopyList(rareImportPools);
            FacilityPools = CopyList(facilityPools);
            FacilityPoolMinTier = facilityPoolMinTier;
            PriceRules = CopyList(priceRules);
            DefaultSellToPlayerBp = defaultSellToPlayerBp;
            DefaultBuyFromPlayerBp = defaultBuyFromPlayerBp;
            StackableCountMin = stackableCountMin;
            StackableCountMax = stackableCountMax;
            TradeGoodCountMin = tradeGoodCountMin;
            TradeGoodCountMax = tradeGoodCountMax;
            RestockIntervalHours = restockIntervalHours;
        }

        /// <summary>ShopTier(1~4)별 슬롯 수. 0(구 세이브 미해금)은 1로 간주한다.</summary>
        public TradeSlotPlan GetSlotPlan(int shopTier)
        {
            int tier = ClampShopTier(shopTier);
            switch (tier)
            {
                case 1: return new TradeSlotPlan(2, 2, 1);
                case 2: return new TradeSlotPlan(2, 3, 1);
                case 3: return new TradeSlotPlan(2, 3, 2);
                default: return new TradeSlotPlan(3, 3, 2);
            }
        }

        /// <summary>ShopTier가 여는 최대 아이템 Tier (품질 해금, §8.4).</summary>
        public int GetMaxItemTier(int shopTier)
        {
            int tier = ClampShopTier(shopTier);
            return tier > 3 ? 3 : tier;
        }

        public static int ClampShopTier(int shopTier)
        {
            if (shopTier < 1) return 1;
            if (shopTier > 4) return 4;
            return shopTier;
        }

        /// <summary>태그에 첫 매칭되는 가격 규칙의 SellToPlayerBp. 없으면 기본값.</summary>
        public int GetSellToPlayerBp(TradeTag tags)
        {
            for (int i = 0; i < PriceRules.Count; i++)
            {
                if ((PriceRules[i].Tags & tags) != TradeTag.None)
                    return PriceRules[i].SellToPlayerBp;
            }
            return DefaultSellToPlayerBp;
        }

        /// <summary>태그에 첫 매칭되는 가격 규칙의 BuyFromPlayerBp. 없으면 기본값.</summary>
        public int GetBuyFromPlayerBp(TradeTag tags)
        {
            for (int i = 0; i < PriceRules.Count; i++)
            {
                if ((PriceRules[i].Tags & tags) != TradeTag.None)
                    return PriceRules[i].BuyFromPlayerBp;
            }
            return DefaultBuyFromPlayerBp;
        }

        private static IReadOnlyList<T> CopyList<T>(IReadOnlyList<T> source)
        {
            List<T> copy = new List<T>();
            if (source != null)
            {
                for (int i = 0; i < source.Count; i++)
                    copy.Add(source[i]);
            }
            return new ReadOnlyCollection<T>(copy);
        }
    }

    /// <summary>
    /// V5 상권 프로필 카탈로그. V1 계약대로 VillageProfileCatalog가 저장한 TradeProfileId를
    /// 바꾸지 않고 각 ID의 정의만 채운다. V5 범위 제한에 따라 아키타입은 두 가지다 —
    /// Dwarf 광산 상권과 비-Dwarf 공통 상권 (Race별 세분화는 V6).
    /// </summary>
    public static class VillageTradeProfileCatalog
    {
        // 가격 초안 (bp). 교역 밸런스 불변식 (§8.6, 하니스 검증):
        // - 非TradeGood: 전체 프로필 최대 BuyFromPlayerBp(4500) < 최소 SellToPlayerBp(8500)
        //   → 일반 장비 왕복·운송 차익 없음.
        // - TradeGood: Dwarf 판매 8000 < 비-Dwarf 매입 13000 → 광산촌→부족 지역 양수 차익.
        //   같은 프로필 안에서는 매입 < 판매 → 같은 상점 왕복 차익 없음.
        private const int DEFAULT_SELL_BP = 10000;
        private const int DEFAULT_BUY_BP = 4000;

        private static readonly Dictionary<string, VillageTradeProfile> _byTradeProfileId =
            BuildCatalog();

        public static VillageTradeProfile DefaultProfile =>
            _byTradeProfileId[VillageProfileCatalog.DefaultProceduralProfile.TradeProfileId];

        public static IReadOnlyCollection<string> AllTradeProfileIds =>
            _byTradeProfileId.Keys;

        public static bool TryGet(string tradeProfileId, out VillageTradeProfile profile)
        {
            if (string.IsNullOrWhiteSpace(tradeProfileId))
            {
                profile = null!;
                return false;
            }
            return _byTradeProfileId.TryGetValue(tradeProfileId, out profile!);
        }

        private static Dictionary<string, VillageTradeProfile> BuildCatalog()
        {
            Dictionary<string, VillageTradeProfile> byId =
                new Dictionary<string, VillageTradeProfile>(StringComparer.Ordinal);

            IReadOnlyList<VillageProfile> profiles = VillageProfileCatalog.AllProfiles;
            for (int i = 0; i < profiles.Count; i++)
            {
                VillageProfile profile = profiles[i];
                if (byId.ContainsKey(profile.TradeProfileId))
                    throw new InvalidOperationException(
                        $"Duplicate TradeProfileId: {profile.TradeProfileId}");
                byId.Add(profile.TradeProfileId, CreateProfile(profile));
            }

            return byId;
        }

        private static VillageTradeProfile CreateProfile(VillageProfile profile)
        {
            return profile.Race == GlobalEnum.RaceType.Dwarf
                ? CreateDwarfMiningProfile(profile.TradeProfileId)
                : CreateGeneralistProfile(profile.TradeProfileId);
        }

        /// <summary>
        /// Dwarf 광산 상권 — 광물이 보장 품목으로 싸고 풍부하며(지역 특산),
        /// 중장구 특산 할인, 비전 품목은 현지 부족 프리미엄.
        /// </summary>
        private static VillageTradeProfile CreateDwarfMiningProfile(string tradeProfileId)
        {
            return new VillageTradeProfile(
                tradeProfileId,
                new[] { 2003, 2002, 6001 },                       // 철, 구리, 일반 스킬 페이지
                new[]
                {
                    new TradeWeightedTagPool(TradeTag.Ore, 3),
                    new TradeWeightedTagPool(TradeTag.HeavyArmor, 2),
                },
                new[]
                {
                    new TradeWeightedTagPool(TradeTag.RareImport, 2),
                    new TradeWeightedTagPool(TradeTag.Arcane, 1),
                },
                new[]
                {
                    new TradeWeightedTagPool(TradeTag.HeavyArmor, 3),
                    new TradeWeightedTagPool(TradeTag.Ore, 2),
                },
                2,                                                 // 제련소 Tier 2부터 추가 풀
                new[]
                {
                    // TradeGood(광물)이 HeavyArmor보다 먼저 매칭되어야 하므로 순서 고정.
                    new TradePriceRule(TradeTag.TradeGood, 8000, 3000),
                    new TradePriceRule(TradeTag.Ore | TradeTag.HeavyArmor, 8500, 4000),
                    new TradePriceRule(TradeTag.Arcane, 11500, 4500),
                },
                DEFAULT_SELL_BP, DEFAULT_BUY_BP,
                3, 10,
                4, 8,
                24f);
        }

        /// <summary>
        /// 비-Dwarf 공통 상권 — 광물이 재고 풀에 없고(현지 부족) TradeGood 매입 프리미엄으로
        /// 광산촌발 운송 교역 동기를 만든다. Race별 특산 세분화는 V6 범위.
        /// </summary>
        private static VillageTradeProfile CreateGeneralistProfile(string tradeProfileId)
        {
            return new VillageTradeProfile(
                tradeProfileId,
                new[] { 6001, 5000, 6002, 5001, 6003, 5002 },     // 페이지/북 Tier 순
                new[]
                {
                    // None = 태그 없는 일반 품목 풀 (RareImport/TradeGood 제외 — 로러가 처리).
                    new TradeWeightedTagPool(TradeTag.None, 3),
                    new TradeWeightedTagPool(TradeTag.Ranged, 1),
                },
                new[]
                {
                    new TradeWeightedTagPool(TradeTag.RareImport, 2),
                    new TradeWeightedTagPool(TradeTag.Arcane, 1),
                },
                Array.Empty<TradeWeightedTagPool>(),
                int.MaxValue,                                      // 특수 시설 풀 없음 (V6)
                new[]
                {
                    // 현지 부족 광물 — 매입 프리미엄(13000)이 Dwarf 구매가(8000)보다 높아
                    // 제한된 양수 차익을 만들고, 판매가(15000) > 매입가로 같은 상점 왕복은 손해.
                    new TradePriceRule(TradeTag.TradeGood, 15000, 13000),
                },
                DEFAULT_SELL_BP, DEFAULT_BUY_BP,
                3, 10,
                4, 8,
                24f);
        }
    }
}
