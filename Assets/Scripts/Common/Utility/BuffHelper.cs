using ARPG.Component;
using ARPG.Manager;
using ARPG.Tables;
using ARPG.Utility;
using UnityEngine;
using GE = GlobalEnum;

namespace ARPG.Utility
{
    /// <summary>
    /// 버프 헬퍼 유틸리티
    /// 버프 추가, 제거, 조회 등의 기능 제공
    /// BuffInstance만으로 모든 버프를 관리 (BuffListComponent 제거)
    /// </summary>
    public static class BuffHelper
    {
        public static bool TryGetBuffTableByEffectType(GE.BuffEffectType effectType, out BuffTable buffTable)
        {
            buffTable = null;

            BuffTable foundBuffTable = AR.s.Data?.GetBuffByEffectType(effectType);
            if (foundBuffTable == null)
            {
                Debug.LogWarning($"[BuffHelper] BuffTable not found - EffectType: {effectType}");
                return false;
            }

            buffTable = foundBuffTable;
            return true;
        }

        public static int AddBuffByEffectType(int targetEntityId, GE.BuffEffectType effectType, float durationOverride = 0f, int sourceEntityId = -1)
        {
            if (TryGetBuffTableByEffectType(effectType, out BuffTable buffTable) == false)
                return -1;

            float duration = durationOverride > 0f ? durationOverride : buffTable.Duration;
            return AddBuff(targetEntityId, buffTable.Id, duration, sourceEntityId);
        }

        public static int AddDamageBasedBuffByEffectType(int targetEntityId, GE.BuffEffectType effectType, float dealtDamage, float durationOverride = 0f, int sourceEntityId = -1)
        {
            if (TryGetBuffTableByEffectType(effectType, out BuffTable buffTable) == false)
                return -1;

            float duration = durationOverride > 0f ? durationOverride : buffTable.Duration;
            return AddDamageBasedBuff(targetEntityId, buffTable.Id, dealtDamage, duration, sourceEntityId);
        }

        public static int AddDamageBasedBuff(int targetEntityId, int buffTableID, float dealtDamage, float duration, int sourceEntityId = -1)
        {
            if (dealtDamage <= 0f)
                return -1;

            BuffTable buffTable = AR.s.Data?.GetBuff(buffTableID);
            if (buffTable == null)
            {
                Debug.LogError($"[BuffHelper] BuffTable not found - TableID: {buffTableID}");
                return -1;
            }

            int buffEntityId = AddBuff(targetEntityId, buffTableID, duration, sourceEntityId);
            if (buffEntityId == -1)
                return -1;

            ApplyDamageBasedBuffEffect(buffEntityId, buffTable, dealtDamage);
            return buffEntityId;
        }

