using ARPG.Component;
using ARPG.Systems;
using UnityEngine;

namespace ARPG.Systems
{
    // InputSystem은 매 프레임마다 실행 (즉각적인 입력 반응)
    public struct System_Input : IUpdateSystem
    {
        public int Priority => 0; // 가장 먼저 실행 (다른 시스템들이 입력을 사용할 수 있도록)

        private Input.ArpgInputAction.PlayerActions? _input;

        private ComponentManager _componentManager;
        private Entity _playerEntity;

        public void OnCreate()
        {
            Debug.Log("System_Input Created");
        }

        public void Initialize(ComponentManager componentManager, Entity playerEntity)
        {
            _input = AR.s.UI.Input.Player;
            _componentManager = componentManager;
            _playerEntity = playerEntity;
        }

        // Update: 매 프레임마다 입력 수집
        public void OnUpdate(float inDeltaTime)
        {
            if (_input == null || _componentManager == null)
                return;

            if (_input.Value.Inventory.WasPressedThisFrame() == true) // 인벤토리 열기
            {
                var characterUI = AR.s.UI.Show<UI.UICharacter>(AddressablePath.Character, UIManager.Layer.Main);
                if (characterUI == null)
                    return;
            }

            if (AR.s.Component.TryGetComponent<InputComponent>(_playerEntity, out var inputComponent) == false)
                return;

            inputComponent.MoveDirection = _input.Value.Move.ReadValue<Vector2>();
            inputComponent.MousePosition = _input.Value.MouseMove.ReadValue<Vector2>();
            inputComponent.IsAttacking = _input.Value.Attack.IsPressed();
            inputComponent.IsInteracting = _input.Value.Interact.WasPressedThisFrame();
            inputComponent.IsSprinting = _input.Value.Sprint.IsPressed();
            
            // 업데이트된 입력 컴포넌트 저장
            _componentManager.SetComponent(_playerEntity, inputComponent);
        }

        public bool RegisterEntity(IEntity inEntity)
        {
            // InputSystem은 플레이어 엔티티만 처리
            return false;
        }

        public bool UnregisterEntity(IEntity inEntity)
        {
            return false;
        }

        public void Dispose()
        {
            _componentManager = null;
        }
    }
}
