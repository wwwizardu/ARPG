#nullable enable
using ARPG.Component;
using UnityEngine;

namespace ARPG.Utility
{
    /// <summary>
    /// 스킬 관련 유틸리티
    /// </summary>
    public static class SkillHelper
    {
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
            command = new SkillCommandComponent();

            int skillEntityId = EntityIdHelper.GetDeterministicId(entityId, EntityIdCategory.Skill, slotIndex);
            if (skillEntityId == -1)
            {
                return false;
            }

            // 스킬이 실제로 존재하는지 확인
            if (AR.s.Component.TryGetComponent<SkillComponent>(skillEntityId, out var skill) == false)
            {
                Debug.LogWarning($"[SkillHelper] Skill not found at slot {slotIndex}");
                return false;
            }

            // 스킬이 실행 중이면 커맨드 생성하지 않음
            if (AR.s.Component.TryGetComponent<SkillStateComponent>(skillEntityId, out var skillState))
            {
                if (skillState.IsRunning)
                {
                    return false;
                }
            }

            // SkillCommandComponent 생성
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
