using System.Collections.Generic;
using ARPG.Base;
using UnityEngine;

namespace ARPG.Message
{
    /// <summary>
    /// EntityId와 EntityBase 간의 매핑을 관리하는 레지스트리
    /// 메시지 시스템에서 EntityId로 EntityBase를 찾을 때 사용
    /// </summary>
    public static class EntityRegistry
    {
        private static Dictionary<int, EntityBase> _entityMap = new Dictionary<int, EntityBase>();

        /// <summary>
        /// 엔티티 등록
        /// EntityBase.InitializeECSComponents()에서 호출
        /// </summary>
        /// <param name="entityId">엔티티 ID</param>
        /// <param name="entity">EntityBase 인스턴스</param>
        public static void Register(int entityId, EntityBase entity)
        {
            if (entityId < 0)
            {
                Debug.LogWarning($"[EntityRegistry] Invalid entityId: {entityId}");
                return;
            }

            if (entity == null)
            {
                Debug.LogWarning($"[EntityRegistry] Cannot register null entity for id: {entityId}");
                return;
            }

            if (_entityMap.ContainsKey(entityId) == true)
            {
                Debug.LogWarning($"[EntityRegistry] Entity {entityId} is already registered. Overwriting.");
            }

            _entityMap[entityId] = entity;
            Debug.Log($"[EntityRegistry] Registered entity {entityId}");
        }

        /// <summary>
        /// 엔티티 등록 해제
        /// EntityBase.OnDestroy()에서 호출
        /// </summary>
        /// <param name="entityId">엔티티 ID</param>
        public static void Unregister(int entityId)
        {
            if (_entityMap.Remove(entityId) == true)
            {
                Debug.Log($"[EntityRegistry] Unregistered entity {entityId}");
            }
        }

        /// <summary>
        /// EntityId로 EntityBase 조회
        /// </summary>
        /// <param name="entityId">엔티티 ID</param>
        /// <param name="entity">찾은 EntityBase (없으면 null)</param>
        /// <returns>찾았으면 true</returns>
        public static bool TryGet(int entityId, out EntityBase entity)
        {
            return _entityMap.TryGetValue(entityId, out entity);
        }

        /// <summary>
        /// EntityId로 특정 타입의 EntityBase 조회
        /// </summary>
        /// <typeparam name="T">EntityBase를 상속받는 타입</typeparam>
        /// <param name="entityId">엔티티 ID</param>
        /// <param name="entity">찾은 엔티티 (없거나 타입이 다르면 null)</param>
        /// <returns>찾았으면 true</returns>
        public static bool TryGet<T>(int entityId, out T entity) where T : EntityBase
        {
            entity = null;

            if (_entityMap.TryGetValue(entityId, out var baseEntity) == true)
            {
                entity = baseEntity as T;
                return entity != null;
            }

            return false;
        }

        /// <summary>
        /// 엔티티가 등록되어 있는지 확인
        /// </summary>
        /// <param name="entityId">엔티티 ID</param>
        /// <returns>등록되어 있으면 true</returns>
        public static bool IsRegistered(int entityId)
        {
            return _entityMap.ContainsKey(entityId);
        }

        /// <summary>
        /// 등록된 엔티티 수 반환
        /// </summary>
        public static int Count => _entityMap.Count;

        /// <summary>
        /// 초기화 (모든 엔티티 등록 해제)
        /// </summary>
        public static void Initialize()
        {
            _entityMap.Clear();
            Debug.Log("[EntityRegistry] Initialized");
        }

        /// <summary>
        /// 모든 엔티티 등록 해제
        /// </summary>
        public static void Reset()
        {
            _entityMap.Clear();
            Debug.Log("[EntityRegistry] Reset");
        }
    }
}
