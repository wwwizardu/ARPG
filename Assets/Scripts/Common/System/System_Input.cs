#nullable enable
using ARPG.Component;
using ARPG.Systems;
using ARPG.Utility;
using ARPG.Village;
using UnityEngine;
using UnityEngine.InputSystem;

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

            // [TEST] B키 — Inn UI 열기 (실제 InnBed 없이 테스트용)
            if (Keyboard.current != null && Keyboard.current.bKey.wasPressedThisFrame)
            {
                var innUI = AR.s.UI.Show<UI.UIInn>(AddressablePath.Inn, UIManager.Layer.Main);
                if (innUI != null)
                    innUI.BindForTest();
            }

            // [TEST] N키 — Shrine UI 열기 (실제 Shrine 없이 테스트용)
            if (Keyboard.current != null && Keyboard.current.nKey.wasPressedThisFrame)
            {
                var shrineUI = AR.s.UI.Show<UI.UIShrine>(AddressablePath.Shrine, UIManager.Layer.Main);
                if (shrineUI != null)
                    shrineUI.BindForTest();
            }

            // [TEST] G키 — Forge UI 열기 (실제 Forge 없이 테스트용, Premium 단계로 가정)
            if (Keyboard.current != null && Keyboard.current.gKey.wasPressedThisFrame)
            {
                var forgeUI = AR.s.UI.Show<UI.UIForge>(AddressablePath.Forge, UIManager.Layer.Main);
                if (forgeUI != null)
                    forgeUI.BindForTest(UI.UIForge.ForgeTier.Premium);
            }

            // [TEST] M키 — Shop UI 열기 (실제 MerchantStall 없이 테스트용)
            if (Keyboard.current != null && Keyboard.current.mKey.wasPressedThisFrame)
            {
                var shopUI = AR.s.UI.Show<UI.UIShopMerchant>(AddressablePath.ShopMerchant, UIManager.Layer.Main);
                if (shopUI != null)
                    shopUI.BindForTest();
            }

            // K키 — SkillBook UI 열기 (SKILLBOOK_DESIGN.md §5.4)
            if (Keyboard.current != null && Keyboard.current.kKey.wasPressedThisFrame)
            {
                AR.s.UI.Show<UI.UISkillBook>(AddressablePath.SkillBook, UIManager.Layer.Main);
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

            // 슬롯별 입력 유지 비트마스크 (채널링/차징 스킬용)
            // bit 0: Attack(좌클릭) → 슬롯 0
            // bit 1: Jump(Space) → 슬롯 1
            // bit 2~9: Digit1~Digit8 → 슬롯 2~9
            int heldMask = 0;
            if (_input.Value.Attack.IsPressed()) heldMask |= 1 << 0;
            if (_input.Value.Jump.IsPressed()) heldMask |= 1 << 1;
            if (Keyboard.current != null)
            {
                Key[] heldDigitKeys = {
                    Key.Digit1, Key.Digit2, Key.Digit3, Key.Digit4,
                    Key.Digit5, Key.Digit6, Key.Digit7, Key.Digit8,
                };
                for (int i = 0; i < heldDigitKeys.Length; i++)
                {
                    if (Keyboard.current[heldDigitKeys[i]].isPressed)
                        heldMask |= 1 << (i + 2);
                }
            }
            inputComponent.SkillSlotHeldMask = heldMask;

            // 업데이트된 입력 컴포넌트 저장
            _componentManager.SetComponent(_playerEntityId, inputComponent);

            // Phase D: F키(Interact) 입력 시 마을 서비스 UI 열기 (가까운 서비스 우선순위 라우팅)
            if (inputComponent.IsInteracting
                && AR.s.Component.TryGetComponent<PlayerNearbyServicesComponent>(_playerEntityId, out var nearby)
                && nearby.AvailableServices != ProvidedService.None)
            {
                ServiceUIRouter.Open(nearby);
            }

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

            // 숫자키 1~8 → 슬롯 2~9 발동 (SKILLBOOK_DESIGN.md §2.3)
            // 추후 키바인딩 UI 도입 시 ArpgInput 액션으로 이전 가능. 일단 raw 키로 처리.
            if (Keyboard.current != null)
            {
                Key[] digitKeys = {
                    Key.Digit1, Key.Digit2, Key.Digit3, Key.Digit4,
                    Key.Digit5, Key.Digit6, Key.Digit7, Key.Digit8,
                };
                for (int i = 0; i < digitKeys.Length; i++)
                {
                    if (Keyboard.current[digitKeys[i]].wasPressedThisFrame)
                    {
                        UseSkill(ref inputComponent, i + 2); // 슬롯 2~9
                    }
                }
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
