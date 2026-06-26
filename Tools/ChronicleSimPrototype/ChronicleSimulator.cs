namespace ChronicleSim;

public enum Fate { Alive, Ruined, Merged, Conquered }

public enum EventType { Trade, Skirmish, War, Conquer, Alliance, Merge, Ruin, KingdomFound, KingdomJoin }

public sealed class ChronicleEvent
{
    public int EventId;
    public int Year;
    public EventType Type;
    public int SubjectId;
    public int ObjectId = -1;
    public int KingdomId = -1;
}

public sealed class Kingdom
{
    public int Id;
    public string Name = "";
    public Race FoundingRace;
    public int CapitalId;
    public int FoundedYear;
    public HashSet<int> Members = new();
}

public sealed class SettlementState
{
    public int Id;
    public Race Race;
    public Biome Biome;
    public int RegionX, RegionY;
    public int WorldX, WorldY;
    public double Pop;
    public int KingdomId = -1;
    public Fate Fate = Fate.Alive;
    public int MergedIntoId = -1;
    public int ConqueredByKingdomId = -1;

    public bool Active => Fate is Fate.Alive or Fate.Conquered;
    public bool Gone => Fate is Fate.Merged or Fate.Ruined;
}

/// <summary>
/// 정착지 연대기 시뮬 (설계 L1, Tier 2). New Game 1회분 = 1 placement 파일 = 1 시드.
/// 결정론: DerivedSeed(mapSeed, ChannelChronicle)로 시드한 단일 SplitMix64 스트림 +
/// 고정 순회 순서 → 같은 입력은 항상 같은 역사.
/// </summary>
public sealed class Simulator
{
    public int MapSeed { get; }
    public List<ChronicleEvent> Events { get; } = new();
    public double AvgNeighbors { get; private set; }

    private readonly List<SettlementState> _s = new();
    private readonly Dictionary<int, int> _idToIndex = new();
    private readonly List<(int a, int b)> _neighbors = new();
    private readonly Dictionary<long, int> _aff = new();
    private readonly Dictionary<int, HashSet<int>> _alliedAdj = new();
    private readonly Dictionary<int, Kingdom> _kingdoms = new();
    private readonly Dictionary<Race, int> _nameCounter = new();

    private SplitMix64 _rng = null!;
    private int _nextEventId;
    private int _nextKingdomId;

    public IReadOnlyList<SettlementState> Settlements => _s;
    public IReadOnlyCollection<Kingdom> Kingdoms => _kingdoms.Values;

    /// <param name="density">[0,1] 정착지 유지 비율 — 결정론적으로 솎아 배치 밀도 축소 실험.</param>
    public Simulator(PlacementFile file, double density = 1.0)
    {
        MapSeed = file.MapSeed;
        for (int i = 0; i < file.Settlements.Count; i++)
        {
            PlacementSettlement p = file.Settlements[i];
            if (density < 1.0 && KeepRoll(p.Id, MapSeed) >= density)
                continue;
            Biome biome = (Biome)p.Biome;
            var s = new SettlementState
            {
                Id = p.Id,
                Race = (Race)p.Race,
                Biome = biome,
                RegionX = p.RegionX,
                RegionY = p.RegionY,
                WorldX = p.WorldX,
                WorldY = p.WorldY,
                Pop = BiomeData.InitialPopulation(biome),
            };
            _idToIndex[s.Id] = _s.Count;
            _s.Add(s);
        }
    }

    public SettlementState Get(int id) => _s[_idToIndex[id]];

    // 정착지별 결정론 유지 확률 [0,1) — 밀도 솎기용 (id별 독립, 시드 의존)
    private static double KeepRoll(int id, int mapSeed)
    {
        ulong h = DerivedSeed.Derive(unchecked(id * 73856093) ^ mapSeed, 0x0D17D17D17D17D17UL);
        return (h >> 11) * (1.0 / 9007199254740992.0);
    }

    public void Run()
    {
        _rng = new SplitMix64(DerivedSeed.Derive(MapSeed, DerivedSeed.ChannelChronicle));

        // 초기 인구 노이즈 (±20%)
        foreach (SettlementState s in _s)
            s.Pop = Math.Max(1, s.Pop * (0.8 + 0.4 * _rng.NextDouble()));

        BuildNeighbors();

        for (int year = 1; year <= SimConfig.SimYears; year++)
        {
            CurrentYear = year;
            GrowthPhase();
            PairPhase(year);
            KingdomPhase(year);
        }
    }

    // ────────────────────────── 초기화 ──────────────────────────

