#nullable enable
using System;
using ARPG.Tables;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.U2D.Animation;
using System.Collections;
using ARPG.Data;
using System.Collections.Generic;

namespace ARPG.Creature
{
    public abstract class CharacterBase : Base.EntityBase, IMovable, IHittable
    {
        [SerializeField] protected GlobalEnum.EntityType _entityType;
        [SerializeField] protected CharacterInfo _characterInfo;

        protected CreatureTable? _table;

        protected StatController _statController = new StatController();
        protected BuffController _buffController = new BuffController();
        protected MoveController _moveController = new();

        protected CharacterConditions _condition = CharacterConditions.None;
        protected MovementStates _moveState = MovementStates.None;
        protected MovementStates _movementStatePrev = MovementStates.None;

        protected SpriteLibrary _spriteLibrary;
        protected Vector2 _inputDirection = Vector2.zero;
        protected Vector2 _velocity = Vector2.zero;

        protected GlobalEnum.TeamType _team = GlobalEnum.TeamType.None;

        protected Vector2 _pervPos;
        protected Vector2 _currentPos;
        protected float _moveSpeed;
        protected bool _initialized = false;

        protected Coroutine? _LoopUpdateCo = null;
        protected WaitForSeconds _waitForSeconds = new WaitForSeconds(1f);

        protected Dictionary<GlobalEnum.BuffEffectType, GameObject> _buffIconDic = new();

        public int EntityId => _entityId;
        public GlobalEnum.EntityType EntityType { get { return _entityType; } }
        public virtual CreatureTable Table { get {return _table!;} }

        public StatController Stat { get { return _statController; } }

        public CharacterConditions State { get { return _condition; } }

        public GlobalEnum.TeamType Team { get { return _team; } }

        public CharacterInfo CharacterInfo { get { return _characterInfo; } }

        public float MoveSpeed { get {return _moveSpeed; } }

        public override void Initialize()
        {
            _characterInfo.Sr.sprite = _characterInfo.CharacterSprite;
            SpriteLibrary sl = _characterInfo.Sr.GetComponent<SpriteLibrary>();
            if (sl != null && _characterInfo.SpriteLibraryAsset != null)
            {
                sl.spriteLibraryAsset = _characterInfo.SpriteLibraryAsset;
                _spriteLibrary = sl;
            }
            
            _pervPos = transform.position;
            _currentPos = transform.position;

            _condition = CharacterConditions.Normal;
            OnChangeMovementState(MovementStates.Idle);

            _statController.Initialize(this);
            _buffController.Initialize(this);
            _characterInfo.Initialize(this);
            
            Reset();
        }

        public override void Reset()
        {
            // Reset character state
            // _characterInfo.TextName.text = string.Empty;

            _statController.Reset();
            _buffController.Reset();
            //_characterInfo.SkillController.Reset();
        }

        public override void InitializeECSComponents()
        {
            base.InitializeECSComponents();

            // StatComponent 추가
            Component.StatComponent statComponent = new();
            statComponent.InitializeFromTable(Table.Stat);
            AR.s.Component.AddComponent(_entityId, statComponent);

            // StateComponent 추가
            Component.StateComponent stateComponent = new();
            stateComponent.Condition = CharacterConditions.Normal;
            stateComponent.ConditionPrev = CharacterConditions.Normal;
            stateComponent.MoveState = MovementStates.Idle;
            stateComponent.MovementStatePrev = MovementStates.Idle;
            AR.s.Component.AddComponent(_entityId, stateComponent);

            // VelocityComponent 추가
            Component.VelocityComponent velocityComponent = new()
            {
                Velocity = Vector2.zero,
                Speed = MoveSpeed, // CharacterBase의 MoveSpeed 사용
                SprintMultiplier = 2f
            };
            AR.s.Component.AddComponent(_entityId, velocityComponent);

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
        }

        public Vector3 Vector3 { get {return _velocity;} }

        public virtual void UpdateVelocity(float inDeltaTime)
        {
            
        }

        public virtual void UpdatePosition(float inDeltaTime)
        {
            
        }

