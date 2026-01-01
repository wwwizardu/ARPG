#nullable enable
using ARPG.Component;
using ARPG.Data;
using ARPG.Tables;
using ARPG.UI;
using UnityEngine;

namespace ARPG.Creature
{
    public class ArpgPlayer : CharacterBase
    {
        protected CreatureTable? _playerTable = null;
        private Input.ArpgInputAction.PlayerActions? _input = null;

        private PlayerData _playerData = null!;
        private Item.Inventory _inventory = new Item.Inventory();

        public override Tables.CreatureTable Table => _playerTable!;
        public Item.Inventory Inventory { get { return _inventory; } }
        
        public override void Initialize()
        {
            base.Initialize();

            _team = GlobalEnum.TeamType.Player;

            _input = AR.s.UI.Input.Player;

            transform.position = new Vector3(0, 0, -0.1f); // Set initial position

            _inventory.Initialize(AR.s.Data.Player._inventory, AR.s.Data.Player._inventory.Count);

            // ECS 컴포넌트 초기화
            InitializeECSComponents();

            Debug.Log("ArpgPlayer initialized.");
        }

        private void InitializeECSComponents()
        {
            // Entity ID 생성 (간단하게 GetInstanceID 사용)
            _entityId = gameObject.GetInstanceID();

            // InputComponent 추가
            InputComponent inputComponent = new()
            {
                MoveDirection = Vector2.zero,
                MousePosition = Vector2.zero,
                IsAttacking = false,
                IsInteracting = false,
                IsSprinting = false
            };
            AR.s.Component.AddComponent(_entityId, inputComponent);

            // StatComponent 추가
            StatComponent statComponent = new()
            {
                Level = 0,
                CurrentHealth = 0,
                MaxHealth = 0,
                CurrentMana = 0,
                MaxMana = 0,
                Strength = 0,
                Agility = 0,
                Intelligence = 0
            };
            AR.s.Component.AddComponent(_entityId, statComponent);

            // StateComponent 추가
            StateComponent stateComponent = new();
            stateComponent.Condition = CharacterConditions.Normal;
            stateComponent.ConditionPrev = CharacterConditions.Normal;
            stateComponent.MoveState = MovementStates.Idle;
            stateComponent.MovementStatePrev = MovementStates.Idle;
            AR.s.Component.AddComponent(_entityId, stateComponent);

            // VelocityComponent 추가
            VelocityComponent velocityComponent = new()
            {
                Velocity = Vector2.zero,
                Speed = MoveSpeed, // CharacterBase의 MoveSpeed 사용
                SprintMultiplier = 2f
            };
            AR.s.Component.AddComponent(_entityId, velocityComponent);

            // TransformComponent 추가
            TransformComponent transformComponent = new()
            {
                Position = new Vector2(transform.position.x, transform.position.y),
                Rotation = 0f,
                Scale = Vector2.one
            };
            AR.s.Component.AddComponent(_entityId, transformComponent);

            // RenderSystem에 GameObject 등록
            var renderSystem = AR.s.System.GetSystem<Systems.System_Render>();
            if (renderSystem.HasValue)
            {
                var system = renderSystem.Value;
                system.RegisterGameObject(_entityId, gameObject);
                Debug.Log($"GameObject registered to RenderSystem for Entity {_entityId}");
            }

            // AnimationSystem에 Animator 등록 (Animator가 있는 경우)
            if(_characterInfo.Animator != null)
            {
                var animSystem = AR.s.System.GetSystem<Systems.System_Animation>();
                if (animSystem.HasValue)
                {
                    var system = animSystem.Value;
                    system.RegisterAnimator(_entityId, _characterInfo.Animator);
                    Debug.Log($"Animator registered to AnimationSystem for Entity {_entityId}");
                }
            }
            
            Debug.Log($"ECS Components initialized for Entity {_entityId}");
        }

        public override void Reset()
        {
            base.Reset();
            // Reset player-specific state if needed
            Debug.Log("ArpgPlayer reset.");
        }

