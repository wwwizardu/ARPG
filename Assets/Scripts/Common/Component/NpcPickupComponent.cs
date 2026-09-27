using UnityEngine;

namespace ARPG.Component
{
    /// <summary>
    /// NPC 드랍 줍기 진행 컴포넌트 (NPC_COMBAT_DESIGN §6, B5). 드랍 위치로 이동 → 줍기의 짧은 시퀀스 동안만 붙는다.
    /// 이 컴포넌트가 있는 동안 System_AI_Behavior는 해당 NPC의 AI 상태 핸들러를 돌리지 않고, System_NpcPickup이 이동을 소유한다.
    /// 저장하지 않는다 — 비실체화·전환·저장을 넘어 이어지지 않는다(엔티티 제거 시 함께 사라짐).
    /// </summary>
    public struct NpcPickupComponent
    {
        public int TargetItemInstanceId;    // 주울 월드 드랍의 ItemInstanceId (ItemManager 컨텍스트 드랍 키)
        public Vector2 TargetPosition;      // 드랍 위치 (WorldDropItemData 좌표)
        public float ElapsedSeconds;        // 시작 후 경과 시간 (제한 시간 초과 시 취소)
        public float PathFailedSeconds;     // 길찾기 연속 실패 시간 (일정 시간 넘으면 취소)
    }
}
