#nullable enable
using Newtonsoft.Json;

namespace ARPG.Village
{
    /// <summary>
    /// 건물 엔티티의 영구 저장 데이터.
    /// 청크 비활성화 시 BuildingComponent에서 스냅샷하여 보관하고,
    /// 청크 재활성화 시 이 데이터로 엔티티를 복원한다.
    /// </summary>
    public class BuildingSaveData
    {
        /// <summary>BuildableItemTable.Id (건물 종류). EntityFactory/BuildingFactory가 이 값으로 테이블 조회.</summary>
        public int TableId;

        /// <summary>소속 마을 Id. 마을 소속이 아니면 -1. VillageManager.FindVillageContaining 결과.</summary>
        public int VillageId;

        /// <summary>건물이 차지한 타일의 월드 X 좌표 (타일 단위). 멀티타일은 좌측 하단 기준.</summary>
        public int WorldTileX;

        /// <summary>건물이 차지한 타일의 월드 Y 좌표 (타일 단위). 멀티타일은 좌측 하단 기준.</summary>
        public int WorldTileY;

        /// <summary>현재 HP. 0 이하 = 파괴 상태 (Load 시 제외됨).</summary>
        public int CurrentHp;

        /// <summary>ECS EntityId. BuildingManager가 발급·관리하며 청크 언로드/로드 시 재사용.</summary>
        public int EntityId;

        /// <summary>런타임 활성화 상태. true = GameObject 존재, false = 저장 상태 (청크 언로드 또는 미스폰).</summary>
        public bool IsActive;

        /// <summary>스폰 진행 중 플래그 (async CreateBuilding 대기 구간). 이중 스폰 차단용. 세이브 대상 아님.</summary>
        [JsonIgnore]
        public bool IsSpawning;

        public BuildingSaveData(int tableId, int worldTileX, int worldTileY)
        {
            TableId = tableId;
            WorldTileX = worldTileX;
            WorldTileY = worldTileY;
            VillageId = -1;
            CurrentHp = 0;
            EntityId = -1;
            IsActive = false;
        }

        // Newtonsoft.Json 역직렬화용 기본 생성자
        public BuildingSaveData() { }
    }
}
