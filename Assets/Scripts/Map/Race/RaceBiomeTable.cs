using System.Collections.Generic;
using UnityEngine;

namespace ARPG.Map
{
    /// <summary>
    /// biome ↔ race 호환 매핑 (MAP_DESIGN.md §7.4).
    /// Phase 2 초기: 코드 하드코딩. 콘텐츠 늘면 시트 .bytes로 이관 (§12 #13).
    ///
    /// 사용:
    ///   RaceType race = RaceBiomeTable.PickRace(biome, hash);
    ///   bool ok = RaceBiomeTable.IsCompatible(biome, race);
    /// </summary>
    public static class RaceBiomeTable
    {
        // biome → 거기에 살 수 있는 race 목록. 빈 리스트면 그 biome엔 settlement 없음.
        // 한 race가 여러 biome에 등장 가능 (예: Human은 Plains/Forest 둘 다).
        private static readonly Dictionary<GlobalEnum.BiomeType, GlobalEnum.RaceType[]> _biomeToRaces = new()
        {
            { GlobalEnum.BiomeType.Plains,   new[] { GlobalEnum.RaceType.Human } },
            { GlobalEnum.BiomeType.Forest,   new[] { GlobalEnum.RaceType.Elf,    GlobalEnum.RaceType.Human } },
            { GlobalEnum.BiomeType.Desert,   new[] { GlobalEnum.RaceType.Nomad,  GlobalEnum.RaceType.Human } },
            { GlobalEnum.BiomeType.Swamp,    new[] { GlobalEnum.RaceType.Lizardman } },
            { GlobalEnum.BiomeType.Steppe,   new[] { GlobalEnum.RaceType.Nomad,  GlobalEnum.RaceType.Orc } },
            { GlobalEnum.BiomeType.Tundra,   new[] { GlobalEnum.RaceType.Yeti,   GlobalEnum.RaceType.Nomad } },
            { GlobalEnum.BiomeType.Snow,     new[] { GlobalEnum.RaceType.Yeti,   GlobalEnum.RaceType.Dwarf } },
            { GlobalEnum.BiomeType.Mountain, new[] { GlobalEnum.RaceType.Dwarf } },
            // Wasteland: settlement 없음 (빈 매핑)
        };

        /// <summary>biome에서 결정론 hash로 race 1개 선택. 호환 race 없으면 null 반환.</summary>
        public static GlobalEnum.RaceType? PickRace(GlobalEnum.BiomeType biome, ulong hash)
        {
            if (_biomeToRaces.TryGetValue(biome, out var races) == false || races.Length == 0)
                return null;
            int idx = (int)(hash % (ulong)races.Length);
            return races[idx];
        }

        /// <summary>해당 biome이 race를 수용 가능한지.</summary>
        public static bool IsCompatible(GlobalEnum.BiomeType biome, GlobalEnum.RaceType race)
        {
            if (_biomeToRaces.TryGetValue(biome, out var races) == false)
                return false;
            for (int i = 0; i < races.Length; i++)
                if (races[i] == race) return true;
            return false;
        }

        /// <summary>해당 biome에 settlement 후보 race가 있는지.</summary>
        public static bool HasAnyRace(GlobalEnum.BiomeType biome)
        {
            return _biomeToRaces.TryGetValue(biome, out var races) && races.Length > 0;
        }
    }
}
