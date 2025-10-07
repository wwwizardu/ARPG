using UnityEngine;

public class GlobalEnum
{
    public enum TileType
    {
        None = 0,       // 빈 타일
        Ground = 1,     // 맨땅
        Glass = 2,      // 잔디
        StoneGround = 3, // 자갈

    }

    public enum TileFlag
    {
        None = 0,
        Hill = 1 << 4,  // 5번째 비트 (16 = 0x10)
        MonsterSpawn = 1 << 5,  // 6번째 비트 (32 = 0x20)
    }

    public enum Stat
    {
        Str,
        Dex,
        Int,
        Hp,
        Mp,
        HpGeneration,
        MpGeneration,
        AttackMin,
        AttackMax,
        CriRate,
        CriDamage,
        MoveSpeed,
        AttackSpeed,
        CastSpeed,
        Defense,
        FireResist,
        IceResist,
        LightningResist,
        PoisonResist,
        Luck,
    }

    public enum EquipSlotType
    {
        WeaponLeft,     // 왼손 무기
        WeaponRight,    // 오른손 무기
        Helmet,         // 투구
        Armor,          // 갑옷
        Gloves,         // 장갑
        Boots,          // 부츠
        Necklace,       // 목걸이
        RingLeft,       // 왼쪽 반지
        RingRight,      // 오른쪽 반지
        Belt,           // 허리띠
        EarringLeft,    // 왼쪽 귀걸이
        EarringRight,   // 오른쪽 귀걸이
        Max,
    }

    public enum ItemType
    {
        Currency = 1,
        Equipment = 2,
        Consumable = 3,
        Quest = 4,
    }
}
