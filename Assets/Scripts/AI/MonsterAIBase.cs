#nullable enable
using UnityEngine;
using ARPG.Creature;

namespace ARPG.AI
{
    public abstract class MonsterAIBase
    {
        protected Creature.Monster _monster;
        protected float _detectionRange = 5.0f;
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
            ArpgPlayer? player = FindPlayer();
            if (player == null)
            {
                return (Vector2.zero, Vector2.zero);
            }

            Vector2 directionToPlayer = player.transform.position - _monster.transform.position;
            float sqrDistanceToPlayer = directionToPlayer.sqrMagnitude;
            
            if (sqrDistanceToPlayer <= _detectionRange * _detectionRange)
            {
                Vector2 normalizedDirection = directionToPlayer.normalized;
                Vector2 velocity = normalizedDirection * _speed;
                
                return (normalizedDirection, velocity);
            }
            
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