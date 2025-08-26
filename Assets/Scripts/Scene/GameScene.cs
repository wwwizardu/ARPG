using UnityEngine;
using System.Collections;
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

            while (AR.s.IsInitialized == false)
            {
                yield return null; // Wait until AR is initialized
            }

            AR.s.Map.UpdateChunksAroundPlayer(Vector3.zero);

            GameObject playerObject = Instantiate(_playerPrefab);
            if (playerObject != null)
            {
                ArpgPlayer player = playerObject.GetComponent<ArpgPlayer>();
                if (player != null)
                {
                    player.Initialize();
                    _cameraController.Initialize(player);

                    AR.s.Player.AddPlayer(player);
                }
            }
            else
            {
                Debug.LogError("Failed to instantiate player object.");
            }

            AR.s.OnLoadSceneComplete(this);

            Debug.Log("GameScene initialized.");
        }
        
        
    }
}


