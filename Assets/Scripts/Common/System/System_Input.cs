#nullable enable
using ARPG.Component;
using ARPG.Systems;
using ARPG.Utility;
using UnityEngine;

namespace ARPG.Systems
{
    // InputSystem은 매 프레임마다 실행 (즉각적인 입력 반응)
    public class System_Input : IUpdateSystem
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

            if(AR.s.UI.UpdateInput() == false) // UI가 입력을 처리한 경우 Player Input을 처리하지 않음
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

            // if (_input.Value.UseItem.WasPressedThisFrame() == true) // 아이템 사용 시 그냥 리턴
            // {
            //     CharacterComponent.Toolbelt.UseHoldingItem();
            //     return;
            // }

            if (AR.s.Component.TryGetComponent<InputComponent>(_playerEntityId, out var inputComponent) == false)
                return;

            inputComponent.MoveDirection = _input.Value.Move.ReadValue<Vector2>();
            inputComponent.MousePosition = _input.Value.MouseMove.ReadValue<Vector2>();
            inputComponent.IsInteracting = _input.Value.Interact.WasPressedThisFrame();
            inputComponent.IsSprinting = _input.Value.Sprint.IsPressed();

            // 업데이트된 입력 컴포넌트 저장
            _componentManager.SetComponent(_playerEntityId, inputComponent);

            if (_input.Value.Attack.IsPressed() == true)
            {
                if (_input.Value.Attack.WasPressedThisFrame() == true && CheckPickupItem(inputComponent.MousePosition) == true)
                {
                    inputComponent.IsAttacking = false;
                }
                else
                {
                    inputComponent.IsAttacking = _input.Value.Attack.IsPressed();

                    // 공격 입력 처리
                    if(inputComponent.IsAttacking == true)
                    {
                        UseSkill(ref inputComponent, 0);
                    }
                }
            }

            // 점프 입력 처리 (슬롯 1 스킬 발동)
            if (_input.Value.Jump.WasPressedThisFrame() == true)
            {
                UseSkill(ref inputComponent, 1);
            }
        }

        private bool CheckPickupItem(Vector3 inMousePos)
        {
            Vector3 mouseWorldPos = Camera.main.ScreenToWorldPoint(new Vector3(inMousePos.x, inMousePos.y, 0f));

            // 2D 레이캐스트 (2D 게임의 경우)
            RaycastHit2D hit = Physics2D.Raycast(mouseWorldPos, Vector2.zero);
            if (hit.collider != null)
            {
                // 아이템 컴포넌트 확인
                if (hit.collider.gameObject.CompareTag("DropedItem"))
                {
                    var item = hit.collider.gameObject.GetComponentInParent<Item.ItemObject>();
                    if (item != null)
                    {
                        return AR.s.Item.PickupItem(item);
                    }
                }
            }

            return false;
        }

        private void UseSkill(ref InputComponent inInputComponent, int slotIndex)
        {
            Vector2 targetPosition = Camera.main.ScreenToWorldPoint(inInputComponent.MousePosition);

            if (SkillHelper.GetSkillCommandComponent(slotIndex, _playerEntityId, targetPosition, out var command) == false)
                return;

            // 캐릭터 엔티티에 커맨드 설정 (이미 있으면 최신 값으로 덮어쓰기)
            _componentManager.SetComponent(_playerEntityId, command);
        }
    }
}
