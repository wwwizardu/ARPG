using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;

namespace ARPG.Input
{
    public class ArpgInput
    {
        //private InputActionMap _actionMap_Player;
        //private InputActionMap _actionMap_UI;
        public DirectionInput DirectionInput = new DirectionInput();


        private ArpgInputAction.PlayerActions _playerInput = new();
        private ArpgInputAction.UIActions _uiInput = new();

        private bool _isUIMode = false;
        private InputActionAsset _asset;
        private readonly Dictionary<string, InputAction> _actions = new();
        private System.Action<object, InputActionChange> _actionChanged;
        private float _vibrationEnds;
        private Gamepad _bindingPad;
        private readonly HashSet<InputControl> _modifiedButtons = new();
        public InputActionAsset Asset => _asset;
        public bool IsGamepad { get; private set; }
        public int DisplayRevision { get; private set; }
        public float MovementDeadzone { get; set; } = 0.2f;
        public GameAudio Audio { get; } = new();

        public ArpgInputAction.PlayerActions Player { get { return _playerInput; } }
        public ArpgInputAction.UIActions UI { get { return _uiInput; } }

        public bool IsUIMode { get { return _isUIMode; } }

        public Vector2 MousePosition
        {
            get
            {
                if(_playerInput.enabled == true)
                {
                    return _playerInput.MouseMove.ReadValue<Vector2>();
                }
                else
                {
                    return _uiInput.Point.ReadValue<Vector2>();
                }
            }
        }

        public void Initialize(ArpgInputAction inputSystem)
        {
            _asset = inputSystem.asset;
            GameInputBindings.Configure(_asset);
            _playerInput = inputSystem.Player;
            _uiInput = inputSystem.UI;
            DirectionInput.Initialize(_asset, "Up", "Down", "Left", "Right");
            _actions.Clear();
            foreach (InputAction action in _asset) _actions[action.actionMap.name + "/" + action.name] = action;
            _actionChanged = OnActionChanged;
            InputSystem.onActionChange += _actionChanged;

            _isUIMode = false;
            _playerInput.Enable();
            _uiInput.Disable();
        }

        public InputAction Action(string name) => _actions["Player/" + name];

        public bool Held(string name)
        {
            Gamepad pad = Gamepad.current;
            if (pad != _bindingPad) RebuildModifiedButtons();
            InputAction action = Action(name);
            if (IsGamepad == true && pad != null && pad.leftShoulder.isPressed == true && GameInputBindings.IsModified(name) == false)
            {
                var controls = action.controls;
                for (int i = 0; i < controls.Count; i++)
                    if (controls[i].device == pad && _modifiedButtons.Contains(controls[i]) == true) return false;
            }
            return action.IsPressed();
        }

        public bool Pressed(string name) => Held(name) == true && Action(name).WasPressedThisFrame();
        public bool KeyboardShortcutPressed(string name)
        {
            var controls = Action(name).controls;
            for (int i = 0; i < controls.Count; i++)
                if (controls[i].device is Keyboard && controls[i] is UnityEngine.InputSystem.Controls.ButtonControl button && button.wasPressedThisFrame == true) return true;
            return false;
        }

        public Vector2 ReadMovement(float deadzone)
        {
            if (_isUIMode == true) return Vector2.zero;
            Vector2 keyboard = new((Held("Right") == true ? 1f : 0f) - (Held("Left") == true ? 1f : 0f),
                (Held("Up") == true ? 1f : 0f) - (Held("Down") == true ? 1f : 0f));
            Vector2 pad = Gamepad.current != null ? Gamepad.current.leftStick.ReadUnprocessedValue() : Vector2.zero;
            if (IsGamepad == true && pad.sqrMagnitude > deadzone * deadzone)
                return pad.normalized * Mathf.Clamp01((pad.magnitude - deadzone) / (1f - deadzone));
            return Vector2.ClampMagnitude(keyboard, 1f);
        }

        public string Display(string name, bool? gamepad = null)
        {
            InputAction action = Action(name);
            bool pad = gamepad ?? IsGamepad;
            int index = GameInputBindings.BindingIndex(action, pad);
            if (index < 0) return "—";
            string label = action.GetBindingDisplayString(index);
            if (string.IsNullOrEmpty(label) == true) return "—";
            return pad == true && GameInputBindings.IsModified(name) == true ? "LB + " + label : label;
        }

