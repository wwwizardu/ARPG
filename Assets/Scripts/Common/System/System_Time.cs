#nullable enable
using ARPG.Systems;

namespace ARPG.Systems
{
    /// <summary>
    /// 게임 시간 진행 시스템. 던전 안에서도 흐른다 (IWorldSimSystem, DUNGEON_WORLD_SIM_DESIGN §2).
    /// 던전 메뉴 일시정지(GameplayPaused) 동안은 다른 시스템과 함께 멈춘다.
    /// </summary>
    public class System_Time : IUpdateSystem, IWorldSimSystem
    {
        public int Priority => 5;
        public float UpdateInterval => 0f;

        public void OnCreate() { }

        public void OnUpdate(float inDeltaTime)
        {
            AR.s.Time.Tick(inDeltaTime);
        }

        public void OnReset() { }
    }
}
