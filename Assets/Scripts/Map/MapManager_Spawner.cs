using UnityEngine;

namespace ARPG.Map
{
    public partial class MapManager : MonoBehaviour
    {
        private void OnResetSpawner()
        {
        }

        public void OnChunkActivated(Vector2Int chunkCoord, MapChunkData chunkData)
        {
            if (Context.Kind == MapKind.Dungeon) return;
            AR.s.Dungeon.RefreshEntranceChunk(chunkCoord, true);
            if (AR.s.Monster != null)
            {
                AR.s.Monster.ActivateChunkMonsters(chunkCoord);
            }

            // NPC 스폰/복원 (NpcManager의 SaveData 기반)
            if (AR.s.Npc != null)
            {
                AR.s.Npc.OnChunkActivated(chunkCoord);
            }

            // 건물 엔티티 스폰/복원 (BuildingManager의 SaveData 기반)
            if (AR.s.Building != null)
            {
                AR.s.Building.OnChunkActivated(chunkCoord);
            }

            // 광맥 굴림·복원 (WORLD_ORE_VEIN_PLAN §4·§6.1). 이 청크의 타일 데이터만 쓴다
            AR.s.OreVein.OnChunkActivated(chunkCoord, chunkData);
        }

        public void OnChunkDeactivated(Vector2Int chunkCoord)
        {
            if (Context.Kind == MapKind.Dungeon) return;
            AR.s.Dungeon.RefreshEntranceChunk(chunkCoord, false);
            if (AR.s.Monster != null)
            {
                AR.s.Monster.DeactivateChunkMonsters(chunkCoord);
            }

            // NPC 상태 저장 및 비활성화 (NpcManager의 SaveData 기반)
            if (AR.s.Npc != null)
            {
                AR.s.Npc.OnChunkDeactivated(chunkCoord);
            }

            // 건물 엔티티 저장 및 비활성화 (BuildingManager의 SaveData 기반)
            if (AR.s.Building != null)
            {
                AR.s.Building.OnChunkDeactivated(chunkCoord);
            }

            // 광맥 엔티티만 정리한다. 기록은 지우거나 다시 굴리지 않는다 (WORLD_ORE_VEIN_PLAN §8.2)
            AR.s.OreVein.OnChunkDeactivated(chunkCoord);
        }
    }
}
