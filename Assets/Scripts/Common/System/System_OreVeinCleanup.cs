#nullable enable
using ARPG.Scene;

namespace ARPG.Systems
{
    /// <summary>
    /// 월드 광맥 기록 정리 — 오래 비활성인 활성 기록과 재생 가능 시각이 지난 고갈 기록을 게임 하루 1회 지운다 (WORLD_ORE_VEIN_PLAN §6.3).
    /// 상태는 OreVeinManager(세계 저장 기록)가 가진다. 게임 시각만 보므로 던전 안에서도 돈다 (IWorldSimSystem, 월드 맵은 AR.s.WorldMap으로만 읽는다).
    /// 재생을 확인하는 주기 시스템이 아니다 — 굴림은 청크가 켜질 때만 한다 (§6.1).
    /// </summary>
    public class System_OreVeinCleanup : IFixedUpdateSystem, IWorldSimSystem
    {
        public int Priority => 138;
        public float UpdateInterval => 10f;

        public void OnCreate() { }

        public void OnReset() { }

        public void OnFixedUpdate(float inFixedDeltaTime)
        {
            if (AR.s.CurrentScene is GameScene == false)
                return;
            AR.s.OreVein.TickCleanup(AR.s.Time.CurrentGameTime);
        }
    }
}
