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
double density = args.Length > 2 && double.TryParse(args[2], out double d) ? d : 1.0;

try
{
    switch (cmd)
    {
        case "dump": Dump(path, full: true, density); break;
        case "stats": Dump(path, full: false, density); break;
        case "sweep": Sweep(path, density); break;
        case "denscompare": DensityCompare(path); break;
        case "dossier":
        {
            int sid = args.Length > 2 && int.TryParse(args[2], out int v) ? v : -1;
            double dens = args.Length > 3 && double.TryParse(args[3], out double dv) ? dv : 1.0;
            Dossier(path, sid, dens);
            break;
        }
        case "villages": Villages(path, density); break;
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
          dotnet run -- dump  <seed.json>            한 시드의 연대기 전문 + 통계
          dotnet run -- stats <seed.json> [density]  한 시드의 통계만
          dotnet run -- sweep <folder> [density]     폴더의 seed_*.json 일괄 집계
          dotnet run -- denscompare <folder>         밀도별 비교 (1.0/0.5/0.25/0.12)
          dotnet run -- dossier <seed.json> [sid] [density]  마을 1개 NPC 호적부 (sid 생략=자동선택)
          dotnet run -- villages <seed.json> [density]       세계 마을 분류 + NPC/관계 생성량
          dotnet run                                 ./placements 폴더 스윕
        """);
}

void Dump(string file, bool full, double density)
{
    Simulator sim = RunFile(file, density);
    if (full)
        PrintChronicle(sim);
    PrintKingdoms(sim);
    Stats st = Stats.Compute(sim);
    Console.WriteLine();
    Console.WriteLine(st.OneLine());
    Console.WriteLine(st.Detail());
}

// 밀도별 비교 — 같은 배치를 여러 유지비율로 솎아 왕국 수/최대 점유율 변화를 한눈에
void DensityCompare(string folder)
{
    string[] files = Directory.GetFiles(folder, "seed_*.json").OrderBy(f => f).ToArray();
    if (files.Length == 0)
    {
        Console.Error.WriteLine($"{folder} 에 seed_*.json 없음. Unity에서 export 먼저.");
        Environment.Exit(1);
    }

    double[] densities = { 1.0, 0.5, 0.25, 0.12 };
    Console.WriteLine($"밀도별 비교 ({files.Length} seeds 평균)");
    Console.WriteLine($"{"density",8} {"setl",6} {"king",6} {"max%",6} {"conq%",6} {"merg%",6} {"mega%",6} {"multi100%",10}");
    Console.WriteLine(new string('─', 60));
    foreach (double dens in densities)
    {
        var all = new List<Stats>();
        foreach (string f in files)
            all.Add(Stats.Compute(RunFile(f, dens)));
        double mega = all.Count(s => s.LargestPct > 0.40) / (double)all.Count;
        double multi = all.Count(s => s.MultiRaceKingdoms >= 1) / (double)all.Count;
        Console.WriteLine(
            $"{dens,8:F2} {all.Average(s => s.Settlements),6:F0} {all.Average(s => s.SurvivingKingdoms),6:F0} " +
            $"{all.Average(s => s.LargestPct),5:P0} {all.Average(s => s.ConqueredPct),5:P0} " +
            $"{all.Average(s => s.MergedPct),5:P0} {mega,5:P0} {multi,9:P0}");
    }
}

void Sweep(string folder, double density)
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
        Simulator sim = RunFile(f, density);
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

// 한 정착지의 NPC 호적부 (L2 backfill 데모)
void Dossier(string file, int sid, double dens)
{
    Simulator sim = RunFile(file, dens);
    if (sid < 0) sid = PickDramatic(sim);
    if (sid < 0) { Console.WriteLine("표시할 활성 정착지가 없습니다."); return; }

    SettlementState s = sim.Get(sid);
    if (s.Fate is Fate.Merged or Fate.Ruined)
    {
        Console.WriteLine($"S#{sid}는 {s.Fate}(소멸) — NPC 없음. 다른 정착지를 고르세요.");
        return;
    }

    VillageDossier d = VillageDossier.Generate(sim, sid);

    Console.WriteLine($"═══ 정착지 S#{d.SettlementId} 호적부 (seed {sim.MapSeed}, density {dens:F2}) ═══");
    Console.WriteLine($"  종족 {d.NativeRace} | biome {d.Biome} | 추상인구 {d.Pop:F0} | 운명 {d.Fate}");
    Console.WriteLine($"  소속: {d.KingdomName}" + (d.KingdomId != -1 ? $" (지배종족 {d.RulingRace})" : ""));
    if (d.ForeignRuled)
        Console.WriteLine($"  ⚔ 타종족({d.RulingRace}) 지배 하 — 피정복지");
    if (d.ConquerorSettlementId >= 0)
        Console.WriteLine($"  나를 정복한 마을: {Label(sim, d.ConquerorSettlementId)}");
    if (d.WarEnemySettlements.Count > 0)
        Console.WriteLine($"  전쟁 이력 상대: {string.Join(", ", d.WarEnemySettlements.Distinct().Take(6).Select(id => Label(sim, id)))}");

    int rulers = d.Npcs.Count(x => x.IsRuler);
    Console.WriteLine($"  스폰 NPC: {d.Npcs.Count}명" + (rulers > 0 ? $" (native {d.Npcs.Count - rulers} / 지배층 {rulers})" : ""));
    Console.WriteLine();

    Console.WriteLine("[NPC 명단]  ★=핵심 NPC");
    foreach (Npc npc in d.Npcs)
    {
        string key = npc.IsKey ? "★" : " ";
        string ruler = npc.IsRuler ? " {지배층}" : "";
        string sent = npc.Sentiments.Count == 0 ? ""
            : "  | " + string.Join("; ", npc.Sentiments.Select(x => FmtSent(sim, x)));
        Console.WriteLine($"  {key} #{npc.LocalId,-2} {npc.Name,-11} ({npc.Race}/{npc.Job}) h{npc.Household}{ruler}{sent}");
    }

    Console.WriteLine();
    Console.WriteLine($"[관계 요약] 총 {d.Relations.Count}  " +
        $"부부 {d.CountRel(RelType.Spouse)} 부모자식 {d.CountRel(RelType.ParentOf)} 형제 {d.CountRel(RelType.Sibling)} " +
        $"사제 {d.CountRel(RelType.MentorOf)} 친구 {d.CountRel(RelType.Friend)} 라이벌 {d.CountRel(RelType.Rival)}");
    var sentCounts = d.Npcs.SelectMany(x => x.Sentiments).GroupBy(x => x.Type).ToDictionary(g => g.Key, g => g.Count());
    Console.WriteLine("[Sentiment 요약] " + (sentCounts.Count == 0 ? "없음"
        : string.Join("  ", sentCounts.Select(kv => $"{kv.Key} {kv.Value}"))));
}

string FmtSent(Simulator sim, Sentiment s)
{
    string tgt = s.TargetSettlementId >= 0 ? $"→{Label(sim, s.TargetSettlementId)}" : "";
    return $"{s.Type}{tgt} \"{s.Note}\"";
}

int PickDramatic(Simulator sim)
{
    int best = -1, bestScore = int.MinValue;
    foreach (SettlementState s in sim.Settlements)
    {
        if (s.Active == false) continue;
        Kingdom? k = sim.Kingdoms.FirstOrDefault(x => x.Id == s.KingdomId);
        int score = 0;
        if (s.Fate == Fate.Conquered && k != null && k.FoundingRace != s.Race) score += 20;
        score += sim.Events.Count(e => (e.Type == EventType.War || e.Type == EventType.Conquer)
            && (e.SubjectId == s.Id || e.ObjectId == s.Id));
        if (k != null) score += 2;
        if (score > bestScore) { bestScore = score; best = s.Id; }
    }
    return best;
}

// 세계 전체 마을 분류 + NPC/관계 생성량 집계
void Villages(string file, double dens)
{
    Simulator sim = RunFile(file, dens);
    int conqForeign = 0, warScarred = 0, kingdomPeace = 0, lone = 0, active = 0;
    int totalNpc = 0, totalRel = 0;
    var sentTotals = new Dictionary<SentType, int>();

    foreach (SettlementState s in sim.Settlements)
    {
        if (s.Active == false) continue;
        active++;
        VillageDossier d = VillageDossier.Generate(sim, s.Id);
        totalNpc += d.Npcs.Count;
        totalRel += d.Relations.Count;
        foreach (Sentiment snt in d.Npcs.SelectMany(x => x.Sentiments))
            sentTotals[snt.Type] = sentTotals.GetValueOrDefault(snt.Type) + 1;

        if (d.ForeignRuled) conqForeign++;
        else if (d.WarEnemySettlements.Count > 0) warScarred++;
        else if (d.KingdomId != -1) kingdomPeace++;
        else lone++;
    }

    string Pct(int a) => active == 0 ? "0%" : $"{100.0 * a / active:F0}%";
    Console.WriteLine($"=== 마을 분류 (seed {sim.MapSeed}, density {dens:F2}, 활성 {active}개) ===");
    Console.WriteLine($"  피정복(타종족 지배) ... {conqForeign,4}  {Pct(conqForeign)}  → ResentsKingdom/유족 서사");
    Console.WriteLine($"  전쟁 상흔 ............. {warScarred,4}  {Pct(warScarred)}  → LostFamilyToWar/원한");
    Console.WriteLine($"  왕국 소속(평시) ....... {kingdomPeace,4}  {Pct(kingdomPeace)}  → ProudOfKingdom");
    Console.WriteLine($"  독립/외딴 ............. {lone,4}  {Pct(lone)}  → 내부 관계만");
    Console.WriteLine();
    Console.WriteLine($"  생성 NPC 총 {totalNpc}  (마을당 평균 {(active == 0 ? 0 : (double)totalNpc / active):F1}, 상한 50)");
    Console.WriteLine($"  생성 관계 총 {totalRel}  (NPC당 평균 {(totalNpc == 0 ? 0 : (double)totalRel / totalNpc):F1})");
    Console.WriteLine("  Sentiment 총: " + (sentTotals.Count == 0 ? "없음"
        : string.Join("  ", sentTotals.Select(kv => $"{kv.Key} {kv.Value}"))));
}

Simulator RunFile(string file, double density)
{
    PlacementFile pf = PlacementFile.Load(file);
    var sim = new Simulator(pf, density);
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
