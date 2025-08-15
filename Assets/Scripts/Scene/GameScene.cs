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

            while (AR.s.IsInitialized == false)
            {
                yield return null; // Wait until AR is initialized
            }

            //AR.s.Map.GetOrCreateChunk

            Debug.Log("GameScene initialized.");
        }
        
        
    }
}


