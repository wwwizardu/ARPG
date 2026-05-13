#nullable enable
using System;
using System.Collections;
using System.Collections.Generic;
using Newtonsoft.Json;
using ARPG.Component;
using ARPG.Utility;
using GE = GlobalEnum;


namespace ARPG.Tables
{
    [Serializable]
    public class TableBase
    {
        [JsonProperty("Id")] public int Id;

        public virtual void LoadLate()
        {

        }
    }

    [Serializable]
    public class CreatureTable : TableBase
    {
        [JsonProperty("Name")] public string Name = string.Empty;

        [JsonProperty("Stat")] public int StatId;

        // 충돌/피격 메타데이터 (모두 0이면 EntityFactory에서 fallback 적용)
        [JsonProperty("MoveRadius")] public float MoveRadius;
        [JsonProperty("HitRadius")] public float HitRadius;
        [JsonProperty("HitOffsetY")] public float HitOffsetY;

        [JsonProperty("PrefabName")] public string PrefabName = string.Empty;

        [JsonProperty("AnimationId")] public int AnimationId;

        [JsonIgnore] public StatTable Stat = null!;
        [JsonIgnore] public AnimationTable? AnimationData;

        public override void LoadLate()
        {
            StatTable? statTable = AR.s.Data?.GetStat(StatId);
            if (statTable != null)
            {
                Stat = statTable;
            }
            else
            {
                throw new Exception($"CreatureTable LoadLate - StatTable not found for Id: {StatId}");
            }

            AnimationTable? animTable = AR.s.Data?.GetAnimation(AnimationId);
            if (animTable != null)
            {
                AnimationData = animTable;
            }
        }
    }

    [Serializable]
    public class StatTable : TableBase
    {
        [JsonProperty("Str")] public int Str;
        [JsonProperty("Dex")] public int Dex;
        [JsonProperty("Int")] public int Int;
        [JsonProperty("MaxHp")] public int MaxHp;
        [JsonProperty("MaxMp")] public int MaxMp;
        [JsonProperty("HpGeneration")] public int HpGeneration;
        [JsonProperty("MpGeneration")] public int MpGeneration;
        [JsonProperty("AttackMin")] public int AttackMin;
        [JsonProperty("AttackMax")] public int AttackMax;
        [JsonProperty("CriRate")] public int CriRate;
        [JsonProperty("CriDamage")] public int CriDamage;
        [JsonProperty("MoveSpeed")] public int MoveSpeed;
        [JsonProperty("AttackSpeed")] public int AttackSpeed;
        [JsonProperty("CastSpeed")] public int CastSpeed;
        [JsonProperty("Defense")] public int Defense;
        [JsonProperty("FireResist")] public int FireResist;
        [JsonProperty("IceResist")] public int IceResist;
        [JsonProperty("LightningResist")] public int LightningResist;
        [JsonProperty("PoisonResist")] public int PoisonResist;
        [JsonProperty("MaxFireResist")] public int MaxFireResist = BalanceConstants.BaseMaxResistance;
        [JsonProperty("MaxIceResist")] public int MaxIceResist = BalanceConstants.BaseMaxResistance;
        [JsonProperty("MaxLightningResist")] public int MaxLightningResist = BalanceConstants.BaseMaxResistance;
        [JsonProperty("MaxPoisonResist")] public int MaxPoisonResist = BalanceConstants.BaseMaxResistance;
        [JsonProperty("Luck")] public int Luck;
        [JsonProperty("BloodingRate")] public int BloodingRate;
        [JsonProperty("IgniteRate")] public int IgniteRate;

        // 전투 시스템 확장 스탯 (2026-04-01 추가)
        [JsonProperty("Evasion")] public int Evasion;                     // 회피율 (%)
        [JsonProperty("BlockChance")] public int BlockChance;             // 막기 확률 (%)
        [JsonProperty("BlockReduction")] public int BlockReduction;       // 막기 데미지 감소 (%)
        [JsonProperty("SkillDamage")] public int SkillDamage;             // 스킬 데미지 배율 (%)
        [JsonProperty("CooldownReduction")] public int CooldownReduction; // 쿨타임 감소 (%)
        [JsonProperty("LifeSteal")] public int LifeSteal;                 // 생명력 흡수 (%)
        [JsonProperty("Thorns")] public int Thorns;                       // 반사 데미지 (고정값)
    }

    [Serializable]
    public class MonsterTable : CreatureTable
    {
        [JsonProperty("WeaponId")] public int WeaponId;
        [JsonProperty("AiTableId")] public int AiTableId;
        [JsonProperty("DropId")] public int DropId;
        [JsonProperty("DropRateBonus")] public int DropRateBonus;
        [JsonProperty("DropRarityBonus")] public int DropRarityBonus;
        [JsonProperty("Level")] public int Level;
        [JsonProperty("Archetype")] public GE.MonsterArchetype Archetype = GE.MonsterArchetype.Normal;

