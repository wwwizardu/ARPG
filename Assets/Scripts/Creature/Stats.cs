#nullable enable
using ARPG.Data;
using ARPG.Tables;
using UnityEngine;

namespace ARPG.Creature
{
    public class StatController
    {
        private Stats _statsTotal = new Stats();
        private Stats _statsBase = new Stats();
        private Stats? _statEquipment = null;
        private CharacterBase? _owner = null;
        
        public void Initialize(CharacterBase? inCreature)
        {
            _owner = inCreature;

            if(_owner is ArpgPlayer)
            {
                _statEquipment = new Stats();
                _statEquipment.Reset();
            }
        }

        public void Reset()
        {
            if (_owner?.Table == null)
                return;

            _statsBase.Reset();

            _statsBase.Str = _owner.Table.Stat.Str;
            _statsBase.Dex = _owner.Table.Stat.Dex;
            _statsBase.Int = _owner.Table.Stat.Int;
            _statsBase.MaxHp = _owner.Table.Stat.MaxHp;
            _statsBase.MaxMp = _owner.Table.Stat.MaxMp;
            _statsBase.CurrentHp = _owner.Table.Stat.MaxHp;
            _statsBase.CurrentMp = _owner.Table.Stat.MaxMp;
            _statsBase.HpGeneration = _owner.Table.Stat.HpGeneration;
            _statsBase.MpGeneration = _owner.Table.Stat.MpGeneration;
            _statsBase.AttackMin = _owner.Table.Stat.AttackMin;
            _statsBase.AttackMax = _owner.Table.Stat.AttackMax;
            _statsBase.CriRate = _owner.Table.Stat.CriRate;
            _statsBase.CriDamage = _owner.Table.Stat.CriDamage;
            _statsBase.MoveSpeed = _owner.Table.Stat.MoveSpeed;
            _statsBase.AttackSpeed = _owner.Table.Stat.AttackSpeed;
            _statsBase.CastSpeed = _owner.Table.Stat.CastSpeed;
            _statsBase.Defense = _owner.Table.Stat.Defense;
            _statsBase.FireResist = _owner.Table.Stat.FireResist;
            _statsBase.IceResist = _owner.Table.Stat.IceResist;
            _statsBase.LightningResist = _owner.Table.Stat.LightningResist;
            _statsBase.PoisonResist = _owner.Table.Stat.PoisonResist;
            _statsBase.Luck = _owner.Table.Stat.Luck;

            UpdateStat();
        }

        public int GetHp()
        {
            return _statsTotal.CurrentHp;
        }

        public void SetHp(int inHp)
        {
            _statsTotal.CurrentHp = Mathf.Clamp(inHp, 0, _statsTotal.MaxHp);
        }

        public int GetMp()
        {
            return _statsTotal.CurrentMp;
        }

        public void SetMp(int inMp)
        {
            _statsTotal.CurrentMp = Mathf.Clamp(inMp, 0, _statsTotal.MaxMp);
        }

        public float GetHpRatio()
        {
            if (_statsTotal.MaxHp == 0)
                return 0.0f;
                
            return (float)_statsTotal.CurrentHp / _statsTotal.MaxHp;
        }

        public float GetMoveSpeed()
        {
            return _statsTotal.MoveSpeed;
        }

        public void IncreaseHp(int inHeal)
        {
            _statsTotal.CurrentHp += inHeal;

            if (_statsTotal.CurrentHp > _statsTotal.MaxHp)
                _statsTotal.CurrentHp = _statsTotal.MaxHp;
        }

        public void IncreaseMp(int inMana)
        {
            _statsTotal.CurrentMp += inMana;

            if (_statsTotal.CurrentMp > _statsTotal.MaxMp)
                _statsTotal.CurrentMp = _statsTotal.MaxMp;
        }

        public void DecreaseHp(int inDamage)
        {
            _statsTotal.CurrentHp -= inDamage;

            if (_statsTotal.CurrentHp < 0)
                _statsTotal.CurrentHp = 0;
        }

        public void DecreaseMp(int inMana)
        {
            _statsTotal.CurrentMp -= inMana;

            if (_statsTotal.CurrentMp < 0)
                _statsTotal.CurrentMp = 0;
        }

        public int GetAttackMin()
        {
            return _statsTotal.AttackMin;
        }

        public int GetAttackMax()
        {
            return _statsTotal.AttackMax;
        }

        public void UpdateStat()
        {
            _statsTotal.CopyFrom(_statsBase);

            if(_statEquipment != null)
            {
                UpdateEquipmentStat();
            }
        }

        public Stats GetStats()
        {
            return _statsTotal;
        }

        public int GetStat(GlobalEnum.Stat inStat)
        {
            return _statsTotal[inStat];
        }

        public void SetStat(GlobalEnum.Stat inStat, int inValue)
        {
            _statsTotal[inStat] = inValue;
        }

