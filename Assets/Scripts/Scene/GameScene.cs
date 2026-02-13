using UnityEngine;
using System.Collections;
using ARPG.Component;
using ARPG.Creature;
using ARPG.Factory;

namespace ARPG.Scene
{
    public class GameScene : Base.SceneBase
    {
        [SerializeField] private CameraController _cameraController;
        [SerializeField] private Transform _monsterRoot;
        [SerializeField] private GameObject _playerPrefab;

        public Transform MonsterRoot => _monsterRoot;

        protected override IEnumerator OnInitialize()
        {
            yield return base.OnInitialize();

            // EntityFactory를 통해 플레이어 생성 (Initialize + Load + ECS 컴포넌트 추가)
            int playerEntityId = EntityFactory.CreatePlayer(1, _playerPrefab, Vector3.zero, out var player);
            if (playerEntityId < 0 || player == null)
            {
                Debug.LogError("Failed to create player entity.");
                yield break;
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

            AR.s.OnLoadSceneComplete(this);

            Debug.Log("GameScene initialized.");
        }
    }
}


