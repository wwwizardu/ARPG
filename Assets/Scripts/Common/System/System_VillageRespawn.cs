#nullable enable
using ARPG.Scene;

namespace ARPG.Systems
{
    /// <summary>
    /// 마을 기본 NPC 재스폰 체크 시스템.
    /// 모든 마을에 대해 EnsureVillagePopulated를 주기 호출하여 쿨타임 만료 감지 및 스폰을 수행.
    /// </summary>
    public class System_VillageRespawn : IFixedUpdateSystem
    {
        public int Priority => 59;
        public float UpdateInterval => 5.0f;

        public void OnCreate() { }

        public void OnFixedUpdate(float inFixedDeltaTime)
        {
            if (AR.s.CurrentScene is GameScene == false)
                return;

            AR.s.Npc.EnsureAllVillagesPopulated();
        }

        public void OnReset() { }
    }
}
