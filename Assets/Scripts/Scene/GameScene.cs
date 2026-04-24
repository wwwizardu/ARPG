using UnityEngine;
using System.Collections;
using ARPG.Component;
using ARPG.Factory;
using Cysharp.Threading.Tasks;

namespace ARPG.Scene
{
    public class GameScene : Base.SceneBase
    {
        [SerializeField] private CameraController _cameraController;
        [SerializeField] private Transform _monsterRoot;
        [SerializeField] private Transform _npcRoot;
        [SerializeField] private Transform _buildingRoot;

        public Transform MonsterRoot => _monsterRoot;
        public Transform NpcRoot => _npcRoot;
        public Transform BuildingRoot => _buildingRoot;

        protected override IEnumerator OnInitialize()
        {
            yield return base.OnInitialize();

            // EntityFactory를 통해 플레이어 생성 (Addressable 비동기)
            yield return CreatePlayerAsync().ToCoroutine();

            AR.s.OnLoadSceneComplete(this);

            Debug.Log("GameScene initialized.");
        }

        private async UniTask CreatePlayerAsync()
        {
            var (playerEntityId, player) = await EntityFactory.CreatePlayer(1, Vector3.zero);
            if (playerEntityId < 0 || player == null)
            {
                Debug.LogError("Failed to create player entity.");
                return;
            }

            _cameraController.Initialize(player);
            AR.s.Player.AddPlayer(player);

            AR.s.Map.CreateMap(12345, player.transform.position);

            // 맵 생성 완료 후 청크 로더 활성화
            if (AR.s.Component.TryGetComponent<MapChunkLoaderComponent>(playerEntityId, out var chunkLoader))
            {
                chunkLoader.IsInitialized = true;
                AR.s.Component.SetComponent(playerEntityId, chunkLoader);
            }
        }
    }
}