        /// <summary>
        /// 타겟 엔티티에 버프 추가
        /// 같은 타입의 버프가 이미 존재하면 StackCount를 증가시킴
        /// </summary>
        /// <param name="targetEntityId">버프를 받을 엔티티 ID</param>
        /// <param name="buffTableID">버프 테이블 ID</param>
        /// <param name="duration">지속 시간 (초)</param>
        /// <returns>버프 Entity ID (신규 생성 또는 기존), 실패 시 -1</returns>
        public static int AddBuff(int targetEntityId, int buffTableID, float duration, int sourceEntityId = -1)
        {
            // 1. 타겟 엔티티 유효성 검증
            if (AR.s.Entity.IsEntityRegistered(targetEntityId) == false)
            {
                Debug.LogError($"[BuffHelper] Target entity {targetEntityId} is not registered");
                return -1;
            }

            // 2. 버프 테이블 데이터 로드
            var buffTable = AR.s.Data?.GetBuff(buffTableID);
            if (buffTable == null)
            {
                Debug.LogError($"[BuffHelper] BuffTable not found - TableID: {buffTableID}");
                return -1;
            }

            // 효과가 서서히 약해지는 FadeOutTime 구간은 지속 시간 뒤에 붙는다 (0이면 기존과 동일)
            duration += Mathf.Max(0f, buffTable.FadeOutTime);

            // 3. 이미 같은 버프가 존재하는지 먼저 확인
            int buffEntityId = EntityManager.GetDeterministicId(targetEntityId, EntityIdCategory.Buff, buffTableID);

            if (AR.s.Entity.IsValidBuffEntity(buffEntityId))
            {
                // 이미 같은 버프가 존재 - StackCount 증가
                if (AR.s.Component.TryGetComponent<BuffInstance>(buffEntityId, out BuffInstance existingBuff))
                {
                    if(existingBuff.StackCount < buffTable.MaxStack) // 최대 스택 수 체크
                    {
                        existingBuff.StackCount++;    
                    }
                    
                    existingBuff.RemainTime = duration; // 지속 시간 갱신
                    existingBuff.Duration = duration;
                    // Stacks are merged by type; the latest applying source owns the refreshed tick.
                    existingBuff.Credit = Skill.Combat.CombatCredit.Resolve(sourceEntityId);
                    AR.s.Component.SetComponent(buffEntityId, existingBuff);

                    Debug.Log($"[BuffHelper] Buff stacked - BuffEntityId: {buffEntityId}, Target: {targetEntityId}, TableID: {buffTableID}, StackCount: {existingBuff.StackCount}, Duration: {duration}");

                    // Dirty 태그 추가 (스탯 재계산 요청 - 스택 증가 시 스탯 효과 재계산)
                    AR.s.Component.AddComponent(targetEntityId, new StatDirtyTag());

                    return buffEntityId;
                }
                else
                {
                    Debug.LogError($"[BuffHelper] Buff entity {buffEntityId} exists but has no BuffInstance component");
                    return -1;
                }
            }

            // 4. 신규 버프 Entity 생성 시도
            buffEntityId = AR.s.Entity.CreateBuffEntity(targetEntityId, buffTableID);

            if (buffEntityId == -1)
            {
                Debug.LogError($"[BuffHelper] Failed to create buff entity for target {targetEntityId}, table {buffTableID}");
                return -1;
            }

            // 5. 신규 버프 - BuffInstance 컴포넌트 추가 (테이블 데이터 복사)
            BuffInstance newBuff = new BuffInstance(targetEntityId, buffTableID, duration);
            newBuff.Credit = Skill.Combat.CombatCredit.Resolve(sourceEntityId);

            // 틱 데이터를 테이블에서 복사
            newBuff.TickInterval = buffTable.TickInterval;

            ApplyBuffEffect(ref newBuff, targetEntityId, buffTable);

            newBuff.LastTickTime = duration; // RemainTime 기준이므로 duration으로 초기화

            AR.s.Component.AddComponent(buffEntityId, newBuff);

            // 5. 버프 효과 로드 및 StatModifier 추가
            LoadBuffEffects(buffEntityId, targetEntityId, buffTable);

            // 6. Dirty 태그 추가 (스탯 재계산 요청)
            AR.s.Component.AddComponent(targetEntityId, new StatDirtyTag());

            Debug.Log($"[BuffHelper] Buff added - BuffEntityId: {buffEntityId}, Target: {targetEntityId}, TableID: {buffTableID}, Duration: {duration}");
            return buffEntityId;
        }

        /// <summary>
        /// 버프 제거
        /// </summary>
        /// <param name="buffEntityId">제거할 버프 Entity ID</param>
        public static void RemoveBuff(int buffEntityId)
        {
            // 1. BuffInstance 가져오기
            if (AR.s.Component.TryGetComponent<BuffInstance>(buffEntityId, out BuffInstance buff) == false)
            {
                Debug.LogWarning($"[BuffHelper] Buff entity {buffEntityId} has no BuffInstance component");
                return;
            }

            int targetEntityId = buff.TargetEntityId;
            int buffTableID = buff.BuffTableID;

            // 2. StatModifier 제거 (버프로 인한 스탯 효과 제거)
            RemoveBuffModifiers(targetEntityId, buffEntityId);

            // 3. BuffInstance 컴포넌트 제거
            AR.s.Component.RemoveComponent<BuffInstance>(buffEntityId);

            // 4. 버프 Entity 삭제 (결정적 ID 슬롯 해제)
            AR.s.Entity.DestroyBuffEntity(buffEntityId);

            // 5. Dirty 태그 추가 (스탯 재계산 요청)
            AR.s.Component.AddComponent(targetEntityId, new StatDirtyTag());

            Debug.Log($"[BuffHelper] Buff removed - BuffEntityId: {buffEntityId}, Target: {targetEntityId}, TableID: {buffTableID}");
        }

