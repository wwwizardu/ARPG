#nullable enable
using System;
using ARPG.Tables;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.U2D.Animation;
using System.Collections;

namespace ARPG.Creature
{
    public abstract class CharacterBase : MonoBehaviour, IHittable
    {
        [SerializeField] protected CharacterInfo _characterInfo;

        protected CreatureTable? _table;

        protected StatController _statController = new StatController();

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

        public virtual CreatureTable Table { get {return _table!;} }

        public StatController Stat { get { return _statController; } }

        public CharacterConditions State { get { return _condition; } }

        public GlobalEnum.TeamType Team { get { return _team; } }

        public CharacterInfo CharacterInfo { get { return _characterInfo; } }

        public float MoveSpeed { get {return _moveSpeed; } }

        public virtual void Initialize()
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
            _characterInfo.Initialize(this);
            
            Reset();
        }

        public virtual void Reset()
        {
            // Reset character state
            // _characterInfo.TextName.text = string.Empty;

            _statController.Reset();
            _characterInfo.SkillController.Reset();
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
            if (_statController == null || _characterInfo?.SkillController == null)
            {
                Debug.LogError("[CharacterBase] UpdateStat() - null");
                return;
            }

            _statController.UpdateStat();
            _characterInfo.SkillController.UpdateSkillSpeed();

            _moveSpeed = _statController.GetStat(GlobalEnum.Stat.MoveSpeed) * (_statController.GetStat(GlobalEnum.Stat.MoveSpeedMul) * 0.01f);
        }

        public virtual void OnHit(CharacterBase inAttacker, int inDamage)
        {
            Debug.Log($"[CharacterBase] OnHit - Attacker: {inAttacker.name}, Damage: {inDamage}");
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
            _characterInfo.SkillController.StartSkill(inIndex);
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

        public virtual void UpdateAnimator()
        {
            if (CharacterConditions.BlockMoveAnimation <= _condition)
                return;

            if (_moveState == MovementStates.Idle)
            {
                // 스킬이 실행중일 때는 Idle 애니메이션을 실행하지 않음
                if (_characterInfo.SkillController.CurrentSkill != null && _characterInfo.SkillController.CurrentSkill.IsRunning == true)
                {

                }
                else
                {
                    PlayAnimation(Animation.Idle, true);
                }
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

            _characterInfo.SkillController.StopAllSkill();

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

            _characterInfo.SkillController.SkillUpdate(inDeltaTime);

            

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

            UpdateAnimator();
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