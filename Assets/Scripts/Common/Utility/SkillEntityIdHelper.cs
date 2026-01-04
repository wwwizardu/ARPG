#nullable enable

namespace ARPG.Utility
{
    /// <summary>
    /// 스킬 엔티티 ID를 생성하고 파싱하는 헬퍼 클래스
    ///
    /// ID 생성 방식: characterEntityId + (slotIndex + 1) * SKILL_SLOT_OFFSET
    /// 예시:
    /// - Character ID: 12345
    /// - Skill Slot 0: 12345 + 1*1000000 = 1012345
    /// - Skill Slot 1: 12345 + 2*1000000 = 2012345
    /// - Skill Slot 2: 12345 + 3*1000000 = 3012345
    ///
    /// 이렇게 하면 ID만 봐도 어떤 캐릭터의 몇 번 슬롯인지 파악 가능
    /// </summary>
    public static class SkillEntityIdHelper
    {
        /// <summary>
        /// 스킬 슬롯 오프셋 (1,000,000)
        /// 캐릭터당 최대 999,999개의 스킬 슬롯 지원 (실제로는 훨씬 적게 사용)
        /// </summary>
        private const int SKILL_SLOT_OFFSET = 1000000;

        /// <summary>
        /// 최대 스킬 슬롯 수 (안전성을 위한 상한선)
        /// </summary>
        public const int MAX_SKILL_SLOTS = 100;

        /// <summary>
        /// 캐릭터 엔티티 ID와 슬롯 인덱스로 스킬 엔티티 ID 계산
        /// </summary>
        /// <param name="characterEntityId">캐릭터 엔티티 ID</param>
        /// <param name="slotIndex">스킬 슬롯 인덱스 (0부터 시작)</param>
        /// <returns>스킬 엔티티 ID</returns>
        public static int GetSkillEntityId(int characterEntityId, int slotIndex)
        {
            if (slotIndex < 0 || slotIndex >= MAX_SKILL_SLOTS)
            {
                UnityEngine.Debug.LogError($"[SkillEntityIdHelper] Invalid slot index: {slotIndex}. Must be between 0 and {MAX_SKILL_SLOTS - 1}");
                return -1;
            }

            return characterEntityId + (slotIndex + 1) * SKILL_SLOT_OFFSET;
        }

        /// <summary>
        /// 스킬 엔티티 ID에서 캐릭터 엔티티 ID 추출
        /// </summary>
        /// <param name="skillEntityId">스킬 엔티티 ID</param>
        /// <returns>캐릭터 엔티티 ID</returns>
        public static int GetCharacterEntityId(int skillEntityId)
        {
            return skillEntityId % SKILL_SLOT_OFFSET;
        }

        /// <summary>
        /// 스킬 엔티티 ID와 캐릭터 엔티티 ID로부터 슬롯 인덱스 추출
        /// </summary>
        /// <param name="skillEntityId">스킬 엔티티 ID</param>
        /// <param name="characterEntityId">캐릭터 엔티티 ID</param>
        /// <returns>슬롯 인덱스 (0부터 시작)</returns>
        public static int GetSlotIndex(int skillEntityId, int characterEntityId)
        {
            return (skillEntityId - characterEntityId) / SKILL_SLOT_OFFSET - 1;
        }

        /// <summary>
        /// 스킬 엔티티 ID가 유효한지 확인
        /// </summary>
        /// <param name="skillEntityId">스킬 엔티티 ID</param>
        /// <param name="characterEntityId">캐릭터 엔티티 ID</param>
        /// <returns>유효하면 true</returns>
        public static bool IsValidSkillEntityId(int skillEntityId, int characterEntityId)
        {
            int slotIndex = GetSlotIndex(skillEntityId, characterEntityId);
            return slotIndex >= 0 && slotIndex < MAX_SKILL_SLOTS;
        }

        /// <summary>
        /// 디버그용: 스킬 엔티티 ID 정보를 문자열로 반환
        /// </summary>
        public static string GetDebugString(int skillEntityId)
        {
            int characterId = GetCharacterEntityId(skillEntityId);
            int slotIndex = GetSlotIndex(skillEntityId, characterId);
            return $"SkillEntityId={skillEntityId} (CharacterId={characterId}, SlotIndex={slotIndex})";
        }
    }
}
