#nullable enable
using System;
using System.Collections.Generic;

namespace ARPG.Npc
{
    /// <summary>
    /// Hunt를 함께 수행하는 NPC 원정대의 영속 정본이다.
    /// 멤버별 Activity에는 PartyId만 두고, 공통 시간축과 이동 경로는 이 객체만 소유한다.
    /// </summary>
    [Serializable]
    public class NpcPartyState
    {
        public int PartyId;
        public int LeaderId;
        public List<int> MemberIds = new();
        public NpcActivityState Activity = new();

        public void EnsureInitialized()
        {
            if (MemberIds == null)
                MemberIds = new List<int>();
            if (Activity == null)
                Activity = new NpcActivityState();

            Activity.PartyId = PartyId;
            Activity.EnsureInitialized();
        }
    }
}