    private void BuildNeighbors()
    {
        int d2 = SimConfig.NeighborRegionDist * SimConfig.NeighborRegionDist;
        int degreeSum = 0;
        for (int i = 0; i < _s.Count; i++)
        {
            for (int j = i + 1; j < _s.Count; j++)
            {
                int dx = _s[i].RegionX - _s[j].RegionX;
                int dy = _s[i].RegionY - _s[j].RegionY;
                if (dx * dx + dy * dy > d2)
                    continue;

                _neighbors.Add((_s[i].Id, _s[j].Id));
                int baseAff = Math.Clamp(RaceRelations.BaseAffinity(_s[i].Race, _s[j].Race),
                    SimConfig.AffMin, SimConfig.AffMax);
                _aff[Key(_s[i].Id, _s[j].Id)] = baseAff;
                degreeSum += 2;
            }
        }
        AvgNeighbors = _s.Count == 0 ? 0 : (double)degreeSum / _s.Count;
    }

    // ────────────────────────── 틱 단계 ──────────────────────────

    private void GrowthPhase()
    {
        foreach (SettlementState s in _s)
        {
            if (s.Active == false)
                continue;
            double rate = BiomeData.GrowthRate(s.Biome);
            double carry = BiomeData.Carry(s.Biome);
            double g = s.Pop * rate * (1.0 - s.Pop / carry) + 1.0 + _rng.Noise();
            s.Pop = Math.Max(0, s.Pop + g);
        }
    }

    private void PairPhase(int year)
    {
        for (int n = 0; n < _neighbors.Count; n++)
        {
            SettlementState a = Get(_neighbors[n].a);
            SettlementState b = Get(_neighbors[n].b);
            if (a.Active == false || b.Active == false)
                continue;
            // 같은 왕국이면 분쟁/합병 없음 (v1: 내전 없음)
            if (a.KingdomId != -1 && a.KingdomId == b.KingdomId)
                continue;
            RollPair(a, b, year);
        }
    }

    private void RollPair(SettlementState a, SettlementState b, int year)
    {
        int aff = GetAff(a.Id, b.Id);

        if (aff >= SimConfig.AllianceThreshold)
        {
            if (MarkAllied(a.Id, b.Id))
                AddEvent(EventType.Alliance, a.Id, b.Id);
            AddAff(a.Id, b.Id, SimConfig.AffAllianceTick);
            TryMerge(a, b, year);
            if (_rng.Chance(SimConfig.PTrade))
                DoTrade(a, b);
        }
        else if (aff >= SimConfig.TradeThreshold)
        {
            if (_rng.Chance(SimConfig.PTrade))
                DoTrade(a, b);
        }
        else if (aff <= SimConfig.WarThreshold)
        {
            if (_rng.Chance(SimConfig.PWar))
                DoWar(a, b, year);
        }
        else if (aff <= SimConfig.SkirmishThreshold)
        {
            if (_rng.Chance(SimConfig.PSkirmish))
                DoSkirmish(a, b);
        }
        else
        {
            // 중립 band: 드문 소폭 드리프트
            if (_rng.Chance(0.10))
                AddAff(a.Id, b.Id, _rng.Chance(0.5) ? 2 : -2);
        }
    }

    private void KingdomPhase(int year)
    {
        DetectNewKingdoms(year);
        KingdomJoin(year);
    }

    // ────────────────────────── event 처리 ──────────────────────────

    private void DoTrade(SettlementState a, SettlementState b)
    {
        AddAff(a.Id, b.Id, SimConfig.AffTrade);
        a.Pop += a.Pop * 0.01;
        b.Pop += b.Pop * 0.01;
        AddEvent(EventType.Trade, a.Id, b.Id);
    }

    private void DoSkirmish(SettlementState a, SettlementState b)
    {
        AddAff(a.Id, b.Id, SimConfig.AffSkirmish);
        a.Pop = Math.Max(0, a.Pop - (a.Pop * 0.01 + 1));
        b.Pop = Math.Max(0, b.Pop - (b.Pop * 0.01 + 1));
        AddEvent(EventType.Skirmish, a.Id, b.Id);
    }

    private void TryMerge(SettlementState a, SettlementState b, int year)
    {
        if (RaceRelations.MergeAllowed(a.Race, b.Race) == false)
            return;
        SettlementState larger = a.Pop >= b.Pop ? a : b;
        SettlementState smaller = larger == a ? b : a;
        if (smaller.Pop <= 0)
            return;
        if (larger.Pop < SimConfig.MergeRatio * smaller.Pop)
            return;

        double oe = larger.KingdomId != -1 ? SimConfig.Overextension(KingdomSize(larger.KingdomId)) : 1.0;
        if (_rng.Chance(SimConfig.PMerge * oe) == false)
            return;

        smaller.Fate = Fate.Merged;
        smaller.MergedIntoId = larger.Id;
        larger.Pop += smaller.Pop * SimConfig.MergeAbsorb;
        smaller.Pop = 0;
        RemoveFromKingdom(smaller);
        RemoveAlliedEdges(smaller.Id);
        AddEvent(EventType.Merge, smaller.Id, larger.Id, larger.KingdomId);
    }

