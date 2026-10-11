#nullable enable
using ARPG.Component;
using UnityEngine;

namespace ARPG.Systems
{
    /// <summary>
    /// 이동 둔화(Slow) 버프로부터 대상별 현재 둔화율을 계산합니다.
    /// 버프 남은 시간이 FadeOutTime보다 크면 최대 둔화율을 그대로 쓰고, 그 아래로 내려가면 0까지 선형으로 줄입니다.
    /// 한 대상에 둔화가 여러 개면 가장 강한 하나만 MoveSlowComponent에 남깁니다.
    /// 둔화율은 매 틱 바뀌므로 스탯(StatModifier)에 넣지 않고 이동 속도를 계산하는 곳이 배율로 곱합니다.
    ///
    /// Priority 95: Skill Movement Penalty(90) 이후, Move(100) 이전.
    /// </summary>
    public class System_MoveSlow : IFixedUpdateSystem
    {
        public int Priority => 95;
        public float UpdateInterval => 0f;

        private ComponentManager _componentManager = null!;
        private SparseSet<MoveSlowBuffComponent> _slowBuffPool = null!;
        private SparseSet<MoveSlowComponent> _slowPool = null!;
        private SparseSet<BuffInstance> _buffPool = null!;

        public void OnCreate()
        {
            _componentManager = AR.s.Component;
            _slowBuffPool = _componentManager.GetComponentPool<MoveSlowBuffComponent>();
            _slowPool = _componentManager.GetComponentPool<MoveSlowComponent>();
            _buffPool = _componentManager.GetComponentPool<BuffInstance>();

            Debug.Log("System_MoveSlow Created");
        }

        public void OnReset()
        {
            for (int i = _slowPool.Count - 1; i >= 0; i--)
            {
                _componentManager.RemoveComponent<MoveSlowComponent>(_slowPool.GetEntityId(i));
            }

            Debug.Log("System_MoveSlow Reset");
        }

        public void OnFixedUpdate(float inFixedDeltaTime)
        {
            if (_slowBuffPool.Count == 0 && _slowPool.Count == 0)
                return;

            // 1. 지난 틱 값을 비운다. 아래에서 살아 있는 둔화 버프가 다시 채운다.
            for (int i = 0; i < _slowPool.Count; i++)
            {
                MoveSlowComponent slow = _slowPool.GetByIndex(i);
                slow.SlowRate = 0f;
                _slowPool.SetByIndex(i, slow);
            }

            // 2. 둔화 버프마다 현재 둔화율을 구해 대상별 최댓값만 남긴다.
            for (int i = 0; i < _slowBuffPool.Count; i++)
            {
                int buffEntityId = _slowBuffPool.GetEntityId(i);
                if (_buffPool.TryGet(buffEntityId, out BuffInstance buff) == false)
                    continue;

                float slowRate = ResolveSlowRate(_slowBuffPool.GetByIndex(i), buff.RemainTime);
                if (slowRate <= 0f)
                    continue;

                int targetEntityId = buff.TargetEntityId;
                if (_slowPool.TryGet(targetEntityId, out MoveSlowComponent targetSlow) == true)
                {
                    if (slowRate > targetSlow.SlowRate)
                    {
                        targetSlow.SlowRate = slowRate;
                        _slowPool.Set(targetEntityId, targetSlow);
                    }
                }
                else if (AR.s.Entity.IsEntityRegistered(targetEntityId) == true)
                {
                    _componentManager.AddComponent(targetEntityId, new MoveSlowComponent { SlowRate = slowRate });
                }
            }

            // 3. 둔화가 남지 않은 대상은 정리한다.
            for (int i = _slowPool.Count - 1; i >= 0; i--)
            {
                if (_slowPool.GetByIndex(i).SlowRate <= 0f)
                {
                    _componentManager.RemoveComponent<MoveSlowComponent>(_slowPool.GetEntityId(i));
                }
            }
        }

        private static float ResolveSlowRate(MoveSlowBuffComponent slowBuff, float remainTime)
        {
            if (slowBuff.FadeOutTime <= 0f || remainTime >= slowBuff.FadeOutTime)
                return slowBuff.SlowRate;

            return slowBuff.SlowRate * Mathf.Clamp01(remainTime / slowBuff.FadeOutTime);
        }
    }
}