        [JsonIgnore] public WeaponBaseStatTable? Weapon;
        [JsonIgnore] public AiTable? AiTable;


        public override void LoadLate()
        {
            base.LoadLate();

            WeaponBaseStatTable? equipmentTable = AR.s.Data?.WeaponBaseStatTable(WeaponId);
            if (equipmentTable != null)
            {
                Weapon = equipmentTable;
            }
            else
            {
                Weapon = null;
            }

            AiTable? aiTable = AR.s.Data?.GetAiTable(AiTableId);
            if (aiTable != null)
            {
                AiTable = aiTable;
            }
            else
            {
                AiTable = null;
            }
        }
    }

    /// <summary>
    /// 몬스터 등급별 배율 정의. EntityFactory.CreateMonster가
    /// finalHp = BaseHp × HpPerLevel^(Level-1) × HpMul 공식에 사용.
    /// 곡선 상수(HpPerLevel 등)는 BalanceConstants.cs 참조.
    /// </summary>
    [Serializable]
    public class MonsterArchetypeTable : TableBase
    {
        [JsonProperty("Archetype")] public GE.MonsterArchetype Archetype;
        [JsonProperty("HpMul")] public float HpMul = 1f;
        [JsonProperty("DmgMul")] public float DmgMul = 1f;
        [JsonProperty("Note")] public string Note = string.Empty;
    }

    [Serializable]
    public class NpcTable : CreatureTable
    {
        [JsonProperty("JobType")] public GE.JobType JobType;
        [JsonProperty("WeaponId")] public int WeaponId;
        [JsonProperty("AiTableId")] public int AiTableId;
        [JsonProperty("DropId")] public int DropId;
        [JsonProperty("DropRateBonus")] public int DropRateBonus;
        [JsonProperty("DropRarityBonus")] public int DropRarityBonus;

        [JsonIgnore] public WeaponBaseStatTable? Weapon;
        [JsonIgnore] public AiTable? AiTable;

        public override void LoadLate()
        {
            base.LoadLate();

            WeaponBaseStatTable? equipmentTable = AR.s.Data?.WeaponBaseStatTable(WeaponId);
            if (equipmentTable != null)
            {
                Weapon = equipmentTable;
            }
            else
            {
                Weapon = null;
            }

            AiTable? aiTable = AR.s.Data?.GetAiTable(AiTableId);
            if (aiTable != null)
            {
                AiTable = aiTable;
            }
            else
            {
                AiTable = null;
            }
        }
    }

    [Serializable]
    public class ItemTable : TableBase
    {
        [JsonProperty("Tier")] public int Tier;

        [JsonProperty("Name")] public string Name = string.Empty;

        [JsonProperty("ItemType")] public GE.ItemType ItemType;
        [JsonProperty("Category")] public GE.ItemCategory Category;

        [JsonProperty("Stackable")] public bool Stackable;
        [JsonProperty("MaxStack")] public int MaxStack;

        [JsonProperty("Description")] public string Description = string.Empty;

        [JsonProperty("DropRate")] public int DropRate;
        [JsonProperty("DropLevel")] public int DropLevel;

        [JsonProperty("BuildableItemId")] public int BuildableItemId;
        [JsonProperty("EquipmentFixedStatId")] public int EquipmentBastStatId;
        [JsonProperty("EquipmentStatId")] public int EquipmentStatId;
        [JsonProperty("SpriteName")] public string SpriteName = string.Empty;

        // Phase D: 상점 거래 컬럼
        [JsonProperty("BasePrice")] public int BasePrice;                       // 매물 기본가 (Gold). 0이면 비매품
        [JsonProperty("SellRatioBp")] public int SellRatioBp;                   // 매각 비율 ×100 (50=0.5, 40=0.4). 0이면 매각 불가
        [JsonProperty("ReturnResourceType")] public int ReturnResourceType;     // 마을 상인에 매각 시 마을 Storage로 환원될 자원 종류 (ItemType enum 값). 0=환원 없음. ex) 나무공예품 매각 → Wood 일부 회수
        [JsonProperty("ReturnRatioBp")] public int ReturnRatioBp;               // 환원량 비율 ×100 (basis points). returnAmount = amount × ReturnRatioBp / 100. ex) 50이면 매각 수량의 50%가 ReturnResourceType 자원으로 마을 창고에 적립. 0=환원 없음
    }

     [Serializable]
    public class BuildableItemTable : TableBase
    {
        [JsonProperty("Name")] public string Name = string.Empty;
        [JsonProperty("Tooltip")] public string Tooltip = string.Empty;
        [JsonProperty("IsBreakable")] public bool IsBreakable = false;
        [JsonProperty("HP")] public int HP = 1;
        [JsonProperty("DropItemId")] public int DropItemId = 0;
        [JsonProperty("Size_Width")] public int Size_Width = 1;
        [JsonProperty("Size_Height")] public int Size_Height = 1;
        [JsonProperty("Recipe")] public int Recipe = 0;
        [JsonProperty("ResourceName")] public string ResourceName = string.Empty;
        [JsonProperty("SpawnType")] public GE.BuildableSpawnType SpawnType = GE.BuildableSpawnType.Tile;
        [JsonProperty("AnimationId")] public int AnimationId = 0;

