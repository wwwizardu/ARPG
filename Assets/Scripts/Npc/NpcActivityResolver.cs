#nullable enable
using System.Collections.Generic;
using ARPG.Component;
using ARPG.Data;
using ARPG.History;
using ARPG.Tables;
using ARPG.Utility;
using UnityEngine;

namespace ARPG.Npc
{
    /// <summary>
    /// 활동 선택 입력 중 성격 밖의 상황 (NPC_AMBITION_DESIGN §6.2 필요 + Trade 기회).
    /// NpcManager가 NPC·상점 상태로 채우고 NpcActivityResolver.SelectNextActivity가 소비한다.
    /// </summary>
    public struct NpcActivityChoiceContext
    {
        public bool GoldShort;           // Gold < 치료비 예비금 (D36)
        public bool BagFull;             // 인벤토리 빈 칸 ≤ 1
        public bool RecentlyRecovered;   // Wounded 종료 후 RecentRecoveryHours 이내
        public bool TradeAvailable;      // 소속 마을 Shop이 있고 할 일(판매 또는 구매 후보)이 있음
        public bool DelveAvailable;      // 소속 마을에 던전 슬롯(DungeonId)이 있음 (D56, 입구 배치 여부 무관 D57)
        public bool HasPromisingRumor;   // "유망한 소문" 보유 (§6.2, D56·D76) — Delve 필요 배율
    }

    /// <summary>
    /// NPC 활동 선택과 Hunt 결과를 결정론적으로 계산한다.
    /// 상태 적용과 저장은 호출자인 System_NpcSim이 담당한다.
    /// </summary>
    public static class NpcActivityResolver
    {
        // 모두 게임시간(hour) 단위. 튜닝값을 한곳에 두어 상태 전이 코드와 분리한다.
        public const float RestDurationHours = 4f;
        public const float HuntDurationHours = 8f;
        public const float WoundedRecoveryHours = 24f;

        // Hunt 전투력 정규화 (D31). 기준 전투력 = 생성 규칙 그대로인 평범한 NPC 집단의 평균 NpcPower.
        // 기준 전투력에서 capability가 교체 전 식의 평균(0.20 + 용기 평균 0.5 × 0.55 = 0.475)이 되도록 맞춰
        // 평균 성공·부상·사망률을 유지한다. 판정 곡선이 capability에 선형이라 평균 capability가 같으면 평균 확률도 같다.
        // 진단 메뉴 ARPG/Npc/Diagnostics/Hunt Power Normalization으로 교체 전/후 평균을 비교한다.
        // NPC_SHOP_EQUIPMENT(2026-10-03): NPC 장비가 고정 옵션 기본 아이템으로 바뀌어 세이브 생존 2,387명 평균이 25.96 → 10.75가 됐다.
        // 기준을 새 평균으로 옮겨 평균 capability 0.475를 유지한다 (솔로 성공 61.8%·부상 25.3%·Food/Hunt 1.38, Food 문턱 그대로).
        public const float HuntReferencePower = 10.75f;
        public const float HuntCapabilityAtReference = 0.475f;
        // 기준 전투력의 2배 차이가 capability 0.5 차이. 개인 상한 +20%를 다 채우면 capability +0.10.
        public const float HuntCapabilityPerPowerRatio = 0.5f;
        // 교체 전 Food 식의 문턱(용기 0.35 / 0.70)을 같은 capability 축으로 옮긴 값 (0.20 + 문턱 × 0.55)에서 출발했다.
        // 새 capability 분포는 교체 전(용기 균등분포)보다 가운데에 몰려 있어, 상단 문턱을 0.585 → 0.56으로 낮춰
        // 평균 Food/성공을 교체 전과 맞췄다 (세이브 기준 교체 전 2.24 / 새 식 2.25).
        public const float HuntFoodMidCapability = 0.3925f;
        public const float HuntFoodHighCapability = 0.56f;

        // Hunt 피해 확률 (사망 + 부상). 교체 전 곡선의 합 그대로다 — 솔로 사망 0.08→0.01 + 부상 0.32→0.08, 파티원 사망 0.045→0.004 + 부상 0.24→0.035.
        public const float SoloHuntHarmChanceAtZero = 0.40f;
        public const float SoloHuntHarmChanceAtFull = 0.09f;
        public const float PartyHuntHarmChanceAtZero = 0.285f;
        public const float PartyHuntHarmChanceAtFull = 0.039f;
        // Hunt 사망 확률 (사용자 지시 2026-09-28: 거의 0). 교체 전 사망 곡선의 약 0.375%. 나머지는 부상으로 간다.
        // 진단(인구 보충 없음) 30일 사망 0.5~1%(목표 약 0.75%)가 목표다 — ARPG/Npc/Diagnostics/Ambition Sim으로 확인한다.
        public const float SoloHuntDeathChanceAtZero = 0.0003f;
        public const float SoloHuntDeathChanceAtFull = 0.0000375f;
        public const float PartyHuntDeathChanceAtZero = 0.00017f;
        public const float PartyHuntDeathChanceAtFull = 0.000015f;

        // Hunt 전리품은 몬스터 드랍 규칙(DropTable 1번)의 카테고리 비율을 재사용한다. Nothing은 제외한다 (D35).
        public const int HuntLootDropTableId = 1;
        // 개인 전리품 RNG salt. 솔로는 4, 파티원은 30 + 파티 내 인덱스.
        public const int HuntLootSalt = 4;
        public const int PartyHuntLootSaltBase = 30;

        // 추상 치료 서비스 (NPC_AMBITION_DESIGN D36). 부상 상태가 될 때 1회 판정한다.
        public const int TreatmentFee = 10;
        public const float TreatedRecoveryHours = 8f;
        // 치료 구매 RNG salt. 솔로는 5 (NPC EntityId 기준), 파티원은 40 + 파티 내 인덱스 (PartyId 기준).
        public const int TreatmentSalt = 5;
        public const int PartyTreatmentSaltBase = 40;

