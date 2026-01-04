#nullable enable
using UnityEngine;
using ARPG.Utility;
using ARPG.Component;

namespace ARPG.Example
{
    /// <summary>
    /// EntityIdHelper 사용 예제
    /// 엔티티 ID 생성, 스킬 엔티티 생성, 삭제 등의 사용법을 보여줍니다
    /// </summary>
    public class EntityIdHelperExample : MonoBehaviour
    {
        private void Start()
        {
            // EntityIdHelper 초기화 (게임 시작 시 한 번만)
            EntityIdHelper.Initialize();

            // 예제 실행
            Example_BasicEntityCreation();
            Example_SkillEntityCreation();
            Example_EntityRecycling();
            Example_DebugInfo();

            // 실제 사용 예제 (주석 해제하여 실행 가능)
            // Example_RealWorldUsage();
        }

        /// <summary>
        /// 기본 엔티티 생성 예제
        /// </summary>
        private void Example_BasicEntityCreation()
        {
            Debug.Log("\n=== Example 1: Basic Entity Creation ===");

            // 캐릭터 엔티티 생성 (이전: gameObject.GetInstanceID())
            int characterEntityId = EntityIdHelper.CreateEntity();
            Debug.Log($"Created character entity: {characterEntityId}");

            // 몬스터 엔티티 생성
            int monsterEntityId1 = EntityIdHelper.CreateEntity();
            int monsterEntityId2 = EntityIdHelper.CreateEntity();
            Debug.Log($"Created monster entities: {monsterEntityId1}, {monsterEntityId2}");

            // 엔티티 등록 확인
            bool isRegistered = EntityIdHelper.IsEntityRegistered(characterEntityId);
            Debug.Log($"Character entity registered: {isRegistered}");
        }

        /// <summary>
        /// 스킬 엔티티 생성 예제
        /// </summary>
        private void Example_SkillEntityCreation()
        {
            Debug.Log("\n=== Example 2: Skill Entity Creation ===");

            // 캐릭터 엔티티 생성
            int characterEntityId = EntityIdHelper.CreateEntity();
            Debug.Log($"Character entity ID: {characterEntityId}");

            // 스킬 엔티티 생성 (슬롯 인덱스 직접 지정)
            int skillEntityId1 = EntityIdHelper.CreateSkillEntity(characterEntityId, 0); // 슬롯 0
            int skillEntityId2 = EntityIdHelper.CreateSkillEntity(characterEntityId, 1); // 슬롯 1
            Debug.Log($"Skill 1 entity ID: {skillEntityId1}");
            Debug.Log($"Skill 2 entity ID: {skillEntityId2}");

            // 스킬 엔티티 자동 생성 (다음 사용 가능한 슬롯에 자동 할당)
            int skillEntityId3 = EntityIdHelper.CreateSkillEntityAuto(characterEntityId); // 슬롯 2에 자동 할당
            Debug.Log($"Skill 3 entity ID (auto): {skillEntityId3}");

            // 스킬 엔티티 ID에서 정보 추출
            int extractedCharId = SkillEntityIdHelper.GetCharacterEntityId(skillEntityId1);
            int slotIndex = SkillEntityIdHelper.GetSlotIndex(skillEntityId1, extractedCharId);
            Debug.Log($"Extracted from skill entity {skillEntityId1}: Character={extractedCharId}, Slot={slotIndex}");

            // 스킬 슬롯 사용 현황 확인
            int usedSlotCount = EntityIdHelper.GetUsedSkillSlotCount(characterEntityId);
            Debug.Log($"Character {characterEntityId} is using {usedSlotCount} skill slots");

            bool isSlot0Used = EntityIdHelper.IsSkillSlotUsed(characterEntityId, 0);
            bool isSlot5Used = EntityIdHelper.IsSkillSlotUsed(characterEntityId, 5);
            Debug.Log($"Slot 0 used: {isSlot0Used}, Slot 5 used: {isSlot5Used}");
        }

