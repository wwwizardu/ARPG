#nullable enable
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.AddressableAssets;

namespace ARPG.Utility
{
    /// <summary>
    /// Addressable 키(string) 기반 공용 오브젝트 풀
    /// 발사체, 이펙트 등 자주 생성/파괴되는 GameObject를 재사용
    /// </summary>
    public static class AddressablePool
    {
        private static readonly Dictionary<string, Queue<GameObject>> _pools = new();
        private static readonly Dictionary<string, GameObject> _prefabCache = new();
        private static Transform? _poolRoot;

        /// <summary>
        /// 풀 루트 오브젝트 설정 (비활성 오브젝트 보관용)
        /// </summary>
        public static void SetPoolRoot(Transform root)
        {
            _poolRoot = root;
        }

        /// <summary>
        /// 프리팹 사전 로드 + count개 인스턴스 생성 (비활성 상태)
        /// </summary>
        public static async UniTask Preload(string key, int count)
        {
            if (string.IsNullOrEmpty(key))
                return;

            // 프리팹 캐시 로드
            if (_prefabCache.ContainsKey(key) == false)
            {
                try
                {
                    GameObject prefab = await Addressables.LoadAssetAsync<GameObject>(key).ToUniTask();
                    _prefabCache[key] = prefab;
                }
                catch (System.Exception e)
                {
                    Debug.LogError($"[AddressablePool] Failed to load prefab '{key}': {e.Message}");
                    return;
                }
            }

            if (_pools.ContainsKey(key) == false)
            {
                _pools[key] = new Queue<GameObject>();
            }

            GameObject cachedPrefab = _prefabCache[key];
            for (int i = 0; i < count; i++)
            {
                GameObject go = Object.Instantiate(cachedPrefab);
                go.SetActive(false);
                if (_poolRoot != null)
                {
                    go.transform.SetParent(_poolRoot);
                }
                _pools[key].Enqueue(go);
            }

            Debug.Log($"[AddressablePool] Preloaded '{key}' x{count} (Pool size: {_pools[key].Count})");
        }

        /// <summary>
        /// 풀에서 GameObject 꺼내기. 풀이 비어있으면 새로 생성.
        /// 프리팹이 캐시에 없으면 Addressable로 로드 후 Instantiate.
        /// </summary>
        public static async UniTask<GameObject?> Get(string key, Vector3 position, Quaternion rotation)
        {
            if (string.IsNullOrEmpty(key))
                return null;

            // 풀에서 꺼내기
            if (_pools.TryGetValue(key, out var queue) && queue.Count > 0)
            {
                GameObject pooled = queue.Dequeue();
                pooled.transform.position = position;
                pooled.transform.rotation = rotation;
                pooled.SetActive(true);
                return pooled;
            }

            // 프리팹 캐시 확인
            if (_prefabCache.TryGetValue(key, out var prefab))
            {
                GameObject go = Object.Instantiate(prefab, position, rotation);
                return go;
            }

            // 프리팹 캐시에 없으면 Addressable 로드
            try
            {
                GameObject loadedPrefab = await Addressables.LoadAssetAsync<GameObject>(key).ToUniTask();
                _prefabCache[key] = loadedPrefab;

                GameObject newGo = Object.Instantiate(loadedPrefab, position, rotation);
                return newGo;
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[AddressablePool] Failed to load and instantiate '{key}': {e.Message}");
                return null;
            }
        }

        /// <summary>
        /// GameObject를 풀에 반환 (비활성화)
        /// </summary>
        public static void Return(string key, GameObject go)
        {
            if (string.IsNullOrEmpty(key) || go == null)
                return;

            go.SetActive(false);
            if (_poolRoot != null)
            {
                go.transform.SetParent(_poolRoot);
            }

            if (_pools.ContainsKey(key) == false)
            {
                _pools[key] = new Queue<GameObject>();
            }

            _pools[key].Enqueue(go);
        }

        /// <summary>
        /// 특정 키의 풀 정리
        /// </summary>
        public static void Clear(string key)
        {
            if (_pools.TryGetValue(key, out var queue))
            {
                while (queue.Count > 0)
                {
                    GameObject go = queue.Dequeue();
                    if (go != null)
                    {
                        Object.Destroy(go);
                    }
                }
                _pools.Remove(key);
            }

            if (_prefabCache.TryGetValue(key, out var prefab))
            {
                Addressables.Release(prefab);
                _prefabCache.Remove(key);
            }
        }

        /// <summary>
        /// 모든 풀 정리 (씬 전환 시 호출)
        /// </summary>
        public static void ClearAll()
        {
            foreach (var kvp in _pools)
            {
                Queue<GameObject> queue = kvp.Value;
                while (queue.Count > 0)
                {
                    GameObject go = queue.Dequeue();
                    if (go != null)
                    {
                        Object.Destroy(go);
                    }
                }
            }
            _pools.Clear();

            foreach (var kvp in _prefabCache)
            {
                if (kvp.Value != null)
                {
                    Addressables.Release(kvp.Value);
                }
            }
            _prefabCache.Clear();

            Debug.Log("[AddressablePool] All pools cleared");
        }
    }
}
