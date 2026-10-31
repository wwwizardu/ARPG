#nullable enable
using ARPG.Data;
using ARPG.History;
using UnityEngine;

namespace ARPG.OreVein
{
    /// <summary>
    /// 월드 광맥의 순수 규칙 (WORLD_ORE_VEIN_PLAN §4~§6, §10.1). 상태를 갖지 않는다 — 입력(시드·청크 좌표·날짜·기록·시각)만 보고 계산한다.
    /// 게임(OreVeinManager)과 진단(DungeonRuntimeDiagnostics_OreVein)이 같은 메서드를 부른다.
    /// 수치는 1차 시작값이며 나중에 구글 시트 표로 옮긴다 (§8.3). 게임 1시간 = 실제 60초.
    /// </summary>
    public static class OreVeinRules
    {
        // ===== 굴림 (§4.1) =====
        // 청크 광맥 확률. 청크당 시도는 1회
        public const int ChunkChancePercent = 12;
        public const float HoursPerDay = 24f;

        // ===== 잔량·재생·정리 (§2, §6) =====
        public const int Charges = 3;
        // 고갈 뒤 다시 굴릴 수 있을 때까지 (실제 24분). 24게임시간 미만으로 줄이지 않는다 — 그 사이 날짜가 바뀌어야 같은 칸에 그대로 다시 나오지 않는다
        public const float RegrowHours = 24f;
        // 활성 기록이 마지막 활성 시각부터 이만큼 지나면 정리한다 (실제 72분). 정리는 게임 하루 1회
        public const float StaleActiveHours = 72f;

        // ===== 위치 조건 (§5) =====
        // 던전 입구 칸 중심과의 직선거리가 이보다 짧으면 놓지 않는다 (입구끼리의 간격 규칙과 같은 방식)
        public const float EntranceClearance = 8f;
        // 마을은 여유 칸 없이 마을 안(청사진 사각형·마을 영역)에만 만들지 않는다 (2026-10-31 개정, 기존 3칸)

        // ===== 광종 (§4.1) — 구역 번호 = 원점 청크에서 떨어진 청크 수 + 1 =====
        // 주석 시작 구역 5는 주석으로 만드는 장비가 없어 둔 임시값이다 (§10.2). 철 10은 철 장비 드랍 레벨 10에 맞춘 값이다
        public const int TinMinZone = 5;
        public const int IronMinZone = 10;
        // 나올 수 있는 광종 중 가장 상위 광종의 몫. 나머지 광종이 남은 몫을 똑같이 나눈다
        public const int TopOrePercent = 50;

        // ===== 광종 → 아이템 (§2) =====
        public const int CopperItemId = 2002;
        public const int IronItemId = 2003;
        public const int TinItemId = 2004;

        // ===== 플레이어 채광 (§7.1) =====
        // 광맥 루트(칸 중심)에서 이 거리 안이면 E 안내가 뜬다 (NPC 상호작용과 같은 1.5칸)
        public const float InteractRange = 1.5f;
        // 채광 중 이 거리를 넘게 벗어나면 취소한다 (안내 거리 + 발 위치 흔들림 여유)
        public const float MiningCancelRange = 1.75f;
        public const float MiningSeconds = 1.2f;
        public const int MiningYield = 1;

        /// <summary>게임 날짜 = floor(누적 게임시간 / 24) (§4.1).</summary>
        public static int GetDay(float gameTime) => Mathf.FloorToInt(Mathf.Max(0f, gameTime) / HoursPerDay);

        /// <summary>광종이 주는 아이템 (ItemTable Ore 분류).</summary>
        public static int GetItemId(OreVeinType type) => type switch
        {
            OreVeinType.Copper => CopperItemId,
            OreVeinType.Tin => TinItemId,
            OreVeinType.Iron => IronItemId,
            _ => 0,
        };

