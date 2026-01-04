using ARPG.Component;
using ARPG.Utility;
using UnityEngine;

namespace ARPG.Base
{
    public class EntityBase : MonoBehaviour
    {
        protected int _entityId = -1; // ECS Entity ID

        public int EntityId { get { return _entityId; } }

        public virtual void Initialize()
        {

        }

        public virtual void Reset()
        {

        }

        protected virtual void InitializeECSComponents()
        {
            // EntityId 생성
            _entityId = EntityIdHelper.CreateEntity();
            Debug.Log($"[EntityBase] Entity initialized with EntityId: {_entityId}");

            // TransformComponent 추가
            TransformComponent transformComponent = new()
            {
                Position = new Vector2(transform.position.x, transform.position.y),
                Rotation = 0f,
                Scale = Vector2.one
            };
            AR.s.Component.AddComponent(_entityId, transformComponent);
        }

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
                StartDuration = 0,
                ProcessDuration = 0,
                EndDuration = 0
            });

            // SkillTargetComponent 추가
            AR.s.Component.AddComponent(skillEntityId, new SkillTargetComponent());


        }
    }
}