        public override bool LoadTable(int inTableId)
        {
            _playerTable = AR.s.Data.GetPlayer(inTableId);
            if (_playerTable == null)
            {
                Debug.LogError($"[CharacterBase] LoadData - CreatureTable not found for Id: {inTableId}");
                return false;
            }

            return true;
        }

        public override bool Load(int inId)
        {
            if (base.Load(inId) == false)
                return false;

            if(AR.s?.Data?.Player == null)
            {
                Debug.LogError($"[ArpgPlayer] Load - AR.s.Data.Player is null, Id({inId})");
                return false;
            }

            _playerData = AR.s.Data.Player;

           // 기본 스킬을 추가한다.
            _characterInfo.SkillController.CreateSkill(1);

            // 현재 체력과 마나 설정
            _statController.SetHp(_playerData.CurrentHp);
            _statController.SetMp(_playerData.CurrentMp);

            if (_characterInfo.HpBar != null)
            {
                _characterInfo.HpBar.fillAmount = _statController.GetHpRatio();
            }

            return true;
        }

        public override float GetAttackSpeed()
        {
            // 플레이어는 장착하고 있는 무기의 공격 속도를 가지고 온다.
            ItemData? item = _playerData.GetEquipedItem(GlobalEnum.EquipSlotType.WeaponLeft);
            if(item == null)
                return 0f;
            
            if (item.Equipment == null)
            {
                Debug.LogError("[ArpgPlayer] GetAttackSpeed() - item?.Equipment is null");
                return 0f;
            }

            return item.Equipment.GetAttackSpeed();
        }

        public override (int, int) GetAttackDamage()
        {
            ItemData? item = _playerData.GetEquipedItem(GlobalEnum.EquipSlotType.WeaponLeft);
            if (item?.Equipment?.Table == null)
                return (0, 0);

            return (item.Equipment.Table.DamageMin, item.Equipment.Table.DamageMax);
        }


        public void Save(PlayerData inPlayerData)
        {
            inPlayerData.CurrentHp = _statController.GetHp();
            inPlayerData.CurrentMp = _statController.GetMp();
        }

        // protected override void InitializeSkill()
        // {
        //     _skillController.Initialize(this, _gamePlayer?.SkillData?.SkillDatas);
        // }