        public virtual bool LoadTable(int inId)
        {
            return false;
        }

        public virtual bool Load(int inId)
        {
            if (LoadTable(inId) == false)
            {
                Debug.LogError($"[CharacterBase] LoadData - Failed to load table for Id: {inId}");
                return false;
            }

            // if (_characterInfo.TextName != null)
            // {
            //     _characterInfo.TextName.text = Table!.Name;
            // }

            // Load stats from table
            _statController.Load();

            UpdateStat();

            if (_statController.GetStat(GlobalEnum.Stat.HpGeneration) > 0 || _statController.GetStat(GlobalEnum.Stat.MpGeneration) > 0)
            {
                if (_LoopUpdateCo != null)
                {
                    StopCoroutine(_LoopUpdateCo);
                    _LoopUpdateCo = null;
                }

                _LoopUpdateCo = StartCoroutine(LoopUpdate());
            }

            if (_characterInfo.HpBar != null)
            {
                _characterInfo.HpBar.fillAmount = _statController.GetHpRatio();
            }

            _initialized = true;

            return true;
        }

        public virtual void UpdateStat()
        {
            if (_statController == null /*|| _characterInfo?.SkillController == null?*/)
            {
                Debug.LogError("[CharacterBase] UpdateStat() - null");
                return;
            }

            _statController.UpdateStat();
            //_characterInfo.SkillController.UpdateSkillSpeed();

            _moveSpeed = _statController.GetStat(GlobalEnum.Stat.MoveSpeed) * (_statController.GetStat(GlobalEnum.Stat.MoveSpeedMul) * 0.01f);

            if(AR.s.Component.TryGetComponent<Component.VelocityComponent>(_entityId, out var velocity) == true)
            {
                velocity.Speed = _moveSpeed;
                AR.s.Component.SetComponent(_entityId, velocity);
            }
        }

        public virtual void OnHit(CharacterBase? inAttacker, bool isOnHit, GlobalEnum.DamageType inDamageType, int inDamage)
        {
            _statController.DecreaseHp(inDamage);

            if (Stat.GetHp() <= 0)
            {
                Dead();
            }

            _characterInfo.HpBar.fillAmount = Stat.GetHpRatio();
        }

        public virtual void OnHeal(int inHp)
        {
            _statController.IncreaseHp(inHp);
            _characterInfo.HpBar.fillAmount = _statController.GetHpRatio();
        }

        public virtual void OnChangeMp(int inDeltaMp)
        {
            if (inDeltaMp > 0)
            {
                _statController.IncreaseMp(inDeltaMp);
            }
            else if (inDeltaMp < 0)
            {
                _statController.DecreaseMp(-inDeltaMp);
            }
        }

        protected virtual void UpdateInput()
        {
            // Handle character input here
        }
        
        public virtual bool StartSkill(int inIndex)
        {
            //_characterInfo.SkillController.StartSkill(inIndex);
            return true;
        }

        public void PlayAnimation(Animation inAnimation, bool isLoop, int inTrackIndex = 0, float inSpeed = 1f, Action? onAnimDone = null)
        {
            if (inAnimation == Animation.Idle)
            {
                _characterInfo.Animator.SetTrigger("Idle");
            }
            else if (inAnimation == Animation.Walk)
            {
                _characterInfo.Animator.SetTrigger("Walk");
            }
            else if (inAnimation == Animation.Attack)
            {
                _characterInfo.Animator.SetTrigger("Attack");
            }
        }

        public virtual float GetAttackSpeed()
        {
            return 1f;
        }

        public virtual (int, int) GetAttackDamage()
        {
            return (0, 0);
        }

        protected virtual void SetAnimation(int inIndex)
        {

        }

        public void Stop()
        {
            // if (IsOwner == true)
            // {
            //     _controller.SetHorizontalForce(0f);
            // }

            OnChangeMovementState(MovementStates.Idle);
            UpdateMovementState();
        }

        public void OnCompleteSkill(int inSkillId)
        {
            OnChangeMovementState(MovementStates.Idle);
        }

        public void OnStopSkill(int inSkillId)
        {
            // Handle skill stop logic
        }