        // 부상 등급 (NPC_AMBITION_DESIGN D91). 원정 부상이 날 때 별도 결정론 롤로 중상 여부를 정한다 (솔로·파티원, Hunt·Delve 공통).
        // 가벼운 부상 = WoundedRecoveryHours / TreatedRecoveryHours (24h / 8h). 중상 = 72h / 치료 시 24h. 치료 판정·치료비는 같다.
        public const float SevereWoundChance = 0.10f;
        public const float SevereWoundRecoveryHours = 72f;
        public const float SevereWoundTreatedRecoveryHours = 24f;
        // 중상 RNG salt (140번대 이후 — 기존 결과·치료·전리품 난수열과 독립). 소유자·시각은 같은 원정의 치료 판정과 같다.
        // 140 솔로 Hunt, 141 솔로 Delve (NPC EntityId 기준), 150+i 파티원 Hunt, 160+i 파티원 Delve (PartyId 기준, i = 파티 내 인덱스).
        // 142~149는 부상 예비.
        public const int SevereWoundHuntSalt = 140;
        public const int SevereWoundDelveSalt = 141;
        public const int PartySevereWoundHuntSaltBase = 150;
        public const int PartySevereWoundDelveSaltBase = 160;
        // 살 수 있어도 성격에 따라 사지 않을 수 있다. P1b는 탐욕만으로 선형: 탐욕 0 → 95%, 탐욕 100 → 55% (관리 세션 결정).
        public const float TreatmentBuyChanceAtNoGreed = 0.95f;
        public const float TreatmentBuyChanceAtMaxGreed = 0.55f;
        // P1c 야망 가중 (D36). 탐욕 선형값에 더한 뒤 [Min, Max]로 자른다.
        public const float TreatmentGloryExplorerBonus = 0.10f;
        public const float TreatmentCautiousBonus = 0.05f;
        public const float TreatmentWealthPenalty = 0.10f;
        public const float TreatmentBuyChanceMin = 0.40f;
        public const float TreatmentBuyChanceMax = 0.98f;

        // ===== 효용 기반 활동 선택 (NPC_AMBITION_DESIGN §6, D39, D40) =====
        // Trade는 마을 상점에서 1시간. 종료 시점에 TradeAtShop 1회 (D39).
        public const float TradeDurationHours = 1f;
        // 활동별 기본값. 세계 평균 Hunt 선택 비중 ≈ 43%(교체 전 식의 평균)가 되도록 맞춘 개발 튜닝 상수다 (D40).
        // 실행 중 자동 보정은 없다. 진단 메뉴 ARPG/Npc/Diagnostics/Ambition Sim으로 확인한다.
        // P2a (D56): Delve 몫을 주로 Rest에서 가져오도록 1.0 → 0.9. Hunt 비중 43% ±5%p 유지를 진단으로 확인한다.
        // GOLD_BALANCE 본체(2026-10-03): 마을 금고에 Gold가 돌며 Trade 기회가 늘어(6% → 14%) Hunt 기준을 38% ±5%p로 개정했다 (상수는 그대로).
        public const float RestBaseScore = 0.90f;
        public const float HuntBaseScore = 0.85f;
        public const float TradeBaseScore = 0.50f;
        // 필요 배율 (§6.2, P1 범위: 골드 부족·가방 가득·부상 회복 직후). 성향 클램프 밖에서 곱한다.
        public const float GoldShortHuntMultiplier = 1.25f;
        public const float GoldShortTradeMultiplier = 1.5f;
        public const float BagFullTradeMultiplier = 2.0f;
        public const float RecentRecoveryRestMultiplier = 1.5f;
        public const float RecentRecoveryHours = 12f;
        // 가방 가득 기준: 빈 칸 ≤ 1 (§6.2)
        public const int BagFullFreeSlotThreshold = 1;
        // 성향·야망 배율의 최대 편차 ±20% (D40)
        public const float DispositionMaxDeviation = 0.20f;
        public const float SecondaryAmbitionWeight = 0.5f;
        // 성격 수치 항 (수치 0 또는 100일 때의 편차)
        public const float RestPatienceDeviation = 0.08f;
        public const float HuntCourageDeviation = 0.10f;
        public const float HuntCuriosityDeviation = 0.03f;
        public const float TradeGreedDeviation = 0.08f;
        // 야망 항 (주 야망 기준, 보조는 × SecondaryAmbitionWeight)
        public const float AmbitionHuntBonus = 0.10f;              // 명예·탐험 → Hunt
        public const float AmbitionRestPenalty = 0.05f;            // 명예·탐험 → Rest 감소
        public const float AmbitionWealthTradeBonus = 0.12f;       // 부 → Trade
        public const float AmbitionComradeHuntBonus = 0.05f;       // 동료 → Hunt 약간
        public const float AmbitionOpportunistTradeBonus = 0.08f;  // 기회 → Trade 약간
        public const float AmbitionCautiousRestBonus = 0.10f;      // 안전 → Rest
        public const float AmbitionCautiousHuntPenalty = 0.05f;    // 안전 → Hunt 감소

        // ===== 던전 원정 DungeonDelve (NPC_AMBITION_DESIGN §7.1, D49·D55·D56) =====
        public const float DelveDurationHours = 12f;
        // 선택 기본값. 세계 평균 Delve 선택 비중 ≈ 5%가 되도록 맞춘 개발 튜닝 상수 (D56). 늘어난 몫은 Rest 기본값을 낮춰 가져온다.
        // P2b: "유망한 소문" 배율(×1.5, 선택 시점 보유 약 21%)이 더해져 0.11 → 0.10으로 낮춰 세계 비중을 약 5%로 유지한다 (D76).
        public const float DelveBaseScore = 0.10f;
        // 성격 수치 항: 호기심·용기 → 던전 (§4.1 탐험 야망의 주 성격)
        public const float DelveCuriosityDeviation = 0.10f;
        public const float DelveCourageDeviation = 0.05f;
        // 야망 항: 탐험 = 던전 원정 선호, 명예 = 강한 목표 선호, 안전 = 던전 기피 (§4.1)
        public const float AmbitionExplorerDelveBonus = 0.15f;
        public const float AmbitionGloryDelveBonus = 0.08f;
        public const float AmbitionCautiousDelvePenalty = 0.10f;
        // 판정 곡선: Hunt보다 성공은 낮고 피해(부상)는 많다 (D55). 사망 곡선은 Hunt와 같은 D48 수준을 쓴다.
        public const float SoloDelveSuccessChanceAtZero = 0.30f;
        public const float SoloDelveSuccessChanceAtFull = 0.76f;
        public const float SoloDelveHarmChanceAtZero = 0.48f;
        public const float SoloDelveHarmChanceAtFull = 0.14f;
        public const float PartyDelveSuccessChanceAtZero = 0.38f;
        public const float PartyDelveSuccessChanceAtFull = 0.84f;
        public const float PartyDelveHarmChanceAtZero = 0.36f;
        public const float PartyDelveHarmChanceAtFull = 0.08f;
        // 보상은 전리품뿐 (D55, 골드·Food 없음). 원정 성공 = DungeonTable.MonsterLevel 일반 롤 1회,
        // NPC 완료 보상 = BossLevel 일반 롤 1회 (희귀 가중치·보너스 롤 없음, D65 개정).
        // NPC 던전 완료 확률 (D55): 원정 성공 시에만. capability 0.475 미만 0%, 0.475에서 3%, 1.0에서 8% (선형).
        public const float DelveCompletionMinCapability = HuntCapabilityAtReference;
        public const float DelveCompletionChanceAtMin = 0.03f;
        public const float DelveCompletionChanceAtFull = 0.08f;
        // Delve RNG salt (기존 0~5, 10+i, 20, 30+i, 40+i와 겹치지 않게 50번대 이후).
        // 솔로(소유자 EntityId): 50 결과, 51 일반 전리품, 53 완료 판정, 54 완료 보상 전리품, 55 치료.
        // 56 대상 슬롯 선택(솔로 EntityId / 파티 PartyId).
        // 파티(소유자 PartyId): 60+i 파티원 결과, 70+i 일반 전리품, 90+i 치료, 100 완료 판정, 110+i 완료 보상.
        // 52·80+i는 폐기된 희귀 보너스 롤의 번호다 — reserved, 재사용 금지 (다른 번호도 당기지 않는다).
        public const int DelveOutcomeSalt = 50;
        public const int DelveLootSalt = 51;
        public const int DelveCompletionSalt = 53;
        public const int DelveCompletionLootSalt = 54;
        public const int DelveTreatmentSalt = 55;
        public const int DelveTargetSalt = 56;
        public const int PartyDelveOutcomeSaltBase = 60;
        public const int PartyDelveLootSaltBase = 70;
        public const int PartyDelveTreatmentSaltBase = 90;
        public const int PartyDelveCompletionSalt = 100;
        public const int PartyDelveCompletionLootSaltBase = 110;

