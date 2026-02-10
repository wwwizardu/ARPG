using System;
using System.Collections.Generic;
using UnityEngine;

namespace ARPG.Component
{
    public class ComponentManager : MonoBehaviour
    {
        private readonly Dictionary<Type, IComponentPool> _componentPools = new();

        /// <summary>
        /// Entity별로 등록된 컴포넌트 타입들을 추적
        /// Key: EntityId, Value: 해당 Entity에 등록된 컴포넌트 타입들
        /// </summary>
        private Dictionary<int, HashSet<Type>> _entityComponents = new Dictionary<int, HashSet<Type>>();

        /// <summary>
        /// 컴포넌트 타입별 SparseSet 초기 용량 설정
        /// </summary>
        private readonly Dictionary<Type, int> _poolSizes = new Dictionary<Type, int>
        {
            // 많이 사용되는 컴포넌트 (캐릭터/엔티티마다 1개씩)
            { typeof(TransformComponent), 1000 },
            { typeof(VelocityComponent), 500 },
            { typeof(StateComponent), 300 },
            { typeof(StatComponent), 300 },

            // 보통 사용되는 컴포넌트
            { typeof(InputComponent), 100 },

            // AI 컴포넌트 (NPC/몬스터용)
            { typeof(AIComponent), 500 },
            { typeof(AIPerceptionComponent), 500 },
            { typeof(AIBehaviorTypeComponent), 500 },
            { typeof(AIStateComponent), 500 },
            { typeof(AICanSeeTargetTag), 500 },

            // 적게 사용되는 컴포넌트 (스킬 엔티티용)
            { typeof(SkillComponent), 50 },
            { typeof(SkillStateComponent), 50 },
            { typeof(SkillTimingComponent), 50 },
            { typeof(SkillTargetComponent), 50 },

            // 매우 적게 사용되는 컴포넌트 (커맨드용, 일시적)
            { typeof(SkillCommandComponent), 20 },

            // 월드 아이템 컴포넌트
            { typeof(WorldItemComponent), 500 },              // 월드에 떨어진 아이템

            // 애니메이션 컴포넌트
            { typeof(SpriteAnimationComponent), 300 },        // 스프라이트 애니메이션 설정 및 로딩 상태
            { typeof(AnimatorComponent), 300 },               // 애니메이션 재생 요청

            // 맵 컴포넌트
            { typeof(MapChunkLoaderComponent), 4 },           // 청크 로더 (플레이어 전용)

            // 엔티티 라이프사이클 컴포넌트
            { typeof(DestroyTag), 100 },                       // 제거 예약 태그
            { typeof(ActivationDistanceComponent), 500 },      // 거리 기반 활성화
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
            _entityComponents.Clear();
            Debug.Log("ComponentManager Reset - All component pools cleared");
        }

        // 컴포넌트 추가/업데이트 (존재하면 업데이트, 없으면 추가)
        public void AddComponent<T>(int entityId, T component) where T : struct
        {
            SparseSet<T> pool = GetOrCreatePool<T>();
            pool.Add(entityId, component);

            // Entity에 등록된 컴포넌트 타입 추적
            TrackComponentType<T>(entityId);
        }

        // 컴포넌트 설정 (존재하면 업데이트, 없으면 추가)
        public void SetComponent<T>(int entityId, T component) where T : struct
        {
            SparseSet<T> pool = GetOrCreatePool<T>();
            pool.Set(entityId, component);

            // Entity에 등록된 컴포넌트 타입 추적
            TrackComponentType<T>(entityId);
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

            // Entity 추적 정보에서 컴포넌트 타입 제거
            Type type = typeof(T);
            if (_entityComponents.TryGetValue(entityId, out HashSet<Type> componentTypes))
            {
                componentTypes.Remove(type);

                // 컴포넌트가 모두 제거되면 Entity 정보도 제거
                if (componentTypes.Count == 0)
                {
                    _entityComponents.Remove(entityId);
                }
            }
        }

        /// <summary>
        /// Entity에 등록된 모든 컴포넌트를 제거합니다.
        /// IComponentPool 인터페이스를 통해 타입 안전하게 제거 (Reflection 불필요)
        /// </summary>
        /// <param name="entityId">제거할 Entity ID</param>
        public void RemoveAllComponents(int entityId)
        {
            if (!_entityComponents.TryGetValue(entityId, out HashSet<Type> componentTypes))
            {
                // 해당 Entity에 등록된 컴포넌트가 없음
                return;
            }

            // 등록된 모든 컴포넌트 타입을 순회하며 제거
            // ToArray()로 복사본 생성 (순회 중 컬렉션 수정 방지)
            Type[] typesToRemove = new Type[componentTypes.Count];
            componentTypes.CopyTo(typesToRemove);

            int removedCount = 0;
            foreach (Type componentType in typesToRemove)
            {
                if (_componentPools.TryGetValue(componentType, out IComponentPool pool))
                {
                    // IComponentPool 인터페이스를 통해 직접 Remove 호출
                    pool.Remove(entityId);
                    removedCount++;
                }
            }

            // Entity 추적 정보 제거
            _entityComponents.Remove(entityId);

            Debug.Log($"[ComponentManager] Removed all components for Entity {entityId} ({removedCount}/{typesToRemove.Length} components)");
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

        /// <summary>
        /// Entity에 컴포넌트 타입을 추적 정보에 추가
        /// </summary>
        private void TrackComponentType<T>(int entityId) where T : struct
        {
            Type type = typeof(T);

            if (!_entityComponents.TryGetValue(entityId, out HashSet<Type> componentTypes))
            {
                componentTypes = new HashSet<Type>();
                _entityComponents[entityId] = componentTypes;
            }

            componentTypes.Add(type);
        }

    }
}


