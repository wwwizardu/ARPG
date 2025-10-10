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
        Str,                // 힘
        Dex,                // 민첩
        Int,                // 지능
        Hp,                 // 체력
        Mp,                 // 마나
        HpGeneration,       // 체력 재생
        MpGeneration,       // 마나 재생
        AttackMin,          // 최소 공격력
        AttackMax,          // 최대 공격력
        CriRate,            // 치명타 확률
        CriDamage,          // 치명타 피해
        MoveSpeed,          // 이동 속도
        AttackSpeed,        // 공격 속도
        CastSpeed,          // 시전 속도
        Defense,            // 방어력
        FireResist,         // 화염 저항
        IceResist,          // 냉기 저항
        LightningResist,    // 번개 저항
        PoisonResist,       // 독 저항
        Luck,               // 행운
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

    public enum TeamType
    {
        None = 0,       // 중립
        Player = 1,     // 플레이어 팀
        Monster = 2,    // 몬스터 팀
    }

    static public ushort PLAYER_INVENTORY_SLOTCOUNT_MAX = 60;
}