        // 자가 건설 소요 시간 (게임 시간, 시간 단위). 0이면 호출자 기본값.
        [JsonProperty("BuildHours")] public float BuildHours = 0f;

        // Phase B: 마을 자가 건설 비용 (NPC가 짓기 위해 필요한 자원)
        [JsonProperty("Cost_Wood")] public int Cost_Wood = 0;
        [JsonProperty("Cost_Stone")] public int Cost_Stone = 0;
        // Phase C: Metal 비용 (MiningCart, Anvil 등)
        [JsonProperty("Cost_Metal")] public int Cost_Metal = 0;

        // Phase B: 완성 시 마을의 해당 자원 Cap에 가산되는 양 (Woodpile, Chest 등)
        [JsonProperty("StorageCap_Food")] public int StorageCap_Food = 0;
        [JsonProperty("StorageCap_Wood")] public int StorageCap_Wood = 0;
        [JsonProperty("StorageCap_Stone")] public int StorageCap_Stone = 0;

        // Phase D: 데이터 정본화 (no hardcoded TableId 정책 — PHASE_D_DESIGN.md §2.2)
        // 구 Function 컬럼은 제거됨 (placeholder. 의미 없음).
        [JsonProperty("ProvidedService")] public int ProvidedService = 0;       // ProvidedService Flags enum 비트 OR
        [JsonProperty("Category")] public BuildableCategory Category = BuildableCategory.None;
        [JsonProperty("SetMembership")] public int SetMembership = 0;           // SetMemberTag Flags enum 비트 OR
        [JsonProperty("AssociatedJobType")] public int AssociatedJobType = 0;   // JobType enum (NPC 직무 매칭. 0=None)
        [JsonProperty("BaseWeight")] public int BaseWeight = 10;                // 필요도 스코어 베이스
        [JsonProperty("MaxPerVillage")] public int MaxPerVillage = 0;           // 마을당 최대 개수 (0=무제한, 1=Shrine/TownPost/Well 등 단일 시설)

        // 마을 배치 시 다른 점유 타일과의 최소 체비쇼프 거리. 0=카테고리 기본값(VillageTileFinder.GetDefaultMinSeparation) 사용.
        // 같은 카테고리/오브젝트별 차등이 필요할 때만 채움 (예: Furnace 3, Bedroll 1).
        [JsonProperty("MinSeparation")] public int MinSeparation = 0;
    }

    /// <summary>
    /// Phase D: 직업별 시간당 자원 가산값. System_VillagePassiveProduction이 NpcAssignment를 보고 가산.
    /// </summary>
    [Serializable]
    public class JobBonusTable : TableBase
    {
        [JsonProperty("JobType")] public int JobType;                       // GE.JobType enum
        [JsonProperty("Resource1Type")] public int Resource1Type;           // GE.ItemType enum (1차 가산 자원)
        [JsonProperty("Resource1PerHour")] public float Resource1PerHour;   // 시간당 가산량
        [JsonProperty("Resource2Type")] public int Resource2Type;           // (선택) 2차 가산 자원
        [JsonProperty("Resource2PerHour")] public float Resource2PerHour;
    }

    // EquipmentBaseStatTable 제거됨 → ModTable + ItemImplicitTable로 대체

    [Serializable]
    public class WeaponBaseStatTable : TableBase
    {
        [JsonProperty("EquipType")] public GE.EquipmentType EquipType;
        [JsonProperty("AttackSpeed")] public float AttackSpeed;
        [JsonProperty("Critical")] public int Critical;
        [JsonProperty("DamageMin")] public int DamageMin;
        [JsonProperty("DamageMax")] public int DamageMax;

        public WeaponBaseStatTable()
        {

        }
    }


    // EquipmentStatTable 제거됨 → ModTable (Prefix/Postfix)로 대체

    [Serializable]
    public class DropTable : TableBase
    {
        [JsonProperty("Tier")] public int Tier;                             // Drop 아이템 티어
        [JsonProperty("DropRate")] public int NothingRate;                  // 아무것도 안떨어질 확률
        [JsonProperty("CurrencyRate")] public int CurrencyRate;             // Drop 화폐 확률
        [JsonProperty("CurrencyId")] public int CurrencyId;                 // Drop 화폐 테이블 Id
        [JsonProperty("CurrencyPoolMode")] public int CurrencyPoolMode;     // 0=Explicit, 1=Pool
        [JsonProperty("EquipmentRate")] public int EquipmentRate;           // Drop 장비 확률
        [JsonProperty("EquipmentId")] public int EquipmentId;               // Drop 장비 테이블 Id
        [JsonProperty("EquipmentPoolMode")] public int EquipmentPoolMode;   // 0=Explicit, 1=Pool

