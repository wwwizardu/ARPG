namespace ARPG.Systems
{
    public sealed class System_DungeonPlacement : IUpdateSystem, IWorldMapSystem
    {
        public int Priority => 110;
        public float UpdateInterval => 0f; // Candidate, tile-query and millisecond budgets are per frame.
        public void OnCreate() { }
        public void OnReset() { }
        public void OnUpdate(float deltaTime) => AR.s.Dungeon.TickPlacement();
    }
}