    private void DoWar(SettlementState a, SettlementState b, int year)
    {
        AddAff(a.Id, b.Id, SimConfig.AffWar);

        double sa = a.Pop * (0.8 + 0.4 * _rng.NextDouble());
        double sb = b.Pop * (0.8 + 0.4 * _rng.NextDouble());
        SettlementState winner = sa >= sb ? a : b;
        SettlementState loser = winner == a ? b : a;

        loser.Pop = Math.Max(0, loser.Pop * (1.0 - SimConfig.WarLoserLoss));
        winner.Pop = Math.Max(0, winner.Pop * (1.0 - SimConfig.WarWinnerLoss));
        AddEvent(EventType.War, winner.Id, loser.Id);

        double oe = winner.KingdomId != -1 ? SimConfig.Overextension(KingdomSize(winner.KingdomId)) : 1.0;
        if (loser.Pop > SimConfig.RuinPopFloor
            && winner.Pop >= SimConfig.ConquerRatio * loser.Pop
            && _rng.Chance(oe))
        {
            DoConquer(winner, loser, year);
        }
        else if (loser.Pop <= SimConfig.RuinPopFloor)
        {
            DoRuin(loser, year);
        }
    }

    private void DoConquer(SettlementState winner, SettlementState loser, int year)
    {
        int kid = EnsureKingdom(winner, year);
        RemoveFromKingdom(loser);
        loser.Fate = Fate.Conquered;
        loser.ConqueredByKingdomId = kid;
        loser.KingdomId = kid;
        _kingdoms[kid].Members.Add(loser.Id);
        RemoveAlliedEdges(loser.Id);
        AddEvent(EventType.Conquer, loser.Id, winner.Id, kid);
    }

    private void DoRuin(SettlementState loser, int year)
    {
        loser.Fate = Fate.Ruined;
        loser.Pop = 0;
        RemoveFromKingdom(loser);
        RemoveAlliedEdges(loser.Id);
        AddEvent(EventType.Ruin, loser.Id);
    }

    // ────────────────────────── 왕국 ──────────────────────────

    private void DetectNewKingdoms(int year)
    {
        var visited = new HashSet<int>();
        foreach (SettlementState s in _s)
        {
            if (s.Active == false || s.KingdomId != -1 || visited.Contains(s.Id))
                continue;
            if (_alliedAdj.TryGetValue(s.Id, out HashSet<int>? adj) == false || adj.Count == 0)
                continue;

            // 동맹 그래프 BFS — 아직 무소속 + 활성인 노드만
            var component = new List<int>();
            var queue = new Queue<int>();
            queue.Enqueue(s.Id);
            visited.Add(s.Id);
            while (queue.Count > 0)
            {
                int cur = queue.Dequeue();
                component.Add(cur);
                if (_alliedAdj.TryGetValue(cur, out HashSet<int>? nbrs) == false)
                    continue;
                foreach (int nb in nbrs)
                {
                    if (visited.Contains(nb))
                        continue;
                    SettlementState ns = Get(nb);
                    if (ns.Active == false || ns.KingdomId != -1)
                        continue;
                    visited.Add(nb);
                    queue.Enqueue(nb);
                }
            }

            if (component.Count >= SimConfig.KingdomMinCluster)
                FoundKingdom(component, year);
        }
    }

    private void FoundKingdom(List<int> memberIds, int year)
    {
        int capitalId = memberIds[0];
        double bestPop = -1;
        foreach (int id in memberIds)
        {
            double p = Get(id).Pop;
            if (p > bestPop)
            {
                bestPop = p;
                capitalId = id;
            }
        }

        SettlementState capital = Get(capitalId);
        int kid = _nextKingdomId++;
        var k = new Kingdom
        {
            Id = kid,
            FoundingRace = capital.Race,
            CapitalId = capitalId,
            FoundedYear = year,
            Name = MakeName(capital.Race),
        };
        foreach (int id in memberIds)
        {
            Get(id).KingdomId = kid;
            k.Members.Add(id);
        }
        _kingdoms[kid] = k;
        AddEvent(EventType.KingdomFound, capitalId, -1, kid);
    }

