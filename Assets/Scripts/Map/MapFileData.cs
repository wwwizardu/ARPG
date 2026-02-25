using UnityEngine;
using System;
using System.IO;
using System.Collections.Generic;

namespace ARPG.Map
{
    public enum MapType
    {
        Village,
        Dungeon,
        Field,
    }

    [System.Serializable]
    public class MapFileData
    {
        [SerializeField] private MapType _mapType;
        [SerializeField] private int _width;
        [SerializeField] private int _height;
        [SerializeField] private Vector2Int _startPosition;

        [SerializeField] private string _themeName;
        [SerializeField] private ulong[] _tileData;

        [SerializeField] List<MapFileObjectData> _objectList = new();

        public MapType MapType => _mapType;
        public int Width => _width;
        public int Height => _height;
        public Vector2Int StartPosition => _startPosition;

        public MapFileData(MapType mapType, int width, int height, Vector2Int startPosition)
        {
            _mapType = mapType;
            _width = width;
            _height = height;
            _startPosition = startPosition;
            _tileData = new ulong[width * height];
        }

        public ulong GetTile(int x, int y)
        {
            if (x < 0 || x >= _width || y < 0 || y >= _height)
                return 0;

            return _tileData[y * _width + x];
        }

        public void SetTile(int x, int y, ulong tileValue)
        {
            if (x < 0 || x >= _width || y < 0 || y >= _height)
                return;

            _tileData[y * _width + x] = tileValue;
        }

        public ulong[,] GetTileData2D()
        {
            ulong[,] result = new ulong[_width, _height];
            for (int x = 0; x < _width; x++)
            {
                for (int y = 0; y < _height; y++)
                {
                    result[x, y] = _tileData[y * _width + x];
                }
            }
            return result;
        }

        public void SetTileData2D(ulong[,] tileData)
        {
            if (tileData.GetLength(0) != _width || tileData.GetLength(1) != _height)
            {
                Debug.LogError($"Tile data dimensions ({tileData.GetLength(0)}, {tileData.GetLength(1)}) do not match MapFileData dimensions ({_width}, {_height})");
                return;
            }

            for (int x = 0; x < _width; x++)
            {
                for (int y = 0; y < _height; y++)
                {
                    _tileData[y * _width + x] = tileData[x, y];
                }
            }
        }

        public void AddObject(int x, int y, int objectType, int objectId)
        {
            MapFileObjectData objectData = new MapFileObjectData
            {
                X = x,
                Y = y,
                ObjectType = objectType,
                ObjectId = objectId
            };
            _objectList.Add(objectData);
        }

        public void ClearObjects()
        {
            _objectList.Clear();
        }

        public List<MapFileObjectData> GetObjects()
        {
            return _objectList;
        }

        /// <summary>
        /// 바이너리 데이터를 스트림에 저장합니다.
        /// </summary>
        public void WriteToBinary(BinaryWriter writer)
        {
            // JSON으로 직렬화한 후 바이너리로 저장
            string jsonData = JsonUtility.ToJson(this);
            byte[] jsonBytes = System.Text.Encoding.UTF8.GetBytes(jsonData);
            writer.Write(jsonBytes.Length);
            writer.Write(jsonBytes);
        }

        /// <summary>
        /// 바이너리 데이터를 스트림에서 읽어옵니다.
        /// </summary>
        public static MapFileData ReadFromBinary(BinaryReader reader)
        {
            // 바이너리에서 JSON 데이터 읽어온 후 역직렬화
            int jsonLength = reader.ReadInt32();
            byte[] jsonBytes = reader.ReadBytes(jsonLength);
            string jsonData = System.Text.Encoding.UTF8.GetString(jsonBytes);
            return JsonUtility.FromJson<MapFileData>(jsonData);
        }
    }

    [System.Serializable]
    public class MapFileObjectData
    {
        public int X;
        public int Y;

        public int ObjectType;
        public int ObjectId;
    }
}