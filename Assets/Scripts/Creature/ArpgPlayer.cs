#nullable enable
using ARPG.UI;
using UnityEngine;

namespace ARPG.Creature
{
    public class ArpgPlayer : CharacterBase
    {
        private Input.ArpgInputAction.PlayerActions? _input = null;
        private Inventory _inventory = null!;

        public Inventory Inventory { get { return _inventory; } }

        public override void Initialize()
        {
            base.Initialize();

            _input = AR.s.UI.Input.Player;

            transform.position = new Vector3(0, 0, -0.1f); // Set initial position

            _inventory = new Inventory();

            Debug.Log("ArpgPlayer initialized.");
        }

        public override void Reset()
        {
            base.Reset();
            // Reset player-specific state if needed
            Debug.Log("ArpgPlayer reset.");
        }

        public override bool LoadTable(int inId)
        {
            _table = AR.s.Data.GetCreature(inId);
            if (_table == null)
            {
                Debug.LogError($"[CharacterBase] LoadData - CreatureTable not found for Id: {inId}");
                return false;
            }

            return true;
        }

        public override bool Load(int inId)
        {
            if (base.Load(inId) == false)
                return false;

            if (_inventory.Load() == false)
            {
                Debug.LogError("[ArpgPlayer] Load - Failed to load inventory");
                return false;
            }
                
            return true;
        }

        // protected override void InitializeSkill()
        // {
        //     _skillController.Initialize(this, _gamePlayer?.SkillData?.SkillDatas);
        // }


        protected override void UpdateInput()
        {
            if (_input == null)
                return;

            if (_input.Value.Inventory.WasPressedThisFrame() == true) // 인벤토리 열기
            {
                var characterUI = AR.s.UI.Show<UICharacter>(AddressablePath.Character, UIManager.Layer.Main);
                if (characterUI == null)
                    return;
            }

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

                // 입력 방향에 따라 캐릭터 이동
                if (_inputDirection.IsZero() == false)
                {
                    _velocity = new Vector2(_inputDirection.x, _inputDirection.y) * _statController.GetMoveSpeed();
                    Vector3 movement = new Vector3(_velocity.x, _velocity.y, 0) * Time.deltaTime;
                    transform.position += movement;

                    // 맵 매니저에 플레이어 위치 업데이트 알림
                    if (AR.s?.Map != null)
                    {
                        AR.s.Map.UpdateChunksAroundPlayer(transform.position);
                    }
                }
                else
                {
                    _velocity = Vector2.zero;
                }

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
                if (_input.Value.Attack.IsPressed() == true)
                {
                    if (_input.Value.Attack.WasPressedThisFrame() == true && CheckPickupItem(mouseWorldPos) == true)
                    {

                    }
                    else
                    {
                        StartSkill(1);
                    }
                }
                // else if (_input.Value.Interact.WasPressedThisFrame() == true)
                // {
                //     Interact();
                // }

            }

            //_mouseTargetFinder.UpdateTarget(mousePosition);
        }

        protected override void OnFixedUpdateCharacter(float inDeltaTime)
        {
            base.OnFixedUpdateCharacter(inDeltaTime);

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

        private bool CheckPickupItem(Vector3 inMouseWorldPos)
        {
            // 2D 레이캐스트 (2D 게임의 경우)
            RaycastHit2D hit = Physics2D.Raycast(inMouseWorldPos, Vector2.zero);
            if (hit.collider != null)
            {
                // 아이템 컴포넌트 확인
                if (hit.collider.gameObject.CompareTag("DropedItem"))
                {
                    var item = hit.collider.gameObject.GetComponentInParent<Item.ItemObject>();
                    if (item != null)
                    {
                        item.Pickup();
                        return true;
                    }
                }
            }

            return false;
        }
    }
}
 
