#nullable enable
using ARPG.Component;
using ARPG.Skill;
using UnityEngine;

namespace ARPG.Systems
{
    /// <summary>
    /// 실행 중인 스킬 단계에 맞춰 소유자의 일반 이동 배율을 계산합니다.
    /// </summary>
    public class System_SkillMovementPenalty : IFixedUpdateSystem
    {
        public int Priority => 90;

        private ComponentManager _componentManager = null!;
        private SparseSet<SkillMovementPenaltyComponent> _penaltyPool = null!;
        private SparseSet<SkillStateComponent> _skillStatePool = null!;
        private SparseSet<SkillTimingComponent> _skillTimingPool = null!;

        public void OnCreate()
        {
            _componentManager = AR.s.Component;
            _penaltyPool = _componentManager.GetComponentPool<SkillMovementPenaltyComponent>();
            _skillStatePool = _componentManager.GetComponentPool<SkillStateComponent>();
            _skillTimingPool = _componentManager.GetComponentPool<SkillTimingComponent>();

            Debug.Log("System_SkillMovementPenalty Created");
        }

        public void OnReset()
        {
            for (int i = _penaltyPool.Count - 1; i >= 0; i--)
            {
                _componentManager.RemoveComponent<SkillMovementPenaltyComponent>(_penaltyPool.GetEntityId(i));
            }

            Debug.Log("System_SkillMovementPenalty Reset");
        }

        public void OnFixedUpdate(float inFixedDeltaTime)
        {
            for (int i = _penaltyPool.Count - 1; i >= 0; i--)
            {
                int ownerEntityId = _penaltyPool.GetEntityId(i);
                SkillMovementPenaltyComponent penalty = _penaltyPool.GetByIndex(i);
                if (_skillStatePool.TryGet(penalty.SkillEntityId, out SkillStateComponent skillState) == false || skillState.IsRunning == false 
                    || _skillTimingPool.TryGet(penalty.SkillEntityId, out SkillTimingComponent timing) == false)
                {
                    _componentManager.RemoveComponent<SkillMovementPenaltyComponent>(ownerEntityId);
                    continue;
                }

                // 감속하지 않는 모드(FullSpeed/OwnedMotion)는 SkillUsePolicyResolver.ResolveMovementMultiplier가
                // 이미 BaseMultiplier=1f로 해석해 두므로 여기서 모드를 다시 분기하지 않는다 (규칙 단일 출처).
                // 후딜 복구 여부도 SkillUsePolicyResolver가 정한다. 복구하지 않는 모드는 후딜 내내 실행 단계 배율을 유지한다.
                if (skillState.State == SkillState.End
                    && SkillUsePolicyResolver.RecoversMovementDuringEnd(penalty.MovementMode) == true)
                {
                    penalty.EndRecoveryProgress = timing.EndDuration > 0f ? Mathf.Clamp01(skillState.ElapsedTime / timing.EndDuration) : 1f;
                    penalty.CurrentMultiplier = Mathf.Lerp(penalty.BaseMultiplier, 1f, penalty.EndRecoveryProgress);
                }
                else
                {
                    penalty.CurrentMultiplier = penalty.BaseMultiplier;
                    penalty.EndRecoveryProgress = 0f;
                }

                _componentManager.SetComponent(ownerEntityId, penalty);
            }
        }
    }
}
