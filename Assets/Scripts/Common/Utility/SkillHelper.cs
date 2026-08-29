#nullable enable
using ARPG.Component;
using ARPG.Manager;
using ARPG.Tables;
using UnityEngine;

namespace ARPG.Utility
{
    /// <summary>
    /// 스킬 관련 유틸리티
    /// </summary>
    public static class SkillHelper
    {
        /// <summary>AI가 탐색할 스킬 슬롯 수 (AiTable.SkillId1/2/3)</summary>
        public const int AiSkillSlotCount = 3;

        /// <summary>
        /// Chase/Attack 상태 전이 판정에 사용할 교전 사거리 제곱값.
        /// 유효 사거리 = `attacker.HitRadius + SkillRangeMax + target.HitRadius` (몸 가장자리에서 몸 가장자리로의 도달거리).
        /// SkillRangeMax 의미: "몸 밖으로 뻗는 추가 사거리(팔/무기 길이)" — 큰 몹은 같은 SkillRangeMax로도 멀리 도달.
        /// 실제 hit 판정(System_Skill.CircleVsSector)도 동일하게 `attacker.HitRadius + range + target.HitRadius`를 사용.
        /// - 사용 가능(쿨타임 풀림)한 스킬이 있으면 그 중 최대 유효 사거리의 제곱
        /// - 모두 쿨타임 중이면 전체 스킬 중 최대 유효 사거리의 제곱 (대기 기준)
        /// </summary>
        public static float GetEngagementRangeSqr(int ownerEntityId, int targetEntityId, int slotCount)
        {
            float ownerHitRadius = GetEntityHitRadius(ownerEntityId);
            float targetHitRadius = GetEntityHitRadius(targetEntityId);
            float readyMaxSqr = 0f;
            float allMaxSqr = 0f;

            for (int i = 0; i < slotCount; i++)
            {
                int skillEntityId = EntityManager.GetDeterministicId(ownerEntityId, EntityIdCategory.Skill, i);
                if (skillEntityId == -1)
                    continue;

                if (AR.s.Component.TryGetComponent<SkillComponent>(skillEntityId, out var skill) == false)
                    continue;
                if (skill.Table == null)
                    continue;

                float rangeMax = skill.Table.SkillRangeMax;
                if (rangeMax <= 0f)
                    continue;

                float effectiveRange = ownerHitRadius + rangeMax + targetHitRadius;
                float rangeSqr = effectiveRange * effectiveRange;
                if (rangeSqr > allMaxSqr)
                    allMaxSqr = rangeSqr;

                bool isReady = true;
                if (AR.s.Component.TryGetComponent<SkillStateComponent>(skillEntityId, out var state))
                {
                    if (state.IsRunning || state.IsCooldownReady == false)
                        isReady = false;
                }

                if (isReady && rangeSqr > readyMaxSqr)
                    readyMaxSqr = rangeSqr;
            }

            return readyMaxSqr > 0f ? readyMaxSqr : allMaxSqr;
        }

        public static float GetEntityHitRadius(int entityId)
        {
            if (entityId == -1)
                return 0f;
            if (AR.s.Component.TryGetComponent<ColliderComponent>(entityId, out var collider) == false)
                return 0f;
            return collider.HitRadius;
        }

        /// <summary>
        /// 실행 중인 스킬이 이 엔티티의 일반 이동에 적용하는 배율. 페널티가 없으면 1.
        /// 시전자 종류(플레이어/AI)를 구분하지 않으므로 이동 속도를 계산하는 모든 경로가 그대로 호출한다.
        /// 배율은 스킬 단계에 따라 매 틱 변하므로(End에서 1로 선형 복구) 스탯에 캐시하지 않고 그때그때 조회한다.
        /// </summary>
        public static float GetSkillMovementMultiplier(int entityId)
        {
            if (AR.s.Component.TryGetComponent<SkillMovementPenaltyComponent>(entityId, out var penalty) == false)
                return 1f;

            return penalty.CurrentMultiplier;
        }

