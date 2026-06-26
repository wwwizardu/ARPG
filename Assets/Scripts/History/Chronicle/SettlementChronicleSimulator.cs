using System;
using System.Collections.Generic;
using System.Linq;
using ARPG.Map;

namespace ARPG.History
{
    /// <summary>
    /// L1 정착지 연대기 시뮬 (설계 Tier 2: 정복-병합 + 과확장 페널티).
    /// New Game 1회분 = 1 placement 목록 = 1 mapSeed. **저장이 정본** (재생성하지 않음).
    /// 결정론: DerivedSeed(mapSeed, ChannelChronicle)로 시드한 단일 HistoryRng 스트림 +
    /// 고정 순회 순서(정착지/페어는 List, 출력은 Id 정렬) → 같은 입력 = 같은 역사.
    /// 검증 프로토타입: Tools/ChronicleSimPrototype/ChronicleSimulator.cs.
    /// 설계: PROCEDURAL_HISTORY_DESIGN.md §3
    /// </summary>
    public sealed class SettlementChronicleSimulator
    {
        private sealed class SimKingdom
        {
            public int Id;
            public string Name = string.Empty;
            public GlobalEnum.RaceType FoundingRace;
            public int CapitalId;
            public int FoundedYear;
            public HashSet<int> Members = new();
        }

        public int MapSeed { get; }

        private readonly List<SettlementSimState> _s = new();
        private readonly Dictionary<int, int> _idToIndex = new();
        private readonly List<(int a, int b)> _neighbors = new();
        private readonly Dictionary<long, int> _aff = new();
        private readonly Dictionary<int, HashSet<int>> _alliedAdj = new();
        private readonly Dictionary<int, SimKingdom> _kingdoms = new();
        private readonly Dictionary<GlobalEnum.RaceType, int> _nameCounter = new();
        private readonly List<ChronicleEvent> _events = new();

        private HistoryRng _rng;
        private int _nextEventId;
        private int _nextKingdomId;
        private int _currentYear;

        /// <summary>
        /// 정착지 배치 목록으로 연대기를 생성한다. SettlementId = placement 인덱스(ComputeAll 순서).
        /// </summary>
        public static ChronicleSaveData Simulate(int mapSeed, List<RaceSettlementPlacer.SettlementPlacement> placements,
            int years = SettlementSimConfig.SimYears)
        {
            var sim = new SettlementChronicleSimulator(mapSeed, placements);
            sim.Run(years);
            return sim.BuildSaveData(years);
        }

        private SettlementChronicleSimulator(int mapSeed, List<RaceSettlementPlacer.SettlementPlacement> placements)
        {
            MapSeed = mapSeed;
            if (placements == null)
                return;

            for (int i = 0; i < placements.Count; i++)
            {
                RaceSettlementPlacer.SettlementPlacement p = placements[i];
                GlobalEnum.BiomeType biome = p.ChunkBiome;   // export/검증과 동일 — settlement 위치 청크 biome
                var s = new SettlementSimState
                {
                    SettlementId = i,                        // 결정론 ID = 배치 인덱스
                    Race = p.Race,
                    Biome = biome,
                    RegionX = p.RegionCoord.x,
                    RegionY = p.RegionCoord.y,
                    WorldX = p.WorldCoord.x,
                    WorldY = p.WorldCoord.y,
                    Pop = BiomeData.InitialPopulation(biome),
                };
                _idToIndex[s.SettlementId] = _s.Count;
                _s.Add(s);
            }
        }

        private SettlementSimState Get(int id) => _s[_idToIndex[id]];

        private void Run(int years)
        {
            _rng = new HistoryRng(DerivedSeed.Derive(MapSeed, DerivedSeed.ChannelChronicle));

            // 초기 인구 노이즈 (±20%)
            for (int i = 0; i < _s.Count; i++)
                _s[i].Pop = Math.Max(1, _s[i].Pop * (0.8 + 0.4 * _rng.NextDouble()));

            BuildNeighbors();

            for (int year = 1; year <= years; year++)
            {
                _currentYear = year;
                GrowthPhase();
                PairPhase(year);
                KingdomPhase(year);
            }
        }