        // ===== 소문 (NPC_AMBITION_DESIGN §5.2·§6.2·§7.4, P2b D70~D77) =====
        // "유망한 소문" 필요 배율 (D76). 성향 ±20% 클램프 밖에서 곱한다 (필요는 성향과 별개, D40).
        public const float PromisingRumorDelveMultiplier = 1.5f;
        // Rest 종료 시 교환 확률 (D74 개정: 항상이 아니라 확률적으로). 친화력 0 → Min, 100 → Max 선형.
        // 세계 교환 시도 하루 약 1,000~1,500건을 목표로 진단(Ambition Sim)으로 맞춘 개발 튜닝 상수다.
        public const float RumorExchangeChanceAtMinFriendliness = 0.30f;
        public const float RumorExchangeChanceAtMaxFriendliness = 0.50f;
        // 두 번째 상대 확률: 친화력 50 이하 0 → 100에서 Max 선형 (상대는 보통 1명, 친화력이 높으면 가끔 2명).
        public const float RumorSecondPeerChanceAtMaxFriendliness = 0.50f;
        // 받아들일 확률 = Base + PerCuriosity × 호기심/100 (§7.4, D74).
        public const float RumorAcceptChanceBase = 0.50f;
        public const float RumorAcceptChancePerCuriosity = 0.45f;
        // 소문 RNG salt (D77). 소유자 = Rest 중이던 NPC EntityId, 시각 = Rest 시작 시각.
        // 120 교환 발생(1번째 난수)·두 번째 상대(2번째 난수), 121 상대 고르기, 122+i 본인이 상대 i의 소문을 받을지,
        // 124+i 상대 i가 본인의 소문을 받을지 (i = 0,1). 126~129는 비워 둔 P2b 예비 번호 — 재사용 금지 (D85 파티는 130부터).
        public const int RumorExchangeSalt = 120;
        public const int RumorPeerPickSalt = 121;
        public const int RumorAcceptBySelfSaltBase = 122;
        public const int RumorAcceptByPeerSaltBase = 124;

        // ===== 원정 파티 자동 구성 (NPC_AMBITION_DESIGN §7.5, D85) =====
        // 모집 시도 확률 = 기본값 × 성향 배율(1 + clamp(친화력 항 + 동료 야망 항, ±DispositionMaxDeviation)) — D40과 같은 클램프.
        // 원정 중 파티 비율 30~50%를 목표로 진단(Ambition Sim)으로 맞춘 개발 튜닝 상수다 (실행 중 자동 보정 없음).
        public const float PartyRecruitAttemptBaseChance = 0.45f;
        public const float PartyRecruitFriendlinessDeviation = 0.10f;   // 친화력 0/100 → ∓/±
        public const float AmbitionComradePartyBonus = 0.15f;           // 동료 야망 (주 1.0 / 보조 0.5)
        // 수락 확률 = clamp(Base + 호감도/100 × Affinity + 관계(친구·형제·배우자 중 하나면 +CloseRelation, 중복 없음)
        //              + 용기 항 × Courage − (1 − 쉰 정도) × FreshRest, Min, Max).
        // 쉰 정도 = 휴식 경과 / 휴식 길이(활동 없음이면 1) — 방금 쉬기 시작한 NPC는 덜 따라간다.
        // TODO(P3): 리더 명성(Renown) 항 반영 예정 — 명성 도입 전까지 넣지 않는다.
        public const float PartyAcceptBaseChance = 0.47f;
        public const float PartyAcceptAffinityWeight = 0.45f;
        public const float PartyAcceptCloseRelationBonus = 0.10f;
        public const float PartyAcceptCourageDeviation = 0.10f;
        public const float PartyAcceptFreshRestPenalty = 0.25f;
        public const float PartyAcceptChanceMin = 0.05f;
        public const float PartyAcceptChanceMax = 0.90f;
        // 파티 모집 RNG salt (D85). 소유자 = 리더 EntityId, 시각 = 리더의 활동 결정 시각.
        // 130 모집 시도 롤, 131 수락 난수열(모집 점수순 후보마다 1개씩 차례로 뽑는다). 132~139는 파티 모집 예비.
        public const int PartyRecruitAttemptSalt = 130;
        public const int PartyAcceptSalt = 131;

        // NPC 간 절도 RNG salt (NPC_AMBITION_DESIGN D100). 소유자 = 도둑 EntityId, 시각 = 도둑의 Rest 시작 시각.
        // 170 시도 난수열(시도 확률 → 피해자 선택 → 물건 선택 순서로 같은 열에서 뽑는다), 171 발각. 172~179는 절도 예비.
        public const int TheftAttemptSalt = 170;
        public const int TheftDetectionSalt = 171;

        // 생활비 소멸 RNG salt (GOLD_BALANCE_DESIGN D132 B-주). 소유자 = NPC EntityId, 시각 = Rest 시작 시각.
        // 180 소멸액 소수 부분의 확률 반올림 1회. 181~189는 골드 흐름 예비.
        public const int LivingExpenseBurnSalt = 180;

        // 이주 RNG salt (NPC_AMBITION_DESIGN D140). 소유자 = NPC EntityId, 시각 = 이주 시작 시각.
        // 190 목적지 마을 안 도착 좌표(각도 → 거리). 191~199는 이주 예비.
        public const int RelocationArrivalSalt = 190;

        /// <summary>원정형 활동(Venturing + 왕복 경로 표현)인가. Hunt와 DungeonDelve.</summary>
        public static bool IsExpedition(NpcActivityKind kind)
        {
            return kind == NpcActivityKind.Hunt || kind == NpcActivityKind.DungeonDelve;
        }

