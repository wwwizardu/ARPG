using UnityEngine;
using System;
using System.IO;

namespace ARPG.Map
{
    [System.Serializable]
    public class MapFileData
    {
        [SerializeField] private int _width;
        [SerializeField] private int _height;
        [SerializeField] private Vector2Int _startPosition;
        [SerializeField] private uint[] _tileData;
        
        public int Width => _width;
        public int Height => _height;
        public Vector2Int StartPosition => _startPosition;
        
        public MapFileData(int width, int height, Vector2Int startPosition)
        {
            _width = width;
            _height = height;
            _startPosition = startPosition;
            _tileData = new uint[width * height];
        }
        
        public uint GetTile(int x, int y)
        {
            if (x < 0 || x >= _width || y < 0 || y >= _height)
                return 0;
            
            return _tileData[y * _width + x];
        }
        
        public void SetTile(int x, int y, uint tileValue)
        {
            if (x < 0 || x >= _width || y < 0 || y >= _height)
                return;
            
            _tileData[y * _width + x] = tileValue;
        }
        
        public uint[,] GetTileData2D()
        {
            uint[,] result = new uint[_width, _height];
            for (int x = 0; x < _width; x++)
            {
                for (int y = 0; y < _height; y++)
                {
                    result[x, y] = _tileData[y * _width + x];
                }
            }
            return result;
        }
        
        public void SetTileData2D(uint[,] tileData)
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
}