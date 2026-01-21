using System;
using System.Collections.Generic;
using ARPG.Component;
using ARPG.Message;
using ARPG.Utility;
using UnityEngine;

namespace ARPG.Base
{
    public class EntityBase : MonoBehaviour
    {
        protected int _entityId = -1; // ECS Entity ID

        public int EntityId { get { return _entityId; } }

        /// <summary>
        /// 메시지 타입별 핸들러 딕셔너리
        /// </summary>
        private Dictionary<Type, Delegate> _messageHandlers;

        public virtual void Initialize()
        {

        }

        public virtual void Reset()
        {

        }

        protected virtual void OnDestroy()
        {
            // EntityRegistry에서 등록 해제
            if (_entityId >= 0)
            {
                EntityRegistry.Unregister(_entityId);
            }

            // 핸들러 정리
            _messageHandlers?.Clear();
        }

        public virtual void InitializeECSComponents()
        {
            // EntityId가 할당되어 있지 않다면 생성
            if(_entityId < 0)
            {
                _entityId = EntityIdHelper.CreateEntity();    
            }
            
            Debug.Log($"[EntityBase] Entity initialized with EntityId: {_entityId}");

            // EntityRegistry에 등록
            EntityRegistry.Register(_entityId, this);

            // TransformComponent 추가
            TransformComponent transformComponent = new()
            {
                Position = new Vector2(transform.position.x, transform.position.y),
                Rotation = 0f,
                Scale = Vector2.one
            };
            AR.s.Component.AddComponent(_entityId, transformComponent);
        }

        #region Message Handlers

        /// <summary>
        /// 메시지 핸들러 등록
        /// 서브클래스에서 Initialize 시 호출하여 필요한 메시지만 등록
        /// </summary>
        /// <typeparam name="T">메시지 타입</typeparam>
        /// <param name="handler">핸들러 함수</param>
        protected void RegisterMessageHandler<T>(Action<T> handler) where T : struct, IEntityMessage
        {
            if (_messageHandlers == null)
            {
                _messageHandlers = new Dictionary<Type, Delegate>();
            }

            _messageHandlers[typeof(T)] = handler;
        }

        /// <summary>
        /// 메시지 핸들러 해제
        /// </summary>
        /// <typeparam name="T">메시지 타입</typeparam>
        protected void UnregisterMessageHandler<T>() where T : struct, IEntityMessage
        {
            _messageHandlers?.Remove(typeof(T));
        }

        /// <summary>
        /// 메시지 수신 처리
        /// EntityMessenger에서 호출
        /// </summary>
        /// <typeparam name="T">메시지 타입</typeparam>
        /// <param name="message">메시지 데이터</param>
        /// <returns>핸들러가 있어서 처리되었으면 true</returns>
        public bool HandleMessage<T>(T message) where T : struct, IEntityMessage
        {
            if (_messageHandlers == null)
            {
                return false;
            }

            if (_messageHandlers.TryGetValue(typeof(T), out var handler) == true)
            {
                ((Action<T>)handler)(message);
                return true;
            }

            return false;
        }

        /// <summary>
        /// 특정 메시지 타입의 핸들러가 등록되어 있는지 확인
        /// </summary>
        public bool HasMessageHandler<T>() where T : struct, IEntityMessage
        {
            return _messageHandlers != null && _messageHandlers.ContainsKey(typeof(T));
        }

        #endregion

        /// <summary>
        /// 스킬 생성 헬퍼 메서드
        /// </summary>
        /// <param name="inSlotIndex">스킬 슬롯 인덱스 (0부터 시작)</param>
        /// <param name="inSkillId">스킬 테이블 ID</param>
        /// <param name="startDuration">시작 단계 지속 시간 (준비 모션)</param>
        /// <param name="processDuration">진행 단계 지속 시간 (공격 판정까지의 시간)</param>
        /// <param name="endDuration">종료 단계 지속 시간 (후딜레이)</param>
        protected void CreateSkill(int inSlotIndex, int inSkillId)
        {
            // 스킬 엔티티 ID 생성 (EntityIdHelper 사용)
            int skillEntityId = EntityIdHelper.CreateSkillEntity(_entityId, inSlotIndex);

            // 디버그 정보 출력
            Debug.Log($"[EntityBase] Creating skill - {SkillEntityIdHelper.GetDebugString(skillEntityId)}");

            var skillTable = AR.s.Data.GetSkill(inSkillId);
            if(skillTable == null)
            {
                Debug.LogError($"[EntityBase] Skill table not found for SkillId: {inSkillId}");
                return;
            }

            // SkillCommandComponent풀을 미리 만들어 놓는다.
            AR.s.Component.GetComponentPool<SkillCommandComponent>();

            // SkillComponent 추가
            AR.s.Component.AddComponent(skillEntityId, new SkillComponent
            {
                SkillId = inSkillId,
                OwnerEntityId = _entityId,
                SlotIndex = inSlotIndex,
                Table = skillTable,
                IsInitialized = true,
                IsEnabled = true,
                ExecutionType = SkillExecutionType.MultiHit,
                HitCount = 1,
                HitInterval = 0,
            });

            // SkillStateComponent 추가
            AR.s.Component.AddComponent(skillEntityId, new SkillStateComponent
            {
                State = SkillState.None,
                ElapsedTime = 0f
            });

            // SkillTimingComponent 추가
            AR.s.Component.AddComponent(skillEntityId, new SkillTimingComponent
            {
                StartDuration = skillTable.DamageTime,
                ProcessDuration = 0.1f,
                EndDuration = skillTable.Duration - skillTable.DamageTime,
            });

            // SkillTargetComponent 추가
            AR.s.Component.AddComponent(skillEntityId, new SkillTargetComponent());


        }
    }
}
