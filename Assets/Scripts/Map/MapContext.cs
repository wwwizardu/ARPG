using ARPG.Data;

namespace ARPG.Map
{
    public enum MapKind { World, Dungeon }

    /// <summary>Identity/lifetime for map caches and async work. Every MapManager owns one.</summary>
    public sealed class MapContext
    {
        public MapKind Kind { get; }
        public DungeonRecord Dungeon { get; }
        public bool IsActive { get; internal set; } = true;
        public int Epoch { get; internal set; }
        public int PendingLoads { get; private set; }
        public LoadLease BeginLoad() => new LoadLease(this);

        public MapContext(MapKind kind = MapKind.World, DungeonRecord dungeon = null)
        {
            Kind = kind;
            Dungeon = dungeon;
        }

        public sealed class LoadLease : System.IDisposable
        {
            private readonly MapContext _context;
            private readonly int _epoch;
            private bool _disposed;
            public bool IsCurrent => _context.IsActive && _context.Epoch == _epoch;
            internal LoadLease(MapContext context)
            {
                _context = context;
                _epoch = context.Epoch;
                _context.PendingLoads++;
            }
            public void Dispose()
            {
                if (_disposed == true) return;
                _disposed = true;
#if UNITY_EDITOR
                UnityEngine.Debug.Assert(_context.PendingLoads > 0,
                    "[MapContext] LoadLease released with no pending load.");
#endif
                _context.PendingLoads--;
            }
        }
    }
}
