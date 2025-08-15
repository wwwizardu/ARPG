using UnityEngine;
using UnityEngine.Tilemaps;

namespace ARPG.Map
{
    public partial class MapManager : MonoBehaviour
    {
        [Header("타일 에셋")]
        [SerializeField] private TileBase[] _tileAssets;

        private void RenderChunkToTilemap(MapChunkData chunk)
        {
            if (_tileMap == null || _tileAssets == null || _tileAssets.Length < 2) return;
            
            for (int x = 0; x < chunkSize; x++)
            {
                for (int y = 0; y < chunkSize; y++)
                {
                    int worldX = chunk.chunkX * chunkSize + x;
                    int worldY = chunk.chunkY * chunkSize + y;
                    
                    uint tileData = chunk.tiles[x, y];
                    int tileType = (int)(tileData & 0xF); // 하위 4비트 읽기
                    
                    Vector3Int tilePosition = new Vector3Int(worldX, worldY, 0);
                    
                    if (tileType < _tileAssets.Length)
                    {
                        _tileMap.SetTile(tilePosition, _tileAssets[tileType]);
                    }
                }
            }
        }
        
        private void RemoveChunkFromTilemap(MapChunkData chunk)
        {
            if (_tileMap == null) return;
            
            for (int x = 0; x < chunkSize; x++)
            {
                for (int y = 0; y < chunkSize; y++)
                {
                    int worldX = chunk.chunkX * chunkSize + x;
                    int worldY = chunk.chunkY * chunkSize + y;
                    
                    Vector3Int tilePosition = new Vector3Int(worldX, worldY, 0);
                    _tileMap.SetTile(tilePosition, null); // 타일 제거
                }
            }
        }
    }
}