        public void AddBuff(int inBuffId, int inEffectValue = 0)
        {
            _buffController.AddBuff(inBuffId, inEffectValue);
        }

        public virtual void OnAddBuff(BuffEffect inBuff)
        {
            GameObject? iconObject = AR.s.Data.GetIconPrefab(inBuff);
            if(iconObject != null)
            {
                if (_buffIconDic.ContainsKey(inBuff.Type) == true)
                {
                    AR.s.Data.RestoreIconPrefab(iconObject);
                }
                else
                {
                    iconObject.transform.SetParent(_characterInfo.BuffIconRoot);
                    iconObject.transform.localScale = Vector3.one;
                    _buffIconDic.Add(inBuff.Type, iconObject);
                }
            }
        }
        
        public virtual void OnRemoveBuff(BuffEffect inBuff)
        {
            if (_buffIconDic.TryGetValue(inBuff.Type, out GameObject? iconObject))
            {
                AR.s.Data.RestoreIconPrefab(iconObject);
                _buffIconDic.Remove(inBuff.Type);
            }
        }

        public virtual void UpdateAnimator()
        {
            if (CharacterConditions.BlockMoveAnimation <= _condition)
                return;

            if (_moveState == MovementStates.Idle)
            {
                // 스킬이 실행중일 때는 Idle 애니메이션을 실행하지 않음
                // if (_characterInfo.SkillController.CurrentSkill != null && _characterInfo.SkillController.CurrentSkill.IsRunning == true)
                // {

                // }
                // else
                // {
                //     PlayAnimation(Animation.Idle, true);
                // }
            }
            else if (_moveState == MovementStates.Walking)
            {
                PlayAnimation(Animation.Walk, true);
            }
            else if (_moveState == MovementStates.Jumping)
            {
                PlayAnimation(Animation.Jump, false);
            }
            // else if (_moveState == MovementStates.Falling)
            // {
            //     PlayAnimation(Animation.Jump_Idle, true);
            // }
            // else if (_moveState == CharacterStates.MovementStates.Landing)
            // {
            //     PlayAnimation(Animation.Jump_Eed, false);
            // }
        }

        public void ChangeConditionState(CharacterConditions inState, bool isForce = false)
        {
            if (_condition == inState) // 같은 상태면 변경할 필요 없음
                return;

            // Stunned, Dead 상태에서 다른 상태로 변하는 것은 이곳에서 처리하지 않고 따로 함수를 만들어서 처리한다.
            if (_condition == CharacterConditions.Stunned || _condition == CharacterConditions.Dead)
            {
                return;
            }

            //if (IsOwner == true)
            {
                _condition = inState;
            }
        }

        protected virtual void Dead()
        {
            // if (IsOwner == true)
            // {
            //     _controller.SetHorizontalForce(0);
            // }

            //_characterInfo.SkillController.StopAllSkill();

            ChangeConditionState(CharacterConditions.Dead);
        }

        void Update()
        {
            if (_initialized == false)
                return;

            OnUpdate();
        }

        private void FixedUpdate()
        {
            if (_initialized == false)
                return;

            OnFixedUpdateCharacter(Time.fixedDeltaTime);
        }

        protected virtual void OnUpdate()
        {
            UpdateInput(); // 입력 업데이트

            UpdateConditionsState(); // 캐릭터 상태 업데이트

            UpdateMovementState(); // 캐릭터 이동 상태 업데이트

            //if (Input.GetKeyUp(KeyCode.U))
            //{
            //    Hub.s.uiman.Show<MerchantUI>("UI/MerchantUI", UIManager.Layer.Main);
            //}
        }

