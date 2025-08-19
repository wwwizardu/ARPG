#nullable enable
using System;
using UnityEngine;

namespace ARPG.Creature
{
    public class CharacterBase : MonoBehaviour
    {
        [SerializeField] protected Sprite _characterSprite;
        [SerializeField] protected SpriteRenderer _sr;
        [SerializeField] protected Animator _animator;

        [SerializeField] protected TMPro.TextMeshPro _textName;

        protected CharacterConditions _condition = CharacterConditions.None;
        protected MovementStates _moveState = MovementStates.Null;
        protected MovementStates _movementStatePrev = MovementStates.Null;

        protected Vector2 _pervPos;
        protected Vector2 _currentPos;
        protected bool _initialized = false;
        
        public CharacterConditions State { get { return _condition; } }

        public virtual void Initialize()
        {
            Reset(); // Reset character

            _condition = CharacterConditions.Normal;
            OnChangeMovementState(MovementStates.Idle);

            _initialized = true;
        }

        public virtual void Reset()
        {
            // Reset character state
            _sr.sprite = _characterSprite;
            // _textName.text = string.Empty;

            _pervPos = transform.position;
            _currentPos = transform.position;
        }

        protected virtual void UpdateInput()
        {
            // Handle character input here
        }

        public void PlayAnimation(Animation inAnimation, bool isLoop, int inTrackIndex = 0, float inSpeed = 1f, Action? onAnimDone = null)
        {
            if (inAnimation == Animation.Idle)
            {
                _animator.SetTrigger("Idle");
            }
            else if (inAnimation == Animation.Walk)
            {
                _animator.SetTrigger("Walk");
            }
            // else if(inAnimation == Animation.Jump)
            // {
            //     _animator.Play("Jump", inTrackIndex, 0f);
            // }
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
                // 스킬이 실행중일 때는 캐릭터가 이동하지 않고 제자리에 있었다면 Idle 애니로 돌아가지 않는다.
                // if (_skillController.CurrentSkill != null && _skillController.CurrentSkill.IsRunning == true)
                // {
                //     if (_skillController.CurrentSkill.CharacterMoved == true)
                //     {
                //         PlayAnimation(Animation.Idle, true);
                //     }
                // }
                // else
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

            // _skillController.StopAllSkill();

            ChangeConditionState(CharacterConditions.Dead);
        }

        void Update()
        {
            if (_initialized == false)
                return;

            UpdateInput(); // 입력 업데이트

            UpdateConditionsState(); // 캐릭터 상태 업데이트

            UpdateMovementState(); // 캐릭터 이동 상태 업데이트

            //if (Input.GetKeyUp(KeyCode.U))
            //{
            //    Hub.s.uiman.Show<MerchantUI>("UI/MerchantUI", UIManager.Layer.Main);
            //}
        }

        private void FixedUpdate()
        {
            if (_initialized == false)
                return;

            OnFixedUpdateCharacter();
        }

        protected virtual void OnFixedUpdateCharacter()
        {
            if (AR.s == null || AR.s.Map == null /*|| MapManager.mapInitialized == false*/)
                return;

            if (CharacterConditions.Dead <= _condition) // 캐릭터가 죽은 상태라면
                return;

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

        }
        
        protected void OnChangeMovementState(MovementStates inNew)
        {
            if (_moveState == inNew)
                return;

            Debug.Log($"[Character] OnChangeMovementState - {_movementStatePrev} -> {inNew}");

            _movementStatePrev = _moveState;
            _moveState = inNew;

            UpdateAnimator();
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
        UseSkill,
        BlockMoveAnimation, // 이 밑으로는 캐릭터 MoveState에 따라 애니메이션을 변경해주지 않는 상태
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
        Null,
        Idle,
        Walking,        // 달리기
        Jumping,
    }

}