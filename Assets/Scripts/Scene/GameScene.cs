using UnityEngine;
using System.Collections;

namespace ARPG.Scene
{
    public class GameScene : Base.SceneBase
    {
        [SerializeField] private CameraController _cameraController;

        protected override IEnumerator OnInitialize()
        {
            yield return base.OnInitialize();

            Debug.Log("GameScene initialized.");
        }
        
        
    }
}


