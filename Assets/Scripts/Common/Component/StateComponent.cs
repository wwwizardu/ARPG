using UnityEngine;
using ARPG.Creature;

namespace ARPG.Component
{
    public struct StateComponent
    {
        public CharacterConditions Condition;
        public CharacterConditions ConditionPrev;
        public MovementStates MoveState;
        public MovementStates MovementStatePrev;
    }
}


