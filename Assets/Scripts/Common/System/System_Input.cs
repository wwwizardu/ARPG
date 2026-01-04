#nullable enable
using ARPG.Component;
using ARPG.Systems;
using ARPG.Utility;
using UnityEngine;

namespace ARPG.Systems
{
    // InputSystem은 매 프레임마다 실행 (즉각적인 입력 반응)
    public struct System_Input : IUpdateSystem
    {
        public int Priority => 0; // 가장 먼저 실행 (다른 시스템들이 입력을 사용할 수 있도록)

        private Input.ArpgInputAction.PlayerActions? _input;

        private ComponentManager _componentManager;
        private int _playerEntityId;

        public void OnCreate()
        {
            _input = AR.s.UI.Input.Player;
            _componentManager = AR.s.Component;
            _playerEntityId = -1;

            Debug.Log("System_Input Created");
        }

        public void OnReset()
        {
            Debug.Log("System_Input Reset");
        }

        // Update: 매 프레임마다 입력 수집
        public void OnUpdate(float inDeltaTime)
        {
            if (_input == null || _componentManager == null)
                return;

            if (_playerEntityId == -1)
            {
                var inputComponentPool = AR.s.Component.GetComponentPool<InputComponent>();
                if (inputComponentPool.Count > 0)
                {
                    _playerEntityId = inputComponentPool.GetEntityId(0);
                }
                else
                {
                    return; // 플레이어 엔티티가 아직 없음
                }
            }

            if (_input.Value.Inventory.WasPressedThisFrame() == true) // 인벤토리 열기
            {
                var characterUI = AR.s.UI.Show<UI.UICharacter>(AddressablePath.Character, UIManager.Layer.Main);
                if (characterUI == null)
                    return;
            }

            if (AR.s.Component.TryGetComponent<InputComponent>(_playerEntityId, out var inputComponent) == false)
                return;

            inputComponent.MoveDirection = _input.Value.Move.ReadValue<Vector2>();
            inputComponent.MousePosition = _input.Value.MouseMove.ReadValue<Vector2>();
            inputComponent.IsAttacking = _input.Value.Attack.IsPressed();
            inputComponent.IsInteracting = _input.Value.Interact.WasPressedThisFrame();
            inputComponent.IsSprinting = _input.Value.Sprint.IsPressed();

            // 업데이트된 입력 컴포넌트 저장
            _componentManager.SetComponent(_playerEntityId, inputComponent);

            // 공격 입력 처리
            if(inputComponent.IsAttacking == true)
            {
                // 슬롯 인덱스로 스킬 엔티티 ID 즉시 계산
                int slotIndex = 0; // 기본 공격 슬롯
                int skillEntityId = SkillEntityIdHelper.GetSkillEntityId(_playerEntityId, slotIndex);

                // 스킬이 실제로 존재하는지 확인
                if (AR.s.Component.TryGetComponent<SkillComponent>(skillEntityId, out var skill) == true)
                {
                    // 마우스 위치로 스킬 커맨드 생성
                    if(AR.s.Component.TryGetComponent<SkillCommandComponent>(_playerEntityId, out var command) == false)
                    {
                        command = new SkillCommandComponent();
                        if(skill.Table != null)
                        {
                            command.TargetType = skill.Table.SkillTargetType;    
                        }
                        else
                        {
                            Debug.LogError($"[System_Input] Skill.Table is null for SkillId({skill.SkillId}), SkillEntityId({skillEntityId})");
                        }
                    }

                    command.SkillEntityId = skillEntityId;
                    command.TargetPosition = inputComponent.MousePosition;

                    // 캐릭터 엔티티에 커맨드 설정 (이미 있으면 덮어쓰기)
                    _componentManager.SetComponent(_playerEntityId, command);
                }
                else
                {
                    Debug.LogWarning($"[System_Input] Skill not found at slot {slotIndex}");
                }
            }
        }
    }
}
