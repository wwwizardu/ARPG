using System.Text.Json;

namespace ChronicleSim;

/// <summary>에디터 ChronicleSimExport가 쓴 JSON과 1:1 대응.</summary>
public sealed class PlacementFile
{
    public int MapSeed { get; set; }
    public int ChunkSize { get; set; }
    public List<PlacementSettlement> Settlements { get; set; } = new();

    private static readonly JsonSerializerOptions Opts = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    public static PlacementFile Load(string path)
    {
        string json = File.ReadAllText(path);
        PlacementFile? file = JsonSerializer.Deserialize<PlacementFile>(json, Opts);
        if (file == null)
            throw new InvalidDataException($"placement JSON 파싱 실패: {path}");
        return file;
    }
}

public sealed class PlacementSettlement
{
    public int Id { get; set; }
    public int RegionX { get; set; }
    public int RegionY { get; set; }
    public int ChunkX { get; set; }
    public int ChunkY { get; set; }
    public int WorldX { get; set; }
    public int WorldY { get; set; }
    public int Race { get; set; }
    public string RaceName { get; set; } = "";
    public int Biome { get; set; }
    public string BiomeName { get; set; } = "";
    public int RegionDominantBiome { get; set; }
}
