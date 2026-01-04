#nullable enable
using System.Collections.Generic;
using UnityEngine;

namespace ARPG.Utility
{
    /// <summary>
    /// 엔티티 ID를 중앙에서 관리하는 헬퍼 클래스
    /// - 일반 엔티티: 0부터 순차적으로 증가하는 ID 할당
    /// - 스킬 엔티티: SkillEntityIdHelper를 사용하여 생성
    /// - 엔티티 ID 중복 방지 및 유효성 검증
    /// </summary>
    public static class EntityIdHelper
    {
        /// <summary>
        /// 다음에 할당될 일반 엔티티 ID
        /// </summary>
        private static int _nextEntityId = 0;

        /// <summary>
        /// 등록된 모든 엔티티 ID를 추적하는 HashSet
        /// </summary>
        private static readonly HashSet<int> _registeredEntityIds = new HashSet<int>();

        /// <summary>
        /// 캐릭터별 할당된 스킬 슬롯을 추적 (characterEntityId -> 사용중인 슬롯 인덱스들)
        /// </summary>
        private static readonly Dictionary<int, HashSet<int>> _characterSkillSlots = new Dictionary<int, HashSet<int>>();

        /// <summary>
        /// 재사용 가능한 엔티티 ID 풀 (삭제된 ID를 재활용)
        /// </summary>
        private static readonly Queue<int> _recycledEntityIds = new Queue<int>();

        /// <summary>
        /// 헬퍼 초기화 (게임 시작 시 또는 씬 전환 시 호출)
        /// </summary>
        public static void Initialize()
        {
            Reset();
            Debug.Log("[EntityIdHelper] Initialized");
        }

        /// <summary>
        /// 모든 엔티티 정보 초기화
        /// </summary>
        public static void Reset()
        {
            _nextEntityId = 0;
            _registeredEntityIds.Clear();
            _characterSkillSlots.Clear();
            _recycledEntityIds.Clear();
            Debug.Log("[EntityIdHelper] Reset - All entities cleared");
        }

        #region General Entity Management

        /// <summary>
        /// 새로운 엔티티 ID를 생성하고 등록
        /// 재활용 가능한 ID가 있으면 우선 사용
        /// </summary>
        /// <returns>생성된 엔티티 ID</returns>
        public static int CreateEntity()
        {
            int entityId;

            // 재활용 가능한 ID가 있으면 사용
            if (_recycledEntityIds.Count > 0)
            {
                entityId = _recycledEntityIds.Dequeue();
            }
            else
            {
                // 새로운 ID 할당
                entityId = _nextEntityId++;
            }

            _registeredEntityIds.Add(entityId);
            Debug.Log($"[EntityIdHelper] Entity created - ID: {entityId}");

            return entityId;
        }

        /// <summary>
        /// 엔티티 제거 (일반 엔티티 + 스킬 엔티티 모두 정리)
        /// </summary>
        /// <param name="entityId">제거할 엔티티 ID</param>
        /// <param name="allowRecycle">ID 재활용 허용 여부 (기본값: true)</param>
        public static void DestroyEntity(int entityId, bool allowRecycle = true)
        {
            if (!_registeredEntityIds.Contains(entityId))
            {
                Debug.LogWarning($"[EntityIdHelper] Entity {entityId} is not registered");
                return;
            }

            // 캐릭터 엔티티인 경우, 해당 캐릭터의 모든 스킬 엔티티도 제거
            if (_characterSkillSlots.ContainsKey(entityId))
            {
                HashSet<int> slots = _characterSkillSlots[entityId];
                List<int> skillEntitiesToRemove = new List<int>();

                // 이 캐릭터의 모든 스킬 엔티티 ID 수집
                foreach (int slotIndex in slots)
                {
                    int skillEntityId = SkillEntityIdHelper.GetSkillEntityId(entityId, slotIndex);
                    skillEntitiesToRemove.Add(skillEntityId);
                }

                // 스킬 엔티티들 제거
                for (int i = 0; i < skillEntitiesToRemove.Count; i++)
                {
                    _registeredEntityIds.Remove(skillEntitiesToRemove[i]);
                }

                _characterSkillSlots.Remove(entityId);
                Debug.Log($"[EntityIdHelper] Removed {skillEntitiesToRemove.Count} skill entities for character {entityId}");
            }

            _registeredEntityIds.Remove(entityId);

            // ID 재활용 허용
            if (allowRecycle)
            {
                _recycledEntityIds.Enqueue(entityId);
            }

            Debug.Log($"[EntityIdHelper] Entity destroyed - ID: {entityId} (Recycled: {allowRecycle})");
        }