    private int EnsureKingdom(SettlementState seed, int year)
    {
        if (seed.KingdomId != -1)
            return seed.KingdomId;
        int kid = _nextKingdomId++;
        var k = new Kingdom
        {
            Id = kid,
            FoundingRace = seed.Race,
            CapitalId = seed.Id,
            FoundedYear = year,
            Name = MakeName(seed.Race),
        };
        k.Members.Add(seed.Id);
        seed.KingdomId = kid;
        _kingdoms[kid] = k;
        AddEvent(EventType.KingdomFound, seed.Id, -1, kid);
        return kid;
    }

    private void KingdomJoin(int year)
    {
        foreach (SettlementState s in _s)
        {
            if (s.Active == false || s.KingdomId != -1)
                continue;
            if (_alliedAdj.Count == 0 && _kingdoms.Count == 0)
                break;

            int targetKingdom = -1;
            int bestAff = SimConfig.JoinThreshold - 1;
            // 이웃 중 왕국 소속 + 우호 높은 곳 탐색
            for (int n = 0; n < _neighbors.Count; n++)
            {
                (int a, int b) = _neighbors[n];
                int other;
                if (a == s.Id) other = b;
                else if (b == s.Id) other = a;
                else continue;

                SettlementState os = Get(other);
                if (os.Active == false || os.KingdomId == -1)
                    continue;
                int aff = GetAff(s.Id, other);
                if (aff > bestAff)
                {
                    bestAff = aff;
                    targetKingdom = os.KingdomId;
                }
            }

            if (targetKingdom == -1)
                continue;
            double oe = SimConfig.Overextension(KingdomSize(targetKingdom));
            if (_rng.Chance(oe) == false)
                continue;

            s.KingdomId = targetKingdom;
            _kingdoms[targetKingdom].Members.Add(s.Id);
            AddEvent(EventType.KingdomJoin, s.Id, _kingdoms[targetKingdom].CapitalId, targetKingdom);
        }
    }

    private int KingdomSize(int kid) => _kingdoms.TryGetValue(kid, out Kingdom? k) ? k.Members.Count : 0;

    private void RemoveFromKingdom(SettlementState s)
    {
        if (s.KingdomId == -1)
            return;
        if (_kingdoms.TryGetValue(s.KingdomId, out Kingdom? k))
        {
            k.Members.Remove(s.Id);
            if (k.CapitalId == s.Id && k.Members.Count > 0)
            {
                // 수도 상실 → 잔존 멤버 중 최대 인구로 재지정
                int newCap = k.Members.First();
                double best = -1;
                foreach (int id in k.Members)
                {
                    double p = Get(id).Pop;
                    if (p > best) { best = p; newCap = id; }
                }
                k.CapitalId = newCap;
            }
        }
        s.KingdomId = -1;
    }

    private string MakeName(Race race)
    {
        int n = _nameCounter.TryGetValue(race, out int c) ? c + 1 : 1;
        _nameCounter[race] = n;
        return $"{race} Kingdom {n}";
    }

    // ────────────────────────── 호감도 / 동맹 ──────────────────────────

    private static long Key(int a, int b)
    {
        if (a > b) (a, b) = (b, a);
        return ((long)a << 32) | (uint)b;
    }

    private int GetAff(int a, int b) => _aff.TryGetValue(Key(a, b), out int v) ? v : 0;

    private void AddAff(int a, int b, int delta)
    {
        long k = Key(a, b);
        int v = _aff.TryGetValue(k, out int cur) ? cur : 0;
        _aff[k] = Math.Clamp(v + delta, SimConfig.AffMin, SimConfig.AffMax);
    }

    private bool MarkAllied(int a, int b)
    {
        if (_alliedAdj.TryGetValue(a, out HashSet<int>? sa) == false)
            _alliedAdj[a] = sa = new HashSet<int>();
        if (_alliedAdj.TryGetValue(b, out HashSet<int>? sb) == false)
            _alliedAdj[b] = sb = new HashSet<int>();
        bool isNew = sa.Add(b);
        sb.Add(a);
        return isNew;
    }

    private void RemoveAlliedEdges(int id)
    {
        if (_alliedAdj.TryGetValue(id, out HashSet<int>? adj) == false)
            return;
        foreach (int other in adj)
            if (_alliedAdj.TryGetValue(other, out HashSet<int>? oa))
                oa.Remove(id);
        adj.Clear();
    }

    // ────────────────────────── event 기록 ──────────────────────────

    private void AddEvent(EventType type, int subject, int obj = -1, int kingdom = -1)
        => Events.Add(new ChronicleEvent
        {
            EventId = _nextEventId++,
            Year = CurrentYear,
            Type = type,
            SubjectId = subject,
            ObjectId = obj,
            KingdomId = kingdom,
        });

    // AddEvent에서 연도를 알기 위해 매 연도 시작 시 Run()에서 갱신
    private int CurrentYear;
}
