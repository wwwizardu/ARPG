#nullable enable
using UnityEngine;
using ARPG.Creature;

namespace ARPG.AI
{
    public class BasicMonsterAI : MonsterAIBase
    {
        public BasicMonsterAI(Creature.Monster monster) : base(monster)
        {
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
            return base.Think();
        }
    }
}