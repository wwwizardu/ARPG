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
        [JsonProperty("MoveSpeed")] public float MoveSpeed;
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
        // 몬스터 종 드롭 보너스 (ITEM_DROP_BALANCE_DESIGN §8.3 ③ 묶음, %). 옛 DropRateBonus(꽝 칸 줄이기) 자리
        [JsonProperty("DropQuantityBonus")] public int DropQuantityBonus;
        [JsonProperty("DropRarityBonus")] public int DropRarityBonus;
        [JsonProperty("Level")] public int Level;
        [JsonProperty("Archetype")] public GE.MonsterArchetype Archetype = GE.MonsterArchetype.Normal;

        // 트랙 A: 이 몬스터가 서식하는 바이옴 (PLAYER_RECOGNITION_DESIGN.md). monster→biome 역방향 조회 근거.
        // 바이옴 게이트(IsLocalThreat, 후순위)와 BiomeMonsterMapping 데이터화의 근거 데이터.
        // 기본값 Any = 공용: 스폰 시 모든 biome 풀에 포함(BiomeMonsterMapping), 역방향 위협 게이트에선 범용 위협.
        // 특정 biome 값이면 그 biome 전용 스폰 + 국지 위협 가중.
        [JsonProperty("Biome")] public GE.BiomeType Biome = GE.BiomeType.Any;

        // 복수 서식지는 첫 바이옴을 Biome에 유지하고 나머지를 별도 스폰 풀에도 등록한다.
        [JsonProperty("AdditionalBiomes", NullValueHandling = NullValueHandling.Ignore)]
        public GE.BiomeType[]? AdditionalBiomes;

        // 적중 판정 종 배율 (PLAYER_LEVEL_CHARM_DESIGN §4.4·§5.3). 정확도·회피 = 레벨 곡선 × 이 배율. 빈 칸은 1.0
        [JsonProperty("AccuracyMul")] public float AccuracyMul = 1f;
        [JsonProperty("EvasionMul")] public float EvasionMul = 1f;

        [JsonIgnore] public AiTable? AiTable;


        public override void LoadLate()
        {
            base.LoadLate();

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
    /// 몬스터 등급별 배율·보상 정의 (MONSTER_RARITY_DESIGN.md §3). EntityFactory.CreateMonster가
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
        [JsonProperty("ResistAdd")] public int ResistAdd;                 // 원소 저항 4종에 더하는 값 (%p). 상한은 몬스터의 최대 저항
        [JsonProperty("DropRolls")] public int DropRolls = 1;             // 사망 시 드랍 판정 횟수
        [JsonProperty("DropRarityBonus")] public int DropRarityBonus;     // 아이템 희귀도 (%, 몬스터 묶음). MonsterTable.DropRarityBonus에 더한다 (ITEM_DROP_BALANCE_DESIGN §8.3)
        [JsonProperty("ModTier11ChanceBp")] public int ModTier11ChanceBp; // 드랍 장비 1개당 옵션 T11 특수 판정 확률 (만분율, 10000=100%). 아이템 레벨 90 이상만
        [JsonProperty("ModTier12ChanceBp")] public int ModTier12ChanceBp; // 드랍 장비 1개당 옵션 T12 특수 판정 확률 (만분율). 아이템 레벨 90 이상만
        [JsonProperty("AffixBaseCount")] public int AffixBaseCount;       // 접사 기본 개수 (MONSTER_AFFIX_DESIGN.md §3)
        [JsonProperty("AffixExtraChance")] public int AffixExtraChance;   // 접사 추가 판정 확률 (%). 성공할 때마다 1개 더, 실패하거나 상한이면 멈춤
        [JsonProperty("DropRollsPerAffix")] public int DropRollsPerAffix; // 접사 1개당 더하는 드랍 판정 횟수 (§8)
        [JsonProperty("RarityPerAffix")] public int RarityPerAffix;       // 접사 1개당 더하는 아이템 희귀도 (%, ITEM_DROP_BALANCE_DESIGN §8.4)
        [JsonProperty("ExpMul")] public float ExpMul = 1f;                // 처치 경험치 등급 배율. 잡는 시간에 비례하도록 HpMul과 같은 값 (PLAYER_LEVEL_CHARM_DESIGN §3.2)
    }

    /// <summary>
    /// 플레이어 레벨별 필요 경험치와 몬스터 레벨별 기본 경험치 (PLAYER_LEVEL_CHARM_DESIGN.md §3.4).
    /// Id = 레벨(1~100). 시트에는 계산된 값만 두고 식은 설계 문서가 정본이다.
    /// </summary>
    [Serializable]
    public class ExpTable : TableBase
    {
        [JsonProperty("RequiredExp")] public int RequiredExp;   // 이 레벨 → 다음 레벨 필요 경험치 R(L). 최고 레벨은 0
        [JsonProperty("MonsterExp")] public int MonsterExp;     // 이 레벨 몬스터의 기본 경험치 E(L)
    }

    /// <summary>
    /// 몬스터 접사 정의 (MONSTER_AFFIX_DESIGN.md §5). 무리 생성 때 MonsterAffixRoller가 뽑고 EntityFactory.CreateMonster가 적용한다.
    /// Kind=Stat이면 Stat1~3 칸을 자기 스탯 보정으로, Kind=Aura면 반경 안 같은 진영에게 AuraBuffId 버프를 건다.
    /// </summary>
    [Serializable]
    public class MonsterAffixTable : TableBase
    {
        [JsonProperty("Name")] public string Name = string.Empty;                    // 이름표 표시 이름
        [JsonProperty("Kind")] public GE.MonsterAffixKind Kind;
        [JsonProperty("Group")] public string Group = string.Empty;                  // 같은 그룹은 한 몬스터에 중복 불가 (티어 구분)
        [JsonProperty("Weight")] public int Weight;                                  // 뽑기 가중치. 0이면 뽑히지 않음
        [JsonProperty("MinLevel")] public int MinLevel;                              // 몬스터 레벨이 이 값 이상일 때만 후보
        [JsonProperty("AllowedArchetypes")] public GE.MonsterArchetypeMask AllowedArchetypes;
        [JsonProperty("HasRequiredDamageType")] public bool HasRequiredDamageType;   // false면 피해 종류 제한 없음
        [JsonProperty("RequiredDamageType")] public GE.DamageType RequiredDamageType; // 이 피해 종류 스킬이 있는 몬스터만 후보
        [JsonProperty("Stat1")] public GE.Stat Stat1;
        [JsonProperty("ModType1")] public StatModifierType ModType1;
        [JsonProperty("Value1")] public int Value1;                                  // 0이면 빈 칸
        [JsonProperty("Stat2")] public GE.Stat Stat2;
        [JsonProperty("ModType2")] public StatModifierType ModType2;
        [JsonProperty("Value2")] public int Value2;
        [JsonProperty("Stat3")] public GE.Stat Stat3;
        [JsonProperty("ModType3")] public StatModifierType ModType3;
        [JsonProperty("Value3")] public int Value3;
        [JsonProperty("AuraBuffId")] public int AuraBuffId;                          // Kind=Aura: 반경 안에 거는 BuffTable Id
        [JsonProperty("AuraRadius")] public float AuraRadius;                        // Kind=Aura: 반경 (칸)
    }

    /// <summary>
    /// 네임드 위협 템플릿 (NAMED_THREAT_DESIGN §11.1). 기존 몬스터 종에 Named 등급·고정 접사·칭호를 얹는다.
    /// 전용 몬스터 행을 만들지 않는다 — 모든 MonsterTable 행이 필드 종 풀에 들어가기 때문이다 (§14).
    /// </summary>
    [Serializable]
    public class NamedThreatTable : TableBase
    {
        [JsonProperty("Name")] public string Name = string.Empty;                // 관리용 이름
        [JsonProperty("Kind")] public GE.NamedThreatKind Kind;                    // 1차는 Predator만
        [JsonProperty("MonsterId")] public int MonsterId;                         // 기본 종 (MonsterTable)
        [JsonProperty("AiTableId")] public int AiTableId;                         // 전용 AI 행 (감지·순찰 반경)
        [JsonProperty("MinZone")] public int MinZone;
        [JsonProperty("MaxZone")] public int MaxZone;
        [JsonProperty("Biome")] public GE.BiomeType Biome;                        // 영역 중심 청크 바이옴 조건 (Any = 어디든)
        [JsonProperty("Weight")] public int Weight;
        [JsonProperty("Title1")] public string Title1 = string.Empty;
        [JsonProperty("Title2")] public string Title2 = string.Empty;
        [JsonProperty("Title3")] public string Title3 = string.Empty;
        [JsonProperty("Affix1")] public int Affix1;                               // 1단계 고정 접사 (스탯형만)
        [JsonProperty("Affix2")] public int Affix2;
        [JsonProperty("Stage2Affix")] public int Stage2Affix;
        [JsonProperty("Stage3Affix")] public int Stage3Affix;
        [JsonProperty("TrophyItemId")] public int TrophyItemId;                   // 확정 전리품 (ItemTable)
        [JsonProperty("NarrativeContentId")] public int NarrativeContentId;       // 부탁·흔적·보고 문구 (NarrativeContent)
        [JsonProperty("Enabled")] public bool Enabled;

        /// <summary>칭호 후보 (비어 있지 않은 것만, 순서 유지). index는 0~2.</summary>
        public string GetTitle(int index) => index switch { 0 => Title1, 1 => Title2, 2 => Title3, _ => string.Empty };
    }

    /// <summary>
    /// NPC 단기 목표 정의 (NPC_OBJECTIVE_DESIGN §4.1, D147). 목표 종류 하나 = 행 하나. 동작은 enum 이름으로 고르고 수치·문구는 행에 둔다.
    /// 문구의 {target}·{item}·{gold}·{need}·{remain}·{subject}는 표시할 때 사실로 채운다 (LLM은 말투만).
    /// </summary>
    [Serializable]
    public class NpcObjectiveTable : TableBase
    {
        [JsonProperty("Name")] public string Name = string.Empty;                                  // 표시 이름
        [JsonProperty("Category")] public ARPG.Npc.NpcObjectiveCategory Category;                   // 분류 (강제 > 극적 > 일상)
        [JsonProperty("Priority")] public int Priority;                                             // 같은 분류 안 순서 (클수록 우선)
        [JsonProperty("Trigger")] public ARPG.Npc.NpcObjectiveTrigger Trigger;
        [JsonProperty("Target")] public ARPG.Npc.NpcObjectiveTargetKind Target;
        [JsonProperty("Complete")] public ARPG.Npc.NpcObjectiveComplete Complete;
        [JsonProperty("SameKindRefresh")] public bool SameKindRefresh;                              // 같은 종류 새 계기면 더 나은 대상으로 갱신
        [JsonProperty("ReactChance")] public float ReactChance;                                     // 생성 확률 기본값 (1 = 조건만 맞으면, 굴리지 않음)
        [JsonProperty("ReactProfile")] public ARPG.Npc.NpcObjectiveReactProfile ReactProfile;       // 성격 배율 규칙
        [JsonProperty("PrepHours")] public float PrepHours;                                         // 준비 단계 길이 (0 = 바로 실행)
        [JsonProperty("DeadlineHours")] public float DeadlineHours;                                 // 기한 (기준 시각부터)
        [JsonProperty("TimeFrom")] public ARPG.Npc.NpcObjectiveTimeFrom TimeFrom;                   // 준비·기한의 기준
        [JsonProperty("MaxAttempts")] public int MaxAttempts;                                       // 시도 상한 (0 = 없음)
        [JsonProperty("PrepRest")] public float PrepRest;                                           // 준비 단계 활동 배율
        [JsonProperty("PrepHunt")] public float PrepHunt;
        [JsonProperty("PrepTrade")] public float PrepTrade;
        [JsonProperty("PrepDelve")] public float PrepDelve;
        [JsonProperty("ActRest")] public float ActRest;                                             // 실행 단계 활동 배율
        [JsonProperty("ActHunt")] public float ActHunt;
        [JsonProperty("ActTrade")] public float ActTrade;
        [JsonProperty("ActDelve")] public float ActDelve;
        [JsonProperty("AlwaysRecruit")] public bool AlwaysRecruit;                                  // 목표 원정 때 모집 시도 확률을 건너뜀
        [JsonProperty("SkipOtherParties")] public bool SkipOtherParties;                            // 실행 단계에 다른 리더의 모집 후보에서 빠짐 (같은 대상 리더는 예외)
        [JsonProperty("RequestAffinity")] public int RequestAffinity;                               // 플레이어에게 부탁하는 호감 문턱 (0 = 부탁 없음)
        [JsonProperty("RecordDeed")] public bool RecordDeed;                                        // 결말 Deed를 남김
        [JsonProperty("OutcomeTopicHours")] public float OutcomeTopicHours;                         // 결말 화제가 나오는 기간 (0 = 없음)
        [JsonProperty("InspectText")] public string InspectText = string.Empty;                     // 검사 창 한 줄
        [JsonProperty("StatusText")] public string StatusText = string.Empty;                       // 진행 중 화제 (빈칸 = 화제 없음)
        [JsonProperty("RequestText")] public string RequestText = string.Empty;                     // 부탁 대사
        [JsonProperty("FulfilledText")] public string FulfilledText = string.Empty;                 // 이룸 화제
        [JsonProperty("FulfilledByOtherText")] public string FulfilledByOtherText = string.Empty;   // 남이 이뤘을 때 화제
        [JsonProperty("AbandonedText")] public string AbandonedText = string.Empty;                 // 꺾임 화제
    }

    [Serializable]
    public class NpcTable : CreatureTable
    {
        [JsonProperty("JobType")] public GE.JobType JobType;
        // NPC 성별의 정본. 종류마다 Male/Female 중 하나로 고정하며 세이브에는 저장하지 않는다 (범용·Unknown 금지).
        [JsonProperty("Gender")] public ARPG.Npc.NpcGender Gender;
        // NPC 시작 장비 세트 — NpcEquipTable.Id 참조. 0이면 무장 없음.
        [JsonProperty("EquipTableId")] public int EquipTableId;
        [JsonProperty("AiTableId")] public int AiTableId;
        [JsonProperty("DropId")] public int DropId;
        [JsonProperty("DropRateBonus")] public int DropRateBonus;
        [JsonProperty("DropRarityBonus")] public int DropRarityBonus;

        [JsonIgnore] public AiTable? AiTable;

        public override void LoadLate()
        {
            base.LoadLate();

            AiTable? aiTable = AR.s.Data?.GetAiTable(AiTableId);
            if (aiTable != null)
            {
                AiTable = aiTable;
            }
            else
            {
                AiTable = null;
            }

#if UNITY_EDITOR
            if (Gender != ARPG.Npc.NpcGender.Male && Gender != ARPG.Npc.NpcGender.Female)
                UnityEngine.Debug.LogError($"[NpcTable] Id={Id}({Name}): Gender={Gender} — Npc 시트 Gender 열에 Male 또는 Female을 입력하세요.");
#endif
        }
    }

    /// <summary>
    /// NPC 시작 장비 세트. 슬롯별 ItemId와 최초 생성 시 장착 확률(0~100)을 명시한다.
    /// 확률 판정에 성공한 슬롯만 ItemManager.CreateInventoryItemData(itemId)로 인스턴스를 생성한다.
    /// ItemId 0 = 슬롯 비움.
    /// </summary>
    [Serializable]
    public class NpcEquipTable : TableBase
    {
        [JsonProperty("Desc")] public string Desc = string.Empty;
        [JsonProperty("WeaponRight")] public int WeaponRight;
        [JsonProperty("WeaponRightChance")] public int WeaponRightChance;
        [JsonProperty("WeaponLeft")] public int WeaponLeft;
        [JsonProperty("WeaponLeftChance")] public int WeaponLeftChance;
        [JsonProperty("Helmet")] public int Helmet;
        [JsonProperty("HelmetChance")] public int HelmetChance;
        [JsonProperty("Armor")] public int Armor;
        [JsonProperty("ArmorChance")] public int ArmorChance;
        [JsonProperty("Gloves")] public int Gloves;
        [JsonProperty("GlovesChance")] public int GlovesChance;
        [JsonProperty("Boots")] public int Boots;
        [JsonProperty("BootsChance")] public int BootsChance;
        [JsonProperty("Necklace")] public int Necklace;
        [JsonProperty("NecklaceChance")] public int NecklaceChance;
        [JsonProperty("RingRight")] public int RingRight;
        [JsonProperty("RingRightChance")] public int RingRightChance;
        [JsonProperty("RingLeft")] public int RingLeft;
        [JsonProperty("RingLeftChance")] public int RingLeftChance;
        [JsonProperty("Belt")] public int Belt;
        [JsonProperty("BeltChance")] public int BeltChance;
        [JsonProperty("EarringRight")] public int EarringRight;
        [JsonProperty("EarringRightChance")] public int EarringRightChance;
        [JsonProperty("EarringLeft")] public int EarringLeft;
        [JsonProperty("EarringLeftChance")] public int EarringLeftChance;

        public int GetItemId(GE.EquipSlotType slot)
        {
            return slot switch
            {
                GE.EquipSlotType.WeaponRight => WeaponRight,
                GE.EquipSlotType.WeaponLeft => WeaponLeft,
                GE.EquipSlotType.Helmet => Helmet,
                GE.EquipSlotType.Armor => Armor,
                GE.EquipSlotType.Gloves => Gloves,
                GE.EquipSlotType.Boots => Boots,
                GE.EquipSlotType.Necklace => Necklace,
                GE.EquipSlotType.RingRight => RingRight,
                GE.EquipSlotType.RingLeft => RingLeft,
                GE.EquipSlotType.Belt => Belt,
                GE.EquipSlotType.EarringRight => EarringRight,
                GE.EquipSlotType.EarringLeft => EarringLeft,
                _ => 0,
            };
        }

        public int GetEquipChance(GE.EquipSlotType slot)
        {
            return slot switch
            {
                GE.EquipSlotType.WeaponRight => WeaponRightChance,
                GE.EquipSlotType.WeaponLeft => WeaponLeftChance,
                GE.EquipSlotType.Helmet => HelmetChance,
                GE.EquipSlotType.Armor => ArmorChance,
                GE.EquipSlotType.Gloves => GlovesChance,
                GE.EquipSlotType.Boots => BootsChance,
                GE.EquipSlotType.Necklace => NecklaceChance,
                GE.EquipSlotType.RingRight => RingRightChance,
                GE.EquipSlotType.RingLeft => RingLeftChance,
                GE.EquipSlotType.Belt => BeltChance,
                GE.EquipSlotType.EarringRight => EarringRightChance,
                GE.EquipSlotType.EarringLeft => EarringLeftChance,
                _ => 0,
            };
        }
    }

    [Serializable]
    public class ItemTable : TableBase
    {
        [JsonProperty("RestoreHpPercent")] public int RestoreHpPercent;
        [JsonProperty("RestoreMpPercent")] public int RestoreMpPercent;
        [JsonProperty("UseCooldownSeconds")] public float UseCooldownSeconds;
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
        [JsonProperty("SpriteName")] public string SpriteName = string.Empty;

        // Phase D: 상점 거래 컬럼
        [JsonProperty("BasePrice")] public int BasePrice;                       // 매물 기본가 (Gold). 0이면 비매품
        [JsonProperty("SellRatioBp")] public int SellRatioBp;                   // 매각 비율 ×100 (50=0.5, 40=0.4). 0이면 매각 불가
        [JsonProperty("ReturnResourceType")] public int ReturnResourceType;     // 마을 상인에 매각 시 마을 Storage로 환원될 자원 종류 (ItemType enum 값). 0=환원 없음. ex) 나무공예품 매각 → Wood 일부 회수
        [JsonProperty("ReturnRatioBp")] public int ReturnRatioBp;               // 환원량 비율 ×100 (basis points). returnAmount = amount × ReturnRatioBp / 100. ex) 50이면 매각 수량의 50%가 ReturnResourceType 자원으로 마을 창고에 적립. 0=환원 없음

        // 트랙 A: 몬스터 전리품 식별 (PLAYER_RECOGNITION_DESIGN.md). 이 아이템이 어느 몬스터에서 나온 전리품인지(MonsterTable.Id).
        // 0 = 전리품 아님. 위협 등급 등은 소스 몬스터의 Level/Archetype에서 파생. 조회는 MonsterTrophyIdentifier 경유.
        [JsonProperty("SourceMonsterId")] public int SourceMonsterId;
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

        // 건물 의미 데이터 정본화 — VILLAGE_RUNTIME_DESIGN.md §5
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

    /// <summary>
    /// 장비 기본 베이스 스탯 테이블 — 모든 일반 장비의 표준 베이스 스탯 정의.
    /// 와이드 포맷: 한 행 = 한 아이템, Base1~4 슬롯에 (ModId, Value1, Value2) 3쌍으로 베이스 옵션 명시.
    /// **고정값** — 베이스 스탯엔 RNG 없음 (아이템 정체성). 같은 아이템은 항상 같은 베이스 스탯.
    /// Prefix/Postfix 모드가 랜덤 옵션을 담당.
    /// 장비 자격 판별 기준 (이 테이블에 행이 있어야 장비로 인정).
    /// ItemImplicitTable은 유니크/특수 아이템의 추가 옵션 전용으로 의미가 분리됨.
    /// </summary>
    [Serializable]
    public class EquipmentBaseStatTable : TableBase
    {
        [JsonProperty("ItemId")] public int ItemId;

        [JsonProperty("Base1ModId")] public int Base1ModId;
        [JsonProperty("Base1Value1")] public int Base1Value1;
        [JsonProperty("Base1Value2")] public int Base1Value2;

        [JsonProperty("Base2ModId")] public int Base2ModId;
        [JsonProperty("Base2Value1")] public int Base2Value1;
        [JsonProperty("Base2Value2")] public int Base2Value2;

        [JsonProperty("Base3ModId")] public int Base3ModId;
        [JsonProperty("Base3Value1")] public int Base3Value1;
        [JsonProperty("Base3Value2")] public int Base3Value2;

        [JsonProperty("Base4ModId")] public int Base4ModId;
        [JsonProperty("Base4Value1")] public int Base4Value1;
        [JsonProperty("Base4Value2")] public int Base4Value2;

        // LoadLate에서 해석된 ModTable 참조만 보유 (수치는 위 컬럼 직접 사용)
        [JsonIgnore] public ModTable?[] Mods = new ModTable?[4];

        public override void LoadLate()
        {
            Mods[0] = Base1ModId > 0 ? AR.s.Data?.GetMod(Base1ModId) : null;
            Mods[1] = Base2ModId > 0 ? AR.s.Data?.GetMod(Base2ModId) : null;
            Mods[2] = Base3ModId > 0 ? AR.s.Data?.GetMod(Base3ModId) : null;
            Mods[3] = Base4ModId > 0 ? AR.s.Data?.GetMod(Base4ModId) : null;
        }

        /// <summary>슬롯 인덱스(0~3)의 ModId 조회</summary>
        public int GetModId(int slot)
        {
            switch (slot)
            {
                case 0: return Base1ModId;
                case 1: return Base2ModId;
                case 2: return Base3ModId;
                case 3: return Base4ModId;
                default: return 0;
            }
        }

        /// <summary>슬롯 인덱스(0~3)의 (Value1, Value2) 조회</summary>
        public (int value1, int value2) GetValues(int slot)
        {
            switch (slot)
            {
                case 0: return (Base1Value1, Base1Value2);
                case 1: return (Base2Value1, Base2Value2);
                case 2: return (Base3Value1, Base3Value2);
                case 3: return (Base4Value1, Base4Value2);
                default: return (0, 0);
            }
        }
    }

    /// <summary>
    /// 무기 고유 강화 재료의 롤 규칙 스키마.
    /// 실제 재료 데이터와 다운로드 시트는 재료 추가 시 연결한다.
    /// 무기 종류 제한은 두지 않으며 ItemId 하나에 여러 후보 행을 둘 수 있다.
    /// </summary>
    [Serializable]
    public class WeaponLocalEnhancementMaterialTable : TableBase
    {
        [JsonProperty("ItemId")] public int ItemId;
        [JsonProperty("StatType")] public GE.WeaponLocalStatType StatType;
        [JsonProperty("MinValue")] public int MinValue;
        [JsonProperty("MaxValue")] public int MaxValue;
        [JsonProperty("MaxTotal")] public int MaxTotal;
        [JsonProperty("Weight")] public int Weight;
    }

    /// <summary>
    /// 일반 장비 Prefix/Postfix 마법부여 작업별 비용 규칙.
    /// 플레이어가 선택한 랜덤 교체/추가 Operation으로 이 테이블을 조회한다.
    /// </summary>
    [Serializable]
    public class ItemEnchantmentCostTable : TableBase
    {
        [JsonProperty("MaterialItemId")] public int MaterialItemId;
        [JsonProperty("Operation")] public GE.ItemEnchantmentOperation Operation;
        [JsonProperty("MaterialCount")] public int MaterialCount;
        [JsonProperty("GoldCost")] public int GoldCost;
        [JsonProperty("RequiredForgeTier")] public GE.ForgeTier RequiredForgeTier;

        [JsonIgnore] public ItemTable? MaterialItem;
        [JsonIgnore] public string ValidationError = string.Empty;

        [JsonIgnore]
        public bool IsValid => string.IsNullOrEmpty(ValidationError) == true;

        /// <summary>재료 없이 골드만 드는 작업인지 (품질 다시 굴리기, ITEM_DROP_BALANCE_DESIGN D13). 재료 칸은 0·0이다.</summary>
        [JsonIgnore]
        public bool IsGoldOnly => MaterialItemId == 0 && MaterialCount == 0;

        public override void LoadLate()
        {
            MaterialItem = IsGoldOnly == true ? null : AR.s.Data?.GetItem(MaterialItemId);
            bool goldOnlyOperation = Operation == GE.ItemEnchantmentOperation.RerollQuality;
            if (IsGoldOnly != goldOnlyOperation)
            {
                ValidationError = goldOnlyOperation == true
                    ? "RerollQuality must be gold-only (MaterialItemId 0, MaterialCount 0)"
                    : "Only RerollQuality can be a gold-only operation";
            }
            else if (IsGoldOnly == false && MaterialItem == null)
            {
                ValidationError = $"Material ItemTable {MaterialItemId} not found";
            }
            else if (IsGoldOnly == false && MaterialItem!.ItemType != GE.ItemType.Currency)
            {
                ValidationError = $"Material ItemTable {MaterialItemId} is not Currency";
            }
            else if (IsGoldOnly == false && MaterialCount <= 0)
            {
                ValidationError = "MaterialCount must be greater than zero";
            }
            else if (GoldCost < 0)
            {
                ValidationError = "GoldCost cannot be negative";
            }
            else if (RequiredForgeTier < GE.ForgeTier.Basic)
            {
                ValidationError = "RequiredForgeTier must be Basic or higher";
            }
            else
            {
                ValidationError = string.Empty;
            }

            if (IsValid == false)
            {
                UnityEngine.Debug.LogError($"[ItemEnchantmentCostTable] Invalid row Id={Id}: {ValidationError}");
            }
        }
    }

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

        // 스킬북 드랍 — Pool 모드 전용. DropEnabled인 스킬 중 SkillTable.Tier == DropTable.Tier 매칭 풀에서 랜덤 픽
        [JsonProperty("SkillBookRate")] public int SkillBookRate;           // Drop 스킬북 가중치. 0=드랍 없음

        // 스킬 페이지 드랍 (SKILL_RUNE_DESIGN.md §8.1) — Pool 모드 전용. PageCost 범위로 Tier 매칭 후 랜덤 픽
        [JsonProperty("SkillPageRate")] public int SkillPageRate;           // Drop 스킬 페이지 가중치. 0=드랍 없음

        // 참·참 판 드랍 (PLAYER_LEVEL_CHARM_DESIGN §6.6). 아이템 레벨 = 몬스터 레벨
        [JsonProperty("CharmRate")] public int CharmRate;                   // Drop 참 가중치. 0=드랍 없음
        [JsonProperty("CharmBoardRate")] public int CharmBoardRate;         // Drop 참 판 가중치. 0=드랍 없음
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
        [JsonProperty("DropEnabled")] public bool DropEnabled;                    // 랜덤 스킬북 획득 허용 여부. 미입력은 false (몬스터 전용 스킬 등 제외)
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
        [JsonProperty("AimTrackingMode")] public GE.SkillAimTrackingMode AimTrackingMode = GE.SkillAimTrackingMode.Default;
        [JsonProperty("AimTurnSpeedDegPerSec")] public float AimTurnSpeedDegPerSec;
        [JsonProperty("MovementMode")] public GE.SkillMovementMode MovementMode = GE.SkillMovementMode.Default;
        [JsonProperty("MoveSpeedWhileUsingMul")] public float MoveSpeedWhileUsingMul;
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
        // AS열: 참이면 실행(Process) 단계 동안 시전자가 넉백·당기기를 받지 않는다 (SKILL_DESIGN §6.10 적용 규칙 7)
        [JsonProperty("ForcedMoveImmune")] public bool ForcedMoveImmune;

        /// <summary>
        /// 커밋 시점에 직접 타격 판정을 수행하는지. 커밋 스케줄과 예고 도형이 공유하는 단일 정본.
        /// 실제 데미지는 BaseDamageMul × (무기/스탯 Added + DamageMin/Max)이므로 DamageMin/Max가 0이어도
        /// Attack/Spell 스킬은 타격한다 (예: QuickHop은 DamageMin/Max=0, BaseDamageMul=30).
        /// 비전투 스킬은 Attack/Spell 태그를 빼거나 BaseDamageMul=0으로 구분한다.
        /// </summary>
        [JsonIgnore]
        public bool HasDirectImpact => BaseDamageMul > 0
            && (Tags & (GE.SkillTag.Attack | GE.SkillTag.Spell)) != 0;

        /// <summary>
        /// 타격 커밋을 System_Jump의 실제 착지 완료에 연결하는 스킬인지
        /// (MONSTER_ATTACK_TELEGRAPH_DESIGN.md §12.3 점프 피해 커밋)
        /// 채널링은 제외한다. 채널 틱은 ChannelingInterval 주기로 발생해 착지 한 시점에 정렬할 수 없고,
        /// 착지 커밋 소비 경로가 ProcessMultiHitSkill뿐이라 채널링에 적용하면 커밋이 영구 잔류해
        /// Process 상태를 벗어나지 못한다. 채널링 점프는 ChannelImpact로 "도약 1회 + 주기 타격"이 된다.
        /// </summary>
        [JsonIgnore]
        public bool UsesLandingCommit => SkillType == GE.SkillType.Jump
            && ArcHeight > 0f
            && ExecutionType != SkillExecutionType.Channeling
            && HasDirectImpact == true;

        /// <summary>
        /// 발사체가 목표 지점까지 포물선(ArcHeight)으로 날아가 내려앉은 자리에 장판을 까는 스킬인지.
        /// 위치 지정 + 발사체 + 장판 조합이면 장판을 커밋 위치에 바로 만들지 않고 발사체 착지에 맡긴다.
        /// 비행 중에는 아무도 맞히지 않으며, 공격 예고도 그리지 않는다 (깔린 장판 자체가 위험을 알림).
        /// </summary>
        [JsonIgnore]
        public bool DeliversAreaByProjectile => SkillTargetType == GE.SkillTargetType.Position
            && ProjectileId > 0
            && AreaEffectId > 0;

        /// <summary>
        /// 땅 위 직선 돌진 스킬인지 (SKILL_DESIGN §6.11). 돌진 출발이 커밋이고, 피해는 돌진 중 경로 판정이 담당한다.
        /// </summary>
        [JsonIgnore]
        public bool UsesDash => SkillType == GE.SkillType.Dash;

        /// <summary>돌진 거리(칸). Dash에서는 SkillTargetRange2를 각도가 아니라 거리로 쓴다.</summary>
        [JsonIgnore]
        public float DashDistance => UsesDash == true ? SkillTargetRange2 : 0f;
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
        [JsonProperty("Tier")] public int Tier;                              // 스킬 페이지 등급. 0이면 랜덤 스킬 페이지 풀에서 제외
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
    /// Shared chain/fork propagation profile. SkillEffectTable.SkillPropagation actions
    /// reference this table through Param1.
    /// </summary>
    [Serializable]
    public class SkillPropagationTable : TableBase
    {
        [JsonProperty("Name")] public string Name = string.Empty;
        [JsonProperty("DeliveryType")] public GE.SkillDeliveryType DeliveryType = GE.SkillDeliveryType.Inherit;
        [JsonProperty("ChildCount")] public int ChildCount = 1;
        [JsonProperty("MaxDepth")] public int MaxDepth;
        [JsonProperty("InitialSearchRadius")] public float InitialSearchRadius;
        [JsonProperty("SearchRadius")] public float SearchRadius;
        [JsonProperty("HopInterval")] public float HopInterval;
        [JsonProperty("DamageRatioPerGeneration")] public float DamageRatioPerGeneration = 100f;
        // 상한은 DeliveryType에 따라 다르다. SkillPropagationTargetIds.GetMaxTotalDeliveries 참고.
        [JsonProperty("MaxTotalDeliveries")] public int MaxTotalDeliveries = 4;
        [JsonProperty("AllowRepeatTarget")] public bool AllowRepeatTarget;
        [JsonProperty("SegmentEffectKey")] public string SegmentEffectKey = string.Empty;
        [JsonProperty("MaxTurnAngleDeg")] public float MaxTurnAngleDeg = 180f;
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
        [JsonProperty("ActivationDelay")] public float ActivationDelay;             // 설치 후 첫 발동까지 대기 시간. 대기 중에는 피해/버프 없음
        [JsonProperty("SingleShot")] public bool SingleShot;                       // 1회 폭발 후 제거. 연결된 스킬의 공격 프로필을 설치 시 저장해 사용
        [JsonProperty("ForcedMoveEffectId")] public int ForcedMoveEffectId;         // 맞힌 대상에게 장판 중심 기준으로 걸 Knockback/Pull SkillEffect Id (0이면 없음, SKILL_DESIGN §6.10)
    }

    /// <summary>
    /// 절차 던전의 콘텐츠 및 보상 프로필. 런타임 프레임 예산은 DungeonSettings에 남긴다.
    /// 던전 최초 생성 시 이 값들은 DungeonRecord에 스냅샷된다.
    /// </summary>
    [Serializable]
    public class DungeonTable : TableBase
    {
        [JsonProperty("Name")] public string Name = string.Empty;
        [JsonProperty("RoomCount")] public int RoomCount;
        [JsonProperty("MonsterTableIds")] public List<int> MonsterTableIds = new(); // 일반 몬스터 후보. 몬스터마다 균등 확률로 1종 선택
        [JsonProperty("MonsterLevel")] public int MonsterLevel;
        [JsonProperty("BossTableId")] public int BossTableId;
        [JsonProperty("BossLevel")] public int BossLevel;
        [JsonProperty("CompletionGold")] public int CompletionGold;
        [JsonProperty("ChestGold")] public int ChestGold;
        [JsonProperty("ReplacementCooldownHours")] public float ReplacementCooldownHours;
        [JsonProperty("MonsterCountMin")] public int MonsterCountMin = 1;
        [JsonProperty("MonsterCountMax")] public int MonsterCountMax = 1;
        [JsonProperty("RoomWidthMin")] public int RoomWidthMin = 9;
        [JsonProperty("RoomWidthMax")] public int RoomWidthMax = 12;
        [JsonProperty("RoomHeightMin")] public int RoomHeightMin = 9;
        [JsonProperty("RoomHeightMax")] public int RoomHeightMax = 12;

        // 던전 드롭 (ITEM_DROP_BALANCE_DESIGN §7.6). MonsterLevel/BossLevel은 NPC 원정 전리품 전용으로 남는다
        [JsonProperty("MonsterLevelOffset")] public int MonsterLevelOffset;   // 일반 몬스터 레벨 = 입구 청크 지역 레벨 + 이 값
        [JsonProperty("BossLevelOffset")] public int BossLevelOffset;         // 보스 레벨 = 입구 청크 지역 레벨 + 이 값
        [JsonProperty("ChestDropRolls")] public int ChestDropRolls;           // 상자를 열 때 굴리는 드롭 판정 횟수
        [JsonProperty("DropQuantityBonus")] public int DropQuantityBonus;     // 플레이어가 던전 안일 때 장소 묶음 수량 (%)
        [JsonProperty("DropRarityBonus")] public int DropRarityBonus;         // 플레이어가 던전 안일 때 장소 묶음 희귀도 (%)
    }

    /// <summary>
    /// 드롭 구간표 (ITEM_DROP_BALANCE_DESIGN §7.1). 몬스터 레벨(= 드롭 장비의 아이템 레벨)로 행을 고른다.
    /// 확률은 %, 배율은 100 = ×1.0이다. 행은 1~99를 빈틈없이 덮어야 한다(다운로드 검증).
    /// </summary>
    [Serializable]
    public class DropBandTable : TableBase
    {
        [JsonProperty("MinLevel")] public int MinLevel;
        [JsonProperty("MaxLevel")] public int MaxLevel;
        [JsonProperty("NormalRate")] public int NormalRate;               // 장비가 나왔을 때 희귀도 0%의 일반 확률 (%)
        [JsonProperty("MagicRate")] public int MagicRate;                 // 마법 확률 (%)
        [JsonProperty("RareRate")] public int RareRate;                   // 희귀 확률 (%)
        [JsonProperty("RareModMin")] public int RareModMin;               // 희귀 장비 옵션 수 범위 (3~6)
        [JsonProperty("RareModMax")] public int RareModMax;
        [JsonProperty("NormalQualityMin")] public int NormalQualityMin;   // 일반 장비 드롭 품질 범위 (0~20)
        [JsonProperty("NormalQualityMax")] public int NormalQualityMax;
        [JsonProperty("QuantityBonus")] public int QuantityBonus;         // 장소 묶음에 더하는 수량 (%)
        [JsonProperty("HigherCurrencyMul")] public int HigherCurrencyMul; // Tier 2 이상 화폐 가중치 배율 (100 = ×1.0)
        [JsonProperty("BookCapacityMin")] public int BookCapacityMin;     // 스킬북 페이지 용량 보너스 범위
        [JsonProperty("BookCapacityMax")] public int BookCapacityMax;
        [JsonProperty("BookSlotChance")] public int BookSlotChance;       // 스킬북 슬롯 +1 확률 (%)
        [JsonProperty("CurrentBandShare")] public int CurrentBandShare;   // 장비 현재 구간 비중 (%), 나머지는 직전 구간
        [JsonProperty("SellPriceMul")] public int SellPriceMul;           // 플레이어 판매가 아이템 레벨 배율 (100 = ×1.0)
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
        [JsonProperty("KeepDistance")] public float KeepDistance;   // 유지 거리 (Attack에서 더 가까우면 Retreat. 0=후퇴 안함)
        // 순찰 반경 (NAMED_THREAT_DESIGN §5.5). 0 = 순찰 안 함(몬스터는 Idle 대기). 0보다 크면 몬스터도 목표가 없을 때
        // 순찰 기준점 주변을 돌고, 목표를 잃으면 기준점으로 돌아간다. NPC는 이 값과 무관하게 기본 순찰을 쓴다.
        [JsonProperty("PatrolRadius")] public float PatrolRadius;
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
        [JsonProperty("IsDispellable")] public bool IsDispellable;              // 해제 가능 여부. 패시브·영구 효과는 false
        [JsonProperty("BuffEffectId")] public GE.BuffEffectType EffectType;     // 버프 이팩트 타입
        [JsonProperty("EffectValue")] public int EffectValue;                   // 버프 이팩트 값
        [JsonProperty("FadeOutTime")] public float FadeOutTime;                 // 끝나기 전 효과가 0까지 서서히 약해지는 시간 (초). 전체 수명 = Duration + FadeOutTime. 0이면 끝까지 그대로 (현재 Slow만 사용)

        // EffectType=StatModifier일 때 거는 스탯 보정 3칸 (MONSTER_AFFIX_DESIGN.md §6.3). Value 0인 칸은 무시
        [JsonProperty("Stat1")] public GE.Stat Stat1;
        [JsonProperty("ModType1")] public StatModifierType ModType1;
        [JsonProperty("Value1")] public int Value1;
        [JsonProperty("Stat2")] public GE.Stat Stat2;
        [JsonProperty("ModType2")] public StatModifierType ModType2;
        [JsonProperty("Value2")] public int Value2;
        [JsonProperty("Stat3")] public GE.Stat Stat3;
        [JsonProperty("ModType3")] public StatModifierType ModType3;
        [JsonProperty("Value3")] public int Value3;

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
    /// 참 옵션 값 표 (PLAYER_LEVEL_CHARM_DESIGN.md §6.3). Mod·티어마다 모양별로 실제로 붙는 값의 범위를 정수로 적는다.
    /// 1×3과 L자는 같은 3칸 칸을 쓴다. 0(시트 빈칸)이면 그 모양에 붙지 않는 옵션이다.
    /// 어떤 티어가 나올지는 ModTierTable(열리는 레벨·가중치)이 정한다. Id = ModId × 100 + Tier.
    /// </summary>
    [Serializable]
    public class CharmModTierTable : TableBase
    {
        [JsonProperty("ModId")] public int ModId;
        [JsonProperty("Tier")] public int Tier;
        [JsonProperty("Min1x1")] public int Min1x1;
        [JsonProperty("Max1x1")] public int Max1x1;
        [JsonProperty("Min1x2")] public int Min1x2;
        [JsonProperty("Max1x2")] public int Max1x2;
        [JsonProperty("Min3Cell")] public int Min3Cell;     // 1×3·L자
        [JsonProperty("Max3Cell")] public int Max3Cell;
        [JsonProperty("Min2x2")] public int Min2x2;
        [JsonProperty("Max2x2")] public int Max2x2;
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
    /// 상점용 기본 아이템 (NPC_SHOP_EQUIPMENT_DESIGN.md §3, D116). 한 행 = 장비 1종의 고정 옵션 변형 1개.
    /// Base·Implicit은 기존 테이블 그대로 붙고, 고정 옵션은 Prefix 또는 Postfix 1개다. 무작위 롤은 없다.
    /// Id가 저장 식별자(EquipmentData.BasicItemId)이므로 기존 행의 Id를 바꾸거나 다른 변형에 재사용하지 않는다.
    /// </summary>
    [Serializable]
    public class BasicItemTable : TableBase
    {
        [JsonProperty("ItemId")] public int ItemId;
        [JsonProperty("ModTableId")] public int ModTableId;
        [JsonProperty("Value1")] public int Value1;
        [JsonProperty("Value2")] public int Value2;
        [JsonProperty("Weight")] public int Weight;              // 같은 ItemId 변형 사이의 진열 가중치

        [JsonIgnore] public ModTable? Mod;
        // DataManager 로드 검증 통과 여부. 실패 행은 조회 인덱스에서 빠진다.
        [JsonIgnore] public bool IsValid;
        // 이 행으로 만든 장비의 전체 옵션 원형(Base + Implicit + 고정 옵션). 검증 통과 시 DataManager가 채운다.
        // 생성은 복사, 저장 생략 판정은 비교에만 쓰며 직접 수정하지 않는다.
        [JsonIgnore] public IReadOnlyList<ModInstance> TemplateMods = Array.Empty<ModInstance>();

        public override void LoadLate()
        {
            Mod = AR.s.Data?.GetMod(ModTableId);
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
        // 무리 단위 등급 확률 (%, MONSTER_RARITY_DESIGN.md §4). 한 번 굴려 나누므로 두 값의 합은 100 이하.
        [JsonProperty("MagicPackRate")] public int MagicPackRate;   // 무리 전체가 마법
        [JsonProperty("RarePackRate")]  public int RarePackRate;    // 무리 중 1마리가 희귀, 나머지는 일반 호위
    }

    /// <summary>
    /// 절차적 역사 시스템 — 사건 슬롯에 들어갈 재료(종족/신/괴물/장소/유물/영웅).
    /// Type 정수 매핑: 0=Race, 1=God, 2=Monster, 3=Place, 4=Artifact, 5=Hero.
    /// 태그/지형 CSV는 런타임에 HistoryTagRegistry로 비트마스크 변환됨.
    /// </summary>
    [Serializable]
    public class HistoryEntityTable : TableBase
    {
        [JsonProperty("StringId")]            public string StringId = string.Empty;
        [JsonProperty("DisplayName")]         public string DisplayName = string.Empty;
        [JsonProperty("Type")]                public int Type;
        [JsonProperty("TagsCsv")]             public string TagsCsv = string.Empty;
        [JsonProperty("PreferredTerrainCsv")] public string PreferredTerrainCsv = string.Empty;
        [JsonProperty("HostileTagsCsv")]      public string HostileTagsCsv = string.Empty;
        [JsonProperty("StartingPopulation")]  public int StartingPopulation;
        [JsonProperty("ThreatLevel")]         public int ThreatLevel;
        [JsonProperty("EndsWithJongseong")]   public bool EndsWithJongseong;
    }

    /// <summary>
    /// 절차적 역사 시스템 — 사건 청사진. SlotsJson/EffectsJson은 가변 길이라 raw string으로 보관,
    /// 런타임에 SlotDefinition/EffectDefinition으로 deserialize (Phase 1 코드에서 처리).
    /// </summary>
    [Serializable]
    public class HistoryTemplateTable : TableBase
    {
        [JsonProperty("StringId")]          public string StringId = string.Empty;
        [JsonProperty("Weight")]            public int Weight;
        [JsonProperty("EraRestrictionCsv")] public string EraRestrictionCsv = string.Empty;
        [JsonProperty("CooldownYears")]     public int CooldownYears;
        [JsonProperty("SlotsJson")]         public string SlotsJson = string.Empty;
        [JsonProperty("TextTemplate")]      public string TextTemplate = string.Empty;
        [JsonProperty("EffectsJson")]       public string EffectsJson = string.Empty;
        [JsonProperty("CanTriggerIdsCsv")]  public string CanTriggerIdsCsv = string.Empty;
    }

}
