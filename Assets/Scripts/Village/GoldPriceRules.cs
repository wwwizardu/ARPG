#nullable enable
using System;
using UnityEngine;

namespace ARPG.Village
{
    /// <summary>물가 등급 (GOLD_BALANCE_DESIGN D129). 등급마다 지수를 따라가는 정도(민감도)가 다르다.</summary>
    public enum GoldPriceClass
    {
        Common = 0,     // 장비 Tier 1, 재료
        Mid = 1,        // 장비 Tier 2~3, 스킬 페이지
        Rare = 2,       // 스킬북
        Service = 3,    // 치료비·생활비 예비금·보충 NPC 시작 Gold·금고 기준선
    }

    /// <summary>
    /// 세계 물가 지수 저장 상태 (GOLD_BALANCE_DESIGN D126·D127·D130, World 파티션).
    /// 기준값이 0이면 아직 정하지 않은 것이다 — 처음 갱신할 때 그 순간의 세계 Gold로 정하고 지수 1.0에서 시작한다
    /// (새 게임·기존 세이브 공통, Q6). 저장 복사기가 공개 속성도 저장하므로 속성은 두지 않는다.
    /// </summary>
    [Serializable]
    public sealed class GoldPriceIndexState
    {
        public long BaselineWorldGold;
        public float Index = 1f;
        public int LastUpdateDay = -1;

        /// <summary>로드 보정 — 손상 값이면 처음 상태로 되돌린다.</summary>
        public void Sanitize()
        {
            if (BaselineWorldGold < 0 || float.IsNaN(Index) || float.IsInfinity(Index) || Index <= 0f)
            {
                BaselineWorldGold = 0;
                Index = 1f;
                LastUpdateDay = -1;
            }
        }
    }

    /// <summary>
    /// 마을 골드 밸런스 — 세계 물가 지수 규칙 (GOLD_BALANCE_DESIGN §4.1). 상태 없는 순수 로직이고,
    /// 지수 상태는 WorldData.GoldPrice가 소유한다. 게임(VillageManager.UpdateGoldPriceIndex)과
    /// 진단(NpcAmbitionSimDiagnostics)이 같은 Advance를 부른다.
    /// </summary>
    public static class GoldPriceRules
    {
        // 본체 단계(2026-10-03)에서 켰다. 끄면 게임은 지수를 갱신하지 않고 가격 배율은 항상 1이다.
        // const가 아닌 static readonly — 꺼진 분기를 컴파일러가 도달 불가로 경고하지 않게 한다.
        public static readonly bool IndexEnabled = true;

        // D130: 지수가 통화량 비율을 따라가는 시간상수 (게임일). 하루마다 차이의 1/45씩 좁힌다.
        public const float IndexLagDays = 45f;

        // D129: 등급별 민감도. 희귀 1.0이 상한이다 (넘기면 물가가 통화량보다 빨리 올라 마을 금고가 매입을 감당하지 못한다).
        public const float CommonSensitivity = 0.3f;
        public const float MidSensitivity = 0.6f;
        public const float RareSensitivity = 1.0f;
        public const float ServiceSensitivity = 0.6f;

        // 통화량 비율의 안전 범위 — 비정상 상태(세계 Gold 0 등)에서 가격이 0이나 무한대가 되지 않게 한다.
        private const float MinTarget = 0.1f;
        private const float MaxTarget = 100f;

        /// <summary>D129 등급 분류: 스킬북 = 희귀, 스킬 페이지·장비 Tier 2 이상 = 중간, 나머지(장비 Tier 1·재료 등) = 흔함.</summary>
        public static GoldPriceClass Classify(GlobalEnum.ItemType itemType, int tier)
        {
            if (itemType == GlobalEnum.ItemType.SkillBook)
                return GoldPriceClass.Rare;
            if (itemType == GlobalEnum.ItemType.SkillPage)
                return GoldPriceClass.Mid;
            if (itemType == GlobalEnum.ItemType.Equipment && tier >= 2)
                return GoldPriceClass.Mid;
            return GoldPriceClass.Common;
        }

        public static float GetSensitivity(GoldPriceClass priceClass)
        {
            switch (priceClass)
            {
                case GoldPriceClass.Mid: return MidSensitivity;
                case GoldPriceClass.Rare: return RareSensitivity;
                case GoldPriceClass.Service: return ServiceSensitivity;
                default: return CommonSensitivity;
            }
        }

        /// <summary>가격 배율 = 지수^민감도.</summary>
        public static float GetMultiplier(float index, GoldPriceClass priceClass)
        {
            if (index == 1f)
                return 1f;
            return Mathf.Pow(Mathf.Max(index, 0.0001f), GetSensitivity(priceClass));
        }

        /// <summary>정수 가격에 배율을 곱한다. 배율이 정확히 1이면 원래 값을 그대로 돌려준다 (부동소수 반올림 영향 없음).</summary>
        public static int ApplyMultiplier(int price, float multiplier, int minPrice)
        {
            if (multiplier == 1f || price <= 0)
                return price;
            return Mathf.Max(minPrice, Mathf.RoundToInt(price * multiplier));
        }

        /// <summary>통화량 비율 = 세계 Gold ÷ 기준값 (기준값이 없으면 1).</summary>
        public static float GetTarget(GoldPriceIndexState state, long worldGold)
        {
            if (state.BaselineWorldGold <= 0)
                return 1f;
            return Mathf.Clamp((float)((double)worldGold / state.BaselineWorldGold), MinTarget, MaxTarget);
        }

        /// <summary>
        /// 게임일 경계를 지났을 때만 지수를 갱신한다. 기준값이 없으면 지금 세계 Gold로 정하고 지수 1.0에서 시작한다.
        /// 여러 날을 건너뛰면 하루 단계를 그만큼 적용한 것과 같은 닫힌 식으로 따라잡는다:
        /// 지수 = 목표 + (지수 − 목표) × (1 − 1/45)^(지난 날 수).
        /// </summary>
        public static void Advance(GoldPriceIndexState state, long worldGold, float gameTime)
        {
            int day = Mathf.FloorToInt(gameTime / 24f);
            if (state.BaselineWorldGold <= 0)
            {
                state.BaselineWorldGold = Math.Max(1L, worldGold);
                state.Index = 1f;
                state.LastUpdateDay = day;
                return;
            }
            if (day <= state.LastUpdateDay)
                return;

            int elapsedDays = day - state.LastUpdateDay;
            float target = GetTarget(state, worldGold);
            float keep = Mathf.Pow(1f - 1f / IndexLagDays, elapsedDays);
            state.Index = target + (state.Index - target) * keep;
            state.LastUpdateDay = day;
        }
    }
}
