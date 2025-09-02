using UnityEngine;

public class GlobalEnum
{
    public enum TileType
    {
        Ground = 0,     // 맨땅
        Glass = 1,      // 잔디
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
}
