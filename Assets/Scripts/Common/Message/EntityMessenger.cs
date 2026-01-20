using System;
using System.Collections.Generic;
using UnityEngine;

namespace ARPG.Message
{
    /// <summary>
    /// 엔티티 메시지 전송 및 큐잉 시스템
    /// 메시지를 타입별 큐에 저장하고 일괄 처리
    /// 새 메시지 타입 추가 시 코드 수정 불필요
    /// </summary>
    public static class EntityMessenger
    {
        /// <summary>
        /// 타입별 Queue 저장 (Queue<T>를 object로 저장)
        /// </summary>
        private static Dictionary<Type, object> _queues = new Dictionary<Type, object>();

        /// <summary>
        /// 타입별 처리 함수 저장
        /// </summary>
        private static Dictionary<Type, Action> _processors = new Dictionary<Type, Action>();

        #region Send Method

        /// <summary>
        /// 메시지 전송 (큐에 추가)
        /// </summary>
        /// <typeparam name="T">메시지 타입</typeparam>
        /// <param name="message">메시지 데이터</param>
        public static void Send<T>(T message) where T : struct, IEntityMessage
        {
            var queue = GetOrCreateQueue<T>();
            queue.Enqueue(message);
        }

        /// <summary>
        /// 타입별 Queue를 가져오거나 생성
        /// </summary>
        private static Queue<T> GetOrCreateQueue<T>() where T : struct, IEntityMessage
        {
            var type = typeof(T);

            if (_queues.TryGetValue(type, out var obj) == false)
            {
                // 새 Queue 생성
                var queue = new Queue<T>();
                _queues[type] = queue;

                // 프로세서도 함께 등록
                _processors[type] = () => ProcessQueue(queue);

                Debug.Log($"[EntityMessenger] Created queue for {type.Name}");
                return queue;
            }

            return (Queue<T>)obj;
        }

        #endregion

        #region Process Methods

        /// <summary>
        /// 모든 큐의 메시지 일괄 처리
        /// System_EntityMessage의 OnLateUpdate에서 호출
        /// </summary>
        public static void ProcessAll()
        {
            foreach (var processor in _processors.Values)
            {
                processor();
            }
        }

        /// <summary>
        /// 특정 타입의 Queue 처리
        /// </summary>
        private static void ProcessQueue<T>(Queue<T> queue) where T : struct, IEntityMessage
        {
            while (queue.Count > 0)
            {
                var msg = queue.Dequeue();
                if (EntityRegistry.TryGet(msg.TargetEntityId, out var entity) == true)
                {
                    entity.HandleMessage(msg);
                }
            }
        }

        #endregion

        #region Utility

        /// <summary>
        /// 초기화 (모든 큐와 프로세서 제거)
        /// </summary>
        public static void Initialize()
        {
            _queues.Clear();
            _processors.Clear();

            Debug.Log("[EntityMessenger] Initialized");
        }

        /// <summary>
        /// 모든 큐의 메시지만 제거 (큐 구조는 유지)
        /// </summary>
        public static void Reset()
        {
            foreach (var kvp in _queues)
            {
                var clearMethod = kvp.Value.GetType().GetMethod("Clear");
                clearMethod?.Invoke(kvp.Value, null);
            }

            Debug.Log("[EntityMessenger] Reset");
        }

        /// <summary>
        /// 대기 중인 메시지 수 반환
        /// </summary>
        public static int GetPendingMessageCount()
        {
            int count = 0;

            foreach (var kvp in _queues)
            {
                var countProperty = kvp.Value.GetType().GetProperty("Count");
                if (countProperty != null)
                {
                    count += (int)countProperty.GetValue(kvp.Value);
                }
            }

            return count;
        }

        /// <summary>
        /// 특정 타입의 대기 중인 메시지 수 반환
        /// </summary>
        public static int GetPendingMessageCount<T>() where T : struct, IEntityMessage
        {
            var type = typeof(T);

            if (_queues.TryGetValue(type, out var obj) == true)
            {
                return ((Queue<T>)obj).Count;
            }

            return 0;
        }

        /// <summary>
        /// 등록된 메시지 타입 수 반환
        /// </summary>
        public static int GetRegisteredTypeCount()
        {
            return _queues.Count;
        }

        #endregion
    }
}
