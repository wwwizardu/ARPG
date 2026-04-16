using ARPG.Component;
using UnityEngine;
using System.Collections.Generic;

namespace ARPG.Systems
{
    /// <summary>
    /// SpriteLibraryAsset 기반 프레임 애니메이션 시스템.
    /// SpriteAnimationComponent의 프레임 타이머를 갱신하고 SpriteAnimationData를 통해 스프라이트를 교체.
    /// </summary>
    public class System_Animation : IUpdateSystem
    {
        public int Priority => 500; // Render 이전, Movement 이후 실행

        private ComponentManager _componentManager;
        private Dictionary<int, SpriteAnimationData> _entityAnimData;         // EntityId -> SpriteAnimationData
        private Dictionary<int, float[]> _defaultFrameDurations;              // EntityId -> AnimCategory별 기본 FrameDuration

        public void OnCreate()
        {
            _componentManager = AR.s.Component;
            _entityAnimData = new Dictionary<int, SpriteAnimationData>();
            _defaultFrameDurations = new Dictionary<int, float[]>();

            Debug.Log("System_Animation Created");
        }

        public void OnReset()
        {
            _entityAnimData?.Clear();
            _defaultFrameDurations?.Clear();
            _componentManager = null;

            Debug.Log("System_Animation Reset called");
        }

        /// <summary>
        /// SpriteAnimationData 등록 (Entity 생성 시 호출)
        /// </summary>
        public void RegisterSpriteAnimation(int entityId, SpriteAnimationData animData, float[] defaultFrameDurations)
        {
            if (_entityAnimData == null)
            {
                _entityAnimData = new Dictionary<int, SpriteAnimationData>();
            }

            if (_defaultFrameDurations == null)
            {
                _defaultFrameDurations = new Dictionary<int, float[]>();
            }

            _entityAnimData[entityId] = animData;
            _defaultFrameDurations[entityId] = defaultFrameDurations;

            // AnimatorComponent 자동 추가
            if (_componentManager != null && _componentManager.HasComponent<AnimatorComponent>(entityId) == false)
            {
                _componentManager.AddComponent(entityId, new AnimatorComponent());
            }

            Debug.Log($"SpriteAnimationData registered for Entity {entityId}");
        }

        /// <summary>
        /// SpriteAnimationData 해제 (Entity 삭제 시 호출)
        /// </summary>
        public void UnregisterSpriteAnimation(int entityId)
        {
            if (_entityAnimData != null)
            {
                _entityAnimData.Remove(entityId);
            }

            if (_defaultFrameDurations != null)
            {
                _defaultFrameDurations.Remove(entityId);
            }
        }

        /// <summary>
        /// SpriteAnimationData 가져오기
        /// </summary>
        public bool TryGetSpriteAnimationData(int entityId, out SpriteAnimationData animData)
        {
            animData = null;

            if (_entityAnimData == null)
            {
                return false;
            }

            return _entityAnimData.TryGetValue(entityId, out animData);
        }

        public void OnUpdate(float inDeltaTime)
        {
            if (_componentManager == null || _entityAnimData == null)
            {
                return;
            }

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

                if (_entityAnimData.TryGetValue(entityId, out var animData) == false)
                {
                    continue;
                }

                bool changed = false;

                // 1. AnimatorComponent의 애니메이션 재생 요청 처리
                if (_componentManager.TryGetComponent<AnimatorComponent>(entityId, out var animatorComp) == true)
                {
                    if (animatorComp.HasRequest == true)
                    {
                        changed = ProcessAnimationRequest(entityId, ref spriteAnim, ref animatorComp, animData);
                        animatorComp.ClearRequest();
                        _componentManager.SetComponent(entityId, animatorComp);
                    }
                }

                // 2. StateComponent 기반 애니메이션 처리
                if (_componentManager.TryGetComponent<StateComponent>(entityId, out var state) == true)
                {
                    if (UpdateAnimatorFromState(entityId, ref spriteAnim, ref state, animData) == true)
                    {
                        changed = true;
                    }
                }

                // 3. 프레임 타이머 갱신 및 스프라이트 교체
                if (spriteAnim.IsPlaying == true && spriteAnim.FrameDuration > 0f)
                {
                    spriteAnim.FrameTimer += inDeltaTime * spriteAnim.PlaybackSpeed;

                    if (spriteAnim.FrameTimer >= spriteAnim.FrameDuration)
                    {
                        spriteAnim.FrameTimer -= spriteAnim.FrameDuration;

                        int frameCount = animData.GetFrameCount(spriteAnim.CurrentCategory);

                        if (frameCount > 0)
                        {
                            int nextFrame = spriteAnim.CurrentFrame + 1;

                            if (spriteAnim.IsLooping == true)
                            {
                                spriteAnim.CurrentFrame = nextFrame % frameCount;
                            }
                            else
                            {
                                // 원샷: 마지막 프레임에서 정지
                                if (nextFrame >= frameCount)
                                {
                                    spriteAnim.CurrentFrame = frameCount - 1;
                                    spriteAnim.FrameTimer = 0f;
                                }
                                else
                                {
                                    spriteAnim.CurrentFrame = nextFrame;
                                }
                            }
                        }

                        changed = true;
                    }
                }

                // 4. 스프라이트 교체
                if (changed == true)
                {
                    animData.SetSprite(spriteAnim.CurrentCategory, spriteAnim.CurrentFrame);
                }

                _componentManager.SetComponent(entityId, spriteAnim);
            }
        }

