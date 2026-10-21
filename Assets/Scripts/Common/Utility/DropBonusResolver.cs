#nullable enable
using System;
using ARPG.Component;
using ARPG.Tables;
using UnityEngine;
using GE = GlobalEnum;

namespace ARPG.Utility
{
    /// <summary>
    /// 아이템 수량·희귀도 출처 네 묶음의 값 (ITEM_DROP_BALANCE_DESIGN.md §8.3, 단위 %).
    /// ① 플레이어, ②+③ 몬스터(잡힌 몬스터 스탯 + 종·등급·접사 수), ④ 장소(구간표 + 위치 효과).
    /// </summary>
    public struct DropBonusSources
    {
        public int PlayerQuantity;
        public int PlayerRarity;
        public int MonsterQuantity;
        public int MonsterRarity;
        public int LocationQuantity;
        public int LocationRarity;
    }

    /// <summary>드롭 판정에 쓰는 계산 결과. 수량 합계 Q(%)와 희귀도 배율 M, 그 레벨의 구간표 행.</summary>
    public readonly struct DropBonus
    {
        public readonly int QuantityPercent;
        public readonly float RarityMultiplier;
        public readonly DropBandTable? Band;

        public DropBonus(int quantityPercent, float rarityMultiplier, DropBandTable? band)
        {
            QuantityPercent = quantityPercent;
            RarityMultiplier = rarityMultiplier;
            Band = band;
        }
    }

    /// <summary>
    /// 드롭 보너스 계산부 (ITEM_DROP_BALANCE_DESIGN.md §8.2·§8.5). 상태를 갖지 않는다.
    /// 네 묶음을 읽어 {수량 합계, 희귀도 배율}을 만들고, 드롭 코드(DropHelper)는 이 결과만 받는다.
    /// 새 출처는 플레이어·몬스터 스탯(ItemQuantity·ItemRarity)이나 위치 조회 함수로 넣으므로 드롭 코드를 고치지 않는다.
    /// - 수량: 네 묶음을 더한다. 판정 횟수 = 기본 판정 × max(0, 1 + Q/100), 소수 부분은 확률로 1회 더, 상한 40회.
    /// - 희귀도: 묶음 안에서는 더하고 묶음끼리는 곱한다. 곡선(150까지 그대로, 넘는 양 × 100/(넘는 양+100), 최대 250)은
    ///   플레이어 묶음에만 적용한다. 묶음마다 (1 + 값/100)은 0 밑으로 내려가지 않는다.
    /// </summary>
    public static class DropBonusResolver
    {
        /// <summary>드롭 출처(몬스터 1마리·상자 1개) 하나가 굴리는 판정 횟수 상한.</summary>
        public const int MAX_ROLLS_PER_SOURCE = 40;
        /// <summary>플레이어 희귀도 곡선이 꺾이는 지점 (%).</summary>
        public const int PLAYER_RARITY_SOFT_CAP = 150;
        /// <summary>꺾인 뒤 더 늘 수 있는 최대량 (%). 적용값 최대 = 150 + 100 = 250.</summary>
        public const int PLAYER_RARITY_EXCESS_LIMIT = 100;

        // ===== 출처 읽기 =====

        /// <summary>
        /// 몬스터가 죽는 순간 한 번 계산한다. ①은 항상 플레이어 엔티티 값(없으면 0), ②는 죽은 몬스터의 스탯,
        /// ③은 생성 때 DropComponent에 합산해 둔 값, ④는 구간표 수량 + 위치 효과다.
        /// </summary>
        public static DropBonus ResolveForMonster(int monsterEntityId, in DropComponent drop)
        {
            DropBandTable? band = AR.s.Data.GetDropBand(drop.MonsterLevel);
            DropBonusSources sources = new DropBonusSources();
            ReadPlayer(ref sources);
            if (AR.s.Component.TryGetComponent<StatComponent>(monsterEntityId, out StatComponent monsterStat) == true)
            {
                sources.MonsterQuantity += monsterStat.FinalItemQuantity;
                sources.MonsterRarity += monsterStat.FinalItemRarity;
            }
            sources.MonsterQuantity += drop.DropQuantityBonus;
            sources.MonsterRarity += drop.DropRarityBonus;
            ReadLocation(ref sources, band);
            return Resolve(sources, band);
        }

        /// <summary>던전 상자처럼 몬스터가 아닌 출처. ① 플레이어와 ④ 장소 묶음만 적용한다 (§7.6).</summary>
        public static DropBonus ResolveForChest(int itemLevel)
        {
            DropBandTable? band = AR.s.Data.GetDropBand(itemLevel);
            DropBonusSources sources = new DropBonusSources();
            ReadPlayer(ref sources);
            ReadLocation(ref sources, band);
            return Resolve(sources, band);
        }

        /// <summary>네 묶음 값으로 결과를 만든다. 진단도 같은 식을 쓴다.</summary>
        public static DropBonus Resolve(in DropBonusSources sources, DropBandTable? band)
        {
            int quantity = sources.PlayerQuantity + sources.MonsterQuantity + sources.LocationQuantity;
            float multiplier = ResolveRarityMultiplier(sources.PlayerRarity, sources.MonsterRarity, sources.LocationRarity);
            return new DropBonus(quantity, multiplier, band);
        }