        protected virtual void OnFixedUpdateCharacter(float inDeltaTime)
        {
            if (AR.s == null || AR.s.Map == null /*|| MapManager.mapInitialized == false*/)
                return;

            if (CharacterConditions.Dead <= _condition) // 캐릭터가 죽은 상태라면
                return;

            //_characterInfo.SkillController.SkillUpdate(inDeltaTime);

            _buffController.UpdateTick(inDeltaTime);

            

            // if (IsOwner == true && GetPlayerTilePosition(out Vector2Int tilePos) == true)
            // {
            //     _currentMapTilePos = tilePos;

            //     if (_currentMapTilePos != _prevMapTilePos) // 캐릭터 위치가 달라졌다면
            //     {
            //         //Debug.Log($"[Character] OnFixedUpdateCharacter - Pos({_currentMapTilePos.x}, {_currentMapTilePos.y})");

            //         Vector2Int currentChunkIndex = GetPlayerChunk();
            //         if (_currentChunkIndex != currentChunkIndex)
            //         {
            //             _currentChunkIndex = currentChunkIndex;
            //             UpdateAroundChunk();
            //         }

            //         _currentChunkTilePos = _currentMapTilePos - (_currentChunkIndex * MapGenerator.CHUNK_SIZE);

            //         CheckSpecialBlock(true); // 특수 블럭 체크(사다리, 바닥)
            //         UpdateSight();

            //         _prevMapTilePos = _currentMapTilePos;
            //     }

            //     _floorController.Update(Time.fixedDeltaTime);
            // }
        }

        protected virtual bool UpdateConditionsState()
        {
            if (CharacterConditions.Dead <= _condition)
                return false;

            // float hp = Status.GetFloat(Shared.Status.StatusPropertyType.HpCurrent);
            // if (Mathf.Approximately(hp, 0f) || hp <= 0f)
            // {
            //     Dead();
            //     return false;
            // }

            // if (_conditionState.Value == CharacterStates.CharacterConditions.InstallStructure && _conditionTime + 0.2f < Time.time)
            // {
            //     ChangeConditionState(CharacterStates.CharacterConditions.Normal);
            // }

            return true;
        }

        protected virtual void UpdateMovementState()
        {
            if (_velocity.IsZero() == true)
            {
                OnChangeMovementState(MovementStates.Idle);
            }
            else
            {
                OnChangeMovementState(MovementStates.Walking);
            }
        }

        protected void OnChangeMovementState(MovementStates inNew)
        {
            if (_moveState == inNew)
                return;

            //Debug.Log($"[Character] OnChangeMovementState - {_moveState} -> {inNew}");

            _movementStatePrev = _moveState;
            _moveState = inNew;

            //UpdateAnimator();
        }

        protected IEnumerator LoopUpdate()
        {
            while (true)
            {
                yield return _waitForSeconds;

                if (_initialized == false) // 초기화가 안된 상태라면 대기
                    continue;

                // Hp, Mp 자연 회복
                OnHeal(_statController.GetStat(GlobalEnum.Stat.HpGeneration));
                OnChangeMp(_statController.GetStat(GlobalEnum.Stat.MpGeneration));
            }
        }

		protected Vector2 _boundsTopLeftCorner;
		protected Vector2 _boundsBottomLeftCorner;
		protected Vector2 _boundsTopRightCorner;
		protected Vector2 _boundsBottomRightCorner;
		protected Vector2 _boundsCenter;
		protected Vector2 _bounds;
		protected float _boundsWidth;
        protected float _boundsHeight;
        
        protected Vector2 _horizontalRayCastFromBottom = Vector2.zero;
		protected Vector2 _horizontalRayCastToTop = Vector2.zero;
		protected Vector2 _verticalRayCastFromLeft = Vector2.zero;
		protected Vector2 _verticalRayCastToRight = Vector2.zero;
		protected Vector2 _aboveRayCastStart = Vector2.zero;
		protected Vector2 _aboveRayCastEnd = Vector2.zero;
        protected Vector2 _rayCastOrigin = Vector2.zero;
        protected RaycastHit2D[] _sideHitsStorage;

        [Tooltip("the number of rays cast horizontally")]
        public int NumberOfHorizontalRays = 8;
        
        [Tooltip("an offset to apply vertically to the origin of the controller's raycasts that will have an impact on obstacle detection. Tweak this to adapt to your character's and obstacle's size")]
        public float ObstacleHeightTolerance = 0.05f;
		
