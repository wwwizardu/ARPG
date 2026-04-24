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
            if (AR.s.Monster == null)
                return;

            // 이미 스폰된 청크는 기존 몬스터 활성화만 수행
            // 최초 스폰은 System_MonsterSpawn이 담당
            if (AR.s.Monster.HasChunkSpawned(chunkCoord))
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
        }

        public void OnChunkDeactivated(Vector2Int chunkCoord)
        {
            if (AR.s.Monster == null)
                return;

            AR.s.Monster.DeactivateChunkMonsters(chunkCoord);

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
        }
    }
}