        /// <summary>
        /// analytic 이동 경로를 쓰는 활동인가. 원정 + 이주(편도). 위치 갱신·화면 안 일시정지·경로 재계산이 이것을 본다.
        /// 파티·원정 판정은 IsExpedition을 쓴다 (이주는 파티를 만들지 않는다).
        /// </summary>
        public static bool HasTravelPath(NpcActivityKind kind)
        {
            return IsExpedition(kind) || kind == NpcActivityKind.Relocate;
        }

        /// <summary>
        /// 효용 기반 다음 활동 선택 (NPC_AMBITION_DESIGN §6, D39, D40, D56). Idle 전환 때만 호출한다.
        /// 점수 = 기본값 × 필요 배율 × 성향·야망 배율(±20% 클램프) × 기회(0이면 후보 제외).
        /// 후보 전체(Rest/Hunt/Trade/DungeonDelve)를 점수 가중 랜덤으로 고른다. 결정론 RNG salt 0.
        /// §6.3 원안의 "상위 3개"를 활동 4개에 그대로 쓰면 점수가 가장 낮은 Delve가 Trade가 있을 때마다 빠져
        /// D56의 5%를 맞출 수 없으므로 P2a는 전체 가중 랜덤을 유지한다.
        /// </summary>
        public static NpcActivityKind SelectNextActivity(NpcSaveData saveData, in NpcActivityChoiceContext context,
            int entityId, float startGameTime, int worldSeed)
        {
            HistoryRng rng = CreateRng(entityId, startGameTime, worldSeed, 0);
            NpcAmbitionProfile ambition = NpcAmbitionEvaluator.Evaluate(saveData.NpcStat);
            float restScore = GetActivityScore(NpcActivityKind.Rest, saveData.NpcStat, ambition, context);
            float huntScore = GetActivityScore(NpcActivityKind.Hunt, saveData.NpcStat, ambition, context);
            float tradeScore = GetActivityScore(NpcActivityKind.Trade, saveData.NpcStat, ambition, context);
            float delveScore = GetActivityScore(NpcActivityKind.DungeonDelve, saveData.NpcStat, ambition, context);
            float totalScore = restScore + huntScore + tradeScore + delveScore;
            if (totalScore <= 0f)
                return NpcActivityKind.Rest;

            // 최고 점수 선택이 아니라 가중 랜덤으로 선택해 같은 성향의 NPC도 행동 다양성을 갖는다.
            float pick = (float)(rng.NextDouble() * totalScore);
            if (pick < restScore)
                return NpcActivityKind.Rest;
            if (pick < restScore + huntScore)
                return NpcActivityKind.Hunt;
            if (pick < restScore + huntScore + tradeScore)
                return NpcActivityKind.Trade;
            return NpcActivityKind.DungeonDelve;
        }

        /// <summary>활동 하나의 효용 점수. 기회가 없는 활동(Trade 할 일 없음)은 0 — 후보에서 빠진다.</summary>
        public static float GetActivityScore(NpcActivityKind kind, in NpcStatComponent stat,
            in NpcAmbitionProfile ambition, in NpcActivityChoiceContext context)
        {
            float baseScore;
            float needMultiplier = 1f;
            switch (kind)
            {
                case NpcActivityKind.Rest:
                    baseScore = RestBaseScore;
                    if (context.RecentlyRecovered == true)
                        needMultiplier *= RecentRecoveryRestMultiplier;
                    break;
                case NpcActivityKind.Hunt:
                    baseScore = HuntBaseScore;
                    if (context.GoldShort == true)
                        needMultiplier *= GoldShortHuntMultiplier;
                    break;
                case NpcActivityKind.Trade:
                    if (context.TradeAvailable == false)
                        return 0f;
                    baseScore = TradeBaseScore;
                    if (context.GoldShort == true)
                        needMultiplier *= GoldShortTradeMultiplier;
                    if (context.BagFull == true)
                        needMultiplier *= BagFullTradeMultiplier;
                    break;
                case NpcActivityKind.DungeonDelve:
                    if (context.DelveAvailable == false)
                        return 0f;
                    baseScore = DelveBaseScore;
                    // "유망한 소문" 필요 배율 (§6.2, D56·D76)
                    if (context.HasPromisingRumor == true)
                        needMultiplier *= PromisingRumorDelveMultiplier;
                    break;
                default:
                    return 0f;
            }

            // 필요는 성향과 별개로 곱한다 (D40). 성향·야망 배율만 ±20%로 묶는다.
            return baseScore * needMultiplier * GetDispositionMultiplier(kind, stat, ambition);
        }

        /// <summary>
        /// 성향·야망이 활동 빈도에 주는 배율 (D40). 성격 수치 항 + 야망 항(주 1.0 / 보조 0.5)을 더한 뒤
        /// ±DispositionMaxDeviation(20%)으로 클램프한다. 겁 많은 NPC도 사냥을 꾸준히 나가도록 폭을 좁게 둔다.
        /// </summary>
        public static float GetDispositionMultiplier(NpcActivityKind kind, in NpcStatComponent stat,
            in NpcAmbitionProfile ambition)
        {
            float deviation = GetTraitDeviation(kind, stat)
                + GetAmbitionDeviation(kind, ambition.Primary)
                + GetAmbitionDeviation(kind, ambition.Secondary) * SecondaryAmbitionWeight;
            return 1f + Mathf.Clamp(deviation, -DispositionMaxDeviation, DispositionMaxDeviation);
        }

        /// <summary>
        /// 성격 수치 항. 수치 50을 0으로 두고 0/100에서 ±계수. 기존 선택식의 방향(인내 → 휴식, 용기·호기심 → 사냥)을
        /// 유지하고, 신규 Trade는 탐욕을 쓴다.
        /// </summary>
        private static float GetTraitDeviation(NpcActivityKind kind, in NpcStatComponent stat)
        {
            return kind switch
            {
                NpcActivityKind.Rest => CenteredTrait(stat.Patience) * RestPatienceDeviation,
                NpcActivityKind.Hunt => CenteredTrait(stat.Courage) * HuntCourageDeviation
                    + CenteredTrait(stat.Curiosity) * HuntCuriosityDeviation,
                NpcActivityKind.Trade => CenteredTrait(stat.Greed) * TradeGreedDeviation,
                NpcActivityKind.DungeonDelve => CenteredTrait(stat.Curiosity) * DelveCuriosityDeviation
                    + CenteredTrait(stat.Courage) * DelveCourageDeviation,
                _ => 0f,
            };
        }

