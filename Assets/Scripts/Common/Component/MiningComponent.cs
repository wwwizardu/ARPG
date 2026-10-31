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
        // 피해와 회복이 같은 틱에 일어나도 피격을 놓치지 않는다
        public bool InterruptedByDamage;
    }
}