        protected override void UpdateInput()
        {
            if (_input == null)
                return;

            // if (_input.Value.Inventory.WasPressedThisFrame() == true) // 인벤토리 열기
            // {
            //     var characterUI = AR.s.UI.Show<UICharacter>(AddressablePath.Character, UIManager.Layer.Main);
            //     if (characterUI == null)
            //         return;
            // }

            // // if (_input.Value.UseItem.WasPressedThisFrame() == true) // 아이템 사용 시 그냥 리턴
            // // {
            // //     CharacterComponent.Toolbelt.UseHoldingItem();
            // //     return;
            // // }

            // if (_condition == CharacterConditions.Normal || _condition == CharacterConditions.UseSkill)
            // {
            //     Vector2 mousePosition = _input.Value.MouseMove.ReadValue<Vector2>();
            //     Vector3 mouseWorldPos = Camera.main.ScreenToWorldPoint(new Vector3(mousePosition.x, mousePosition.y, 0f));
            //     //SetCharacterFaceDirection(mouseWorldPos.x < transform.position.x);

            //     _inputDirection = _input.Value.Move.ReadValue<Vector2>();

            //     // 입력 방향에 따라 캐릭터 이동
            //     if (_inputDirection.IsZero() == false)
            //     {
            //         // 안전한 이동 거리 계산 및 적용
            //         Vector2 safeMovement = CalculateSafeMovement(_inputDirection);

            //         if (safeMovement.sqrMagnitude > 0.0001f)
            //         {
            //             transform.position += (Vector3)safeMovement;

            //             // velocity 업데이트 (실제 이동한 거리 기반)
            //             _velocity = safeMovement / Time.deltaTime;

            //             // 맵 매니저에 플레이어 위치 업데이트 알림
            //             if (AR.s != null && AR.s.Map != null)
            //             {
            //                 AR.s.Map.UpdateChunksAroundPlayer(transform.position);
            //             }
            //         }
            //         else
            //         {
            //             _velocity = Vector2.zero;
            //         }
            //     }
            //     else
            //     {
            //         _velocity = Vector2.zero;
            //     }

            //     // Input.DirectionInput.Direction dirHorizontal = AR.s.UI.Input.DirectionInput.GetHorizontalInput();
            //     // Input.DirectionInput.Direction dirVertical = AR.s.UI.Input.DirectionInput.GetVerticalInput();

            //     // UpdateHorizontalForce(dirHorizontal);
            //     // UpdateVerticalForce(dirVertical);

            //     // if (_input.Value.Jump.WasPressedThisFrame() == true)
            //     // {
            //     //     if (IsOwner == true)
            //     //     {
            //     //         if (_controller.State.IsGrounded == true || _isLadderClimbing == true) // 땅이거나 사다리를 타는 중에만 점프 가능
            //     //         {
            //     //             // 아래 방향키를 누르고 있었을때는 점프 안뛰고 그냥 떨어지도록
            //     //             if (dirVertical != InputController.DirectionInput.Direction.Down) 
            //     //             {
            //     //                 float jumpHeight = Status.GetFloat(StatusPropertyType.JumpHeightStat);
            //     //                 _controller.SetVerticalForce(Mathf.Sqrt(2f * jumpHeight * Mathf.Abs(_controller.Parameters.Gravity)));
            //     //             }

            //     //             if(IsLadderClimbing == true) // 사다리를 타고 있었다면 종료한다.
            //     //             {
            //     //                 EndClimb(CharacterStates.MovementStates.Jumping);
            //     //             }
            //     //         }
            //     //     }
            //     // }
            //     if (_input.Value.Attack.IsPressed() == true)
            //     {
            //         if (_input.Value.Attack.WasPressedThisFrame() == true && CheckPickupItem(mouseWorldPos) == true)
            //         {

            //         }
            //         else
            //         {
            //             StartSkill(1);
            //         }
            //     }
            //     // else if (_input.Value.Interact.WasPressedThisFrame() == true)
            //     // {
            //     //     Interact();
            //     // }

            // }

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
                        return PickupItem(item);
                    }
                }
            }

            return false;
        }

        private bool PickupItem(Item.ItemObject inItem)
        {
            if (inItem == null)
                return false;

            if (0 <= AR.s.MyPlayer?.Inventory?.AddItem(inItem.ItemData))
            {
                AR.s.Item.DestroyItem(inItem.ItemData.ItemInstanceId);

                AR.s.Data.Save();
            }

            return false;
        }

        private Vector2 CalculateSafeMovement(Vector2 inputDirection)
        {
            if (AR.s == null || AR.s.Map == null || _characterInfo == null || _characterInfo.BoxCollider == null)
                return Vector2.zero;

            float deltaTime = Time.deltaTime;
            float desiredMoveDistance = MoveSpeed * deltaTime;
            Vector2 desiredMovement = inputDirection * desiredMoveDistance;

            // BoxCollider의 bounds 정보
            Bounds bounds = _characterInfo.BoxCollider.bounds;
            Vector2 currentCenter = bounds.center;
            Vector2 halfSize = bounds.size * 0.5f;
            float margin = 0.01f;

            // X축과 Y축 각각 안전한 이동 거리 계산
            float safeX = CalculateSafeDistance(desiredMovement.x, currentCenter, halfSize, margin, true);
            float safeY = CalculateSafeDistance(desiredMovement.y, currentCenter, halfSize, margin, false);

            return new Vector2(safeX, safeY);
        }

        private float CalculateSafeDistance(float desiredMove, Vector2 center, Vector2 halfSize, float margin, bool isXAxis)
        {
            if (AR.s == null || AR.s.Map == null)
                return desiredMove;

            // 이동이 거의 없으면 그대로 반환
            if (Mathf.Abs(desiredMove) < 0.0001f)
                return desiredMove;

            int checkPoints = 3;
            float bestSafeDistance = desiredMove;

            // 이동 방향에 따라 체크할 엣지 결정
            if (isXAxis) // X축 이동
            {
                if (desiredMove > 0) // 오른쪽
                {
                    // 오른쪽 엣지 체크
                    for (int i = 0; i < checkPoints; i++)
                    {
                        float t = i / (float)(checkPoints - 1);
                        float y = center.y - halfSize.y + (halfSize.y * 2f * t);

                        // 현재 엣지 위치
                        float currentEdge = center.x + halfSize.x - margin;

                        // 목표 엣지 위치
                        float targetEdge = currentEdge + desiredMove;

                        // 목표 위치가 벽인지 체크
                        if (!AR.s.Map.IsWalkable(new Vector2(targetEdge, y)))
                        {
                            // 벽까지의 안전한 거리 계산
                            Vector3 wallCellCenter = AR.s.Map.GetCellCenterWorld(new Vector2(targetEdge, y));
                            float wallLeftEdge = wallCellCenter.x - 0.5f; // 타일 크기 1 가정
                            float safeDistance = wallLeftEdge - currentEdge - margin;

                            if (safeDistance < bestSafeDistance)
                            {
                                bestSafeDistance = Mathf.Max(0f, safeDistance);
                            }
                        }
                    }
                }
                else if (desiredMove < 0) // 왼쪽
                {
                    // 왼쪽 엣지 체크
                    for (int i = 0; i < checkPoints; i++)
                    {
                        float t = i / (float)(checkPoints - 1);
                        float y = center.y - halfSize.y + (halfSize.y * 2f * t);

                        float currentEdge = center.x - halfSize.x + margin;
                        float targetEdge = currentEdge + desiredMove;

                        if (!AR.s.Map.IsWalkable(new Vector2(targetEdge, y)))
                        {
                            Vector3 wallCellCenter = AR.s.Map.GetCellCenterWorld(new Vector2(targetEdge, y));
                            float wallRightEdge = wallCellCenter.x + 0.5f;
                            float safeDistance = wallRightEdge - currentEdge + margin;

                            if (safeDistance > bestSafeDistance)
                            {
                                bestSafeDistance = Mathf.Min(0f, safeDistance);
                            }
                        }
                    }
                }
            }
            else // Y축 이동
            {
                if (desiredMove > 0) // 위
                {
                    // 위쪽 엣지 체크
                    for (int i = 0; i < checkPoints; i++)
                    {
                        float t = i / (float)(checkPoints - 1);
                        float x = center.x - halfSize.x + (halfSize.x * 2f * t);

                        float currentEdge = center.y + halfSize.y - margin;
                        float targetEdge = currentEdge + desiredMove;

                        if (!AR.s.Map.IsWalkable(new Vector2(x, targetEdge)))
                        {
                            Vector3 wallCellCenter = AR.s.Map.GetCellCenterWorld(new Vector2(x, targetEdge));
                            float wallBottomEdge = wallCellCenter.y - 0.5f;
                            float safeDistance = wallBottomEdge - currentEdge - margin;

                            if (safeDistance < bestSafeDistance)
                            {
                                bestSafeDistance = Mathf.Max(0f, safeDistance);
                            }
                        }
                    }
                }
                else if (desiredMove < 0) // 아래
                {
                    // 아래쪽 엣지 체크
                    for (int i = 0; i < checkPoints; i++)
                    {
                        float t = i / (float)(checkPoints - 1);
                        float x = center.x - halfSize.x + (halfSize.x * 2f * t);

                        float currentEdge = center.y - halfSize.y + margin;
                        float targetEdge = currentEdge + desiredMove;

                        if (!AR.s.Map.IsWalkable(new Vector2(x, targetEdge)))
                        {
                            Vector3 wallCellCenter = AR.s.Map.GetCellCenterWorld(new Vector2(x, targetEdge));
                            float wallTopEdge = wallCellCenter.y + 0.5f;
                            float safeDistance = wallTopEdge - currentEdge + margin;

                            if (safeDistance > bestSafeDistance)
                            {
                                bestSafeDistance = Mathf.Min(0f, safeDistance);
                            }
                        }
                    }
                }
            }

            return bestSafeDistance;
        }
    }
}
 