		[Tooltip("a small value added to all horizontal raycasts to accomodate for edge cases")]
		public float RayOffsetHorizontal = 0.05f;
		/// a small value added to all raycasts to accomodate for edge cases	
		[Tooltip("a small value added to all vertical raycasts to accomodate for edge cases")]
		public float RayOffsetVertical = 0.05f;
		/// an extra length you an add when casting rays horizontally
		[Tooltip("an extra length you an add when casting rays horizontally")]
		public float RayExtraLengthHorizontal = 0f;
		/// an extra length you an add when casting rays vertically
		[Tooltip("an extra length you an add when casting rays vertically")]
		public float RayExtraLengthVertical = 0f;
        
        protected virtual void SetRaysParameters()
        {
            BoxCollider2D boxCollider = _characterInfo.BoxCollider;
            float top = boxCollider.offset.y + (boxCollider.size.y / 2f);
            float bottom = boxCollider.offset.y - (boxCollider.size.y / 2f);
            float left = boxCollider.offset.x - (boxCollider.size.x / 2f);
            float right = boxCollider.offset.x + (boxCollider.size.x / 2f);

            _boundsTopLeftCorner.x = left;
            _boundsTopLeftCorner.y = top;

            _boundsTopRightCorner.x = right;
            _boundsTopRightCorner.y = top;

            _boundsBottomLeftCorner.x = left;
            _boundsBottomLeftCorner.y = bottom;

            _boundsBottomRightCorner.x = right;
            _boundsBottomRightCorner.y = bottom;

            _boundsTopLeftCorner = transform.TransformPoint(_boundsTopLeftCorner);
            _boundsTopRightCorner = transform.TransformPoint(_boundsTopRightCorner);
            _boundsBottomLeftCorner = transform.TransformPoint(_boundsBottomLeftCorner);
            _boundsBottomRightCorner = transform.TransformPoint(_boundsBottomRightCorner);
            _boundsCenter = boxCollider.bounds.center;

            _boundsWidth = Vector2.Distance(_boundsBottomLeftCorner, _boundsBottomRightCorner);
            _boundsHeight = Vector2.Distance(_boundsBottomLeftCorner, _boundsTopLeftCorner);
        }
        