        // ===== 계산식 =====

        /// <summary>플레이어 희귀도 합계에 곡선을 적용한 값. 150 이하와 음수는 그대로다.</summary>
        public static float ApplyPlayerRarityCurve(int playerRarity)
        {
            if (playerRarity <= PLAYER_RARITY_SOFT_CAP)
                return playerRarity;

            float excess = playerRarity - PLAYER_RARITY_SOFT_CAP;
            return PLAYER_RARITY_SOFT_CAP + excess * PLAYER_RARITY_EXCESS_LIMIT / (excess + PLAYER_RARITY_EXCESS_LIMIT);
        }

        /// <summary>희귀도 배율 M = (1 + 플레이어 적용값/100) × (1 + 몬스터/100) × (1 + 장소/100).</summary>
        public static float ResolveRarityMultiplier(int playerRarity, int monsterRarity, int locationRarity)
        {
            return GroupMultiplier(ApplyPlayerRarityCurve(playerRarity))
                * GroupMultiplier(monsterRarity)
                * GroupMultiplier(locationRarity);
        }

        /// <summary>
        /// 판정 횟수. 기본 판정 × max(0, 1 + Q/100)의 정수 부분은 그대로, 소수 부분은 그 확률로 1번 더. roll01은 [0, 1).
        /// </summary>
        public static int ResolveRollCount(int baseRolls, int quantityPercent, float roll01)
        {
            if (baseRolls <= 0)
                return 0;

            double expected = baseRolls * Math.Max(0d, 1d + quantityPercent / 100d);
            int whole = (int)Math.Floor(expected);
            if (roll01 < expected - whole)
                whole++;
            return Mathf.Min(whole, MAX_ROLLS_PER_SOURCE);
        }

        /// <summary>
        /// 장비 등급 판정. 희귀 → 마법 → 일반 순서로 확인한다 (D17, §8.2).
        /// 희귀 확인 = min(1, 기본 희귀 × M), 마법 확인 = min(1, 기본 마법 ÷ (100 − 기본 희귀) × M). 두 roll은 [0, 1).
        /// </summary>
        public static GE.EquipmentGrade ResolveGrade(DropBandTable band, float rarityMultiplier, float rareRoll01,
            float magicRoll01)
        {
            if (rareRoll01 < GetRareChance(band, rarityMultiplier))
                return GE.EquipmentGrade.Rare;
            if (magicRoll01 < GetMagicCheckChance(band, rarityMultiplier))
                return GE.EquipmentGrade.Magic;
            return GE.EquipmentGrade.Normal;
        }

        public static float GetRareChance(DropBandTable band, float rarityMultiplier)
        {
            return Mathf.Clamp01(band.RareRate / 100f * rarityMultiplier);
        }

        /// <summary>희귀가 아닐 때의 마법 확인 확률 (전체 확률이 아니다).</summary>
        public static float GetMagicCheckChance(DropBandTable band, float rarityMultiplier)
        {
            int nonRare = 100 - band.RareRate;
            if (nonRare <= 0)
                return 0f;
            return Mathf.Clamp01((float)band.MagicRate / nonRare * rarityMultiplier);
        }

        /// <summary>
        /// 화폐 가중치. Tier 1은 DropRate 그대로, Tier 2 이상은 DropRate × 구간표 상위 화폐 배율 × M (§7.3).
        /// </summary>
        public static float GetCurrencyWeight(int dropRate, int tier, DropBandTable? band, float rarityMultiplier)
        {
            if (dropRate <= 0)
                return 0f;
            if (tier <= 1)
                return dropRate;

            float higherMul = band != null ? band.HigherCurrencyMul / 100f : 1f;
            return dropRate * higherMul * Mathf.Max(0f, rarityMultiplier);
        }

        private static float GroupMultiplier(float percent)
        {
            return Mathf.Max(0f, 1f + percent / 100f);
        }

        private static void ReadPlayer(ref DropBonusSources sources)
        {
            int playerId = AR.s.Data.CurrentPlayerEntityId;
            if (playerId < 0
                || AR.s.Component.TryGetComponent<StatComponent>(playerId, out StatComponent playerStat) == false)
                return;

            sources.PlayerQuantity = playerStat.FinalItemQuantity;
            sources.PlayerRarity = playerStat.FinalItemRarity;
        }

        // ④ 장소: 구간표 수량 보너스 + 위치 효과. 위치에 묶인 효과는 그 효과를 가진 시스템이 조회 함수를 제공한다 (§8.6).
        private static void ReadLocation(ref DropBonusSources sources, DropBandTable? band)
        {
            if (band != null)
                sources.LocationQuantity += band.QuantityBonus;

            if (AR.s.Dungeon != null
                && AR.s.Dungeon.TryGetPlayerLocationDropBonus(out int dungeonQuantity, out int dungeonRarity) == true)
            {
                sources.LocationQuantity += dungeonQuantity;
                sources.LocationRarity += dungeonRarity;
            }
        }
    }
}
