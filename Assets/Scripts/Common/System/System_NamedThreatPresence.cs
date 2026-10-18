#nullable enable
using ARPG.Scene;

namespace ARPG.Systems
{
    /// <summary>
    /// 네임드 위협 존재 — 청크 활성에 따라 네임드 엔티티를 만들고 지우며, 순찰 기준점을 배회 지점으로 옮긴다 (§5.3·§5.4).
    /// 상태는 NamedThreatManager(세계 저장 기록)가 가진다. 던전 안에서는 월드 시스템과 함께 멈춘다 (IWorldMapSystem).
    /// NAMED_THREAT_DESIGN §12.2.
    /// </summary>
    public class System_NamedThreatPresence : IFixedUpdateSystem, IWorldMapSystem
    {
        public int Priority => 136;
        public float UpdateInterval => 0.5f;

        public void OnCreate() { }

        public void OnReset() { }

        public void OnFixedUpdate(float inFixedDeltaTime)
        {
            if (AR.s.CurrentScene is GameScene == false || AR.s.NamedThreat == null)
                return;
            AR.s.NamedThreat.TickPresence(AR.s.Time.CurrentGameTime);
        }
    }
}