        /// <summary>
        /// 엔티티의 특정 버프 테이블 ID를 가진 버프 제거
        /// </summary>
        /// <param name="targetEntityId">타겟 엔티티 ID</param>
        /// <param name="buffTableID">제거할 버프 테이블 ID</param>
        /// <returns>제거된 버프 개수</returns>
        public static int RemoveBuffByTableID(int targetEntityId, int buffTableID)
        {
            SparseSet<BuffInstance> buffPool = AR.s.Component.GetComponentPool<BuffInstance>();
            if (buffPool == null || buffPool.Count == 0)
                return 0;

            int removedCount = 0;

            // 역순으로 순회 (제거 중에도 안전)
            for (int i = buffPool.Count - 1; i >= 0; i--)
            {
                int buffEntityId = buffPool.GetEntityId(i);
                BuffInstance buff = buffPool.GetByIndex(i);

                if (buff.TargetEntityId == targetEntityId && buff.BuffTableID == buffTableID)
                {
                    RemoveBuff(buffEntityId);
                    removedCount++;
                }
            }

            return removedCount;
        }

        /// <summary>
        /// 엔티티의 모든 버프 제거
        /// </summary>
        /// <param name="targetEntityId">타겟 엔티티 ID</param>
        /// <returns>제거된 버프 개수</returns>
        public static int RemoveAllBuffs(int targetEntityId)
        {
            SparseSet<BuffInstance> buffPool = AR.s.Component.GetComponentPool<BuffInstance>();
            if (buffPool == null || buffPool.Count == 0)
                return 0;

            int removedCount = 0;

            // 역순으로 순회 (제거 중에도 안전)
            for (int i = buffPool.Count - 1; i >= 0; i--)
            {
                int buffEntityId = buffPool.GetEntityId(i);
                BuffInstance buff = buffPool.GetByIndex(i);

                if (buff.TargetEntityId == targetEntityId)
                {
                    RemoveBuff(buffEntityId);
                    removedCount++;
                }
            }

            return removedCount;
        }

        /// <summary>
        /// 엔티티의 해제 가능한 버프·디버프만 제거한다.
        /// IsDispellable=false인 패시브·영구 효과는 유지한다.
        /// </summary>
        public static int RemoveAllDispellableBuffs(int targetEntityId)
        {
            SparseSet<BuffInstance> buffPool = AR.s.Component.GetComponentPool<BuffInstance>();
            if (buffPool == null || buffPool.Count == 0)
                return 0;

            int removedCount = 0;
            for (int i = buffPool.Count - 1; i >= 0; i--)
            {
                int buffEntityId = buffPool.GetEntityId(i);
                BuffInstance buff = buffPool.GetByIndex(i);
                if (buff.TargetEntityId != targetEntityId || IsDispellable(buff) == false)
                    continue;

                RemoveBuff(buffEntityId);
                removedCount++;
            }

            return removedCount;
        }

        /// <summary>
        /// 엔티티가 특정 버프를 보유하고 있는지 확인
        /// </summary>
        public static bool HasBuff(int targetEntityId, int buffTableID)
        {
            SparseSet<BuffInstance> buffPool = AR.s.Component.GetComponentPool<BuffInstance>();
            if (buffPool == null || buffPool.Count == 0)
                return false;

            for (int i = 0; i < buffPool.Count; i++)
            {
                BuffInstance buff = buffPool.GetByIndex(i);
                if (buff.TargetEntityId == targetEntityId && buff.BuffTableID == buffTableID)
                    return true;
            }

            return false;
        }

        /// <summary>
        /// 이동 둔화 버프가 이 엔티티의 일반 이동에 적용하는 배율. 둔화가 없으면 1.
        /// 둔화가 여러 개면 System_MoveSlow가 가장 강한 하나만 MoveSlowComponent에 남겨 둔다.
        /// 배율은 회복 구간에서 매 틱 변하므로 스탯에 캐시하지 않고 이동 속도를 계산할 때 조회한다.
        /// </summary>
        public static float GetMoveSlowMultiplier(int entityId)
        {
            if (AR.s.Component.TryGetComponent<MoveSlowComponent>(entityId, out MoveSlowComponent slow) == false)
                return 1f;

            return 1f - slow.SlowRate;
        }

        /// <summary>
        /// 엔티티의 버프 개수 반환
        /// </summary>
        public static int GetBuffCount(int targetEntityId)
        {
            SparseSet<BuffInstance> buffPool = AR.s.Component.GetComponentPool<BuffInstance>();
            if (buffPool == null || buffPool.Count == 0)
                return 0;

            int count = 0;
            for (int i = 0; i < buffPool.Count; i++)
            {
                if (buffPool.GetByIndex(i).TargetEntityId == targetEntityId)
                    count++;
            }

            return count;
        }

