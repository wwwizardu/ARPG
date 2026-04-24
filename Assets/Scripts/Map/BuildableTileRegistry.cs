#nullable enable
using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.Tilemaps;

namespace ARPG.Map
{
    /// <summary>
    /// BuildableItemTable 엔트리의 TileBase를 lazy 방식으로 Addressable 로드해 캐시.
    /// 앱 시작 시 프리로드 없음. Get(id) 호출 시 캐시 미스면 백그라운드 로드 자동 트리거.
    /// 로드 완료 시 TileLoaded 이벤트 → MapManager가 활성 청크 재렌더.
    /// 타일은 소형 공유 에셋이므로 캐시는 영구 유지 (청크별 ref-count 불필요).
    /// </summary>
    public static class BuildableTileRegistry
    {
        private static readonly Dictionary<int, TileBase> _cache = new();
        // 동일 Id에 대한 중복 로드 방지 - in-flight UniTask 공유
        private static readonly Dictionary<int, UniTask<TileBase?>> _inflight = new();

        /// <summary>타일 로드 완료 이벤트. MapManager가 구독해 활성 청크를 재렌더.</summary>
        public static event Action<int>? TileLoaded;

        /// <summary>1개 타일 비동기 로드. 이미 로드/로딩 중이면 기존 Task 반환.</summary>
        public static UniTask<TileBase?> EnsureLoadedAsync(int buildableId)
        {
            if (_cache.TryGetValue(buildableId, out var cached))
                return UniTask.FromResult<TileBase?>(cached);

            if (_inflight.TryGetValue(buildableId, out var pending))
                return pending;

            var task = LoadInternalAsync(buildableId);
            _inflight[buildableId] = task;
            return task;
        }

        /// <summary>
        /// 동기 조회. 캐시 hit이면 타일 반환, 미스면 백그라운드 로드를 트리거하고 null 반환.
        /// 렌더러가 호출 - 미스 시 Object 레이어가 일시적으로 공란(지면은 정상 표시).
        /// 로드 완료 시 TileLoaded 이벤트 → MapManager가 해당 위치 재렌더.
        /// </summary>
        public static TileBase? Get(int buildableId)
        {
            if (_cache.TryGetValue(buildableId, out var tile))
                return tile;

            // 캐시 미스 → 백그라운드 로드 트리거 (fire-and-forget)
            // 중복 로드는 _inflight가 방지
            EnsureLoadedAsync(buildableId).Forget();
            return null;
        }

        private static async UniTask<TileBase?> LoadInternalAsync(int buildableId)
        {
            try
            {
                var table = AR.s.Data.GetBuildableItem(buildableId);
                if (table == null)
                {
                    // 레거시 ObjectType 값(1=Stone, 2=Npc, 3=WoodWall 등) - ObjectSet이 처리, 조용히 무시
                    return null;
                }
                if (string.IsNullOrEmpty(table.ResourceName))
                {
                    Debug.LogWarning($"[BuildableTileRegistry] Id={buildableId} ResourceName 비어있음");
                    return null;
                }

                var handle = Addressables.LoadAssetAsync<TileBase>(table.ResourceName);
                TileBase tile = await handle.ToUniTask();
                if (tile != null)
                {
                    _cache[buildableId] = tile;
                    TileLoaded?.Invoke(buildableId);
                }
                return tile;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[BuildableTileRegistry] Id={buildableId} 로드 실패: {e.Message}");
                return null;
            }
            finally
            {
                _inflight.Remove(buildableId);
            }
        }

        public static void Reset()
        {
            _cache.Clear();
            _inflight.Clear();
            TileLoaded = null;
        }
    }
}
