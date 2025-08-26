using UnityEngine;

namespace ARPG.Creature
{
    public class StatController
    {
        private Stats _stats = new Stats();

        private Stats _statEquipment = new Stats();

        public void Initialize()
        {

        }

        public void Reset()
        {

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
    }
}