        // 스킬북 드랍 (SKILLBOOK_DESIGN.md §10) — Pool 모드 전용. SkillTable.Tier == DropTable.Tier 매칭 풀에서 랜덤 픽
        [JsonProperty("SkillBookRate")] public int SkillBookRate;           // Drop 스킬북 가중치. 0=드랍 없음

        // 스킬 페이지 드랍 (SKILL_RUNE_DESIGN.md §8.1) — Pool 모드 전용. PageCost 범위로 Tier 매칭 후 랜덤 픽
        [JsonProperty("SkillPageRate")] public int SkillPageRate;           // Drop 스킬 페이지 가중치. 0=드랍 없음
    }

    [Serializable]
    public class DropCurrencyTable : TableBase
    {
        [JsonProperty("Tier")] public int Tier;                 // Drop 아이템 티어

        [JsonProperty("DropList")] public List<DropInfo>? DropList;

    }

    [Serializable]
    public class DropEquipmentTable : TableBase
    {
        [JsonProperty("Tier")] public int Tier;                 // Drop 아이템 티어
        [JsonProperty("DropList")] public List<DropInfo>? DropList;

    }

    [Serializable]
    public class SkillTable : TableBase
    {
        [JsonProperty("Name")] public string Name = string.Empty;               // 이름
        [JsonProperty("Desctiption")] public string Desctiption = string.Empty; // 설명
        // 스킬북 시스템 (SKILLBOOK_DESIGN.md §3.3) — ItemTable.Tier와 같은 등급 체계 공유
        [JsonProperty("Tier")] public int Tier;                                   // 이 스킬이 들어갈 스킬북 등급(=ItemTable.Tier). 1=Common, 2=Rare, 3=Epic. 0이면 책 드랍/상점 풀에서 제외
        [JsonProperty("Tags")] public GE.SkillTag Tags;                           // 스킬 태그 (비트 플래그)
        [JsonProperty("SkillType")] public GE.SkillType SkillType;              // 스킬 타입
        [JsonProperty("SubType")] public GE.SkillSubType SubType;               // 스킬 서브 타입
        [JsonProperty("SkillRangeMin")] public float SkillRangeMin;             // 스킬 최소 사정 거리
        [JsonProperty("SkillRangeMax")] public float SkillRangeMax;             // 스킬 최대 사정 거리
        [JsonProperty("Cooltime")] public float Cooltime;                       // 쿨 타임
        [JsonProperty("Mana")] public int Mana;                                 // 마나 소모량
        [JsonProperty("StartTime")] public float StartTime;                     // Start 상태 지속 시간 (초) - 대부분 0
        [JsonProperty("ProcessTime")] public float ProcessTime;                 // Process 상태 지속 시간 (초) - 애니메이션 재생 시간
        [JsonProperty("EndTime")] public float EndTime;                         // End 상태 지속 시간 (초) - 대부분 0
        [JsonProperty("DamageTime")] public float DamageTime;                   // Process 내부 히트 오프셋 비율 (0~1)
        [JsonProperty("HitCount")] public int HitCount;                         // 히트 횟수 (1=단발, 2+=멀티히트)
        [JsonProperty("HitInterval")] public float HitInterval;                 // 멀티히트 간격 (초)
        [JsonProperty("BaseCriRate")] public int BaseCriRate;                   // Spell 스킬 베이스 치명타 확률 (%, Attack 스킬은 무기에서 가져오므로 무시)
        [JsonProperty("DamageType")] public GE.DamageType DamageType;           // 데미지 속성
        [JsonProperty("DamageMin")] public int DamageMin;                       // 데미지 최소값
        [JsonProperty("DamageMax")] public int DamageMax;                       // 데미지 최대값
        [JsonProperty("SkillTargetType")] public GE.SkillTargetType SkillTargetType; // 스킬 타겟 타입
        [JsonProperty("SkillTargetRange1")] public float SkillTargetRange1;     // 스킬 타겟 타입 범위 1
        [JsonProperty("SkillTargetRange2")] public float SkillTargetRange2;     // 스킬 타겟 타입 범위 2
        [JsonProperty("AnimationName")] public string AnimationName = string.Empty;     // 애니메이션 이름
        [JsonProperty("StartEffectName")] public string StartEffectName = string.Empty; // 시작 이펙트
        [JsonProperty("ActivateName")] public string ActivateName = string.Empty;       // 활성 이펙트
        [JsonProperty("HitEffect")] public string HitEffect = string.Empty;             // 히트 이펙트
        [JsonProperty("ProjectileId")] public int ProjectileId;                          // 발사체 테이블 ID (0이면 즉발)
        [JsonProperty("AreaEffectId")] public int AreaEffectId;                          // 장판 테이블 ID (0이면 장판 없음)
        [JsonProperty("BaseProjectileCount")] public int BaseProjectileCount = 1;        // 기본 발사 개수. 최종 = BaseProjectileCount + Stat.ProjectileCountAdd
        [JsonProperty("ArcHeight")] public float ArcHeight;                              // 포물선 최대 높이 (점프, Arc 투사체 등에 사용. 0이면 지면 유지)
        [JsonProperty("BaseDamageMul")] public int BaseDamageMul = 100;                  // 스킬 베이스 데미지 배율 (100=1.0x). 플랫 데미지(베이스+Added) 합산 후 스킬 배율/치명타보다 먼저 적용
        [JsonProperty("BaseAttackSpeedMul")] public int BaseAttackSpeedMul = 100;        // 스킬 베이스 공속 배율 (100=1.0x). 무기 공속에 곱한 뒤 FinalAttackSpeed% 적용

