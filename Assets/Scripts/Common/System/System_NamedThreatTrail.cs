#nullable enable
using ARPG.Scene;

namespace ARPG.Systems
{
    /// <summary>
    /// 네임드 위협 흔적 자막 — 플레이어가 영역 안에 있으면 배회 지점 방향을 가운데 한 줄로 알린다 (§8.5).
    /// 상태는 NamedThreatManager(세계 저장 기록)가 가진다. 던전 안에서는 월드 시스템과 함께 멈춘다 (IWorldMapSystem).
    /// NAMED_THREAT_DESIGN §12.2.
    /// </summary>
    public class System_NamedThreatTrail : IFixedUpdateSystem, IWorldMapSystem
    {
        public int Priority => 137;
        public float UpdateInterval => 0.5f;

        public void OnCreate() { }

        public void OnReset() { }

        public void OnFixedUpdate(float inFixedDeltaTime)
        {
            if (AR.s.CurrentScene is GameScene == false || AR.s.NamedThreat == null)
                return;
            AR.s.NamedThreat.TickTrail(AR.s.Time.CurrentGameTime);
        }
    }
}
