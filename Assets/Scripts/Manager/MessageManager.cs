#nullable enable
using System;
using System.Collections.Generic;
using ARPG.Base;
using ARPG.Message;
using UnityEngine;

namespace ARPG.Manager
{
    /// <summary>
    /// 통합 메시지 매니저
    /// Entity-targeted 메시지 (특정 엔티티 대상) + Broadcast 메시지 (전역 구독자) 지원
    /// EntityMessenger + EntityRegistry를 통합
    /// </summary>
    public class MessageManager : MonoBehaviour
    {
        #region Entity Registry

        private Dictionary<int, EntityBase> _entityMap = new();

        public void RegisterEntity(int entityId, EntityBase entity)
        {
            if (entityId < 0)
            {
                Debug.LogWarning($"[MessageManager] Invalid entityId: {entityId}");
                return;
            }

            if (entity == null)
            {
                Debug.LogWarning($"[MessageManager] Cannot register null entity for id: {entityId}");
                return;
            }

            if (_entityMap.ContainsKey(entityId) == true)
            {
                Debug.LogWarning($"[MessageManager] Entity {entityId} is already registered. Overwriting.");
            }

            _entityMap[entityId] = entity;
        }

        public void UnregisterEntity(int entityId)
        {
            _entityMap.Remove(entityId);
        }

        public bool TryGetEntity(int entityId, out EntityBase entity)
        {
            return _entityMap.TryGetValue(entityId, out entity);
        }

        public bool TryGetEntity<T>(int entityId, out T? entity) where T : EntityBase
        {
            entity = null;

            if (_entityMap.TryGetValue(entityId, out var baseEntity) == true)
            {
                entity = baseEntity as T;
                return entity != null;
            }

            return false;
        }

        public bool IsEntityRegistered(int entityId)
        {
            return _entityMap.ContainsKey(entityId);
        }

        public int EntityCount => _entityMap.Count;

        #endregion

        #region Entity-targeted Messages

        private Dictionary<Type, object> _entityQueues = new();
        private Dictionary<Type, Action> _entityProcessors = new();

        /// <summary>
        /// 특정 엔티티에 메시지 전송 (TargetEntityId 기반)
        /// </summary>
        public void SendToEntity<T>(T message) where T : struct, IEntityMessage
        {
            var queue = GetOrCreateEntityQueue<T>();
            queue.Enqueue(message);
        }

        private Queue<T> GetOrCreateEntityQueue<T>() where T : struct, IEntityMessage
        {
            var type = typeof(T);

            if (_entityQueues.TryGetValue(type, out var obj) == false)
            {
                var queue = new Queue<T>();
                _entityQueues[type] = queue;
                _entityProcessors[type] = () => ProcessEntityQueue(queue);
                return queue;
            }

            return (Queue<T>)obj;
        }

        private void ProcessEntityQueue<T>(Queue<T> queue) where T : struct, IEntityMessage
        {
            while (queue.Count > 0)
            {
                var msg = queue.Dequeue();
                if (_entityMap.TryGetValue(msg.TargetEntityId, out var entity) == true)
                {
                    entity.HandleMessage(msg);
                }
            }
        }

        #endregion

        #region Broadcast Messages

        private Dictionary<Type, List<Delegate>> _broadcastHandlers = new();
        private Dictionary<Type, object> _broadcastQueues = new();
        private Dictionary<Type, Action> _broadcastProcessors = new();

        /// <summary>
        /// 메시지 타입에 대한 전역 구독
        /// UI, Manager 등 아무 객체나 구독 가능
        /// </summary>
        public void Subscribe<T>(Action<T> handler) where T : struct
        {
            var type = typeof(T);

            if (_broadcastHandlers.ContainsKey(type) == false)
            {
                _broadcastHandlers[type] = new List<Delegate>();
            }

            _broadcastHandlers[type].Add(handler);
        }

        /// <summary>
        /// 구독 해제
        /// </summary>
        public void Unsubscribe<T>(Action<T> handler) where T : struct
        {
            var type = typeof(T);

            if (_broadcastHandlers.TryGetValue(type, out var handlers) == true)
            {
                handlers.Remove(handler);
            }
        }

        /// <summary>
        /// 모든 구독자에게 메시지 전달
        /// </summary>
        public void Broadcast<T>(T message) where T : struct
        {
            var queue = GetOrCreateBroadcastQueue<T>();
            queue.Enqueue(message);
        }

        private Queue<T> GetOrCreateBroadcastQueue<T>() where T : struct
        {
            var type = typeof(T);

            if (_broadcastQueues.TryGetValue(type, out var obj) == false)
            {
                var queue = new Queue<T>();
                _broadcastQueues[type] = queue;
                _broadcastProcessors[type] = () => ProcessBroadcastQueue<T>(queue);
                return queue;
            }

            return (Queue<T>)obj;
        }

        private void ProcessBroadcastQueue<T>(Queue<T> queue) where T : struct
        {
            if (_broadcastHandlers.TryGetValue(typeof(T), out var handlers) == false)
            {
                queue.Clear();
                return;
            }

            while (queue.Count > 0)
            {
                var msg = queue.Dequeue();
                for (int i = 0; i < handlers.Count; i++)
                {
                    ((Action<T>)handlers[i])(msg);
                }
            }
        }

        #endregion

        #region Process

        /// <summary>
        /// 모든 큐의 메시지 일괄 처리
        /// System_EntityMessage의 OnLateUpdate에서 호출
        /// </summary>
        public void ProcessAll()
        {
            // Entity-targeted 메시지 처리
            foreach (var processor in _entityProcessors.Values)
            {
                processor();
            }

            // Broadcast 메시지 처리
            foreach (var processor in _broadcastProcessors.Values)
            {
                processor();
            }
        }

        #endregion

        #region Lifecycle

        public void Initialize()
        {
            _entityMap.Clear();
            _entityQueues.Clear();
            _entityProcessors.Clear();
            _broadcastHandlers.Clear();
            _broadcastQueues.Clear();
            _broadcastProcessors.Clear();

            Debug.Log("[MessageManager] Initialized");
        }

        public void Reset()
        {
            // 큐 내용만 제거 (구조 유지)
            foreach (var kvp in _entityQueues)
            {
                var clearMethod = kvp.Value.GetType().GetMethod("Clear");
                clearMethod?.Invoke(kvp.Value, null);
            }

            foreach (var kvp in _broadcastQueues)
            {
                var clearMethod = kvp.Value.GetType().GetMethod("Clear");
                clearMethod?.Invoke(kvp.Value, null);
            }

            _entityMap.Clear();

            Debug.Log("[MessageManager] Reset");
        }

        #endregion
    }
}
