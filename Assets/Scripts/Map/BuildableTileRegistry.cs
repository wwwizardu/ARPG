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
    /// 캐시는 영구 유지 (타일은 소형 공유 에셋). 실패도 캐시(null)되어 재시도 안 함.
    ///
    /// 중복 로드 방지:
    ///  - 결과는 _cache(Dictionary)에 보관 — TryGetValue로 즉시 hit 판정
    ///  - 진행 중은 _loading(HashSet)으로 게이트 — Add() 반환값으로 "내가 첫 호출자" 판정
    ///  - 다중 awaiter는 각자 WaitUntil로 캐시 채워질 때까지 대기 (Preserve 불필요)
    /// </summary>
    public static class BuildableTileRegistry
    {
        // 결과 영구 캐시. null 값도 의미 있음 (실패/미등록 — 재시도 안 함).
        private static readonly Dictionary<int, TileBase?> _cache = new();
        // 진행 중 게이트. Add() 반환값으로 첫 호출자 판정.
        private static readonly HashSet<int> _loading = new();

        /// <summary>타일 로드 완료 이벤트. MapManager가 구독해 활성 청크를 재렌더.</summary>
        public static event Action<int>? TileLoaded;

        /// <summary>1개 타일 비동기 로드. 진행 중이면 캐시 채워질 때까지 대기.</summary>
        public static async UniTask<TileBase?> EnsureLoadedAsync(int buildableId)
        {
            if (_cache.TryGetValue(buildableId, out var tile))
                return tile;

            // 첫 호출자면 실제 로드, 아니면 캐시 채워질 때까지 대기 (다른 awaiter가 진행 중)
            if (_loading.Add(buildableId))
                return await LoadInternalAsync(buildableId);

            await UniTask.WaitUntil(() => _cache.ContainsKey(buildableId));
            return _cache[buildableId];
        }

        /// <summary>
        /// 동기 조회. 캐시 hit이면 결과 반환, 미스면 백그라운드 로드 트리거 후 null 반환.
        /// 렌더러가 호출 — 미스 시 Object 레이어가 일시적으로 공란(지면은 정상 표시).
        /// 로드 완료 시 TileLoaded 이벤트 → MapManager가 해당 위치 재렌더.
        /// </summary>
        public static TileBase? Get(int buildableId)
        {
            if (_cache.TryGetValue(buildableId, out var tile))
                return tile;

            // 첫 호출자만 fire-and-forget으로 로드 트리거. dedup은 _loading이 처리.
            if (_loading.Add(buildableId))
                LoadInternalAsync(buildableId).Forget();

            return null;
        }

        private static async UniTask<TileBase?> LoadInternalAsync(int buildableId)
        {
            TileBase? result = null;
            try
            {
                var table = AR.s.Data.GetBuildableItem(buildableId);
                if (table == null || string.IsNullOrEmpty(table.ResourceName))
                {
                    if (table != null)
                        Debug.LogWarning($"[BuildableTileRegistry] Id={buildableId} ResourceName 비어있음");
                    return null;
                }

                // 키 존재 여부를 먼저 확인 (없는 키로 LoadAssetAsync 호출 시 Addressables가 콘솔에 InvalidKeyException 출력)
                var locHandle = Addressables.LoadResourceLocationsAsync(table.ResourceName, typeof(TileBase));
                var locations = await locHandle.ToUniTask();
                bool exists = locations != null && locations.Count > 0;
                Addressables.Release(locHandle);
                if (exists == false)
                {
                    Debug.LogWarning($"[BuildableTileRegistry] Id={buildableId} Addressable 키 '{table.ResourceName}' 미등록 — 렌더 스킵");
                    return null;
                }

                result = await Addressables.LoadAssetAsync<TileBase>(table.ResourceName).ToUniTask();
                if (result != null)
                    TileLoaded?.Invoke(buildableId);
                return result;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[BuildableTileRegistry] Id={buildableId} 로드 실패: {e.Message}");
                return null;
            }
            finally
            {
                // 결과(null 포함)를 캐시에 박는 것은 항상 마지막에 — WaitUntil이 ContainsKey로 감지
                _cache[buildableId] = result;
                _loading.Remove(buildableId);
            }
        }

        public static void Reset()
        {
            _cache.Clear();
            _loading.Clear();
            TileLoaded = null;
        }
    }
}
