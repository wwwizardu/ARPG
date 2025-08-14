using UnityEngine;
using System.Collections;

namespace ARPG.Base
{
    public class GameScene : SceneBase
    {
        [SerializeField] private CameraController _cameraController;

        protected override IEnumerator OnInitialize()
        {
            yield return base.OnInitialize();

            Debug.Log("GameScene initialized.");
        }
        
        
    }
}


