#nullable enable
using System;
using System.ComponentModel;
using ARPG.Data;
using Newtonsoft.Json;

namespace ARPG.Village
{
    /// <summary>
    /// 상점 위탁 칸 1개 (NPC_OCCUPATIONS_PERSONAL_STORIES_PLAN §7.2). 팔릴 때까지 물건은 제작자 것이고, 팔리면 대금이 제작자 몫으로 남는다.
    /// 가격은 맡긴 시각과 상점 가격에서 계산하므로 저장하지 않는다. 모험가 매입품(D42)과 다른 칸이며 재입고 때 지우지 않는다.
    /// </summary>
    [Serializable]
    public sealed class VillageConsignment
    {
        public ItemData Item = null!;
        public int MakerNpcId;
        public string MakerName = string.Empty;
        public float ConsignedGameTime;
        // 장비를 잃은 이웃을 보고 만든 제작품이면 그 NPC (§9.2 ② "○○가 잃었다길래 만들어 뒀지"). -1 = 없음.
        [DefaultValue(-1), JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)]
        public int MadeForNpcId = -1;
    }

    /// <summary>팔렸지만 제작자가 아직 받지 않은 위탁 대금 1건. 제작자는 다음 상점 거래 때 받고 그때 누가 샀는지 안다 (§7.3).</summary>
    [Serializable]
    public sealed class VillageConsignmentProceeds
    {
        public int MakerNpcId;
        public int Gold;
        public int ItemId;
        public string ItemName = string.Empty;
        public float SoldGameTime;
        // 산 NPC (-1 = 상점이 끝까지 안 팔린 물건을 사들임)
        [DefaultValue(-1), JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)]
        public int BuyerNpcId = -1;
        [DefaultValue(""), JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)]
        public string BuyerName = string.Empty;
        [JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)]
        public bool MadeForBuyer;
    }
}
