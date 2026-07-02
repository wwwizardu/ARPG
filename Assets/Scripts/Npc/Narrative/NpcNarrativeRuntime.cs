#nullable enable
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

namespace ARPG.Npc.Narrative
{
    /// <summary>
    /// NPC 서사(대화·소망) 세션 런타임 상태 — NpcManager가 소유.
    /// 전부 휘발성(세이브 대상 아님): 대화 캐시 / 소망 캐시 / 화제 셔플백.
    /// static 필드가 아니라 인스턴스로 두어 새 게임·씬 로드 시 Reset()으로 일괄 정리한다.
    /// 설계: Docs/world_history/NARRATIVE_ENGINE_SPIKE.md
    /// </summary>
    public class NpcNarrativeRuntime
    {
        // 대화 캐시: (npcId, tier, topic) → 생성 대사
        private readonly Dictionary<(int, AffinityTier, DialogueTopicType), string> _dialogueCache = new();
        // 소망 캐시: npcId → 소망
        private readonly Dictionary<int, NpcWish> _wishCache = new();
        // 화제 셔플백: npcId → 최근 말한 토픽(중복 회피, 소진 시 리셋)
        private readonly Dictionary<int, HashSet<DialogueTopicType>> _recentTopics = new();

        // ── 대화 캐시 ──
        public bool TryGetDialogue((int, AffinityTier, DialogueTopicType) key, [MaybeNullWhen(false)] out string text) =>
            _dialogueCache.TryGetValue(key, out text);

        public void SetDialogue((int, AffinityTier, DialogueTopicType) key, string text) =>
            _dialogueCache[key] = text;

        // ── 소망 캐시 ──
        public bool TryGetWish(int npcId, [MaybeNullWhen(false)] out NpcWish wish) =>
            _wishCache.TryGetValue(npcId, out wish);

        public void SetWish(int npcId, NpcWish wish) =>
            _wishCache[npcId] = wish;

        // ── 화제 셔플백 ── (없으면 생성해 반환; 반환된 집합에 대한 변경은 이 런타임에 그대로 유지)
        public HashSet<DialogueTopicType> GetRecentTopics(int npcId)
        {
            if (_recentTopics.TryGetValue(npcId, out HashSet<DialogueTopicType> set) == false)
            {
                set = new HashSet<DialogueTopicType>();
                _recentTopics[npcId] = set;
            }
            return set;
        }

        // 새 게임·씬 로드 시 매니저가 호출
        public void Reset()
        {
            _dialogueCache.Clear();
            _wishCache.Clear();
            _recentTopics.Clear();
        }
    }
}
