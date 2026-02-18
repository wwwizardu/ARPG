#nullable enable
using System.Collections.Generic;
using UnityEngine;

namespace ARPG.Utility
{
    /// <summary>
    /// 결정적 엔티티 ID 카테고리
    /// 각 카테고리별 오프셋으로 ownerEntityId + (index+1) * offset 패턴으로 ID 생성
    /// </summary>
    public enum EntityIdCategory
    {
        Buff,   // offset: 100,000  / maxIndex: 9,999
        Skill,  // offset: 1,000,000 / maxIndex: 99
    }

    /// <summary>
    /// 엔티티 ID를 중앙에서 관리하는 헬퍼 클래스
    /// - 일반 엔티티: 0부터 순차적으로 증가하는 ID 할당
    /// - 결정적 엔티티: 카테고리별 오프셋 기반 ID (버프, 스킬 등)
    /// - 엔티티 ID 중복 방지 및 유효성 검증
    /// </summary>
    public static class EntityIdHelper
    {
        /// <summary>
        /// 다음에 할당될 일반 엔티티 ID
        /// </summary>
        private static int _nextEntityId = 1;

        /// <summary>
        /// 등록된 모든 엔티티 ID를 추적하는 HashSet
        /// </summary>
        private static readonly HashSet<int> _registeredEntityIds = new HashSet<int>();

        /// <summary>
        /// 캐릭터별 할당된 스킬 슬롯을 추적 (characterEntityId -> 사용중인 슬롯 인덱스들)
        /// </summary>
        private static readonly Dictionary<int, HashSet<int>> _characterSkillSlots = new Dictionary<int, HashSet<int>>();

        /// <summary>
        /// 타겟별 버프 타입 추적 (targetEntityId -> 사용중인 buffTableID 집합)
        /// 같은 타입의 버프는 StackCount로 관리하므로 타입당 하나의 Entity만 존재
        /// </summary>
        private static readonly Dictionary<int, HashSet<int>> _targetBuffTypes = new Dictionary<int, HashSet<int>>();

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
            _nextEntityId = 1;
            _registeredEntityIds.Clear();
            _characterSkillSlots.Clear();
            _targetBuffTypes.Clear();
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
            if(AR.s == null || AR.s.Component == null)
                return;

            if (_registeredEntityIds.Contains(entityId) == false)
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
                    int skillEntityId = GetDeterministicId(entityId, EntityIdCategory.Skill, slotIndex);
                    skillEntitiesToRemove.Add(skillEntityId);
                }

                // 스킬 엔티티들의 ECS 컴포넌트 제거
                for (int i = 0; i < skillEntitiesToRemove.Count; i++)
                {
                    int skillEntityId = skillEntitiesToRemove[i];
                    AR.s.Component.RemoveAllComponents(skillEntityId);
                    _registeredEntityIds.Remove(skillEntityId);
                }

