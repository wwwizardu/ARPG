using System.Collections;
using UnityEngine;

namespace ARPG.Base
{
    public class SceneBase : MonoBehaviour
    {
        private Coroutine _initializeCoroutine;

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
            yield return null; // Placeholder for initialization logic
        }
    }
}


