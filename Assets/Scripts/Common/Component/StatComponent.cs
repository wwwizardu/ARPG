using UnityEngine;

namespace ARPG.Component
{
    public struct StatComponent
    {
        public int Level;               // 레벨
        public float CurrentHealth;     // 현재 체력
        public float MaxHealth;         // 최대 체력
        public float CurrentMana;       // 현재 마나
        public float MaxMana;           // 최대 마나
        public float Strength;          // 힘
        public float Agility;           // 민첩
        public float Intelligence;      // 지능
    }
}