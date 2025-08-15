using UnityEngine;
using System.Collections;
using ARPG.Creature;

namespace ARPG.Scene
{
    public class GameScene : Base.SceneBase
    {
        [SerializeField] private CameraController _cameraController;
        [SerializeField] private GameObject _playerPrefab;

        protected override IEnumerator OnInitialize()
        {
            yield return base.OnInitialize();

            while (AR.s.IsInitialized == false)
            {
                yield return null; // Wait until AR is initialized
            }

            AR.s.Map.UpdateChunksAroundPlayer(Vector3.zero);

            GameObject playerObject = Instantiate(_playerPrefab);
            if(playerObject != null)
            {
                ArpgPlayer _player = playerObject.GetComponent<ArpgPlayer>();
                if (_player != null)
                {
                    _player.Initialize();
                }
            }
            else
            {
                Debug.LogError("Failed to instantiate player object.");
            }
            

            Debug.Log("GameScene initialized.");
        }
        
        
    }
}


