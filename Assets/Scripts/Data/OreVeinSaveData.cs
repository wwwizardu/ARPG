#nullable enable
using System;
using Newtonsoft.Json;

namespace ARPG.Data
{
    /// <summary>광맥 종류 (WORLD_ORE_VEIN_PLAN §2). 저장되므로 새 값은 끝에만 추가한다. 첫 버전은 구리만 나온다.</summary>
    public enum OreVeinType
    {
        None = 0,
        Copper = 1,
        Tin = 2,
        Iron = 3,
    }

    /// <summary>광맥 기록 상태 (§6.1). 기록이 없는 청크는 저장하지 않는다. 저장되므로 새 값은 끝에만 추가한다.</summary>
    public enum OreVeinState
    {
        None = 0,
        Active = 1,
        // 잔량을 다 쓴 상태. 재생 가능 시각이 지난 뒤 다음 활성화 때 기록을 지우고 다시 굴린다
        Exhausted = 2,
    }

    /// <summary>
    /// 청크 하나의 광맥 기록 (WORLD_ORE_VEIN_PLAN §4.2) — ore_veins 파트. 청크당 최대 1개이고, 광맥이 놓였거나 고갈 대기 중인 청크에만 있다.
    /// 소유자는 OreVeinManager다.
    /// </summary>
    [Serializable]
    public sealed class OreVeinRecord
    {
        public int ChunkX;
        public int ChunkY;
        public OreVeinType Type;
        // 광맥이 놓인 월드 칸 (기록한 청크 안)
        public int TileX;
        public int TileY;
        public int Remaining;
        // 이 광맥을 만든 굴림의 게임 날짜. 채광 대상 식별에 쓴다 (§7.1)
        public int RollDay;
        public OreVeinState State;
        // 고갈이면 다시 굴릴 수 있는 게임 시각 (활성이면 0)
        [JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)]
        public float RegrowGameTime;
        // 청크가 마지막으로 켜져 있던 게임 시각 (켜질 때·꺼질 때 갱신). 오래 비활성인 기록 정리에 쓴다 (§6.3)
        public float LastActiveGameTime;
    }
}
