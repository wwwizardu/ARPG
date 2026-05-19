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
        // v1.9 cluster-grained — 청크당 cluster ThemeTileSet 캐시 (재사용으로 GC 0).
        private ThemeTileSet[] _clusterThemes;

        // 누락 타일 로그 dedupe — 같은 biome/케이스가 매 청크마다 스팸 로그 남기는 것 방지.
        // MapManager.Reset()에서 ClearMissingTileLog() 호출하여 초기화.
        private readonly HashSet<GlobalEnum.BiomeType> _loggedMissingTheme = new HashSet<GlobalEnum.BiomeType>();
        private readonly HashSet<GlobalEnum.BiomeType> _loggedMissingGround = new HashSet<GlobalEnum.BiomeType>();
        private readonly HashSet<Vector2Int> _loggedNoneCellChunks = new HashSet<Vector2Int>();
        private bool _loggedMissingRuleTile = false;

        public void ClearMissingTileLog()
        {
            _loggedMissingTheme.Clear();
            _loggedMissingGround.Clear();
            _loggedNoneCellChunks.Clear();
            _loggedMissingRuleTile = false;
        }

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
            if (_tileMap == null) return;

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

            // v1.9 cluster-grained — 청크 내 cluster마다 ThemeTileSet 선택 (MapChunkData.clusterBiomes).
            //   cluster당 ResolveThemeForBiome 1회 캐시 → 셀 루프에선 cluster 인덱스로 즉시 룩업.
            int clustersPerSide = chunkSize / BiomeNoise.CLUSTER_SIZE;
            int clusterCount = clustersPerSide * clustersPerSide;
            if (_clusterThemes == null || _clusterThemes.Length != clusterCount)
                _clusterThemes = new ThemeTileSet[clusterCount];

            for (int i = 0; i < clusterCount; i++)
            {
                GlobalEnum.BiomeType cb = chunk.clusterBiomes[i];
                ThemeTileSet t = ResolveThemeForBiome(cb);
                _clusterThemes[i] = t;

                // 누락 타일 감지 — biome별 1회만 로그 (dedupe)
                if (t == null)
                {
                    if (_loggedMissingTheme.Add(cb))
                        Debug.LogError($"[MapManager] ThemeTileSet 미설정 — biome={cb}, chunk=({chunk.chunkX},{chunk.chunkY}). 인스펙터의 Theme Entries에 해당 biome 매핑 또는 폴백 Theme Tile Set 확인 필요.");
                }
                else if (t.Ground == null)
                {
                    if (_loggedMissingGround.Add(cb))
                        Debug.LogError($"[MapManager] ThemeTileSet.Ground 미설정 — biome={cb}, themeName='{t.themeName}', chunk=({chunk.chunkX},{chunk.chunkY}). 해당 ThemeTileSet SO의 Ground 필드에 바닥 타일 할당 필요.");
                }
            }

            for (int x = 0; x < chunkSize; x++)
            {
                for (int y = 0; y < chunkSize; y++)
                {
                    ulong tileData = chunk.tiles[x, y];
                    int baseTileType = (int)(tileData & (ulong)GlobalEnum.TileFlag.GroundLayerMask); // 하위 10비트: 바닥 타입
                    bool isHill = (tileData & (ulong)GlobalEnum.TileFlag.Hill) != 0; // 20번째 비트: 언덕 여부
                    ulong objectId = (tileData & (ulong)GlobalEnum.TileFlag.ObjectLayerMask) >> 10;

                    int index = y * chunkSize + x; // 2D to 1D index

                    // cluster theme 룩업 (셀이 속한 cluster의 캐시된 ThemeTileSet)
                    int clusterIdx = (y / BiomeNoise.CLUSTER_SIZE) * clustersPerSide + (x / BiomeNoise.CLUSTER_SIZE);
                    ThemeTileSet theme = _clusterThemes[clusterIdx];

                    // 바닥 타일 설정 (MAP_DESIGN.md §4.5 Option A)
                    //   - Ground (맨땅, MapFileData 청사진용): 바이옴 무관 단일 _ruleTile
                    //   - None: null (빈 칸)
                    //   - 그 외 절차 생성 바닥(Glass/Sand/Snow 등): cluster biome의 ThemeTileSet.Ground
                    if (baseTileType == (int)GlobalEnum.TileType.Ground)
                    {
                        if (_ruleTile == null && _loggedMissingRuleTile == false)
                        {
                            _loggedMissingRuleTile = true;
                            Debug.LogError($"[MapManager] _ruleTile 미설정 — MapFileData 청사진의 Ground(맨땅) 타일이 렌더되지 않음. chunk=({chunk.chunkX},{chunk.chunkY}). 인스펙터의 '땅 룰 타일' 필드에 RuleTile 할당 필요.");
                        }
                        _tempTileArray[index] = _ruleTile;
                    }
                    else if (baseTileType == (int)GlobalEnum.TileType.None)
                    {
                        // None 셀 — 청크당 1회만 로그 (위치는 첫 발견 셀 기준).
                        // 주 원인: MapFileData overlay가 objectId/Hill만 깔고 바닥 비트를 0으로 만든 청사진 셀.
                        Vector2Int chunkKey = new Vector2Int(chunk.chunkX, chunk.chunkY);
                        if (_loggedNoneCellChunks.Add(chunkKey))
                        {
                            int worldX = chunk.chunkX * chunkSize + x;
                            int worldY = chunk.chunkY * chunkSize + y;
                            Debug.LogError($"[MapManager] TileType.None 셀 — chunk=({chunk.chunkX},{chunk.chunkY}), local=({x},{y}), world=({worldX},{worldY}), tileData=0x{tileData:X}. MapFileData 청사진이 바닥 없이 오브젝트/Hill만 깐 가능성 (overlay 시 절차 생성 바닥이 0으로 덮어쓰여짐).");
                        }
                        _tempTileArray[index] = null;
                    }
                    else
                    {
                        _tempTileArray[index] = theme != null ? theme.Ground : null;
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
                            && _themeTileSet != null
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