        // ────────────────────────── 초기화 ──────────────────────────

        private void BuildNeighbors()
        {
            int d2 = SettlementSimConfig.NeighborRegionDist * SettlementSimConfig.NeighborRegionDist;
            for (int i = 0; i < _s.Count; i++)
            {
                for (int j = i + 1; j < _s.Count; j++)
                {
                    int dx = _s[i].RegionX - _s[j].RegionX;
                    int dy = _s[i].RegionY - _s[j].RegionY;
                    if (dx * dx + dy * dy > d2)
                        continue;

                    _neighbors.Add((_s[i].SettlementId, _s[j].SettlementId));
                    int baseAff = Math.Clamp(RaceRelations.BaseAffinity(_s[i].Race, _s[j].Race),
                        SettlementSimConfig.AffMin, SettlementSimConfig.AffMax);
                    _aff[Key(_s[i].SettlementId, _s[j].SettlementId)] = baseAff;
                }
            }
        }

        // ────────────────────────── 틱 단계 ──────────────────────────

        private void GrowthPhase()
        {
            for (int i = 0; i < _s.Count; i++)
            {
                SettlementSimState s = _s[i];
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
                SettlementSimState a = Get(_neighbors[n].a);
                SettlementSimState b = Get(_neighbors[n].b);
                if (a.Active == false || b.Active == false)
                    continue;
                // 같은 왕국이면 분쟁/합병 없음 (v1: 내전 없음)
                if (a.KingdomId != -1 && a.KingdomId == b.KingdomId)
                    continue;
                RollPair(a, b, year);
            }
        }

        private void RollPair(SettlementSimState a, SettlementSimState b, int year)
        {
            int aff = GetAff(a.SettlementId, b.SettlementId);

            if (aff >= SettlementSimConfig.AllianceThreshold)
            {
                if (MarkAllied(a.SettlementId, b.SettlementId))
                    AddEvent(ChronicleEventType.Alliance, a.SettlementId, b.SettlementId);
                AddAff(a.SettlementId, b.SettlementId, SettlementSimConfig.AffAllianceTick);
                TryMerge(a, b, year);
                if (_rng.Chance(SettlementSimConfig.PTrade))
                    DoTrade(a, b);
            }
            else if (aff >= SettlementSimConfig.TradeThreshold)
            {
                if (_rng.Chance(SettlementSimConfig.PTrade))
                    DoTrade(a, b);
            }
            else if (aff <= SettlementSimConfig.WarThreshold)
            {
                if (_rng.Chance(SettlementSimConfig.PWar))
                    DoWar(a, b, year);
            }
            else if (aff <= SettlementSimConfig.SkirmishThreshold)
            {
                if (_rng.Chance(SettlementSimConfig.PSkirmish))
                    DoSkirmish(a, b);
            }
            else
            {
                // 중립 band: 드문 소폭 드리프트
                if (_rng.Chance(0.10))
                    AddAff(a.SettlementId, b.SettlementId, _rng.Chance(0.5) ? 2 : -2);
            }
        }

        private void KingdomPhase(int year)
        {
            DetectNewKingdoms(year);
            KingdomJoin(year);
        }

        // ────────────────────────── event 처리 ──────────────────────────

        private void DoTrade(SettlementSimState a, SettlementSimState b)
        {
            AddAff(a.SettlementId, b.SettlementId, SettlementSimConfig.AffTrade);
            a.Pop += a.Pop * 0.01;
            b.Pop += b.Pop * 0.01;
            AddEvent(ChronicleEventType.Trade, a.SettlementId, b.SettlementId);
        }

