using UnityEngine;

namespace ARPG.Component
{
    // 입력 데이터만 저장하는 컴포넌트
    public struct InputComponent
    {
        public Vector2 MoveDirection;    // 이동 방향 (WASD)
        public Vector2 MousePosition;    // 시선 방향 (마우스)
        public bool IsAttacking;         // 공격 입력
        public bool IsInteracting;       // 상호작용 입력
        public bool IsSprinting;         // 달리기 입력
    }
}
