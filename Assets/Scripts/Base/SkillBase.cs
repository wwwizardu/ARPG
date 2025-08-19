using UnityEngine;

namespace ARPG.Skill
{
    public class SkillBase
    {
        public enum State
        {
            None,
            Start,
            Process,
            End,
        }

        protected int _id;
        //protected Bifrost.Cooked.SkillDataInfo _table;
        protected Creature.CharacterBase _character;
        protected SkillController _controller;
        protected bool _isRunning;
        protected State _state = State.None;

        protected float _startTime;
        protected float _processTime;
        protected float _endTime;

        protected byte _targetType;
        protected long _targetId;
        
        private float _time;
        private bool _initialized = false;

        public int Id { get { return _id; } }

        //public Bifrost.Cooked.SkillDataInfo Table { get { return _table; } }

        public bool IsRunning { get { return State.None < _state; } }

        public virtual void Initialize(Creature.CharacterBase inCharacter, SkillController inController, int inSkillId)
        {
            _character = inCharacter;
            _controller = inController;
            _id = inSkillId;
            _state = State.None;

            _startTime = 0f;
            _processTime = 0f;
            _endTime = 0f;

            //_table = Hub.s.dataman.ExcelDataManager.GetSkillDataInfo(inSkillId);

            _initialized = true;
        }

        public virtual void Stop()
        {
            _state = State.None;
            _controller.OnCompleteSkill(_id);
        }

        public virtual bool EnableSkill()
        {
            if (_state != State.None)
                return false;

            return true;
        }

        public virtual void SkillUpdate(float inTimeDT)
        {
            if (_initialized == false || _state == State.None)
                return;

            _time += inTimeDT;
            if (_state == State.Start)
            {
                if(_startTime < _time)
                {
                    _time = 0f;
                    _state = State.Process;
                    OnChangeState(State.Process);
                }
            }
            else if (_state == State.Process)
            {
                if (_processTime < _time)
                {
                    _time = 0f;
                    _state = State.End;
                    OnChangeState(State.End);
                }
            }
            else if (_state == State.End)
            {
                if (_endTime < _time)
                {
                    _time = 0f;
                    _state = State.None;
                    OnChangeState(State.None);
                }
            }
        }

        public virtual bool StartSkill(byte inTargetType, long inTargetId)
        {
            _targetType = inTargetType;
            _targetId = inTargetId;

            _state = State.Start;
            
            return true;
        }

        public virtual void ProcessSkill(float inTimeDT)
        {

        }

        public virtual void EndSkill()
        {

        }

        protected virtual void OnChangeState(State inNewState)
        {
            //Debug.Log($"[Skillbase] OnChangeState - State({inNewState}), Time({Time.time})");

            if (inNewState == State.None)
            {
                _controller.OnCompleteSkill(Id);
            }
        }
    }
}