        /// <summary>
        /// 야망 항 (§4.1 선호 수단·기피를 P1 활동 세트로 옮긴 것). 던전·소문은 P2 전까지 Hunt로 대신한다.
        /// </summary>
        private static float GetAmbitionDeviation(NpcActivityKind kind, NpcAmbitionKind ambition)
        {
            // 던전 원정 (D56): 탐험·명예는 더 자주, 안전은 덜. Hunt 항은 P1 그대로 둔다 (Hunt 비중 유지).
            if (kind == NpcActivityKind.DungeonDelve)
            {
                return ambition switch
                {
                    NpcAmbitionKind.Explorer => AmbitionExplorerDelveBonus,
                    NpcAmbitionKind.Glory => AmbitionGloryDelveBonus,
                    NpcAmbitionKind.Cautious => -AmbitionCautiousDelvePenalty,
                    _ => 0f,
                };
            }

            switch (ambition)
            {
                case NpcAmbitionKind.Explorer:
                case NpcAmbitionKind.Glory:
                    // 던전·강한 목표 선호 → Hunt. 오래 쉬기·저위험 기피 → Rest 소폭 감소.
                    return kind == NpcActivityKind.Hunt ? AmbitionHuntBonus
                        : kind == NpcActivityKind.Rest ? -AmbitionRestPenalty : 0f;
                case NpcAmbitionKind.Wealth:
                    // 되팔기·저가 매수 선호 → Trade.
                    return kind == NpcActivityKind.Trade ? AmbitionWealthTradeBonus : 0f;
                case NpcAmbitionKind.Comrade:
                    // 파티 원정 선호 → Hunt 약간 (파티 구성 로직은 그대로).
                    return kind == NpcActivityKind.Hunt ? AmbitionComradeHuntBonus : 0f;
                case NpcAmbitionKind.Opportunist:
                    return kind == NpcActivityKind.Trade ? AmbitionOpportunistTradeBonus : 0f;
                case NpcAmbitionKind.Cautious:
                    // 저위험 선호 → Rest, 강한 목표 기피 → Hunt 소폭 감소.
                    return kind == NpcActivityKind.Rest ? AmbitionCautiousRestBonus
                        : kind == NpcActivityKind.Hunt ? -AmbitionCautiousHuntPenalty : 0f;
                default:
                    return 0f;
            }
        }

        private static float CenteredTrait(int value)
        {
            return Mathf.Clamp((value - 50) / 50f, -1f, 1f);
        }

        public static float GetActivityDurationHours(NpcActivityKind kind)
        {
            return kind switch
            {
                NpcActivityKind.Hunt => HuntDurationHours,
                NpcActivityKind.Trade => TradeDurationHours,
                NpcActivityKind.DungeonDelve => DelveDurationHours,
                _ => RestDurationHours,
            };
        }

        /// <summary>"부상 회복 직후" 필요 (§6.2). 회복 종료 후 RecentRecoveryHours 안이면 true.</summary>
        public static bool IsRecentlyRecovered(NpcActivityState activity, float gameTime)
        {
            return activity.RecoveredGameTime >= 0f && gameTime >= activity.RecoveredGameTime &&
                   gameTime - activity.RecoveredGameTime < RecentRecoveryHours;
        }

        /// <summary>
        /// NpcPower(장비 점수 + 스킬 점수, NpcPowerEvaluator)를 기존 Hunt 판정 곡선의 capability(0~1)로 옮긴다.
        /// 솔로·파티 판정과 Food 보상이 모두 이 값 하나를 쓴다 (NPC_AMBITION_DESIGN §5.1, D31, D32).
        /// </summary>
        public static float GetHuntCapability(float npcPower)
        {
            float powerRatio = npcPower / HuntReferencePower;
            return Mathf.Clamp01(HuntCapabilityAtReference + (powerRatio - 1f) * HuntCapabilityPerPowerRatio);
        }

        /// <summary>
        /// 솔로 Hunt 판정 확률. 사망 + 부상(= 피해 확률)은 교체 전 곡선 그대로이고, 사망은 거의 0으로 낮춰
        /// 줄인 만큼을 부상으로 옮겼다 — 같은 롤에서 성공·실패·Food는 바뀌지 않는다. capability가 높을수록 사망이 낮다.
        /// </summary>
        public static void GetSoloHuntChances(float capability, out float deathChance, out float woundedChance,
            out float successChance)
        {
            capability = Mathf.Clamp01(capability);
            float harmChance = Mathf.Lerp(SoloHuntHarmChanceAtZero, SoloHuntHarmChanceAtFull, capability);
            deathChance = Mathf.Lerp(SoloHuntDeathChanceAtZero, SoloHuntDeathChanceAtFull, capability);
            woundedChance = harmChance - deathChance;
            successChance = Mathf.Lerp(0.40f, 0.86f, capability);
        }

        /// <summary>파티원 Hunt 판정 확률. 솔로와 같은 방식으로 사망을 부상으로 옮겼다.</summary>
        public static void GetPartyMemberHuntChances(float capability, out float deathChance, out float woundedChance,
            out float successChance)
        {
            capability = Mathf.Clamp01(capability);
            float harmChance = Mathf.Lerp(PartyHuntHarmChanceAtZero, PartyHuntHarmChanceAtFull, capability);
            deathChance = Mathf.Lerp(PartyHuntDeathChanceAtZero, PartyHuntDeathChanceAtFull, capability);
            woundedChance = harmChance - deathChance;
            successChance = Mathf.Lerp(0.48f, 0.92f, capability);
        }

        /// <summary>파티원 판정 capability = 개인 70% + 나머지 파티원 평균 30%.</summary>
        public static float GetPartyMemberCapability(float personalPower, float partyPower, int partySize)
        {
            float supportPower = partySize > 1
                ? Mathf.Clamp01((partyPower - personalPower) / (partySize - 1))
                : 0f;
            return Mathf.Clamp01(personalPower * 0.70f + supportPower * 0.30f);
        }

        public static NpcHuntOutcome ResolveHunt(float capability, int entityId,
            float startGameTime, int worldSeed)
        {
            HistoryRng rng = CreateRng(entityId, startGameTime, worldSeed, 1);
            GetSoloHuntChances(capability, out float deathChance, out float woundedChance, out float successChance);
            float roll = (float)rng.NextDouble();

            // 심각한 결과부터 먼저 소비한다. 남은 확률은 평범한 실패로 귀결된다.
            if (roll < deathChance)
                return NpcHuntOutcome.Dead;
            if (roll < deathChance + woundedChance)
                return NpcHuntOutcome.Wounded;
            if (roll < deathChance + woundedChance + successChance)
                return NpcHuntOutcome.Success;
            return NpcHuntOutcome.Failed;
        }

        /// <summary>성공한 Hunt가 마을에 가져오는 식량 보상. 개인 전리품은 RollHuntLoot가 따로 만든다.</summary>
        public static int GetHuntFoodYield(float capability, int entityId,
            float startGameTime, int worldSeed)
        {
            HistoryRng rng = CreateRng(entityId, startGameTime, worldSeed, 2);
            int baseYield = capability >= HuntFoodHighCapability ? 3 : capability >= HuntFoodMidCapability ? 2 : 1;
            return baseYield + (rng.Chance(0.20d) ? 1 : 0);
        }

        /// <summary>파티 전투력 = 파티원 capability 합. 파티 Food 보상과 지원 전투력 계산에 쓴다.</summary>
        public static float GetPartyHuntPower(IReadOnlyList<float> memberCapabilities)
        {
            float power = 0f;
            for (int i = 0; i < memberCapabilities.Count; i++)
                power += memberCapabilities[i];
            return power;
        }

