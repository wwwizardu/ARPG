using System.Collections;
using UnityEngine;

namespace ARPG.Base
{
    public class SceneBase : MonoBehaviour
    {
        public enum SceneType
        {
            None,
            Title,
            Login,
            Game,
        }

        [SerializeField] protected SceneType _sceneType = SceneType.None;
        private Coroutine _initializeCoroutine;

        public SceneType CurrentSceneType => _sceneType;

        protected void Start()
        {
            if (_initializeCoroutine != null)
            {
                StopCoroutine(_initializeCoroutine);
            }

            _initializeCoroutine = StartCoroutine(OnInitialize());

        }

        protected virtual IEnumerator OnInitialize()
        {
            while (AR.s.IsInitialized == false)
            {
                yield return null; // Wait until AR is initialized
            }

            AR.s.OnSceneLoadStart(this);
        }
    }
}


