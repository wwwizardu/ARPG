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
        }

        public void OnChunkDeactivated(Vector2Int chunkCoord)
        {
            if (AR.s.Monster == null)
                return;

            AR.s.Monster.DeactivateChunkMonsters(chunkCoord);
        }
    }
}
