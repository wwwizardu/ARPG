#nullable enable
using System.Collections.Generic;
using Newtonsoft.Json;
using UnityEngine;

namespace ARPG.Village
{
    public class VillageData
    {
        public int VillageId;
        public int TableId;                  // VillageTable 참조 (0이면 기본 스폰 로직 없음)
        public Dictionary<GlobalEnum.ItemType, int> Resources = new();
        public Dictionary<GlobalEnum.ItemType, int> ResourceCaps = new();  // 빈 경우 기본 50
        public VillageStage Stage;
        public int Population;
        // V1: 생성/마이그레이션 시 한 번 확정한 Profile ID. 이후 Race/Biome 변화로 재계산하지 않는다.
        public string VillageProfileId = string.Empty;
        // History에 연결되지 않은 수제작 BaseTown의 고정 경제 인구. -1 = 구 세이브 미보정.
        public int FixedPopulation = -1;
        public float PositionX;
        public float PositionY;

        // 스폰 상태 (세이브 대상). 재스폰/전멸(DepletedAt)은 NPC 단순화(D5/D7)로 제거 — 초기 코호트 1회만.
        public bool HasBeenPopulated;        // 초기 모험가 코호트가 스폰된 적 있는지

        // R1: 정착지 연대기 ↔ 마을 링크. -1 = 미연결 (연대기 SettlementId, PROCEDURAL §3.4)
        public int SettlementId = -1;

        // Phase A: 자원 생산 상태
        public int StoneTimer;               // Stone 5시간 누적 카운터 (0~4)
        public int IronTimer;                // Iron 10시간 누적 카운터 (D3 — 인구 기반 생산)
        public int GoldTimer;                // Gold 20시간 누적 카운터 (D3 — 인구 기반 생산)
        public int HungerHoursAccumulated;   // Food 0 유지 누적 시간 (24h 넘으면 경고 1회)
        public float RegisteredAt;           // 마을 등록 게임 시각

        // 추상/실시간 생산 공통 원장. 정수 자원으로 반영되지 않은 소수 생산량을 보존한다.
        public float FoodCarry;
        public float WoodCarry;
        public float StoneCarry;
        public float IronCarry;
        public float GoldCarry;

        // 추상 건설 clock 및 절차적 역사 초기 발전 1회 적용 플래그.
        public float LastConstructionSimGameTime;
        public float LastConstructionTierCheckGameTime;
        public bool InitialDevelopmentApplied;

        // V3 (VILLAGE_REWORK_PLAN §3.5/§5.3): 적용된 Stage 누적 보상의 MilestoneKey 목록.
        // 초기 보상 중복 방지의 정본 — Stage만 보고 부작용을 재적용하지 않는다.
        public List<string> AppliedGrowthMilestones = new();

        // V3: UnlockShopTier 누적 결과. 0 = 미해금(구 세이브). 슬롯 수·재고 풀 해금은 V5 TradeProfile 범위.
        public int ShopTier;

        // §5.3 PendingInitialPlacements는 별도 리스트가 아니라 PlacedObjects[].PlacementPending이 정본이다.
        // 완공 확정 + 좌표 미할당 레코드는 materialize(RestorePlacedObjectsFromData)가 결정론적으로 재시도한다.

        // Phase A: 첫 Campfire 제작 상태 (Phase B에서 PlacedObjectTypeIds + CurrentBuild*로 일반화)
        // 구 세이브 호환 위해 필드 유지. VillageManager.Load에서 마이그레이션 후 새 필드만 사용.
        public bool HasCampfire;
        public float FirstBuildStartedAt = -1f;   // -1 = 미착수
        public int FirstBuildTileX;
        public int FirstBuildTileY;

        // (Legacy) 단일 빌드 task — Step C 이전 세이브 호환용. 신규 코드는 ActiveBuildTasks 사용.
        // 신규 세이브에서는 0/-1로 비워둠. 구 세이브 로드 시 자동 마이그레이션됨.
        public int CurrentBuildTableId;
        public float CurrentBuildStartedAt = -1f;
        public int CurrentBuildTileX;
        public int CurrentBuildTileY;
        public int CurrentBuildReservedWood;
        public int CurrentBuildReservedStone;

