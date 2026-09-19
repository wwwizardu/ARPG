using UnityEngine;

namespace ARPG.Systems
{
    public sealed class System_DungeonStreaming : IUpdateSystem
    {
        public int Priority => 120;
        public float UpdateInterval => 0f;
        public void OnCreate() { }
        public void OnReset() { }
        public void OnUpdate(float deltaTime)
        {
            if (AR.s.Dungeon.IsInside == false || AR.s.MyPlayer == null) return;
            AR.s.Map.UpdateChunksAroundPlayer(AR.s.MyPlayer.transform.position);
            AR.s.Dungeon.TickContent();
        }
    }
}
