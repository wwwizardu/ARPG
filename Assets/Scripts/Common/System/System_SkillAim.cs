#nullable enable
using ARPG.Component;
using ARPG.Skill;
using ARPG.Skill.Aim;
using UnityEngine;
using GE = GlobalEnum;

namespace ARPG.Systems
{
    /// <summary>
    /// 실행 중인 스킬의 논리 조준 방향을 FixedUpdate 기준으로 갱신합니다.
    /// 플레이어는 커서를, AI는 노리는 대상(AIComponent.TargetEntityId)을 회전 속도 제한으로 추적합니다
    /// (AI 추적은 POE2_CURSOR_TRACKING_PLAN §15, 2026-10-13).
    /// </summary>
    public class System_SkillAim : IFixedUpdateSystem
    {
        public int Priority => 190;

        private ComponentManager _componentManager = null!;
        private SparseSet<SkillComponent> _skillPool = null!;
        private SparseSet<SkillStateComponent> _skillStatePool = null!;
        private SparseSet<SkillTargetComponent> _skillTargetPool = null!;
        private SparseSet<InputComponent> _inputPool = null!;
        private SparseSet<TransformComponent> _transformPool = null!;
        private SparseSet<AIComponent> _aiPool = null!;

        public void OnCreate()
        {
            _componentManager = AR.s.Component;
            _skillPool = _componentManager.GetComponentPool<SkillComponent>();
            _skillStatePool = _componentManager.GetComponentPool<SkillStateComponent>();
            _skillTargetPool = _componentManager.GetComponentPool<SkillTargetComponent>();
            _inputPool = _componentManager.GetComponentPool<InputComponent>();
            _transformPool = _componentManager.GetComponentPool<TransformComponent>();
            _aiPool = _componentManager.GetComponentPool<AIComponent>();

            Debug.Log("System_SkillAim Created");
        }

        public void OnReset()
        {
            Debug.Log("System_SkillAim Reset");
        }

        public void OnFixedUpdate(float inFixedDeltaTime)
        {
            // 스킬 풀에는 플레이어·몬스터·NPC의 슬롯별 스킬 엔티티가 모두 들어 있고 대부분은 쉬는 중이다.
            // 가장 값싼 판별(실행 상태)을 루프 맨 앞에 두고, 실행 중인 스킬만 조준 정책을 해석한다.
            for (int i = 0; i < _skillPool.Count; i++)
            {
                int skillEntityId = _skillPool.GetEntityId(i);
                if (_skillStatePool.TryGet(skillEntityId, out SkillStateComponent state) == false
                    || state.IsRunning == false
                    || state.State == SkillState.End)
                {
                    continue;
                }

                SkillComponent skill = _skillPool.GetByIndex(i);
                if (skill.IsInitialized == false || skill.Table == null)
                {
                    continue;
                }

                // 추적 여부는 해석된 정책만으로 판단한다. 비방향성 스킬은 Default 해석에서
                // SnapshotOnCommand가 되므로 아래 검사에서 걸러지고, 테이블 명시값은 그대로 존중된다.
                bool hasPlayerInput = _inputPool.TryGet(skill.OwnerEntityId, out InputComponent input);
                GE.SkillAimTrackingMode trackingMode = SkillUsePolicyResolver.ResolveAimTrackingMode(skill, hasPlayerInput);
                if (trackingMode == GE.SkillAimTrackingMode.SnapshotOnCommand
                    || (trackingMode == GE.SkillAimTrackingMode.TrackUntilCommit && skill.IsAimCommitted == true))
                {
                    continue;
                }

                if (_transformPool.TryGet(skill.OwnerEntityId, out TransformComponent ownerTransform) == false
                    || _skillTargetPool.TryGet(skillEntityId, out SkillTargetComponent target) == false)
                {
                    continue;
                }

                Vector2 aimPoint;
                if (hasPlayerInput == true)
                {
                    aimPoint = input.IsGamepad == true ? target.ControllerAimPoint : input.MouseWorldPosition;
                }
                else if (TryGetAiAimPoint(skill.OwnerEntityId, out aimPoint) == false)
                {
                    // 노리는 대상이 없으면 방향을 그대로 둔다 (토템처럼 AIComponent가 없는 시전자 포함)
                    continue;
                }

                Vector2 desiredDirection = SkillAimResolver.ResolveDirection(
                    ownerTransform.Position,
                    aimPoint,
                    target.TargetDirection);
                target.TargetDirection = SkillAimResolver.RotateTowards(
                    target.TargetDirection,
                    desiredDirection,
                    SkillUsePolicyResolver.ResolveAimTurnSpeed(skill.Table),
                    inFixedDeltaTime);
                _componentManager.SetComponent(skillEntityId, target);
            }
        }

        /// <summary>
        /// AI가 노리는 대상의 현재 발 위치. 대상이 없거나 위치를 알 수 없으면 false.
        /// </summary>
        private bool TryGetAiAimPoint(int inOwnerEntityId, out Vector2 outAimPoint)
        {
            outAimPoint = Vector2.zero;
            if (_aiPool.TryGet(inOwnerEntityId, out AIComponent ai) == false || ai.TargetEntityId == -1)
                return false;

            if (_transformPool.TryGet(ai.TargetEntityId, out TransformComponent targetTransform) == false)
                return false;

            outAimPoint = targetTransform.Position;
            return true;
        }
    }
}
