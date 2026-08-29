#nullable enable
using ARPG.Component;
using ARPG.Skill;
using ARPG.Skill.Aim;
using UnityEngine;
using GE = GlobalEnum;

namespace ARPG.Systems
{
    /// <summary>
    /// 실행 중인 플레이어 스킬의 논리 조준 방향을 FixedUpdate 기준으로 갱신합니다.
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

        public void OnCreate()
        {
            _componentManager = AR.s.Component;
            _skillPool = _componentManager.GetComponentPool<SkillComponent>();
            _skillStatePool = _componentManager.GetComponentPool<SkillStateComponent>();
            _skillTargetPool = _componentManager.GetComponentPool<SkillTargetComponent>();
            _inputPool = _componentManager.GetComponentPool<InputComponent>();
            _transformPool = _componentManager.GetComponentPool<TransformComponent>();

            Debug.Log("System_SkillAim Created");
        }

        public void OnReset()
        {
            Debug.Log("System_SkillAim Reset");
        }

        public void OnFixedUpdate(float inFixedDeltaTime)
        {
            // 커서 조준은 InputComponent를 가진 엔티티(플레이어)만 대상이다.
            // 스킬 풀에는 몬스터/NPC의 슬롯별 스킬 엔티티가 모두 들어 있으므로,
            // 상태·타겟을 읽고 나서 걸러내면 대부분이 낭비가 된다.
            // 따라서 가장 값싼 판별(Contains = 배열 인덱스 1회)을 루프 맨 앞에 둔다.
            if (_inputPool.Count == 0)
            {
                return;
            }

            for (int i = 0; i < _skillPool.Count; i++)
            {
                int skillEntityId = _skillPool.GetEntityId(i);
                SkillComponent skill = _skillPool.GetByIndex(i);
                if (_inputPool.Contains(skill.OwnerEntityId) == false)
                {
                    continue;
                }

                if (skill.IsInitialized == false || skill.Table == null)
                {
                    continue;
                }

                if (_skillStatePool.TryGet(skillEntityId, out SkillStateComponent state) == false
                    || state.IsRunning == false
                    || state.State == SkillState.End)
                {
                    continue;
                }

                if (_inputPool.TryGet(skill.OwnerEntityId, out InputComponent input) == false
                    || _transformPool.TryGet(skill.OwnerEntityId, out TransformComponent ownerTransform) == false
                    || _skillTargetPool.TryGet(skillEntityId, out SkillTargetComponent target) == false)
                {
                    continue;
                }

                // 추적 여부는 해석된 정책만으로 판단한다. 비방향성 스킬은 Default 해석에서
                // SnapshotOnCommand가 되므로 아래 검사에서 걸러지고, 테이블 명시값은 그대로 존중된다.
                GE.SkillAimTrackingMode trackingMode = SkillUsePolicyResolver.ResolveAimTrackingMode(skill, true);
                if (trackingMode == GE.SkillAimTrackingMode.SnapshotOnCommand
                    || (trackingMode == GE.SkillAimTrackingMode.TrackUntilCommit && skill.IsAimCommitted == true))
                {
                    continue;
                }

                Vector2 desiredDirection = SkillAimResolver.ResolveDirection(
                    ownerTransform.Position,
                    input.MouseWorldPosition,
                    target.TargetDirection);
                target.TargetDirection = SkillAimResolver.RotateTowards(
                    target.TargetDirection,
                    desiredDirection,
                    SkillUsePolicyResolver.ResolveAimTurnSpeed(skill.Table),
                    inFixedDeltaTime);
                _componentManager.SetComponent(skillEntityId, target);
            }
        }
    }
}
