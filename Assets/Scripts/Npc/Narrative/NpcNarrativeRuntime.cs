#nullable enable
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

namespace ARPG.Npc.Narrative
{
    /// <summary>
    /// NPC 대화 세션 런타임 상태 — NpcManager가 소유.
    /// 전부 휘발성(세이브 대상 아님): 대화 캐시 / 화제 셔플백. 소망 진행은 저장되는 원장(NarrativeSaveRoot)에 있다.
    /// static 필드가 아니라 인스턴스로 두어 새 게임·씬 로드 시 Reset()으로 일괄 정리한다.
    /// 설계: Docs/world_history/NARRATIVE_ENGINE_SPIKE.md
    /// </summary>
    public class NpcNarrativeRuntime
    {
        // 대화 캐시: npcId → (tier, topic, variant) → 생성 대사.
        // npcId를 최상위 키로 둬 새 사건(Deed·사별) 발생 시 NPC 단위 무효화가 O(1).
        // variant는 같은 토픽 타입 안의 내용 구분자(DialogueTopic.CacheVariant) — 새 사건이면 새 대사.
        private readonly Dictionary<int, Dictionary<(AffinityTier, DialogueTopicType, int), string>> _dialogueCache = new();
        // 화제 셔플백: npcId → 최근 말한 토픽(중복 회피, 소진 시 리셋)
        private readonly Dictionary<int, HashSet<DialogueTopicType>> _recentTopics = new();
        // 세계 교체(Reset)마다 올라간다. 늦게 도착한 대화 응답·저장 결과가 새 세계에 적용되지 않게 비교한다.
        public int Lifetime { get; private set; }

        // ── 대화 캐시 ──
        public bool TryGetDialogue(int npcId, AffinityTier tier, DialogueTopicType topic, int variant,
            [MaybeNullWhen(false)] out string text)
        {
            if (_dialogueCache.TryGetValue(npcId, out Dictionary<(AffinityTier, DialogueTopicType, int), string> perNpc))
                return perNpc.TryGetValue((tier, topic, variant), out text);
            text = null!;
            return false;
        }

        public void SetDialogue(int npcId, AffinityTier tier, DialogueTopicType topic, int variant, string text)
        {
            if (_dialogueCache.TryGetValue(npcId, out Dictionary<(AffinityTier, DialogueTopicType, int), string> perNpc) == false)
            {
                perNpc = new Dictionary<(AffinityTier, DialogueTopicType, int), string>();
                _dialogueCache[npcId] = perNpc;
            }
            perNpc[(tier, topic, variant)] = text;
        }

        /// <summary>
        /// 새 사건(Deed 추가·사별 기록) 발생 시 해당 NPC의 대화 캐시만 폐기 — 다음 대화에서 재생성.
        /// 셔플백(화제 순환)은 유지한다. NARRATIVE_DIALOGUE_VARIETY §5 P1-4.
        /// </summary>
        public void InvalidateDialogues(int npcId) => _dialogueCache.Remove(npcId);

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
            Lifetime++;
            _dialogueCache.Clear();
            _recentTopics.Clear();
        }
    }
}