        private void DoSkirmish(SettlementSimState a, SettlementSimState b)
        {
            AddAff(a.SettlementId, b.SettlementId, SettlementSimConfig.AffSkirmish);
            a.Pop = Math.Max(0, a.Pop - (a.Pop * 0.01 + 1));
            b.Pop = Math.Max(0, b.Pop - (b.Pop * 0.01 + 1));
            AddEvent(ChronicleEventType.Skirmish, a.SettlementId, b.SettlementId);
        }

        private void TryMerge(SettlementSimState a, SettlementSimState b, int year)
        {
            if (RaceRelations.MergeAllowed(a.Race, b.Race) == false)
                return;
            SettlementSimState larger = a.Pop >= b.Pop ? a : b;
            SettlementSimState smaller = larger == a ? b : a;
            if (smaller.Pop <= 0)
                return;
            if (larger.Pop < SettlementSimConfig.MergeRatio * smaller.Pop)
                return;

            double oe = larger.KingdomId != -1 ? SettlementSimConfig.Overextension(KingdomSize(larger.KingdomId)) : 1.0;
            if (_rng.Chance(SettlementSimConfig.PMerge * oe) == false)
                return;

            smaller.Fate = SettlementFate.Merged;
            smaller.MergedIntoId = larger.SettlementId;
            larger.Pop += smaller.Pop * SettlementSimConfig.MergeAbsorb;
            smaller.Pop = 0;
            RemoveFromKingdom(smaller);
            RemoveAlliedEdges(smaller.SettlementId);
            AddEvent(ChronicleEventType.Merge, smaller.SettlementId, larger.SettlementId, larger.KingdomId);
        }

        private void DoWar(SettlementSimState a, SettlementSimState b, int year)
        {
            AddAff(a.SettlementId, b.SettlementId, SettlementSimConfig.AffWar);

            double sa = a.Pop * (0.8 + 0.4 * _rng.NextDouble());
            double sb = b.Pop * (0.8 + 0.4 * _rng.NextDouble());
            SettlementSimState winner = sa >= sb ? a : b;
            SettlementSimState loser = winner == a ? b : a;

            loser.Pop = Math.Max(0, loser.Pop * (1.0 - SettlementSimConfig.WarLoserLoss));
            winner.Pop = Math.Max(0, winner.Pop * (1.0 - SettlementSimConfig.WarWinnerLoss));
            AddEvent(ChronicleEventType.War, winner.SettlementId, loser.SettlementId);

            double oe = winner.KingdomId != -1 ? SettlementSimConfig.Overextension(KingdomSize(winner.KingdomId)) : 1.0;
            if (loser.Pop > SettlementSimConfig.RuinPopFloor
                && winner.Pop >= SettlementSimConfig.ConquerRatio * loser.Pop
                && _rng.Chance(oe))
            {
                DoConquer(winner, loser, year);
            }
            else if (loser.Pop <= SettlementSimConfig.RuinPopFloor)
            {
                DoRuin(loser, year);
            }
        }

        private void DoConquer(SettlementSimState winner, SettlementSimState loser, int year)
        {
            int kid = EnsureKingdom(winner, year);
            RemoveFromKingdom(loser);
            loser.Fate = SettlementFate.Conquered;
            loser.ConqueredByKingdomId = kid;
            loser.KingdomId = kid;
            _kingdoms[kid].Members.Add(loser.SettlementId);
            RemoveAlliedEdges(loser.SettlementId);
            AddEvent(ChronicleEventType.Conquer, loser.SettlementId, winner.SettlementId, kid);
        }

        private void DoRuin(SettlementSimState loser, int year)
        {
            loser.Fate = SettlementFate.Ruined;
            loser.Pop = 0;
            RemoveFromKingdom(loser);
            RemoveAlliedEdges(loser.SettlementId);
            AddEvent(ChronicleEventType.Ruin, loser.SettlementId);
        }

        // ────────────────────────── 왕국 ──────────────────────────

