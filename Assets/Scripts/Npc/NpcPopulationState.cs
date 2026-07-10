#nullable enable
using System;

namespace ARPG.Npc
{
    /// <summary>
    /// 맵 전체 모험가 총량의 영속 상태.
    /// 마을 경제 인구와 분리되며, 사망 뒤 점진 보충의 기준으로만 사용한다.
    /// </summary>
    [Serializable]
    public class NpcPopulationState
    {
        public int TargetNpcCount = -1;
        public float NextReplenishGameTime;
        public int ReplenishVillageCursor;
    }
}