        public void RestoreActionMaps()
        {
            _playerInput.Disable(); _uiInput.Disable();
            InputAction closeInventory = _asset.FindAction("UI/CloseInventory", true);
            closeInventory.ApplyBindingOverride(0, Action("Inventory").bindings[0].effectivePath);
            if (closeInventory.bindings.Count > 1) closeInventory.ApplyBindingOverride(1, Action("Inventory").bindings[1].effectivePath);
            if (_isUIMode == true) _uiInput.Enable(); else _playerInput.Enable();
            RebuildModifiedButtons();
            DisplayRevision++;
        }

        public void Vibrate(float strength, float seconds)
        {
            if (IsGamepad == false || Gamepad.current == null || AR.s.UI.Settings.Current.Vibration == false) return;
            Gamepad.current.SetMotorSpeeds(strength, strength * 0.5f);
            _vibrationEnds = Time.unscaledTime + seconds;
        }

        public void Tick()
        {
            if (_vibrationEnds > 0f && Time.unscaledTime >= _vibrationEnds) StopVibration();
        }

        public void StopVibration()
        {
            for (int i = 0; i < Gamepad.all.Count; i++) Gamepad.all[i].SetMotorSpeeds(0f, 0f);
            _vibrationEnds = 0f;
        }

        public void Dispose()
        {
            if (_asset != null) _asset.Disable();
            InputSystem.onActionChange -= _actionChanged;
            StopVibration(); Audio.Dispose(); UnityEngine.Object.Destroy(_asset);
        }

        public void ChangeActionMap(bool isUIMode)
        {
            if (_isUIMode == isUIMode)
                return;

            if (isUIMode == true)
            {
                _playerInput.Disable();
                _uiInput.Enable();
            }
            else
            {
                _playerInput.Enable();
                _uiInput.Disable();
            }

            _isUIMode = isUIMode;
        }

        //public bool GetChangeStageInput()
        //{
        //    if (_playerInput.enabled == true)
        //    {
        //        return _playerInput.GoGround.WasPressedThisFrame();
        //    }
        //    else
        //    {
        //        return _uiInput.GoUnderground.WasPressedThisFrame();
        //    }
        //}

        // public bool GetShowDisplayInput()
        // {
        //     if (_playerInput.enabled == true)
        //     {
        //         return _playerInput.ShowPlayInfo.WasPressedThisFrame();
        //     }
        //     else
        //     {
        //         return _uiInput.CloseUI.WasPressedThisFrame();
        //     }
        // }

        // public int GetToolBeltInput()
        // {
        //     if (_playerInput.enabled == true)
        //     {
        //         if (_playerInput.Toolbelt_1.WasReleasedThisFrame() == true)
        //             return 0;
        //         if (_playerInput.Toolbelt_2.WasReleasedThisFrame() == true)
        //             return 1;
        //         if (_playerInput.Toolbelt_3.WasReleasedThisFrame() == true)
        //             return 2;
        //         if (_playerInput.Toolbelt_4.WasReleasedThisFrame() == true)
        //             return 3;
        //         if (_playerInput.Toolbelt_5.WasReleasedThisFrame() == true)
        //             return 4;
        //     }
        //     else
        //     {
        //         if (_uiInput.UI_Toolbelt_1.WasReleasedThisFrame() == true)
        //             return 0;
        //         if (_uiInput.UI_Toolbelt_2.WasReleasedThisFrame() == true)
        //             return 1;
        //         if (_uiInput.UI_Toolbelt_3.WasReleasedThisFrame() == true)
        //             return 2;
        //         if (_uiInput.UI_Toolbelt_4.WasReleasedThisFrame() == true)
        //             return 3;
        //         if (_uiInput.UI_Toolbelt_5.WasReleasedThisFrame() == true)
        //             return 4;
        //     }

        //     return -1;
        // }
        private void RebuildModifiedButtons()
        {
            _bindingPad = Gamepad.current; _modifiedButtons.Clear();
            if (_bindingPad == null) return;
            for (int i = 0; i < GameInputBindings.Definitions.Length; i++)
            {
                var definition = GameInputBindings.Definitions[i];
                if (definition.Modified == false) continue;
                InputAction action = Action(definition.Name);
                int index = GameInputBindings.BindingIndex(action, true);
                if (index < 0) continue;
                string path = action.bindings[index].effectivePath;
                var controls = action.controls;
                for (int j = 0; j < controls.Count; j++)
                    if (controls[j].device == _bindingPad && InputControlPath.Matches(path, controls[j]) == true) _modifiedButtons.Add(controls[j]);
            }
        }

