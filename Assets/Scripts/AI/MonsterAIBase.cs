#nullable enable
using UnityEngine;
using ARPG.Creature;

namespace ARPG.AI
{
    public abstract class AIBase
    {
        protected CharacterBase _character;
        protected Tables.AiTable _table = null!;

        protected float _detectionRange = 5.0f;

        protected float _attackRange = 0.8f;
        protected float _speed = 3.0f;
        
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

        public abstract (Vector2 inputDirection, Vector2 velocity) Think();

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