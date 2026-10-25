#nullable enable
using ARPG.Component;
using ARPG.Tables;
using UnityEngine;
using GE = GlobalEnum;

namespace ARPG.Skill.Combat
{
    /// <summary>
    /// 몬스터 소환(SkillEffect SummonMonster)의 수·위치·레벨 판정 (SKILL_DESIGN §6.12). 상태를 갖지 않는다.
    /// 소환 실행(SkillEffectExecutor)과 AI 스킬 후보 판정(SkillHelper)이 같은 최대 수 규칙을 쓴다.
    /// </summary>
    public static class MonsterSummonRules
    {
        // 소환 위치: 주인 몸 바깥 둘레에서 걸을 수 있는 칸을 고른다
        private const float SUMMON_RADIUS_MIN = 0.8f;
        private const float SUMMON_RADIUS_MAX = 1.5f;
        private const int SUMMON_POSITION_ATTEMPTS = 8;

        /// <summary>효과 목록에서 첫 몬스터 소환 효과 Id를 찾는다. 없으면 0.</summary>
        public static int FindSummonEffectId(in SkillEffectIds inEffectIds)
        {
            for (int i = 0; i < inEffectIds.Count; i++)
            {
                int effectId = inEffectIds.Get(i);
                SkillEffectTable? effect = AR.s.Data.GetSkillEffect(effectId);
                if (effect != null && effect.EffectType == GE.Stat.SummonMonster)
                    return effectId;
            }
            return 0;
        }

        /// <summary>Param2는 한 번에 부르는 수이자 같은 효과로 동시에 살아 있을 수 있는 최대 수다.</summary>
        public static int GetMaxCount(SkillEffectTable inEffect)
        {
            return Mathf.Max(1, Mathf.RoundToInt(inEffect.Param2));
        }

        /// <summary>주인이 이 효과로 불러 아직 살아 있는 몬스터 수. 생성 대기 중인 것은 세지 않는다.</summary>
        public static int CountActiveSummons(int inOwnerEntityId, int inSummonEffectId)
        {
            ComponentManager cm = AR.s.Component;
            SparseSet<SummonedMonsterComponent> pool = cm.GetComponentPool<SummonedMonsterComponent>();
            int count = 0;
            for (int i = 0; i < pool.Count; i++)
            {
                SummonedMonsterComponent summoned = pool.GetByIndex(i);
                if (summoned.OwnerEntityId != inOwnerEntityId || summoned.SummonEffectId != inSummonEffectId)
                    continue;

                int entityId = pool.GetEntityId(i);
                if (cm.HasComponent<DestroyTag>(entityId) == true)
                    continue;
                if (cm.TryGetComponent<StatComponent>(entityId, out StatComponent stat) == true && stat.CurrentHp <= 0f)
                    continue;

                count++;
            }
            return count;
        }

        /// <summary>최대 수만큼 살아 있으면 참. AI는 이때 소환 스킬을 후보에서 뺀다.</summary>
        public static bool IsAtCapacity(int inOwnerEntityId, int inSummonEffectId)
        {
            SkillEffectTable? effect = AR.s.Data.GetSkillEffect(inSummonEffectId);
            if (effect == null)
                return false;

            return CountActiveSummons(inOwnerEntityId, inSummonEffectId) >= GetMaxCount(effect);
        }

        /// <summary>소환 몬스터 레벨 = 주인 레벨. 주인 레벨을 모르면 0(몬스터 표 기본 레벨)을 돌려준다.</summary>
        public static int ResolveOwnerLevel(int inOwnerEntityId)
        {
            ComponentManager cm = AR.s.Component;
            if (cm.TryGetComponent<ExpRewardComponent>(inOwnerEntityId, out ExpRewardComponent expReward) == true)
                return expReward.MonsterLevel;
            if (cm.TryGetComponent<DropComponent>(inOwnerEntityId, out DropComponent drop) == true)
                return drop.MonsterLevel;
            return 0;
        }

        /// <summary>주인 둘레에서 걸을 수 있는 소환 위치를 고른다. 못 찾으면 주인 위치를 돌려주고 false.</summary>
        public static bool TryPickPosition(Vector2 inOwnerPosition, out Vector2 outPosition)
        {
            for (int attempt = 0; attempt < SUMMON_POSITION_ATTEMPTS; attempt++)
            {
                float angle = Random.Range(0f, Mathf.PI * 2f);
                float radius = Random.Range(SUMMON_RADIUS_MIN, SUMMON_RADIUS_MAX);
                Vector2 candidate = inOwnerPosition + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius;
                if (AR.s.Map.IsWalkable(candidate) == true)
                {
                    outPosition = candidate;
                    return true;
                }
            }

            outPosition = inOwnerPosition;
            return false;
        }
    }
}
