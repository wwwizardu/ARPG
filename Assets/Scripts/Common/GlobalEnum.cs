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

    /// <summary>
    /// 바이옴 분류 (MAP_DESIGN.md §4.2). per-tile warped Perlin으로 결정.
    /// Whittaker 모델 — 온도 × 습도 (1차) + 고도 (변형).
    /// </summary>
    public enum BiomeType
    {
        // 온도 × 습도 (Whittaker)
        Plains = 0,         // 중간 온도 + 중간 습도
        Forest = 1,         // 중간 온도 + 습함
        Desert = 2,         // 더움 + 건조
        Swamp = 3,          // 더움 + 매우 습함 / 저고도 + 습함
        Steppe = 4,         // 더움 + 중간 습도 (초원)
        Tundra = 5,         // 추움 + 건조
        Snow = 6,           // 추움 + 중간~습함

        // 고도 변형
        Mountain = 10,      // 고도 ↑ — 다른 축 덮어쓰기

        // 향후 (Live Backbone — Phase 7 이후)
        Wasteland = 30,
        Any = 100,
    }

    /// <summary>
    /// Race Settlement(MAP_DESIGN.md §7)의 종족 분류. biome → race 매핑에 사용.
    /// </summary>
    public enum RaceType
    {
        Human = 0,
        Elf = 1,
        Dwarf = 2,
        Orc = 3,
        Nomad = 4,
        Lizardman = 5,
        Yeti = 6,
    }

    /// <summary>
    /// Race Settlement 크기. (race × size) 조합으로 청사진 카탈로그 선택.
    /// Phase 진입 시 단계 수 결정 (§12 #14) — 우선 Village 1단계로 시작.
    /// </summary>
    public enum SettlementSize
    {
        Village = 0,        // 작은 마을 (~10-20 NPC)
        Town = 1,           // 중형 마을 (~30-50)
        City = 2,           // 대형 도시 (~100+)
    }

    /// <summary>
    /// 유적 단편(LoreFragment)의 출신 종족 태그. race_origin (§6.1).
    /// LoreFragment용이라 race_origin이 *과거의* 종족도 포함 가능 (Ancient, Monster 등).
    /// </summary>
    public enum RaceOrigin
    {
        Human = 0,
        Elf = 1,
        Dwarf = 2,
        Orc = 3,
        Nomad = 4,
        Lizardman = 5,
        Yeti = 6,
        Ancient = 100,      // 멸종한 고대 종족
        Monster = 101,      // 몬스터 둥지 폐허
    }

    /// <summary>
    /// 유적 단편의 운명 키워드 (LoreFragment.FateKeyword, §6.1).
    /// "이 종족이 어떻게 사라졌는가"를 단어 하나로 표현. LLM 프롬프트에 사용.
    /// </summary>
    public enum FateKeyword
    {
        Burned = 0,
        Plagued = 1,
        Betrayed = 2,
        Sealed = 3,
        Forgotten = 4,
        Drowned = 5,
        Conquered = 6,
        Cursed = 7,
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
        CriRateMul,         // 치명타 확률 증가 배율
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
        MaxFireResist,      // 화염 저항 최대 캡 (%, 기본 75)
        MaxIceResist,       // 냉기 저항 최대 캡 (%, 기본 75)
        MaxLightningResist, // 번개 저항 최대 캡 (%, 기본 75)
        MaxPoisonResist,    // 독 저항 최대 캡 (%, 기본 75)
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
        BlockReduction,     // 막기 성공 시 피해 감소율 (%, 0이면 0.5 fallback)
        ProjectileCountAdd, // 발사체 추가 개수 (Add 합산만 의미 있음. SkillTable.BaseProjectileCount + 이 stat이 최종 발사 수)

        // ===== SkillEffect 효과 종류 (구 SkillEffectType에서 통합) =====
        // 일반 stat과 같은 enum에 합쳐 SkillEffectTable.EffectType이 stat 가산도/효과 실행도 표현하도록 일원화
        LifeSteal,           // OnHit. Param1=흡혈 비율(%)
        ApplyBuff,           // OnHit. Param1=BuffId, Param2=스택, Param3=지속시간 오버라이드(0=기본)
        DelegateToTotem,     // OnSkillCommand. Param1=토템 생존시간(초)
    }

    /// <summary>
    /// 몬스터 등급 (Archetype). MonsterArchetypeTable에서 HpMul/DmgMul 정의.
    /// PoE2 톤: Normal(white) < Magic(blue) < Rare(yellow/엘리트) < Boss(액트보스) < EndgameBoss(Pinnacle)
    /// </summary>
    public enum MonsterArchetype
    {
        Normal,         // 일반 잡몹 (white) - HpMul 1
        Magic,          // 마법몹 (blue) - HpMul 2
        Rare,           // 희귀/엘리트 (yellow) - HpMul 5
        Boss,           // 액트 보스 - HpMul 20
        EndgameBoss,    // 엔드게임 보스 (Pinnacle) - HpMul 60
    }

    public enum EquipSlotType
    {
        WeaponRight,    // 오른손 무기
        WeaponLeft,     // 왼손 무기
        Helmet,         // 투구
        Armor,          // 갑옷
        Gloves,         // 장갑
        Boots,          // 부츠
        Necklace,       // 목걸이
        RingRight,      // 오른쪽 반지
        RingLeft,       // 왼쪽 반지
        Belt,           // 허리띠
        EarringRight,   // 오른쪽 귀걸이
        EarringLeft,    // 왼쪽 귀걸이
        Max,
    }

    /// <summary>
    /// EquipmentData.EquipType uses exactly one bit. ModTable.AllowedEquipTypes can use combined bits.
    /// </summary>
    [Flags]
    public enum EquipmentTypeMask
    {
        None       = 0,
        Weapon     = 1 << 0,
        Shield     = 1 << 1,
        Helmet     = 1 << 2,
        Armor      = 1 << 3,
        Gloves     = 1 << 4,
        Boots      = 1 << 5,
        Necklace   = 1 << 6,
        Ring       = 1 << 7,
        Belt       = 1 << 8,
        Earring    = 1 << 9,

        AllArmor   = Helmet | Armor | Gloves | Boots | Belt | Shield,
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
        Direction,
        Position,   // 지점 지정 (마우스 위치로 도약하는 Leap Slam 등)
    }

    public enum SkillAimTrackingMode : byte
    {
        Default,
        SnapshotOnCommand,
        TrackUntilCommit,
        TrackPerCommit,
        TrackContinuously,
    }

    public enum SkillMovementMode : byte
    {
        Default,
        FullSpeed,
        Reduced,
        Locked,
        OwnedMotion,
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
    /// SkillEffect 처리 경로. 시트의 Kind 컬럼이 결정한다(코드 화이트리스트 대체).
    /// StatBonus는 SkillStatBonusHelper가 트리거별 컴포넌트로 사전 합산, EffectAction은 트리거 시점에 SkillEffectExecutor가 직접 실행.
    /// </summary>
    public enum SkillEffectKind : byte
    {
        StatBonus,      // 수치 가산 — SkillStatBonusOn*Component로 흡수, DamageCalculator가 소비
        EffectAction,   // 액션 — SkillEffectExecutor가 트리거 시점에 실행
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
        Implicit,      // 유니크/특수 아이템에만 붙는 추가 옵션 (장비 자격 판별엔 사용 안 함)
        Prefix,        // 접두
        Postfix,       // 접미
        Base,          // 모든 일반 장비의 표준 베이스 스탯 (EquipmentBaseStatTable에서 옴). 장비 자격 판별 기준
    }

    /// <summary>
    /// 개별 무기 인스턴스의 기본 능력을 강화하는 스탯 종류.
    /// 값은 모두 해당 기본 능력에 적용할 증가율(%)이며 일반 장비 Mod와 분리해 저장한다.
    /// </summary>
    public enum WeaponLocalStatType
    {
        PhysicsDamage,
        FireDamage,
        IceDamage,
        LightningDamage,
        PoisonDamage,
        CriticalRate,
        AttackSpeed,
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

    // NPC 인벤토리 슬롯 수 (NpcSaveData.Inventory 길이). 변경 시 세이브 마이그레이션 필요.
    public const int NPC_INVENTORY_SLOTCOUNT_MAX = 5;

    // 스킬북 시스템 (SKILLBOOK_DESIGN.md §3.1)
    public const int PLAYER_SKILL_SLOT_COUNT = 10;          // 플레이어 스킬 슬롯 수 (0~9). 슬롯 0=좌클릭, 1=Space, 2~9=숫자키 1~8
    public const int DEFAULT_SKILLBOOK_ITEM_ID_COMMON = 5000; // 신규 플레이어에게 시드되는 Common 등급 책 ItemId
    public const int DEFAULT_STARTER_SKILL_ID = 1;          // 신규 플레이어 슬롯 0에 시드되는 기본 스킬 (Strike)
    public const int DEFAULT_STARTER_WEAPON_ITEM_ID = 1;    // 신규 플레이어가 시작 시 우측 무기 슬롯에 장착하고 시작하는 무기 (낡은 단검)
}
