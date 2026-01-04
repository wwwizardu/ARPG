#nullable enable
using ARPG.Component;
using ARPG.Utility;
using UnityEngine;

namespace ARPG.Examples
{
    /// <summary>
    /// 다중 스킬 시스템 사용 예시
    /// 이 클래스는 캐릭터에 여러 스킬을 추가하고 사용하는 방법을 보여줍니다
    /// </summary>
    public class SkillSystemExample : MonoBehaviour
    {
        private int _characterEntityId;

        /// <summary>
        /// 캐릭터 초기화 및 스킬 3개 추가
        /// </summary>
        public void Initialize()
        {
            // ========== 1. 캐릭터 엔티티 생성 ==========
            _characterEntityId = EntityIdHelper.CreateEntity();

            // 기본 컴포넌트들 추가
            AR.s.Component.AddComponent(_characterEntityId, new StateComponent
            {
                Condition = Creature.CharacterConditions.Normal
            });


            // ========== 2. 스킬 엔티티들 생성 ==========

            // 스킬 슬롯 0: 파이어볼 (스킬 ID 1)
            CreateSkill(slotIndex: 0, skillId: 1, startDuration: 0.3f, processDuration: 0.5f, endDuration: 0.2f);

            // 스킬 슬롯 1: 아이스블라스트 (스킬 ID 2)
            CreateSkill(slotIndex: 1, skillId: 2, startDuration: 0.5f, processDuration: 1.0f, endDuration: 0.3f);

            // 스킬 슬롯 2: 라이트닝볼트 (스킬 ID 3)
            CreateSkill(slotIndex: 2, skillId: 3, startDuration: 0.2f, processDuration: 0.3f, endDuration: 0.1f);

            Debug.Log($"[SkillSystemExample] Character initialized with 3 skills (CharacterEntityId: {_characterEntityId})");
        }

        /// <summary>
        /// 스킬 생성 헬퍼 메서드
        /// </summary>
        private void CreateSkill(int slotIndex, int skillId, float startDuration, float processDuration, float endDuration)
        {
            // 스킬 엔티티 ID 생성 (EntityIdHelper 사용)
            int skillEntityId = EntityIdHelper.CreateSkillEntity(_characterEntityId, slotIndex);

            // 디버그 정보 출력
            Debug.Log($"[SkillSystemExample] Creating skill - {SkillEntityIdHelper.GetDebugString(skillEntityId)}");

            // SkillComponent 추가
            AR.s.Component.AddComponent(skillEntityId, new SkillComponent
            {
                SkillId = skillId,
                OwnerEntityId = _characterEntityId,
                SlotIndex = slotIndex,
                Table = null, // TODO: 스킬 테이블에서 로드
                IsInitialized = true,
                IsEnabled = true,
            });

            // SkillStateComponent 추가
            AR.s.Component.AddComponent(skillEntityId, new SkillStateComponent
            {
                State = SkillState.None,
                ElapsedTime = 0f
            });

            // SkillTimingComponent 추가
            AR.s.Component.AddComponent(skillEntityId, new SkillTimingComponent
            {
                StartDuration = startDuration,
                ProcessDuration = processDuration,
                EndDuration = endDuration
            });

            // SkillTargetComponent 추가
            AR.s.Component.AddComponent(skillEntityId, new SkillTargetComponent());
        }


        // ========== 3. 스킬 사용 예시 ==========

        /// <summary>
        /// 슬롯 인덱스로 스킬 사용 (위치 타겟)
        /// </summary>
        public void UseSkillAtPosition(int slotIndex, Vector2 targetPosition)
        {
            // 스킬 엔티티 ID 계산
            int skillEntityId = SkillEntityIdHelper.GetSkillEntityId(_characterEntityId, slotIndex);

            // 스킬이 존재하는지 확인
            if (!AR.s.Component.HasComponent<SkillComponent>(skillEntityId))
            {
                Debug.LogError($"[SkillSystemExample] Skill not found at slot {slotIndex}");
                return;
            }

            // 커맨드 설정
            var command = new SkillCommandComponent();
            //command.SetPositionCommand(skillEntityId, targetPosition);

            AR.s.Component.SetComponent(_characterEntityId, command);

            Debug.Log($"[SkillSystemExample] Skill command issued - Slot: {slotIndex}, Target: {targetPosition}");
        }

