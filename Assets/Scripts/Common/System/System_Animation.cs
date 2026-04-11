using ARPG.Component;
using UnityEngine;
using System.Collections.Generic;

namespace ARPG.Systems
{
    // AnimationSystem: SpriteAnimationComponent 기반으로 애니메이션 상태 결정 및 PlayableAnimator 제어
    public class System_Animation : IUpdateSystem
    {
        public int Priority => 500; // Render 이전, Movement 이후 실행

        private ComponentManager _componentManager;
        private Dictionary<int, PlayableAnimator> _entityToPlayableAnimator; // EntityId -> PlayableAnimator 매핑

        private static readonly int IDLE_HASH = Animator.StringToHash("Idle");
        private static readonly int WALK_HASH = Animator.StringToHash("Move");

        public void OnCreate()
        {
            _componentManager = AR.s.Component;
            _entityToPlayableAnimator = new Dictionary<int, PlayableAnimator>();

            Debug.Log("System_Animation Created");
        }

        public void OnReset()
        {
            _entityToPlayableAnimator?.Clear();
            _componentManager = null;

            Debug.Log("System_Animation Reset called");
        }

        // PlayableAnimator 등록 (Entity 생성 시 호출)
        public void RegisterPlayableAnimator(int entityId, PlayableAnimator playableAnimator)
        {
            if (_entityToPlayableAnimator == null)
            {
                _entityToPlayableAnimator = new Dictionary<int, PlayableAnimator>();
            }

            _entityToPlayableAnimator[entityId] = playableAnimator;

            // PlayableAnimator가 있으면 AnimatorComponent도 자동 추가
            if (_componentManager != null && _componentManager.HasComponent<AnimatorComponent>(entityId) == false)
            {
                _componentManager.AddComponent(entityId, new AnimatorComponent());
            }

            Debug.Log($"PlayableAnimator registered for Entity {entityId}");
        }

        // PlayableAnimator 해제 (Entity 삭제 시 호출)
        public void UnregisterPlayableAnimator(int entityId)
        {
            if (_entityToPlayableAnimator == null)
            {
                return;
            }

            _entityToPlayableAnimator.Remove(entityId);
            Debug.Log($"PlayableAnimator unregistered for Entity {entityId}");
        }

        // PlayableAnimator 가져오기
        public bool TryGetPlayableAnimator(int entityId, out PlayableAnimator playableAnimator)
        {
            playableAnimator = null;

            if (_entityToPlayableAnimator == null)
            {
                return false;
            }

            return _entityToPlayableAnimator.TryGetValue(entityId, out playableAnimator);
        }

        // Update: SpriteAnimationComponent 기반으로 애니메이션 상태 결정
        public void OnUpdate(float inDeltaTime)
        {
            if (_componentManager == null || _entityToPlayableAnimator == null)
            {
                return;
            }

            // SpriteAnimationComponent 풀 기반 순회 (for 루프)
            SparseSet<SpriteAnimationComponent> pool = _componentManager.GetComponentPool<SpriteAnimationComponent>();

            for (int i = 0; i < pool.Count; i++)
            {
                int entityId = pool.GetEntityId(i);
                SpriteAnimationComponent spriteAnim = pool.GetByIndex(i);

                // 로드 완료된 엔티티만 처리
                if (spriteAnim.LoadState != AnimationLoadState.Loaded)
                {
                    continue;
                }

                if (_entityToPlayableAnimator.TryGetValue(entityId, out var playableAnimator) == false)
                {
                    continue;
                }

                if (playableAnimator == null || playableAnimator.IsInitialized == false)
                {
                    continue;
                }

                // 1. AnimatorComponent의 애니메이션 재생 요청 처리
                if (_componentManager.TryGetComponent<AnimatorComponent>(entityId, out var animatorComp) == true)
                {
                    if (animatorComp.HasRequest)
                    {
                        playableAnimator.Play(animatorComp.RequestedAnimationHash, animatorComp.Force, animatorComp.BlendTime);
                        animatorComp.ClearRequest();
                        _componentManager.SetComponent(entityId, animatorComp);
                    }
                }

                // 2. PlaybackSpeed 반영
                playableAnimator.SetSpeed(spriteAnim.PlaybackSpeed);

                // 3. StateComponent 기반 애니메이션 처리
                if (_componentManager.TryGetComponent<StateComponent>(entityId, out var state) == true)
                {
                    UpdateAnimatorFromState(playableAnimator, ref state, entityId);
                }
            }
        }

        private void UpdateAnimatorFromState(PlayableAnimator playableAnimator, ref StateComponent state, int entityId)
        {
            if (state.Condition != state.ConditionPrev)
            {
                switch (state.Condition)
                {
                    case Creature.CharacterConditions.Normal:   // 정상 상태
                        playableAnimator.Play(IDLE_HASH);
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

            if (state.Condition == Creature.CharacterConditions.Normal) // 정상 상태일 경우에만 이동 애니메이션 적용
            {
                if (state.MoveState != state.MovementStatePrev)
                {
                    switch (state.MoveState)
                    {
                        case Creature.MovementStates.Idle:
                            playableAnimator.Play(IDLE_HASH);
                            break;
                        case Creature.MovementStates.Walking:
                            playableAnimator.Play(WALK_HASH);
                            break;
                        // 추가 상태 처리 가능
                    }

                    state.MovementStatePrev = state.MoveState;
                    AR.s.Component.SetComponent(entityId, state);
                }
            }
        }
    }
}
