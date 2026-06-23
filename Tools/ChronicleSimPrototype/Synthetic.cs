using System.Text.Json;

namespace ChronicleSim;

/// <summary>
/// ⚠️ 합성(가짜) placement 생성기 — 파이프라인/시뮬 로직 스모크 테스트용.
/// 실제 게임 배치가 아니라 "종족 클러스터 + 격자 산포"를 흉내 낸 근사 분포다.
/// 신뢰할 결론은 Unity 'ARPG/Chronicle Sim/Export ...' 의 실제 배치(placements/)로 낼 것.
/// (우리가 §토론에서 'mock'이라 부른 것 — 동역학 거동 확인엔 충분, 실제 월드 보장은 아님)
/// </summary>
public static class Synthetic
{
    private const int RegionMin = -25, RegionMax = 24; // ComputeAll의 region 범위와 동일
    private const int ChunkSize = 8;

    // 종족 대표 biome (RaceBiomeTable 역매핑 근사)
    private static Biome RepresentativeBiome(Race r) => r switch
    {
        Race.Human => Biome.Plains,
        Race.Elf => Biome.Forest,
        Race.Dwarf => Biome.Mountain,
        Race.Orc => Biome.Steppe,
        Race.Nomad => Biome.Desert,
        Race.Lizardman => Biome.Swamp,
        Race.Yeti => Biome.Snow,
        _ => Biome.Plains,
    };

    public static void GenerateAndSave(int seed, double density, string dir)
    {
        Directory.CreateDirectory(dir);
        var rng = new SplitMix64(DerivedSeed.Derive(seed, 0xA5A5A5A5A5A5A5A5UL));

        // 1) 종족 클러스터 중심 — 종족별 2~3개 (같은 종족이 지리적으로 뭉치게)
        var centers = new List<(int x, int y, Race race)>();
        for (int r = 0; r < 7; r++)
        {
            int n = rng.Range(2, 3);
            for (int k = 0; k < n; k++)
                centers.Add((rng.Range(RegionMin, RegionMax), rng.Range(RegionMin, RegionMax), (Race)r));
        }

        // 2) 격자 순회 — density 확률로 정착지 배치, race = 최근접 중심 종족
        var settlements = new List<PlacementSettlement>();
        int id = 0;
        for (int rx = RegionMin; rx <= RegionMax; rx++)
        {
            for (int ry = RegionMin; ry <= RegionMax; ry++)
            {
                if (rng.Chance(density) == false)
                    continue;

                Race race = NearestRace(centers, rx, ry, rng);
                Biome biome = RepresentativeBiome(race);
                settlements.Add(new PlacementSettlement
                {
                    Id = id++,
                    RegionX = rx,
                    RegionY = ry,
                    ChunkX = rx * 4 + 2,
                    ChunkY = ry * 4 + 2,
                    WorldX = rx * 32 + 16,
                    WorldY = ry * 32 + 16,
                    Race = (int)race,
                    RaceName = race.ToString(),
                    Biome = (int)biome,
                    BiomeName = biome.ToString(),
                    RegionDominantBiome = (int)biome,
                });
            }
        }

        var file = new { MapSeed = seed, ChunkSize, Settlements = settlements };
        string json = JsonSerializer.Serialize(file, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(Path.Combine(dir, $"seed_{seed}.json"), json);
        Console.WriteLine($"[synthetic] seed {seed}: {settlements.Count} settlements → {dir}");
    }

    private static Race NearestRace(List<(int x, int y, Race race)> centers, int rx, int ry, SplitMix64 rng)
    {
        // 5% 확률로 노이즈(외지 종족 섞임) — 국경 혼합 표현
        if (rng.Chance(0.05))
            return (Race)rng.Range(0, 6);

        Race best = Race.Human;
        int bestD = int.MaxValue;
        foreach ((int x, int y, Race race) in centers)
        {
            int dx = x - rx, dy = y - ry;
            int d = dx * dx + dy * dy;
            if (d < bestD) { bestD = d; best = race; }
        }
        return best;
    }
}