        // BUILD_PRIORITY_DESIGN.md Step B/C — N개 동시 진행 task 스냅샷.
        // 마을 NPC 수만큼 동시 빌드 가능. 세이브 시 task entity 풀 → 이 리스트로 미러링,
        // 로드 시 각 스냅샷마다 task entity를 발급한다.
        public List<BuildTaskSnapshot> ActiveBuildTasks = new();

        // Phase B: 완성된 오브젝트 TableId 누적 (중복 허용 — 같은 종류 여러 개 카운트)
        public List<int> PlacedObjectTypeIds = new();

        // Phase C: 마을 경계 (Tier 승격 시 확장). VillageComponent.Bounds 미러 — 세이브 정본.
        public int BoundsX;
        public int BoundsY;
        public int BoundsW;
        public int BoundsH;

        // Phase C: 위협도 (Phase F에서 본격 사용. C는 0 고정)
        public float ThreatLevel;

        // Phase C: 벽 세그먼트 통계
        public int WallSegmentCount;
        public int CompletedWallSegments;

        // Phase C: 벽 빌더 활성 플래그 (WallPlanRequestTag 미러)
        public bool WallPlanRequested;

        // Phase C: 세그먼트 영구 데이터 (활성 청크 진입 시 ECS 컴포넌트로 복원)
        public List<WallSegmentSaveData> WallSegments = new();

        // Phase D: 배치 오브젝트 영구 데이터 (좌표/HP/쿨다운). 활성 청크에서 PlacedObjectComponent로 복원.
        // PlacedObjectTypeIds(ID-only 카운트)와 양립 — 카운트는 그대로, 위치/상태는 이 리스트.
        public List<PlacedObjectSaveData> PlacedObjects = new();

        // Phase D: 상점 매물 풀. MerchantStall 보유 마을만 의미 있음.
        // V5: 재입고 회차(MerchantRestockCycle)가 재롤 게이트 정본 — 같은 회차는 같은 재고(§8.5).
        public List<MerchantStockEntry> MerchantStock = new();
        public float LastMerchantRollGameTime;
        // V5: 마지막 롤의 재입고 회차. -1 = 구 세이브/미롤 (다음 상점 진입 시 결정론 재롤).
        public int MerchantRestockCycle = -1;

        // NPC_AMBITION_DESIGN §7.2, §12 — NPC 구매의 상점 보호(재고 쪽) 원장. 구 세이브는 필드가 없어 기본값으로 로드된다.
        // NpcPurchaseCount가 속한 재입고 회차. 현재 회차와 다르면 카운트를 0으로 보고 새로 센다. -1 = 기록 없음.
        public int NpcPurchaseCycle = -1;
        // NpcPurchaseCycle 회차의 NPC 구매 건수 (매물 종류의 25%, 최소 1·최대 3 제한).
        public int NpcPurchaseCount;
        // 마지막 NPC 구매 트랜잭션 게임시각 (상점당 6시간 1건 제한). -1 = 없음.
        // 모험가 매입품 슬롯과 품절 구매자 표시는 MerchantStock 엔트리(SlotKind/NpcBuyerName)에 들어 있어
        // 재입고 롤의 Clear와 함께 사라진다.
        public float LastNpcTradeGameTime = -1f;

        [JsonIgnore]
        public List<int> NpcEntityIds = new();

        [JsonIgnore]
        public int EntityId = -1;             // Village 엔티티 ID (런타임 재발급)

        [JsonIgnore]
        public Vector2 Position
        {
            get => new Vector2(PositionX, PositionY);
            set { PositionX = value.x; PositionY = value.y; }
        }

        public VillageData(int villageId, Vector2 position)
        {
            VillageId = villageId;
            Position = position;
            Stage = VillageStage.Settlement;
            Population = 0;
            TableId = 0;
            HasBeenPopulated = false;
            StoneTimer = 0;
            HungerHoursAccumulated = 0;
            RegisteredAt = 0f;
            LastConstructionSimGameTime = 0f;
            LastConstructionTierCheckGameTime = 0f;
            InitialDevelopmentApplied = false;
            HasCampfire = false;
            FirstBuildStartedAt = -1f;
            CurrentBuildTableId = 0;
            CurrentBuildStartedAt = -1f;
        }
    }
}