        public void UpdateEquipmentStat()
        {
            if (_owner == null || _statEquipment == null)
                return;

            _statEquipment.Reset();

            Data.ItemData?[] equipItems = AR.s.Data.Player._inventoryEquip;
            for (int i = 0; i < (int)GlobalEnum.EquipSlotType.Max; i++)
            {
                Data.ItemData? item = equipItems[i];
                if (item?.Equipment?.StatData != null)
                {
                    for (int j = 0; j < item.Equipment.StatData.Prefix.Count; j++)
                    {
                        _statEquipment[item.Equipment.StatData.Prefix[j].Type] += item.Equipment.StatData.Prefix[j].Value;
                    }
                    for (int j = 0; j < item.Equipment.StatData.Postfix.Count; j++)
                    {
                        _statEquipment[item.Equipment.StatData.Postfix[j].Type] += item.Equipment.StatData.Postfix[j].Value;
                    }
                }
            }
            
            _statsTotal.Add(_statEquipment);    
        }

        public void Load()
        {
            Reset(); // 기본 스탯 테이블 수치로 초기화
        }
    }

    public class Stats
    {
        private readonly int[] _stats = new int[System.Enum.GetValues(typeof(GlobalEnum.Stat)).Length];

        public int CurrentHp;       // 현재 체력
        public int CurrentMp;       // 현재 마나

        // 인덱서로 GlobalEnum.Stat을 통해 접근
        public int this[GlobalEnum.Stat stat]
        {
            get { return _stats[(int)stat]; }
            set { _stats[(int)stat] = value; }
        }

        // 편의를 위한 프로퍼티들
        public int Str
        {
            get { return this[GlobalEnum.Stat.Str]; }
            set { this[GlobalEnum.Stat.Str] = value; }
        }

        public int Dex
        {
            get { return this[GlobalEnum.Stat.Dex]; }
            set { this[GlobalEnum.Stat.Dex] = value; }
        }

        public int Int
        {
            get { return this[GlobalEnum.Stat.Int]; }
            set { this[GlobalEnum.Stat.Int] = value; }
        }

        public int MaxHp
        {
            get { return this[GlobalEnum.Stat.Hp]; }
            set { this[GlobalEnum.Stat.Hp] = value; }
        }

        public int MaxMp
        {
            get { return this[GlobalEnum.Stat.Mp]; }
            set { this[GlobalEnum.Stat.Mp] = value; }
        }

        public int HpGeneration
        {
            get { return this[GlobalEnum.Stat.HpGeneration]; }
            set { this[GlobalEnum.Stat.HpGeneration] = value; }
        }

        public int MpGeneration
        {
            get { return this[GlobalEnum.Stat.MpGeneration]; }
            set { this[GlobalEnum.Stat.MpGeneration] = value; }
        }

        public int AttackMin
        {
            get { return this[GlobalEnum.Stat.AttackMin]; }
            set { this[GlobalEnum.Stat.AttackMin] = value; }
        }

        public int AttackMax
        {
            get { return this[GlobalEnum.Stat.AttackMax]; }
            set { this[GlobalEnum.Stat.AttackMax] = value; }
        }

        public int CriRate
        {
            get { return this[GlobalEnum.Stat.CriRate]; }
            set { this[GlobalEnum.Stat.CriRate] = value; }
        }

        public int CriDamage
        {
            get { return this[GlobalEnum.Stat.CriDamage]; }
            set { this[GlobalEnum.Stat.CriDamage] = value; }
        }

        public int MoveSpeed
        {
            get { return this[GlobalEnum.Stat.MoveSpeed]; }
            set { this[GlobalEnum.Stat.MoveSpeed] = value; }
        }

        public int AttackSpeed
        {
            get { return this[GlobalEnum.Stat.AttackSpeed]; }
            set { this[GlobalEnum.Stat.AttackSpeed] = value; }
        }

        public int CastSpeed
        {
            get { return this[GlobalEnum.Stat.CastSpeed]; }
            set { this[GlobalEnum.Stat.CastSpeed] = value; }
        }

        public int Defense
        {
            get { return this[GlobalEnum.Stat.Defense]; }
            set { this[GlobalEnum.Stat.Defense] = value; }
        }

        public int FireResist
        {
            get { return this[GlobalEnum.Stat.FireResist]; }
            set { this[GlobalEnum.Stat.FireResist] = value; }
        }

        public int IceResist
        {
            get { return this[GlobalEnum.Stat.IceResist]; }
            set { this[GlobalEnum.Stat.IceResist] = value; }
        }

        public int LightningResist
        {
            get { return this[GlobalEnum.Stat.LightningResist]; }
            set { this[GlobalEnum.Stat.LightningResist] = value; }
        }

        public int PoisonResist
        {
            get { return this[GlobalEnum.Stat.PoisonResist]; }
            set { this[GlobalEnum.Stat.PoisonResist] = value; }
        }

        public int Luck
        {
            get { return this[GlobalEnum.Stat.Luck]; }
            set { this[GlobalEnum.Stat.Luck] = value; }
        }

        public void Add(Stats other)
        {
            for (int i = 0; i < _stats.Length; i++)
            {
                _stats[i] += other._stats[i];
            }
            CurrentHp += other.CurrentHp;
            CurrentMp += other.CurrentMp;
        }

        public void CopyFrom(Stats source)
        {
            for (int i = 0; i < _stats.Length; i++)
            {
                _stats[i] = source._stats[i];
            }
            CurrentHp = source.CurrentHp;
            CurrentMp = source.CurrentMp;
        }

        public void Reset()
        {
            for (int i = 0; i < _stats.Length; i++)
            {
                _stats[i] = 0;
            }
            CurrentHp = 0;
            CurrentMp = 0;
        }
    }
}


