namespace ChronicleSim;

// 설계 §5.2 NpcRelationType / NpcSentimentType 미러
public enum RelType { ParentOf, MentorOf, GrudgeAgainst, Spouse, Sibling, Friend, Rival }

public enum SentType { LostFamilyToWar, ProudOfKingdom, ResentsKingdom, ResentsMerge }

public enum Job { Elder, Farmer, Hunter, Smith, Guard, Merchant, Healer, Miner }

public sealed class Npc
{
    public int LocalId;
    public string Name = "";
    public Race Race;
    public Job Job;
    public bool IsRuler;     // 정복지의 지배종족(점령) 분파인지
    public int Household;
    public int Friendliness, Loyalty, Pride, Courage;
    public bool IsKey;       // 플레이어가 주목할 핵심 NPC
    public List<Sentiment> Sentiments = new();
}

public sealed class Sentiment
{
    public SentType Type;
    public int TargetSettlementId = -1;
    public string Note = "";
}

public sealed class Relation
{
    public int From, To;
    public RelType Type;
}

/// <summary>
/// L2 — 한 정착지의 연대기 맥락으로 NPC 명단 + 관계 + sentiment 생성 (설계 §5.3).
/// 추상 인구 → 스폰 NPC 수(≤50)로 환산, 연대기를 제약으로 주입.
/// </summary>
public sealed class VillageDossier
{
    public int SettlementId;
    public Race NativeRace;
    public Biome Biome;
    public double Pop;
    public Fate Fate;
    public int KingdomId;
    public string KingdomName = "(무소속)";
    public Race RulingRace;
    public bool ForeignRuled;          // 정복당해 타종족 지배 하
    public int ConquerorSettlementId = -1;
    public List<int> WarEnemySettlements = new();
    public List<Npc> Npcs = new();
    public List<Relation> Relations = new();

    public int CountRel(RelType t) => Relations.Count(r => r.Type == t);

    public static VillageDossier Generate(Simulator sim, int settlementId)
    {
        SettlementState s = sim.Get(settlementId);
        var d = new VillageDossier
        {
            SettlementId = settlementId,
            NativeRace = s.Race,
            Biome = s.Biome,
            Pop = s.Pop,
            Fate = s.Fate,
            KingdomId = s.KingdomId,
        };

        // ── 연대기 맥락 추출 ──
        Kingdom? k = sim.Kingdoms.FirstOrDefault(x => x.Id == s.KingdomId);
        if (k != null)
        {
            d.KingdomName = k.Name;
            d.RulingRace = k.FoundingRace;
            d.ForeignRuled = s.Fate == Fate.Conquered && k.FoundingRace != s.Race;
        }
        foreach (ChronicleEvent e in sim.Events)
        {
            if (e.Type == EventType.Conquer && e.SubjectId == settlementId)
                d.ConquerorSettlementId = e.ObjectId;             // 나를 정복한 마을
            if (e.Type == EventType.War && e.ObjectId == settlementId)
                d.WarEnemySettlements.Add(e.SubjectId);           // 내가 패한 전쟁 상대
            if (e.Type == EventType.War && e.SubjectId == settlementId)
                d.WarEnemySettlements.Add(e.ObjectId);            // 내가 이긴 전쟁 상대
        }

        int backfillSeed = unchecked(sim.MapSeed * 73856093 ^ settlementId * 19349663);
        var rng = new SplitMix64(DerivedSeed.Derive(backfillSeed, DerivedSeed.ChannelRelationBackfill));

        d.GenerateNpcs(rng);
        d.GenerateRelations(rng);
        d.GenerateSentiments(rng);
        d.MarkKeyNpcs();
        return d;
    }

    // 추상 인구 → 스폰 NPC 수 (Tier, 상한 50)
    private int SpawnCount(SplitMix64 rng)
    {
        int baseN = Pop switch
        {
            < 500 => rng.Range(4, 6),
            < 2000 => rng.Range(8, 16),
            < 8000 => rng.Range(20, 35),
            _ => rng.Range(40, 50),
        };
        return Math.Min(50, baseN);
    }

    private void GenerateNpcs(SplitMix64 rng)
    {
        int n = SpawnCount(rng);

        // 정복지(타종족 지배)면 일부 NPC를 지배종족(점령/행정)으로
        int rulers = ForeignRuled ? Math.Max(1, n * rng.Range(15, 30) / 100) : 0;

        var jobs = BuildJobPlan(n, rng);
        for (int i = 0; i < n; i++)
        {
            bool isRuler = i < rulers;
            Race race = isRuler ? RulingRace : NativeRace;
            var npc = new Npc
            {
                LocalId = i,
                Race = race,
                Job = jobs[i],
                IsRuler = isRuler,
                Friendliness = rng.Range(0, 100),
                Loyalty = rng.Range(0, 100),
                Pride = rng.Range(0, 100),
                Courage = rng.Range(0, 100),
                Name = NameGen.Make(race, rng),
            };
            Npcs.Add(npc);
        }
    }

