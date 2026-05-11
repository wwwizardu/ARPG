using System;
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

    public enum BuildableSpawnType
    {
        Tile = 0,           // Tilemap 셀로 렌더, objectId를 타일 비트에 기록
        Entity = 1,         // EntityBase GameObject로 스폰, BuildingManager가 관리
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
        PoisonRate,         // 중독 확률
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
        ProjectileCountAdd, // 발사체 추가 개수 (Add 합산만 의미 있음. SkillEffect.SpawnProjectile에서 base+이 stat으로 최종 발사 수 결정)
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

    /// <summary>
    /// Mod가 붙을 수 있는 장비 타입 비트마스크. ModTable.AllowedEquipTypes에서 사용.
    /// 한 mod가 여러 카테고리에 동시 허용될 수 있어 [Flags] 비트마스크로 표현.
    /// </summary>
    [Flags]
    public enum EquipmentTypeMask
    {
        None       = 0,
        Weapon     = 1 << 0,
        Helmet     = 1 << 1,
        Armor      = 1 << 2,
        Gloves     = 1 << 3,
        Boots      = 1 << 4,
        Necklace   = 1 << 5,
        Ring       = 1 << 6,
        Belt       = 1 << 7,
        Earring    = 1 << 8,

        AllArmor   = Helmet | Armor | Gloves | Boots | Belt,
        AllJewelry = Necklace | Ring | Earring,
        All        = AllArmor | AllJewelry | Weapon,
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
        SkillBook = 100,  // 스킬북 (새 스킬 획득)
        SkillPage = 101,  // 스킬 페이지 (스킬북에 장착하는 SkillEffect 조각)

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

        SkillBook = 100,          // 스킬북 (새 스킬 획득)
        SkillPage = 101,          // 스킬 페이지
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
        Move        = 1 << 13,  // 이동 계열 (점프, 대시, 순간이동 등)
    }

    public enum SkillType
    {
        None = 0,
        Melee,
        Range,
        Buff,
        Summon,
        Jump,       // 점프 스킬 (시전자를 포물선 궤적으로 이동)
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
        Position,   // 지점 지정 (마우스 위치로 도약하는 Leap Slam 등)
    }

    /// <summary>
    /// 스킬 효과(SkillEffect)의 발동 시점.
    /// SkillEffectExecutor가 이 트리거에 매칭되는 효과만 실행한다.
    /// </summary>
    public enum SkillTrigger : byte
    {
        OnSkillCommand,     // 시전 명령 처리 직전 (캔슬·시전 위임 가능)
        OnSkillStart,       // Process 단계 진입 직후 (시전 확정)
        OnHit,              // 적중한 적마다 1회
        OnCrit,             // 치명타 적중 (OnHit과 함께 발화)
        OnKill,             // 적중으로 적이 사망
        OnProjectileSpawn,  // 발사체 생성 직후
        OnProjectileHit,    // 발사체 적중 시
        OnSkillEnd,         // End 단계 종료
    }

    /// <summary>
    /// 스킬에 합성 가능한 효과 타입. SkillEffectTable이 이 enum + (Param1/2/3)으로 효과를 정의.
    /// 새 효과 추가 시 enum 1줄 + SkillEffectExecutor에 case 1개만 추가하면 데이터로 합성 가능.
    /// </summary>
    public enum SkillEffectType : byte
    {
        None = 0,
        LifeStealOnHit,         // OnHit. Param1=흡혈 비율(%)
        ApplyBuffOnHit,         // OnHit. Param1=BuffId, Param2=스택, Param3=지속시간 오버라이드(0=기본)
        DelegateToTotem,        // OnSkillCommand. Param1=토템 생존시간(초), Param2=캐스팅 거리 오프셋(0=시전자 위치). 시전 캔슬 후 토템이 자율 발사
        SpawnProjectile,        // 스킬 본인의 발사체와 무관한 추가 발사체 스폰 용도(예: "폭발 시 작은 폭탄 5개"). Param1=별도 ProjectileId, Param2=Count(base), Param3=미사용. 스킬 자체의 발사체 개수 조절은 SkillTable.BaseProjectileCount + Stat.ProjectileCountAdd로 처리하므로 일반 Multi Shot에는 사용하지 않음.
        // 향후 확장 슬롯 (구현은 후속 페이즈)
        // ManaRestoreOnKill, SpawnProjectileOnHit, SpawnAreaEffectOnKill,
        // KnockbackOnHit, DelegateToMine, DelegateToTrap, ...
    }

    /// <summary>
    /// 스킬 페이지 발동 조건. P-1에서는 데이터만 보관하고, 실제 조건 체크는 후속 페이즈에서 활성화한다.
    /// </summary>
    public enum PageCondition : byte
    {
        None = 0,
        TargetHpBelow,
        TargetStunned,
        TargetIgnited,
        OwnerHpBelow,
        WallNearby,
        KnockbackTarget,
        IsBoss,
    }

    public enum AiType
    {
        None = 0,
        NormalMonster,
    }

    public enum JobType
    {
        None,           // 무직
        Farmer,         // 농부 (식량 생산 - CropPlot)
        Blacksmith,     // 대장장이 (도구/무기 제작 - Furnace+Anvil)
        Merchant,       // 상인 (교역 - MerchantStall)
        Hunter,         // 사냥꾼 (식량/가죽 획득 - DryingRack)
        Builder,        // 건축가 (건설)
        Scholar,        // 학자 (연구)
        Guard,          // 경비병 (방어)
        Chief,          // 촌장 (마을 관리)
        Woodcutter,     // Phase D: 벌목꾼 (Wood 생산 - ChoppingBlock)
        Miner,          // Phase D: 광부 (Stone/Iron 생산 - MiningCart)
        Gatherer,       // Phase D: 채집꾼 (만능 - 작업 오브젝트 없을 때 fallback)
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
        Jump = 4,
    }

    static public ushort PLAYER_INVENTORY_SLOTCOUNT_MAX = 60;

    // 스킬북 시스템 (SKILLBOOK_DESIGN.md §3.1)
    public const int PLAYER_SKILL_SLOT_COUNT = 10;          // 플레이어 스킬 슬롯 수 (0~9). 슬롯 0=좌클릭, 1=Space, 2~9=숫자키 1~8
    public const int DEFAULT_SKILLBOOK_ITEM_ID_COMMON = 5000; // 신규 플레이어에게 시드되는 Common 등급 책 ItemId
    public const int DEFAULT_STARTER_SKILL_ID = 1;          // 신규 플레이어 슬롯 0에 시드되는 기본 스킬 (Strike)
}
