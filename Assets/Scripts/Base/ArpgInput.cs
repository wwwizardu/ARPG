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

        public ArpgInputAction.PlayerActions Player { get { return _playerInput; } }
        public ArpgInputAction.UIActions UI { get { return _uiInput; } }

        public bool IsUIMode { get { return _isUIMode; } }

        public Vector2 MousePosition
        {
            get
            {
                if(_playerInput.enabled == true)
                {
                    return _playerInput.Move.ReadValue<Vector2>();
                }
                else
                {
                    return _uiInput.Point.ReadValue<Vector2>();
                }
            }
        }

        public void Initialize(ArpgInputAction inputSystem)
        {
            _playerInput = inputSystem.Player;
            _uiInput = inputSystem.UI;

            DirectionInput.Initialize("Up", "Down", "Left", "Right");

            _isUIMode = false;
            _playerInput.Enable();
            _uiInput.Disable();
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

        public void Initialize(string inUp, string inDown, string inLeft, string inRight)
        {
            if(string.IsNullOrEmpty(inUp) == false)
            {
                _upInput = InputSystem.actions.FindAction(inUp);

                _upInput.performed += ctx => HandleInputVertical(Direction.Up, true);
                _upInput.canceled += ctx => HandleInputVertical(Direction.Up, false);
            }

            if (string.IsNullOrEmpty(inDown) == false)
            {
                _downInput = InputSystem.actions.FindAction(inDown);

                _downInput.performed += ctx => HandleInputVertical(Direction.Down, true);
                _downInput.canceled += ctx => HandleInputVertical(Direction.Down, false);
            }

            if (string.IsNullOrEmpty(inLeft) == false)
            {
                _leftInput = InputSystem.actions.FindAction(inLeft);

                _leftInput.performed += ctx => HandleInputHorizontal(Direction.Left, true);
                _leftInput.canceled += ctx => HandleInputHorizontal(Direction.Left, false);
            }

            if (string.IsNullOrEmpty(inRight) == false)
            {
                _rightInput = InputSystem.actions.FindAction(inRight);

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


