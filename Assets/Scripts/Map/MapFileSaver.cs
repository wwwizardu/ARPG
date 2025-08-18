using UnityEngine;
using System.IO;
using UnityEngine.Tilemaps;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace ARPG.Map
{
    public class MapFileSaver : MonoBehaviour
    {
        [Header("Tilemap")]
        [SerializeField] private Tilemap _tilemap;

        [Header("파일 저장 설정")]
        [SerializeField] private string _fileName = "MapData";
        [SerializeField] private string _folderName = "SavedMaps";
        
        private string SaveFolderPath => Path.Combine(Application.persistentDataPath, _folderName);
        
        private void Awake()
        {
            // 저장 폴더가 없으면 생성
            if (!Directory.Exists(SaveFolderPath))
            {
                Directory.CreateDirectory(SaveFolderPath);
            }
        }
        
        /// <summary>
        /// 현재 타일맵의 전체 데이터를 파일로 저장합니다.
        /// 좌표계: 왼쪽 아래가 (0,0), 오른쪽 위가 (width, height)
        /// </summary>
        /// <param name="fileName">저장할 파일명 (확장자 제외)</param>
        /// <returns>저장 성공 여부</returns>
        public bool SaveMapData(string fileName = null)
        {
            if (_tilemap == null)
            {
                Debug.LogError("Tilemap is not assigned");
                return false;
            }
            
            // 타일맵의 실제 사용 영역 계산
            BoundsInt bounds = _tilemap.cellBounds;
            if (bounds.size.x == 0 || bounds.size.y == 0)
            {
                Debug.LogWarning("Tilemap is empty or has no bounds");
                return false;
            }
            
            string saveFileName = fileName ?? _fileName;
            Vector2Int startPosition = new Vector2Int(bounds.xMin, bounds.yMin);
            MapFileData mapFileData = new MapFileData(bounds.size.x, bounds.size.y, startPosition);
            
            // 타일맵의 전체 영역의 타일 데이터를 수집
            // 좌표계 변환: 왼쪽 아래가 (0,0)이 되도록 Y축을 뒤집어서 저장
            for (int x = 0; x < bounds.size.x; x++)
            {
                for (int y = 0; y < bounds.size.y; y++)
                {
                    int tilemapX = bounds.xMin + x;
                    int tilemapY = bounds.yMin + y;
                    
                    // Unity Tilemap에서 타일을 가져옴
                    Vector3Int tilemapPosition = new Vector3Int(tilemapX, tilemapY, 0);
                    TileBase tile = _tilemap.GetTile(tilemapPosition);
                    
                    // TileBase를 uint로 변환 (간단한 해시 또는 인덱스 기반)
                    uint tileData = ConvertTileToUint(tile);
                    
                    // Y좌표를 뒤집어서 저장 (왼쪽 아래가 (0,0)이 되도록)
                    int flippedY = bounds.size.y - 1 - y;
                    mapFileData.SetTile(x, flippedY, tileData);
                }
            }
            
            // 바이너리 파일로 저장
            try
            {
                string filePath = Path.Combine(SaveFolderPath, $"{saveFileName}.dat");
                using (FileStream fileStream = new FileStream(filePath, FileMode.Create))
                using (BinaryWriter writer = new BinaryWriter(fileStream))
                {
                    mapFileData.WriteToBinary(writer);
                }
                
                Debug.Log($"Map data saved successfully: {filePath}");
                return true;
            }
            catch (System.Exception e)
            {
                Debug.LogError($"Failed to save map data: {e.Message}");
                return false;
            }
        }
        
        /// <summary>
        /// 저장된 맵 데이터를 로드합니다.
        /// </summary>
        /// <param name="fileName">로드할 파일명 (확장자 제외)</param>
        /// <returns>로드된 맵 데이터, 실패 시 null</returns>
        public MapFileData LoadMapData(string fileName = null)
        {
            string loadFileName = fileName ?? _fileName;
            string filePath = Path.Combine(SaveFolderPath, $"{loadFileName}.dat");
            
            if (!File.Exists(filePath))
            {
                Debug.LogWarning($"Map data file not found: {filePath}");
                return null;
            }
            
            try
            {
                using (FileStream fileStream = new FileStream(filePath, FileMode.Open))
                using (BinaryReader reader = new BinaryReader(fileStream))
                {
                    MapFileData mapFileData = MapFileData.ReadFromBinary(reader);
                    Debug.Log($"Map data loaded successfully: {filePath}");
                    return mapFileData;
                }
            }
            catch (System.Exception e)
            {
                Debug.LogError($"Failed to load map data: {e.Message}");
                return null;
            }
        }
        
        /// <summary>
        /// 로드된 맵 데이터를 지정된 위치에 덮어씌웁니다.
        /// 좌표계: 왼쪽 아래가 (0,0), 오른쪽 위가 (width, height)
        /// </summary>
        /// <param name="mapFileData">로드된 맵 데이터</param>
        /// <param name="targetX">덮어씌울 시작 X 좌표 (null인 경우 원본 위치 사용)</param>
        /// <param name="targetY">덮어씌울 시작 Y 좌표 (null인 경우 원본 위치 사용)</param>
        /// <returns>적용 성공 여부</returns>
        public bool ApplyMapData(MapFileData mapFileData, int? targetX = null, int? targetY = null)
        {
            if (_tilemap == null || mapFileData == null)
            {
                Debug.LogError("Tilemap is not assigned or MapFileData is null");
                return false;
            }
            
            int startX = targetX ?? mapFileData.StartPosition.x;
            int startY = targetY ?? mapFileData.StartPosition.y;
            
            try
            {
                // 지정된 범위에 타일 데이터를 적용
                // 좌표계 변환: 저장할 때 Y축을 뒤집었으므로 로드할 때도 뒤집어서 복원
                for (int x = 0; x < mapFileData.Width; x++)
                {
                    for (int y = 0; y < mapFileData.Height; y++)
                    {
                        int tilemapX = startX + x;
                        int tilemapY = startY + y;
                        
                        // Y좌표를 뒤집어서 읽기 (저장할 때 뒤집었으므로)
                        int flippedY = mapFileData.Height - 1 - y;
                        uint tileData = mapFileData.GetTile(x, flippedY);
                        
                        // uint를 TileBase로 변환 후 타일맵에 설정
                        TileBase tile = ConvertUintToTile(tileData);
                        Vector3Int tilemapPosition = new Vector3Int(tilemapX, tilemapY, 0);
                        _tilemap.SetTile(tilemapPosition, tile);
                    }
                }
                
                Debug.Log($"Map data applied successfully at position ({startX}, {startY})");
                return true;
            }
            catch (System.Exception e)
            {
                Debug.LogError($"Failed to apply map data: {e.Message}");
                return false;
            }
        }
        
        /// <summary>
        /// TileBase를 uint로 변환합니다.
        /// </summary>
        private uint ConvertTileToUint(TileBase tile)
        {
            if (tile == null)
                return 0;
            
            // 간단한 해시 기반 변환 (실제 프로젝트에서는 타일 ID 매핑 테이블 사용 권장)
            return (uint)tile.GetInstanceID();
        }
        
        /// <summary>
        /// uint를 TileBase로 변환합니다.
        /// </summary>
        private TileBase ConvertUintToTile(uint tileData)
        {
            if (tileData == 0)
                return null;
            
#if UNITY_EDITOR
            // 에디터에서만 인스턴스 ID로 오브젝트 찾기 (제한적이므로 실제로는 타일 매핑 테이블 사용 권장)
            UnityEngine.Object obj = EditorUtility.InstanceIDToObject((int)tileData);
            return obj as TileBase;
#else
            // 런타임에서는 다른 방식으로 타일을 찾아야 함 (예: 타일 매핑 테이블)
            Debug.LogWarning("Runtime tile conversion not implemented. Use tile mapping table.");
            return null;
#endif
        }
    }
}