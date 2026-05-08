#nullable enable
using System;

namespace ARPG.Village
{
    /// <summary>
    /// Phase D: MerchantStall 매물 1건. VillageData.MerchantStock에 누적.
    /// 게임시간 24h마다 VillageManager.RollMerchantStock으로 재롤.
    /// 풀 조건: ItemTable.Tier ≤ village.Stage AND ItemTable.BasePrice > 0 (BasePrice가 매물 자격 플래그 겸용).
    /// </summary>
    [Serializable]
    public class MerchantStockEntry
    {
        public int ItemTableId;
        public int RemainingCount;

        // 스킬북 매물 (SKILLBOOK_DESIGN.md §10) — ItemTableId가 SkillBook 등급 책일 때만 사용.
        // RollMerchantStock에서 같은 Tier의 SkillTable에서 픽해 저장 → 같은 stock entry는 항상 같은 스킬.
        // 0이면 일반 아이템(스킬북 아님).
        public int SkillId;

        // 스킬 페이지 매물 (SKILL_RUNE_DESIGN.md §8.2) — ItemTableId가 SkillPage 등급 페이지일 때만 사용.
        // RollMerchantStock에서 PageCost 범위 매칭 SkillEffect에서 픽해 저장 → 같은 stock entry는 항상 같은 페이지.
        // 0이면 일반 아이템(스킬 페이지 아님).
        public int SkillEffectId;
    }
}
