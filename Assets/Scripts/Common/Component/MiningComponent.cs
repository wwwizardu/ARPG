using UnityEngine;

namespace ARPG.Component
{
    /// <summary>
    /// 플레이어 채광 작업 상태 (WORLD_ORE_VEIN_PLAN §7.1). E로 시작할 때 OreVeinManager.TryBeginMining이 붙이고,
    /// System_PlayerMining이 진행·취소·완료를 판단해 뗀다. 대상 광맥은 청크 좌표·굴린 날짜로 가리킨다.
    /// </summary>
    public struct MiningComponent
    {
        public int ChunkX;
        public int ChunkY;
        public int RollDay;
        public float Elapsed;
        // 시작 위치에서 벗어나면(이동·밀림) 취소한다
        public Vector2 StartPosition;
        // 직전 확인 때의 HP. 줄었으면 피격으로 보고 취소한다 (회복으로 늘면 갱신)
        public int LastHp;
    }
}