        /// <summary>
        /// 엔티티가 등록되어 있는지 확인
        /// </summary>
        public static bool IsEntityRegistered(int entityId)
        {
            return _registeredEntityIds.Contains(entityId);
        }

        #endregion

        #region Skill Entity Management

        /// <summary>
        /// 캐릭터의 스킬 엔티티 ID를 생성하고 등록
        /// </summary>
        /// <param name="characterEntityId">스킬을 소유할 캐릭터의 엔티티 ID</param>
        /// <param name="slotIndex">스킬 슬롯 인덱스 (0부터 시작)</param>
        /// <returns>생성된 스킬 엔티티 ID, 실패시 -1</returns>
        public static int CreateSkillEntity(int characterEntityId, int slotIndex)
        {
            // 캐릭터 엔티티가 등록되어 있는지 확인
            if (!_registeredEntityIds.Contains(characterEntityId))
            {
                Debug.LogError($"[EntityIdHelper] Character entity {characterEntityId} is not registered");
                return -1;
            }

            // 슬롯 인덱스 유효성 검증
            if (slotIndex < 0 || slotIndex >= SkillEntityIdHelper.MAX_SKILL_SLOTS)
            {
                Debug.LogError($"[EntityIdHelper] Invalid slot index: {slotIndex}. Must be between 0 and {SkillEntityIdHelper.MAX_SKILL_SLOTS - 1}");
                return -1;
            }

            // 이미 할당된 슬롯인지 확인
            if (!_characterSkillSlots.ContainsKey(characterEntityId))
            {
                _characterSkillSlots[characterEntityId] = new HashSet<int>();
            }

            HashSet<int> usedSlots = _characterSkillSlots[characterEntityId];
            if (usedSlots.Contains(slotIndex))
            {
                Debug.LogWarning($"[EntityIdHelper] Slot {slotIndex} is already assigned to character {characterEntityId}");
                int existingSkillId = SkillEntityIdHelper.GetSkillEntityId(characterEntityId, slotIndex);
                return existingSkillId;
            }

            // 스킬 엔티티 ID 생성
            int skillEntityId = SkillEntityIdHelper.GetSkillEntityId(characterEntityId, slotIndex);
            if (skillEntityId == -1)
            {
                Debug.LogError($"[EntityIdHelper] Failed to create skill entity ID");
                return -1;
            }

            // 등록
            _registeredEntityIds.Add(skillEntityId);
            usedSlots.Add(slotIndex);

            Debug.Log($"[EntityIdHelper] Skill entity created - ID: {skillEntityId}, Character: {characterEntityId}, Slot: {slotIndex}");
            return skillEntityId;
        }

        /// <summary>
        /// 캐릭터의 다음 사용 가능한 스킬 슬롯 인덱스를 찾아서 스킬 엔티티 생성
        /// </summary>
        /// <param name="characterEntityId">스킬을 소유할 캐릭터의 엔티티 ID</param>
        /// <returns>생성된 스킬 엔티티 ID, 실패시 -1</returns>
        public static int CreateSkillEntityAuto(int characterEntityId)
        {
            // 캐릭터 엔티티가 등록되어 있는지 확인
            if (!_registeredEntityIds.Contains(characterEntityId))
            {
                Debug.LogError($"[EntityIdHelper] Character entity {characterEntityId} is not registered");
                return -1;
            }

            // 사용 가능한 슬롯 찾기
            if (!_characterSkillSlots.ContainsKey(characterEntityId))
            {
                _characterSkillSlots[characterEntityId] = new HashSet<int>();
            }

            HashSet<int> usedSlots = _characterSkillSlots[characterEntityId];

            // 사용 가능한 첫 번째 슬롯 찾기
            for (int slotIndex = 0; slotIndex < SkillEntityIdHelper.MAX_SKILL_SLOTS; slotIndex++)
            {
                if (!usedSlots.Contains(slotIndex))
                {
                    return CreateSkillEntity(characterEntityId, slotIndex);
                }
            }

            Debug.LogError($"[EntityIdHelper] No available skill slots for character {characterEntityId}. Max slots: {SkillEntityIdHelper.MAX_SKILL_SLOTS}");
            return -1;
        }

