#nullable enable
using System;

namespace ARPG.Npc
{
    /// <summary>
    /// 임시 동행(던전 전용) 예약·진행 상태. World 파티션에 저장한다 (NPC_COMBAT_DESIGN §4.5).
    /// NpcSaveData에는 필드를 추가하지 않는다(§9). 한 번에 1명만 예약할 수 있다.
    /// </summary>
    [Serializable]
    public class NpcCompanionState
    {
        // 예약된 NPC EntityId. -1이면 예약 없음. 만료되지 않고 대화로만 취소한다 [D17].
        public int ReservedNpcId = -1;
        // 예약 NPC가 현재 던전 안에서 동행 중인지. 입장 커밋에서 true, 퇴장 커밋에서 false로 저장된다.
        public bool IsInDungeon;
    }
}
