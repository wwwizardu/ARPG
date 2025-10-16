#nullable enable
using UnityEngine;
using ARPG.Creature;
using ARPG.Tables;

namespace ARPG.AI
{
    public class BasicMonsterAI : AIBase
    {
        public BasicMonsterAI(CharacterBase inCharacter, AiTable inTable) : base(inCharacter)
        {
            _table = inTable;

            _detectionRange = 5.0f;
        }

        public override void Initialize()
        {
            base.Initialize();
        }

        public override void Reset()
        {
            base.Reset();
            _currentState = AIState.Idle;
            _targetPlayer = null;
            _moveDirection = Vector2.zero;
        }

        public override void Think()
        {
            if (_character == null || _character.State != CharacterConditions.Normal)
                return;

            switch (_currentState)
            {
                case AIState.Idle:
                    ThinkIdle();
                    break;
                case AIState.Chase:
                    ThinkChase();
                    break;
                case AIState.Attack:
                    ThinkAttack();
                    break;
            }
        }

        private void ThinkIdle()
        {
            // Idle 상태에서는 주변에 플레이어가 있는지 탐지
            _targetPlayer = FindPlayer();
            if (_targetPlayer == null)
                return;

            float sqrDistanceToPlayer = (_targetPlayer.transform.position - _character.transform.position).sqrMagnitude;

            // 탐지 범위 내에 플레이어 발견 시 Chase 상태로 전환
            if (sqrDistanceToPlayer <= _detectionRange * _detectionRange)
            {
                _currentState = AIState.Chase;
            }
        }

        private void ThinkChase()
        {
            // 타겟이 유효한지 확인
            if (_targetPlayer == null)
            {
                _currentState = AIState.Idle;
                _moveDirection = Vector2.zero;
                return;
            }

            Vector2 directionToPlayer = _targetPlayer.transform.position - _character.transform.position;
            float sqrDistanceToPlayer = directionToPlayer.sqrMagnitude;

            // 공격 범위 내면 Attack 상태로 전환
            if (sqrDistanceToPlayer <= _attackRange * _attackRange)
            {
                _currentState = AIState.Attack;
                _moveDirection = Vector2.zero;
            }
            // 탐지 범위 밖이면 Idle 상태로 전환
            else if (sqrDistanceToPlayer > _detectionRange * _detectionRange)
            {
                _currentState = AIState.Idle;
                _targetPlayer = null;
                _moveDirection = Vector2.zero;
            }
            else
            {
                // Chase 상태 유지 - 이동 방향 계산
                _moveDirection = directionToPlayer.normalized;
            }
        }

        private void ThinkAttack()
        {
            // 타겟이 유효한지 확인
            if (_targetPlayer == null)
            {
                _currentState = AIState.Idle;
                return;
            }

            float sqrDistanceToPlayer = (_targetPlayer.transform.position - _character.transform.position).sqrMagnitude;

            // 공격 범위 내에 있으면 스킬 사용
            if (sqrDistanceToPlayer <= _attackRange * _attackRange)
            {
                _character.StartSkill(1);
            }
            // 공격 범위 밖이지만 탐지 범위 내면 Chase 상태로 전환
            else if (sqrDistanceToPlayer <= _detectionRange * _detectionRange)
            {
                _currentState = AIState.Chase;
            }
            // 탐지 범위 밖이면 Idle 상태로 전환
            else
            {
                _currentState = AIState.Idle;
                _targetPlayer = null;
            }
        }

        public override (Vector2 inputDirection, Vector2 velocity) CalculateMove()
        {
            if (_character == null || _character.State != CharacterConditions.Normal)
                return (Vector2.zero, Vector2.zero);

            switch (_currentState)
            {
                case AIState.Chase:
                    Vector2 velocity = _moveDirection * _character.Stat.GetMoveSpeed();
                    return (_moveDirection, velocity);

                case AIState.Attack:
                case AIState.Idle:
                default:
                    return (Vector2.zero, Vector2.zero);
            }
        }
    }
}