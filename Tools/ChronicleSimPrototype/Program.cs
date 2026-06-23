using ChronicleSim;

// ─────────────────────────────────────────────────────────────
// 정착지 연대기 시뮬 프로토타입 CLI
//   dotnet run -- dump  <seed.json>     한 시드: 연대기 텍스트 + 통계
//   dotnet run -- stats <seed.json>     한 시드: 통계만
//   dotnet run -- sweep <folder>        폴더 내 seed_*.json 일괄 → 집계
//   dotnet run                          기본: ./placements 스윕
//   dotnet run -- gen <seed>            ⚠️합성 placement 1개 → placements_synth/
//   dotnet run -- gensweep              ⚠️합성 placement 21개 → placements_synth/
// ─────────────────────────────────────────────────────────────

string cmd = args.Length > 0 ? args[0].ToLowerInvariant() : "sweep";
string path = args.Length > 1 ? args[1] : "placements";

try
{
    switch (cmd)
    {
        case "dump": Dump(path, full: true); break;
        case "stats": Dump(path, full: false); break;
        case "sweep": Sweep(path); break;
        case "gen":
            Synthetic.GenerateAndSave(args.Length > 1 ? int.Parse(args[1]) : 12345, 0.20, "placements_synth");
            break;
        case "gensweep":
            Synthetic.GenerateAndSave(12345, 0.20, "placements_synth");
            for (int s = 1001; s <= 1020; s++) Synthetic.GenerateAndSave(s, 0.20, "placements_synth");
            break;
        default: Usage(); break;
    }
}
catch (FileNotFoundException ex) { Console.Error.WriteLine($"파일 없음: {ex.FileName ?? ex.Message}"); Environment.Exit(1); }
catch (DirectoryNotFoundException) { Console.Error.WriteLine($"폴더 없음: {path}\n먼저 Unity 에디터 메뉴 'ARPG/Chronicle Sim/Export ...'로 placement를 export 하세요."); Environment.Exit(1); }

return;

// ─────────────────────────────────────────────────────────────

void Usage()
{
    Console.WriteLine("""
        사용법:
          dotnet run -- dump  <seed.json>   한 시드의 연대기 전문 + 통계
          dotnet run -- stats <seed.json>   한 시드의 통계만
          dotnet run -- sweep <folder>      폴더의 seed_*.json 일괄 집계
          dotnet run                        ./placements 폴더 스윕
        """);
}

void Dump(string file, bool full)
{
    Simulator sim = RunFile(file);
    if (full)
        PrintChronicle(sim);
    PrintKingdoms(sim);
    Stats st = Stats.Compute(sim);
    Console.WriteLine();
    Console.WriteLine(st.OneLine());
    Console.WriteLine(st.Detail());
}

void Sweep(string folder)
{
    string[] files = Directory.GetFiles(folder, "seed_*.json").OrderBy(f => f).ToArray();
    if (files.Length == 0)
    {
        Console.Error.WriteLine($"{folder} 에 seed_*.json 없음. Unity에서 export 먼저.");
        Environment.Exit(1);
    }

    var all = new List<Stats>();
    Console.WriteLine($"{"seed",-8} {"setl",5} {"king",5} {"max%",6} {"multi",6} {"ruin%",6} {"merg%",6} {"conq%",6} {"nbr",5}");
    Console.WriteLine(new string('─', 66));
    foreach (string f in files)
    {
        Simulator sim = RunFile(f);
        Stats st = Stats.Compute(sim);
        all.Add(st);
        Console.WriteLine(
            $"{st.MapSeed,-8} {st.Settlements,5} {st.SurvivingKingdoms,5} {st.LargestPct,5:P0} " +
            $"{st.MultiRaceKingdoms,6} {st.RuinedPct,5:P0} {st.MergedPct,5:P0} {st.ConqueredPct,5:P0} {st.AvgNeighbors,5:F1}");
    }

    Console.WriteLine(new string('─', 66));
    int runs = all.Count;
    double megaPct = all.Count(s => s.LargestPct > 0.40) / (double)runs;
    double anyMultiPct = all.Count(s => s.MultiRaceKingdoms >= 1) / (double)runs;
    Console.WriteLine($"""

        집계 ({runs} seeds)
          평균 생존 왕국 수 ........ {all.Average(s => s.SurvivingKingdoms):F1}
          평균 최대 왕국 점유율 .... {all.Average(s => s.LargestPct):P1}
          평균 다종족 왕국 수 ...... {all.Average(s => s.MultiRaceKingdoms):F1}
          평균 폐허/합병/정복 ...... {all.Average(s => s.RuinedPct):P0} / {all.Average(s => s.MergedPct):P0} / {all.Average(s => s.ConqueredPct):P0}
          평균 이웃 수 ............. {all.Average(s => s.AvgNeighbors):F1}

        ★ 단일 제국 수렴(최대 왕국 >40%) 발생 ... {megaPct:P0}  (낮을수록 좋음 — 과확장 페널티 작동)
        ★ 다종족 제국 ≥1 발생 ................... {anyMultiPct:P0}  (높을수록 좋음 — 정복-병합 작동)
        """);
}