        private void DetectNewKingdoms(int year)
        {
            var visited = new HashSet<int>();
            for (int i = 0; i < _s.Count; i++)
            {
                SettlementSimState s = _s[i];
                if (s.Active == false || s.KingdomId != -1 || visited.Contains(s.SettlementId))
                    continue;
                if (_alliedAdj.TryGetValue(s.SettlementId, out HashSet<int> adj) == false || adj.Count == 0)
                    continue;

                // 동맹 그래프 BFS — 아직 무소속 + 활성인 노드만
                var component = new List<int>();
                var queue = new Queue<int>();
                queue.Enqueue(s.SettlementId);
                visited.Add(s.SettlementId);
                while (queue.Count > 0)
                {
                    int cur = queue.Dequeue();
                    component.Add(cur);
                    if (_alliedAdj.TryGetValue(cur, out HashSet<int> nbrs) == false)
                        continue;
                    foreach (int nb in nbrs)
                    {
                        if (visited.Contains(nb))
                            continue;
                        SettlementSimState ns = Get(nb);
                        if (ns.Active == false || ns.KingdomId != -1)
                            continue;
                        visited.Add(nb);
                        queue.Enqueue(nb);
                    }
                }

                if (component.Count >= SettlementSimConfig.KingdomMinCluster)
                    FoundKingdom(component, year);
            }
        }

        private void FoundKingdom(List<int> memberIds, int year)
        {
            int capitalId = memberIds[0];
            double bestPop = -1;
            for (int i = 0; i < memberIds.Count; i++)
            {
                double p = Get(memberIds[i]).Pop;
                if (p > bestPop)
                {
                    bestPop = p;
                    capitalId = memberIds[i];
                }
            }

            SettlementSimState capital = Get(capitalId);
            int kid = _nextKingdomId++;
            var k = new SimKingdom
            {
                Id = kid,
                FoundingRace = capital.Race,
                CapitalId = capitalId,
                FoundedYear = year,
                Name = MakeName(capital.Race),
            };
            for (int i = 0; i < memberIds.Count; i++)
            {
                Get(memberIds[i]).KingdomId = kid;
                k.Members.Add(memberIds[i]);
            }
            _kingdoms[kid] = k;
            AddEvent(ChronicleEventType.KingdomFound, capitalId, -1, kid);
        }

        private int EnsureKingdom(SettlementSimState seed, int year)
        {
            if (seed.KingdomId != -1)
                return seed.KingdomId;
            int kid = _nextKingdomId++;
            var k = new SimKingdom
            {
                Id = kid,
                FoundingRace = seed.Race,
                CapitalId = seed.SettlementId,
                FoundedYear = year,
                Name = MakeName(seed.Race),
            };
            k.Members.Add(seed.SettlementId);
            seed.KingdomId = kid;
            _kingdoms[kid] = k;
            AddEvent(ChronicleEventType.KingdomFound, seed.SettlementId, -1, kid);
            return kid;
        }

        private void KingdomJoin(int year)
        {
            for (int i = 0; i < _s.Count; i++)
            {
                SettlementSimState s = _s[i];
                if (s.Active == false || s.KingdomId != -1)
                    continue;

                int targetKingdom = -1;
                int bestAff = SettlementSimConfig.JoinThreshold - 1;
                // 이웃 중 왕국 소속 + 우호 높은 곳 탐색
                for (int n = 0; n < _neighbors.Count; n++)
                {
                    (int a, int b) = _neighbors[n];
                    int other;
                    if (a == s.SettlementId) other = b;
                    else if (b == s.SettlementId) other = a;
                    else continue;

                    SettlementSimState os = Get(other);
                    if (os.Active == false || os.KingdomId == -1)
                        continue;
                    int aff = GetAff(s.SettlementId, other);
                    if (aff > bestAff)
                    {
                        bestAff = aff;
                        targetKingdom = os.KingdomId;
                    }
                }

                if (targetKingdom == -1)
                    continue;
                double oe = SettlementSimConfig.Overextension(KingdomSize(targetKingdom));
                if (_rng.Chance(oe) == false)
                    continue;

                s.KingdomId = targetKingdom;
                _kingdoms[targetKingdom].Members.Add(s.SettlementId);
                AddEvent(ChronicleEventType.KingdomJoin, s.SettlementId, _kingdoms[targetKingdom].CapitalId, targetKingdom);
            }
        }