        /// <summary>
        /// 스킬 엔티티를 제거하고 슬롯 해제
        /// </summary>
        /// <param name="skillEntityId">제거할 스킬 엔티티 ID</param>
        public static void DestroySkillEntity(int skillEntityId)
        {
            if (!_registeredEntityIds.Contains(skillEntityId))
            {
                Debug.LogWarning($"[EntityIdHelper] Skill entity {skillEntityId} is not registered");
                return;
            }

            // 스킬 엔티티 ID에서 캐릭터 ID와 슬롯 인덱스 추출
            int characterEntityId = SkillEntityIdHelper.GetCharacterEntityId(skillEntityId);
            int slotIndex = SkillEntityIdHelper.GetSlotIndex(skillEntityId, characterEntityId);

            // 유효성 검증
            if (!SkillEntityIdHelper.IsValidSkillEntityId(skillEntityId, characterEntityId))
            {
                Debug.LogWarning($"[EntityIdHelper] Invalid skill entity ID: {skillEntityId}");
                return;
            }

            // 등록 해제
            _registeredEntityIds.Remove(skillEntityId);

            // 슬롯 해제
            if (_characterSkillSlots.ContainsKey(characterEntityId))
            {
                _characterSkillSlots[characterEntityId].Remove(slotIndex);
            }

            Debug.Log($"[EntityIdHelper] Skill entity destroyed - ID: {skillEntityId}, Character: {characterEntityId}, Slot: {slotIndex}");
        }

        /// <summary>
        /// 스킬 엔티티 ID가 유효한지 확인
        /// </summary>
        public static bool IsValidSkillEntity(int skillEntityId)
        {
            if (!_registeredEntityIds.Contains(skillEntityId))
                return false;

            int characterEntityId = SkillEntityIdHelper.GetCharacterEntityId(skillEntityId);
            return SkillEntityIdHelper.IsValidSkillEntityId(skillEntityId, characterEntityId);
        }

        /// <summary>
        /// 캐릭터가 사용 중인 스킬 슬롯 개수 반환
        /// </summary>
        public static int GetUsedSkillSlotCount(int characterEntityId)
        {
            if (_characterSkillSlots.ContainsKey(characterEntityId))
            {
                return _characterSkillSlots[characterEntityId].Count;
            }
            return 0;
        }

        /// <summary>
        /// 캐릭터의 특정 슬롯이 사용 중인지 확인
        /// </summary>
        public static bool IsSkillSlotUsed(int characterEntityId, int slotIndex)
        {
            if (_characterSkillSlots.ContainsKey(characterEntityId))
            {
                return _characterSkillSlots[characterEntityId].Contains(slotIndex);
            }
            return false;
        }

        #endregion

        #region Debug / Utility

        /// <summary>
        /// 등록된 총 엔티티 수 반환
        /// </summary>
        public static int GetTotalEntityCount()
        {
            return _registeredEntityIds.Count;
        }

        /// <summary>
        /// 재활용 대기 중인 엔티티 ID 개수 반환
        /// </summary>
        public static int GetRecycledEntityCount()
        {
            return _recycledEntityIds.Count;
        }

        /// <summary>
        /// 디버그용: 엔티티 정보 출력
        /// </summary>
        public static void PrintDebugInfo()
        {
            Debug.Log($"[EntityIdHelper] Total entities: {_registeredEntityIds.Count}");
            Debug.Log($"[EntityIdHelper] Next entity ID: {_nextEntityId}");
            Debug.Log($"[EntityIdHelper] Recycled IDs available: {_recycledEntityIds.Count}");
            Debug.Log($"[EntityIdHelper] Characters with skills: {_characterSkillSlots.Count}");

            foreach (var kvp in _characterSkillSlots)
            {
                Debug.Log($"  Character {kvp.Key}: {kvp.Value.Count} skills in slots [{string.Join(", ", kvp.Value)}]");
            }
        }

        #endregion
    }
}
