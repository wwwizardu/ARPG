#nullable enable
using UnityEngine;

namespace ARPG.Creature
{
    public class StatController
    {
        private Stats _stats = new Stats();

        private Stats _statEquipment = new Stats();
        private CharacterBase? _owner = null;
        
        public void Initialize()
        {
            _statEquipment.Reset();
        }

        public void Reset()
        {
            if (_owner == null || _owner.Table == null)
                return;

            _stats.Str = _owner.Table.Str;
            _stats.Dex = _owner.Table.Dex;
            _stats.Int = _owner.Table.Int;
            _stats.MaxHp = _owner.Table.MaxHp;
            _stats.MaxMp = _owner.Table.MaxMp;
            _stats.CurrentHp = _owner.Table.MaxHp;
            _stats.CurrentMp = _owner.Table.MaxMp;
            _stats.HpGeneration = _owner.Table.HpGeneration;
            _stats.MpGeneration = _owner.Table.MpGeneration;
            _stats.AttackMin = _owner.Table.AttackMin;
            _stats.AttackMax = _owner.Table.AttackMax;
            _stats.CriRate = _owner.Table.CriRate;
            _stats.CriDamage = _owner.Table.CriDamage;
            _stats.MoveSpeed = _owner.Table.MoveSpeed;
            _stats.AttackSpeed = _owner.Table.AttackSpeed;
            _stats.CastSpeed = _owner.Table.CastSpeed;
            _stats.Defense = _owner.Table.Defense;
            _stats.FireResist = _owner.Table.FireResist;
            _stats.IceResist = _owner.Table.IceResist;
            _stats.LightningResist = _owner.Table.LightningResist;
            _stats.PoisonResist = _owner.Table.PoisonResist;
            _stats.Luck = _owner.Table.Luck;
        }

        public int GetHp()
        {
            return _stats.CurrentHp;
        }

        public float GetMoveSpeed()
        {
            return _stats.MoveSpeed + _statEquipment.MoveSpeed;
        }

        public void DecreaseHp(int inDamage)
        {
            _stats.CurrentHp -= inDamage;

            if (_stats.CurrentHp < 0)
                _stats.CurrentHp = 0;
        }

        public int GetAttackMin()
        {
            return _stats.AttackMin + _statEquipment.AttackMin;
        }

        public int GetAttackMax()
        {
            return _stats.AttackMax + _statEquipment.AttackMax;
        }

        public void LoadFromTable(CharacterBase inCreature, Tables.CreatureTable inTable)
        {
            _owner = inCreature;

            Reset();
        }
    }

    public class Stats
    {
        public int Str; // 힘
        public int Dex; // 민첩
        public int Int; // 지능

        public int CurrentHp;       // 현재 체력
        public int CurrentMp;       // 현재 마나
        public int MaxHp;           // 최대 체력
        public int MaxMp;           // 최대 마나   
        public int HpGeneration;    // 체력 재생
        public int MpGeneration;    // 마나 재생   
        public int AttackMin;       // 최소 공격력
        public int AttackMax;       // 최대 공격력
        public int CriRate;         // 치명타 확률
        public int CriDamage;       // 치명타 피해
        public int MoveSpeed;       // 이동 속도
        public int AttackSpeed;     // 공격 속도
        public int CastSpeed;       // 시전 속도  
        public int Defense;         // 방어력 
        public int FireResist;      // 화염 저항
        public int IceResist;       // 냉기 저항
        public int LightningResist; // 번개 저항
        public int PoisonResist;
        public int Luck;            // 운
        
        public void Reset()
        {
            Str = 0;
            Dex = 0;
            Int = 0;

            CurrentHp = 0;
            CurrentMp = 0;
            MaxHp = 0;
            MaxMp = 0;
            HpGeneration = 0;
            MpGeneration = 0;
            AttackMin = 0;
            AttackMax = 0;
            CriRate = 0;
            CriDamage = 0;
            MoveSpeed = 0;
            AttackSpeed = 0;
            CastSpeed = 0;
            Defense = 0;
            FireResist = 0;
            IceResist = 0;
            LightningResist = 0;
            PoisonResist = 0;
            Luck = 0;
        }
    }
}