        // Phase 1: SkillEffect 합성 시스템 - 이 스킬에 부착된 효과 ID 목록 (SkillEffectTable 참조)
        [JsonProperty("SkillEffectIds")] public List<int>? SkillEffectIds = null;
        // Phase 2: ExecutionType 컬럼화 - Component.SkillExecutionType과 매칭. 시트는 문자열(Single/MultiHit/Channeling/Toggle/Charge)
        [JsonProperty("ExecutionType")] public SkillExecutionType ExecutionType = SkillExecutionType.MultiHit;  // 기본 MultiHit (HitCount=1이면 사실상 Single)
        [JsonProperty("ChannelingInterval")] public float ChannelingInterval;            // Channeling: 효과 적용 간격(초)
        [JsonProperty("MaxChargeTime")] public float MaxChargeTime;                      // Charge: 최대 차징 시간(초)
        [JsonProperty("MinChargeRatio")] public float MinChargeRatio;                    // Charge: 최소 차징 비율(0~1)
    }

    /// <summary>
    /// Phase 1: 스킬에 합성 가능한 효과 정의. SkillTable.SkillEffectIds가 이 테이블을 참조.
    /// 한 효과 = (Trigger 시점, EffectType, 파라미터 3개, 발동 확률).
    /// </summary>
    [Serializable]
    public class SkillEffectTable : TableBase
    {
        [JsonProperty("Name")] public string Name = string.Empty;
        [JsonProperty("Description")] public string Description = string.Empty;
        [JsonProperty("EffectType")] public GE.Stat EffectType;              // 효과/Stat 종류. 처리 경로는 Kind가 결정
        [JsonProperty("Kind")] public GE.SkillEffectKind Kind = GE.SkillEffectKind.StatBonus;  // 처리 경로: StatBonus=Helper가 컴포넌트로 흡수, EffectAction=Executor가 트리거 시점에 실행
        [JsonProperty("Trigger")] public GE.SkillTrigger Trigger;             // 발동 시점 (OnHit 등)
        [JsonProperty("Param1")] public float Param1;                        // 효과별 파라미터 1
        [JsonProperty("Param2")] public float Param2;                        // 효과별 파라미터 2
        [JsonProperty("Param3")] public float Param3;                        // 효과별 파라미터 3
        [JsonProperty("Probability")] public int Probability = 100;          // 발동 확률(%) 100=항상
        [JsonProperty("PageCost")] public int PageCost;                      // 스킬 페이지 장착 비용. 0이면 페이지 장착 불가/미사용
        [JsonProperty("Condition")] public GE.PageCondition Condition;        // 후속 페이즈용 발동 조건
        [JsonProperty("ConditionParam")] public float ConditionParam;         // 조건 파라미터
    }

    /// <summary>
    /// 스킬북 등급별 룰. 현재는 페이지 시스템(용량/슬롯) 룰을 보관하지만 향후 다른 등급별 책 룰도 여기에 통합 가능.
    /// Id는 ItemTable.Tier와 매칭한다. (1=Common, 2=Rare, 3=Epic)
    /// </summary>
    [Serializable]
    public class SkillBookTable : TableBase
    {
        [JsonProperty("PageCapacity")] public int PageCapacity;
        [JsonProperty("PageSlots")] public int PageSlots;
    }

    [Serializable]
    public class ProjectileTable : TableBase
    {
        [JsonProperty("Name")] public string Name = string.Empty;           // 이름
        [JsonProperty("Speed")] public float Speed;                         // 이동 속도
        [JsonProperty("LifeTime")] public float LifeTime;                   // 최대 수명 (초)
        [JsonProperty("HitRadius")] public float HitRadius;                 // 충돌 반경
        [JsonProperty("IsPiercing")] public bool IsPiercing;                // 관통 여부
        [JsonProperty("PrefabKey")] public string PrefabKey = string.Empty; // Addressable 프리팹 키
    }

