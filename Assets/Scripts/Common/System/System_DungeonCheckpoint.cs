namespace ARPG.Systems
{
    public sealed class System_DungeonCheckpoint : IUpdateSystem
    {
        public int Priority => 260;
        public float UpdateInterval => 0f; // Unscaled deadline, including failed death/completion retries.
        public void OnCreate() { }
        public void OnReset() { }
        public void OnUpdate(float deltaTime) => AR.s.Dungeon.TickCheckpoint();
    }
}
