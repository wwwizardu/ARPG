#nullable enable
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace ARPG.Village
{
    /// <summary>V4 (VILLAGE_REWORK_PLAN §7.2) 역할 슬롯. 초기 시설 1종 = 슬롯 1개.</summary>
    public enum VillageLayoutSlotRole
    {
        Hub = 0,             // VillageHub (100)
        Market = 1,          // MerchantStall (151)
        Inn = 2,             // Inn (153)
        PrimarySpecial = 3,  // Profile 주 특수 시설 (Dwarf: Forge 160)
        Shrine = 4,          // Shrine (170) — Shrine 콘텐츠 확정 Profile 전용
        Secondary = 5,       // Town/City 보조 특수 시설 (V6 콘텐츠)
    }

    /// <summary>마을 중심 기준 상대 좌표의 역할 슬롯 (결정론 — 같은 Profile은 항상 같은 오프셋).</summary>
    public readonly struct VillageLayoutSlot
    {
        public VillageLayoutSlotRole Role { get; }
        public int OffsetX { get; }
        public int OffsetY { get; }

        public VillageLayoutSlot(VillageLayoutSlotRole role, int offsetX, int offsetY)
        {
            Role = role;
            OffsetX = offsetX;
            OffsetY = offsetY;
        }
    }

    /// <summary>
    /// V4 (VILLAGE_REWORK_PLAN §7): LayoutProfileId가 가리키는 읽기 전용 배치 명세.
    /// - 시설 역할별 지정 슬롯이 기본 배치기다 (VillageTileFinder 전역 점수 탐색 아님).
    /// - Stage별 초기 기능 구역 반경(Bounds)과 축소된 도로/광장 예약을 제공한다 (§7.4).
    /// - HasFunctionalWalls면 Hamlet 이상 초기 Stage에서 Bounds 둘레에 완공 벽을 생성한다 (§7.5 1안).
    /// </summary>
    public sealed class VillageLayoutProfile
    {
        /// <summary>슬롯 점유 시 결정론 fallback 링 탐색 최대 반경 (§7.2 "작은 반경").</summary>
        public const int SLOT_FALLBACK_MAX_RADIUS = 3;

        public string LayoutKey { get; }
        public IReadOnlyList<VillageLayoutSlot> Slots { get; }
        /// <summary>true면 Stage별 콤팩트 반경을 사용, false면 VillageStageTable.BoundsRadius 유지.</summary>
        public bool UsesCompactBounds { get; }
        public bool HasFunctionalWalls { get; }
        /// <summary>fallback 탐색에만 적용되는 축소 예약 (지정 슬롯은 예약 위에도 배치 가능).</summary>
        public int PlazaReserveRadius { get; }
        public int RoadReserveRadius { get; }
        public int RoadReserveHalfWidth { get; }

        private readonly int _settlementRadius;
        private readonly int _hamletRadius;
        private readonly int _villageRadius;
        private readonly int _townRadius;
        private readonly int _cityRadius;

        public VillageLayoutProfile(string layoutKey, IReadOnlyList<VillageLayoutSlot> slots,
            bool usesCompactBounds, bool hasFunctionalWalls,
            int plazaReserveRadius, int roadReserveRadius, int roadReserveHalfWidth,
            int settlementRadius, int hamletRadius, int villageRadius,
            int townRadius, int cityRadius)
        {
            LayoutKey = layoutKey;
            List<VillageLayoutSlot> copy = new List<VillageLayoutSlot>();
            if (slots != null)
            {
                for (int i = 0; i < slots.Count; i++)
                    copy.Add(slots[i]);
            }
            Slots = new ReadOnlyCollection<VillageLayoutSlot>(copy);
            UsesCompactBounds = usesCompactBounds;
            HasFunctionalWalls = hasFunctionalWalls;
            PlazaReserveRadius = plazaReserveRadius;
            RoadReserveRadius = roadReserveRadius;
            RoadReserveHalfWidth = roadReserveHalfWidth;
            _settlementRadius = settlementRadius;
            _hamletRadius = hamletRadius;
            _villageRadius = villageRadius;
            _townRadius = townRadius;
            _cityRadius = cityRadius;
        }

        /// <summary>Stage별 초기 기능 구역 반경. UsesCompactBounds=false면 0 (호출자가 테이블 값 사용).</summary>
        public int GetInitialBoundsRadius(VillageStage stage)
        {
            if (UsesCompactBounds == false)
                return 0;
            switch (stage)
            {
                case VillageStage.Settlement: return _settlementRadius;
                case VillageStage.Hamlet: return _hamletRadius;
                case VillageStage.Village: return _villageRadius;
                case VillageStage.Town: return _townRadius;
                case VillageStage.City: return _cityRadius;
                default: return _settlementRadius;
            }
        }

        public bool TryGetSlot(VillageLayoutSlotRole role, out VillageLayoutSlot slot)
        {
            for (int i = 0; i < Slots.Count; i++)
            {
                if (Slots[i].Role == role)
                {
                    slot = Slots[i];
                    return true;
                }
            }
            slot = default;
            return false;
        }
    }

    /// <summary>
    /// V4 레이아웃 카탈로그. V1 VillageProfile.LayoutProfileId가 연결점이며 저장 ID는 바꾸지 않는다.
    /// - Dwarf 광산 수직 슬라이스: 콤팩트 기능 구역 + 기능성 벽 (§7.4/§7.5).
    /// - 그 외 Profile: 같은 슬롯 배치기를 쓰는 공통 기본 레이아웃 (Race variant는 V6).
    ///   기본 레이아웃은 Bounds/예약을 기존 Stage 테이블 값으로 유지하고 벽을 만들지 않아
    ///   Dwarf 외 청사진의 장식 경계와 런타임 벽이 중복되지 않는다.
    /// </summary>
    public static class VillageLayoutCatalog
    {
        private const string DWARF_LAYOUT_PREFIX = "layout.dwarf.";

        // Dwarf 광산 마을 슬롯 (§7.4): 남쪽 입구(Gate)에서 모든 공통 서비스까지 짧은 동선.
        //  - Hub는 중심 광장, Market/Inn은 남쪽 입구 가까이 좌우, 주 특수 시설(제련소)은 북동쪽 안쪽.
        //  - N-S 큰길 축(x=중심)을 비워 두도록 Hub 외 슬롯은 전부 축에서 벗어나 있고,
        //    슬롯 간 체비쇼프 거리 ≥ 2 → 기능 시설이 서로 구분되면서 14×14 기능 구역 안에 수렴.
        private static readonly VillageLayoutSlot[] _dwarfSlots =
        {
            new VillageLayoutSlot(VillageLayoutSlotRole.Hub, 0, 0),
            new VillageLayoutSlot(VillageLayoutSlotRole.Market, -3, -2),
            new VillageLayoutSlot(VillageLayoutSlotRole.Inn, 3, -2),
            new VillageLayoutSlot(VillageLayoutSlotRole.PrimarySpecial, 3, 2),
            new VillageLayoutSlot(VillageLayoutSlotRole.Shrine, -3, 2),
            new VillageLayoutSlot(VillageLayoutSlotRole.Secondary, 4, 4),
        };

        // 공통 기본 레이아웃 — 같은 슬롯 세트, Bounds/예약은 기존 Stage 테이블 유지, 벽 없음.
        private static readonly VillageLayoutProfile _defaultLayout =
            new VillageLayoutProfile("layout.default", _dwarfSlots,
                usesCompactBounds: false, hasFunctionalWalls: false,
                plazaReserveRadius: 0, roadReserveRadius: 0, roadReserveHalfWidth: 0,
                settlementRadius: 0, hamletRadius: 0, villageRadius: 0,
                townRadius: 0, cityRadius: 0);

        // Dwarf 광산 수직 슬라이스 (§7.4):
        //  - 기능 구역: Settlement 11×11 → Hamlet 13×13 → Village 이상 15×15(벽 포함, 내부 13×13)
        //  - 광장 3×3(반경 1), 큰길 폭 1타일(halfWidth 0) — 기존 Stage 테이블(폭 3, 반경 4~10) 대비 축소
        private static readonly VillageLayoutProfile _dwarfMiningLayout =
            new VillageLayoutProfile("layout.dwarf.mining", _dwarfSlots,
                usesCompactBounds: true, hasFunctionalWalls: true,
                plazaReserveRadius: 1, roadReserveRadius: 5, roadReserveHalfWidth: 0,
                settlementRadius: 5, hamletRadius: 6, villageRadius: 7,
                townRadius: 7, cityRadius: 7);

        // V6 Race별 Layout variant (VILLAGE_REWORK_PLAN V6 체크리스트): 같은 슬롯 배치기·기존
        // Bounds·벽 없음을 공유하되 시설 상대 오프셋만 Race별로 달라 배치가 시각적으로 구분된다.
        // 사용자 결정(V6): 특수 시설은 Dwarf만 → 非Dwarf는 Hub/Market/Inn 슬롯만 실제 사용하고
        // PrimarySpecial/Shrine/Secondary 오프셋은 특수 시설이 추가될 후속 Phase를 위한 확장점이다.
        // VillageSlotPlacer·VillageWallRules 로직은 수정하지 않는다(데이터만).
        private static readonly VillageLayoutProfile _humanLayout = CreateRaceVariant(
            "layout.human", new[]
            {
                new VillageLayoutSlot(VillageLayoutSlotRole.Hub, 0, 0),
                new VillageLayoutSlot(VillageLayoutSlotRole.Market, -3, -1),
                new VillageLayoutSlot(VillageLayoutSlotRole.Inn, 3, -1),
                new VillageLayoutSlot(VillageLayoutSlotRole.PrimarySpecial, 0, 3),
                new VillageLayoutSlot(VillageLayoutSlotRole.Shrine, -3, 2),
                new VillageLayoutSlot(VillageLayoutSlotRole.Secondary, 3, 2),
            });
        private static readonly VillageLayoutProfile _elfLayout = CreateRaceVariant(
            "layout.elf", new[]
            {
                new VillageLayoutSlot(VillageLayoutSlotRole.Hub, 0, 0),
                new VillageLayoutSlot(VillageLayoutSlotRole.Market, -2, 2),
                new VillageLayoutSlot(VillageLayoutSlotRole.Inn, 2, 2),
                new VillageLayoutSlot(VillageLayoutSlotRole.PrimarySpecial, 0, -3),
                new VillageLayoutSlot(VillageLayoutSlotRole.Shrine, -3, -1),
                new VillageLayoutSlot(VillageLayoutSlotRole.Secondary, 3, -1),
            });
        private static readonly VillageLayoutProfile _orcLayout = CreateRaceVariant(
            "layout.orc", new[]
            {
                new VillageLayoutSlot(VillageLayoutSlotRole.Hub, 0, 0),
                new VillageLayoutSlot(VillageLayoutSlotRole.Market, -3, 0),
                new VillageLayoutSlot(VillageLayoutSlotRole.Inn, 0, -3),
                new VillageLayoutSlot(VillageLayoutSlotRole.PrimarySpecial, 3, 0),
                new VillageLayoutSlot(VillageLayoutSlotRole.Shrine, 0, 3),
                new VillageLayoutSlot(VillageLayoutSlotRole.Secondary, 3, 3),
            });
        private static readonly VillageLayoutProfile _nomadLayout = CreateRaceVariant(
            "layout.nomad", new[]
            {
                new VillageLayoutSlot(VillageLayoutSlotRole.Hub, 0, 0),
                new VillageLayoutSlot(VillageLayoutSlotRole.Market, -2, 0),
                new VillageLayoutSlot(VillageLayoutSlotRole.Inn, 3, 0),
                new VillageLayoutSlot(VillageLayoutSlotRole.PrimarySpecial, -3, -3),
                new VillageLayoutSlot(VillageLayoutSlotRole.Shrine, 3, -3),
                new VillageLayoutSlot(VillageLayoutSlotRole.Secondary, 0, 3),
            });
        private static readonly VillageLayoutProfile _lizardmanLayout = CreateRaceVariant(
            "layout.lizardman", new[]
            {
                new VillageLayoutSlot(VillageLayoutSlotRole.Hub, 0, 0),
                new VillageLayoutSlot(VillageLayoutSlotRole.Market, -3, 1),
                new VillageLayoutSlot(VillageLayoutSlotRole.Inn, 2, -2),
                new VillageLayoutSlot(VillageLayoutSlotRole.PrimarySpecial, 3, 2),
                new VillageLayoutSlot(VillageLayoutSlotRole.Shrine, -2, 3),
                new VillageLayoutSlot(VillageLayoutSlotRole.Secondary, -3, -2),
            });
        private static readonly VillageLayoutProfile _yetiLayout = CreateRaceVariant(
            "layout.yeti", new[]
            {
                new VillageLayoutSlot(VillageLayoutSlotRole.Hub, 0, 0),
                new VillageLayoutSlot(VillageLayoutSlotRole.Market, -2, -2),
                new VillageLayoutSlot(VillageLayoutSlotRole.Inn, 2, -2),
                new VillageLayoutSlot(VillageLayoutSlotRole.PrimarySpecial, 2, 2),
                new VillageLayoutSlot(VillageLayoutSlotRole.Shrine, -2, 2),
                new VillageLayoutSlot(VillageLayoutSlotRole.Secondary, 0, 3),
            });

        /// <summary>Race variant 공통: 콤팩트 Bounds·벽 없음(기존 Stage 테이블 유지), 오프셋만 상이.</summary>
        private static VillageLayoutProfile CreateRaceVariant(string layoutKey,
            VillageLayoutSlot[] slots)
        {
            return new VillageLayoutProfile(layoutKey, slots,
                usesCompactBounds: false, hasFunctionalWalls: false,
                plazaReserveRadius: 0, roadReserveRadius: 0, roadReserveHalfWidth: 0,
                settlementRadius: 0, hamletRadius: 0, villageRadius: 0,
                townRadius: 0, cityRadius: 0);
        }

        /// <summary>
        /// LayoutProfileId → 레이아웃. Dwarf 절차적 레이아웃은 수직 슬라이스(콤팩트+벽)를 받고,
        /// 그 외 Race는 오프셋만 다른 variant, 수제작 BaseTown·미지원은 공통 기본이다. 항상 non-null.
        /// </summary>
        public static VillageLayoutProfile Resolve(string? layoutProfileId)
        {
            if (string.IsNullOrEmpty(layoutProfileId))
                return _defaultLayout;

            // Dwarf 절차적 레이아웃(layout.dwarf.*)만 콤팩트+벽. BaseTown(layout.base_town.dwarf)은 제외.
            if (layoutProfileId!.StartsWith(DWARF_LAYOUT_PREFIX, StringComparison.Ordinal))
                return _dwarfMiningLayout;

            switch (ExtractRaceToken(layoutProfileId))
            {
                case "human": return _humanLayout;
                case "elf": return _elfLayout;
                case "orc": return _orcLayout;
                case "nomad": return _nomadLayout;
                case "lizardman": return _lizardmanLayout;
                case "yeti": return _yetiLayout;
                default: return _defaultLayout;
            }
        }

        /// <summary>"layout.&lt;race&gt;.&lt;biome&gt;.&lt;role&gt;"에서 race 토큰 추출. 없으면 빈 문자열.</summary>
        private static string ExtractRaceToken(string layoutProfileId)
        {
            int start = layoutProfileId.IndexOf('.');
            if (start < 0)
                return string.Empty;
            start += 1;
            int end = layoutProfileId.IndexOf('.', start);
            return end < 0
                ? layoutProfileId.Substring(start)
                : layoutProfileId.Substring(start, end - start);
        }

        public static VillageLayoutProfile DefaultLayout => _defaultLayout;
        public static VillageLayoutProfile DwarfMiningLayout => _dwarfMiningLayout;

        /// <summary>초기 시설 TableId → 슬롯 역할. 카탈로그 밖 TableId(구 세이브 일반 오브젝트)는 false.</summary>
        public static bool TryGetSlotRole(int tableId, int primarySpecialFacilityId,
            out VillageLayoutSlotRole role)
        {
            if (tableId == primarySpecialFacilityId && tableId > 0)
            {
                role = VillageLayoutSlotRole.PrimarySpecial;
                return true;
            }
            switch (tableId)
            {
                case VillageInitialFacilityCatalog.VILLAGE_HUB_TABLE_ID:
                    role = VillageLayoutSlotRole.Hub;
                    return true;
                case VillageInitialFacilityCatalog.MERCHANT_STALL_TABLE_ID:
                    role = VillageLayoutSlotRole.Market;
                    return true;
                case VillageInitialFacilityCatalog.INN_TABLE_ID:
                    role = VillageLayoutSlotRole.Inn;
                    return true;
                case VillageInitialFacilityCatalog.FORGE_TABLE_ID:
                    role = VillageLayoutSlotRole.PrimarySpecial;
                    return true;
                case VillageInitialFacilityCatalog.SHRINE_TABLE_ID:
                    role = VillageLayoutSlotRole.Shrine;
                    return true;
                default:
                    role = default;
                    return false;
            }
        }
    }

    /// <summary>
    /// V4 (§7.2) 역할 슬롯 우선 배치기 — 순수 로직. 맵 점유 판정은 호출자가 predicate로 주입한다
    /// (VillageManager는 실제 맵, 정적 하니스는 합성 점유를 전달).
    /// 결정론: RNG 없이 지정 슬롯 → 슬롯 주변 링을 고정 순서로 스캔한다. 같은 seed·Profile이면
    /// 같은 점유 상태에서 항상 같은 타일이 나온다.
    /// </summary>
    public static class VillageSlotPlacer
    {
        /// <summary>
        /// 슬롯 우선 배치. 반환 false면 호출자가 PlacementPending을 유지한다 (기존 재시도 계약).
        /// isTileFree: 점유·경계 검사 포함한 배치 가능 판정 (true = 배치 가능).
        /// </summary>
        public static bool TryFindTile(int centerX, int centerY,
            VillageLayoutProfile layout, VillageLayoutSlotRole role,
            Func<int, int, bool> isTileFree, out int tileX, out int tileY)
        {
            tileX = 0;
            tileY = 0;
            if (layout.TryGetSlot(role, out VillageLayoutSlot slot) == false)
                return false;

            int slotX = centerX + slot.OffsetX;
            int slotY = centerY + slot.OffsetY;
            if (isTileFree(slotX, slotY) == true)
            {
                tileX = slotX;
                tileY = slotY;
                return true;
            }

            // 슬롯 점유 시 작은 반경 fallback — 링 r=1..MAX, 고정 스캔 순서(결정론).
            for (int r = 1; r <= VillageLayoutProfile.SLOT_FALLBACK_MAX_RADIUS; r++)
            {
                for (int dy = -r; dy <= r; dy++)
                {
                    for (int dx = -r; dx <= r; dx++)
                    {
                        if (Math.Max(Math.Abs(dx), Math.Abs(dy)) != r)
                            continue;   // 링 가장자리만
                        int x = slotX + dx;
                        int y = slotY + dy;
                        if (IsReservedForRoadOrPlaza(x, y, centerX, centerY, layout) == true)
                            continue;
                        if (isTileFree(x, y) == true)
                        {
                            tileX = x;
                            tileY = y;
                            return true;
                        }
                    }
                }
            }
            return false;
        }

        /// <summary>
        /// 축소된 도로/광장 예약 (§7.4). 지정 슬롯 자체는 예약과 무관하게 배치되고,
        /// fallback 후보만 예약 셀을 피한다 — 입구→중심 동선을 비워 두기 위함.
        /// </summary>
        public static bool IsReservedForRoadOrPlaza(int tileX, int tileY,
            int centerX, int centerY, VillageLayoutProfile layout)
        {
            int adx = Math.Abs(tileX - centerX);
            int ady = Math.Abs(tileY - centerY);

            if (layout.PlazaReserveRadius > 0
                && adx <= layout.PlazaReserveRadius && ady <= layout.PlazaReserveRadius)
                return true;

            if (layout.RoadReserveRadius > 0)
            {
                int hw = layout.RoadReserveHalfWidth;
                if (adx <= hw && ady <= layout.RoadReserveRadius) return true;
                if (ady <= hw && adx <= layout.RoadReserveRadius) return true;
            }
            return false;
        }
    }
}
