using UnityEngine;
using System.Collections;
using ARPG.Component;
using ARPG.Creature;

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

            GameObject playerObject = Instantiate(_playerPrefab);
            if (playerObject != null)
            {
                ArpgPlayer player = playerObject.GetComponent<ArpgPlayer>();
                if (player != null)
                {
                    player.Initialize();
                    player.Load(1); // 임시로 ID 1 사용
                    player.InitializeECSComponents();

                    _cameraController.Initialize(player);

                    AR.s.Player.AddPlayer(player);
                }
            }
            else
            {
                Debug.LogError("Failed to instantiate player object.");
            }

            AR.s.Map.CreateMap(12345, playerObject.transform.position);

            // 맵 생성 완료 후 청크 로더 활성화
            int playerEntityId = AR.s.Data.CurrentPlayerEntityId;
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