Simulator RunFile(string file)
{
    PlacementFile pf = PlacementFile.Load(file);
    var sim = new Simulator(pf);
    sim.Run();
    return sim;
}

void PrintChronicle(Simulator sim)
{
    Console.WriteLine($"=== 연대기 (seed {sim.MapSeed}, 정착지 {sim.Settlements.Count}, {SimConfig.SimYears}년) ===");
    foreach (ChronicleEvent e in sim.Events)
        Console.WriteLine($"Y{e.Year,-3} [{e.Type,-12}] {Describe(sim, e)}");
}

void PrintKingdoms(Simulator sim)
{
    var surviving = sim.Kingdoms
        .Select(k => (k, members: k.Members.Where(id => sim.Get(id).Active).ToList()))
        .Where(x => x.members.Count > 0)
        .OrderByDescending(x => x.members.Count)
        .ToList();

    Console.WriteLine();
    Console.WriteLine($"=== 생존 왕국 {surviving.Count}개 ===");
    foreach ((Kingdom k, List<int> members) in surviving)
    {
        var races = members.Select(id => sim.Get(id).Race).Distinct().ToList();
        string multi = races.Count >= 2 ? "  ※다종족" : "";
        long pop = (long)members.Sum(id => sim.Get(id).Pop);
        Console.WriteLine(
            $"  {k.Name,-20} 수도 S#{k.CapitalId,-4} 멤버 {members.Count,3}  종족[{string.Join(",", races)}]  인구 {pop}{multi}");
    }
}

string Describe(Simulator sim, ChronicleEvent e)
{
    string A() => Label(sim, e.SubjectId);
    string B() => Label(sim, e.ObjectId);
    string K() => sim.Kingdoms.FirstOrDefault(k => k.Id == e.KingdomId)?.Name ?? $"왕국#{e.KingdomId}";

    return e.Type switch
    {
        EventType.Trade => $"{A()} ↔ {B()} 교역",
        EventType.Skirmish => $"{A()} ⚔ {B()} 국경 분쟁",
        EventType.War => $"{A()} 가 {B()} 에 전쟁 승리",
        EventType.Conquer => $"[{K()}] {B()} 가 {A()} 정복·편입",
        EventType.Alliance => $"{A()} = {B()} 동맹",
        EventType.Merge => $"{B()} 가 {A()} 흡수 합병",
        EventType.Ruin => $"{A()} 폐허화",
        EventType.KingdomFound => $"{K()} 건국 (수도 {A()})",
        EventType.KingdomJoin => $"{A()} 가 {K()} 에 편입",
        _ => $"{A()}",
    };
}

string Label(Simulator sim, int id)
{
    if (id < 0) return "-";
    SettlementState s = sim.Get(id);
    return $"S#{id}<{s.Race}>";
}
