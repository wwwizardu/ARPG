using ARPG.Component;
using UnityEngine;
using System.Collections.Generic;

namespace ARPG.Systems
{
    // AnimationSystem: VelocityComponent와 InputComponent를 기반으로 애니메이션 상태 결정 및 Animator 제어
    public struct System_Animation : IUpdateSystem
    {
        public int Priority => 500; // Render 이전, Movement 이후 실행

        private ComponentManager _componentManager;
        private Dictionary<int, Animator> _entityToAnimator; // EntityId -> Animator 매핑

        public void OnCreate()
        {
            _componentManager = AR.s.Component;
            _entityToAnimator = new Dictionary<int, Animator>();

            Debug.Log("System_Animation Created");
        }

        public void OnReset()
        {
            _entityToAnimator?.Clear();
            _componentManager = null;

            Debug.Log("System_Animation Reset called");
        }

        // Animator 등록 (Entity 생성 시 호출)
        public void RegisterAnimator(int entityId, Animator animator)
        {
            if (_entityToAnimator == null)
                _entityToAnimator = new Dictionary<int, Animator>();

            _entityToAnimator[entityId] = animator;
            Debug.Log($"Animator registered for Entity {entityId}");
        }

        // Animator 해제 (Entity 삭제 시 호출)
        public void UnregisterAnimator(int entityId)
        {
            if (_entityToAnimator == null)
                return;

            _entityToAnimator.Remove(entityId);
            Debug.Log($"Animator unregistered for Entity {entityId}");
        }

        // Update: VelocityComponent 기반으로 애니메이션 상태 결정
        public readonly void OnUpdate(float inDeltaTime)
        {
            if (_componentManager == null || _entityToAnimator == null)
                return;

            // Animator가 등록된 엔티티들만 순회
            foreach (var kvp in _entityToAnimator)
            {
                int entityId = kvp.Key;
                Animator animator = kvp.Value;

                if (animator == null)
                    continue;

                if (_componentManager.TryGetComponent<StateComponent>(entityId, out var state) == true)
                {
                    UpdateAnimatorFromState(animator, ref state, entityId);
                }
            }
        }

        private readonly void UpdateAnimatorFromState(Animator animator, ref StateComponent state, int entityId)
        {
            if(state.Condition != state.ConditionPrev)
            {
                switch(state.Condition)
                {
                    case Creature.CharacterConditions.Normal:   // 정상 상태
                        break;
                    case Creature.CharacterConditions.UseSkill: // 스킬 사용 상태
                        UpdateSkillAnimation(animator, entityId, ref state);
                        break;
                    case Creature.CharacterConditions.Stunned:  // 기절 상태
                        break;
                    case Creature.CharacterConditions.Dead:     // 사망 상태
                        break;
                    // 추가 상태 처리 가능
                }

                state.ConditionPrev = state.Condition;
                AR.s.Component.SetComponent(entityId, state);
            }

            if(state.Condition == Creature.CharacterConditions.Normal) // 정상 상태일 경우에만 이동 애니메이션 적용
            {
                if(state.MoveState != state.MovementStatePrev)
                {
                    switch(state.MoveState)
                    {
                        case Creature.MovementStates.Idle:
                            animator.SetTrigger("Idle");
                            break;
                        case Creature.MovementStates.Walking:
                            animator.SetTrigger("Walk");
                            break;
                        // 추가 상태 처리 가능
                    }

                    state.MovementStatePrev = state.MoveState;
                    AR.s.Component.SetComponent(entityId, state);
                }
            }
        }

        private readonly void UpdateSkillAnimation(Animator inAnimato, int inEneityId, ref StateComponent inState)
        {
            
        }
    }
}
