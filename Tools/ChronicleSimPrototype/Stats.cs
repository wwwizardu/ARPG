namespace ChronicleSim;

/// <summary>한 시뮬 결과의 요약 지표. 사용자 우려(다종족 제국 / 단일 제국 수렴)를 직접 측정.</summary>
public sealed class Stats
{
    public int MapSeed;
    public int Settlements;
    public int Alive;        // 독립 생존
    public int Conquered;
    public int Merged;
    public int Ruined;
    public int Active;       // Alive + Conquered (= 실재하는 마을)
    public int SurvivingKingdoms;
    public int LargestKingdomMembers;
    public double LargestPct; // 최대 왕국 멤버 / Active
    public int MultiRaceKingdoms;
    public double AvgNeighbors;

    public double RuinedPct => Settlements == 0 ? 0 : (double)Ruined / Settlements;
    public double MergedPct => Settlements == 0 ? 0 : (double)Merged / Settlements;
    public double ConqueredPct => Settlements == 0 ? 0 : (double)Conquered / Settlements;

    public static Stats Compute(Simulator sim)
    {
        var st = new Stats
        {
            MapSeed = sim.MapSeed,
            Settlements = sim.Settlements.Count,
            AvgNeighbors = sim.AvgNeighbors,
        };

        foreach (SettlementState s in sim.Settlements)
        {
            switch (s.Fate)
            {
                case Fate.Alive: st.Alive++; break;
                case Fate.Conquered: st.Conquered++; break;
                case Fate.Merged: st.Merged++; break;
                case Fate.Ruined: st.Ruined++; break;
            }
        }
        st.Active = st.Alive + st.Conquered;

        foreach (Kingdom k in sim.Kingdoms)
        {
            var activeMembers = k.Members.Where(id => sim.Get(id).Active).ToList();
            if (activeMembers.Count == 0)
                continue;

            st.SurvivingKingdoms++;
            if (activeMembers.Count > st.LargestKingdomMembers)
                st.LargestKingdomMembers = activeMembers.Count;

            int distinctRaces = activeMembers.Select(id => sim.Get(id).Race).Distinct().Count();
            if (distinctRaces >= 2)
                st.MultiRaceKingdoms++;
        }

        st.LargestPct = st.Active == 0 ? 0 : (double)st.LargestKingdomMembers / st.Active;
        return st;
    }

    public string OneLine() =>
        $"seed {MapSeed}: 정착지 {Settlements} | 생존 왕국 {SurvivingKingdoms} (다종족 {MultiRaceKingdoms}) | " +
        $"최대 왕국 {LargestKingdomMembers}개 ({LargestPct:P0}) | 폐허 {Ruined} 합병 {Merged} 정복 {Conquered}";

    public string Detail() =>
        $"""
          독립 생존 ...... {Alive}
          정복당함 ....... {Conquered}
          합병 소멸 ...... {Merged}
          폐허 ........... {Ruined}
          실재 마을(Active) {Active}
          이웃 평균 ...... {AvgNeighbors:F1}
        """;
}