                _characterSkillSlots.Remove(entityId);
                Debug.Log($"[EntityIdHelper] Removed {skillEntitiesToRemove.Count} skill entities for character {entityId}");
            }

            // 메인 엔티티의 모든 ECS 컴포넌트 제거
            AR.s.Component.RemoveAllComponents(entityId);

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

        #region Deterministic ID Calculation

        /// <summary>
        /// 카테고리별 오프셋 정의
        /// </summary>
        private static int GetOffset(EntityIdCategory category)
        {
            switch (category)
            {
                case EntityIdCategory.Buff:  return 100000;
                case EntityIdCategory.Skill: return 1000000;
                default:
                    Debug.LogError($"[EntityIdHelper] Unknown category: {category}");
                    return 0;
            }
        }

        /// <summary>
        /// 카테고리별 최대 인덱스 반환
        /// </summary>
        public static int GetMaxIndex(EntityIdCategory category)
        {
            switch (category)
            {
                case EntityIdCategory.Buff:  return 9999;
                case EntityIdCategory.Skill: return 100;
                default:
                    Debug.LogError($"[EntityIdHelper] Unknown category: {category}");
                    return 0;
            }
        }

        /// <summary>
        /// 결정적 ID 생성: ownerEntityId + (index + 1) * offset
        /// </summary>
        public static int GetDeterministicId(int ownerEntityId, EntityIdCategory category, int index)
        {
            int maxIndex = GetMaxIndex(category);
            if (index < 0 || index > maxIndex)
            {
                Debug.LogError($"[EntityIdHelper] Invalid index: {index} for category {category}. Must be between 0 and {maxIndex}");
                return -1;
            }

            return ownerEntityId + (index + 1) * GetOffset(category);
        }

        /// <summary>
        /// 결정적 ID에서 소유자 엔티티 ID 추출
        /// </summary>
        public static int GetOwnerEntityId(int entityId, EntityIdCategory category)
        {
            return entityId % GetOffset(category);
        }

        /// <summary>
        /// 결정적 ID에서 인덱스 추출
        /// </summary>
        public static int GetIndex(int entityId, EntityIdCategory category)
        {
            return (entityId / GetOffset(category)) - 1;
        }

        /// <summary>
        /// 결정적 ID가 유효한지 확인
        /// </summary>
        public static bool IsValidDeterministicId(int entityId, EntityIdCategory category)
        {
            int index = GetIndex(entityId, category);
            return index >= 0 && index <= GetMaxIndex(category);
        }

        /// <summary>
        /// 디버그용: 결정적 ID 정보를 문자열로 반환
        /// </summary>
        public static string GetDebugString(int entityId, EntityIdCategory category)
        {
            int ownerId = GetOwnerEntityId(entityId, category);
            int index = GetIndex(entityId, category);
            return $"{category}EntityId={entityId} (OwnerId={ownerId}, Index={index})";
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
            if (_registeredEntityIds.Contains(characterEntityId) == false)
            {
                Debug.LogError($"[EntityIdHelper] Character entity {characterEntityId} is not registered");
                return -1;
            }

            // 슬롯 인덱스 유효성 검증
            int maxSkillSlots = GetMaxIndex(EntityIdCategory.Skill);
            if (slotIndex < 0 || slotIndex >= maxSkillSlots)
            {
                Debug.LogError($"[EntityIdHelper] Invalid slot index: {slotIndex}. Must be between 0 and {maxSkillSlots - 1}");
                return -1;
            }

            // 이미 할당된 슬롯인지 확인
            if (_characterSkillSlots.ContainsKey(characterEntityId) == false)
            {
                _characterSkillSlots[characterEntityId] = new HashSet<int>();
            }

            HashSet<int> usedSlots = _characterSkillSlots[characterEntityId];
            if (usedSlots.Contains(slotIndex))
            {
                Debug.LogWarning($"[EntityIdHelper] Slot {slotIndex} is already assigned to character {characterEntityId}");
                int existingSkillId = GetDeterministicId(characterEntityId, EntityIdCategory.Skill, slotIndex);
                return existingSkillId;
            }

            // 스킬 엔티티 ID 생성
            int skillEntityId = GetDeterministicId(characterEntityId, EntityIdCategory.Skill, slotIndex);
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
            if (_registeredEntityIds.Contains(characterEntityId) == false)
            {
                Debug.LogError($"[EntityIdHelper] Character entity {characterEntityId} is not registered");
                return -1;
            }

            // 사용 가능한 슬롯 찾기
            if (_characterSkillSlots.ContainsKey(characterEntityId) == false)
            {
                _characterSkillSlots[characterEntityId] = new HashSet<int>();
            }

            HashSet<int> usedSlots = _characterSkillSlots[characterEntityId];

            // 사용 가능한 첫 번째 슬롯 찾기
            int maxSlots = GetMaxIndex(EntityIdCategory.Skill);
            for (int slotIndex = 0; slotIndex < maxSlots; slotIndex++)
            {
                if (usedSlots.Contains(slotIndex) == false)
                {
                    return CreateSkillEntity(characterEntityId, slotIndex);
                }
            }

            Debug.LogError($"[EntityIdHelper] No available skill slots for character {characterEntityId}. Max slots: {maxSlots}");
            return -1;
        }

        /// <summary>
        /// 스킬 엔티티를 제거하고 슬롯 해제
        /// </summary>
        /// <param name="skillEntityId">제거할 스킬 엔티티 ID</param>
        public static void DestroySkillEntity(int skillEntityId)
        {
            if (_registeredEntityIds.Contains(skillEntityId) == false)
            {
                Debug.LogWarning($"[EntityIdHelper] Skill entity {skillEntityId} is not registered");
                return;
            }

            // 스킬 엔티티 ID에서 캐릭터 ID와 슬롯 인덱스 추출
            int characterEntityId = GetOwnerEntityId(skillEntityId, EntityIdCategory.Skill);
            int slotIndex = GetIndex(skillEntityId, EntityIdCategory.Skill);

            // 유효성 검증
            if (IsValidDeterministicId(skillEntityId, EntityIdCategory.Skill) == false)
            {
                Debug.LogWarning($"[EntityIdHelper] Invalid skill entity ID: {skillEntityId}");
                return;
            }

            // ECS 컴포넌트 일괄 제거
            AR.s.Component.RemoveAllComponents(skillEntityId);

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
            if (_registeredEntityIds.Contains(skillEntityId) == false)
                return false;

            return IsValidDeterministicId(skillEntityId, EntityIdCategory.Skill);
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

        #region Buff Entity Management

        /// <summary>
        /// 버프 엔티티 생성 (결정적 ID 사용)
        /// 같은 타입의 버프가 이미 존재하면 -1을 반환 (BuffSystem에서 StackCount 증가 처리)
        /// </summary>
        /// <param name="targetEntityId">버프를 받을 타겟 엔티티 ID</param>
        /// <param name="buffTableID">버프 테이블 ID</param>
        /// <returns>생성된 버프 엔티티 ID, 이미 존재하거나 실패시 -1</returns>
        public static int CreateBuffEntity(int targetEntityId, int buffTableID)
        {
            // 타겟 엔티티가 등록되어 있는지 확인
            if (_registeredEntityIds.Contains(targetEntityId) == false)
            {
                Debug.LogError($"[EntityIdHelper] Target entity {targetEntityId} is not registered");
                return -1;
            }

            // 버프 테이블 ID 유효성 검증
            int maxBuffTableId = GetMaxIndex(EntityIdCategory.Buff);
            if (buffTableID < 0 || buffTableID > maxBuffTableId)
            {
                Debug.LogError($"[EntityIdHelper] Invalid buff table ID: {buffTableID}. Must be between 0 and {maxBuffTableId}");
                return -1;
            }

            // 버프 엔티티 ID 생성
            int buffEntityId = GetDeterministicId(targetEntityId, EntityIdCategory.Buff, buffTableID);
            if (buffEntityId == -1)
            {
                Debug.LogError($"[EntityIdHelper] Failed to create buff entity ID");
                return -1;
            }

            // 이미 같은 버프가 존재하는지 확인
            if (_registeredEntityIds.Contains(buffEntityId))
            {
                // 이미 존재함 - BuffHelper에서 StackCount 증가 처리
                return -1;
            }

            // 타겟의 버프 타입 추적 초기화
            if (_targetBuffTypes.ContainsKey(targetEntityId) == false)
            {
                _targetBuffTypes[targetEntityId] = new HashSet<int>();
            }

            // 등록
            _registeredEntityIds.Add(buffEntityId);
            _targetBuffTypes[targetEntityId].Add(buffTableID);

            Debug.Log($"[EntityIdHelper] Buff entity created - ID: {buffEntityId}, Target: {targetEntityId}, TableID: {buffTableID}");
            return buffEntityId;
        }

        /// <summary>
        /// 버프 엔티티를 제거하고 타입 추적에서 제거
        /// </summary>
        /// <param name="buffEntityId">제거할 버프 엔티티 ID</param>
        public static void DestroyBuffEntity(int buffEntityId)
        {
            if (_registeredEntityIds.Contains(buffEntityId) == false)
            {
                Debug.LogWarning($"[EntityIdHelper] Buff entity {buffEntityId} is not registered");
                return;
            }

            // 버프 엔티티 ID에서 정보 추출
            int targetEntityId = GetOwnerEntityId(buffEntityId, EntityIdCategory.Buff);
            int buffTableID = GetIndex(buffEntityId, EntityIdCategory.Buff);

            // 유효성 검증
            if (IsValidDeterministicId(buffEntityId, EntityIdCategory.Buff) == false)
            {
                Debug.LogWarning($"[EntityIdHelper] Invalid buff entity ID: {buffEntityId}");
                return;
            }

            // ECS 컴포넌트 일괄 제거
            AR.s.Component.RemoveAllComponents(buffEntityId);

            // 등록 해제
            _registeredEntityIds.Remove(buffEntityId);

            // 버프 타입 추적에서 제거
            if (_targetBuffTypes.ContainsKey(targetEntityId))
            {
                _targetBuffTypes[targetEntityId].Remove(buffTableID);

                // 타겟의 모든 버프가 제거되면 Dictionary에서도 제거
                if (_targetBuffTypes[targetEntityId].Count == 0)
                {
                    _targetBuffTypes.Remove(targetEntityId);
                }
            }

            Debug.Log($"[EntityIdHelper] Buff entity destroyed - ID: {buffEntityId}, Target: {targetEntityId}, TableID: {buffTableID}");
        }

        /// <summary>
        /// 버프 엔티티 ID가 유효한지 확인
        /// </summary>
        public static bool IsValidBuffEntity(int buffEntityId)
        {
            if (_registeredEntityIds.Contains(buffEntityId) == false)
                return false;

            return IsValidDeterministicId(buffEntityId, EntityIdCategory.Buff);
        }

        /// <summary>
        /// 타겟이 특정 버프 타입을 가지고 있는지 확인
        /// </summary>
        public static bool HasBuffType(int targetEntityId, int buffTableID)
        {
            if (_targetBuffTypes.ContainsKey(targetEntityId))
            {
                return _targetBuffTypes[targetEntityId].Contains(buffTableID);
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