        /// <summary>
        /// AI 엔티티가 발동할 스킬을 가중치 기반 랜덤으로 선택.
        /// AiTable.SkillWeight1/2/3 가중치와 쿨타임/사거리 조건을 모두 만족하는 슬롯 중 하나를 가중치 비율로 뽑는다.
        /// 사거리 비교 시 `attacker.HitRadius + SkillRangeMax + target.HitRadius`를 유효 사거리로 사용 (몸 가장자리 기준).
        /// </summary>
        /// <returns>발동 가능한 스킬 슬롯 인덱스, 없으면 -1</returns>
        public static int PickFireSkill(int ownerEntityId, int targetEntityId, Vector2 targetPosition, int slotCount)
        {
            if (AR.s.Component.TryGetComponent<AIComponent>(ownerEntityId, out var ai) == false)
                return -1;

            AiTable? aiTable = AR.s.Data.GetAiTable(ai.AITableID);
            if (aiTable == null)
                return -1;

            if (AR.s.Component.TryGetComponent<TransformComponent>(ownerEntityId, out var ownerTransform) == false)
                return -1;

            float sqrDistance = (ownerTransform.Position - targetPosition).sqrMagnitude;
            float ownerHitRadius = GetEntityHitRadius(ownerEntityId);
            float targetHitRadius = GetEntityHitRadius(targetEntityId);

            // 1차 패스: 총 가중치 계산
            int totalWeight = 0;
            for (int i = 0; i < slotCount; i++)
            {
                if (IsFireCandidate(ownerEntityId, i, aiTable, sqrDistance, ownerHitRadius, targetHitRadius, out int weight))
                    totalWeight += weight;
            }

            if (totalWeight <= 0)
                return -1;

            // 2차 패스: 가중치 랜덤 롤
            int roll = Random.Range(0, totalWeight);
            int acc = 0;
            for (int i = 0; i < slotCount; i++)
            {
                if (IsFireCandidate(ownerEntityId, i, aiTable, sqrDistance, ownerHitRadius, targetHitRadius, out int weight) == false)
                    continue;

                acc += weight;
                if (roll < acc)
                    return i;
            }

            return -1;
        }

        private static bool IsFireCandidate(int ownerEntityId, int slotIndex, AiTable aiTable, float sqrDistance, float ownerHitRadius, float targetHitRadius, out int weight)
        {
            weight = GetSkillWeight(aiTable, slotIndex);
            if (weight <= 0)
                return false;

            int skillEntityId = EntityManager.GetDeterministicId(ownerEntityId, EntityIdCategory.Skill, slotIndex);
            if (skillEntityId == -1)
                return false;

            if (AR.s.Component.TryGetComponent<SkillComponent>(skillEntityId, out var skill) == false)
                return false;
            if (skill.Table == null)
                return false;

            if (AR.s.Component.TryGetComponent<SkillStateComponent>(skillEntityId, out var state))
            {
                if (state.IsRunning || state.IsCooldownReady == false)
                    return false;
            }

            float rangeMax = skill.Table.SkillRangeMax;
            if (rangeMax > 0f)
            {
                float effectiveMax = ownerHitRadius + rangeMax + targetHitRadius;
                if (sqrDistance > effectiveMax * effectiveMax)
                    return false;
            }

            float rangeMin = skill.Table.SkillRangeMin;
            if (rangeMin > 0f && sqrDistance < rangeMin * rangeMin)
                return false;

            return true;
        }

        private static int GetSkillWeight(AiTable aiTable, int slotIndex)
        {
            switch (slotIndex)
            {
                case 0: return aiTable.SkillWeight1;
                case 1: return aiTable.SkillWeight2;
                case 2: return aiTable.SkillWeight3;
                default: return 0;
            }
        }

        /// <summary>
        /// 슬롯 인덱스와 엔티티 ID로 SkillCommandComponent를 생성
        /// </summary>
        /// <param name="slotIndex">스킬 슬롯 인덱스 (0부터 시작)</param>
        /// <param name="entityId">캐릭터 엔티티 ID</param>
        /// <param name="targetPosition">타겟 위치</param>
        /// <param name="command">생성된 SkillCommandComponent (out)</param>
        /// <returns>성공 시 true, 스킬이 없거나 실행 중이면 false</returns>
        public static bool GetSkillCommandComponent(int slotIndex, int entityId, Vector2 targetPosition, out SkillCommandComponent command)
        {
            int skillEntityId = EntityManager.GetDeterministicId(entityId, EntityIdCategory.Skill, slotIndex);
            if (skillEntityId == -1)
            {
                command = default;
                return false;
            }

            // 스킬이 실제로 존재하는지 확인
            if (AR.s.Component.TryGetComponent<SkillComponent>(skillEntityId, out var skill) == false)
            {
                Debug.LogWarning($"[SkillHelper] Skill not found at slot {slotIndex}");
                command = default;
                return false;
            }

            // 스킬이 실행 중이거나 쿨타임 중이면 커맨드 생성하지 않음
            if (AR.s.Component.TryGetComponent<SkillStateComponent>(skillEntityId, out var skillState))
            {
                if (skillState.IsRunning)
                {
                    command = default;
                    return false;
                }

                if (skillState.IsCooldownReady == false)
                {
                    command = default;
                    return false;
                }
            }

            // SkillCommandComponent 생성
            command = new SkillCommandComponent();
            if (skill.Table != null)
            {
                command.TargetType = skill.Table.SkillTargetType;
            }
            else
            {
                Debug.LogError($"[SkillHelper] Skill.Table is null for SkillId({skill.SkillId}), SkillEntityId({skillEntityId})");
            }

            command.SkillEntityId = skillEntityId;
            command.TargetPosition = targetPosition;

            return true;
        }
    }
}
