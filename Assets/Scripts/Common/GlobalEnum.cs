using UnityEngine;

public class GlobalEnum
{
    public enum EntityType
    {
        Actor,
        Item,
        SkillEffect,
    }

    public enum TileLayer
    {
        Ground,
        Object,
        Npc,
    }

    public enum TileType
    {
        None = 0,           // 빈 타일
        Ground = 1,         // 맨땅
        Glass = 2,          // 잔디
        StoneGround = 3,    // 돌 바닥
        
    }

    public enum ObjectType
    {
        None = 0,           // 빈 타일
        Stone = 1,          // 돌 벽
        Npc = 2,            // NPC가 위치하는 타일 (맵 에디터에서만 사용)
        WoodWall = 3,       // 나무 벽
    }

    public enum TileFlag : ulong
    {
        None = 0,

        // 하위 10비트: Ground Layer (0-9번 비트, 최대 1024개 타입)
        GroundLayerMask = 0x3FFUL,  // 0x3FF = 0b1111111111 (10비트)

        // 다음 10비트: Object Layer (10-19번 비트, 최대 1024개 타입)
        ObjectLayerMask = 0x3FFUL << 10,  // 0xFFC00 (10비트)

        // 다음 1비트: Hill (20번 비트)
        Hill = 1UL << 20,

        // 다음 1비트: MonsterSpawn (21번 비트)
        MonsterSpawn = 1UL << 21,

        // 다음 1비트: Blocked (22번 비트) - 이동 불가능
        Blocked = 1UL << 22,
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
        AttackMin,          // 물리 최소 공격력
        AttackMax,          // 물리 최대 공격력
        CriRate,            // 치명타 확률
        CriDamageMul,       // 치명타 피해 배율
        MoveSpeed,          // 이동 속도 (1초당 이동 거리)
        MoveSpeedMul,       // 이동 속도 증가 배율
        AttackSpeed,        // 공격 속도 (초당 공격 횟수, 100배 정수: 1.2 → 120)
        AttackSpeedMul,     // 공격 속도 증가 배율
        CastSpeedMul,       // 시전 속도 증가 배율
        Defense,            // 방어력
        FireResist,         // 화염 저항
        IceResist,          // 냉기 저항
        LightningResist,    // 번개 저항
        PoisonResist,       // 독 저항
        Luck,               // 행운
        BloodingRate,       // 출혈 확률
        IgniteRate,         // 점화 확률
        FireAttackMin,      // 화염 최소 공격력
        FireAttackMax,      // 화염 최대 공격력
        IceAttackMin,       // 냉기 최소 공격력
        IceAttackMax,       // 냉기 최대 공격력
        LightningAttackMin, // 번개 최소 공격력
        LightningAttackMax, // 번개 최대 공격력
        PoisonAttackMin,    // 독 최소 공격력
        PoisonAttackMax,    // 독 최대 공격력
        Evasion,            // 회피
        BlockChance,        // 블록 확률
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

    public enum EquipmentType
    {
        Weapon,         // 무기
        Helmet,         // 투구
        Armor,          // 갑옷
        Gloves,         // 장갑
        Boots,          // 부츠
        Necklace,       // 목걸이
        Ring,           // 반지
        Belt,           // 허리띠
        Earring,        // 귀걸이
    }

    public enum ItemType
    {
        Currency = 1,
        Equipment = 2,
        Consumable = 3,
        Quest = 4,
        Food = 5,       // 식량
        Wood = 6,       // 목재
        Stone = 7,      // 석재
        Copper = 8,     // 구리
        Iron = 9,       // 철
        Gold = 10,      // 골드 (화폐)
        Herb = 11,      // 약초/마법 재료
        Object = 12,    // 건축 자재

    }

    public enum ItemCategory
    {
        // 장비
        Weapon,             // 무기
        Helmet,             // 투구
        Armor,              // 갑옷
        Gloves,             // 장갑
        Boots,              // 부츠
        Belt,               // 허리띠
        Necklace,           // 목걸이
        Ring,               // 반지
        Earring,            // 귀걸이
        Shield,             // 방패

        // 자원
        Currency,           // 화폐
        Wood,               // 목재
        Stone,              // 석재
        Ore,                // 광석

        // 소비/기타
        Food,               // 식량
        Herb,               // 약초
        Consumable,         // 소모품
        Quest,              // 퀘스트

        // 건축 자재
        WoodWall,           // 나무 벽 (건축 자재)
    }

    public enum TeamType
    {
        None = 0,       // 중립
        Player = 1,     // 플레이어 팀
        Monster = 2,    // 몬스터 팀
    }

