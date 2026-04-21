#nullable enable
using ARPG.Component;
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
        /// - 사용 가능(쿨타임 풀림)한 스킬이 있으면 그 중 최대 SkillRangeMax의 제곱
        /// - 모두 쿨타임 중이면 전체 스킬 중 최대 SkillRangeMax의 제곱 (대기 기준)
        /// </summary>
        public static float GetEngagementRangeSqr(int ownerEntityId, int slotCount)
        {
            float readyMaxSqr = 0f;
            float allMaxSqr = 0f;

            for (int i = 0; i < slotCount; i++)
            {
                int skillEntityId = EntityIdHelper.GetDeterministicId(ownerEntityId, EntityIdCategory.Skill, i);
                if (skillEntityId == -1)
                    continue;

                if (AR.s.Component.TryGetComponent<SkillComponent>(skillEntityId, out var skill) == false)
                    continue;
                if (skill.Table == null)
                    continue;

                float rangeMax = skill.Table.SkillRangeMax;
                if (rangeMax <= 0f)
                    continue;

                float rangeSqr = rangeMax * rangeMax;
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

        /// <summary>
        /// AI 엔티티가 발동할 스킬을 가중치 기반 랜덤으로 선택.
        /// AiTable.SkillWeight1/2/3 가중치와 쿨타임/사거리 조건을 모두 만족하는 슬롯 중 하나를 가중치 비율로 뽑는다.
        /// </summary>
        /// <returns>발동 가능한 스킬 슬롯 인덱스, 없으면 -1</returns>
        public static int PickFireSkill(int ownerEntityId, Vector2 targetPosition, int slotCount)
        {
            if (AR.s.Component.TryGetComponent<AIComponent>(ownerEntityId, out var ai) == false)
                return -1;

            AiTable? aiTable = AR.s.Data.GetAiTable(ai.AITableID);
            if (aiTable == null)
                return -1;

            if (AR.s.Component.TryGetComponent<TransformComponent>(ownerEntityId, out var ownerTransform) == false)
                return -1;

            float sqrDistance = (ownerTransform.Position - targetPosition).sqrMagnitude;

            // 1차 패스: 총 가중치 계산
            int totalWeight = 0;
            for (int i = 0; i < slotCount; i++)
            {
                if (IsFireCandidate(ownerEntityId, i, aiTable, sqrDistance, out int weight))
                    totalWeight += weight;
            }

            if (totalWeight <= 0)
                return -1;

            // 2차 패스: 가중치 랜덤 롤
            int roll = Random.Range(0, totalWeight);
            int acc = 0;
            for (int i = 0; i < slotCount; i++)
            {
                if (IsFireCandidate(ownerEntityId, i, aiTable, sqrDistance, out int weight) == false)
                    continue;

                acc += weight;
                if (roll < acc)
                    return i;
            }

            return -1;
        }

        private static bool IsFireCandidate(int ownerEntityId, int slotIndex, AiTable aiTable, float sqrDistance, out int weight)
        {
            weight = GetSkillWeight(aiTable, slotIndex);
            if (weight <= 0)
                return false;

            int skillEntityId = EntityIdHelper.GetDeterministicId(ownerEntityId, EntityIdCategory.Skill, slotIndex);
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
            if (rangeMax > 0f && sqrDistance > rangeMax * rangeMax)
                return false;

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
            int skillEntityId = EntityIdHelper.GetDeterministicId(entityId, EntityIdCategory.Skill, slotIndex);
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
