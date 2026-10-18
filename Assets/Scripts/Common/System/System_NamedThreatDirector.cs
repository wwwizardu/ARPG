#nullable enable
using ARPG.Scene;

namespace ARPG.Systems
{
    /// <summary>
    /// 네임드 위협 감독 — 플레이어와 가장 가까운 마을과 게임 시각으로 출현, 각 네임드의 단계·사라짐·소문 주인 재배정 (§4·§7·§8.1). 처치 묶음 저장 재시도도 여기서 한다.
    /// 상태는 NamedThreatManager(세계 저장 기록)가 가진다. 던전 안에서는 월드 시스템과 함께 멈춘다 (IWorldMapSystem).
    /// NAMED_THREAT_DESIGN §12.2.
    /// </summary>
    public class System_NamedThreatDirector : IFixedUpdateSystem, IWorldMapSystem
    {
        public int Priority => 135;
        public float UpdateInterval => 1f;

        public void OnCreate() { }

        public void OnReset() { }

        public void OnFixedUpdate(float inFixedDeltaTime)
        {
            if (AR.s.CurrentScene is GameScene == false || AR.s.NamedThreat == null)
                return;
            AR.s.NamedThreat.TickDirector(AR.s.Time.CurrentGameTime);
        }
    }
}
