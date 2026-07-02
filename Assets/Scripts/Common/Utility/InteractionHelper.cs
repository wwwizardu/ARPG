#nullable enable
using ARPG.Data;

namespace ARPG.Utility
{
    /// <summary>
    /// 상호작용 타입 (v1 핵심 3종). 감정·태도·물리·관계 계열은 이후 확장.
    /// </summary>
    public enum InteractionType
    {
        Talk,   // 대화
        Trade,  // 거래 (물건 사기/팔기)
        Gift,   // 선물
    }

    /// <summary>상호작용 처리 결과 종류.</summary>
    public enum ResultKind
    {
        Success,      // 즉시 성립/성공
        Fail,         // 실패 (추후 판정형 타입용)
        OpenTrade,    // 거래 UI를 열어야 함 (호출측에 위임)
        NeedsItem,    // 아이템 페이로드 필요 — ResolveGift 사용
        Unsupported,  // 미지원 타입
    }

    /// <summary>선물 반응 등급 (선호/혐오). v1은 Neutral 고정 — 배수는 NPC 백본 유래로 추후 일반화.</summary>
    public enum GiftReactionTier { Loved, Liked, Neutral, Disliked }

    /// <summary>UI 표시용 상호작용 결과 (ECS 컴포넌트 아님).</summary>
    public struct InteractionResult
    {
        public bool Success;
        public ResultKind Kind;
        public int AffinityDelta;
        public int TrustDelta;
        public int FearDelta;
        public int IntimacyDelta;
    }

    /// <summary>선물 처리 결과 (반응 등급 + 관계 델타).</summary>
    public struct GiftResult
    {
        public bool Success;
        public GiftReactionTier Tier;
        public int AffinityDelta;
        public int IntimacyDelta;
    }

    /// <summary>
    /// 플레이어 → NPC 상호작용 판정 유틸리티 (static·무상태).
    /// 성격 스탯 + 관계 4축 기반으로 결과를 계산하고, 관계 변동은 NpcManager에 위임한다.
    /// v1은 대화·거래·선물 3종만 처리하며 모두 판정 없이 성립(항상 성공).
    /// </summary>
    public static class InteractionHelper
    {
        /// <summary>UI가 성공률을 미리 표시 (v1 3종은 항상 100%).</summary>
        public static float GetSuccessRate(int npcId, InteractionType type)
        {
            // TODO: 판정형 타입 추가 시 NpcStatComponent + 관계 4축으로 계산 (설계 §2).
            return 100f;
        }

        /// <summary>
        /// 인자 없는 상호작용 처리 (v1: 대화). 거래는 UI 오픈 신호만, 선물은 ResolveGift 사용.
        /// 관계 변동은 NpcManager.ModifyPlayerRelation 경유(불변식 단일 지점) — 관계 데이터 계층 구현 후 연결.
        /// </summary>
        public static InteractionResult Resolve(int npcId, InteractionType type)
        {
            switch (type)
            {
                case InteractionType.Talk:
                    // 항상 성공 — Affinity +1, Intimacy +1 (설계 §2 Talk).
                    // TODO: NpcManager.ModifyPlayerRelation(npcId, 1, 0, 0, 1);
                    return new InteractionResult
                    {
                        Success = true,
                        Kind = ResultKind.Success,
                        AffinityDelta = 1,
                        IntimacyDelta = 1,
                    };

                case InteractionType.Trade:
                    // 거래는 관계 변동 없음 — 호출측이 거래 UI(NPC 인벤토리 기반)를 연다.
                    return new InteractionResult { Success = true, Kind = ResultKind.OpenTrade };

                case InteractionType.Gift:
                    // 선물은 아이템 페이로드가 필요 — ResolveGift(npcId, item)를 사용한다.
                    return new InteractionResult { Success = false, Kind = ResultKind.NeedsItem };

                default:
                    return new InteractionResult { Success = false, Kind = ResultKind.Unsupported };
            }
        }

        /// <summary>
        /// 선물 처리 — 아이템 가치 tier → 호감도 (설계 §2 Gift).
        /// 선호/혐오 반응 배수는 NPC 백본 유래(NARRATIVE_ENGINE)로 추후 일반화 — v1은 Neutral.
        /// 관계 변동은 NpcManager.ModifyPlayerRelation 경유 — 관계 데이터 계층 구현 후 연결.
        /// </summary>
        public static GiftResult ResolveGift(int npcId, ItemData? item)
        {
            int value = 0;
            if (item != null && item.Table != null)
                value = item.Table.BasePrice;

            int affinityDelta;
            int intimacyDelta = 0;
            if (value <= 10)
            {
                affinityDelta = 3;      // 저가
            }
            else if (value <= 50)
            {
                affinityDelta = 7;      // 중가
            }
            else
            {
                affinityDelta = 15;     // 고가
                intimacyDelta = 5;
            }

            // TODO: 선호/혐오 카테고리 배수 (Loved×2.0 / Disliked 음수 …) — 백본 유래(설계 §4.4/§4.5).
            GiftReactionTier tier = GiftReactionTier.Neutral;

            // TODO: NpcManager.ModifyPlayerRelation(npcId, affinityDelta, 0, 0, intimacyDelta);
            return new GiftResult
            {
                Success = true,
                Tier = tier,
                AffinityDelta = affinityDelta,
                IntimacyDelta = intimacyDelta,
            };
        }
    }
}
