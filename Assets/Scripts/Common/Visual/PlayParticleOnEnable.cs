using UnityEngine;

namespace ARPG.Visual
{
    /// <summary>
    /// GameObject가 활성화될 때마다 ParticleSystem을 재생한다.
    /// AddressablePool 사이클(Return → Get)에서 Play On Awake가 다시 동작하지 않는 문제를 해결.
    /// 풀링되는 VFX 프리팹 루트에 부착해 두면 모든 자식 파티클이 활성화 시 자동 재생.
    /// </summary>
    [DisallowMultipleComponent]
    public class PlayParticleOnEnable : MonoBehaviour
    {
        [SerializeField] private bool _includeChildren = true;
        [SerializeField] private bool _stopAndClearOnDisable = true;

        private ParticleSystem[] _systems;

        private void Awake()
        {
            _systems = _includeChildren
                ? GetComponentsInChildren<ParticleSystem>(true)
                : GetComponents<ParticleSystem>();
        }

        private void OnEnable()
        {
            if (_systems == null)
                return;

            for (int i = 0; i < _systems.Length; i++)
            {
                ParticleSystem ps = _systems[i];
                if (ps == null)
                    continue;

                ps.Clear(true);
                ps.Play(true);
            }
        }

        private void OnDisable()
        {
            if (_stopAndClearOnDisable == false || _systems == null)
                return;

            for (int i = 0; i < _systems.Length; i++)
            {
                ParticleSystem ps = _systems[i];
                if (ps == null)
                    continue;

                ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            }
        }
    }
}
