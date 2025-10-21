#nullable enable
using System.Collections.Generic;
using ARPG.Data;
using ARPG.Tables;
using UnityEngine;

namespace ARPG.Creature
{
    public class BuffController
    {
        private CharacterBase? _character;
        private readonly List<BuffData> _buffList = new();                          // 버프 원본 리스트
        private readonly Dictionary<GlobalEnum.BuffEffectType, BuffEffect> _buffDic = new();  // 효과 타입별 빠른 접근용

        private float _tickTimer = 0f;
        private const float TICK_INTERVAL = 0.1f; // 0.1초마다 틱 처리

        // ========================================
        // Public Methods
        // ========================================

        public void Initialize(CharacterBase inCharacter)
        {
            _character = inCharacter;
            _buffList.Clear();
            _buffDic.Clear();
            _tickTimer = 0f;
        }

        public void Reset()
        {
            _buffList.Clear();
            _buffDic.Clear();
            _tickTimer = 0f;
        }

        public bool AddBuff(int inBuffId, int inEffectValue = 0)
        {
            BuffTable? buffTable = AR.s.Data != null ? AR.s.Data.GetBuff(inBuffId) : null;
            if (buffTable == null)
            {
                Debug.LogError($"[BuffController] AddBuff - BuffTable not found, BuffId({inBuffId})");
                return false;
            }

            // 이미 같은 버프 ID가 있는지 확인
            BuffData? existingBuff = _buffList.Find(b => b.Id == inBuffId);
            if (existingBuff != null)
            {
                // 스택 가능한 버프인 경우
                if (buffTable.MaxStack > 1)
                {
                    // TODO: 스택 처리 로직
                }
                
                // 시간 리셋
                existingBuff.LeftTime = buffTable.Duration;
                Debug.Log($"[BuffController] AddBuff - Buff refreshed, BuffId({inBuffId})");
                return true;
            }

            // 새 버프 생성
            BuffData newBuff = new()
            {
                Id = inBuffId,
                LeftTime = buffTable.Duration,
                Damage = 0,
                BuffEffect = CreateBuffEffect(buffTable, inEffectValue),
                Table = buffTable
            };

            _buffList.Add(newBuff);

            // Dictionary에 증분 업데이트
            AddBuffToDictionary(newBuff);

            // 버프 효과 적용
            ApplyBuffEffect(newBuff);


            Debug.Log($"[BuffController] AddBuff - BuffId({inBuffId}), Name({buffTable.Name})");
            return true;
        }

        public bool RemoveBuff(int inBuffId)
        {
            BuffData? buff = _buffList.Find(b => b.Id == inBuffId);
            if (buff == null)
            {
                Debug.LogWarning($"[BuffController] RemoveBuff - Buff not found, BuffId({inBuffId})");
                return false;
            }

            // Dictionary에서 증분 제거
            RemoveBuffFromDictionary(buff);

            // 버프 효과 제거
            RemoveBuffEffect(buff);

            _buffList.Remove(buff);

            Debug.Log($"[BuffController] RemoveBuff - BuffId({inBuffId})");
            return true;
        }

        public void ClearAllBuffs()
        {
            // 모든 버프 효과 제거
            foreach (BuffData buff in _buffList)
            {
                RemoveBuffEffect(buff);
            }

            _buffList.Clear();
            _buffDic.Clear();
        }

        public void UpdateTick(float inDeltaTime)
        {
            if (_buffList.Count == 0)
                return;

            _tickTimer += inDeltaTime;

            // TICK_INTERVAL마다 처리
            if (_tickTimer >= TICK_INTERVAL)
            {
                _tickTimer -= TICK_INTERVAL;

                // 역순으로 순회하면서 제거할 버프 처리
                for (int i = _buffList.Count - 1; i >= 0; i--)
                {
                    BuffData buff = _buffList[i];

                    // 남은 시간 감소
                    buff.LeftTime -= TICK_INTERVAL;

                    // 틱 데미지 처리
                    if (buff.Table != null && buff.Table.TickInterval > 0)
                    {
                        // 데미지 들어가는 틱 업데이트
                        UpdateDamageTick();
                    }

                    // 버프 시간이 만료되면 제거
                    if (buff.LeftTime <= 0)
                    {
                        RemoveBuffEffect(buff);
                        RemoveBuffFromDictionary(buff); // 증분 업데이트
                        _buffList.RemoveAt(i);
                        Debug.Log($"[BuffController] Buff expired - BuffId({buff.Id})");
                    }
                }
            }
        }

