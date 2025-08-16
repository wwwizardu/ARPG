using UnityEngine;
using UnityEngine.Tilemaps;

namespace ARPG.Map
{
    public partial class MapManager : MonoBehaviour
    {
        [Header("타일 에셋")]
        [SerializeField] private TileBase[] _tileAssets;
        [SerializeField] private RuleTile _ruleTile;
        
        // 타일맵 렌더링용 재사용 변수들
        private Vector3Int _tempStartPos;
        private BoundsInt _tempArea;
        private TileBase[] _tempTileArray;
        private TileBase[] _tempNullArray;

        private void RenderChunkToTilemap(MapChunkData chunk)
        {
            if (_tileMap == null || _tileAssets == null || _tileAssets.Length < 2) return;
            
            // 배열 초기화 (필요시)
            if (_tempTileArray == null || _tempTileArray.Length != chunkSize * chunkSize)
            {
                _tempTileArray = new TileBase[chunkSize * chunkSize];
            }
            
            // 청크 영역 정의
            _tempStartPos.Set(chunk.chunkX * chunkSize, chunk.chunkY * chunkSize, 0);
            _tempArea = new BoundsInt(_tempStartPos.x, _tempStartPos.y, 0, chunkSize, chunkSize, 1);
            
            for (int x = 0; x < chunkSize; x++)
            {
                for (int y = 0; y < chunkSize; y++)
                {
                    uint tileData = chunk.tiles[x, y];
                    int tileType = (int)(tileData & 0xF); // 하위 4비트 읽기
                    
                    int index = y * chunkSize + x; // 2D to 1D index
                    
                    if (tileType < _tileAssets.Length)
                    {
                        if (tileType == (int)GlobalEnum.TileType.Ground) // 룰 타일인 경우
                        {
                            _tempTileArray[index] = _ruleTile;
                        }
                        else
                        {
                            _tempTileArray[index] = _tileAssets[tileType];
                        }
                    }
                    else
                    {
                        _tempTileArray[index] = null;
                    }
                }
            }
            
            // 한 번에 모든 타일 설정
            _tileMap.SetTilesBlock(_tempArea, _tempTileArray);
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
            
            // 한 번에 모든 타일 제거
            _tileMap.SetTilesBlock(_tempArea, _tempNullArray);
        }
    }
}

