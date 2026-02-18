#nullable enable
using System.Collections.Generic;
using ARPG.Component;
using UnityEngine;
using GE = GlobalEnum;

namespace ARPG.Utility
{
    /// <summary>
    /// StatModifier 생성/제거/조회 공통 유틸리티
    /// Dictionary 기반으로 관리하여 O(1) 조회/제거 지원
    /// </summary>
    public static class StatModifierHelper
    {
        /// <summary>
        /// ownerEntityId → 해당 엔티티에 적용된 모든 StatModifier 목록
        /// </summary>
        private static readonly Dictionary<int, List<StatModifier>> _modifiersByOwner = new();

        /// <summary>
        /// StatModifier를 타겟 엔티티에 추가
        /// </summary>
        public static void AddStatModifier(int targetEntityId, StatModifierSource source, int sourceId, GE.Stat statType, StatModifierType modifierType, int value, int priority = 0)
        {
            StatModifier modifier = new StatModifier(
                statType,
                modifierType,
                source,
                sourceId,
                value,
                priority
            );

            if (_modifiersByOwner.ContainsKey(targetEntityId) == false)
            {
                _modifiersByOwner[targetEntityId] = new List<StatModifier>();
            }

            _modifiersByOwner[targetEntityId].Add(modifier);
        }

        /// <summary>
        /// 특정 소스의 StatModifier를 모두 제거
        /// </summary>
        /// <returns>제거된 modifier 수</returns>
        public static int RemoveModifiersBySource(int targetEntityId, StatModifierSource source, int sourceId)
        {
            if (_modifiersByOwner.ContainsKey(targetEntityId) == false)
                return 0;

            List<StatModifier> modifiers = _modifiersByOwner[targetEntityId];
            int removedCount = 0;

            for (int i = modifiers.Count - 1; i >= 0; i--)
            {
                if (modifiers[i].Source == source && modifiers[i].SourceId == sourceId)
                {
                    modifiers.RemoveAt(i);
                    removedCount++;
                }
            }

            if (modifiers.Count == 0)
            {
                _modifiersByOwner.Remove(targetEntityId);
            }

            return removedCount;
        }

        /// <summary>
        /// 특정 엔티티의 모든 StatModifier 목록 반환 (System_StatCalculation에서 사용)
        /// </summary>
        public static List<StatModifier>? GetModifiers(int ownerEntityId)
        {
            if (_modifiersByOwner.ContainsKey(ownerEntityId) == false)
                return null;

            return _modifiersByOwner[ownerEntityId];
        }

        /// <summary>
        /// 특정 엔티티의 모든 StatModifier 제거 (엔티티 사망 시 등)
        /// </summary>
        public static void RemoveAllModifiers(int ownerEntityId)
        {
            _modifiersByOwner.Remove(ownerEntityId);
        }

        /// <summary>
        /// 전체 초기화 (씬 전환 시)
        /// </summary>
        public static void Reset()
        {
            _modifiersByOwner.Clear();
        }
    }
}