        protected virtual void CastRaysToTheSides(float raysDirection)
        {
            // we determine the origin of our rays
            _horizontalRayCastFromBottom = (_boundsBottomRightCorner + _boundsBottomLeftCorner) / 2;
            _horizontalRayCastToTop = (_boundsTopLeftCorner + _boundsTopRightCorner) / 2;
            _horizontalRayCastFromBottom = _horizontalRayCastFromBottom + (Vector2)transform.up * ObstacleHeightTolerance;
            _horizontalRayCastToTop = _horizontalRayCastToTop - (Vector2)transform.up * ObstacleHeightTolerance;

            // we determine the length of our rays
            // float horizontalRayLength = Mathf.Abs(_speed.x * DeltaTime) + _boundsWidth / 2 + RayOffsetHorizontal * 2 + RayExtraLengthHorizontal;

            // // we resize our storage if needed
            // if (_sideHitsStorage.Length != NumberOfHorizontalRays)
            // {
            //     _sideHitsStorage = new RaycastHit2D[NumberOfHorizontalRays];
            // }

            // // we cast rays to the sides
            // for (int i = 0; i < NumberOfHorizontalRays; i++)
            // {
            //     Vector2 rayOriginPoint = Vector2.Lerp(_horizontalRayCastFromBottom, _horizontalRayCastToTop, (float)i / (float)(NumberOfHorizontalRays - 1));

            //     // if we were grounded last frame and if this is our first ray, we don't cast against one way platforms
            //     if (State.WasGroundedLastFrame && i == 0)
            //     {
            //         _sideHitsStorage[i] = RayCast(rayOriginPoint, raysDirection * (transform.right), horizontalRayLength, PlatformMask, MMColors.Indigo, Parameters.DrawRaycastsGizmos);
            //     }
            //     else
            //     {
            //         _sideHitsStorage[i] = RayCast(rayOriginPoint, raysDirection * (transform.right), horizontalRayLength, PlatformMask & ~OneWayPlatformMask & ~MovingOneWayPlatformMask, MMColors.Indigo, Parameters.DrawRaycastsGizmos);
            //     }
            //     // if we've hit something
            //     if (_sideHitsStorage[i].distance > 0)
            //     {
            //         // if this collider is on our ignore list, we break
            //         if (_sideHitsStorage[i].collider == _ignoredCollider)
            //         {
            //             break;
            //         }

            //         // we determine and store our current lateral slope angle
            //         float hitAngle = Mathf.Abs(Vector2.Angle(_sideHitsStorage[i].normal, transform.up));

            //         if (OneWayPlatformMask.MMContains(_sideHitsStorage[i].collider.gameObject))
            //         {
            //             if (hitAngle > 90)
            //             {
            //                 break;
            //             }
            //         }

            //         // we check if this is our movement direction
            //         if (_movementDirection == raysDirection)
            //         {
            //             State.LateralSlopeAngle = hitAngle;
            //         }

            //         // if the lateral slope angle is higher than our maximum slope angle, then we've hit a wall, and stop x movement accordingly
            //         if (hitAngle > Parameters.MaximumSlopeAngle)
            //         {
            //             if (raysDirection < 0)
            //             {
            //                 State.IsCollidingLeft = true;
            //                 State.DistanceToLeftCollider = _sideHitsStorage[i].distance;
            //             }
            //             else
            //             {
            //                 State.IsCollidingRight = true;
            //                 State.DistanceToRightCollider = _sideHitsStorage[i].distance;
            //             }

            //             if ((_movementDirection == raysDirection) || (CastRaysOnBothSides && (_speed.x == 0f)))
            //             {
            //                 CurrentWallCollider = _sideHitsStorage[i].collider.gameObject;
            //                 State.SlopeAngleOK = false;

            //                 float distance = MMMaths.DistanceBetweenPointAndLine(_sideHitsStorage[i].point, _horizontalRayCastFromBottom, _horizontalRayCastToTop);
            //                 if (raysDirection <= 0)
            //                 {
            //                     _newPosition.x = -distance
            //                                      + _boundsWidth / 2
            //                                      + RayOffsetHorizontal * 2;
            //                 }
            //                 else
            //                 {
            //                     _newPosition.x = distance
            //                                      - _boundsWidth / 2
            //                                      - RayOffsetHorizontal * 2;
            //                 }

            //                 // if we're in the air, we prevent the character from being pushed back.
            //                 if (!State.IsGrounded && (Speed.y != 0) && (!Mathf.Approximately(hitAngle, 90f)))
            //                 {
            //                     _newPosition.x = 0;
            //                 }

            //                 _contactList.Add(_sideHitsStorage[i]);
            //                 _speed.x = 0;
            //                 _shouldComputeNewSpeed = true;
            //             }

            //             break;
            //         }
            //     }
            // }
        }
        
        protected RaycastHit2D RayCast(Vector2 rayOriginPoint, Vector2 rayDirection, float rayDistance, LayerMask mask, Color color,bool drawGizmo=false)
        {
#if UNITY_EDITOR
            if (drawGizmo)
            {
                Debug.DrawRay(rayOriginPoint, rayDirection * rayDistance, color);
            }
#endif
            
			return Physics2D.Raycast(rayOriginPoint,rayDirection,rayDistance,mask);		
		}
    }

    public enum Animation
    {
        Idle,
        Attack,
        Walk,
        Jump,
        Hit,
        Dead,
    }

    public enum CharacterConditions // *** 상태를 추가할 때 위치에 신경써서 추가해주세요 ***
    {
        None,
        Normal,
        BlockMoveAnimation, // 이 밑으로는 캐릭터 MoveState에 따라 애니메이션을 변경해주지 않는 상태
        UseSkill,
        InstallStructure,
        Interact,
        Stunned,            // Stunned 밑으로는 Input도 영양을 주지 못하는 상태
        Dead,
        Revival,
    }

    /// The possible Movement States the character can be in. These usually correspond to their own class, 
    /// but it's not mandatory
    public enum MovementStates
    {
        None,
        Idle,
        Walking,        // 달리기
        Jumping,
    }

}