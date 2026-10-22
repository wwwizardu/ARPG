#nullable enable
using ARPG.Component;
using ARPG.Manager;
using ARPG.Skill.Combat;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace ARPG.Systems
{
    /// <summary>채널 첫 타격부터 종료까지 시전자 위치와 논리 조준을 따라 루프 이펙트를 표시한다.</summary>
    public class System_SkillChannelEffect : IUpdateSystem
    {
        public int Priority => 520;
        public float UpdateInterval => 0f;

        private ComponentManager _componentManager = null!;
        private SparseSet<SkillComponent> _skillPool = null!;
        private SparseSet<SkillStateComponent> _statePool = null!;
        private SparseSet<SkillTargetComponent> _targetPool = null!;
        private SparseSet<TransformComponent> _transformPool = null!;
        private SparseSet<SkillChannelEffectComponent> _effectPool = null!;

        public void OnCreate()
        {
            _componentManager = AR.s.Component;
            _skillPool = _componentManager.GetComponentPool<SkillComponent>();
            _statePool = _componentManager.GetComponentPool<SkillStateComponent>();
            _targetPool = _componentManager.GetComponentPool<SkillTargetComponent>();
            _transformPool = _componentManager.GetComponentPool<TransformComponent>();
            _effectPool = _componentManager.GetComponentPool<SkillChannelEffectComponent>();
        }

        public void OnReset()
        {
            for (int i = _effectPool.Count - 1; i >= 0; i--)
                StopEffect(_effectPool.GetEntityId(i));
        }

        public void OnUpdate(float inDeltaTime)
        {
            for (int i = 0; i < _skillPool.Count; i++)
            {
                int skillEntityId = _skillPool.GetEntityId(i);
                if (_effectPool.Contains(skillEntityId) == true
                    || TryGetPose(skillEntityId, out Vector3 position, out Quaternion rotation) == false)
                    continue;

                SkillComponent skill = _skillPool.GetByIndex(i);
                object request = new object();
                _componentManager.SetComponent(skillEntityId, new SkillChannelEffectComponent
                {
                    Handle = EffectHandle.Invalid,
                    LoadRequest = request,
                });
                SpawnEffect(skillEntityId, skill.Table!.ActivateName, request, position, rotation).Forget();
            }

            for (int i = _effectPool.Count - 1; i >= 0; i--)
            {
                int skillEntityId = _effectPool.GetEntityId(i);
                if (TryGetPose(skillEntityId, out Vector3 position, out Quaternion rotation) == false)
                {
                    StopEffect(skillEntityId);
                    continue;
                }

                SkillChannelEffectComponent effect = _effectPool.GetByIndex(i);
                if (effect.Handle.IsValid == true)
                    AR.s.Effect.SetPose(effect.Handle, position, rotation);
            }
        }

        /// <summary>엔티티/슬롯 제거도 로드 중인 요청을 무효화한 뒤 핸들을 반환한다.</summary>
        public void StopEffect(int skillEntityId)
        {
            if (_effectPool.TryGet(skillEntityId, out SkillChannelEffectComponent effect) == false)
                return;

            _componentManager.RemoveComponent<SkillChannelEffectComponent>(skillEntityId);
            AR.s.Effect.Stop(effect.Handle);
        }

        private bool TryGetPose(int skillEntityId, out Vector3 position, out Quaternion rotation)
        {
            position = default;
            rotation = Quaternion.identity;
            if (_statePool.TryGet(skillEntityId, out SkillStateComponent state) == false
                || state.IsRunning == false
                || state.State != SkillState.Process
                || _skillPool.TryGet(skillEntityId, out SkillComponent skill) == false
                || skill.ExecutionType != SkillExecutionType.Channeling
                || skill.CurrentHitIndex <= 0
                || skill.Table == null
                || string.IsNullOrEmpty(skill.Table.ActivateName) == true
                || skill.Table.ActivateName.StartsWith("Effects/", System.StringComparison.Ordinal) == false
                || SkillInterruptRule.Evaluate(_componentManager, skill.OwnerEntityId) != SkillInterruptReason.None
                || _transformPool.TryGet(skill.OwnerEntityId, out TransformComponent owner) == false
                || _targetPool.TryGet(skillEntityId, out SkillTargetComponent target) == false)
                return false;

            if (_componentManager.TryGetComponent<InputComponent>(skill.OwnerEntityId, out InputComponent input) == true
                && (input.SkillSlotHeldMask & (1 << skill.SlotIndex)) == 0)
                return false;

            position = new Vector3(owner.Position.x, owner.Position.y, 0f);
            float angle = Mathf.Atan2(target.TargetDirection.y, target.TargetDirection.x) * Mathf.Rad2Deg;
            rotation = Quaternion.Euler(0f, 0f, angle);
            return true;
        }

        private async UniTask SpawnEffect(int skillEntityId, string key, object request, Vector3 position, Quaternion rotation)
        {
            EffectManager effects = AR.s.Effect;
            EffectHandle handle = await effects.Spawn(key, position, rotation);
            // 같은 결정적 스킬 ID가 재장착/재시전에 재사용돼도 옛 로드 결과를 새 시전에 붙이지 않는다.
            if (_effectPool.TryGet(skillEntityId, out SkillChannelEffectComponent effect) == false
                || ReferenceEquals(effect.LoadRequest, request) == false
                || TryGetPose(skillEntityId, out position, out rotation) == false)
            {
                if (effects != null)
                    effects.Stop(handle);
                return;
            }

            effect.Handle = handle;
            _componentManager.SetComponent(skillEntityId, effect);
            effects.SetPose(handle, position, rotation);
        }
    }
}
