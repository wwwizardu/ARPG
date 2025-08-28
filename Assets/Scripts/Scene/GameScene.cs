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

            GameObject playerObject = Instantiate(_playerPrefab);
            if (playerObject != null)
            {
                ArpgPlayer player = playerObject.GetComponent<ArpgPlayer>();
                if (player != null)
                {
                    player.Initialize();
                    player.LoadData(1); // 임시로 ID 1 사용
                    _cameraController.Initialize(player);

                    AR.s.Player.AddPlayer(player);
                }
            }
            else
            {
                Debug.LogError("Failed to instantiate player object.");
            }

            AR.s.Map.CreateMap(12345, playerObject.transform.position);

            AR.s.OnLoadSceneComplete(this);

            Debug.Log("GameScene initialized.");
        }
        
        
    }
}


