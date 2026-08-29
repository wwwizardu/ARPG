#nullable enable
using ARPG.Base;
using ARPG.Component;
using ARPG.Skill;
using ARPG.Skill.Aim;
using ARPG.View;
using UnityEngine;

namespace ARPG.Systems
{
    /// <summary>
    /// ECS의 현재 유효 조준 방향을 플레이어 전용 화살표 View에 그대로 표시합니다.
    /// </summary>
    public class System_SkillAimIndicator : IUpdateSystem
    {
        public int Priority => 510;

        private ComponentManager _componentManager = null!;
        private System_Render? _renderSystem;
        private SparseSet<SkillAimIndicatorComponent> _indicatorPool = null!;
        private SparseSet<SkillComponent> _skillPool = null!;
        private SparseSet<SkillStateComponent> _skillStatePool = null!;
        private SparseSet<SkillTargetComponent> _skillTargetPool = null!;
        private SparseSet<StateComponent> _ownerStatePool = null!;

        public void OnCreate()
        {
            _componentManager = AR.s.Component;
            _renderSystem = AR.s.System.GetSystem<System_Render>();
            _indicatorPool = _componentManager.GetComponentPool<SkillAimIndicatorComponent>();
            _skillPool = _componentManager.GetComponentPool<SkillComponent>();
            _skillStatePool = _componentManager.GetComponentPool<SkillStateComponent>();
            _skillTargetPool = _componentManager.GetComponentPool<SkillTargetComponent>();
            _ownerStatePool = _componentManager.GetComponentPool<StateComponent>();

            Debug.Log("System_SkillAimIndicator Created");
        }

        public void OnReset()
        {
            for (int i = 0; i < _indicatorPool.Count; i++)
            {
                int ownerEntityId = _indicatorPool.GetEntityId(i);
                ResetIndicator(ownerEntityId, _indicatorPool.GetByIndex(i));
            }

            Debug.Log("System_SkillAimIndicator Reset");
        }

        public void OnUpdate(float inDeltaTime)
        {
            if (_renderSystem == null)
            {
                _renderSystem = AR.s.System.GetSystem<System_Render>();
            }

            for (int i = 0; i < _indicatorPool.Count; i++)
            {
                int ownerEntityId = _indicatorPool.GetEntityId(i);
                SkillAimIndicatorComponent indicator = _indicatorPool.GetByIndex(i);
                EntityBase? entityBase = _renderSystem?.GetEntity(ownerEntityId);
                SkillAimIndicatorView? view = entityBase != null ? entityBase.SkillAimIndicatorView : null;
                if (view == null)
                {
                    indicator.IsVisible = false;
                    _componentManager.SetComponent(ownerEntityId, indicator);
                    continue;
                }

                if (ShouldShow(ownerEntityId, indicator.ActiveSkillEntityId, out SkillTargetComponent target) == false)
                {
                    if (indicator.IsVisible == true)
                    {
                        view.Hide();
                    }
                    indicator.IsVisible = false;
                    _componentManager.SetComponent(ownerEntityId, indicator);
                    continue;
                }

                float displayAngle = SkillAimResolver.GetDisplayAngle(target.TargetDirection, view.AngleOffsetDeg);
                view.SetAngle(displayAngle);
                if (indicator.IsVisible == false)
                {
                    view.Show();
                }

                indicator.IsVisible = true;
                indicator.LastAppliedAngle = displayAngle;
                _componentManager.SetComponent(ownerEntityId, indicator);
            }
        }

        private bool ShouldShow(int ownerEntityId, int skillEntityId, out SkillTargetComponent target)
        {
            target = default;
            if (skillEntityId < 0
                || _skillPool.TryGet(skillEntityId, out SkillComponent skill) == false
                || skill.Table == null
                || SkillUsePolicyResolver.RequiresDirectionalAim(skill.Table) == false
                || _skillStatePool.TryGet(skillEntityId, out SkillStateComponent skillState) == false
                || skillState.IsRunning == false
                || _skillTargetPool.TryGet(skillEntityId, out target) == false
                || target.TargetDirection.sqrMagnitude < SkillAimResolver.DirectionEpsilonSqr)
            {
                return false;
            }

            if (_ownerStatePool.TryGet(ownerEntityId, out StateComponent ownerState) == true
                && ownerState.Condition >= Creature.CharacterConditions.Stunned)
            {
                return false;
            }

            return true;
        }

        private void ResetIndicator(int ownerEntityId, SkillAimIndicatorComponent indicator)
        {
            EntityBase? entityBase = _renderSystem?.GetEntity(ownerEntityId);
            entityBase?.SkillAimIndicatorView?.ResetView();
            indicator.Reset();
            _componentManager.SetComponent(ownerEntityId, indicator);
        }
    }
}
