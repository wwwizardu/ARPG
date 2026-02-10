#nullable enable
using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using ARPG.Data;
using ARPG.Tables;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.UI;
using UnityEngine.U2D.Animation;

namespace ARPG.Creature
{
    public abstract class CharacterBase : Base.EntityBase
    {
        [SerializeField] protected CharacterInfo _characterInfo;

        protected CreatureTable? _table;

        protected CharacterConditions _condition = CharacterConditions.None;

        protected bool _initialized = false;

        protected PlayableAnimator? _playableAnimator;
        protected CancellationTokenSource? _animationCts;

        public virtual CreatureTable Table { get {return _table!;} }

        public CharacterConditions State { get { return _condition; } }

        public override void Initialize()
        {
            _characterInfo.Sr.sprite = _characterInfo.CharacterSprite;

            _condition = CharacterConditions.Normal;

            _characterInfo.Initialize(this);

            RegisterMessageHandler<Message.DamageMessage>(OnDamage);
            RegisterMessageHandler<Message.DeathMessage>(OnDead);

            Reset();
        }

        public override void Reset()
        {
            // Reset character state
            // _characterInfo.TextName.text = string.Empty;

        }

        protected override void OnDestroy()
        {
            base.OnDestroy();

            // 애니메이션 로드 취소
            _animationCts?.Cancel();
            _animationCts?.Dispose();
            _animationCts = null;
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
                Direction = Vector2.zero,
                Speed = 0, // CharacterBase의 MoveSpeed 사용
                SprintMultiplier = 2f
            };
            AR.s.Component.AddComponent(_entityId, velocityComponent);

            // SpriteAnimationComponent 추가 (테이블 기반 애니메이션 설정)
            Component.SpriteAnimationComponent spriteAnimComp = new()
            {
                AnimationTableId = Table.AnimationId,
                LoadState = Component.AnimationLoadState.None,
                PlaybackSpeed = 1f
            };
            AR.s.Component.AddComponent(_entityId, spriteAnimComp);

            // RenderSystem에 GameObject 등록
            var renderSystem = AR.s.System.GetSystem<Systems.System_Render>();
            if (renderSystem != null)
            {
                renderSystem.RegisterGameObject(_entityId, gameObject);
                Debug.Log($"GameObject registered to RenderSystem for Entity {_entityId}");
            }

            // AnimationSystem에 PlayableAnimator 등록
            _playableAnimator = _characterInfo.gameObject.GetComponent<PlayableAnimator>();
            if (_playableAnimator == null)
            {
                _playableAnimator = _characterInfo.gameObject.AddComponent<PlayableAnimator>();
            }

            var animSystem = AR.s.System.GetSystem<Systems.System_Animation>();
            if (animSystem != null)
            {
                animSystem.RegisterPlayableAnimator(_entityId, _playableAnimator);
                Debug.Log($"PlayableAnimator registered to AnimationSystem for Entity {_entityId}");
            }

            AR.s.Component.AddComponent(_entityId, new Component.AnimatorComponent());

            // 비동기로 애니메이션 클립 로드 및 초기화
            InitializeAnimationAsync().Forget();
        }

