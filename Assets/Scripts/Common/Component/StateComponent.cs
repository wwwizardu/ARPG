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

        /// <summary>
        /// 점프 중이었다면 지상 상태로 되돌린다. 착지·스킬 취소·시스템 Reset이 공유하는 규칙.
        /// </summary>
        /// <returns>실제로 상태가 바뀌었으면 true (호출자가 SetComponent 여부를 판단할 수 있게)</returns>
        public bool TryEndJumpMovement()
        {
            if (MoveState != MovementStates.Jumping)
                return false;

            MovementStatePrev = MoveState;
            MoveState = MovementStates.Idle;
            return true;
        }
    }
}


