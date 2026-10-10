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

        private ComponentManager _componentManager = null!;
        private Camera? _mainCamera;
        private int _playerEntityId;

        private static readonly Key[] _skillDigitKeys =
        {
            Key.Digit1, Key.Digit2, Key.Digit3, Key.Digit4,
            Key.Digit5, Key.Digit6, Key.Digit7, Key.Digit8,
        };

        public void OnCreate()
        {
            _input = AR.s.UI.Input.Player;
            
            _componentManager = AR.s.Component;
            _mainCamera = Camera.main;
            _playerEntityId = -1;

            Debug.Log("System_Input Created");
        }

        public void OnReset()
        {
            _mainCamera = null;
            _playerEntityId = -1;
            Debug.Log("System_Input Reset");
        }

        // Update: 매 프레임마다 입력 수집
        public void OnUpdate(float inDeltaTime)
        {
            if (_input == null || _componentManager == null)
                return;

            if(AR.s.UI.UpdateInput() == false) // UI가 입력을 처리한 경우 Player Input을 처리하지 않음
                return;

            if (AR.s.Dungeon.GameplayPaused) return;

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

            // [TEST] B키 — Inn UI 열기 (실제 InnBed 없이 테스트용, 휴식/세이브 전용 — 고용 경로는 D6로 제거)
            if (AR.s.Dungeon.IsInside == false && Keyboard.current != null && Keyboard.current.bKey.wasPressedThisFrame)
            {
                var innUI = AR.s.UI.Show<UI.UIInn>(AddressablePath.Inn, UIManager.Layer.Main);
                if (innUI != null)
                    innUI.BindForTest();
            }

            // [TEST] N키 — Shrine UI 열기 (실제 Shrine 없이 테스트용)
            if (AR.s.Dungeon.IsInside == false && Keyboard.current != null && Keyboard.current.nKey.wasPressedThisFrame)
            {
                var shrineUI = AR.s.UI.Show<UI.UIShrine>(AddressablePath.Shrine, UIManager.Layer.Main);
                if (shrineUI != null)
                    shrineUI.BindForTest();
            }

            // [TEST] G키 — Forge UI 열기 (Editor 진단 전용, Premium 단계로 가정)
#if UNITY_EDITOR
            if (AR.s.Dungeon.IsInside == false && Keyboard.current != null && Keyboard.current.gKey.wasPressedThisFrame)
            {
                var forgeUI = AR.s.UI.Show<UI.UIForge>(AddressablePath.Forge, UIManager.Layer.Main);
                if (forgeUI != null)
                    forgeUI.BindForTest(GlobalEnum.ForgeTier.Premium);
            }
#endif

            // [TEST] M키 — Shop UI 열기 (실제 MerchantStall 없이 테스트용)
            if (AR.s.Dungeon.IsInside == false && Keyboard.current != null && Keyboard.current.mKey.wasPressedThisFrame)
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
            if (_mainCamera == null)
            {
                _mainCamera = Camera.main;
            }
            if (_mainCamera != null)
            {
                inputComponent.MouseWorldPosition = _mainCamera.ScreenToWorldPoint(inputComponent.MousePosition);
            }
            bool interactionPressed = _input.Value.Interact.WasPressedThisFrame();
            bool interactionBlocked = AR.s.Npc.IsPlayerInDuel == true;
            inputComponent.IsInteracting = interactionPressed && interactionBlocked == false;
            if (interactionPressed == true && interactionBlocked == true)
                AR.s.UI.SetNotify("결투 중에는 상호작용할 수 없습니다.");
            inputComponent.IsSprinting = _input.Value.Sprint.IsPressed();

            // 좌클릭 = 아이템 줍기 + 슬롯 0 공격 공용.
            // 누른 순간 UI나 드랍 아이템을 클릭했다면(줍기 성공 여부 무관) 버튼을 뗄 때까지 공격을 막는다.
            // 새로 누를 때마다 다시 판정하므로, UI가 입력을 가져간 사이 버튼을 떼도 플래그가 남지 않는다.
            bool attackPressed = _input.Value.Attack.IsPressed();
            if (_input.Value.Attack.WasPressedThisFrame() == true)
            {
                inputComponent.AttackSuppressedUntilRelease = AR.s.UI.IsPointerOverUIElement(inputComponent.MousePosition) == true
                    || TryClickDroppedItem(inputComponent.MouseWorldPosition);
            }
            else if (attackPressed == false)
            {
                inputComponent.AttackSuppressedUntilRelease = false;
            }
            inputComponent.IsAttacking = attackPressed && inputComponent.AttackSuppressedUntilRelease == false;

            // 슬롯별 입력 유지 비트마스크 (채널링/차징 스킬용)
            // bit 0: Attack(좌클릭) → 슬롯 0 (아이템 클릭으로 차단 중이면 제외)
            // bit 1: Jump(Space) → 슬롯 1
            // bit 2~9: Digit1~Digit8 → 슬롯 2~9
            int heldMask = 0;
            if (inputComponent.IsAttacking) heldMask |= 1 << 0;
            if (_input.Value.Jump.IsPressed()) heldMask |= 1 << 1;
            if (Keyboard.current != null)
            {
                for (int i = 0; i < _skillDigitKeys.Length; i++)
                {
                    if (Keyboard.current[_skillDigitKeys[i]].isPressed)
                        heldMask |= 1 << (i + 2);
                }
            }
            inputComponent.SkillSlotHeldMask = heldMask;

            // 업데이트된 입력 컴포넌트 저장
            _componentManager.SetComponent(_playerEntityId, inputComponent);

            // Interact 키 입력 시 가까운 후보(시설 + NPC) 중 거리 가장 가까운 것 UI 라우팅
            if (inputComponent.IsInteracting
                && AR.s.Component.TryGetComponent<PlayerNearbyServicesComponent>(_playerEntityId, out var nearby))
            {
                ServiceUIRouter.Open(nearby);
                if (AR.s.Dungeon.IsBusy) return;
            }

            // 공격 입력 처리 (슬롯 0 스킬 발동)
            if (inputComponent.IsAttacking == true)
            {
                UseSkill(ref inputComponent, 0);
            }

            // 점프 입력 처리 (슬롯 1 스킬 발동)
            if (_input.Value.Jump.WasPressedThisFrame() == true)
            {
                UseSkill(ref inputComponent, 1);
            }

            // 숫자키 1~8 유지 → 슬롯 2~9 연속 발동 (SKILLBOOK_DESIGN.md §2.3)
            // 추후 키바인딩 UI 도입 시 ArpgInput 액션으로 이전 가능. 일단 raw 키로 처리.
            if (Keyboard.current != null)
            {
                for (int i = 0; i < _skillDigitKeys.Length; i++)
                {
                    if (Keyboard.current[_skillDigitKeys[i]].isPressed)
                    {
                        UseSkill(ref inputComponent, i + 2); // 슬롯 2~9
                    }
                }
            }
        }

        // 마우스 아래 드랍 아이템이 있으면 줍기를 시도하고 true 반환 (인벤토리가 가득 차 줍기에 실패해도 true)
        private bool TryClickDroppedItem(Vector2 inMouseWorldPosition)
        {
            // 2D 레이캐스트 (2D 게임의 경우)
            RaycastHit2D hit = Physics2D.Raycast(inMouseWorldPosition, Vector2.zero);
            if (hit.collider == null)
                return false;

            if (hit.collider.gameObject.CompareTag("DropedItem") == false)
                return false;

            var item = hit.collider.gameObject.GetComponentInParent<Item.ItemObject>();
            if (item == null)
                return false;

            AR.s.Item.PickupItem(item);
            return true;
        }

        private void UseSkill(ref InputComponent inInputComponent, int slotIndex)
        {
            // 스킬북이 비어 있는 슬롯은 오류가 아니라 정상 상태다.
            // 슬롯 0(좌클릭)과 2~9(숫자키)는 유지 입력이라 매 프레임 이 경로를 지나므로,
            // 여기서 걸러내지 않으면 SkillHelper의 "Skill not found" 경고가 초당 60회 찍힌다.
            // 조건은 EntityFactory가 스킬 엔티티를 만드는 기준(SkillId > 0)과 같다.
            if (AR.s.PlayerSkill.GetEquippedSkillId(slotIndex) == 0)
                return;

            if (SkillHelper.GetSkillCommandComponent(slotIndex, _playerEntityId, inInputComponent.MouseWorldPosition, out var command) == false)
                return;

            // 캐릭터 엔티티에 커맨드 설정 (이미 있으면 최신 값으로 덮어쓰기)
            _componentManager.SetComponent(_playerEntityId, command);
        }
    }
}