        /// <summary>
        /// AnimatorComponent 요청 처리
        /// </summary>
        private bool ProcessAnimationRequest(int entityId, ref SpriteAnimationComponent spriteAnim, ref AnimatorComponent animatorComp, SpriteAnimationData animData)
        {
            GlobalEnum.AnimCategory category = animatorComp.RequestedCategory;

            // Force가 아니고 같은 카테고리면 무시
            if (animatorComp.Force == false && spriteAnim.CurrentCategory == category)
            {
                return false;
            }

            // 카테고리가 없으면 Idle로 폴백
            if (animData.HasCategory(category) == false)
            {
                category = GlobalEnum.AnimCategory.Idle;
            }

            spriteAnim.CurrentCategory = category;
            spriteAnim.CurrentFrame = 0;
            spriteAnim.FrameTimer = 0f;

            // RequestedDuration > 0이면 스킬 타이밍에 맞춰 FrameDuration 계산
            if (animatorComp.RequestedDuration > 0f)
            {
                int frameCount = animData.GetFrameCount(category);
                if (frameCount > 0)
                {
                    spriteAnim.FrameDuration = animatorComp.RequestedDuration / frameCount;
                }

                spriteAnim.IsLooping = false;
            }
            else
            {
                // 기본 FrameDuration 사용
                spriteAnim.FrameDuration = GetDefaultFrameDuration(entityId, category);
                spriteAnim.IsLooping = true;
            }

            spriteAnim.IsPlaying = true;

            return true;
        }

        /// <summary>
        /// StateComponent 변화 감지 → 애니메이션 카테고리 전환
        /// </summary>
        private bool UpdateAnimatorFromState(int entityId, ref SpriteAnimationComponent spriteAnim, ref StateComponent state, SpriteAnimationData animData)
        {
            bool changed = false;

            // Condition 변화 처리
            if (state.Condition != state.ConditionPrev)
            {
                switch (state.Condition)
                {
                    case Creature.CharacterConditions.Normal:
                        ChangeCategory(entityId, ref spriteAnim, GlobalEnum.AnimCategory.Idle, animData, true);
                        changed = true;
                        break;
                    case Creature.CharacterConditions.Dead:
                        ChangeCategory(entityId, ref spriteAnim, GlobalEnum.AnimCategory.Dead, animData, false);
                        changed = true;
                        break;
                    case Creature.CharacterConditions.UseSkill:
                        // 스킬은 AnimatorComponent 요청으로 처리되므로 여기서는 무시
                        break;
                    case Creature.CharacterConditions.Stunned:
                        break;
                }

                state.ConditionPrev = state.Condition;
                AR.s.Component.SetComponent(entityId, state);
            }

            // Normal 상태일 때만 이동 애니메이션 적용
            if (state.Condition == Creature.CharacterConditions.Normal)
            {
                if (state.MoveState != state.MovementStatePrev)
                {
                    switch (state.MoveState)
                    {
                        case Creature.MovementStates.Idle:
                            ChangeCategory(entityId, ref spriteAnim, GlobalEnum.AnimCategory.Idle, animData, true);
                            changed = true;
                            break;
                        case Creature.MovementStates.Walking:
                            ChangeCategory(entityId, ref spriteAnim, GlobalEnum.AnimCategory.Move, animData, true);
                            changed = true;
                            break;
                    }

                    state.MovementStatePrev = state.MoveState;
                    AR.s.Component.SetComponent(entityId, state);
                }
            }

            return changed;
        }

        /// <summary>
        /// 카테고리 변경 헬퍼
        /// </summary>
        private void ChangeCategory(int entityId, ref SpriteAnimationComponent spriteAnim, GlobalEnum.AnimCategory category, SpriteAnimationData animData, bool isLooping)
        {
            // 카테고리가 없으면 Idle로 폴백
            if (animData.HasCategory(category) == false)
            {
                category = GlobalEnum.AnimCategory.Idle;
            }

            spriteAnim.CurrentCategory = category;
            spriteAnim.CurrentFrame = 0;
            spriteAnim.FrameTimer = 0f;
            spriteAnim.FrameDuration = GetDefaultFrameDuration(entityId, category);
            spriteAnim.IsLooping = isLooping;
            spriteAnim.IsPlaying = true;
        }

        /// <summary>
        /// AnimationTable에서 설정된 기본 FrameDuration 조회
        /// </summary>
        private float GetDefaultFrameDuration(int entityId, GlobalEnum.AnimCategory category)
        {
            if (_defaultFrameDurations != null && _defaultFrameDurations.TryGetValue(entityId, out var durations) == true)
            {
                int idx = (int)category;
                if (idx >= 0 && idx < durations.Length && durations[idx] > 0f)
                {
                    return durations[idx];
                }
            }

            // 기본값
            return 0.1f;
        }
    }
}