    private List<Job> BuildJobPlan(int n, SplitMix64 rng)
    {
        var jobs = new List<Job> { Job.Elder }; // 촌장 1
        // 대략적 직업 분포
        for (int i = 1; i < n; i++)
        {
            int r = rng.Range(0, 99);
            Job j = r switch
            {
                < 45 => Job.Farmer,
                < 60 => Job.Hunter,
                < 72 => Job.Guard,
                < 82 => Job.Smith,
                < 90 => Job.Merchant,
                < 96 => Job.Miner,
                _ => Job.Healer,
            };
            jobs.Add(j);
        }
        return jobs;
    }

    private void GenerateRelations(SplitMix64 rng)
    {
        int n = Npcs.Count;
        if (n == 0) return;

        // ── 가구 분할 (1~5인) ── 같은 분파(native/ruler)끼리만 한 가구
        var nativeIdx = Npcs.Where(x => x.IsRuler == false).Select(x => x.LocalId).ToList();
        var rulerIdx = Npcs.Where(x => x.IsRuler).Select(x => x.LocalId).ToList();
        int hid = 0;
        hid = SplitHouseholds(nativeIdx, rng, hid);
        SplitHouseholds(rulerIdx, rng, hid);

        // ── 직업 사제 (같은 직업 ≥2 → 선임→후임 MentorOf) ──
        foreach (var grp in Npcs.GroupBy(x => x.Job).Where(g => g.Count() >= 2))
        {
            var list = grp.OrderBy(x => x.LocalId).ToList();
            for (int i = 1; i < list.Count && i <= 3; i++) // 사제 사슬은 적당히
                Relations.Add(new Relation { From = list[0].LocalId, To = list[i].LocalId, Type = RelType.MentorOf });
        }

        // ── 성격 기반 ──
        for (int i = 0; i < n; i++)
        {
            Npc a = Npcs[i];
            // 친화력 높으면 친구 1~2
            if (a.Friendliness >= 60)
            {
                int friends = a.Friendliness >= 85 ? 2 : 1;
                for (int f = 0; f < friends; f++)
                {
                    int j = rng.Range(0, n - 1);
                    if (j != i && Npcs[j].IsRuler == a.IsRuler)
                        AddUnique(i, j, RelType.Friend);
                }
            }
            // 자존심 높고 같은 직업이면 라이벌
            if (a.Pride >= 70)
            {
                Npc? rival = Npcs.FirstOrDefault(x => x.LocalId != i && x.Job == a.Job && x.Pride >= 55);
                if (rival != null)
                    AddUnique(i, rival.LocalId, RelType.Rival);
            }
        }
    }

    private int SplitHouseholds(List<int> idx, SplitMix64 rng, int hid)
    {
        int pos = 0;
        while (pos < idx.Count)
        {
            int size = Math.Min(rng.Range(1, 5), idx.Count - pos);
            var members = idx.GetRange(pos, size);
            foreach (int m in members) Npcs[m].Household = hid;

            // 가구 내 관계: 2명 이상이면 첫 둘은 부부, 나머지는 자녀
            if (members.Count >= 2)
            {
                int pa = members[0], ma = members[1];
                Relations.Add(new Relation { From = pa, To = ma, Type = RelType.Spouse });
                var children = members.Skip(2).ToList();
                foreach (int c in children)
                {
                    Relations.Add(new Relation { From = pa, To = c, Type = RelType.ParentOf });
                    Relations.Add(new Relation { From = ma, To = c, Type = RelType.ParentOf });
                }
                for (int i = 0; i < children.Count; i++)
                    for (int j = i + 1; j < children.Count; j++)
                        Relations.Add(new Relation { From = children[i], To = children[j], Type = RelType.Sibling });
            }
            pos += size;
            hid++;
        }
        return hid;
    }

