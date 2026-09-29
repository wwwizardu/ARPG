#nullable enable

namespace ARPG.Npc.Narrative
{
    /// <summary>
    /// 야망 소문(NpcRumor)을 말로 옮길 때 쓰는 낱말 조각 (NPC_AMBITION P2c, D60). 대화 토픽(DialogueTopicSelector)과 검사 UI(UINpcInspect)가 함께 쓴다.
    /// 상태 없음 — 입력만 보고 문자열을 돌려준다. 마을·정착지에 이름이 없으므로 장소는 "이 마을 ○쪽"처럼 방향으로만 말한다.
    /// </summary>
    public static class NpcRumorPhrasing
    {
        private static readonly string[] BearingNames = { "북쪽", "북동쪽", "동쪽", "남동쪽", "남쪽", "남서쪽", "서쪽", "북서쪽" };

        /// <summary>8방위 이름 (0 북 … 7 북서). 범위 밖이면 빈 문자열.</summary>
        public static string BearingName(int bearing)
        {
            return bearing >= 0 && bearing < BearingNames.Length ? BearingNames[bearing] : string.Empty;
        }

        /// <summary>
        /// 장소 핵심어: "이 마을 북동쪽" / "이 근처" / "먼 마을". 뒤에 "던전"·"에 있소" 등을 붙여 쓴다.
        /// </summary>
        public static string PlaceCore(NpcRumorPlace place, int bearing)
        {
            return place switch
            {
                NpcRumorPlace.Bearing => $"이 마을 {BearingName(bearing)}",
                NpcRumorPlace.Nearby => "이 근처",
                NpcRumorPlace.OtherVillage => "먼 마을",
                _ => "어딘가",
            };
        }

        /// <summary>사건 시점 (게임시간 단위): 방금 / 오늘 / 어제 / 그저께 / N일 전.</summary>
        public static string AgoPhrase(float eventGameTime, float now)
        {
            float hours = now - eventGameTime;
            if (hours < 3f)
                return "방금";
            if (hours < 24f)
                return "오늘";
            if (hours < 48f)
                return "어제";
            if (hours < 72f)
                return "그저께";
            return $"{(int)(hours / 24f)}일 전";
        }

        /// <summary>NPC 이름. 세이브에서 사라진 NPC는 "어떤 모험가"로 말한다 (지어낸 이름 금지).</summary>
        public static string NpcName(int entityId)
        {
            if (entityId < 0 || AR.s.Npc == null || AR.s.Npc.GetSaveData(entityId) == null)
                return "어떤 모험가";
            return AR.s.Npc.GetDisplayName(entityId);
        }

        /// <summary>이 소문을 말하는 쪽이 이름을 댈 수 있는 출처인가: 한 다리(Hops 1)로 들었고 출처 NPC가 아직 세이브에 있음.</summary>
        public static bool HasNamedSource(NpcRumor rumor)
        {
            return rumor.SourceNpcId >= 0 && rumor.Hops == 1 && AR.s.Npc != null &&
                   AR.s.Npc.GetSaveData(rumor.SourceNpcId) != null;
        }

        /// <summary>들은 소문의 말머리: "○○에게 들었는데, " (한 다리) / "누가 그러던데, " (여러 다리·출처 불명). 직접 겪은 소문은 빈 문자열.</summary>
        public static string HeardPrefix(NpcRumor rumor)
        {
            if (rumor.SourceNpcId < 0)
                return string.Empty;
            if (HasNamedSource(rumor) == true)
                return $"{NpcName(rumor.SourceNpcId)}에게 들었는데, ";
            return "누가 그러던데, ";
        }

        /// <summary>출처 설명 (LLM Facts·검사 UI용): 직접 겪음 / ○○에게 들음 / 여러 사람을 거쳐 들음(n단계).</summary>
        public static string SourceNote(NpcRumor rumor)
        {
            if (rumor.SourceNpcId < 0)
                return "직접 겪음";
            if (HasNamedSource(rumor) == true)
                return $"{NpcName(rumor.SourceNpcId)}에게 직접 들음";
            return $"여러 사람을 거쳐 들음({rumor.Hops}단계)";
        }

        /// <summary>받침에 맞는 조사: Josa("초록", "이", "가") → "초록이". 한글이 아니면 받침 없음으로 본다.</summary>
        public static string Josa(string word, string withFinal, string withoutFinal)
        {
            if (string.IsNullOrEmpty(word) == true)
                return withoutFinal;
            char last = word[word.Length - 1];
            if (last < '가' || last > '힣')
                return word + withoutFinal;
            int finalConsonant = (last - '가') % 28;
            // "으로/로"는 ㄹ받침도 "로"
            if (withoutFinal == "로" && finalConsonant == 8)
                return word + withoutFinal;
            return word + (finalConsonant != 0 ? withFinal : withoutFinal);
        }
    }
}