        public static NpcHuntOutcome ResolvePartyMemberHunt(float personalPower, float partyPower,
            int partySize, int partyId, float startGameTime, int worldSeed, int memberIndex)
        {
            HistoryRng rng = CreateRng(partyId, startGameTime, worldSeed, 10 + memberIndex);
            float capability = GetPartyMemberCapability(personalPower, partyPower, partySize);
            GetPartyMemberHuntChances(capability, out float deathChance, out float woundedChance,
                out float successChance);
            float roll = (float)rng.NextDouble();

            if (roll < deathChance)
                return NpcHuntOutcome.Dead;
            if (roll < deathChance + woundedChance)
                return NpcHuntOutcome.Wounded;
            if (roll < deathChance + woundedChance + successChance)
                return NpcHuntOutcome.Success;
            return NpcHuntOutcome.Failed;
        }

        public static int GetPartyHuntFoodYield(float partyPower, int partyId, float startGameTime,
            int worldSeed)
        {
            HistoryRng rng = CreateRng(partyId, startGameTime, worldSeed, 20);
            int baseYield = Mathf.Clamp(Mathf.RoundToInt(partyPower * 2f), 2, 6);
            return baseYield + (rng.Chance(0.25d) ? 1 : 0);
        }

        /// <summary>
        /// 성공한 Hunt의 개인 전리품 1회 롤 (§7.1, D33, D35). Hunt는 골드를 주지 않는다.
        /// DropTable 1번의 카테고리 비율(화폐·장비·스킬북·스킬 페이지, Nothing 제외)과 레벨 풀을 재사용하고,
        /// 모든 선택과 아이템 롤을 결정론 RNG(rngOwnerId, 시작시각, worldSeed, salt)로 수행한다.
        /// 같은 입력이면 같은 아이템 내용이 나온다 (ItemInstanceId만 새로 발급된다).
        /// </summary>
        /// <param name="rngOwnerId">솔로는 NPC EntityId, 파티는 PartyId</param>
        /// <param name="salt">솔로 HuntLootSalt, 파티원 PartyHuntLootSaltBase + memberIndex</param>
        /// <param name="zoneLevel">기준 레벨 = 소속 마을 청크의 MapManager.GetZone (월드 몬스터 레벨과 같은 기준)</param>
        public static ItemData? RollHuntLoot(int rngOwnerId, float startGameTime, int worldSeed, int salt,
            int zoneLevel)
        {
            DropTable? dropTable = AR.s.Data?.GetDrop(HuntLootDropTableId);
            if (dropTable == null || AR.s.Item == null)
                return null;

            HistoryRng rng = CreateRng(rngOwnerId, startGameTime, worldSeed, salt);
            int currencyRate = Mathf.Max(0, dropTable.CurrencyRate);
            int equipmentRate = Mathf.Max(0, dropTable.EquipmentRate);
            int skillBookRate = Mathf.Max(0, dropTable.SkillBookRate);
            int skillPageRate = Mathf.Max(0, dropTable.SkillPageRate);
            int totalRate = currencyRate + equipmentRate + skillBookRate + skillPageRate;
            if (totalRate <= 0)
                return null;

            int pick = rng.Range(0, totalRate);
            if (pick < currencyRate)
            {
                int itemId = dropTable.CurrencyPoolMode == 1
                    ? DropPoolBuilder.SelectWeightedRandom(DropPoolBuilder.GetCurrencyPool(zoneLevel), 0, rng)
                    : SelectFromDropList(AR.s.Data?.GetDropCurrency(dropTable.CurrencyId)?.DropList, rng);
                return itemId > 0 ? AR.s.Item.CreateInventoryItemData(itemId, 1, rng) : null;
            }

            pick -= currencyRate;
            if (pick < equipmentRate)
            {
                int itemId = dropTable.EquipmentPoolMode == 1
                    ? DropPoolBuilder.SelectWeightedRandom(DropPoolBuilder.GetEquipmentPool(zoneLevel), 0, rng)
                    : SelectFromDropList(AR.s.Data?.GetDropEquipment(dropTable.EquipmentId)?.DropList, rng);
                return itemId > 0 ? AR.s.Item.CreateInventoryItemData(itemId, 1, rng) : null;
            }

            pick -= equipmentRate;
            if (pick < skillBookRate)
                return AR.s.Item.CreateRandomSkillBookOfTier(dropTable.Tier, rng);

            return AR.s.Item.CreateRandomSkillPageOfTier(dropTable.Tier, rng);
        }

        /// <summary>DropHelper의 Explicit 모드 선택을 결정론 RNG로 옮긴 것. 기존 DropHelper 동작은 바꾸지 않는다.</summary>
        private static int SelectFromDropList(List<DropInfo>? dropList, HistoryRng rng)
        {
            if (dropList == null || dropList.Count == 0)
                return 0;

            int totalRate = 0;
            for (int i = 0; i < dropList.Count; i++)
                totalRate += Mathf.Max(0, dropList[i].Rate);
            if (totalRate <= 0)
                return 0;

            int pick = rng.Range(0, totalRate);
            int cumulative = 0;
            for (int i = 0; i < dropList.Count; i++)
            {
                cumulative += Mathf.Max(0, dropList[i].Rate);
                if (pick < cumulative)
                    return dropList[i].Id;
            }
            return 0;
        }

        /// <summary>
        /// 치료비를 낼 수 있는 부상 NPC가 실제로 치료를 살 확률 (D36). 치료 구매 확률 계산은 이 함수 한 곳에 모은다 —
        /// P1c의 야망 가중은 여기에 덧붙인다.
        /// </summary>
        public static float GetTreatmentBuyChance(NpcSaveData saveData)
        {
            float greed = Mathf.Clamp01(saveData.NpcStat.Greed / 100f);
            float chance = Mathf.Lerp(TreatmentBuyChanceAtNoGreed, TreatmentBuyChanceAtMaxGreed, greed);
            // P1c 야망 가중 (D36): 명예·탐험은 빨리 원정에 복귀하려 거의 항상 사고, 부는 돈을 아낀다. 주 1.0 / 보조 0.5.
            NpcAmbitionProfile ambition = NpcAmbitionEvaluator.Evaluate(saveData.NpcStat);
            chance += GetTreatmentAmbitionBonus(ambition.Primary)
                + GetTreatmentAmbitionBonus(ambition.Secondary) * SecondaryAmbitionWeight;
            return Mathf.Clamp(chance, TreatmentBuyChanceMin, TreatmentBuyChanceMax);
        }

        private static float GetTreatmentAmbitionBonus(NpcAmbitionKind ambition)
        {
            return ambition switch
            {
                NpcAmbitionKind.Glory => TreatmentGloryExplorerBonus,
                NpcAmbitionKind.Explorer => TreatmentGloryExplorerBonus,
                NpcAmbitionKind.Cautious => TreatmentCautiousBonus,
                NpcAmbitionKind.Wealth => -TreatmentWealthPenalty,
                _ => 0f,
            };
        }

