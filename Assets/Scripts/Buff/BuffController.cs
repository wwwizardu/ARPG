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
        private readonly List<BuffData> _buffList = new();
        private readonly Dictionary<int, BuffData> _buffDic = new();

        private float _tickTimer = 0f;
        private const float TICK_INTERVAL = 0.1f; // 0.1초마다 틱 처리

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
                        // TODO: 틱 데미지 처리 로직
                    }

                    // 버프 시간이 만료되면 제거
                    if (buff.LeftTime <= 0)
                    {
                        RemoveBuff(buff.Id);
                    }
                }
            }
        }

        public bool AddBuff(int inBuffId)
        {
            BuffTable? buffTable = AR.s.Data != null ? AR.s.Data.GetBuff(inBuffId) : null;
            if (buffTable == null)
            {
                Debug.LogError($"[BuffController] AddBuff - BuffTable not found, BuffId({inBuffId})");
                return false;
            }

            // 이미 같은 버프가 있는지 확인
            if (_buffDic.ContainsKey(inBuffId))
            {
                BuffData existingBuff = _buffDic[inBuffId];

                // 스택 가능한 버프인 경우
                if (buffTable.MaxStack > 1)
                {
                    // TODO: 스택 처리 로직
                    // 현재는 시간만 리셋
                    existingBuff.LeftTime = buffTable.Duration;
                    return true;
                }
                else
                {
                    // 시간 리셋
                    existingBuff.LeftTime = buffTable.Duration;
                    return true;
                }
            }

            // 새 버프 생성
            BuffData newBuff = new()
            {
                Id = inBuffId,
                LeftTime = buffTable.Duration,
                Damage = 0, // BuffEffectTable에서 계산될 예정
                Table = buffTable
            };

            _buffList.Add(newBuff);
            _buffDic.Add(inBuffId, newBuff);

            // 버프 효과 적용
            ApplyBuffEffect(newBuff);

            Debug.Log($"[BuffController] AddBuff - BuffId({inBuffId}), Name({buffTable.Name})");
            return true;
        }

        public bool RemoveBuff(int inBuffId)
        {
            if (!_buffDic.ContainsKey(inBuffId))
            {
                Debug.LogWarning($"[BuffController] RemoveBuff - Buff not found, BuffId({inBuffId})");
                return false;
            }

            BuffData buff = _buffDic[inBuffId];

            // 버프 효과 제거
            RemoveBuffEffect(buff);

            _buffList.Remove(buff);
            _buffDic.Remove(inBuffId);

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

        public bool HasBuff(int inBuffId)
        {
            return _buffDic.ContainsKey(inBuffId);
        }

        public BuffData? GetBuff(int inBuffId)
        {
            if (_buffDic.ContainsKey(inBuffId))
                return _buffDic[inBuffId];

            return null;
        }

        public List<BuffData> GetAllBuffs()
        {
            return _buffList;
        }

        private void ApplyBuffEffect(BuffData buff)
        {
            if (buff.Table == null || _character == null)
                return;

            // TODO: 스탯 보너스 적용
            // if (buff.Table.StatList != null)
            // {
            //     foreach (var stat in buff.Table.StatList)
            //     {
            //         _character.Stat.AddModifier(stat.Type, stat.Value);
            //     }
            // }
        }

        private void RemoveBuffEffect(BuffData buff)
        {
            if (buff.Table == null || _character == null)
                return;

            // TODO: 스탯 보너스 제거
            // if (buff.Table.StatList != null)
            // {
            //     foreach (var stat in buff.Table.StatList)
            //     {
            //         _character.Stat.RemoveModifier(stat.Type, stat.Value);
            //     }
            // }
        }
    }
}


