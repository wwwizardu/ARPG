#nullable enable
using System.Collections.Generic;
using ARPG.Component;
using GE = GlobalEnum;

namespace ARPG.Data
{
    /// <summary>
    /// DataManager 런타임(휘발성·세이브 아님) 상태 파티션.
    /// 여기 담기는 건 장비/버프 등에서 파생돼 세션 동안만 유지되는 데이터 — 저장되지 않으며 Reset()에서 일괄 정리된다.
    /// 수명(lifetime) 기준 분할: 본체=영속(세이브), _Table=정적 설정, _Runtime=휘발성.
    /// 새 항목 추가 시 책임별 region으로 묶고 "왜 런타임(비저장)인지"를 주석으로 명시할 것.
    /// </summary>
    public partial class DataManager
    {
        // ─────────────────────────────────────────────────────────────
        // Stat Modifiers — ownerEntityId → 적용된 StatModifier 목록.
        // 장비/버프에서 파생, 세이브 아님. (구 StatModifierHelper._modifiersByOwner 이관:
        // static 제거 + 엔티티 사망 시 정리로 누수/ID 재활용 오염 방지)
        // ─────────────────────────────────────────────────────────────
        private readonly Dictionary<int, List<StatModifier>> _modifiersByOwner = new();

        /// <summary>StatModifier를 타겟 엔티티에 추가</summary>
        public void AddStatModifier(int targetEntityId, StatModifierSource source, int sourceId, GE.Stat statType, StatModifierType modifierType, int value, int priority = 0)
        {
            StatModifier modifier = new StatModifier(statType, modifierType, source, sourceId, value, priority);

            if (_modifiersByOwner.ContainsKey(targetEntityId) == false)
                _modifiersByOwner[targetEntityId] = new List<StatModifier>();

            _modifiersByOwner[targetEntityId].Add(modifier);
        }

        /// <summary>특정 소스의 StatModifier를 모두 제거</summary>
        /// <returns>제거된 modifier 수</returns>
        public int RemoveModifiersBySource(int targetEntityId, StatModifierSource source, int sourceId)
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
                _modifiersByOwner.Remove(targetEntityId);

            return removedCount;
        }

        /// <summary>특정 엔티티의 모든 StatModifier 목록 반환 (System_StatCalculation에서 사용). 없으면 null</summary>
        public List<StatModifier>? GetModifiers(int ownerEntityId)
        {
            if (_modifiersByOwner.ContainsKey(ownerEntityId) == false)
                return null;

            return _modifiersByOwner[ownerEntityId];
        }

        /// <summary>특정 엔티티의 모든 StatModifier 제거 (엔티티 사망 시 — EntityIdHelper.DestroyEntity에서 호출)</summary>
        public void RemoveAllModifiers(int ownerEntityId)
        {
            _modifiersByOwner.Remove(ownerEntityId);
        }

        // ─────────────────────────────────────────────────────────────
        // 런타임 상태 전체 초기화 — 씬 전환/새 게임 시 DataManager.Reset()에서 호출.
        // 런타임 파티션에 항목이 늘면 여기에 Clear를 추가한다.
        // ─────────────────────────────────────────────────────────────
        private void ResetRuntimeData()
        {
            _modifiersByOwner.Clear();
        }
    }
}
