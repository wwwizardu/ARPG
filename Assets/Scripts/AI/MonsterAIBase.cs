#nullable enable
using UnityEngine;
using ARPG.Creature;

namespace ARPG.AI
{
    public abstract class MonsterAIBase
    {
        protected Creature.Monster _monster;
        protected float _detectionRange = 5.0f;

        protected float _attackRange = 0.8f;
        protected float _speed = 3.0f;
        
        public MonsterAIBase(Creature.Monster monster)
        {
            _monster = monster;
        }

        public virtual void Initialize()
        {
            
        }

        public virtual void Reset()
        {
            
        }

        public virtual (Vector2 inputDirection, Vector2 velocity) Think()
        {
            if(_monster == null || _monster.State != CharacterConditions.Normal) // Normal 상태가 아니면 아무것도 안함
                return (Vector2.zero, Vector2.zero);

            ArpgPlayer? player = FindPlayer();
            if (player == null)
                return (Vector2.zero, Vector2.zero);

            // 플레이어와의 거리 계산
            Vector2 directionToPlayer = player.transform.position - _monster.transform.position;
            float sqrDistanceToPlayer = directionToPlayer.sqrMagnitude;

            // 공격 범위 내에 있으면 기본 공격 스킬 사용
            if (sqrDistanceToPlayer <= _attackRange * _attackRange)
            {
                _monster.StartSkill(0);
                return (Vector2.zero, Vector2.zero);
            }
            
            // 탐지 범위 내에 있으면 플레이어를 향해 이동
            if (sqrDistanceToPlayer <= _detectionRange * _detectionRange)
            {
                Vector2 normalizedDirection = directionToPlayer.normalized;
                Vector2 velocity = normalizedDirection * _speed;

                return (normalizedDirection, velocity);
            }
            
            // 탐지 범위 밖에 있으면 아무것도 안함
            return (Vector2.zero, Vector2.zero);
        }

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

                float sqrDistance = (player.transform.position - _monster.transform.position).sqrMagnitude;
                if (sqrDistance < closestSqrDistance)
                {
                    closestSqrDistance = sqrDistance;
                    closestPlayer = player;
                }
            }

            return closestPlayer;
        }
    }
}