    /// <summary>
    /// 장판(지속 영역 효과) 정의 테이블. SkillTable.AreaEffectId가 이 테이블을 참조.
    /// 위치 지정 즉발(SkillTargetType=Position) 또는 SkillEffect.SpawnAreaEffect 트리거에서 스폰.
    /// </summary>
    [Serializable]
    public class AreaEffectTable : TableBase
    {
        [JsonProperty("Name")] public string Name = string.Empty;                    // 이름
        [JsonProperty("Description")] public string Description = string.Empty;      // 설명
        [JsonProperty("DamageType")] public GE.DamageType DamageType;                // 데미지 속성 (Physics/Fire/Ice/Lightning/Poison)
        [JsonProperty("Damage")] public int Damage;                                  // 틱당 데미지 (0이면 데미지 없음, 버프만 부여)
        [JsonProperty("Radius")] public float Radius;                                // 효과 반경
        [JsonProperty("Duration")] public float Duration;                            // 지속 시간 (초)
        [JsonProperty("TickInterval")] public float TickInterval;                    // 틱 간격 (초)
        [JsonProperty("OnTickBuffId")] public int OnTickBuffId;                      // 매 틱 진입자에게 부여할 BuffTable Id (0이면 없음)
        [JsonProperty("OnEnterBuffId")] public int OnEnterBuffId;                    // 진입 시 1회 부여할 BuffTable Id (0이면 없음)
        [JsonProperty("TargetFaction")] public Faction TargetFaction;                // 타격 대상 진영 (Neutral=caster의 적, 그 외=명시 진영)
        [JsonProperty("TickEffectName")] public string TickEffectName = string.Empty; // 매 틱 재생할 이펙트 키
        [JsonProperty("PrefabKey")] public string PrefabKey = string.Empty;          // Addressable 프리팹 키 (장판 본체 시각)
    }

    [Serializable]
    public class VillageTable : TableBase
    {
        [JsonProperty("Name")] public string Name = string.Empty;                 // 마을 이름
        [JsonProperty("DefaultNpcList")] public string DefaultNpcList = string.Empty; // 스폰할 NpcTableId 목록 (CSV, 예: "1,2,3")
        [JsonProperty("RespawnCooldown")] public float RespawnCooldown;           // 전멸 후 재스폰 대기 (게임 시간, 시간 단위)
        [JsonProperty("SpawnRadius")] public float SpawnRadius = 3f;              // 마을 중심 기준 스폰 반경

        [JsonIgnore] public List<int> DefaultNpcIds = new();

        public override void LoadLate()
        {
            DefaultNpcIds.Clear();
            if (string.IsNullOrEmpty(DefaultNpcList))
                return;

            string[] tokens = DefaultNpcList.Split(',');
            for (int i = 0; i < tokens.Length; i++)
            {
                string trimmed = tokens[i].Trim();
                if (string.IsNullOrEmpty(trimmed))
                    continue;

                if (int.TryParse(trimmed, out int id))
                    DefaultNpcIds.Add(id);
            }
        }
    }

    /// <summary>
    /// 마을 단계별 파라미터 테이블. Id = (int)VillageStage (Settlement=0 ~ City=4) 5행.
    /// 단계로 키잉되는 모든 수치(경계 반경, 이민 주기/확률, 고용 기본가, 승격 임계값)의 정본.
    /// 승격 임계값은 "이 단계로 *진입*하기 위한 조건"으로 표현. Settlement(시작)/City(미구현)는 PromoMinPopulation = -1.
    /// </summary>
    [Serializable]
    public class VillageStageTable : TableBase
    {
        [JsonProperty("Name")] public string Name = string.Empty;                       // 표시용 이름

        // 단계 파라미터
        [JsonProperty("BoundsRadius")] public int BoundsRadius;                         // 마을 경계 반경 (타일)
        [JsonProperty("ImmigrationCheckHours")] public float ImmigrationCheckHours;     // 방문자 도착 체크 주기
        [JsonProperty("ImmigrationArriveChance")] public float ImmigrationArriveChance; // 도착 확률 (0~1)
        [JsonProperty("HireBaseCost")] public int HireBaseCost;                         // NPC 고용 기본가 (Gold)

        // 승격 게이트 (이 단계로 진입하기 위한 조건)
        [JsonProperty("PromoMinPopulation")] public int PromoMinPopulation;             // -1 = 진입 불가 (시작점/미구현)
        [JsonProperty("PromoMinHousing")] public int PromoMinHousing;
        [JsonProperty("PromoMinFood")] public int PromoMinFood;
        [JsonProperty("PromoMinAgeHours")] public float PromoMinAgeHours;
        [JsonProperty("PromoRequiredSet")] public int PromoRequiredSet = -1;            // (int)ObjectSetType, -1 = 없음
        [JsonProperty("PromoRequiredCivic")] public int PromoRequiredCivic;             // 0 = 없음, ≥1 = CountByService(Civic) >= N
        [JsonProperty("PromoRequiredShop")] public int PromoRequiredShop;               // 0 = 없음, ≥1 = CountByService(Shop) >= N

