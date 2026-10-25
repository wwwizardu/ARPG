using UnityEngine;

namespace ARPG.Component
{
    // 입력 데이터만 저장하는 컴포넌트
    public struct InputComponent
    {
        public Vector2 MoveDirection;    // 이동 방향 (WASD)
        public Vector2 MousePosition;    // 시선 방향 (마우스)
        public Vector2 MouseWorldPosition; // UI가 입력을 소유하지 않은 마지막 마우스 월드 위치
        public bool IsGamepad;
        public Vector2 AimDirection;
        public int QuickItemPressedMask;
        public bool RequireSkillRelease;
        public int ControllerTargetId;
        public bool IsAttacking;         // 공격 입력
        public bool IsInteracting;       // 상호작용 입력
        public bool IsSprinting;         // 달리기 입력
        public int SkillSlotHeldMask;    // 슬롯별 입력 유지 비트마스크 (bit i = 슬롯 i 키 눌림). 채널링/차징 스킬에서 SlotIndex로 조회
        public bool AttackSuppressedUntilRelease; // 좌클릭으로 드랍 아이템을 클릭한 경우, 버튼을 뗄 때까지 좌클릭 공격 차단
    }
}
