using UnityEngine;
using System.IO;
using System.Collections.Generic;
using UnityEngine.Tilemaps;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace ARPG.Map
{
    public class MapFileSaver : MonoBehaviour
    {
        [Header("Tilemap")]
        [SerializeField] private Tilemap _tilemapGround;
        [SerializeField] private Tilemap _tilemapObject;

        [Header("파일 저장 설정")]
        [SerializeField] private string _fileName = "MapData";
        [SerializeField] private string _folderName = "SavedMaps";

        [Header("Theme Settings")]
        [SerializeField] private ThemeTileSet _themeTileSet;

        [Header("Editor Only")]
        [SerializeField] private string _saveFileName = "";
        [SerializeField] private string _loadFileName = "";
        
        private string SaveFolderPath => Path.Combine(Application.dataPath, "_BinaryData", "TilemapData");
        
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
            if (_tilemapGround == null)
            {
                Debug.LogError("Tilemap is not assigned");
                return false;
            }
            
            // 타일맵의 실제 사용 영역 계산
            BoundsInt bounds = _tilemapGround.cellBounds;
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
                    Vector3Int tilemapPosition = new Vector3Int(tilemapX, tilemapY, 0);

                    // 1. Ground 타일 데이터 가져오기
                    TileBase tileBase = _tilemapGround.GetTile(tilemapPosition);
                    CustomTile customTile = tileBase as CustomTile;

                    if(customTile == null)
                    {
                        if(tileBase == null)
                        {
                            Debug.LogWarning($"[MapFileSaver] Empty tile at X({x}), Y({y}) - TilemapPos({tilemapX}, {tilemapY})");
                        }
                        else
                        {
                            Debug.LogError($"[MapFileSaver] Non-CustomTile detected at X({x}), Y({y}) - TilemapPos({tilemapX}, {tilemapY}), Type: {tileBase.GetType().Name}");
                        }
                        continue;
                    }

                    // 2. Object 타일 데이터 가져오기
                    ulong objectId = 0;
                    if (_tilemapObject != null)
                    {
                        TileBase objectTileBase = _tilemapObject.GetTile(tilemapPosition);
                        CustomTile objectCustomTile = objectTileBase as CustomTile;

                        if (objectCustomTile != null)
                        {
                            objectId = (ulong)objectCustomTile.CustomData;
                        }
                    }

                    // 3. Ground 타일과 Object 타일 데이터를 통합하여 생성
                    ulong tileData = MapManager.CombineTileData(0, (GlobalEnum.TileType)customTile.CustomData, 0, 0, objectId);

                    // 4. Y좌표를 뒤집어서 저장 (왼쪽 아래가 (0,0)이 되도록)
                    int flippedY = bounds.size.y - 1 - y;
                    mapFileData.SetTile(x, flippedY, tileData);
                }
            }
            
            // 바이너리 파일로 저장
            try
            {
                // 폴더가 없으면 생성
                if (!Directory.Exists(SaveFolderPath))
                {
                    Directory.CreateDirectory(SaveFolderPath);
                }

                string filePath = Path.Combine(SaveFolderPath, $"{saveFileName}.bytes");
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
            string filePath = Path.Combine(SaveFolderPath, $"{loadFileName}.bytes");
            
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
            if (_tilemapGround == null || mapFileData == null)
            {
                Debug.LogError("Tilemap is not assigned or MapFileData is null");
                return false;
            }
            
            int startX = targetX ?? mapFileData.StartPosition.x;
            int startY = targetY ?? mapFileData.StartPosition.y;
            
            try
            {
                // 1. Ground 타일 데이터 적용
                // 좌표계 변환: 저장할 때 Y축을 뒤집었으므로 로드할 때도 뒤집어서 복원
                for (int x = 0; x < mapFileData.Width; x++)
                {
                    for (int y = 0; y < mapFileData.Height; y++)
                    {
                        int tilemapX = startX + x;
                        int tilemapY = startY + y;

                        // Y좌표를 뒤집어서 읽기 (저장할 때 뒤집었으므로)
                        int flippedY = mapFileData.Height - 1 - y;
                        ulong tileData = mapFileData.GetTile(x, flippedY);

                        // uint를 TileBase로 변환 후 타일맵에 설정
                        CustomTile tile = ConvertUintToTile(tileData);
                        Vector3Int tilemapPosition = new Vector3Int(tilemapX, tilemapY, 0);
                        _tilemapGround.SetTile(tilemapPosition, tile);

                        // 오브젝트 Layer 타일맵 로드
                        CustomTile objectTile = ConvertObjectIdToTile(tileData);
                        if (objectTile != null)
                        {
                            _tilemapObject.SetTile(tilemapPosition, objectTile);
                        }
                    }
                }

                return true;
            }
            catch (System.Exception e)
            {
                Debug.LogError($"Failed to apply map data: {e.Message}");
                return false;
            }
        }

        /// <summary>
        /// uint를 TileBase로 변환합니다.
        /// </summary>
        private CustomTile ConvertUintToTile(ulong tileData)
        {
            if (tileData == 0)
                return null;

            // 하위 10비트를 TileType으로 변환 (GroundLayerMask)
            GlobalEnum.TileType tileType = (GlobalEnum.TileType)(tileData & (ulong)GlobalEnum.TileFlag.GroundLayerMask);

            // ThemeTileSet에서 해당 타일 가져오기
            if (_themeTileSet != null && _themeTileSet.TileSet != null)
            {
                int tileIndex = (int)tileType;
                if (tileIndex >= 0 && tileIndex < _themeTileSet.TileSet.Length)
                {
                    return _themeTileSet.TileSet[tileIndex] as CustomTile;
                }
            }

            Debug.LogError($"[MapFileSaver] ConvertUintToTile - TileType {tileType} not found in ThemeTileSet");
            return null;
        }

        /// <summary>
        /// tileData에서 ObjectTypeMask를 읽어 오브젝트 ID를 추출하고 CustomTile로 변환합니다.
        /// </summary>
        private CustomTile ConvertObjectIdToTile(ulong tileData)
        {
            // ObjectLayerMask를 사용해 오브젝트 ID 추출 (10~19번째 비트)
            ulong objectId = (tileData & (ulong)GlobalEnum.TileFlag.ObjectLayerMask) >> 10;

            if (objectId == 0)
                return null;

            // ThemeTileSet에서 해당 타일 가져오기
            if (_themeTileSet != null && _themeTileSet.ObjectSet != null)
            {
                // ObjectId를 TileSet의 인덱스로 사용
                if (objectId >= 0 && objectId < (ulong)_themeTileSet.ObjectSet.Length)
                {
                    return _themeTileSet.ObjectSet[objectId] as CustomTile;
                }
            }

            Debug.LogError($"[MapFileSaver] ConvertObjectIdToTile - ObjectId {objectId} not found in ThemeTileSet");
            return null;
        }

        public ThemeTileSet ThemeTileSet => _themeTileSet;
    }
}