        /// <summary>
        /// 구역 번호로 광종을 고른다 (§4.1). 나올 수 있는 광종 = 구리(1+)·주석(5+)·철(10+). 가장 상위 광종 50%,
        /// 나머지는 남은 50%를 똑같이 나눈다 → 1~4 구리 100%, 5~9 구리 50·주석 50, 10+ 구리 25·주석 25·철 50.
        /// roll은 0~99.
        /// </summary>
        public static OreVeinType SelectType(int zone, int roll)
        {
            int available = zone >= IronMinZone ? 3 : zone >= TinMinZone ? 2 : 1;
            if (available == 1)
                return OreVeinType.Copper;
            if (roll < TopOrePercent)
                return available == 3 ? OreVeinType.Iron : OreVeinType.Tin;
            int lower = (roll - TopOrePercent) * (available - 1) / (100 - TopOrePercent);
            return lower == 0 ? OreVeinType.Copper : OreVeinType.Tin;
        }

        /// <summary>(세계, 청크, 날짜)의 고정 무작위. UnityEngine.Random을 쓰지 않는다 (§4.1).</summary>
        public static ulong Hash(int worldSeed, int chunkX, int chunkY, int day)
        {
            ulong x = DerivedSeed.Derive(worldSeed, DerivedSeed.ChannelOreVein);
            x ^= unchecked((ulong)(uint)chunkX * 0x9E3779B97F4A7C15UL);
            x = Mix(x);
            x ^= unchecked((ulong)(uint)chunkY * 0xC2B2AE3D27D4EB4FUL);
            x = Mix(x);
            x ^= unchecked((ulong)(uint)day * 0x165667B19E3779F9UL);
            return Mix(x);
        }

        /// <summary>
        /// 청크 굴림 1회: 확률 판정 → 광종(청크 구역 번호) → 청크 안 칸 하나 (§4.1). 같은 시드·청크·날짜는 항상 같은 결과다.
        /// 위치 조건(§5)은 호출자가 이 청크의 타일 데이터로 검사한다.
        /// </summary>
        public static bool TryRoll(int worldSeed, int chunkX, int chunkY, int day, int chunkSize, int zone,
            out int localX, out int localY, out OreVeinType type)
        {
            ulong hash = Hash(worldSeed, chunkX, chunkY, day);
            localX = -1;
            localY = -1;
            type = OreVeinType.None;
            if ((int)(hash % 100UL) >= ChunkChancePercent)
                return false;
            int cell = (int)((hash >> 32) % (ulong)(chunkSize * chunkSize));
            localX = cell % chunkSize;
            localY = cell / chunkSize;
            // 광종은 확률·칸과 겹치지 않게 한 번 더 섞은 값으로 고른다
            type = SelectType(zone, (int)(Mix(hash ^ 0xA24BAED4963EE407UL) % 100UL));
            return true;
        }

        /// <summary>언덕·막힌 칸·오브젝트가 없는 칸 (청크 타일 비트, §5).</summary>
        public static bool IsTerrainClear(ulong tile)
            => (tile & ((ulong)GlobalEnum.TileFlag.Hill | (ulong)GlobalEnum.TileFlag.Blocked |
                        (ulong)GlobalEnum.TileFlag.ObjectLayerMask)) == 0;

        public static bool IsRegrowDue(float regrowGameTime, float now) => now >= regrowGameTime;

        /// <summary>정리 대상 (§6.3): 활성은 마지막 활성 시각부터 72게임시간, 고갈은 재생 가능 시각이 지난 뒤에만.</summary>
        public static bool IsCleanupDue(OreVeinRecord record, float now) => record.State switch
        {
            OreVeinState.Active => now - record.LastActiveGameTime >= StaleActiveHours,
            OreVeinState.Exhausted => IsRegrowDue(record.RegrowGameTime, now),
            _ => true,
        };

        private static ulong Mix(ulong x)
        {
            x = (x ^ (x >> 30)) * 0xBF58476D1CE4E5B9UL;
            x = (x ^ (x >> 27)) * 0x94D049BB133111EBUL;
            return x ^ (x >> 31);
        }
    }
}
