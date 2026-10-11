#nullable enable
using ARPG.Manager;
using UnityEngine;

namespace ARPG.Visual
{
    /// <summary>
    /// 이펙트 프리팹 루트에 부착되는 수명 + 파티클 재생 통합 컴포넌트.
    /// EffectManager가 풀에서 꺼낼 때 Bind로 키/InstanceId 주입.
    /// Once: 자동 산정된 duration 후 풀로 반환. Loop: 외부 Stop 호출 시까지 유지.
    /// OnEnable에서 파티클 자동 재생 (Bind 호출 여부와 무관) — 풀 재사용 사이클 대응.
    /// </summary>
    [DisallowMultipleComponent]
    public class EffectInstance : MonoBehaviour
    {
        /// <summary>true=루프(외부 Stop 호출까지 유지), false=단발(duration 후 자동 반환).</summary>
        [SerializeField] private bool _isLoop = false;
        /// <summary>0보다 크면 자동 산정 duration을 무시하고 이 값을 사용. _isLoop=false일 때만 의미 있음.</summary>
        [SerializeField] private float _overrideDuration = 0f;
        /// <summary>자동 산정된 duration에 곱하는 안전 계수 (trail/sub-emitter 보정).</summary>
        [SerializeField] private float _autoDurationSafetyFactor = 1.2f;
        /// <summary>OnDisable 시 파티클을 정지+클리어. 풀 반환 시 잔여 파티클 제거 목적.</summary>
        [SerializeField] private bool _stopAndClearOnDisable = true;

        private ParticleSystem[] _particles = System.Array.Empty<ParticleSystem>();

        private string _addressableKey = string.Empty;
        private int _instanceId = -1;
        private EffectManager? _manager;

        private float _duration;
        private float _elapsed;
        private bool _isActive;
        private Vector3 _initialScale;

        public bool IsLoop => _isLoop;
        public int InstanceId => _instanceId;
        public string AddressableKey => _addressableKey;

        private void Awake()
        {
            _particles = GetComponentsInChildren<ParticleSystem>(true);
            _initialScale = transform.localScale;
        }

        private void OnEnable()
        {
            for (int i = 0; i < _particles.Length; i++)
            {
                ParticleSystem ps = _particles[i];
                if (ps == null)
                    continue;

                ps.Clear(true);
                ps.Play(true);
            }
        }

        private void OnDisable()
        {
            if (_stopAndClearOnDisable == false)
                return;

            for (int i = 0; i < _particles.Length; i++)
            {
                ParticleSystem ps = _particles[i];
                if (ps == null)
                    continue;

                ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            }
        }

        /// <summary>풀 재사용마다 프리팹 기준 배율을 적용해 이전 스폰의 크기가 남지 않게 한다.</summary>
        public void SetScale(float? scale)
        {
            transform.localScale = scale.HasValue == true
                ? _initialScale * Mathf.Max(0f, scale.Value)
                : _initialScale;
        }

        /// <summary>
        /// EffectManager가 Spawn 시점에 호출. 키/ID/매니저 참조를 주입하고 수명 계산 시작.
        /// </summary>
        public void Bind(string key, int instanceId, EffectManager manager)
        {
            _addressableKey = key;
            _instanceId = instanceId;
            _manager = manager;

            _duration = _overrideDuration > 0f ? _overrideDuration : CalculateAutoDuration() * _autoDurationSafetyFactor;
            _elapsed = 0f;
            _isActive = true;
        }

        /// <summary>
        /// 매니저가 호출. InstanceId 무효화 후 풀로 반환 통지.
        /// 직접 호출하지 말 것 — 외부에서는 AR.s.Effect.Stop(handle) 사용.
        /// </summary>
        public void ReturnToPool()
        {
            if (_isActive == false)
                return;

            _isActive = false;
            _instanceId = -1;
            // 매니저가 풀 반환 처리. _manager는 매니저 측에서 호출하므로 여기서 다시 부르지 않음.
        }

        private void Update()
        {
            if (_isActive == false || _isLoop)
                return;

            _elapsed += Time.deltaTime;
            if (_elapsed >= _duration)
            {
                if (_manager != null)
                {
                    _manager.ReturnInternal(this);
                }
            }
        }

        private float CalculateAutoDuration()
        {
            float maxLifetime = 0f;
            for (int i = 0; i < _particles.Length; i++)
            {
                ParticleSystem ps = _particles[i];
                if (ps == null)
                    continue;

                var main = ps.main;
                float startLifetime = GetMinMaxMax(main.startLifetime);
                float candidate = main.duration + startLifetime;
                if (candidate > maxLifetime)
                    maxLifetime = candidate;
            }

            // 파티클이 하나도 없으면 짧은 기본값
            return maxLifetime > 0f ? maxLifetime : 1f;
        }

        private static float GetMinMaxMax(ParticleSystem.MinMaxCurve curve)
        {
            switch (curve.mode)
            {
                case ParticleSystemCurveMode.Constant:
                    return curve.constant;
                case ParticleSystemCurveMode.TwoConstants:
                    return Mathf.Max(curve.constantMin, curve.constantMax);
                case ParticleSystemCurveMode.Curve:
                    return curve.curveMultiplier;
                case ParticleSystemCurveMode.TwoCurves:
                    return curve.curveMultiplier;
                default:
                    return curve.constant;
            }
        }
    }
}