        /// <summary>치료 구매 결정론 판정. 같은 (소유자, 활동 시작시각, worldSeed, salt)는 세이브/로드 뒤에도 같은 결과.</summary>
        public static bool RollTreatmentPurchase(float buyChance, int rngOwnerId, float startGameTime, int worldSeed,
            int salt)
        {
            HistoryRng rng = CreateRng(rngOwnerId, startGameTime, worldSeed, salt);
            return rng.NextDouble() < Mathf.Clamp01(buyChance);
        }

        /// <summary>
        /// 중상 결정론 판정 (D91 ②). 결과 판정과 따로 만든 RNG를 쓰므로 호출 순서와 무관하게 다른 판정의 난수열을 바꾸지 않는다.
        /// </summary>
        public static bool RollSevereWound(int rngOwnerId, float startGameTime, int worldSeed, int salt)
        {
            HistoryRng rng = CreateRng(rngOwnerId, startGameTime, worldSeed, salt);
            return rng.NextDouble() < SevereWoundChance;
        }

        /// <summary>부상 등급·치료 여부에 따른 회복 시간 (D36·D91).</summary>
        public static float GetWoundRecoveryHours(bool severe, bool treated)
        {
            if (severe == true)
                return treated == true ? SevereWoundTreatedRecoveryHours : SevereWoundRecoveryHours;
            return treated == true ? TreatedRecoveryHours : WoundedRecoveryHours;
        }

        /// <summary>솔로 Delve 판정 확률. Hunt보다 성공은 낮고 부상은 많다. 사망은 솔로 Hunt와 같은 D48 곡선.</summary>
        public static void GetSoloDelveChances(float capability, out float deathChance, out float woundedChance,
            out float successChance)
        {
            capability = Mathf.Clamp01(capability);
            float harmChance = Mathf.Lerp(SoloDelveHarmChanceAtZero, SoloDelveHarmChanceAtFull, capability);
            deathChance = Mathf.Lerp(SoloHuntDeathChanceAtZero, SoloHuntDeathChanceAtFull, capability);
            woundedChance = harmChance - deathChance;
            successChance = Mathf.Lerp(SoloDelveSuccessChanceAtZero, SoloDelveSuccessChanceAtFull, capability);
        }

        /// <summary>파티원 Delve 판정 확률. 사망은 파티원 Hunt와 같은 D48 곡선.</summary>
        public static void GetPartyMemberDelveChances(float capability, out float deathChance, out float woundedChance,
            out float successChance)
        {
            capability = Mathf.Clamp01(capability);
            float harmChance = Mathf.Lerp(PartyDelveHarmChanceAtZero, PartyDelveHarmChanceAtFull, capability);
            deathChance = Mathf.Lerp(PartyHuntDeathChanceAtZero, PartyHuntDeathChanceAtFull, capability);
            woundedChance = harmChance - deathChance;
            successChance = Mathf.Lerp(PartyDelveSuccessChanceAtZero, PartyDelveSuccessChanceAtFull, capability);
        }

        public static NpcHuntOutcome ResolveDelve(float capability, int entityId, float startGameTime, int worldSeed)
        {
            HistoryRng rng = CreateRng(entityId, startGameTime, worldSeed, DelveOutcomeSalt);
            GetSoloDelveChances(capability, out float deathChance, out float woundedChance, out float successChance);
            return PickOutcome((float)rng.NextDouble(), deathChance, woundedChance, successChance);
        }

        /// <summary>파티원 Delve 판정. capability는 Hunt처럼 개인 70% + 나머지 파티원 평균 30% (동료 지원).</summary>
        public static NpcHuntOutcome ResolvePartyMemberDelve(float personalPower, float partyPower, int partySize,
            int partyId, float startGameTime, int worldSeed, int memberIndex)
        {
            HistoryRng rng = CreateRng(partyId, startGameTime, worldSeed, PartyDelveOutcomeSaltBase + memberIndex);
            float capability = GetPartyMemberCapability(personalPower, partyPower, partySize);
            GetPartyMemberDelveChances(capability, out float deathChance, out float woundedChance,
                out float successChance);
            return PickOutcome((float)rng.NextDouble(), deathChance, woundedChance, successChance);
        }

        /// <summary>
        /// NPC 던전 완료 확률 (D55). capability가 평범한 NPC 수준(0.475) 미만이면 0, 0.475에서 3%, 1.0에서 8%로 선형 증가.
        /// </summary>
        public static float GetDelveCompletionChance(float capability)
        {
            if (capability < DelveCompletionMinCapability)
                return 0f;
            float t = Mathf.Clamp01((capability - DelveCompletionMinCapability) / (1f - DelveCompletionMinCapability));
            return Mathf.Lerp(DelveCompletionChanceAtMin, DelveCompletionChanceAtFull, t);
        }

        public static bool RollDelveCompletion(float chance, int rngOwnerId, float startGameTime, int worldSeed, int salt)
        {
            if (chance <= 0f)
                return false;
            HistoryRng rng = CreateRng(rngOwnerId, startGameTime, worldSeed, salt);
            return rng.NextDouble() < Mathf.Clamp01(chance);
        }

        /// <summary>원정 대상 슬롯 인덱스를 결정론적으로 고른다 (소속 마을 슬롯 중 균등).</summary>
        public static int PickDelveSlotIndex(int rngOwnerId, float startGameTime, int worldSeed, int slotCount)
        {
            if (slotCount <= 1)
                return 0;
            HistoryRng rng = CreateRng(rngOwnerId, startGameTime, worldSeed, DelveTargetSalt);
            return rng.Range(0, slotCount);
        }

        private static NpcHuntOutcome PickOutcome(float roll, float deathChance, float woundedChance, float successChance)
        {
            if (roll < deathChance)
                return NpcHuntOutcome.Dead;
            if (roll < deathChance + woundedChance)
                return NpcHuntOutcome.Wounded;
            if (roll < deathChance + woundedChance + successChance)
                return NpcHuntOutcome.Success;
            return NpcHuntOutcome.Failed;
        }

        /// <summary>
        /// Hunt 대상 좌표를 결정론적으로 고른다. 실제 경로 탐색은 하지 않고 오프스크린 보간용 종점만 만든다.
        /// </summary>
        public static Vector2 GetHuntTargetPosition(Vector2 origin, int entityId, float startGameTime,
            int worldSeed, float minX, float maxX, float minY, float maxY)
        {
            HistoryRng rng = CreateRng(entityId, startGameTime, worldSeed, 3);
            float angle = (float)(rng.NextDouble() * Mathf.PI * 2f);
            float distance = Mathf.Lerp(8f, 16f, (float)rng.NextDouble());
            Vector2 target = origin + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * distance;
            target.x = Mathf.Clamp(target.x, minX, maxX);
            target.y = Mathf.Clamp(target.y, minY, maxY);
            return target;
        }

