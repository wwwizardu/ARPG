#nullable enable
using UnityEngine;

namespace ARPG.Village
{
    /// <summary>
    /// 마을 안 NPC 표현에 쓰는 여관·마을 영역 조회 (NPC_SIMULATION_DESIGN §3.1 D13~D15).
    /// 실체화 여부와 관계없이 VillageData 저장 기록이 정본이다 — ECS 시설 엔티티를 찾지 않는다.
    /// </summary>
    public partial class VillageManager
    {
        /// <summary>
        /// 원정 부상자가 여관 안에서 회복하는 마을인지 (D13): 마을이 있고, 사라진 터가 아니며, Inn 서비스 시설 기록이 있다(배치 대기 포함).
        /// </summary>
        public bool IsInnHome(int villageId)
        {
            if (_villages.TryGetValue(villageId, out VillageData? village) == false)
                return false;
            if (IsAbandonedSite(village) == true)
                return false;
            return VillageConstructionLedger.CountByService(village, ProvidedService.Inn) > 0;
        }

        /// <summary>
        /// 배치된 여관의 문 위치 = 여관 건물 가로 가운데의 바로 아래 칸 중심. 아직 배치 전(PlacementPending·좌표 미할당)이면 false.
        /// 그 칸이 막혔을 때의 보정은 이동 반경을 아는 호출자(NpcManager)가 한다.
        /// </summary>
        public bool TryGetInnDoorPosition(int villageId, out Vector2 door)
        {
            door = Vector2.zero;
            if (_villages.TryGetValue(villageId, out VillageData? village) == false)
                return false;

            for (int i = 0; i < village.PlacedObjects.Count; i++)
            {
                PlacedObjectSaveData placed = village.PlacedObjects[i];
                if (placed.PlacementPending == true || VillageConstructionPosition.IsAssigned(placed) == false)
                    continue;

                Tables.BuildableItemTable? table = AR.s.Data.GetBuildableItem(placed.TableId);
                if (table == null || ((ProvidedService)table.ProvidedService & ProvidedService.Inn) == 0)
                    continue;

                int width = Mathf.Max(1, table.Size_Width);
                door = new Vector2(placed.TileX + width * 0.5f, placed.TileY - 0.5f);
                return true;
            }
            return false;
        }

        /// <summary>
        /// 좌표가 어느 마을 영역 안인지 (D14 — 이동 중에 실체화된 NPC가 그 자리에 멈출지, 걸을지 가른다).
        /// 마을 영역 = 저장된 경계(Bounds) 사각형. 경계가 아직 없는 마을은 Stage 반경을 쓴다. 사라진 터도 포함한다.
        /// 마을 수만큼 순회하므로 실체화 같은 드문 순간에만 부른다.
        /// </summary>
        public bool IsInsideVillageArea(Vector2 position)
        {
            for (int i = 0; i < _villageList.Count; i++)
            {
                VillageData village = _villageList[i];
                if (village.BoundsW > 0 && village.BoundsH > 0)
                {
                    if (position.x >= village.BoundsX && position.x < village.BoundsX + village.BoundsW &&
                        position.y >= village.BoundsY && position.y < village.BoundsY + village.BoundsH)
                        return true;
                    continue;
                }

                int radius = GetBoundsRadius(village.Stage);
                if (radius <= 0)
                    continue;
                if (Mathf.Abs(position.x - village.PositionX) < radius &&
                    Mathf.Abs(position.y - village.PositionY) < radius)
                    return true;
            }
            return false;
        }
    }
}
