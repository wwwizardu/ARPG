namespace ARPG.Systems
{
    /// <summary>
    /// Game-time abstract world simulation. Keeps running in an interior context and reads the world map only
    /// through AR.s.WorldMap (DUNGEON_WORLD_SIM_DESIGN §2·§4).
    /// </summary>
    public interface IWorldSimSystem : ISystem { }
}
