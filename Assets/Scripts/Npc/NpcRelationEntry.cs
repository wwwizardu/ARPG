using System;

namespace ARPG.Npc
{
    /// <summary>
    /// NPC 쌍의 *유형 있는* 관계 (가족/친구/원한…). 호감도 수치(_npcAffinity)와 별개 채널.
    /// 정본은 NpcManager + WorldData.NpcRelationList (엔티티 수명과 분리 — 오프라인 NPC 간에도 존재).
    /// 설계: NPC_RELATION_HISTORY_DESIGN.md §4.2
    /// </summary>
    [Serializable]
    public class NpcRelationEntry
    {
        public int FromEntityId;
        public int ToEntityId;
        public NpcRelationType Type;
        public int OriginEventId = -1;   // ChronicleEvent 참조. 연대기와 무관하면 -1

        public NpcRelationEntry() { }

        public NpcRelationEntry(int fromEntityId, int toEntityId, NpcRelationType type, int originEventId = -1)
        {
            FromEntityId = fromEntityId;
            ToEntityId = toEntityId;
            Type = type;
            OriginEventId = originEventId;
        }
    }

    /// <summary>
    /// 사망으로 활성 관계 그래프에서 제거된 NPC를 생존 NPC가 회고할 수 있도록 남기는 불변 스냅샷.
    /// 현재 관계 판정과 호감도 계산에는 사용하지 않는다.
    /// </summary>
    [Serializable]
    public class DeceasedRelationMemory
    {
        public int DeceasedNpcEntityId;
        public string DeceasedNpcName = string.Empty;
        public NpcRelationType RelationType;
        public bool WasFromEntity;
        public int FellEventId = -1;
        public float DeathGameTime;

        public DeceasedRelationMemory() { }

        public DeceasedRelationMemory(int deceasedNpcEntityId, string deceasedNpcName,
            NpcRelationType relationType, bool wasFromEntity, int fellEventId, float deathGameTime)
        {
            DeceasedNpcEntityId = deceasedNpcEntityId;
            DeceasedNpcName = deceasedNpcName;
            RelationType = relationType;
            WasFromEntity = wasFromEntity;
            FellEventId = fellEventId;
            DeathGameTime = deathGameTime;
        }
    }

    public enum NpcRelationType
    {
        // 방향성 (From → To)
        ParentOf = 0,        // From이 To의 부모
        MentorOf = 1,        // 직업 사제
        GrudgeAgainst = 2,   // From이 To에게 원한

        // 대칭 (NpcPairKey 정규화, 양방향 인덱스로 1건만 저장)
        Spouse = 100,
        Sibling = 101,
        Friend = 102,
        Rival = 103,
    }
}
