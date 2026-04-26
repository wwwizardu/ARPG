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

        // Phase B: 현재 진행 중인 배치 작업 (마을당 1건). 0 = 미착수.
        public int CurrentBuildTableId;
        public float CurrentBuildStartedAt = -1f;
        public int CurrentBuildTileX;
        public int CurrentBuildTileY;
        public int CurrentBuildReservedWood;
        public int CurrentBuildReservedStone;

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
