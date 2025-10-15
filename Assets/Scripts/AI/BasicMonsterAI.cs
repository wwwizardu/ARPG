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
            _speed = 3.0f;
        }

        public override void Initialize()
        {
            base.Initialize();
        }

        public override void Reset()
        {
            base.Reset();
        }

        public override (Vector2 inputDirection, Vector2 velocity) Think()
        {
            if(_character == null || _character.State != CharacterConditions.Normal) // Normal 상태가 아니면 아무것도 안함
                return (Vector2.zero, Vector2.zero);

            ArpgPlayer? player = FindPlayer();
            if (player == null)
                return (Vector2.zero, Vector2.zero);

            // 플레이어와의 거리 계산
            Vector2 directionToPlayer = player.transform.position - _character.transform.position;
            float sqrDistanceToPlayer = directionToPlayer.sqrMagnitude;

            // 공격 범위 내에 있으면 기본 공격 스킬 사용
            if (sqrDistanceToPlayer <= _attackRange * _attackRange)
            {
                _character.StartSkill(1);
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
    }
}