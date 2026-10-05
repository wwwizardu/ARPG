namespace ARPG.Systems
{
    /// <summary>메시지와 엔티티 제거가 끝난 뒤 NPC·마을·시뮬 저장 DTO를 게시한다. 던전에서도 실행한다.</summary>
    public sealed class System_SaveSnapshotFlush : ILateUpdateSystem
    {
        public int Priority => 990;
        public float UpdateInterval => 0f;
        public void OnCreate() { }
        public void OnReset() { }

        public void OnLateUpdate(float inDeltaTime)
        {
            AR.s.Npc.FlushSaveSnapshot(recordFrameTiming: true);
            AR.s.Village.FlushSaveSnapshot(recordFrameTiming: true);
            AR.s.Npc.RefreshSimulationSaveSnapshots(ARPG.Data.SaveScope.All, recordFrameTiming: true);
            AR.s.History.FlushSaveSnapshot(recordFrameTiming: true);
        }
    }
}
