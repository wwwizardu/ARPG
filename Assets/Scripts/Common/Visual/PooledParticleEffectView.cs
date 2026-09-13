using UnityEngine;

namespace ARPG.Visual
{
    [DisallowMultipleComponent]
    public sealed class PooledParticleEffectView : MonoBehaviour
    {
        [SerializeField] private ParticleSystem[] _systems;

        public void Restart()
        {
            if (_systems == null)
                return;

            for (int i = 0; i < _systems.Length; i++)
            {
                ParticleSystem system = _systems[i];
                if (system == null)
                    continue;

                system.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
                system.Play(false);
            }
        }

        public void ResetEffect()
        {
            if (_systems == null)
                return;

            for (int i = 0; i < _systems.Length; i++)
            {
                ParticleSystem system = _systems[i];
                if (system != null)
                    system.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
            }
        }

        private void OnEnable()
        {
            Restart();
        }

        private void OnDisable()
        {
            ResetEffect();
        }
    }
}
