#nullable enable
using UnityEngine;

namespace ARPG.Creature
{
    public class ArpgPlayer : CharacterBase
    {
        private Vector2 _inputDirection = Vector2.zero;
        private Input.ArpgInputAction.PlayerActions? _input = null;

        public override void Initialize()
        {
            base.Initialize();

            _input = AR.s.UI.Input.Player;

            transform.position = new Vector3(0, 0, -0.1f); // Set initial position

            Debug.Log("ArpgPlayer initialized.");
        }

        public override void Reset()
        {
            base.Reset();
            // Reset player-specific state if needed
            Debug.Log("ArpgPlayer reset.");
        }

        // protected override void InitializeSkill()
        // {
        //     _skillController.Initialize(this, _gamePlayer?.SkillData?.SkillDatas);
        // }


        protected override void UpdateInput()
        {
            if (_input == null)
                return;

            // if (_input.Value.UseItem.WasPressedThisFrame() == true) // 아이템 사용 시 그냥 리턴
            // {
            //     CharacterComponent.Toolbelt.UseHoldingItem();
            //     return;
            // }

            if (_condition == CharacterConditions.Normal || _condition == CharacterConditions.UseSkill)
            {
                Vector2 mousePosition = _input.Value.MouseMove.ReadValue<Vector2>();
                Vector3 mouseWorldPos = Camera.main.ScreenToWorldPoint(new Vector3(mousePosition.x, mousePosition.y, 0f));
                //SetCharacterFaceDirection(mouseWorldPos.x < transform.position.x);

                _inputDirection = _input.Value.Move.ReadValue<Vector2>();

                if (0.01f < _inputDirection.x || _inputDirection.x < -0.01)
                {
                    Debug.Log($"Input Direction: {_inputDirection}");
                }
                // {
                //     // Update character movement based on input direction
                //     UpdateHorizontalForce(_inputDirection.x > 0 ? Input.DirectionInput.Direction.Right : Input.DirectionInput.Direction.Left);
                //     UpdateVerticalForce(_inputDirection.y > 0 ? Input.DirectionInput.Direction.Up : Input.DirectionInput.Direction.Down);
                // }
                // else
                // {
                //     // Stop character movement if no input
                //     Stop();
                // }

                // Input.DirectionInput.Direction dirHorizontal = AR.s.UI.Input.DirectionInput.GetHorizontalInput();
                // Input.DirectionInput.Direction dirVertical = AR.s.UI.Input.DirectionInput.GetVerticalInput();

                // UpdateHorizontalForce(dirHorizontal);
                // UpdateVerticalForce(dirVertical);

                // if (_input.Value.Jump.WasPressedThisFrame() == true)
                // {
                //     if (IsOwner == true)
                //     {
                //         if (_controller.State.IsGrounded == true || _isLadderClimbing == true) // 땅이거나 사다리를 타는 중에만 점프 가능
                //         {
                //             // 아래 방향키를 누르고 있었을때는 점프 안뛰고 그냥 떨어지도록
                //             if (dirVertical != InputController.DirectionInput.Direction.Down) 
                //             {
                //                 float jumpHeight = Status.GetFloat(StatusPropertyType.JumpHeightStat);
                //                 _controller.SetVerticalForce(Mathf.Sqrt(2f * jumpHeight * Mathf.Abs(_controller.Parameters.Gravity)));
                //             }

                //             if(IsLadderClimbing == true) // 사다리를 타고 있었다면 종료한다.
                //             {
                //                 EndClimb(CharacterStates.MovementStates.Jumping);
                //             }
                //         }
                //     }
                // }
                // else if (_input.Value.Shoot.IsPressed() == true)
                // {
                //     if (Hub.s.uiman.DummySlot.HasItem() == true)
                //     {
                //         Hub.s.uiman.DropItem();
                //     }
                //     else
                //     {
                //         Shoot();
                //     }
                // }
                // else if (_input.Value.Interact.WasPressedThisFrame() == true)
                // {
                //     Interact();
                // }

            }

            //_mouseTargetFinder.UpdateTarget(mousePosition);
        }

        protected override void OnFixedUpdateCharacter()
        {
            base.OnFixedUpdateCharacter();

            // if(_condition < CharacterConditions.RobotControl)
            // {
            //     CheckMoveDistance();

            //     CheckChangeStatForSecond();
            // }
        }
        
        private void UpdateHorizontalForce(Input.DirectionInput.Direction inDirection)
        {
            float horizontalForce = 0f;
            if (inDirection == Input.DirectionInput.Direction.Left || inDirection == Input.DirectionInput.Direction.Right)
            {
                // if(IsLadderClimbing == true)
                // {
                //     horizontalForce = (float)inDirection * 1.75f;
                // }
                // else
                {
                    // float speed = Status.GetFloat(StatusPropertyType.MoveSpeedStat);
                    horizontalForce = (float)inDirection * 5; //speed;
                }
            }

            //_controller.SetHorizontalForce(horizontalForce);
        }

        private void UpdateVerticalForce(Input.DirectionInput.Direction inDirection)
        {
            
        }
    }
}
 