        /// <summary>엔티티가 보유한 해제 가능한 버프·디버프 개수.</summary>
        public static int GetDispellableBuffCount(int targetEntityId)
        {
            SparseSet<BuffInstance> buffPool = AR.s.Component.GetComponentPool<BuffInstance>();
            if (buffPool == null || buffPool.Count == 0)
                return 0;

            int count = 0;
            for (int i = 0; i < buffPool.Count; i++)
            {
                BuffInstance buff = buffPool.GetByIndex(i);
                if (buff.TargetEntityId == targetEntityId && IsDispellable(buff) == true)
                    count++;
            }

            return count;
        }

        private static bool IsDispellable(BuffInstance buff)
        {
            BuffTable buffTable = AR.s.Data?.GetBuff(buff.BuffTableID);
            if (buffTable != null)
                return buffTable.IsDispellable;

            // 테이블이 사라진 고아 버프는 패시브로 간주해 보존할 근거가 없으므로 정리한다.
            Debug.LogWarning($"[BuffHelper] BuffTable not found while checking dispel policy - TableID: {buff.BuffTableID}");
            return true;
        }

        private static void ApplyDamageBasedBuffEffect(int buffEntityId, BuffTable buffTable, float dealtDamage)
        {
            if (AR.s.Component.TryGetComponent<BuffInstance>(buffEntityId, out BuffInstance buff) == false)
            {
                Debug.LogWarning($"[BuffHelper] Damage based buff entity has no BuffInstance - BuffEntityId: {buffEntityId}, TableID: {buffTable.Id}");
                return;
            }

            switch (buffTable.EffectType)
            {
                case GE.BuffEffectType.Blooding:
                    buff.DamageType = GE.DamageType.Physics;
                    int newTickDamage = CalculateDamagePercent(dealtDamage, buffTable.EffectValue);
                    buff.TickDamage = Mathf.Max(buff.TickDamage, newTickDamage);
                    break;

                default:
                    return;
            }

            AR.s.Component.SetComponent(buffEntityId, buff);
        }

        private static int CalculateDamagePercent(float dealtDamage, int percent)
        {
            if (dealtDamage <= 0f || percent <= 0)
                return 0;

            int roundedDamage = Mathf.RoundToInt(dealtDamage);
            return Mathf.Max(1, Mathf.FloorToInt(roundedDamage * percent * 0.01f));
        }

        /// <summary>
        /// 버프 효과 타입에 따라 BuffInstance에 효과 데이터 설정
        /// </summary>
        private static void ApplyBuffEffect(ref BuffInstance buff, int targetEntityId, BuffTable buffTable)
        {
            if (AR.s.Component.TryGetComponent<StatComponent>(targetEntityId, out StatComponent _) == false)
            {
                Debug.LogWarning($"[BuffHelper] Target entity has no StatComponent - TargetEntityId: {targetEntityId}, BuffTableId: {buffTable.Id}");
                buff.TickDamage = 0;
                buff.DamageType = 0;
                return;
            }

            switch (buffTable.EffectType)
            {
                case GE.BuffEffectType.Blooding:
                    // 출혈: 최대 체력의 퍼센트로 계산
                    buff.DamageType = GE.DamageType.Physics;
                    buff.TickDamage = 0;
                    break;

                case GE.BuffEffectType.Ignite:
                    // 점화: EffectValue에 DoT 데미지 저장 (DamageCalculator에서 계산)
                    buff.DamageType = GE.DamageType.Fire;
                    buff.TickDamage = buffTable.EffectValue;
                    break;

                case GE.BuffEffectType.Poison:
                    // 중독: EffectValue에 DoT 데미지 저장
                    buff.DamageType = GE.DamageType.Poison;
                    buff.TickDamage = buffTable.EffectValue;
                    break;

                case GE.BuffEffectType.Chill:
                    // 냉기: DoT 없음, StatModifier만 사용
                    buff.DamageType = 0;
                    buff.TickDamage = 0;
                    break;

                default:
                    buff.TickDamage = 0;
                    buff.DamageType = 0;
                    break;
            }
        }

