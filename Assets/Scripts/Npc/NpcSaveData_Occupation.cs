#nullable enable
using System;
using System.ComponentModel;
using Newtonsoft.Json;

namespace ARPG.Npc
{
    /// <summary>
    /// NPC 채광·대장 숙련과 생활 기록 (NPC_OCCUPATIONS_PERSONAL_STORIES_PLAN §3.1·§9.4). 기본값이면 저장하지 않는다.
    /// 숙련 칸은 Identity 묶음이다 (SaveSnapshotGroups.Npc 기본값).
    /// </summary>
    public partial class NpcSaveData
    {
        // 채광·대장 숙련 (1~100). LifeSkillCap이 0이면 아직 정하지 않음 — NpcManager가 첫 활동 선택 때 한 번 정한다 (InitialPower와 같은 방식).
        [JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)]
        public int MiningSkill;
        [JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)]
        public int SmithingSkill;
        // 두 숙련의 합산 상한 (60~120). 합계가 이 값에 닿으면 더 오르지 않는다.
        [JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)]
        public int LifeSkillCap;
        // 채광·제작 기록. 한 번도 하지 않은 NPC는 null이고 저장하지 않는다.
        [JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)]
        public NpcWorkRecord? Work;

        [JsonIgnore]
        public bool LifeSkillsInitialized => LifeSkillCap > 0;

        public int GetLifeSkill(NpcLifeSkill skill) => skill == NpcLifeSkill.Mining ? MiningSkill : SmithingSkill;

        /// <summary>숙련 +1. 상한(100)·합산 상한에 닿았으면 false.</summary>
        public bool TryRaiseLifeSkill(NpcLifeSkill skill)
        {
            if (MiningSkill + SmithingSkill >= LifeSkillCap)
                return false;
            if (skill == NpcLifeSkill.Mining)
            {
                if (MiningSkill >= NpcOccupationRules.SkillMax)
                    return false;
                MiningSkill++;
            }
            else
            {
                if (SmithingSkill >= NpcOccupationRules.SkillMax)
                    return false;
                SmithingSkill++;
            }
            return true;
        }

        /// <summary>분실한 장비 칸이 아직 비어 있는가 (보충 전). 빈 칸이 있는 동안은 더 잃지 않는다 (§4.3).</summary>
        public bool HasPendingEquipmentLoss(out NpcEquipmentLoss? loss)
        {
            loss = PersonalStories?.Loss;
            return loss != null && loss.RefillGameTime < 0f;
        }
    }

    /// <summary>채광·제작 기록 (§9.4 대장장이·광부 대사, §9.2 숙련 이정표). 시각 -1 = 없음.</summary>
    [Serializable]
    public sealed class NpcWorkRecord
    {
        [Flags]
        public enum Milestone
        {
            None = 0,
            MinedTin = 1,
            MinedIron = 2,
            CraftedIron = 4,
        }

        [DefaultValue(-1f), JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)]
        public float LastMiningGameTime = -1f;
        [JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)]
        public int MiningCount;
        [DefaultValue(-1f), JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)]
        public float LastCraftGameTime = -1f;
        [JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)]
        public int CraftCount;
        // 최근 7일 안의 채광·제작 횟수를 세기 위한 직전 시각 (그보다 오래된 기록은 세지 않는다)
        [DefaultValue(-1f), JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)]
        public float PreviousMiningGameTime = -1f;
        [DefaultValue(-1f), JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)]
        public float PreviousCraftGameTime = -1f;
        // 상점이 광석을 받아 주지 않은 마지막 시각 (재고 상한·금고 부족)
        [DefaultValue(-1f), JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)]
        public float LastOreSellRejectedGameTime = -1f;
        // 만들 장비가 있는데 광석이 없어 못 만든 마지막 시각과 그 광석
        [DefaultValue(-1f), JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)]
        public float CraftBlockedGameTime = -1f;
        [JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)]
        public int CraftBlockedOreItemId;
        // 마지막 제작품: 위탁할 때 "누구를 위해 만들었나"를 잇는다 (§9.2 ② 장비 잃은 이웃을 보고 만든 경우)
        [JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)]
        public int LastCraftItemInstanceId;
        [DefaultValue(-1), JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)]
        public int LastCraftForNpcId = -1;
        [JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)]
        public Milestone Milestones;
    }

    /// <summary>
    /// 원정 중상으로 잃은 장비와 그 칸을 채운 결과 (§4.3·§9.2 ①). 부상 사연 칸에 붙어 "죽다 살아났소. 검까지 잃었지. 지금 이건 C 솜씨요"를 만든다.
    /// RefillGameTime이 -1이면 아직 빈 칸이다. 최신 1건만 둔다.
    /// </summary>
    [Serializable]
    public sealed class NpcEquipmentLoss
    {
        public int Slot = -1;
        public int ItemId;
        public string ItemName = string.Empty;
        public float LostGameTime;
        [DefaultValue(-1f), JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)]
        public float RefillGameTime = -1f;
        [JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)]
        public int RefillItemId;
        [DefaultValue(""), JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)]
        public string RefillItemName = string.Empty;
        // 위탁 제작품으로 채웠으면 제작자 (0 = 상점 기본 장비)
        [JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)]
        public int RefillMakerId;
        [DefaultValue(""), JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)]
        public string RefillMakerName = string.Empty;
        // 마지막으로 못 채운 이유 (빈 칸으로 지내는 동안의 대사, §9.2 ④)
        [JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)]
        public NpcRefillBlock LastRefillBlock;
    }

    public enum NpcRefillBlock
    {
        None = 0,
        NoCandidate = 1,   // 상점에 맞는 장비가 없음
        NoMoney = 2,       // 있지만 돈이 모자람
    }

    /// <summary>생활 사연 종류 (§9.2 ②③④). 끝에만 추가한다.</summary>
    public enum NpcLifeStoryKind
    {
        None = 0,
        CraftSold = 1,          // 내 위탁품이 팔려 다른 NPC가 씀 (제작 자랑)
        MiningMilestone = 2,    // 처음 주석·철을 캠
        SmithingMilestone = 3,  // 처음 철 장비를 만듦
        Hardship = 4,           // 빈 칸으로 7일 이상 지내다 채움 (고생담)
    }

    /// <summary>생활 사연 칸 (§9.2, 2026-11-01 결정 — 기존 6칸에 1칸 추가). 제작 자랑·숙련 이정표·고생담 중 최신 1건.</summary>
    [Serializable]
    public sealed class NpcLifeStory
    {
        public NpcLifeStoryKind Kind;
        public float When;
        [JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)]
        public int ItemId;
        [DefaultValue(""), JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)]
        public string ItemName = string.Empty;
        // 제작 자랑: 산 사람 / 고생담: 채운 장비의 제작자 (-1 = 없음). 이름은 스냅샷이라 상대가 죽어도 말할 수 있다.
        [DefaultValue(-1), JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)]
        public int OtherNpcId = -1;
        [DefaultValue(""), JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)]
        public string OtherName = string.Empty;
        [JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)]
        public int OreItemId;
        // 고생담: 빈 칸으로 지낸 날 수
        [JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)]
        public int Days;
        // 제작 자랑: 산 사람이 잃은 장비를 보고 만든 것 / 고생담: 잃은 장비 이름
        [JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)]
        public bool MadeForBuyer;
        [DefaultValue(""), JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)]
        public string LostItemName = string.Empty;
    }
}
