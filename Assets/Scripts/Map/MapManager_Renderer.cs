using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;

namespace ARPG.Map
{
    [System.Serializable]
    public struct BiomeThemeEntry
    {
        public GlobalEnum.BiomeType Biome;
        public ThemeTileSet TileSet;
    }

    public partial class MapManager : MonoBehaviour
    {
        [Header("타일 에셋")]
        [SerializeField] private ThemeTileSet _themeTileSet;   // 기본/폴백 (BiomeThemeEntry 미설정 시)

        [Header("바이옴별 타일 에셋 (MAP_DESIGN.md §4.5)")]
        [SerializeField] private BiomeThemeEntry[] _themeEntries;
        private Dictionary<GlobalEnum.BiomeType, ThemeTileSet> _themesByBiome;

        [Header("땅 룰 타일")]
        [SerializeField] private RuleTile _ruleTile;

        [Header("언덕 룰 타일")]
        [SerializeField] private RuleTile _ruleTile_Hill;

        [Header("오브젝트 룰 타일")]
        [SerializeField] private RuleTile _ruleTile_Object;
        
        // 타일맵 렌더링용 재사용 변수들
        private Vector3Int _tempStartPos;
        private BoundsInt _tempArea;
        private TileBase[] _tempTileArray;
        private TileBase[] _tempHillTileArray;
        private TileBase[] _tempObjectTileArray;
        private TileBase[] _tempNullArray;

        /// <summary>
        /// 인스펙터 _themeEntries → Dictionary 변환. Initialize에서 호출.
        /// MAP_DESIGN.md §4.5.
        /// </summary>
        private void BuildThemeDictionary()
        {
            _themesByBiome = new Dictionary<GlobalEnum.BiomeType, ThemeTileSet>();
            if (_themeEntries == null)
                return;
            for (int i = 0; i < _themeEntries.Length; i++)
            {
                if (_themeEntries[i].TileSet == null)
                    continue;
                _themesByBiome[_themeEntries[i].Biome] = _themeEntries[i].TileSet;
            }
        }

        /// <summary>
        /// biome → ThemeTileSet. 매핑 없으면 폴백 _themeTileSet.
        /// </summary>
        private ThemeTileSet ResolveThemeForBiome(GlobalEnum.BiomeType biome)
        {
            if (_themesByBiome != null && _themesByBiome.TryGetValue(biome, out ThemeTileSet ts) && ts != null)
                return ts;
            return _themeTileSet;
        }

