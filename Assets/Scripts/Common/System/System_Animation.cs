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

                // VelocityComponent로 애니메이션 상태 결정
                if (_componentManager.TryGetComponent<VelocityComponent>(entityId, out var velocity))
                {
                    UpdateAnimatorFromVelocity(animator, velocity, entityId);
                }
            }
        }

        private readonly void UpdateAnimatorFromVelocity(Animator animator, VelocityComponent velocity, int entityId)
        {
            float speed = velocity.Velocity.magnitude;

            // 속도에 따른 애니메이션 파라미터 설정
            animator.SetFloat("Speed", speed);

            // 이동 방향 설정 (2D)
            if (speed > 0.01f)
            {
                animator.SetFloat("Horizontal", velocity.Velocity.x);
                animator.SetFloat("Vertical", velocity.Velocity.y);
            }

            // 상태 결정
            bool isMoving = speed > 0.01f;
            animator.SetBool("IsMoving", isMoving);

            // 달리기 체크
            if (_componentManager.TryGetComponent<InputComponent>(entityId, out var input))
            {
                bool isRunning = input.IsSprinting && speed > 3f;
                animator.SetBool("IsRunning", isRunning);

                // 공격 애니메이션
                if (input.IsAttacking)
                {
                    animator.SetTrigger("Attack");
                }
            }
        }
    }
}