        /// <summary>
        /// 버프 효과를 테이블에서 로드하여 StatModifier 추가
        /// </summary>
        private static void LoadBuffEffects(int buffEntityId, int targetEntityId, BuffTable buffTable)
        {
            // 테이블 ID가 변경되어도 상태이상 효과가 유지되도록 EffectType으로 분기
            switch (buffTable.EffectType)
            {
                // ========== 상태 이상 버프 ==========
                case GE.BuffEffectType.Blooding: // 출혈 (DoT만, StatModifier 없음)
                    break;

                case GE.BuffEffectType.Ignite: // 점화 (DoT만, StatModifier 없음)
                    break;

                case GE.BuffEffectType.Chill: // 냉기 (이동/공격 속도 감소)
                    AddStatModifier(targetEntityId, buffEntityId, GE.Stat.MoveSpeed, StatModifierType.Multiply, -30, 0); // -30%
                    AddStatModifier(targetEntityId, buffEntityId, GE.Stat.AttackSpeed, StatModifierType.Multiply, -20, 0); // -20%
                    break;

                case GE.BuffEffectType.Poison: // 중독 (DoT + HP 재생 감소)
                    AddStatModifier(targetEntityId, buffEntityId, GE.Stat.HpGeneration, StatModifierType.Multiply, -50, 0); // HP 재생 -50%
                    break;

                case GE.BuffEffectType.Slow: // 둔화 (이동 속도만 감소). 서서히 회복되도록 StatModifier 대신 System_MoveSlow가 이동 배율로 적용
                    AR.s.Component.AddComponent(buffEntityId, new MoveSlowBuffComponent
                    {
                        SlowRate = Mathf.Clamp01(buffTable.EffectValue / 100f),
                        FadeOutTime = Mathf.Max(0f, buffTable.FadeOutTime),
                    });
                    break;

                case GE.BuffEffectType.StatModifier: // 시트의 Stat1~3 칸 그대로 (몬스터 오라 등, MONSTER_AFFIX_DESIGN.md §6.3)
                    AddTableStatModifier(targetEntityId, buffEntityId, buffTable.Stat1, buffTable.ModType1, buffTable.Value1);
                    AddTableStatModifier(targetEntityId, buffEntityId, buffTable.Stat2, buffTable.ModType2, buffTable.Value2);
                    AddTableStatModifier(targetEntityId, buffEntityId, buffTable.Stat3, buffTable.ModType3, buffTable.Value3);
                    break;
            }

            // ========== 일반 버프 (예시) ==========
            switch (buffTable.Id)
            {
                case 1001: // 공격력 버프
                    AddStatModifier(targetEntityId, buffEntityId, GE.Stat.AttackMin, StatModifierType.Add, 10, 0);
                    AddStatModifier(targetEntityId, buffEntityId, GE.Stat.AttackMax, StatModifierType.Add, 10, 0);
                    break;

                case 1002: // 방어력 버프
                    AddStatModifier(targetEntityId, buffEntityId, GE.Stat.Defense, StatModifierType.Add, 20, 0);
                    break;

                case 1003: // 이동속도 버프
                    AddStatModifier(targetEntityId, buffEntityId, GE.Stat.MoveSpeed, StatModifierType.Multiply, 30, 0); // 30% 증가
                    break;
            }
        }

        /// <summary>
        /// 버프 시트 스탯 칸 한 세트를 StatModifier로 추가한다. 값이 0인 칸은 비어 있는 것으로 보고 건너뛴다.
        /// </summary>
        private static void AddTableStatModifier(int targetEntityId, int sourceBuffEntityId, GE.Stat statType, StatModifierType modifierType, int value)
        {
            if (value == 0)
                return;

            AddStatModifier(targetEntityId, sourceBuffEntityId, statType, modifierType, value, 0);
        }

        /// <summary>
        /// StatModifier를 타겟 엔티티에 추가
        /// </summary>
        private static void AddStatModifier(int targetEntityId, int sourceBuffEntityId, GE.Stat statType, StatModifierType modifierType, int value, int priority)
        {
            AR.s.Data.AddStatModifier(targetEntityId, StatModifierSource.Buff, sourceBuffEntityId, statType, modifierType, value, priority);
        }

        /// <summary>
        /// 버프로 인한 StatModifier 제거
        /// </summary>
        private static void RemoveBuffModifiers(int targetEntityId, int buffEntityId)
        {
            int removedCount = AR.s.Data.RemoveModifiersBySource(targetEntityId, StatModifierSource.Buff, buffEntityId);
            Debug.Log($"[BuffHelper] StatModifiers removed - Target: {targetEntityId}, BuffEntity: {buffEntityId}, Removed: {removedCount}");
        }
    }
}