        // 도시 계획 파라미터 (System_VillageBuildQueue)
        [JsonProperty("RoadReserveRadius")] public int RoadReserveRadius;               // 큰길 예약 반경 (0=비활성)
        [JsonProperty("RoadReserveHalfWidth")] public int RoadReserveHalfWidth;         // 큰길 폭의 절반 (1=±1로 폭 3타일)
        [JsonProperty("PlazaRadius")] public int PlazaRadius;                           // 마을 중심 광장 반경 (0=없음, 1=3×3, 2=5×5)
    }

    [Serializable]
    public class AiTable : TableBase
    {
        [JsonProperty("Name")] public string Name = string.Empty;   // 이름
        [JsonProperty("AiType")] public GE.AiType AiType;           // Ai 타입
        [JsonProperty("BehaviorType")] public AIBehaviorType BehaviorType; // 행동 타입 (Melee/Ranged 등)
        [JsonProperty("DetectionRange")] public float DetectionRange; // 감지 범위
        [JsonProperty("SkillId1")] public int SkillId1;             // 스킬 1
        [JsonProperty("SkillWeight1")] public int SkillWeight1;     // 스킬 1 선택 가중치 (0이면 선택 안함)
        [JsonProperty("SkillId2")] public int SkillId2;             // 스킬 2
        [JsonProperty("SkillWeight2")] public int SkillWeight2;     // 스킬 2 선택 가중치
        [JsonProperty("SkillId3")] public int SkillId3;             // 스킬 3
        [JsonProperty("SkillWeight3")] public int SkillWeight3;     // 스킬 3 선택 가중치
    }

    [Serializable]
    public class BuffTable : TableBase
    {
        [JsonProperty("Name")] public string Name = string.Empty;               // 버프 이름
        [JsonProperty("Description")] public string Description = string.Empty; // 버프 설명
        [JsonProperty("BuffType")] public GE.BuffType BuffType;                 // 버프 타입 (버프/디버프)
        [JsonProperty("Duration")] public float Duration;                       // 지속 시간 (초)
        [JsonProperty("TickInterval")] public float TickInterval;               // 틱 간격 (DoT/HoT용, 0이면 즉시 적용)
        [JsonProperty("MaxStack")] public int MaxStack;                         // 최대 스택 수
        [JsonProperty("IsDispellable")] public bool IsDispellable;              // 해제 가능 여부
        [JsonProperty("BuffEffectId")] public GE.BuffEffectType EffectType;     // 버프 이팩트 타입
        [JsonProperty("EffectValue")] public int EffectValue;                   // 버프 이팩트 값
        
        //[JsonIgnore] public BuffEffectTable? BuffEffectTable;

        // public override void LoadLate()
        // {
        //     if (AR.s.Data != null)
        //     {
        //         BuffEffectTable? buffEffectTable = AR.s.Data.GetBuffEffect(BuffEffectId);
        //         if (buffEffectTable != null)
        //         {
        //             BuffEffectTable = buffEffectTable;
        //         }
        //         else
        //         {
        //             BuffEffectTable = null;
        //         }
        //     }
        //}
    }

    [Serializable]
    public class BuffEffectTable : TableBase
    {
        [JsonProperty("Name")] public string Name = string.Empty;               // 버프 이름
        [JsonProperty("BuffEffectList")] public List<BuffEffect>? BuffEffectList = null;          // 스탯 리스트
    }

    // Stat 구조체 제거됨 → ModInstance로 대체

    [Serializable]
    public class DropInfo
    {
        [JsonProperty("Type")] public int Id;
        [JsonProperty("Value")] public int Rate;
    }

    [Serializable]
    public class BuffEffect
    {
        [JsonProperty("Type")] public GE.BuffEffectType Type;
        [JsonProperty("Value")] public ushort Value;
    }

    /// <summary>
    /// Mod 정의 테이블 - 모든 장비 옵션의 기본 정보
    /// </summary>
    [Serializable]
    public class ModTable : TableBase
    {
        [JsonProperty("Name")] public string Name = string.Empty;
        [JsonProperty("EffectType")] public GE.ModEffectType EffectType;
        [JsonProperty("ApplyType")] public GE.ModApplyType ApplyType;
        [JsonProperty("Slot")] public GE.ModSlot Slot;
        [JsonProperty("Group")] public string Group = string.Empty;         // 같은 그룹 중복 불가
        [JsonProperty("Element")] public GE.DamageType Element;             // 속성 (데미지 계열)
        [JsonProperty("Tags")] public GE.SkillTag Tags;                     // 적용 조건 (Attack, Spell 등)
        [JsonProperty("TargetStat")] public GE.Stat TargetStat;             // FlatStat/IncreasedStat 대상
        [JsonProperty("AllowedEquipTypes")] public GE.EquipmentTypeMask AllowedEquipTypes;  // 이 mod가 붙을 수 있는 장비 타입. Implicit은 빈 칸 허용
    }