        private int KingdomSize(int kid) => _kingdoms.TryGetValue(kid, out SimKingdom k) ? k.Members.Count : 0;

        private void RemoveFromKingdom(SettlementSimState s)
        {
            if (s.KingdomId == -1)
                return;
            if (_kingdoms.TryGetValue(s.KingdomId, out SimKingdom k))
            {
                k.Members.Remove(s.SettlementId);
                if (k.CapitalId == s.SettlementId && k.Members.Count > 0)
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

        private string MakeName(GlobalEnum.RaceType race)
        {
            int n = _nameCounter.TryGetValue(race, out int c) ? c + 1 : 1;
            _nameCounter[race] = n;
            return $"{race} 왕국 {n}";
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
            _aff[k] = Math.Clamp(v + delta, SettlementSimConfig.AffMin, SettlementSimConfig.AffMax);
        }

        private bool MarkAllied(int a, int b)
        {
            if (_alliedAdj.TryGetValue(a, out HashSet<int> sa) == false)
                _alliedAdj[a] = sa = new HashSet<int>();
            if (_alliedAdj.TryGetValue(b, out HashSet<int> sb) == false)
                _alliedAdj[b] = sb = new HashSet<int>();
            bool isNew = sa.Add(b);
            sb.Add(a);
            return isNew;
        }

        private void RemoveAlliedEdges(int id)
        {
            if (_alliedAdj.TryGetValue(id, out HashSet<int> adj) == false)
                return;
            foreach (int other in adj)
                if (_alliedAdj.TryGetValue(other, out HashSet<int> oa))
                    oa.Remove(id);
            adj.Clear();
        }

        // ────────────────────────── event 기록 ──────────────────────────

        private void AddEvent(ChronicleEventType type, int subject, int obj = -1, int kingdom = -1)
            => _events.Add(new ChronicleEvent
            {
                EventId = _nextEventId++,
                Year = _currentYear,
                Type = type,
                SubjectSettlementId = subject,
                ObjectSettlementId = obj,
                KingdomId = kingdom,
            });

        // ────────────────────────── 출력 (결정론 정렬) ──────────────────────────

        private ChronicleSaveData BuildSaveData(int years)
        {
            var save = new ChronicleSaveData
            {
                Version = 1,
                SimulatedYears = years,
                Events = _events,   // append 순서 = 결정론
            };

            // 정착지: _s 삽입 순서(= placement 인덱스) = 결정론
            for (int i = 0; i < _s.Count; i++)
            {
                SettlementSimState s = _s[i];
                save.Settlements.Add(new SettlementSnapshot
                {
                    SettlementId = s.SettlementId,
                    Race = s.Race,
                    RegionCoordX = s.RegionX,
                    RegionCoordY = s.RegionY,
                    Population = (int)Math.Round(s.Pop),
                    KingdomId = s.KingdomId,
                    Fate = s.Fate,
                    MergedIntoId = s.MergedIntoId,
                    ConqueredByKingdomId = s.ConqueredByKingdomId,
                    FoundedYear = s.FoundedYear,
                });
            }

            // 왕국: KingdomId 오름차순(발급 순), 멤버도 정렬 → 결정론 저장
            foreach (SimKingdom k in _kingdoms.Values.OrderBy(x => x.Id))
            {
                var data = new KingdomData(k.Id, k.FoundingRace, k.CapitalId) { Name = k.Name };
                data.MemberSettlementIds = k.Members.OrderBy(x => x).ToList();
                save.Kingdoms.Add(data);
            }

            return save;
        }
    }
}