        public bool HasBuff(int inBuffId)
        {
            return _buffList.Find(b => b.Id == inBuffId) != null;
        }

        public bool HasBuffEffect(GlobalEnum.BuffEffectType inBuffEffectType)
        {
            return _buffDic.ContainsKey(inBuffEffectType);
        }

        public BuffData? GetBuff(int inBuffId)
        {
            return _buffList.Find(b => b.Id == inBuffId);
        }

        public BuffEffect? GetBuffEffect(GlobalEnum.BuffEffectType inBuffEffectType)
        {
            if (_buffDic.ContainsKey(inBuffEffectType))
                return _buffDic[inBuffEffectType];

            return null;
        }

        public List<BuffData> GetAllBuffs()
        {
            return _buffList;
        }

        public Dictionary<GlobalEnum.BuffEffectType, BuffEffect> GetAllBuffEffects()
        {
            return _buffDic;
        }

        // ========================================
        // Private Methods
        // ========================================

        /// <summary>
        /// BuffEffect를 생성하고 inEffectValue로 값을 덮어씁니다.
        /// </summary>
        private BuffEffect CreateBuffEffect(BuffTable buffTable, int inEffectValue)
        {
            BuffEffect newEffect = new()
            {
                Type = buffTable.EffectType,
                Value = inEffectValue != 0 ? (ushort)inEffectValue : (ushort)buffTable.EffectValue
            };

            return newEffect;
        }

        /// <summary>
        /// 버프를 Dictionary에 추가합니다.
        /// 같은 효과 타입이 있으면 값을 누적합니다.
        /// </summary>
        private void AddBuffToDictionary(BuffData buff)
        {
            if (buff.BuffEffect == null)
                return;

            BuffEffect effect = buff.BuffEffect;

            if (_buffDic.ContainsKey(effect.Type))
            {
                // 같은 효과 타입이 이미 있으면 값을 누적
                _buffDic[effect.Type].Value += effect.Value;
            }
            else
            {
                // 새로운 효과 타입이면 추가 (값 복사)
                _buffDic[effect.Type] = new BuffEffect
                {
                    Type = effect.Type,
                    Value = effect.Value
                };
            }
        }

        /// <summary>
        /// 버프를 Dictionary에서 제거합니다.
        /// 같은 효과 타입의 다른 버프가 있으면 값을 차감하고, 없으면 제거합니다.
        /// </summary>
        private void RemoveBuffFromDictionary(BuffData buff)
        {
            if (buff.BuffEffect == null)
                return;

            BuffEffect effect = buff.BuffEffect;

            if (_buffDic.ContainsKey(effect.Type))
            {
                // 값을 차감
                _buffDic[effect.Type].Value -= effect.Value;

                // 값이 0 이하가 되면 Dictionary에서 제거
                if (_buffDic[effect.Type].Value <= 0)
                {
                    _buffDic.Remove(effect.Type);
                }
            }
        }

        private void ApplyBuffEffect(BuffData buff)
        {
            if (buff?.BuffEffect == null || _character == null)
                return;

            // 버프 이펙트 적용
            BuffEffect buffEffect = buff.BuffEffect;
            if(_buffDic.ContainsKey(buffEffect.Type) == true)
            {
                _character.OnAddBuff(buffEffect);
            }
        }

        private void RemoveBuffEffect(BuffData buff)
        {
            if (buff?.BuffEffect == null || _character == null)
                return;

            // 버프 이펙트 제거
            BuffEffect buffEffect = buff.BuffEffect;
            if (_buffDic.ContainsKey(buffEffect.Type) == false)
            {
                _character.OnRemoveBuff(buffEffect);
            }
        }

        private void UpdateDamageTick()
        {
            if(_buffDic.TryGetValue(GlobalEnum.BuffEffectType.Blooding, out var blooding) == true)
            {
                _character?.OnHit(null, false, GlobalEnum.DamageType.Physics, blooding.Value);
            }
        }
    }
}

