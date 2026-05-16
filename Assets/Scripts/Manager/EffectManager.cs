#nullable enable
using System.Collections.Generic;
using ARPG.Utility;
using ARPG.Visual;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace ARPG.Manager
{
    /// <summary>
    /// 핸들. 외부에서 루프 이펙트를 종료할 때 사용.
    /// InstanceId가 -1이거나 매니저에 없으면 무효 핸들.
    /// </summary>
    public readonly struct EffectHandle
    {
        public readonly int InstanceId;
        public EffectHandle(int instanceId) { InstanceId = instanceId; }
        public static EffectHandle Invalid => new EffectHandle(-1);
        public bool IsValid => InstanceId >= 0;
    }

    /// <summary>
    /// 이펙트 스폰/반환/씬 정리 관리자.
    /// 내부적으로 AddressablePool을 사용해 GameObject 풀링.
    /// 루프 이펙트는 Stop이 호출될 때까지 유지, 단발은 EffectInstance가 자동 반환.
    /// </summary>
    public class EffectManager : MonoBehaviour
    {
        private readonly Dictionary<int, EffectInstance> _active = new Dictionary<int, EffectInstance>(64);
        private readonly List<EffectInstance> _resetBuffer = new List<EffectInstance>(64);
        private Transform? _root;
        private int _nextInstanceId;

        public void Initialize()
        {
            _root = transform;
            _nextInstanceId = 0;
            AddressablePool.SetPoolRoot(_root);
        }

        /// <summary>
        /// 씬 전환 / 게임 리셋 시 호출. 활성 이펙트 전부 풀로 반환.
        /// AddressablePool 자체는 유지 (다음 씬에서도 재사용).
        /// </summary>
        public void Reset()
        {
            _resetBuffer.Clear();
            foreach (var kvp in _active)
            {
                if (kvp.Value != null)
                    _resetBuffer.Add(kvp.Value);
            }

            for (int i = 0; i < _resetBuffer.Count; i++)
            {
                ReturnInternal(_resetBuffer[i]);
            }

            _active.Clear();
            _resetBuffer.Clear();
        }

        /// <summary>
        /// 풀 사전 로드. 게임 시작이나 맵 로드 시 호출하면 첫 스폰의 await 비용 제거.
        /// </summary>
        public UniTask Preload(string key, int count)
        {
            return AddressablePool.Preload(key, count);
        }

        /// <summary>
        /// 이펙트 스폰. 부모를 지정하면 그 자식으로 부착(부모 따라다님).
        /// 키가 비어있거나 로드 실패 시 Invalid 핸들 반환.
        /// </summary>
        public async UniTask<EffectHandle> Spawn(string key, Vector3 position, Quaternion rotation, Transform? parent = null)
        {
            if (string.IsNullOrEmpty(key))
                return EffectHandle.Invalid;

            GameObject? go = await AddressablePool.Get(key, position, rotation);
            if (go == null)
                return EffectHandle.Invalid;

            if (parent != null)
            {
                go.transform.SetParent(parent, worldPositionStays: true);
            }
            else if (_root != null)
            {
                go.transform.SetParent(_root, worldPositionStays: true);
            }

            EffectInstance? instance = go.GetComponent<EffectInstance>();
            if (instance == null)
            {
                Debug.LogError($"[EffectManager] EffectInstance component not found on effect prefab '{key}'. Add EffectInstance to the prefab root.");
                AddressablePool.Return(key, go);
                return EffectHandle.Invalid;
            }

            int id = _nextInstanceId++;
            instance.Bind(key, id, this);
            _active[id] = instance;

            return new EffectHandle(id);
        }

        /// <summary>
        /// 부모를 따라다니는 이펙트. 로컬 오프셋/회전을 지정.
        /// </summary>
        public async UniTask<EffectHandle> SpawnFollow(string key, Transform follow, Vector3 localOffset, Quaternion localRotation)
        {
            if (string.IsNullOrEmpty(key) || follow == null)
                return EffectHandle.Invalid;

            Vector3 worldPos = follow.TransformPoint(localOffset);
            Quaternion worldRot = follow.rotation * localRotation;

            EffectHandle handle = await Spawn(key, worldPos, worldRot, follow);
            if (handle.IsValid && _active.TryGetValue(handle.InstanceId, out var instance) && instance != null)
            {
                instance.transform.localPosition = localOffset;
                instance.transform.localRotation = localRotation;
            }
            return handle;
        }

        /// <summary>
        /// 핸들로 이펙트 종료. 풀로 반환. 이미 반환됐거나 무효 핸들이면 false.
        /// </summary>
        public bool Stop(EffectHandle handle)
        {
            if (handle.IsValid == false)
                return false;

            if (_active.TryGetValue(handle.InstanceId, out var instance) == false || instance == null)
                return false;

            // 세대 확인 — 풀에서 재사용되어 InstanceId가 바뀌었으면 옛 핸들 무시
            if (instance.InstanceId != handle.InstanceId)
                return false;

            ReturnInternal(instance);
            return true;
        }

        /// <summary>
        /// EffectInstance가 자기 자신을 반환할 때 호출. 외부에서 직접 호출 금지 — Stop 사용.
        /// </summary>
        public void ReturnInternal(EffectInstance instance)
        {
            if (instance == null)
                return;

            int id = instance.InstanceId;
            string key = instance.AddressableKey;

            instance.ReturnToPool();
            if (id >= 0)
            {
                _active.Remove(id);
            }

            // 부모가 follow Transform이었을 수 있으니 풀 루트로 되돌림
            if (_root != null)
            {
                instance.transform.SetParent(_root, worldPositionStays: false);
            }

            if (string.IsNullOrEmpty(key) == false)
            {
                AddressablePool.Return(key, instance.gameObject);
            }
            else
            {
                instance.gameObject.SetActive(false);
            }
        }
    }
}