        private void OnActionChanged(object changed, InputActionChange change)
        {
            if (change != InputActionChange.ActionPerformed || changed is InputAction == false) return;
            InputAction action = (InputAction)changed;
            if (action.actionMap.asset != _asset) return;
            InputDevice device = action.activeControl?.device;
            if (action.activeControl is UnityEngine.InputSystem.Controls.StickControl stick
                && stick.ReadUnprocessedValue().sqrMagnitude < MovementDeadzone * MovementDeadzone) return;
            if (device is Gamepad == false && device is Keyboard == false && device is Mouse == false) return;
            bool gamepad = device is Gamepad;
            if (IsGamepad == gamepad) return;
            IsGamepad = gamepad; DisplayRevision++;
        }

    }

    public class DirectionInput
    {
        public enum Direction
        {
            None = 0,
            Up = 2,
            Down = -2,
            Left = -1, 
            Right = 1,
        }

        private List<Direction> _inputOrderHorizontal = new List<Direction>();
        private HashSet<Direction> _activeInputsHorizontal = new HashSet<Direction>();
        private List<Direction> _inputOrderVertical = new List<Direction>();
        private HashSet<Direction> _activeInputsVertical = new HashSet<Direction>();

        private InputAction _upInput = null;
        private InputAction _downInput = null;
        private InputAction _leftInput = null;
        private InputAction _rightInput = null;

        public void Initialize(InputActionAsset asset, string inUp, string inDown, string inLeft, string inRight)
        {
            if(string.IsNullOrEmpty(inUp) == false)
            {
                _upInput = asset.FindAction("Player/" + inUp, true);

                _upInput.performed += ctx => HandleInputVertical(Direction.Up, true);
                _upInput.canceled += ctx => HandleInputVertical(Direction.Up, false);
            }

            if (string.IsNullOrEmpty(inDown) == false)
            {
                _downInput = asset.FindAction("Player/" + inDown, true);

                _downInput.performed += ctx => HandleInputVertical(Direction.Down, true);
                _downInput.canceled += ctx => HandleInputVertical(Direction.Down, false);
            }

            if (string.IsNullOrEmpty(inLeft) == false)
            {
                _leftInput = asset.FindAction("Player/" + inLeft, true);

                _leftInput.performed += ctx => HandleInputHorizontal(Direction.Left, true);
                _leftInput.canceled += ctx => HandleInputHorizontal(Direction.Left, false);
            }

            if (string.IsNullOrEmpty(inRight) == false)
            {
                _rightInput = asset.FindAction("Player/" + inRight, true);

                _rightInput.performed += ctx => HandleInputHorizontal(Direction.Right, true);
                _rightInput.canceled += ctx => HandleInputHorizontal(Direction.Right, false);
            }
        }

        public Direction GetHorizontalInput()
        {
            if(_inputOrderHorizontal.Count <= 0)
                return Direction.None;

            return _inputOrderHorizontal[_inputOrderHorizontal.Count - 1];
        }

        public Direction GetVerticalInput()
        {
            if (_inputOrderVertical.Count <= 0)
                return Direction.None;

            return _inputOrderVertical[_inputOrderVertical.Count - 1];
        }

        private void HandleInputHorizontal(Direction inDirection, bool isPressed)
        {
            if (isPressed)
            {
                if (_activeInputsHorizontal.Contains(inDirection) == false)
                {
                    _activeInputsHorizontal.Add(inDirection);
                    _inputOrderHorizontal.Add(inDirection);
                }
            }
            else
            {
                _activeInputsHorizontal.Remove(inDirection);
                _inputOrderHorizontal.Remove(inDirection);
            }
        }

        private void HandleInputVertical(Direction inDirection, bool isPressed)
        {
            if (isPressed)
            {
                if (_activeInputsVertical.Contains(inDirection) == false)
                {
                    _activeInputsVertical.Add(inDirection);
                    _inputOrderVertical.Add(inDirection);
                }
            }
            else
            {
                _activeInputsVertical.Remove(inDirection);
                _inputOrderVertical.Remove(inDirection);
            }
        }
    }
}


