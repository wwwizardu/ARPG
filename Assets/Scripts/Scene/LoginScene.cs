using System.Collections;
using UnityEngine;

namespace ARPG.Scene
{
    public class LoginScene : Base.SceneBase
    {
        protected override IEnumerator OnInitialize()
        {
            yield return base.OnInitialize();

            Debug.Log("LoginScene initialized.");
        }
    }
}


