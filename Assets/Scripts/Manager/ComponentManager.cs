using System;
using System.Collections.Generic;
using UnityEngine;

namespace ARPG.Component
{
    public class ComponentManager : MonoBehaviour
    {
        private Dictionary<Type, object> _componentPools = new Dictionary<Type, object>();

        /// <summary>
        /// 컴포넌트 타입별 SparseSet 초기 용량 설정
        /// </summary>
        private readonly Dictionary<Type, int> _poolSizes = new Dictionary<Type, int>
        {
            // 많이 사용되는 컴포넌트 (캐릭터/엔티티마다 1개씩)
            { typeof(TransformComponent), 1000 },
            { typeof(VelocityComponent), 500 },
            { typeof(MovementComponent), 500 },
            { typeof(StateComponent), 300 },
            { typeof(StatComponent), 300 },

            // 보통 사용되는 컴포넌트
            { typeof(InputComponent), 100 },

            // AI 컴포넌트 (NPC/몬스터용)
            { typeof(AIComponent), 500 },
            { typeof(AIPerceptionComponent), 500 },

            // 적게 사용되는 컴포넌트 (스킬 엔티티용)
            { typeof(SkillComponent), 50 },
            { typeof(SkillStateComponent), 50 },
            { typeof(SkillTimingComponent), 50 },
            { typeof(SkillTargetComponent), 50 },

            // 매우 적게 사용되는 컴포넌트 (커맨드용, 일시적)
            { typeof(SkillCommandComponent), 20 },
        };

        /// <summary>
        /// 기본 풀 크기 (설정되지 않은 컴포넌트 타입)
        /// </summary>
        private const int DEFAULT_POOL_SIZE = 300;

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
        public void AddComponent<T>(int entityId, T component) where T : struct
        {
            SparseSet<T> pool = GetOrCreatePool<T>();
            pool.Add(entityId, component);
        }

        // 컴포넌트 설정 (존재하면 업데이트, 없으면 추가)
        public void SetComponent<T>(int entityId, T component) where T : struct
        {
            SparseSet<T> pool = GetOrCreatePool<T>();
            pool.Set(entityId, component);
        }

        // 컴포넌트 조회
        public T GetComponent<T>(int entityId) where T : struct
        {
            SparseSet<T> pool = GetPool<T>();
            return pool != null ? pool.Get(entityId) : default;
        }

        // 컴포넌트 조회 시도 (Unity 패턴, 가장 효율적)
        public bool TryGetComponent<T>(int entityId, out T component) where T : struct
        {
            SparseSet<T> pool = GetPool<T>();

            if (pool != null && pool.Contains(entityId))
            {
                component = pool.Get(entityId);
                return true;
            }

            component = default;
            return false;
        }

        // 컴포넌트 제거
        public void RemoveComponent<T>(int entityId) where T : struct
        {
            SparseSet<T> pool = GetPool<T>();
            pool?.Remove(entityId);
        }

        // 컴포넌트 존재 확인
        public bool HasComponent<T>(int entityId) where T : struct
        {
            SparseSet<T> pool = GetPool<T>();
            return pool != null && pool.Contains(entityId);
        }

        // System이 특정 타입의 모든 컴포넌트에 접근할 때 사용
        public SparseSet<T> GetComponentPool<T>() where T : struct
        {
            return GetOrCreatePool<T>();
        }

        private SparseSet<T> GetOrCreatePool<T>() where T : struct
        {
            Type type = typeof(T);
            if (_componentPools.ContainsKey(type) == false)
            {
                // 타입별로 설정된 용량 사용, 없으면 기본값
                int capacity = _poolSizes.TryGetValue(type, out int size) ? size : DEFAULT_POOL_SIZE;
                _componentPools[type] = new SparseSet<T>(capacity);

                Debug.Log($"[ComponentManager] Created component pool for {type.Name} with capacity {capacity}");
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