    [System.Flags]
    public enum SkillTag
    {
        None        = 0,
        Attack      = 1 << 0,
        Spell       = 1 << 1,
        Physics     = 1 << 2,
        Fire        = 1 << 3,
        Ice         = 1 << 4,
        Lightning   = 1 << 5,
        Poison      = 1 << 6,
        Melee       = 1 << 7,
        Ranged      = 1 << 8,
        AoE         = 1 << 9,
        Projectile  = 1 << 10,
        Buff        = 1 << 11,
        Debuff      = 1 << 12,
    }

    public enum SkillType
    {
        None = 0,
        Melee,
        Range,
        Buff,
        Summon,
    }

    public enum SkillSubType
    {
        None = 0,
        SelfDestroy,
    }

    public enum SkillTargetType
    {
        SingleEntity,
        Direction,
    }

    public enum AiType
    {
        None = 0,
        NormalMonster,
    }

    public enum JobType
    {
        None,           // 무직
        Farmer,         // 농부 (식량 생산)
        Blacksmith,     // 대장장이 (도구/무기 제작)
        Merchant,       // 상인 (교역)
        Hunter,         // 사냥꾼 (식량/가죽 획득)
        Builder,        // 건축가 (건설)
        Scholar,        // 학자 (연구)
        Guard,          // 경비병 (방어)
        Chief           // 촌장 (마을 관리)
    }

    public enum DamageType
    {
        Physics,
        Fire,
        Ice,
        Lightning,
        Poison,
    }

    public enum BuffType
    {
        Buff,       // 버프 (긍정적 효과)
        Debuff,     // 디버프 (부정적 효과)
    }

    public enum BuffEffectType
    {
        Blooding,   // 출혈 (물리 DoT)
        Ignite,     // 점화 (화염 DoT, 스택 가능)
        Poison,     // 중독 (독 DoT + HP 재생 감소, 스택 가능)
        Chill,      // 냉기 (이동/공격 속도 감소)
    }

    public enum InventoryType
    {
        Character = 0,  // 캐릭터 인벤토리
        Equipment = 1,  // 장비 인벤토리
        Stash = 2,      // 창고
        Shop = 3,       // 상점
        Loot = 4,       // 드랍 아이템
    }


    /// <summary>
    /// Mod 효과 종류
    /// </summary>
    public enum ModEffectType
    {
        // ===== Passive - 장착 시 StatModifier로 변환 =====
        FlatStat,              // 단일 스탯 증가 (+50 HP, +20 방어력)
        AddedPhysDamage,       // 물리 데미지 추가 (Value1=Min, Value2=Max)
        AddedFireDamage,       // 화염 데미지 추가
        AddedIceDamage,        // 냉기 데미지 추가
        AddedLightningDamage,  // 번개 데미지 추가
        AddedPoisonDamage,     // 독 데미지 추가
        IncreasedStat,         // 스탯 % 증가 (+10% 공격 속도)

        // ===== OnCalculate - 데미지 계산 시 조회 =====
        IncreasedDamage,       // 조건부 데미지 증가 (Tags 매칭)
        DamageConversion,      // 데미지 속성 전환 (물리→화염 10%)
        ResistPenetration,     // 저항 관통

        // ===== OnEvent - 이벤트 발생 시 조회 =====
        BleedOnHit,            // 타격 시 출혈 (Value1=확률%)
        IgniteOnHit,           // 타격 시 점화
        FreezeOnHit,           // 타격 시 동결
        PoisonOnHit,           // 타격 시 중독
        LifeOnKill,            // 처치 시 HP 회복
        ManaOnHit,             // 타격 시 MP 회복
        LifeOnHit,             // 타격 시 HP 회복
    }

    /// <summary>
    /// Mod 적용 시점
    /// </summary>
    public enum ModApplyType
    {
        Passive,       // 장착 시 StatModifier로 자동 등록
        OnCalculate,   // 데미지 계산 시 조회
        OnEvent,       // 히트/킬 등 이벤트 시 조회
    }

    /// <summary>
    /// Mod 슬롯 종류
    /// </summary>
    public enum ModSlot
    {
        Implicit,      // 기본 내장 (아이템 타입에 고정)
        Prefix,        // 접두
        Postfix,       // 접미
    }

    /// <summary>
    /// 애니메이션 카테고리 (SpriteLibraryAsset 카테고리명과 1:1 매핑)
    /// </summary>
    public enum AnimCategory : byte
    {
        Idle = 0,
        Move = 1,
        Attack = 2,
        Dead = 3,
    }

    static public ushort PLAYER_INVENTORY_SLOTCOUNT_MAX = 60;
}
