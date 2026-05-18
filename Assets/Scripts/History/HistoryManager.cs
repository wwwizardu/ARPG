using UnityEngine;

namespace ARPG.History
{
    /// <summary>
    /// 절차적 역사 시스템의 진입점. AR.s.History로 접근.
    /// Phase 0: 골격만 — Initialize/Reset 호출 시 로그만 출력.
    /// Phase 1 이후: GenerateOrLoad(worldSeed)에서 카탈로그 로드 + 백본 생성.
    /// </summary>
    public class HistoryManager : MonoBehaviour
    {
        private bool _generated = false;
        private int _worldSeed = 0;

        public bool IsGenerated => _generated;
        public int WorldSeed => _worldSeed;

        public void Initialize()
        {
            Debug.Log("HistoryManager Initialized");
        }

        public void Reset()
        {
            _generated = false;
            _worldSeed = 0;
            HistoryTagRegistry.Reset();
            Debug.Log("HistoryManager Reset");
        }

        /// <summary>
        /// 월드 시드를 기반으로 Past Backbone을 생성하거나 세이브에서 로드한다.
        /// Phase 0: 골격 — 호출은 받지만 실제 생성은 Phase 1 이후.
        /// </summary>
        public void GenerateOrLoad(int worldSeed)
        {
            _worldSeed = worldSeed;
            _generated = true;
            Debug.Log($"[HistoryManager] GenerateOrLoad(seed={worldSeed}) — Phase 0 stub");
        }

        /// <summary>
        /// 청크 활성화 시 호출. Phase 4 이후 AmbientTag → VFX 트리거, Lore 아이템 spawn 등을 처리.
        /// </summary>
        public void OnChunkActivated(Vector2Int chunkCoord)
        {
            // Phase 0: no-op
        }
    }
}
