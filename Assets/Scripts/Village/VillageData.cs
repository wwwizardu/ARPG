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
        public float PositionX;
        public float PositionY;

        // 스폰/쿨타임 상태 (세이브 대상)
        public bool HasBeenPopulated;        // 기본 NPC가 한번이라도 스폰된 적 있는지
        public float DepletedAt;             // 전멸 감지 시각 (게임 시간). 0이면 정상

        // Phase A: 자원 생산 상태
        public int StoneTimer;               // Stone 5시간 누적 카운터 (0~4)
        public int HungerHoursAccumulated;   // Food 0 유지 누적 시간 (24h 넘으면 경고 1회)
        public float RegisteredAt;           // 마을 등록 게임 시각

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

        // Phase D: 상점 매물 풀 (게임시간 24h마다 재롤). MerchantStall 보유 마을만 의미 있음.
        public List<MerchantStockEntry> MerchantStock = new();
        public float LastMerchantRollGameTime;

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
            DepletedAt = 0f;
            StoneTimer = 0;
            HungerHoursAccumulated = 0;
            RegisteredAt = 0f;
            HasCampfire = false;
            FirstBuildStartedAt = -1f;
            CurrentBuildTableId = 0;
            CurrentBuildStartedAt = -1f;
        }
    }
}