        private void RenderChunkToTilemap(MapChunkData chunk)
        {
            if (_tileMap == null || _themeTileSet == null || _themeTileSet.TileSet == null || _themeTileSet.TileSet.Length < 2) return;

            // 배열 초기화 (필요시)
            if (_tempTileArray == null || _tempTileArray.Length != chunkSize * chunkSize)
            {
                _tempTileArray = new TileBase[chunkSize * chunkSize];
            }

            if (_tempHillTileArray == null || _tempHillTileArray.Length != chunkSize * chunkSize)
            {
                _tempHillTileArray = new TileBase[chunkSize * chunkSize];
            }

            if (_tempObjectTileArray == null || _tempObjectTileArray.Length != chunkSize * chunkSize)
            {
                _tempObjectTileArray = new TileBase[chunkSize * chunkSize];
            }

            // 청크 영역 정의
            _tempStartPos.Set(chunk.chunkX * chunkSize, chunk.chunkY * chunkSize, 0);
            _tempArea = new BoundsInt(_tempStartPos.x, _tempStartPos.y, 0, chunkSize, chunkSize, 1);

            // region 단위 단일 biome — 청크 1번 조회로 ThemeTileSet 결정 (MAP_DESIGN.md §4.5 v1.8)
            ThemeTileSet theme = ResolveThemeForBiome(chunk.chunkBiome);

            for (int x = 0; x < chunkSize; x++)
            {
                for (int y = 0; y < chunkSize; y++)
                {
                    ulong tileData = chunk.tiles[x, y];
                    int baseTileType = (int)(tileData & (ulong)GlobalEnum.TileFlag.GroundLayerMask); // 하위 10비트: 바닥 타입
                    bool isHill = (tileData & (ulong)GlobalEnum.TileFlag.Hill) != 0; // 20번째 비트: 언덕 여부
                    ulong objectId = (tileData & (ulong)GlobalEnum.TileFlag.ObjectLayerMask) >> 10;

                    int index = y * chunkSize + x; // 2D to 1D index

                    // 바닥 타일 설정
                    try
                    {
                        if (baseTileType == (int)GlobalEnum.TileType.Ground)
                        {
                            _tempTileArray[index] = _ruleTile;
                        }
                        else
                        {
                            _tempTileArray[index] = (theme != null && theme.TileSet != null && theme.TileSet.Length > baseTileType) ? theme.TileSet[baseTileType] : null;
                        }
                    }
                    catch
                    {
                        Debug.LogError($"[MapManager] RenderChunkToTilemap - exception baseTileType({baseTileType}), Position({x},{y})");
                        _tempTileArray[index] = null;
                    }

                    // 언덕 타일 설정
                    if (isHill)
                    {
                        _tempHillTileArray[index] = _ruleTile_Hill;
                    }
                    else
                    {
                        _tempHillTileArray[index] = null;
                    }

                    // 오브젝트 타일 설정
                    // 1) BuildableTileRegistry (BuildableItemTable.Id → Addressable 로드) 우선 조회
                    // 2) 미스 시 레거시 ThemeTileSet.ObjectSet fallback — Phase A 이전 자산만 해당.
                    //    Docs/THEME_TILESET_REFACTOR.md 단계 2~3 완료 시 fallback 분기 제거 예정.
                    if (objectId > 0)
                    {
                        TileBase tile = BuildableTileRegistry.Get((int)objectId);
#pragma warning disable 0618    // ObjectSet은 [Obsolete] 처리됨 — 의도적 fallback 사용
                        if (tile == null
                            && _themeTileSet.ObjectSet != null
                            && objectId < (ulong)_themeTileSet.ObjectSet.Length)
                        {
                            tile = _themeTileSet.ObjectSet[objectId];
                        }
#pragma warning restore 0618
                        _tempObjectTileArray[index] = tile;
                    }
                    else
                    {
                        _tempObjectTileArray[index] = null;
                    }
                }
            }
            
            // 한 번에 모든 타일 설정
            _tileMap.SetTilesBlock(_tempArea, _tempTileArray);
            if (_tileMap_Hill != null)
            {
                _tileMap_Hill.SetTilesBlock(_tempArea, _tempHillTileArray);
            }
            if (_tileMap_Object != null)
            {
                _tileMap_Object.SetTilesBlock(_tempArea, _tempObjectTileArray);
            }
        }
        
        private void RemoveChunkFromTilemap(MapChunkData chunk)
        {
            if (_tileMap == null) return;
            
            // null 배열 초기화 (필요시)
            if (_tempNullArray == null || _tempNullArray.Length != chunkSize * chunkSize)
            {
                _tempNullArray = new TileBase[chunkSize * chunkSize];
                // 배열은 이미 모든 요소가 null로 초기화됨
            }
            
            // 청크 영역 정의
            _tempStartPos.Set(chunk.chunkX * chunkSize, chunk.chunkY * chunkSize, 0);
            _tempArea = new BoundsInt(_tempStartPos.x, _tempStartPos.y, 0, chunkSize, chunkSize, 1);
            
            // 기본 타일맵과 Hill 타일맵, Object 타일맵에서 모든 타일 제거
            _tileMap.SetTilesBlock(_tempArea, _tempNullArray);
            if (_tileMap_Hill != null)
            {
                _tileMap_Hill.SetTilesBlock(_tempArea, _tempNullArray);
            }
            if (_tileMap_Object != null)
            {
                _tileMap_Object.SetTilesBlock(_tempArea, _tempNullArray);
            }
        }
    }
}

