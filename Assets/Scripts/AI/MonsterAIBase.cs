#nullable enable
using UnityEngine;
using ARPG.Creature;

namespace ARPG.AI
{
    public abstract class AIBase
    {
        public enum AIState
        {
            Idle,
            Chase,
            Attack
        }

        protected CharacterBase _character;
        protected Tables.AiTable _table = null!;

        protected AIState _currentState = AIState.Idle;
        protected ArpgPlayer? _targetPlayer = null;
        protected Vector2 _moveDirection = Vector2.zero;

        protected float _detectionRange = 5.0f;

        protected float _attackRange = 0.8f;
        
        public AIBase(Creature.CharacterBase monster)
        {
            _character = monster;
        }

        public virtual void Initialize()
        {
            if (_table == null)
                return;

            // 스킬 생성
            CreateSkill();
        }

        public virtual void Reset()
        {

        }
        
        public abstract void Think();

        public abstract (Vector2 inputDirection, Vector2 velocity) CalculateMove();

        protected ArpgPlayer? FindPlayer()
        {
            if (AR.s == null)
                return null;

            var allPlayers = AR.s.Player.GetAllPlayers();
            if (allPlayers.Count == 0)
                return null;

            ArpgPlayer? closestPlayer = null;
            float closestSqrDistance = float.MaxValue;

            foreach (var player in allPlayers)
            {
                if (player == null)
                    continue;

                float sqrDistance = (player.transform.position - _character.transform.position).sqrMagnitude;
                if (sqrDistance < closestSqrDistance)
                {
                    closestSqrDistance = sqrDistance;
                    closestPlayer = player;
                }
            }

            return closestPlayer;
        }

        protected void ThinkChase()
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

        private void CreateSkill()
        {
            if (0 < _table.SkillId1)
            {
                _character.CharacterInfo.SkillController.CreateSkill(_table.SkillId1);
            }
            if (0 < _table.SkillId2)
            {
                _character.CharacterInfo.SkillController.CreateSkill(_table.SkillId2);
            }
            if(0 < _table.SkillId3)
            {
                _character.CharacterInfo.SkillController.CreateSkill(_table.SkillId3);    
            }
        }
    }
}