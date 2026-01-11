using ARPG.Component;
using ARPG.Utility;
using UnityEngine;
using GE = GlobalEnum;

namespace ARPG.Utility
{
    /// <summary>
    /// 버프 시스템 유틸리티
    /// 버프 추가, 제거, 조회 등의 기능 제공
    /// BuffInstance만으로 모든 버프를 관리 (BuffListComponent 제거)
    /// </summary>
    public static class BuffSystem
    {
        /// <summary>
        /// 타겟 엔티티에 버프 추가
        /// 같은 타입의 버프가 이미 존재하면 StackCount를 증가시킴
        /// </summary>
        /// <param name="targetEntityId">버프를 받을 엔티티 ID</param>
        /// <param name="buffTableID">버프 테이블 ID</param>
        /// <param name="duration">지속 시간 (초)</param>
        /// <returns>버프 Entity ID (신규 생성 또는 기존), 실패 시 -1</returns>
        public static int AddBuff(int targetEntityId, int buffTableID, float duration)
        {
            // 1. 타겟 엔티티 유효성 검증
            if (!EntityIdHelper.IsEntityRegistered(targetEntityId))
            {
                Debug.LogWarning($"[BuffSystem] Target entity {targetEntityId} is not registered");
                return -1;
            }

            // 2. 버프 Entity 생성 시도 (결정적 ID 사용)
            int buffEntityId = EntityIdHelper.CreateBuffEntity(targetEntityId, buffTableID);

            if (buffEntityId == -1)
            {
                // 이미 같은 버프가 존재 - StackCount 증가
                buffEntityId = BuffEntityIdHelper.GetBuffEntityId(targetEntityId, buffTableID);

                if (AR.s.Component.TryGetComponent<BuffInstance>(buffEntityId, out BuffInstance existingBuff))
                {
                    existingBuff.StackCount++;
                    existingBuff.RemainTime = duration; // 지속 시간 갱신
                    existingBuff.Duration = duration;
                    AR.s.Component.SetComponent(buffEntityId, existingBuff);

                    Debug.Log($"[BuffSystem] Buff stacked - BuffEntityId: {buffEntityId}, Target: {targetEntityId}, TableID: {buffTableID}, StackCount: {existingBuff.StackCount}, Duration: {duration}");

                    // Dirty 태그 추가 (스탯 재계산 요청 - 스택 증가 시 스탯 효과 재계산)
                    AR.s.Component.AddComponent(targetEntityId, new StatDirtyTag());

                    return buffEntityId;
                }
                else
                {
                    Debug.LogWarning($"[BuffSystem] Failed to create or find buff entity for target {targetEntityId}, table {buffTableID}");
                    return -1;
                }
            }

            // 3. 신규 버프 - BuffInstance 컴포넌트 추가
            AR.s.Component.AddComponent(buffEntityId, new BuffInstance(targetEntityId, buffTableID, duration));

            // 4. 버프 효과 로드 및 StatModifier 추가
            LoadBuffEffects(buffEntityId, targetEntityId, buffTableID);

            // 5. Dirty 태그 추가 (스탯 재계산 요청)
            AR.s.Component.AddComponent(targetEntityId, new StatDirtyTag());

            Debug.Log($"[BuffSystem] Buff added - BuffEntityId: {buffEntityId}, Target: {targetEntityId}, TableID: {buffTableID}, Duration: {duration}");
            return buffEntityId;
        }

        /// <summary>
        /// 버프 제거
        /// </summary>
        /// <param name="buffEntityId">제거할 버프 Entity ID</param>
        public static void RemoveBuff(int buffEntityId)
        {
            // 1. BuffInstance 가져오기
            if (!AR.s.Component.TryGetComponent<BuffInstance>(buffEntityId, out BuffInstance buff))
            {
                Debug.LogWarning($"[BuffSystem] Buff entity {buffEntityId} has no BuffInstance component");
                return;
            }

            int targetEntityId = buff.TargetEntityId;
            int buffTableID = buff.BuffTableID;

            // 2. StatModifier 제거 (버프로 인한 스탯 효과 제거)
            RemoveBuffModifiers(targetEntityId, buffEntityId);

            // 3. BuffInstance 컴포넌트 제거
            AR.s.Component.RemoveComponent<BuffInstance>(buffEntityId);

            // 4. 버프 Entity 삭제 (결정적 ID 슬롯 해제)
            EntityIdHelper.DestroyBuffEntity(buffEntityId);

            // 5. Dirty 태그 추가 (스탯 재계산 요청)
            AR.s.Component.AddComponent(targetEntityId, new StatDirtyTag());

            Debug.Log($"[BuffSystem] Buff removed - BuffEntityId: {buffEntityId}, Target: {targetEntityId}, TableID: {buffTableID}");
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

        /// <summary>
        /// 버프 효과를 테이블에서 로드하여 StatModifier 추가
        /// TODO: 실제 버프 테이블 구현 시 수정 필요
        /// </summary>
        private static void LoadBuffEffects(int buffEntityId, int targetEntityId, int buffTableID)
        {
            // TODO: 버프 테이블에서 효과 데이터 로드
            // 현재는 예시로 간단한 효과 추가

            // 예시: buffTableID에 따라 다른 효과 적용
            switch (buffTableID)
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
        /// StatModifier를 타겟 엔티티에 추가
        /// </summary>
        private static void AddStatModifier(int targetEntityId, int sourceBuffEntityId, GE.Stat statType, StatModifierType modifierType, int value, int priority)
        {
            StatModifier modifier = new StatModifier(
                statType,
                modifierType,
                StatModifierSource.Buff,
                sourceBuffEntityId,  // 버프 Entity ID를 SourceId로 사용
                value,
                priority
            );

            // StatModifier Entity 생성
            int modifierEntityId = EntityIdHelper.CreateEntity();
            AR.s.Component.AddComponent(modifierEntityId, new StatModifierComponent(targetEntityId, modifier));

            Debug.Log($"[BuffSystem] StatModifier added - ModifierEntity: {modifierEntityId}, Target: {targetEntityId}, Stat: {statType}, Type: {modifierType}, Value: {value}");
        }

        /// <summary>
        /// 버프로 인한 StatModifier 제거
        /// </summary>
        private static void RemoveBuffModifiers(int targetEntityId, int buffEntityId)
        {
            // StatModifierComponent 풀에서 해당 버프로 인한 modifier들 찾아서 제거
            SparseSet<StatModifierComponent> modifierPool = AR.s.Component.GetComponentPool<StatModifierComponent>();
            if (modifierPool == null || modifierPool.Count == 0)
                return;

            int removedCount = 0;

            // 역순으로 순회 (제거 중에도 안전)
            for (int i = modifierPool.Count - 1; i >= 0; i--)
            {
                int modifierEntityId = modifierPool.GetEntityId(i);
                StatModifierComponent modComp = modifierPool.GetByIndex(i);

                // 이 modifier가 해당 버프로 인한 것인지 확인
                if (modComp.OwnerEntityId == targetEntityId &&
                    modComp.Modifier.Source == StatModifierSource.Buff &&
                    modComp.Modifier.SourceId == buffEntityId)
                {
                    // StatModifierComponent 제거
                    AR.s.Component.RemoveComponent<StatModifierComponent>(modifierEntityId);

                    // Modifier Entity 삭제
                    EntityIdHelper.DestroyEntity(modifierEntityId);

                    removedCount++;
                }
            }

            Debug.Log($"[BuffSystem] StatModifiers removed - Target: {targetEntityId}, BuffEntity: {buffEntityId}, Removed: {removedCount}");
        }
    }
}