        /// <summary>
        /// 슬롯 인덱스로 스킬 사용 (방향 타겟)
        /// </summary>
        public void UseSkillInDirection(int slotIndex, Vector2 direction)
        {
            int skillEntityId = SkillEntityIdHelper.GetSkillEntityId(_characterEntityId, slotIndex);

            if (!AR.s.Component.HasComponent<SkillComponent>(skillEntityId))
            {
                Debug.LogError($"[SkillSystemExample] Skill not found at slot {slotIndex}");
                return;
            }

            var command = new SkillCommandComponent();
            //command.SetDirectionCommand(skillEntityId, direction);

            AR.s.Component.SetComponent(_characterEntityId, command);

            Debug.Log($"[SkillSystemExample] Skill command issued - Slot: {slotIndex}, Direction: {direction}");
        }

        /// <summary>
        /// 슬롯 인덱스로 스킬 중단
        /// </summary>
        public void StopSkill(int slotIndex)
        {
            int skillEntityId = SkillEntityIdHelper.GetSkillEntityId(_characterEntityId, slotIndex);

            var command = new SkillCommandComponent();
            //command.SetStopCommand(skillEntityId);

            AR.s.Component.SetComponent(_characterEntityId, command);

            Debug.Log($"[SkillSystemExample] Skill stop command issued - Slot: {slotIndex}");
        }


        // ========== 4. 스킬 정보 조회 예시 ==========

        /// <summary>
        /// 특정 슬롯의 스킬이 실행 중인지 확인
        /// </summary>
        public bool IsSkillRunning(int slotIndex)
        {
            int skillEntityId = SkillEntityIdHelper.GetSkillEntityId(_characterEntityId, slotIndex);

            if (AR.s.Component.TryGetComponent<SkillStateComponent>(skillEntityId, out var state))
            {
                return state.IsRunning;
            }

            return false;
        }

        /// <summary>
        /// 특정 슬롯의 스킬 정보 가져오기
        /// </summary>
        public SkillComponent? GetSkillInfo(int slotIndex)
        {
            int skillEntityId = SkillEntityIdHelper.GetSkillEntityId(_characterEntityId, slotIndex);

            if (AR.s.Component.TryGetComponent<SkillComponent>(skillEntityId, out var skill))
            {
                return skill;
            }

            return null;
        }

        /// <summary>
        /// 스킬 엔티티 ID 디버그 정보 출력
        /// </summary>
        public void PrintSkillDebugInfo(int slotIndex)
        {
            int skillEntityId = SkillEntityIdHelper.GetSkillEntityId(_characterEntityId, slotIndex);
            Debug.Log(SkillEntityIdHelper.GetDebugString(skillEntityId));
        }


        // ========== Unity 테스트용 메서드 ==========

        void Start()
        {
            // 예시: 초기화
            Initialize();
        }

        void Update()
        {
            // 예시: 키보드 입력으로 스킬 사용
            // if (Input.GetKeyDown(KeyCode.Alpha1))
            // {
            //     // 1번 키: 슬롯 0 스킬 사용 (마우스 위치로)
            //     Vector2 mousePos = Camera.main!.ScreenToWorldPoint(Input.mousePosition);
            //     UseSkillAtPosition(0, mousePos);
            // }

            // if (Input.GetKeyDown(KeyCode.Alpha2))
            // {
            //     // 2번 키: 슬롯 1 스킬 사용 (마우스 방향으로)
            //     Vector2 mousePos = Camera.main!.ScreenToWorldPoint(Input.mousePosition);
            //     Vector2 direction = (mousePos - (Vector2)transform.position).normalized;
            //     UseSkillInDirection(1, direction);
            // }

            // if (Input.GetKeyDown(KeyCode.Alpha3))
            // {
            //     // 3번 키: 슬롯 2 스킬 사용
            //     Vector2 mousePos = Camera.main!.ScreenToWorldPoint(Input.mousePosition);
            //     UseSkillAtPosition(2, mousePos);
            // }

            // if (Input.GetKeyDown(KeyCode.Escape))
            // {
            //     // ESC 키: 모든 스킬 중단
            //     for (int i = 0; i < 3; i++)
            //     {
            //         if (IsSkillRunning(i))
            //         {
            //             StopSkill(i);
            //         }
            //     }
            // }

            // // 디버그: 스킬 상태 표시
            // if (Input.GetKeyDown(KeyCode.D))
            // {
            //     for (int i = 0; i < 3; i++)
            //     {
            //         Debug.Log($"Slot {i}: Running = {IsSkillRunning(i)}");
            //         PrintSkillDebugInfo(i);
            //     }
            // }
        }

        void OnDestroy()
        {
            // 엔티티 ID 제거 (스킬 엔티티도 함께 제거됨)
            if (_characterEntityId != 0)
            {
                EntityIdHelper.DestroyEntity(_characterEntityId);
                Debug.Log($"[SkillSystemExample] Character entity {_characterEntityId} destroyed");
            }
        }
    }
}