        /// <summary>
        /// 엔티티 재활용 예제
        /// </summary>
        private void Example_EntityRecycling()
        {
            Debug.Log("\n=== Example 3: Entity Recycling ===");

            // 엔티티 생성
            int entity1 = EntityIdHelper.CreateEntity(); // ID: 0
            int entity2 = EntityIdHelper.CreateEntity(); // ID: 1
            int entity3 = EntityIdHelper.CreateEntity(); // ID: 2
            Debug.Log($"Created entities: {entity1}, {entity2}, {entity3}");

            // entity1 삭제 (재활용 허용)
            EntityIdHelper.DestroyEntity(entity1, allowRecycle: true);
            Debug.Log($"Destroyed entity {entity1} with recycling");

            // 새 엔티티 생성 - 재활용된 ID 사용
            int entity4 = EntityIdHelper.CreateEntity(); // entity1의 ID를 재활용
            Debug.Log($"Created new entity: {entity4} (should reuse {entity1})");

            // 재활용 가능한 ID 개수 확인
            int recycledCount = EntityIdHelper.GetRecycledEntityCount();
            Debug.Log($"Recycled IDs available: {recycledCount}");
        }

        /// <summary>
        /// 디버그 정보 출력 예제
        /// </summary>
        private void Example_DebugInfo()
        {
            Debug.Log("\n=== Example 4: Debug Information ===");

            // 캐릭터 2명 생성
            int char1 = EntityIdHelper.CreateEntity();
            int char2 = EntityIdHelper.CreateEntity();

            // 캐릭터1에 스킬 3개
            EntityIdHelper.CreateSkillEntity(char1, 0);
            EntityIdHelper.CreateSkillEntity(char1, 1);
            EntityIdHelper.CreateSkillEntity(char1, 2);

            // 캐릭터2에 스킬 2개
            EntityIdHelper.CreateSkillEntityAuto(char2);
            EntityIdHelper.CreateSkillEntityAuto(char2);

            // 전체 디버그 정보 출력
            EntityIdHelper.PrintDebugInfo();

            // 총 엔티티 개수
            int totalCount = EntityIdHelper.GetTotalEntityCount();
            Debug.Log($"Total entities in system: {totalCount}");
        }

        /// <summary>
        /// 실제 게임에서 사용하는 패턴 예제
        /// </summary>
        private void Example_RealWorldUsage()
        {
            Debug.Log("\n=== Example 5: Real World Usage ===");

            // 1. 플레이어 캐릭터 생성
            int playerEntityId = EntityIdHelper.CreateEntity();
            Debug.Log($"Player entity created: {playerEntityId}");

            // 2. 기본 컴포넌트 추가
            AR.s.Component.AddComponent(playerEntityId, new StateComponent
            {
                Condition = Creature.CharacterConditions.Normal
            });

            // 3. 플레이어의 스킬 슬롯에 스킬 추가
            int skill1EntityId = EntityIdHelper.CreateSkillEntityAuto(playerEntityId);
            int skill2EntityId = EntityIdHelper.CreateSkillEntityAuto(playerEntityId);

            // 4. 스킬 컴포넌트 설정
            AR.s.Component.AddComponent(skill1EntityId, new SkillComponent
            {
                SkillId = 1001,
                OwnerEntityId = playerEntityId,
                SlotIndex = 0,
                Table = null,
                IsInitialized = true,
                IsEnabled = true,
            });

            Debug.Log($"Player {playerEntityId} equipped with skills: {skill1EntityId}, {skill2EntityId}");

            // 5. 플레이어 삭제 시 - 스킬도 함께 정리됨
            EntityIdHelper.DestroyEntity(playerEntityId);
            Debug.Log($"Player {playerEntityId} destroyed (all skills automatically removed)");

            // 6. 스킬 엔티티가 삭제되었는지 확인
            bool isSkillValid = EntityIdHelper.IsValidSkillEntity(skill1EntityId);
            Debug.Log($"Skill {skill1EntityId} is still valid: {isSkillValid}"); // false
        }

        private void OnDestroy()
        {
            // 씬 전환 시 또는 게임 종료 시 초기화
            EntityIdHelper.Reset();
        }
    }
}
