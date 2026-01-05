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

            // Animator가 있으면 AnimatorComponent도 자동 추가
            if (_componentManager != null && !_componentManager.HasComponent<AnimatorComponent>(entityId))
            {
                _componentManager.AddComponent(entityId, new AnimatorComponent());
            }

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

        // Animator 가져오기
        public readonly bool TryGetAnimator(int entityId, out Animator animator)
        {
            animator = null;

            if (_entityToAnimator == null)
                return false;

            return _entityToAnimator.TryGetValue(entityId, out animator);
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

                // 1. AnimatorComponent의 애니메이션 재생 요청 처리
                if (_componentManager.TryGetComponent<AnimatorComponent>(entityId, out var animatorComp) == true)
                {
                    if (animatorComp.HasRequest)
                    {
                        animator.SetTrigger(animatorComp.RequestedAnimationHash);
                        animatorComp.ClearRequest();
                        _componentManager.SetComponent(entityId, animatorComp);
                    }
                }

                // 2. StateComponent 기반 애니메이션 처리
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
