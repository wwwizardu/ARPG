#nullable enable
using ARPG.Systems;

namespace ARPG.Systems
{
    /// <summary>
    /// 게임 시간 진행 시스템
    /// </summary>
    public class System_Time : IUpdateSystem
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
