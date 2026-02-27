using System;
using System.Collections.Generic;

namespace ARPG.Map
{
    [Serializable]
    public class TileModification
    {
        public int LocalX;      // 0~7 (청크 내 로컬 좌표)
        public int LocalY;      // 0~7
        public int ObjectId;    // GlobalEnum.ObjectType 값
        public bool IsRemoval;  // true = 기존 오브젝트 제거
    }

    [Serializable]
    public class ChunkModificationData
    {
        public int ChunkX;
        public int ChunkY;
        public List<TileModification> Modifications = new();
    }
}
