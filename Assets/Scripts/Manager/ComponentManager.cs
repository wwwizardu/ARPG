using System;
using System.Collections.Generic;
using UnityEngine;

namespace ARPG.Component
{
    public class ComponentManager : MonoBehaviour
    {
        private Dictionary<Type, object> _componentPools = new Dictionary<Type, object>();

        public void Initialize()
        {
            Debug.Log("ComponentManager Initialized");
        }

        public void Reset()
        {
            // 모든 컴포넌트 풀 정리
            _componentPools.Clear();
            Debug.Log("ComponentManager Reset - All component pools cleared");
        }

        // 컴포넌트 추가/업데이트 (존재하면 업데이트, 없으면 추가)
        public void AddComponent<T>(Entity entity, T component) where T : struct
        {
            SparseSet<T> pool = GetOrCreatePool<T>();
            pool.Add(entity.Id, component);
        }

        // 컴포넌트 설정 (존재하면 업데이트, 없으면 추가)
        public void SetComponent<T>(Entity entity, T component) where T : struct
        {
            SparseSet<T> pool = GetOrCreatePool<T>();
            pool.Set(entity.Id, component);
        }

        // 컴포넌트 조회
        public T GetComponent<T>(Entity entity) where T : struct
        {
            SparseSet<T> pool = GetPool<T>();
            return pool != null ? pool.Get(entity.Id) : default;
        }

        // 컴포넌트 조회 시도 (Unity 패턴, 가장 효율적)
        public bool TryGetComponent<T>(Entity entity, out T component) where T : struct
        {
            SparseSet<T> pool = GetPool<T>();

            if (pool != null && pool.Contains(entity.Id))
            {
                component = pool.Get(entity.Id);
                return true;
            }

            component = default;
            return false;
        }

        // 컴포넌트 제거
        public void RemoveComponent<T>(Entity entity) where T : struct
        {
            SparseSet<T> pool = GetPool<T>();
            pool?.Remove(entity.Id);
        }

        // 컴포넌트 존재 확인
        public bool HasComponent<T>(Entity entity) where T : struct
        {
            SparseSet<T> pool = GetPool<T>();
            return pool != null && pool.Contains(entity.Id);
        }

        // System이 특정 타입의 모든 컴포넌트에 접근할 때 사용
        public SparseSet<T> GetComponentPool<T>() where T : struct
        {
            return GetOrCreatePool<T>();
        }

        private SparseSet<T> GetOrCreatePool<T>() where T : struct
        {
            Type type = typeof(T);
            if (!_componentPools.ContainsKey(type))
            {
                _componentPools[type] = new SparseSet<T>(1000); // 초기 용량 설정
            }

            return _componentPools[type] as SparseSet<T>;
        }

        private SparseSet<T> GetPool<T>() where T : struct
        {
            Type type = typeof(T);
            return _componentPools.ContainsKey(type) ? _componentPools[type] as SparseSet<T> : null;
        }

    }    
}


