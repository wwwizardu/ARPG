#nullable enable
using System;

namespace ARPG.Village
{
    /// <summary>
    /// Phase D: 마을 배치 오브젝트 1개의 영구 세이브 데이터.
    /// VillageData.PlacedObjects에 누적. 청크 활성/비활성과 무관하게 보존.
    /// 실제 ECS 컴포넌트(PlacedObjectComponent)는 활성 청크에서만 부착, 로드 시 재구성.
    ///
    /// PlacedObjectTypeIds(ID-only 카운트)와 양립 — 카운트 검증/Tier 승격은 그대로 List int 사용,
    /// 위치/HP/쿨다운 등 엔티티 상태는 이 클래스가 정본.
    /// </summary>
    [Serializable]
    public class PlacedObjectSaveData
    {
        public int TableId;
        public int TileX;
        public int TileY;
        public int Hp;
        public int MaxHp;
        public float LastUseGameTime;   // Shrine 등 쿨다운 추적. 0 = 미사용
    }
}