        /// <summary>
        /// Hunt 단일 구간의 analytic 좌표. 전반은 사냥 지점으로, 후반은 출발지로 돌아온다.
        /// </summary>
        public static Vector2 EvaluateHuntPosition(NpcActivityState activity, float gameTime)
        {
            if (activity.HasMovementPath == false || activity.PhaseEndGameTime <= activity.PhaseStartedGameTime)
                return activity.OriginPosition;

            float progress = Mathf.Clamp01((gameTime - activity.PhaseStartedGameTime) /
                (activity.PhaseEndGameTime - activity.PhaseStartedGameTime));
            float outboundFraction = Mathf.Clamp01(activity.OutboundFraction);
            if (outboundFraction <= 0f)
                return Vector2.Lerp(activity.TargetPosition, activity.ReturnPosition, progress);
            if (outboundFraction >= 1f || progress <= outboundFraction)
                return Vector2.Lerp(activity.OriginPosition, activity.TargetPosition, progress / outboundFraction);
            return Vector2.Lerp(activity.TargetPosition, activity.ReturnPosition,
                (progress - outboundFraction) / (1f - outboundFraction));
        }

        /// <summary>소문 교환이 일어날 확률 (D74). 친화력 선형.</summary>
        public static float GetRumorExchangeChance(int friendliness)
        {
            return Mathf.Lerp(RumorExchangeChanceAtMinFriendliness, RumorExchangeChanceAtMaxFriendliness,
                Mathf.Clamp01(friendliness / 100f));
        }

        /// <summary>두 번째 교환 상대 확률 (D74). 친화력 50 이하 0.</summary>
        public static float GetRumorSecondPeerChance(int friendliness)
        {
            return Mathf.Clamp01((friendliness - 50) / 50f) * RumorSecondPeerChanceAtMaxFriendliness;
        }

        /// <summary>건네받은 소문을 받아들일 확률 (§7.4, D74). 호기심 선형.</summary>
        public static float GetRumorAcceptChance(int curiosity)
        {
            return RumorAcceptChanceBase + RumorAcceptChancePerCuriosity * Mathf.Clamp01(curiosity / 100f);
        }

        /// <summary>
        /// 리더의 모집 시도 확률 (D85 ②). 친화력과 동료 야망을 약하게 반영하고 성향 영향은 ±20%로 묶는다 (D40).
        /// </summary>
        public static float GetPartyRecruitAttemptChance(in NpcStatComponent stat)
        {
            NpcAmbitionProfile ambition = NpcAmbitionEvaluator.Evaluate(stat);
            float deviation = CenteredTrait(stat.Friendliness) * PartyRecruitFriendlinessDeviation;
            if (ambition.Primary == NpcAmbitionKind.Comrade)
                deviation += AmbitionComradePartyBonus;
            if (ambition.Secondary == NpcAmbitionKind.Comrade)
                deviation += AmbitionComradePartyBonus * SecondaryAmbitionWeight;
            float multiplier = 1f + Mathf.Clamp(deviation, -DispositionMaxDeviation, DispositionMaxDeviation);
            return Mathf.Clamp01(PartyRecruitAttemptBaseChance * multiplier);
        }

        /// <summary>
        /// 후보의 수락 확률 (D85 ③). 리더와의 호감도(−100~100)·친밀 관계(친구·형제·배우자)·용기·쉰 정도(0~1)를 반영한다.
        /// P3에서 리더 명성(Renown) 항이 추가될 예정이다.
        /// </summary>
        public static float GetPartyAcceptChance(int affinity, bool hasCloseRelation, int courage, float restedFraction)
        {
            float chance = PartyAcceptBaseChance
                + Mathf.Clamp(affinity / 100f, -1f, 1f) * PartyAcceptAffinityWeight
                + (hasCloseRelation == true ? PartyAcceptCloseRelationBonus : 0f)
                + CenteredTrait(courage) * PartyAcceptCourageDeviation
                - (1f - Mathf.Clamp01(restedFraction)) * PartyAcceptFreshRestPenalty;
            return Mathf.Clamp(chance, PartyAcceptChanceMin, PartyAcceptChanceMax);
        }

        /// <summary>
        /// 활동 결정론 RNG. 같은 (소유자, 활동 시작시각, worldSeed, salt)는 세이브/로드 뒤에도 같은 난수열을 준다.
        /// 소문 교환(NpcManager_Rumor)도 이 채널을 쓴다 (salt 120~125). 파티 모집(NpcManager_Party)은 130·131. 중상 판정은 140·141·150+i·160+i.
        /// NPC 간 절도(NpcManager_Theft)는 170·171.
        /// </summary>
        internal static HistoryRng CreateRng(int entityId, float startGameTime, int worldSeed, int salt)
        {
            // 활동 시작시각을 1/100 게임시간 단위로 양자화한다.
            // 같은 월드·NPC·활동 시작시각·판정 종류는 세이브/로드 뒤에도 같은 난수열을 얻는다.
            int startTick = Mathf.FloorToInt(startGameTime * 100f);
            // salt=0은 활동 선택, salt=1은 Hunt 결과, salt=2는 보상, salt=3은 목표 좌표, salt=4는 개인 전리품,
            // salt=5는 솔로 치료 구매, 10+i는 파티원 결과, 20은 파티 보상, 30+i는 파티원 개인 전리품,
            // 40+i는 파티원 치료 구매, 50·51·53~56·60+i·70+i·90+i·100·110+i는 DungeonDelve(Delve*Salt 상수)에 사용한다.
            // 52·80+i는 폐기된 Delve 희귀 보너스 롤 번호 — reserved, 재사용 금지.
            // 120~125는 소문 교환(Rumor*Salt 상수). 126~129는 비워 둔 P2b 예비 번호 — 재사용 금지.
            // 130은 파티 모집 시도, 131은 파티 수락 난수열(리더 EntityId·리더 결정 시각 기준, PartyRecruit*/PartyAccept* 상수).
            // 132~139는 파티 모집 예비 — 재사용 금지.
            // 140은 솔로 Hunt 중상, 141은 솔로 Delve 중상 (EntityId 기준), 150+i는 파티원 Hunt 중상, 160+i는 파티원 Delve 중상
            // (PartyId 기준, SevereWound*Salt 상수, D91). 142~149는 부상 예비.
            // 170은 절도 시도 난수열(시도 확률 → 피해자 → 물건), 171은 절도 발각 (도둑 EntityId·도둑의 Rest 시작 시각 기준,
            // Theft*Salt 상수, D100). 172~179는 절도 예비 — 재사용 금지.
            int key = unchecked(worldSeed ^ (entityId * 486187739) ^ (startTick * 16777619) ^ salt);
            return new HistoryRng(DerivedSeed.Derive(key, DerivedSeed.ChannelNpcActivity));
        }
    }
}