        /// <summary>
        /// AnimationTable 데이터를 기반으로 SpriteLibraryAsset과 AnimationClip을 Addressable에서 로드합니다.
        /// </summary>
        protected virtual async UniTaskVoid InitializeAnimationAsync()
        {
            if (_playableAnimator == null || Table?.AnimationData == null)
            {
                return;
            }

            UpdateAnimationLoadState(Component.AnimationLoadState.Loading);
            _animationCts = new CancellationTokenSource();

            try
            {
                Tables.AnimationTable animData = Table.AnimationData;

                // 1. SpriteLibraryAsset 런타임 로드
                if (string.IsNullOrEmpty(animData.SpriteLibraryPath) == false && _characterInfo.SpriteLibrary != null)
                {
                    var slAsset = await Addressables.LoadAssetAsync<SpriteLibraryAsset>(
                        animData.SpriteLibraryPath).ToUniTask(cancellationToken: _animationCts.Token);
                    if (slAsset != null)
                    {
                        _characterInfo.SpriteLibrary.spriteLibraryAsset = slAsset;
                    }
                }

                // 2. AnimationClip 테이블 기반 로드
                string[] clipNames = animData.ClipNameArray;
                string clipPath = animData.AnimClipPath;

                if (clipNames == null || clipNames.Length == 0)
                {
                    Debug.LogWarning($"[{GetType().Name}] No animation clip names in AnimationTable Id: {animData.Id}");
                    UpdateAnimationLoadState(Component.AnimationLoadState.Failed);
                    return;
                }

                List<AnimationClip> clips = new List<AnimationClip>();

                for (int i = 0; i < clipNames.Length; i++)
                {
                    _animationCts.Token.ThrowIfCancellationRequested();

                    string path = $"{clipPath}/{clipNames[i]}";
                    var handle = Addressables.LoadAssetAsync<AnimationClip>(path);

                    try
                    {
                        AnimationClip clip = await handle.ToUniTask(cancellationToken: _animationCts.Token);

                        if (handle.Status == AsyncOperationStatus.Succeeded && clip != null)
                        {
                            clips.Add(clip);
                        }
                        else
                        {
                            Debug.LogWarning($"[{GetType().Name}] Failed to load animation clip: {path}");
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        Addressables.Release(handle);
                        throw;
                    }
                }

                // 3. 로드 완료 후 객체가 파괴되었는지 확인
                if (this == null || _playableAnimator == null)
                {
                    ReleaseAnimationClips(clips);
                    return;
                }

                // 4. PlayableAnimator 초기화
                _playableAnimator.Initialize(clips.ToArray());
                UpdateAnimationLoadState(Component.AnimationLoadState.Loaded);
                Debug.Log($"PlayableAnimator initialized with {clips.Count} clips for Entity {_entityId}");
            }
            catch (OperationCanceledException)
            {
                Debug.Log($"Animation initialization cancelled for Entity {_entityId}");
            }
            catch (Exception e)
            {
                UpdateAnimationLoadState(Component.AnimationLoadState.Failed);
                Debug.LogError($"[{GetType().Name}] Failed to initialize animation: {e.Message}");
            }
        }

        private void UpdateAnimationLoadState(Component.AnimationLoadState state)
        {
            if (AR.s.Component.TryGetComponent<Component.SpriteAnimationComponent>(_entityId, out var comp))
            {
                comp.LoadState = state;
                AR.s.Component.SetComponent(_entityId, comp);
            }
        }

        /// <summary>
        /// 로드된 애니메이션 클립들을 해제합니다.
        /// </summary>
        protected void ReleaseAnimationClips(List<AnimationClip> clips)
        {
            if (clips == null)
            {
                return;
            }

            for (int i = 0; i < clips.Count; i++)
            {
                if (clips[i] != null)
                {
                    Addressables.Release(clips[i]);
                }
            }
        }

        public virtual bool LoadTable(int inId)
        {
            return false;
        }

        public virtual bool Load(int inTableId)
        {
            if (LoadTable(inTableId) == false)
            {
                Debug.LogError($"[CharacterBase] LoadData - Failed to load table for Id: {inTableId}");
                return false;
            }

            // if (_characterInfo.TextName != null)
            // {
            //     _characterInfo.TextName.text = Table!.Name;
            // }

            _initialized = true;

            return true;
        }

        public virtual void OnHit(CharacterBase? inAttacker, bool isOnHit, GlobalEnum.DamageType inDamageType, int inDamage)
        {
            // TODO: StatComponent 기반 HP 감소 로직 구현 필요
        }

        public virtual void OnHeal(int inHp)
        {
            // TODO: StatComponent 기반 HP 회복 로직 구현 필요
        }

        public virtual void OnChangeMp(int inDeltaMp)
        {
            // TODO: StatComponent 기반 MP 변경 로직 구현 필요
        }

        public virtual float GetAttackSpeed()
        {
            return 1f;
        }

        public virtual (int, int) GetAttackDamage()
        {
            return (0, 0);
        }

        public virtual void OnAddBuff(BuffEffect inBuff)
        {
            GameObject? iconObject = AR.s.Data.GetIconPrefab(inBuff);
            if(iconObject != null)
            {
                // if (_buffIconDic.ContainsKey(inBuff.Type) == true)
                // {
                //     AR.s.Data.RestoreIconPrefab(iconObject);
                // }
                // else
                // {
                //     iconObject.transform.SetParent(_characterInfo.BuffIconRoot);
                //     iconObject.transform.localScale = Vector3.one;
                //     _buffIconDic.Add(inBuff.Type, iconObject);
                // }
            }
        }
        
        public virtual void OnRemoveBuff(BuffEffect inBuff)
        {
            // if (_buffIconDic.TryGetValue(inBuff.Type, out GameObject? iconObject))
            // {
            //     AR.s.Data.RestoreIconPrefab(iconObject);
            //     _buffIconDic.Remove(inBuff.Type);
            // }
        }

        protected virtual void Dead()
        {
            _condition = CharacterConditions.Dead;
        }

        private void OnDamage(Message.DamageMessage msg)
        {
            Debug.Log($"데미지 받음: {msg.DamageAmount}, 치명타: {msg.IsCritical}");
            _characterInfo.HpBar.fillAmount = (float)msg.CurrentHp / (float)msg.MaxHp;
        }

        private void OnDead(Message.DeathMessage msg)
        {
            Dead();

            // 죽음 처리 후 제거 예약
            if (_entityId >= 0)
            {
                AR.s.Component.AddComponent(_entityId, new Component.DestroyTag());
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