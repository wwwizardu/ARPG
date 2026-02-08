using ARPG.Component;
using UnityEngine;

namespace ARPG.Systems
{
    public struct System_MapChunkLoader : IFixedUpdateSystem
    {
        public int Priority => 120;
        public float UpdateInterval => 0.2f;

        public void OnCreate()
        {
            Debug.Log("System_MapChunkLoader Created");
        }

        public void OnReset()
        {
            Debug.Log("System_MapChunkLoader Reset called");
        }

        public readonly void OnFixedUpdate(float inFixedDeltaTime)
        {
            if (AR.s.Map == null)
                return;

            SparseSet<MapChunkLoaderComponent> pool = AR.s.Component.GetComponentPool<MapChunkLoaderComponent>();

            for (int i = 0; i < pool.Count; i++)
            {
                int entityId = pool.GetEntityId(i);
                MapChunkLoaderComponent loader = pool.GetByIndex(i);

                if (loader.IsInitialized == false)
                    continue;

                if (AR.s.Component.TryGetComponent<TransformComponent>(entityId, out var transform) == false)
                    continue;

                Vector3 playerPosition = new Vector3(transform.Position.x, transform.Position.y, 0f);

                AR.s.Map.UpdateChunksAroundPlayer(playerPosition);
            }
        }
    }
}