    private void GenerateSentiments(SplitMix64 rng)
    {
        foreach (Npc npc in Npcs)
        {
            if (npc.IsRuler)
            {
                // 점령 분파 → 왕국 자부심
                npc.Sentiments.Add(new Sentiment { Type = SentType.ProudOfKingdom, Note = $"{KingdomName}의 지배층" });
                continue;
            }

            // 피정복 native
            if (ForeignRuled)
            {
                // Loyalty 낮을수록 강한 반감
                if (npc.Loyalty < 70)
                    npc.Sentiments.Add(new Sentiment
                    {
                        Type = SentType.ResentsKingdom,
                        TargetSettlementId = ConquerorSettlementId,
                        Note = $"{RulingRace} 지배에 반감 (충성 {npc.Loyalty})",
                    });
                else
                    npc.Sentiments.Add(new Sentiment { Type = SentType.ProudOfKingdom, Note = "체념·순응" });

                // 일부는 정복 전쟁 유족
                if (ConquerorSettlementId >= 0 && npc.Courage >= 55 && rng.Chance(0.35))
                    npc.Sentiments.Add(new Sentiment
                    {
                        Type = SentType.LostFamilyToWar,
                        TargetSettlementId = ConquerorSettlementId,
                        Note = "정복 전쟁에서 가족을 잃음",
                    });
            }
            else if (KingdomId != -1)
            {
                // 자국(동족) 왕국 — 충성 높으면 자부심
                if (npc.Loyalty >= 55)
                    npc.Sentiments.Add(new Sentiment { Type = SentType.ProudOfKingdom, Note = $"{KingdomName} 시민" });
            }

            // 전쟁 이력(정복 외) 유족
            if (WarEnemySettlements.Count > 0 && rng.Chance(0.18))
            {
                int enemy = WarEnemySettlements[rng.Range(0, WarEnemySettlements.Count - 1)];
                if (enemy != ConquerorSettlementId)
                    npc.Sentiments.Add(new Sentiment
                    {
                        Type = SentType.LostFamilyToWar,
                        TargetSettlementId = enemy,
                        Note = "과거 전쟁의 상흔",
                    });
            }
        }
    }

    private void MarkKeyNpcs()
    {
        // 촌장
        Npc? elder = Npcs.FirstOrDefault(x => x.Job == Job.Elder);
        if (elder != null) elder.IsKey = true;
        // 직업 마스터(사제) = MentorOf의 From
        foreach (int from in Relations.Where(r => r.Type == RelType.MentorOf).Select(r => r.From).Distinct())
            Npcs[from].IsKey = true;
        // 강한 연대기 sentiment 보유자
        foreach (Npc npc in Npcs)
            if (npc.Sentiments.Any(s => s.Type is SentType.ResentsKingdom or SentType.LostFamilyToWar))
                npc.IsKey = true;
    }

    private void AddUnique(int a, int b, RelType t)
    {
        if (a == b) return;
        if (Relations.Any(r => r.Type == t &&
            ((r.From == a && r.To == b) || (r.From == b && r.To == a))))
            return;
        Relations.Add(new Relation { From = a, To = b, Type = t });
    }
}

/// <summary>종족별 음절 조합 간이 작명기 (가독성용).</summary>
public static class NameGen
{
    private static readonly Dictionary<Race, (string[] a, string[] b)> Parts = new()
    {
        [Race.Human] = (new[] { "Al", "Bran", "Cor", "Ed", "Gar", "Mer", "Rho", "Tam" }, new[] { "win", "ric", "den", "mund", "ric", "el", "son" }),
        [Race.Elf] = (new[] { "Ae", "Cae", "El", "Fae", "Lyr", "Syl", "Thae" }, new[] { "rion", "thil", "wyn", "las", "dor", "neth" }),
        [Race.Dwarf] = (new[] { "Bor", "Dur", "Grim", "Khaz", "Thra", "Bal" }, new[] { "in", "ak", "din", "grim", "nor", "bek" }),
        [Race.Orc] = (new[] { "Gor", "Mor", " Urk".Trim(), "Zog", "Grash", "Krul" }, new[] { "nak", "gash", "mok", "thar", "uk" }),
        [Race.Nomad] = (new[] { "Aza", "Jeb", "Kha", "Sul", "Tem", "Yul" }, new[] { "ra", "khan", "mir", "dar", "een" }),
        [Race.Lizardman] = (new[] { "Sss", "Xa", "Zik", "Ssza", "Kth" }, new[] { "tek", "ssk", "rax", "hiss", "kor" }),
        [Race.Yeti] = (new[] { "Bor", "Hrun", "Kor", "Vor", "Yng" }, new[] { "gar", "uk", "rim", "nok", "var" }),
    };

    public static string Make(Race race, SplitMix64 rng)
    {
        (string[] a, string[] b) = Parts.TryGetValue(race, out var p) ? p : Parts[Race.Human];
        return a[rng.Range(0, a.Length - 1)] + b[rng.Range(0, b.Length - 1)];
    }
}
