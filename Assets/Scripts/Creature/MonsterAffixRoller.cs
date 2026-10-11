#nullable enable
using System.Collections.Generic;
using ARPG.Component;
using ARPG.Data;
using ARPG.Tables;
using UnityEngine;
using GE = GlobalEnum;

namespace ARPG.Monster
{
    /// <summary>
    /// 몬스터 접사 판정 (MONSTER_AFFIX_DESIGN.md §3·§5.2). 무리 생성 때 같은 등급 구성원끼리 한 번 굴려 공유한다.
    /// 개수 = 등급 행의 기본 개수 + 추가 판정 반복(상한 MonsterAffixComponent.MAX_AFFIXES).
    /// 후보 = 등급·레벨·피해 종류 조건을 통과한 접사. 같은 Group은 한 번만 뽑고, 후보가 비면 거기서 멈춘다.
    /// 상태를 갖지 않는다 — 후보 목록은 매 판정 직전에 비우는 스크래치 버퍼다.
    /// </summary>
    public static class MonsterAffixRoller
    {
        private static readonly List<MonsterAffixTable> _candidateScratch = new(32);

        /// <summary>
        /// 접사를 굴린다. 결과의 AuraCount는 채우지 않는다(적용하는 쪽이 센다).
        /// </summary>
        /// <param name="archetype">판정할 등급. 등급 행이 없거나 마스크에 없는 등급(Boss 등)은 접사 없음</param>
        /// <param name="level">몬스터 레벨 (MinLevel 비교)</param>
        /// <param name="memberMonsterTableIds">이 판정을 공유하는 구성원 종. 피해 종류 조건은 전원이 통과해야 한다</param>
        public static MonsterAffixComponent Roll(GE.MonsterArchetype archetype, int level, List<int> memberMonsterTableIds)
        {
            MonsterAffixComponent result = default;
            DataManager data = AR.s.Data;
            MonsterArchetypeTable? archetypeTable = data.GetMonsterArchetype(archetype);
            if (archetypeTable == null)
                return result;

            int count = RollCount(archetypeTable);
            GE.MonsterArchetypeMask archetypeMask = ToMask(archetype);
            if (count <= 0 || archetypeMask == GE.MonsterArchetypeMask.None)
                return result;

            List<MonsterAffixTable> candidates = _candidateScratch;
            candidates.Clear();
            IReadOnlyList<MonsterAffixTable> affixes = data.MonsterAffixes;
            for (int i = 0; i < affixes.Count; i++)
            {
                MonsterAffixTable affix = affixes[i];
                if (affix.Weight <= 0
                    || (affix.AllowedArchetypes & archetypeMask) == 0
                    || level < affix.MinLevel)
                {
                    continue;
                }

                if (affix.HasRequiredDamageType == true
                    && AllMembersHaveSkillDamageType(data, memberMonsterTableIds, affix.RequiredDamageType) == false)
                {
                    continue;
                }

                candidates.Add(affix);
            }

            while (result.Count < count && candidates.Count > 0)
            {
                int pickedIndex = PickWeightedIndex(candidates);
                MonsterAffixTable picked = candidates[pickedIndex];
                result.Add(picked.Id);
                RemovePickedAndSameGroup(candidates, picked);
            }

            candidates.Clear();
            return result;
        }

        /// <summary>
        /// 접사 개수: 기본 개수에서 시작해 추가 판정에 성공할 때마다 1개씩 더한다. 실패하거나 상한이면 멈춘다.
        /// </summary>
        public static int RollCount(MonsterArchetypeTable archetypeTable)
        {
            int count = Mathf.Clamp(archetypeTable.AffixBaseCount, 0, MonsterAffixComponent.MAX_AFFIXES);
            int extraChance = Mathf.Clamp(archetypeTable.AffixExtraChance, 0, 100);
            while (count < MonsterAffixComponent.MAX_AFFIXES && Random.Range(0, 100) < extraChance)
            {
                count++;
            }

            return count;
        }

        private static GE.MonsterArchetypeMask ToMask(GE.MonsterArchetype archetype)
        {
            switch (archetype)
            {
                case GE.MonsterArchetype.Normal: return GE.MonsterArchetypeMask.Normal;
                case GE.MonsterArchetype.Magic: return GE.MonsterArchetypeMask.Magic;
                case GE.MonsterArchetype.Rare: return GE.MonsterArchetypeMask.Rare;
                default: return GE.MonsterArchetypeMask.None;
            }
        }

        private static bool AllMembersHaveSkillDamageType(DataManager data, List<int> memberMonsterTableIds, GE.DamageType damageType)
        {
            if (memberMonsterTableIds.Count == 0)
                return false;

            for (int i = 0; i < memberMonsterTableIds.Count; i++)
            {
                if (HasSkillDamageType(data, memberMonsterTableIds[i], damageType) == false)
                    return false;
            }

            return true;
        }

        /// <summary>
        /// 몬스터의 AI 스킬 중 이 피해 종류로 피해를 주는 스킬(DamageMax > 0)이 있는지.
        /// </summary>
        private static bool HasSkillDamageType(DataManager data, int monsterTableId, GE.DamageType damageType)
        {
            MonsterTable? monster = data.GetMonster(monsterTableId);
            AiTable? aiTable = monster != null ? monster.AiTable : null;
            if (aiTable == null)
                return false;

            return IsDamageSkill(data, aiTable.SkillId1, damageType)
                || IsDamageSkill(data, aiTable.SkillId2, damageType)
                || IsDamageSkill(data, aiTable.SkillId3, damageType);
        }

        private static bool IsDamageSkill(DataManager data, int skillId, GE.DamageType damageType)
        {
            if (skillId <= 0)
                return false;

            SkillTable? skill = data.GetSkill(skillId);
            return skill != null && skill.DamageType == damageType && skill.DamageMax > 0;
        }

        private static int PickWeightedIndex(List<MonsterAffixTable> candidates)
        {
            int totalWeight = 0;
            for (int i = 0; i < candidates.Count; i++)
            {
                totalWeight += candidates[i].Weight;
            }

            int roll = Random.Range(0, totalWeight);
            for (int i = 0; i < candidates.Count; i++)
            {
                roll -= candidates[i].Weight;
                if (roll < 0)
                    return i;
            }

            return candidates.Count - 1;
        }

        private static void RemovePickedAndSameGroup(List<MonsterAffixTable> candidates, MonsterAffixTable picked)
        {
            bool hasGroup = string.IsNullOrEmpty(picked.Group) == false;
            for (int i = candidates.Count - 1; i >= 0; i--)
            {
                MonsterAffixTable candidate = candidates[i];
                if (candidate.Id == picked.Id
                    || (hasGroup == true && candidate.Group == picked.Group))
                {
                    candidates.RemoveAt(i);
                }
            }
        }
    }
}