    /// <summary>
    /// Mod 티어 테이블 - Mod별 값 범위 정의
    /// </summary>
    [Serializable]
    public class ModTierTable : TableBase
    {
        [JsonProperty("ModId")] public int ModId;
        [JsonProperty("Tier")] public int Tier;
        [JsonProperty("Min1")] public int Min1;               // Value1 최소
        [JsonProperty("Max1")] public int Max1;               // Value1 최대
        [JsonProperty("Min2")] public int Min2;               // Value2 최소 (데미지 Max 등)
        [JsonProperty("Max2")] public int Max2;               // Value2 최대
        [JsonProperty("RequiredLevel")] public int RequiredLevel;
        [JsonProperty("Weight")] public int Weight;           // 롤링 가중치

        [JsonIgnore] public ModTable? Mod;

        public override void LoadLate()
        {
            Mod = AR.s.Data?.GetMod(ModId);
        }
    }

    /// <summary>
    /// 아이템별 기본 내장(Implicit) Mod 매핑
    /// </summary>
    [Serializable]
    public class ItemImplicitTable : TableBase
    {
        [JsonProperty("ItemId")] public int ItemId;
        [JsonProperty("ModId")] public int ModId;
        [JsonProperty("Tier")] public int Tier;

        [JsonIgnore] public ModTable? Mod;
        [JsonIgnore] public ModTierTable? TierData;

        public override void LoadLate()
        {
            Mod = AR.s.Data?.GetMod(ModId);
            TierData = AR.s.Data?.GetModTier(ModId, Tier);
        }
    }

    /// <summary>
    /// 아이템에 실제로 부여된 Mod 인스턴스 (저장 대상)
    /// </summary>
    [Serializable]
    public class ModInstance
    {
        [JsonProperty("ModTableId")] public int ModTableId;
        [JsonProperty("Slot")] public GE.ModSlot Slot;
        [JsonProperty("Tier")] public int Tier;
        [JsonProperty("Value1")] public ushort Value1;
        [JsonProperty("Value2")] public ushort Value2;

        [JsonIgnore] public ModTable? Table;

        public void OnLoadCompleted()
        {
            Table = AR.s.Data?.GetMod(ModTableId);
        }
    }

    [Serializable]
    public class AnimationTable : TableBase
    {
        [JsonProperty("Name")] public string Name = string.Empty;
        [JsonProperty("SpriteLibraryPath")] public string SpriteLibraryPath = string.Empty;
        [JsonProperty("IdleFrame")] public float IdleFrame;        // Idle 프레임당 시간 (초)
        [JsonProperty("MoveFrame")] public float MoveFrame;        // Move 프레임당 시간 (초)
        [JsonProperty("AttackFrame")] public float AttackFrame;    // Attack 프레임당 시간 (초)
        [JsonProperty("DeadFrame")] public float DeadFrame;        // Dead 프레임당 시간 (초)

        /// <summary>
        /// AnimCategory에 해당하는 기본 FrameDuration 반환
        /// </summary>
        public float GetFrameDuration(GlobalEnum.AnimCategory category)
        {
            switch (category)
            {
                case GlobalEnum.AnimCategory.Idle: return IdleFrame;
                case GlobalEnum.AnimCategory.Move: return MoveFrame;
                case GlobalEnum.AnimCategory.Attack: return AttackFrame;
                case GlobalEnum.AnimCategory.Dead: return DeadFrame;
                default: return 0.1f;
            }
        }
    }

    /// <summary>
    /// 청크 Zone(시드 청크 (0,0) 기준 Chebyshev 거리 + 1)별 몬스터 스폰 파라미터.
    /// 시트엔 일부 Zone만 입력. 조회 시 N 이하 최대 정의 Zone 행 사용 (Cap/계단식).
    /// Id 컬럼이 곧 Zone 번호. Zone 1 행은 반드시 존재해야 함.
    /// </summary>
    [Serializable]
    public class ZoneTable : TableBase
    {
        [JsonProperty("MainGroupCountMin")] public int MainGroupCountMin;
        [JsonProperty("MainGroupCountMax")] public int MainGroupCountMax;
        [JsonProperty("MainGroupSizeMin")]  public int MainGroupSizeMin;
        [JsonProperty("MainGroupSizeMax")]  public int MainGroupSizeMax;
        [JsonProperty("SubGroupCountMin")]  public int SubGroupCountMin;
        [JsonProperty("SubGroupCountMax")]  public int SubGroupCountMax;
        [JsonProperty("SubGroupSizeMin")]   public int SubGroupSizeMin;
        [JsonProperty("SubGroupSizeMax")]   public int SubGroupSizeMax;
        [JsonProperty("GroupRadius")]           public float GroupRadius;
        [JsonProperty("InterGroupMinDistance")] public float InterGroupMinDistance;
    }

}
