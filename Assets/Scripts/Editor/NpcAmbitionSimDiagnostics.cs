#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using ARPG.Creature;
using ARPG.Data;
using ARPG.Dungeon;
using ARPG.History;
using ARPG.Npc;
using ARPG.Npc.Narrative;
using ARPG.Village;
using Newtonsoft.Json;
using UnityEditor;
using UnityEngine;

namespace ARPG.EditorTools
{
    /// <summary>
    /// NPC 야망·모험가 경제 튜닝 진단 (NPC_AMBITION_DESIGN §10.1, D40, D43). 플레이 모드(GameScene) 전용.
    ///
    /// 현재 NPC·상점·마을·파티 상태를 JSON으로 복제하고, NpcManager/VillageManager/TimeManager의 원장 필드를
    /// 한 번의 동기 호출 동안만 복제본으로 바꿔 끼운 채 N게임일을 오프스크린으로 시뮬레이션한 뒤 CSV로 출력한다.
    /// - 활동 선택(SelectNextActivity)·Trade 기회·거래(TradeAtShop)·치료·전리품·자동 장착·파티 구성·파티 호감도 변화
    ///   (ApplyPartyRelationshipOutcome)·Rest 종료 처리(ResolveRestEnd — 소문 교환·절도 P4a)는 실제 매니저 코드를 그대로 호출한다.
    ///   절도는 적용 직전 관찰자로 상태를 찍고 끝난 뒤 복제본을 직접 읽어 독립 감사한다 (VerifyTheftRules는 강제 검증).
    /// - NPC 호감도·유형 관계도 복제본으로 바꿔 끼우고 라이브 해시에 포함한다 (D107).
    /// - System_NpcSim의 상태 전이 루프는 여기서 같은 순서로 재현한다 (1게임시간 해상도). 사망 정리 중 연대기·Deed·고인 기억,
    ///   화면 안 연출, 전역 인구 보충(TryReplenishGlobalPopulation), 마을 건설 소비는 시뮬레이션하지 않는다.
    ///   마을 인구 자원 생산(VillageResourceSimulation.AdvanceWholeHours)은 모든 마을에 시간당 1회 적용한다
    ///   (금고 유지비 포함 — 인구 0인 사라진 터는 유지비만, GOLD_BALANCE D132·D135).
    /// - 시뮬레이션은 한 프레임 안에서 끝나므로 그 사이 다른 시스템·저장이 돌지 않는다. 끝나면 모든 필드를 되돌리고
    ///   실행 전후 라이브 상태 해시가 같은지 검증한다. 시간·아이템 인스턴스 카운터·UnityEngine.Random 상태도 되돌린다.
    /// 출력: {프로젝트}/Logs/NpcAmbitionSim/ (.gitignore의 /Logs/ — git에 잡히지 않음)
    /// </summary>
    public static partial class NpcAmbitionSimDiagnostics
    {
        private const float PowerEpsilon = 0.0001f;
        private const float CowardFraction = 0.10f;
        private const int MaxExampleCases = 5;
        private const int MaxPhaseTransitionsPerNpc = 4;
        private const int DeedKindCount = (int)NpcDeedKind.TheftExposed + 1;
        private const int MaxTheftAuditFailures = 8;
        private const int MaxTopicExamplesPerKind = 4;

        [MenuItem("ARPG/Npc/Diagnostics/Ambition Sim (30 days)", false, 431)]
        public static void Run30Days() => Run(30);

        [MenuItem("ARPG/Npc/Diagnostics/Ambition Sim (7 days)", false, 432)]
        public static void Run7Days() => Run(7);

        [MenuItem("ARPG/Npc/Diagnostics/Verify Rumor Rules", false, 434)]
        public static void RunVerifyRumorRules() => VerifyRumorRules();

        [MenuItem("ARPG/Npc/Diagnostics/Verify Theft Rules", false, 435)]
        public static void RunVerifyTheftRules() => VerifyTheftRules();

        // 전파 곡선 측정 시점 (사건 뒤 게임시간)
        private static readonly float[] PropagationCheckpointHours = { 12f, 24f, 48f, 72f };
        // 전파 곡선 분류: 0 원정 성공, 1 완료 새 세대, 2 장비 소유, 3 판매(P4b), 4 배신(P4b)
        private const int RumorCategoryCount = 5;
        private static readonly string[] RumorCategoryNames = { "원정 성공", "완료 새 세대", "장비 소유", "판매", "배신" };

        // P4-0 절도 조건 깔때기 (D99·D100) — 실제 절도 판정 직전 상태에서 조건 충족 수만 잰다 (P4a 이후에도 비교용으로 유지).
        // 가상 시도에 쓸 기본 시도 확률 후보 (D100 "기본 약 25%"의 튜닝 범위 추정용)
        // GOLD_BALANCE D136: 생활비로 가방 빈칸이 늘어 5% 아래도 필요해 1~4%를 더했다.
        private static readonly float[] TheftBaseChances = { 0.01f, 0.02f, 0.03f, 0.04f, 0.05f, 0.10f, 0.15f, 0.20f, 0.25f, 0.35f, 0.50f };
        // D100 쿨다운: 도둑 168h(시도 기준), 같은 마을 72h. 깔때기의 가정 쿨다운은 게임 필드와 별개의 진단 표로 센다.
        private const float TheftThiefCooldownHours = NpcManager.TheftThiefCooldownHours;
        private const float TheftVillageCooldownHours = NpcManager.TheftVillageCooldownHours;
        // D100 절도 시도 salt 170. 가상 시도는 같은 RNG 채널로 굴리기만 하고 상태는 바꾸지 않는다.
        private const int TheftAttemptSalt = NpcActivityResolver.TheftAttemptSalt;
        // 가방 아이템 가치(BasePrice × 수량) 분포 구간 상한: 0 / 1-9 / 10-19 / 20-49 / 50-99 / 100-199 / 200+
        private static readonly int[] BagValueBucketUpper = { 0, 9, 19, 49, 99, 199, int.MaxValue };
        private static readonly string[] BagValueBucketLabels = { "0", "1-9", "10-19", "20-49", "50-99", "100-199", "200+" };
        // 관계 지표에 적는 유형 관계 종류 (순서 = 출력 순서)
        private static readonly NpcRelationType[] ReportedRelationTypes =
        {
            NpcRelationType.GrudgeAgainst, NpcRelationType.Rival, NpcRelationType.Friend, NpcRelationType.Spouse,
            NpcRelationType.Sibling, NpcRelationType.ParentOf, NpcRelationType.MentorOf,
        };

        private sealed class DayRow
        {
            public int Day;
            public float GameTime;
            public int Living;
            public float AvgPower;
            public float CohortAvgPower;
            public float SurvivorStartAvgPower;
            public float P98Power;
            public int CapExceed;
            public float MaxCapRatio;
            public int UpgradesLoot;
            public int UpgradesTrade;
            public int SelectRest;
            public int SelectHunt;
            public int SelectTrade;
            public int SelectDelve;
            public int HuntOutcomes;
            public int HuntSuccess;
            public int DelveOutcomes;       // 파티원 개인 판정 포함
            public int DelveSuccess;
            public int DelveCompletionRolls;   // 완료 확률 롤 통과 (보호 규칙 전)
            public int DelveDungeonChangedFailures;   // 원정 중 목표 던전 변경으로 Success → 실패 전환 (D67 개정)
            public int NpcCompletions;
            public int UpgradesDelve;
            public int Wounded;
            public int SevereWounds;        // 그중 중상 (D91)
            public int Treatments;
            public int Deaths;
            public double ExpectedDeaths;   // 판정마다 사망 확률을 더한 기대값 — 고정 시드 한 번의 표본 변동을 걸러 본다
            public long GoldBurned;
            public long VillageGold;
            public long NpcGold;
            public long PopulationGold;
            public int FoodInflow;
            public int FoodYieldRaw;
            public int TradesExecuted;
            public int Sold;
            public long SoldGold;
            public int SellRejected;
            public int Bought;
            public long BoughtGold;
            // 소문 (P2b)
            public long RumorExchangeAttempts;
            public long RumorTransfers;
            public long RumorsHeld;
            public int RumorHolders;
            public int PromisingHolders;
            public int StoredInvalid;
            public int ConfirmationDelves;
            // GOLD_BALANCE: 물가 지수(기록 시각)·생활비·금고 유지비 (D126·D131·D132)
            public float GoldPriceTarget;
            public float GoldPriceIndex;
            public long LivingSpendCount;
            public long LivingSpendDeposit;
            public long LivingSpendBurn;
            public long VaultUpkeepBurn;
        }

        /// <summary>전파 곡선 추적용 사건 1건 (직접 소문이 처음 생긴 사건).</summary>
        private sealed class RumorEventTrack
        {
            public NpcRumor Template = null!;
            public int VillageId;
            public int Category;   // 0 = 원정 성공, 1 = 완료(새 세대), 2 = 장비 소유
            public int NextCheckpoint;
        }

        private sealed class VillageStats
        {
            public int VillageId;
            public bool HasShop;
            public int NpcStart;
            public int NpcEnd;
            public int GoldStart;
            public int GoldEnd;
            public int Sold;
            public long SoldGold;
            public int SellRejected;
            public int Bought;
            public long BoughtGold;
            public int Treatments;
            public int FoodInflow;
            public long PopulationGold;
            public int DungeonSlots;
            public int Delves;
            public int NpcCompletions;
        }

        /// <summary>NPC 가방 통계 (P4-0). 생존 NPC의 개인 인벤토리만 본다 (장착품 제외).</summary>
        private sealed class BagSnapshot
        {
            public bool Captured;
            public int Living;
            public readonly int[] UsedSlotHistogram = new int[GlobalEnum.NPC_INVENTORY_SLOTCOUNT_MAX + 1];
            public int Items;
            public int Stealable;            // 비스택·인스턴스 있음 = 훔칠 수 있는 물건 (D99)
            public int StealableSellable;    // 그중 소속 마을 상점이 매입하는 것 (장물 판매 경로)
            public int Sellable;             // GetNpcSellPrice ≥ 0
            public int Unsellable;           // 매각 불가 (BasePrice 0·SellRatio 0 등)
            public int NoVillage;            // 소속 마을 없음 (매각 판정 불가)
            public int NpcsWithStealable;
            public int NpcsWithFreeSlot;
            public long ValueSum;
            public long StealableValueSum;
            public int MaxValue;
            public long JsonBytes;           // 생존 NPC 가방(Inventory) JSON 합계 (JsonConvert 기준 — 세이브 크기 비교용)
            public readonly int[] ValueBuckets = new int[BagValueBucketUpper.Length];
            public readonly List<int> Values = new();
        }

        /// <summary>NPC 관계 지표 (P4-0). 두 NPC가 모두 살아 있는 호감도 쌍·유형 관계만 센다.</summary>
        private sealed class SocialSnapshot
        {
            public bool Captured;
            public int AffinityPairs;
            public long AffinitySum;
            public int AffinityLowPairs;     // ≤ −50
            public int AffinityHighPairs;    // ≥ +50
            public int AffinityMin;
            public int AffinityMax;
            public readonly int[] RelationCounts = new int[ReportedRelationTypes.Length];
        }

        /// <summary>
        /// 절도 조건 깔때기 (P4-0, D99·D100). 판정·실행 없이 조건 충족 수만 센다. 인덱스 0 = 기회 주 야망, 1 = 보조 야망.
        /// </summary>
        private sealed class TheftFunnel
        {
            public long RestEnds;                                // Rest 정상 종료 (절도 판정 자리 — 파티 합류 경로 제외)
            public readonly long[] Thieves = new long[2];        // 도둑 후보 (야망 배율 > 0)
            public readonly long[] FreeSlot = new long[2];       // 도둑 가방 빈칸 있음
            public readonly long[] WithVictim = new long[2];     // 훔칠 물건이 있는 피해자 후보 ≥ 1 (쿨다운 없을 때)
            public readonly long[] VictimSum = new long[2];      // 그때 피해자 후보 수 합
            public readonly long[] SameVillageItemSum = new long[2];   // 빈칸 단계에서 같은 마을 생존 NPC 중 훔칠 물건 보유 수 합 (대기·관계 조건 전)
            public readonly long[] CooldownPass = new long[2];   // 통과마다 시도한다고 가정할 때 도둑 168h·마을 72h 쿨다운 통과
            public readonly long[,] VirtualAttempts = new long[TheftBaseChances.Length, 2];   // 기본 확률별 가상 시도 (쿨다운 포함)
        }

        /// <summary>
        /// 절도 1건의 적용 직전 상태 (P4a 독립 감사). 게임 관찰자(TheftDecidedObserver)가 적용 직전에 찍고,
        /// Rest 종료 처리가 끝난 뒤 복제본 상태를 직접 읽어 결과를 대조한다 (게임 판정 결과를 그대로 믿지 않는다).
        /// </summary>
        private sealed class TheftAuditSnapshot
        {
            public NpcTheftDecision Decision;
            public float GameTime;
            public int Day;
            public int ItemId;
            public int ItemValue;
            public int VictimThiefAffinityBefore;
            public bool GrudgeExistedBefore;           // 피해자 → 도둑 원한
            public readonly List<int> KinIds = new();   // 독립 계산한 피해자 가족·친구 (생존, 도둑 제외, 중복 없음)
            public readonly List<int> KinAffinityBefore = new();
            public int VictimRelationsBefore, ThiefRelationsBefore;
            public int ThiefDeedsBefore, ThiefTheftDeedsBefore, ThiefNotableBefore, VictimDeedsBefore;
            public readonly List<int> ThiefItemIdsBefore = new();   // 도둑 가방·장비 인스턴스 (버림 없음 확인)
        }

        /// <summary>절도 1건 기록 (P4a 지표). 감사가 복제본 상태로 판정한 결과를 담는다.</summary>
        private sealed class TheftRecord
        {
            public int Day;
            public bool Primary;
            public bool Detected;
            public bool Equipped;
            public int Value;
            public bool NewGrudge;
            public int KinCount;
            public int ThiefId, VictimId, ItemInstanceId;   // P4b 깔때기 (안 들킨 절도 → 장착/판매 → 늦은 발각)
        }

        /// <summary>늦은 발각 1건의 적용 직전 상태 (P4b 독립 감사, D103). 관찰자 직전 호출에서 찍고 직후 호출에서 대조한다.</summary>
        private sealed class TheftDiscoverySnapshot
        {
            public NpcTheftDiscovery Discovery;
            public int AffinityBefore;
            public bool GrudgeExistedBefore;
            public readonly List<int> KinIds = new();
            public readonly List<int> KinAffinityBefore = new();
            public int VictimDeedsBefore, ThiefDeedsBefore;
        }

        private sealed class NpcStats
        {
            public int Rest;
            public int Hunt;
            public int Trade;
            public int Delve;
            public int SoldCount;
            public long SoldGold;
        }

        private sealed class SimContext
        {
            public readonly Dictionary<int, NpcSaveData> Npcs = new();
            public readonly List<int> NpcIds = new();
            public readonly Dictionary<int, VillageData> Villages = new();
            public readonly List<VillageData> VillageList = new();
            public readonly Dictionary<int, NpcPartyState> Parties = new();
            public readonly Dictionary<int, VillageStats> VillageStats = new();
            public readonly Dictionary<int, NpcStats> NpcStats = new();
            public readonly List<string> Examples = new();
            // 저장 스냅샷: 변경 표시 없이 바뀐 NPC 수, 항목별 횟수, 앞쪽 사례
            public int UnmarkedSaveChanges;
            public int UnmarkedVillageSaveChanges;
            public int UnmarkedSimulationSaveChanges;
            public int SnapshotHour;
            public readonly Dictionary<string, int> UnmarkedSaveFields = new();
            public readonly List<string> UnmarkedSaveSamples = new();
            public readonly List<string> UnmarkedFieldScratch = new();
            // 시작 시점 생존 NPC 고정 집단. 사망자는 마지막 장비의 전투력으로 계속 센다 — 생존 편향 없이 성장만 본다.
            public readonly List<int> CohortIds = new();
            // 시작 시점 전투력. 생존자 평균 상승을 "생존 편향"과 "장비 교체"로 나눠 보는 데 쓴다.
            public readonly Dictionary<int, float> StartPower = new();
            // 던전 저장 상태 복제본 (NPC 던전 완료가 슬롯 세대를 넘기므로 라이브 대신 이것을 바꾼다)
            public DungeonSaveRoot Dungeons = new();
            public readonly Dictionary<int, VillageDungeonSlots> DungeonVillageSlots = new();
            public readonly Dictionary<NpcDungeonCompletionResult, int> CompletionResults = new();
            // 완료 시점에 플레이어 DungeonRecord가 있던 던전의 NPC 완료 수 (D62: 항상 0이어야 함, 보호 규칙과 독립 검사)
            public int CompletionsOnPlayerEntered;
            public int PlayerEnteredDungeonsAtStart;
            public readonly List<string> CompletionExamples = new();
            public int WorldSeed;
            public DayRow Today = new DayRow();
            public long TotalGoldBurned;
            public long TotalPopulationGold;
            // GOLD_BALANCE: 세계 물가 지수 복제본(WorldData.GoldPrice 대신 바꿔 끼움)·NPC 골드 흐름 카운터·일 시작 값
            public GoldPriceIndexState GoldPrice = new GoldPriceIndexState();
            public NpcGoldFlowCounters GoldFlow = new NpcGoldFlowCounters();
            public long LivingCountAtDayStart, LivingDepositAtDayStart, LivingBurnAtDayStart;
            // NPC_SHOP_EQUIPMENT (D118·D119): 기본 아이템이 아닌 구매 수, 시작 시점 생존 NPC 장착 장비 중 기본 아이템 아님/전체
            public int NonBasicPurchases;
            public int NonBasicEquippedAtStart;
            public int EquippedAtStart;

            // 소문 (P2b)
            public NpcRumorCounters RumorCounters = new NpcRumorCounters();
            public long RumorAttemptsAtDayStart;
            public long RumorTransfersAtDayStart;
            public long ConfirmationsAtDayStart;
            public readonly Dictionary<string, RumorEventTrack> RumorEventIndex = new();
            public readonly List<RumorEventTrack> RumorEvents = new();
            // [분류, 측정 시점] → 비율 합·최대·표본 수
            public readonly double[,] PropagationSum = new double[RumorCategoryCount, 4];
            public readonly double[,] PropagationMax = new double[RumorCategoryCount, 4];
            public readonly int[,] PropagationSamples = new int[RumorCategoryCount, 4];
            // 선택 시점 "유망한 소문" 보유 여부별 활동 선택 수
            public long HolderSelections, HolderDelve, NonHolderSelections, NonHolderDelve;
            // 확인 원정 추적 (진단 전용 — 게임 상태에는 저장하지 않는다)
            public readonly HashSet<int> ConfirmationSolo = new();
            public readonly HashSet<int> ConfirmationParties = new();
            public long ConfirmationJudgments, ConfirmationSuccess;
            // 보관 중 무효 (일말 감사, 원인별 최댓값·마지막 값)
            public int StoredInvalidMax;
            public readonly int[] StoredInvalidByReasonLast = new int[4];
            public readonly int[] StoredInvalidByReasonMax = new int[4];
            public long AuditExposedFailures;
            public long NpcJsonBytesStart;
            // 원정 성공 사건 중 가치가 유망 기준 이상인 수 (PromisingRumorValueMin 튜닝 참고)
            public long DelveEvents, DelveEventsAboveValueMin;

            // P2c: 대화 토픽 검증 · Deed 크기
            public bool ProbePlayerEntered;
            public readonly long[] DeedBytesStart = new long[DeedKindCount];
            public readonly int[] DeedCountStart = new int[DeedKindCount];
            // D91: 원정 부상 누적 요약(NpcSaveData.WoundCount) 합계 — 시작 시점
            public long WoundCountStart;
            public TopicAudit Topics = new TopicAudit();

            // D85: 원정 파티 자동 구성
            public NpcPartyCounters PartyCounters = new NpcPartyCounters();
            public long SoloHuntJudgments, PartyHuntJudgments, SoloDelveJudgments, PartyDelveJudgments;
            public long SoloHuntStarts, PartyHuntStarts, SoloDelveStarts, PartyDelveStarts;
            // 사라진 터 강제 이주 (NPC_AMBITION D138~D141): 시작·도착 수, 시작 시점 터 소속 생존 NPC 수
            public long RelocationStarts, RelocationArrivals;
            public int AbandonedResidentsAtStart;
            public float RelocationTravelHours;
            // 매 시간 생존 NPC 상태 표본: StateRest..StateIdle 인덱스
            public readonly long[] StateHours = new long[StateSlotCount];
            public long TotalFoodInflow;

            // D107: NPC 호감도·유형 관계 복제본 (라이브 대신 바꿔 끼운다)
            public Dictionary<NpcPairKey, NpcAffinityEntry> Affinity = new();
            public readonly Dictionary<int, List<NpcRelationEntry>> Relations = new();
            // P4-0: 가방·관계 지표(시작/끝), 절도 조건 깔때기(전 구간/마지막 15일), 시작 생존 집단의 끝 전투력
            public int CurrentDay;
            public int TheftLateStartDay;
            public BagSnapshot BagStart = new BagSnapshot();
            public BagSnapshot BagEnd = new BagSnapshot();
            public SocialSnapshot SocialStart = new SocialSnapshot();
            public SocialSnapshot SocialEnd = new SocialSnapshot();
            public readonly TheftFunnel TheftAll = new TheftFunnel();
            public readonly TheftFunnel TheftLate = new TheftFunnel();
            // 가정 쿨다운의 최근 시도 시각: [0] = 통과마다 시도, [1 + c] = TheftBaseChances[c]의 가상 시도
            public readonly Dictionary<int, float>[] TheftLastThiefAttempt = CreateTheftCooldownTables();
            public readonly Dictionary<int, float>[] TheftLastVillageAttempt = CreateTheftCooldownTables();
            // NpcActivityResolver.CreateRng (internal — 리플렉션 델리게이트). 가상 시도 롤에만 쓴다.
            public Func<int, float, int, int, HistoryRng>? CreateActivityRng;
            public readonly Dictionary<int, float> EndPower = new();

            // P4a: 절도 카운터(게임 매니저 인스턴스 소유 — 바꿔 끼움)와 독립 감사
            public NpcTheftCounters TheftCounters = new NpcTheftCounters();
            public TheftAuditSnapshot? PendingTheft;
            public readonly List<TheftRecord> TheftRecords = new();
            public readonly List<string> TheftAuditFailures = new();
            public readonly Dictionary<int, float> AuditLastThiefAttempt = new();
            public readonly Dictionary<int, float> AuditLastVillageAttempt = new();
            public int TheftObserved, TheftAuditFailCount, TheftEligibilityFails, TheftThiefCooldownViolations,
                TheftVillageCooldownViolations, TheftCapExceed, TheftCaughtOk, TheftStolenOk, TheftUnaudited,
                TheftThiefNotable;
            // P4b: 늦은 발각 독립 감사·깔때기 (경로 인덱스 0 장착 소문, 1 판매 소문)
            public TheftDiscoverySnapshot? PendingDiscovery;
            public readonly HashSet<int> SoldStolenInstances = new();
            public readonly HashSet<int> DiscoveredInstances = new();
            public readonly int[] DiscoveryCount = new int[2];
            public readonly int[] DiscoveryOk = new int[2];
            public int DiscoveryTriggerFails, DiscoveryUnaudited, DiscoveryNewGrudges, DiscoveryKinTotal;
            public readonly List<string> DiscoveryAuditFailures = new();
        }

        private static Dictionary<int, float>[] CreateTheftCooldownTables()
        {
            Dictionary<int, float>[] tables = new Dictionary<int, float>[TheftBaseChances.Length + 1];
            for (int i = 0; i < tables.Length; i++)
                tables[i] = new Dictionary<int, float>();
            return tables;
        }

        private const int StateRest = 0, StateSoloHunt = 1, StatePartyHunt = 2, StateTrade = 3, StateSoloDelve = 4,
            StatePartyDelve = 5, StateWounded = 6, StateIdle = 7, StateSlotCount = 8;

        /// <summary>대화 토픽 검증 결과 (P2c). 시뮬 마지막 시각의 복제본에서 실제 DialogueTopicSelector 코드로 만든다.</summary>
        private sealed class TopicAudit
        {
            public int LivingNpcs;
            public int RumorHolders;
            public int NpcsWithRumorTopic;
            public long HeldRumors, HeldInvalid, HeldValid;
            public long ExcludedOwnItem, ExcludedOtherVillage, ExcludedOther;
            public long DungeonTopics, ItemTopics, ClearedTopics;
            public long TargetOk, TargetFail;
            public long WithBearing, WithoutBearing, BearingMismatch;
            public long SubjectKnown, SubjectUnknown, NameMissingInLine;
            public long FirsthandTopics, NamedSourceTopics, AnonymousSourceTopics;
            public readonly long[] BearingHistogram = new long[8];
            public readonly List<string> Failures = new();
            public readonly List<string> Examples = new();
            // 실제 Collect 경로 (대화 1회분): 종류마다 최대 1개인지, 자랑 토픽 출처
            public int CollectDungeonTopicNpcs, CollectItemTopicNpcs, CollectMultiplePerType;
            public int BoastItemNpcs, BoastClearedNpcs, NotableDeedNpcs, ClearedDeedNpcs;
            public readonly List<string> BoastExamples = new();
            // 레퍼토리 포함률 (토픽이 있는 NPC 중 그 관계 단계 레퍼토리에 들어간 비율): [Stranger, Friend]
            public readonly int[] RumorRepertoireIn = new int[2];
            public readonly int[] RumorRepertoireBase = new int[2];
            public readonly int[] BoastRepertoireIn = new int[2];
            public readonly int[] BoastRepertoireBase = new int[2];
            public long ElapsedMs;
            // P4b: 배신 소문 토픽, 피해자 도난 사연, 도둑 침묵, 판매 소문 토픽 없음 (D104·D106)
            public long BetrayalTopics;
            public int CollectBetrayalTopicNpcs;
            public int TheftVictimDeedNpcs, TheftVictimTopicNpcs, TheftTopicNamed, TheftTopicAnonymous, TheftTopicLineFail;
            public int ThiefOnlyNpcs, ThiefSilenceViolations;
            public long ItemSoldTopics;
            public readonly int[] TheftRepertoireIn = new int[2];
            public readonly int[] TheftRepertoireBase = new int[2];
            public readonly List<string> TheftExamples = new();
        }

        /// <summary>한 번에 바꿔 끼운 private 필드. 끝나면 역순으로 되돌린다.</summary>
        private sealed class FieldSwap
        {
            public object Target = null!;
            public FieldInfo Field = null!;
            public object? Original;
        }

        /// <summary>시뮬레이션 중 로그를 삼키고 에러만 센다. 끝나면 원래 핸들러로 되돌린 뒤 에러 요약을 찍는다.</summary>
        private sealed class CapturingLogHandler : ILogHandler
        {
            public int ErrorCount;
            public readonly List<string> FirstErrors = new();

            public void LogFormat(LogType logType, UnityEngine.Object? context, string format, params object[] args)
            {
                if (logType != LogType.Error && logType != LogType.Exception && logType != LogType.Assert)
                    return;
                ErrorCount++;
                if (FirstErrors.Count < 5)
                    FirstErrors.Add(args != null && args.Length > 0 ? string.Format(format, args) : format);
            }

            public void LogException(Exception exception, UnityEngine.Object? context)
            {
                ErrorCount++;
                if (FirstErrors.Count < 5)
                    FirstErrors.Add(exception.ToString());
            }
        }

        /// <summary>진단 실행. 요약 문자열을 돌려주고 콘솔에도 찍는다 (execute_code에서 직접 호출 가능).</summary>
        public static string Run(int days) => RunCore(days, false);

        /// <summary>
        /// D62 검증용 변형: 복제본에서 모든 마을의 슬롯 0에 플레이어 DungeonRecord(가짜)를 심어 두고 같은 시뮬을 돌린다.
        /// 그 던전들의 NPC 완료가 0건이어야 한다. 라이브 상태는 바꾸지 않는다.
        /// </summary>
        public static string RunPlayerEnteredProbe(int days) => RunCore(days, true);

        private static string RunCore(int days, bool probePlayerEntered)
        {
            if (EditorApplication.isPlaying == false || AR.s == null || AR.s.Npc == null || AR.s.Village == null ||
                AR.s.Item == null || AR.s.Time == null || AR.s.Map == null)
            {
                const string notReady = "[NpcAmbitionSim] 플레이 모드에서 GameScene이 로드된 뒤 실행하세요.";
                Debug.LogError(notReady);
                return notReady;
            }
            // GOLD_BALANCE G2: 물가 안정(420게임일 = 현실 168시간) 확인용으로 상한을 840일로 둔다.
            days = Mathf.Clamp(days, 1, 840);

            string hashBefore = ComputeLiveStateHash();
            UnityEngine.Random.State randomState = UnityEngine.Random.state;
            FieldInfo itemCounterField = GetField(AR.s.Item, "_instanceIdCounter");
            object? itemCounter = itemCounterField.GetValue(AR.s.Item);
            FieldInfo elapsedField = GetField(AR.s.Time, "_elapsedGameTime");
            FieldInfo accumulatorField = GetField(AR.s.Time, "_accumulator");
            object? elapsed = elapsedField.GetValue(AR.s.Time);
            object? accumulator = accumulatorField.GetValue(AR.s.Time);
            float startGameTime = AR.s.Time.CurrentGameTime;

            ILogHandler originalLogHandler = Debug.unityLogger.logHandler;
            CapturingLogHandler capture = new CapturingLogHandler();
            List<FieldSwap> swaps = new List<FieldSwap>();
            List<DayRow> rows = new List<DayRow>();
            SimContext sim = new SimContext();
            System.Diagnostics.Stopwatch watch = System.Diagnostics.Stopwatch.StartNew();
            string? failure = null;
            bool cancelled = false;
            List<int> cowardIds = new List<int>();
            int cowardCourageMax = 0;
            Dictionary<NpcAmbitionKind, int> ambitionCounts = new Dictionary<NpcAmbitionKind, int>();

            int snapshotMismatches = -1;
            int villageSnapshotMismatches = -1;
            int simulationSnapshotMismatches = -1;
            double snapshotFlushAverageMs = 0;
            double snapshotFlushMaxMs = 0;

            try
            {
                // 복제는 로그 핸들러 교체 전에 한다 — 복제 실패가 콘솔에 보이도록.
                BuildClones(sim);
                if (probePlayerEntered == true)
                    InjectPlayerEnteredProbe(sim);
                sim.WorldSeed = AR.s.Map.mapSeed;
                sim.TheftLateStartDay = Mathf.Max(1, days - 14);
                MethodInfo? createRng = typeof(NpcActivityResolver).GetMethod("CreateRng",
                    BindingFlags.NonPublic | BindingFlags.Static);
                if (createRng == null)
                    throw new MissingMethodException(nameof(NpcActivityResolver), "CreateRng");
                sim.CreateActivityRng = (Func<int, float, int, int, HistoryRng>)Delegate.CreateDelegate(
                    typeof(Func<int, float, int, int, HistoryRng>), createRng);
                CollectStartPopulation(sim, cowardIds, out cowardCourageMax, ambitionCounts);

                sim.NpcJsonBytesStart = MeasureNpcJsonBytes(sim, out _, out _);
                sim.ProbePlayerEntered = probePlayerEntered;
                MeasureDeedJsonBytes(sim, sim.DeedBytesStart, sim.DeedCountStart);
                sim.WoundCountStart = SumWoundCount(sim);
                Debug.unityLogger.logHandler = capture;
                SwapInClones(sim, swaps);
                AttachRumorProbes(sim, swaps);
                // P4-0: 시작 가방·관계 지표 (매각 판정이 마을 복제본을 보므로 교체 뒤에 잰다)
                sim.BagStart = MeasureBags(sim);
                sim.SocialStart = MeasureSocial(sim);
                // GOLD_BALANCE: 게임과 같은 지수 갱신 메서드 (기준값이 없으면 시작 세계 Gold로 정한다)
                AR.s.Village.UpdateGoldPriceIndex(startGameTime);

                rows.Add(RecordDay(sim, 0, startGameTime));
                sim.Today = new DayRow();
                int totalHours = days * 24;
                for (int hour = 1; hour <= totalHours; hour++)
                {
                    if (EditorUtility.DisplayCancelableProgressBar("NPC / Village snapshot simulation",
                            $"{hour - 1}/{totalHours} game hours (Cancel restores live state)",
                            (float)(hour - 1) / totalHours) == true)
                        throw new OperationCanceledException("Simulation cancelled by user.");
                    float now = startGameTime + hour;
                    sim.CurrentDay = (hour - 1) / 24 + 1;
                    elapsedField.SetValue(AR.s.Time, now);
                    accumulatorField.SetValue(AR.s.Time, 0f);

                    sim.SnapshotHour = hour;
                    AdvanceVillageProduction(sim);
                    AR.s.Village.FlushSaveSnapshot();
                    AdvanceParties(sim, now);
                    AR.s.Npc.RefreshSimulationSaveSnapshots(SaveScope.All);
                    AR.s.Village.FlushSaveSnapshot();
                    for (int i = 0; i < sim.NpcIds.Count; i++)
                    {
                        int entityId = sim.NpcIds[i];
                        // 표시 없이 바뀐 게 있는지 먼저 보고, 처리 직후 바로 정리한다.
                        // tick 끝에 몰아서 정리하면 같은 tick 뒤쪽에서 이 NPC를 표시 없이 고친 것이 가려진다.
                        CheckUnmarkedSaveChange(sim, entityId, hour);
                        AdvanceNpc(sim, entityId, now);
                        AR.s.Npc.FlushSaveSnapshot();
                        AR.s.Npc.RefreshSimulationSaveSnapshots(SaveScope.All);
                        AR.s.Village.FlushSaveSnapshot();
                    }
                    UpdateRumorPropagation(sim, now);
                    SampleActivityStates(sim);
                    AR.s.Village.UpdateGoldPriceIndex(now);

                    // NPC 처리 뒤에 표시된 변경(소문 전파 등)을 정리한다. 여기서 빠진 표시는 다음 tick 검사가 잡는다.
                    AR.s.Npc.FlushSaveSnapshot(recordFrameTiming: true);
                    AR.s.Npc.RefreshSimulationSaveSnapshots(SaveScope.All, recordFrameTiming: true);
                    AR.s.Village.FlushSaveSnapshot(recordFrameTiming: true);

                    if (hour % 24 == 0)
                    {
                        rows.Add(RecordDay(sim, hour / 24, now));
                        sim.Today = new DayRow();
                    }
                }

                // P4-0: 끝 가방·관계 지표, 시작 생존 집단의 끝 전투력 (복제본이 끼워진 동안 잰다)
                CaptureEndState(sim);

                // P2c: 복제본이 끼워진 채로(시각 = 시뮬 마지막) 대화 토픽을 실제 selector 코드로 만들어 검증한다.
                AuditDialogueTopics(sim);
                snapshotMismatches = NpcManager.SaveSnapshotEnabled == true ? AR.s.Npc.ValidateSaveSnapshot() : 0;
                CheckUnmarkedVillageSaveChanges(sim, totalHours);
                villageSnapshotMismatches = VillageManager.SaveSnapshotEnabled == true ? AR.s.Village.ValidateSaveSnapshot() : 0;
                simulationSnapshotMismatches = AR.s.Npc.ValidateSimulationSaveSnapshots();
                snapshotFlushAverageMs = AR.s.Npc.SnapshotFlushAverageMs;
                snapshotFlushMaxMs = AR.s.Npc.SnapshotFlushMaxMs;
                if (sim.UnmarkedSaveChanges != 0 || snapshotMismatches != 0 ||
                    sim.UnmarkedVillageSaveChanges != 0 || villageSnapshotMismatches != 0 ||
                    sim.UnmarkedSimulationSaveChanges != 0 || simulationSnapshotMismatches != 0)
                    throw new InvalidOperationException(BuildSaveSnapshotOmissionReport(sim, snapshotMismatches) +
                        $"\nVillage omissions: unmarked={sim.UnmarkedVillageSaveChanges} final={villageSnapshotMismatches}" +
                        $"\nSimulation omissions: unmarked={sim.UnmarkedSimulationSaveChanges} final={simulationSnapshotMismatches}" +
                        "\n" + string.Join("\n", capture.FirstErrors));
            }
            catch (OperationCanceledException exception)
            {
                cancelled = true;
                failure = exception.Message;
            }
            catch (Exception exception)
            {
                failure = exception.ToString();
            }
            finally
            {
                EditorUtility.ClearProgressBar();
                DetachRumorProbes();
                for (int i = swaps.Count - 1; i >= 0; i--)
                    swaps[i].Field.SetValue(swaps[i].Target, swaps[i].Original);
                elapsedField.SetValue(AR.s.Time, elapsed);
                accumulatorField.SetValue(AR.s.Time, accumulator);
                itemCounterField.SetValue(AR.s.Item, itemCounter);
                UnityEngine.Random.state = randomState;
                Debug.unityLogger.logHandler = originalLogHandler;
            }
            watch.Stop();

            string hashAfter = ComputeLiveStateHash();
            bool liveUnchanged = hashBefore == hashAfter;

            if (failure != null)
            {
                string failed = $"[NpcAmbitionSim] 실패 (라이브 상태 복원 {(liveUnchanged ? "확인" : "불일치!")}): {failure}";
                if (cancelled == true && liveUnchanged == true)
                    Debug.LogWarning(failed);
                else
                    Debug.LogError(failed);
                return failed;
            }

            string directory = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Logs", "NpcAmbitionSim"));
            Directory.CreateDirectory(directory);
            string stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);
            string dailyPath = Path.Combine(directory, $"ambition_sim_{stamp}_{days}d_daily.csv");
            string villagePath = Path.Combine(directory, $"ambition_sim_{stamp}_{days}d_villages.csv");
            string summaryPath = Path.Combine(directory, $"ambition_sim_{stamp}_{days}d_summary.txt");
            File.WriteAllText(dailyPath, BuildDailyCsv(rows), new UTF8Encoding(true));
            File.WriteAllText(villagePath, BuildVillageCsv(sim), new UTF8Encoding(true));

            string summary = BuildSummary(sim, rows, cowardIds, cowardCourageMax, ambitionCounts, days,
                watch.ElapsedMilliseconds, hashBefore, hashAfter, capture);
            summary += $"\n[NpcSaveSnapshot] 표시 누락={sim.UnmarkedSaveChanges} 끝 비교 누락={snapshotMismatches} " +
                $"tick끝 정리 평균={snapshotFlushAverageMs:F3}ms 최대={snapshotFlushMaxMs:F3}ms\n";
            summary += $"\n[VillageSaveSnapshot] 표시 누락={sim.UnmarkedVillageSaveChanges} 끝 비교 누락={villageSnapshotMismatches}\n";
            summary += $"\n[SimulationSaveSnapshot] 표시 누락={sim.UnmarkedSimulationSaveChanges} 끝 비교 누락={simulationSnapshotMismatches}\n";
            File.WriteAllText(summaryPath, summary, new UTF8Encoding(true));

            string report = summary + $"\nCSV: {dailyPath}\n     {villagePath}\n     {summaryPath}";
            if (liveUnchanged == true)
                Debug.Log(report);
            else
                Debug.LogError(report);
            return report;
        }

        [MenuItem("ARPG/Npc/Diagnostics/Verify NPC Dungeon Completion Rules", false, 433)]
        public static void RunVerifyNpcCompletionRules() => VerifyNpcCompletionRules();

        /// <summary>
        /// NPC 던전 완료 보호 규칙·슬롯 세대 전진·플레이어 완료 경로 불변을 던전 저장 복제본에서 검증한다 (P2a, D58·D62).
        /// 라이브 던전 상태는 바꾸지 않으며 실행 전후 라이브 상태 해시를 비교한다. 결과 문자열을 돌려주고 콘솔에도 찍는다.
        /// </summary>
        public static string VerifyNpcCompletionRules()
        {
            if (EditorApplication.isPlaying == false || AR.s == null || AR.s.Dungeon == null || AR.s.Data == null ||
                AR.s.Dungeon.IsInitialized == false)
            {
                const string notReady = "[NpcDungeonRules] 플레이 모드에서 GameScene이 로드된 뒤 실행하세요.";
                Debug.LogError(notReady);
                return notReady;
            }

            string hashBefore = ComputeLiveStateHash();
            List<FieldSwap> swaps = new List<FieldSwap>();
            StringBuilder log = new StringBuilder();
            int failures = 0;
            string? error = null;

            void Check(bool condition, string label)
            {
                if (condition == false)
                    failures++;
                log.AppendLine((condition ? "  PASS " : "  FAIL ") + label);
            }

            try
            {
                DungeonSaveRoot root = CloneJson(AR.s.Data.Dungeons);
                Dictionary<int, VillageDungeonSlots> index = new Dictionary<int, VillageDungeonSlots>();
                for (int i = 0; i < root.Villages.Count; i++)
                    index[root.Villages[i].VillageId] = root.Villages[i];

                object worldData = GetField(AR.s.Data, "_worldData").GetValue(AR.s.Data)!;
                Swap(swaps, worldData, "Dungeons", root);
                object dungeon = AR.s.Dungeon;
                Swap(swaps, dungeon, "_villageSlots", index);
                Swap(swaps, dungeon, "_entranceIndexDirty", false);
                Swap(swaps, dungeon, "_npcCompletionRevision", 0L);
                Swap(swaps, dungeon, "_savedNpcCompletionRevision", 0L);
                Swap(swaps, dungeon, "_busy", false);

                VillageDungeonSlots? two = FindCleanVillage(root, 2);
                VillageDungeonSlots? three = FindCleanVillage(root, 3);
                if (two == null || three == null)
                    throw new InvalidOperationException("슬롯 2개·3개인 깨끗한 마을을 찾지 못했습니다.");

                int worldSeed = AR.s.Data.WorldSeed;
                float t0 = AR.s.Time.CurrentGameTime;
                int playerGold = AR.s.Data.Player.Gold;
                long completedCount = root.CompletedCount;
                DungeonManager dm = AR.s.Dungeon;

                // ① 플레이어가 입장한 던전 (DungeonRecord 존재)
                DungeonSlot slotA = two.Slots[0];
                if (slotA.HasEntrance == false)
                {
                    // 입구가 있던 슬롯의 전진(입구 제거 + 이전 입구 기록)도 보도록 복제본에 입구 좌표를 둔다.
                    slotA.HasEntrance = true;
                    slotA.EntranceX = 111;
                    slotA.EntranceY = 222;
                }
                string idA = slotA.DungeonId;
                int genA = slotA.Generation;
                bool hadEntranceA = slotA.HasEntrance;
                int entranceAX = slotA.EntranceX, entranceAY = slotA.EntranceY;
                DungeonRecord fakeRecord = new DungeonRecord { DungeonId = idA, VillageId = two.VillageId, SlotIndex = 0,
                    Generation = genA };
                root.Dungeons.Add(fakeRecord);
                NpcDungeonCompletionResult r1 = dm.TryCompleteByNpc(two.VillageId, idA, t0);
                Check(r1 == NpcDungeonCompletionResult.PlayerEntered && slotA.DungeonId == idA && slotA.Generation == genA,
                    $"규칙① DungeonRecord가 있는 던전은 거부 (결과 {r1}, 슬롯 불변)");
                root.Dungeons.Remove(fakeRecord);

                // 성공 경로: 슬롯 세대 전진
                NpcDungeonCompletionResult r2 = dm.TryCompleteByNpc(two.VillageId, idA, t0);
                float cooldown = AR.s.Data.GetDungeonTable(slotA.DungeonTableId)?.ReplacementCooldownHours ?? 0f;
                Check(r2 == NpcDungeonCompletionResult.Completed, $"기록 없는 던전은 완료 (결과 {r2})");
                Check(slotA.Generation == genA + 1 && slotA.DungeonId == DungeonLayout.Id(worldSeed, two.VillageId, 0, genA + 1),
                    $"세대 +1·새 DungeonId ({idA} → {slotA.DungeonId})");
                Check(slotA.HasEntrance == false && DungeonLayout.IsNpcReplaced(slotA) == true,
                    "입구 제거 + NPC 교체 세대 표시");
                Check(hadEntranceA == false || (slotA.HasPreviousEntrance == true && slotA.PreviousX == entranceAX &&
                    slotA.PreviousY == entranceAY), $"입구가 있었으면 이전 입구로 기록 (hadEntrance={hadEntranceA})");
                Check(Mathf.Approximately(slotA.PlacementReadyGameTime, t0 + cooldown),
                    $"교체 쿨다운 적용 (ready={slotA.PlacementReadyGameTime:F2}, 기대 {t0 + cooldown:F2})");
                Check(Mathf.Approximately(two.LastNpcCompletionGameTime, t0), "마을 LastNpcCompletionGameTime 기록");
                Check(AR.s.Data.Player.Gold == playerGold && root.CompletedCount == completedCount,
                    "플레이어 Gold·전역 CompletedCount 불변");
                Check(dm.HasUnsavedNpcCompletion == true, "미저장 NPC 완료 표시 → 다음 저장이 Dungeons+NpcState를 함께 싣는다");
                Check(dm.TryCompleteByNpc(two.VillageId, idA, t0 + 100f) == NpcDungeonCompletionResult.DungeonChanged,
                    "이미 넘어간 세대의 옛 DungeonId는 거부 (DungeonChanged)");

                // ② 마을당 72h 쿨다운
                string idB = two.Slots[1].DungeonId;
                NpcDungeonCompletionResult r3 = dm.TryCompleteByNpc(two.VillageId, idB, t0 + 1f);
                Check(r3 == NpcDungeonCompletionResult.VillageCooldown && two.Slots[1].DungeonId == idB,
                    $"규칙② 쿨다운 안(1h 뒤) 재시도 거부 (결과 {r3})");

                // ③ 이번 완료 뒤 NPC 교체 슬롯이 절반을 넘으면 거부 — 2슬롯 마을: 다른 슬롯이 교체 세대면 거부
                NpcDungeonCompletionResult r4 = dm.TryCompleteByNpc(two.VillageId, idB, t0 + 73f);
                Check(r4 == NpcDungeonCompletionResult.NpcReplacedHalf && two.Slots[1].DungeonId == idB,
                    $"규칙③ 2슬롯: 다른 슬롯이 NPC 교체 세대면 쿨다운 뒤에도 거부 (결과 {r4})");
                // 대상 슬롯 자신이 교체 세대면 교체 수가 늘지 않으므로 허용
                string idA2 = slotA.DungeonId;
                NpcDungeonCompletionResult r4b = dm.TryCompleteByNpc(two.VillageId, idA2, t0 + 73f);
                Check(r4b == NpcDungeonCompletionResult.Completed && slotA.Generation == genA + 2 &&
                      DungeonLayout.IsNpcReplaced(slotA) == true && DungeonLayout.IsNpcReplaced(two.Slots[1]) == false,
                    $"규칙③ 2슬롯: 이미 교체 세대인 슬롯 자신을 다시 완료하면 허용, 교체 수 1 유지 (결과 {r4b})");

                // ③ 3슬롯 마을: 1개 교체돼 있으면 다른 슬롯은 거부, 교체된 슬롯 자신은 허용
                string idC0 = three.Slots[0].DungeonId;
                string idC1 = three.Slots[1].DungeonId;
                NpcDungeonCompletionResult r5 = dm.TryCompleteByNpc(three.VillageId, idC0, t0);
                NpcDungeonCompletionResult r6 = dm.TryCompleteByNpc(three.VillageId, idC1, t0 + 73f);
                NpcDungeonCompletionResult r7 = dm.TryCompleteByNpc(three.VillageId, three.Slots[0].DungeonId, t0 + 146f);
                Check(r5 == NpcDungeonCompletionResult.Completed && r6 == NpcDungeonCompletionResult.NpcReplacedHalf &&
                      three.Slots[1].DungeonId == idC1 && r7 == NpcDungeonCompletionResult.Completed,
                    $"규칙③ 3슬롯: 1번째 {r5}, 다른 슬롯(교체 1개 있음) {r6}, 교체된 슬롯 자신 재완료 {r7}");

                // 플레이어 완료 경로 불변: 기존 트랜잭션을 복제 상태에서 스테이징만 하고 롤백한다.
                log.Append(VerifyPlayerCompletionPath(root, three, 2, worldSeed, t0, Check));
                // 플레이어가 NPC 교체 세대를 완료하면 새 세대는 표시가 없다 (새 슬롯 객체 기본값)
                log.Append(VerifyPlayerCompletionPath(root, two, 0, worldSeed, t0, Check));
            }
            catch (Exception exception)
            {
                error = exception.ToString();
            }
            finally
            {
                for (int i = swaps.Count - 1; i >= 0; i--)
                    swaps[i].Field.SetValue(swaps[i].Target, swaps[i].Original);
            }

            string hashAfter = ComputeLiveStateHash();
            bool liveUnchanged = hashBefore == hashAfter;
            string report = $"[NpcDungeonRules] 실패 {failures}건, 라이브 상태 해시 전후 동일: {liveUnchanged}\n" + log +
                            (error != null ? "  ERROR " + error + "\n" : string.Empty);
            if (failures == 0 && liveUnchanged == true && error == null)
                Debug.Log(report);
            else
                Debug.LogError(report);
            return report;
        }

        private static VillageDungeonSlots? FindCleanVillage(DungeonSaveRoot root, int slotCount)
        {
            for (int v = 0; v < root.Villages.Count; v++)
            {
                VillageDungeonSlots village = root.Villages[v];
                if (village.Slots.Count != slotCount || village.LastNpcCompletionGameTime >= 0f)
                    continue;
                bool clean = true;
                for (int s = 0; s < village.Slots.Count && clean == true; s++)
                {
                    if (DungeonLayout.IsNpcReplaced(village.Slots[s]) == true)
                        clean = false;
                    for (int r = 0; r < root.Dungeons.Count && clean == true; r++)
                    {
                        if (root.Dungeons[r].DungeonId == village.Slots[s].DungeonId)
                            clean = false;
                    }
                }
                if (clean == true)
                    return village;
            }
            return null;
        }

        private static string VerifyPlayerCompletionPath(DungeonSaveRoot root, VillageDungeonSlots village, int slotIndex,
            int worldSeed, float now, Action<bool, string> check)
        {
            DungeonSlot slot = village.Slots[slotIndex];
            bool originalHasEntrance = slot.HasEntrance;
            if (slot.HasEntrance == false)
            {
                // 플레이어 완료는 입구가 있는 슬롯에서만 일어난다 — 복제본에 가짜 입구 좌표를 둔다.
                slot.HasEntrance = true;
                slot.EntranceX = 12345;
                slot.EntranceY = 6789;
            }
            DungeonRecord record = new DungeonRecord
            {
                DungeonId = slot.DungeonId, VillageId = village.VillageId, SlotIndex = slotIndex,
                Generation = slot.Generation, DungeonTableId = slot.DungeonTableId, CompletionGold = 100,
                ReplacementCooldownHours = 12f,
            };
            record.Objects.Add(new DungeonObject { Id = 1, Kind = DungeonObjectKind.Boss });
            record.KilledObjects.Add(1);
            root.Dungeons.Add(record);
            PlayerData player = CloneJson(AR.s.Data.Player);
            int goldBefore = player.Gold;
            long countBefore = root.CompletedCount;
            bool wasNpcReplaced = DungeonLayout.IsNpcReplaced(slot);

            bool staged = DungeonCompletionTransaction.TryStage(root, player, record, worldSeed, now, 12f,
                out DungeonCompletionTransaction transaction);
            DungeonSlot next = village.Slots[slotIndex];
            check(staged == true && next.Generation == slot.Generation + 1 &&
                  next.DungeonId == DungeonLayout.Id(worldSeed, village.VillageId, slotIndex, slot.Generation + 1) &&
                  next.HasEntrance == false && next.HasPreviousEntrance == true && next.PreviousX == slot.EntranceX &&
                  next.PreviousY == slot.EntranceY && Mathf.Approximately(next.PlacementReadyGameTime, now + 12f) &&
                  next.NextCandidate == 0 && next.NpcReplacedGeneration == -1 && DungeonLayout.IsNpcReplaced(next) == false &&
                  player.Gold == goldBefore + 100 && record.Completed == true && root.CompletedCount == countBefore + 1,
                $"플레이어 완료 트랜잭션 결과 불변 (village={village.VillageId} slot={slotIndex}, NPC 교체 세대였음={wasNpcReplaced})");
            transaction?.Dispose();
            check(ReferenceEquals(village.Slots[slotIndex], slot) && player.Gold == goldBefore &&
                  record.Completed == false && root.CompletedCount == countBefore,
                "플레이어 완료 트랜잭션 롤백 불변");
            root.Dungeons.Remove(record);
            slot.HasEntrance = originalHasEntrance;
            return string.Empty;
        }

        // ================= 복제 · 교체 =================

        private static void InjectPlayerEnteredProbe(SimContext sim)
        {
            for (int v = 0; v < sim.Dungeons.Villages.Count; v++)
            {
                VillageDungeonSlots village = sim.Dungeons.Villages[v];
                if (village.Slots.Count == 0)
                    continue;
                DungeonSlot slot = village.Slots[0];
                bool exists = false;
                for (int r = 0; r < sim.Dungeons.Dungeons.Count; r++)
                {
                    if (sim.Dungeons.Dungeons[r].DungeonId == slot.DungeonId)
                        exists = true;
                }
                if (exists == false)
                    sim.Dungeons.Dungeons.Add(new DungeonRecord { DungeonId = slot.DungeonId, VillageId = village.VillageId,
                        SlotIndex = 0, Generation = slot.Generation, DungeonTableId = slot.DungeonTableId });
            }
            sim.PlayerEnteredDungeonsAtStart = sim.Dungeons.Dungeons.Count;
        }

        private static void BuildClones(SimContext sim)
        {
            List<int> ids = new List<int>(AR.s.Npc.SaveDataView.Keys);
            ids.Sort();
            for (int i = 0; i < ids.Count; i++)
            {
                NpcSaveData live = AR.s.Npc.SaveDataView[ids[i]];
                NpcSaveData clone = CloneJson(live);
                clone.OnLoadCompleted();
                // 시뮬레이션에서는 모두 오프스크린으로 본다 (실체화 연출·장착 지연 없음).
                clone.IsActive = false;
                clone.IsSpawning = false;
                sim.Npcs.Add(ids[i], clone);
                sim.NpcIds.Add(ids[i]);
                sim.NpcStats.Add(ids[i], new NpcStats());
            }
            CountEquippedBasic(sim, out sim.EquippedAtStart, out sim.NonBasicEquippedAtStart);

            IReadOnlyList<VillageData> villages = AR.s.Village.GetAllVillages();
            for (int i = 0; i < villages.Count; i++)
            {
                VillageData live = villages[i];
                VillageData clone = CloneJson(live);
                clone.NpcEntityIds = new List<int>(live.NpcEntityIds);
                // 엔티티 ID를 끊어 SyncStorageFromData가 라이브 스토리지 컴포넌트를 건드리지 않게 한다.
                clone.EntityId = -1;
                for (int s = 0; s < clone.MerchantStock.Count; s++)
                    clone.MerchantStock[s].Item?.OnLoadCompleted();
                sim.Villages[clone.VillageId] = clone;
                sim.VillageList.Add(clone);
                sim.VillageStats[clone.VillageId] = new VillageStats
                {
                    VillageId = clone.VillageId,
                    HasShop = AR.s.Village.CountByService(clone.VillageId, ProvidedService.Shop) > 0,
                    NpcStart = CountLivingInVillage(sim, clone),
                    GoldStart = VillageResourceSimulation.GetAmount(clone, GlobalEnum.ItemType.Gold),
                };
            }
            sim.AbandonedResidentsAtStart = CountAbandonedSiteResidents(sim);

            // GOLD_BALANCE: 세계 물가 지수 상태 (라이브 기준값이 없으면 시뮬 시작 세계 Gold가 기준이 된다)
            sim.GoldPrice = CloneJson(AR.s.Data.GoldPrice);

            // 던전 저장 상태: NPC 던전 완료가 슬롯 세대를 넘기므로 복제본을 바꿔 끼운다 (라이브 던전은 절대 바꾸지 않는다).
            sim.Dungeons = CloneJson(AR.s.Data.Dungeons);
            for (int i = 0; i < sim.Dungeons.Villages.Count; i++)
            {
                VillageDungeonSlots slots = sim.Dungeons.Villages[i];
                sim.DungeonVillageSlots[slots.VillageId] = slots;
                if (sim.VillageStats.TryGetValue(slots.VillageId, out VillageStats? villageStats))
                    villageStats.DungeonSlots = slots.Slots.Count;
            }
            sim.PlayerEnteredDungeonsAtStart = sim.Dungeons.Dungeons.Count;

            object? liveParties = GetField(AR.s.Npc, "_npcPartyDict").GetValue(AR.s.Npc);
            if (liveParties is Dictionary<int, NpcPartyState> partyDict)
            {
                foreach (KeyValuePair<int, NpcPartyState> pair in partyDict)
                {
                    NpcPartyState clone = CloneJson(pair.Value);
                    clone.EnsureInitialized();
                    sim.Parties.Add(pair.Key, clone);
                }
            }

            // D107: NPC 호감도·유형 관계. 파티 호감도 변화(ApplyPartyRelationshipOutcome)·사망 정리가 복제본만 바꾸게 한다.
            if (GetField(AR.s.Npc, "_npcAffinity").GetValue(AR.s.Npc) is Dictionary<NpcPairKey, NpcAffinityEntry> liveAffinity)
            {
                foreach (var pair in liveAffinity)
                    sim.Affinity.Add(pair.Key, pair.Value.Copy());
            }
            if (GetField(AR.s.Npc, "_npcRelations").GetValue(AR.s.Npc) is Dictionary<int, List<NpcRelationEntry>> liveRelations)
            {
                // 유형 관계 엔트리는 From·To 두 인덱스에 같은 인스턴스로 들어 있고 RemoveAllRelationsInvolving이 참조로 지운다.
                // 엔트리마다 복제본 1개를 만들어(키 = 원본 참조, Equals 재정의 없음) 두 인덱스가 같은 복제 인스턴스를 공유하게 한다.
                Dictionary<NpcRelationEntry, NpcRelationEntry> copies = new Dictionary<NpcRelationEntry, NpcRelationEntry>();
                foreach (KeyValuePair<int, List<NpcRelationEntry>> pair in liveRelations)
                {
                    List<NpcRelationEntry> liveList = pair.Value;
                    List<NpcRelationEntry> list = new List<NpcRelationEntry>(liveList.Count);
                    for (int i = 0; i < liveList.Count; i++)
                    {
                        NpcRelationEntry entry = liveList[i];
                        if (copies.TryGetValue(entry, out NpcRelationEntry? copy) == false)
                        {
                            copy = new NpcRelationEntry(entry.FromEntityId, entry.ToEntityId, entry.Type, entry.OriginEventId);
                            copies.Add(entry, copy);
                        }
                        list.Add(copy);
                    }
                    sim.Relations.Add(pair.Key, list);
                }
            }
        }

        private static void SwapInClones(SimContext sim, List<FieldSwap> swaps)
        {
            object npc = AR.s.Npc;
            object village = AR.s.Village;
            List<int> partyIds = new List<int>(sim.Parties.Keys);
            partyIds.Sort();
            object? nextPartyId = GetField(npc, "_nextPartyId").GetValue(npc);

            Swap(swaps, npc, "_npcSaveDict", sim.Npcs);
            Swap(swaps, npc, "_saveSnapshot", new Dictionary<int, object?>());
            Swap(swaps, npc, "_dirtyNpcIds", new HashSet<int>());
            Swap(swaps, npc, "_dirtyNpcOrder", new List<int>());
            Swap(swaps, npc, "_snapshotFailedIds", new List<int>());
            Swap(swaps, npc, "_saveShardGenerations", new long[DataManager.NPC_SHARD_COUNT]);
            Swap(swaps, npc, "_snapshotFlushCount", 0L);
            Swap(swaps, npc, "_snapshotFlushTotalMs", 0.0);
            Swap(swaps, npc, "_snapshotFlushMaxMs", 0.0);
            var affinitySnapshot = new SaveSnapshotCache<NpcPairKey>("NpcAffinity");
            var relationSnapshot = new SaveSnapshotCache<int>("NpcRelations");
            var partySnapshot = new SaveSnapshotCache<int>("NpcParties");
            var populationSnapshot = new SaveSnapshotCache<int>("NpcPopulation");
            Swap(swaps, npc, "_affinitySaveSnapshot", affinitySnapshot);
            Swap(swaps, npc, "_relationSaveSnapshot", relationSnapshot);
            Swap(swaps, npc, "_partySaveSnapshot", partySnapshot);
            Swap(swaps, npc, "_populationSaveSnapshot", populationSnapshot);
            Swap(swaps, npc, "_npcPowerCache", new Dictionary<int, float>());
            Swap(swaps, npc, "_deferredPurchaseUpgradeItemIds", new Dictionary<int, List<int>>());
            Swap(swaps, npc, "_npcPartyDict", sim.Parties);
            Swap(swaps, npc, "_simulationPartyIds", partyIds);
            Swap(swaps, npc, "_simulationWorkCursor", 0);
            Swap(swaps, npc, "_nextPartyId", nextPartyId);
            // D107: NPC 호감도·유형 관계 (호감도·관계는 런타임 매니저가 정본이고 WorldData 쪽 목록은 저장·로드 때만 쓰인다 — 저장 dirty 플래그 없음)
            Swap(swaps, npc, "_npcAffinity", sim.Affinity);
            Swap(swaps, npc, "_npcRelations", sim.Relations);
            // P2c: 시뮬 중 Deed·소문 변경이 라이브 대화 캐시를 지우지 않게 (InvalidateDialogues) 빈 서사 런타임으로 바꿔 끼운다.
            Swap(swaps, npc, "_narrative", new NpcNarrativeRuntime());
            Swap(swaps, village, "_villages", sim.Villages);
            Swap(swaps, village, "_villageList", sim.VillageList);
            Swap(swaps, village, "_saveSnapshot", new Dictionary<int, object?>());
            Swap(swaps, village, "_dirtyVillageIds", new HashSet<int>());
            Swap(swaps, village, "_dirtyVillageOrder", new List<int>());
            Swap(swaps, village, "_snapshotFailedIds", new List<int>());
            Swap(swaps, village, "_saveSnapshotGeneration", 0L);
            Swap(swaps, village, "_snapshotFlushCount", 0L);
            Swap(swaps, village, "_snapshotFlushTotalMs", 0.0);
            Swap(swaps, village, "_snapshotFlushMaxMs", 0.0);
            Swap(swaps, village, "_snapshotDirtyObserver", new Action<int>(id =>
                CheckUnmarkedVillageSaveChange(sim, id, sim.SnapshotHour)));
            // GOLD_BALANCE: NPC 골드 흐름 카운터 (생활비 입금·소멸)
            Swap(swaps, npc, "_goldFlowCounters", sim.GoldFlow);

            // 던전: DataManager의 WorldData.Dungeons와 DungeonManager의 마을 슬롯 색인·NPC 완료 런타임 플래그.
            // 시뮬은 한 프레임 안의 동기 실행이라 진행 중인 입구 배치 저장(_busy)이 끼어들 수 없다 — 그동안만 false로 본다.
            object worldData = GetField(AR.s.Data, "_worldData").GetValue(AR.s.Data)!;
            Swap(swaps, worldData, "Dungeons", sim.Dungeons);
            // GOLD_BALANCE: 세계 물가 지수 상태 — 가격 함수(VillageManager)와 지수 갱신이 복제본만 읽고 바꾼다.
            Swap(swaps, worldData, "GoldPrice", sim.GoldPrice);
            Swap(swaps, worldData, "NpcPopulation", CloneJson(AR.s.Data.NpcPopulation));
            object dungeon = AR.s.Dungeon;
            Swap(swaps, dungeon, "_villageSlots", sim.DungeonVillageSlots);
            Swap(swaps, dungeon, "_entranceIndexDirty", false);
            Swap(swaps, dungeon, "_npcCompletionRevision", 0L);
            Swap(swaps, dungeon, "_savedNpcCompletionRevision", 0L);
            Swap(swaps, dungeon, "_busy", false);
            if (NpcManager.SaveSnapshotEnabled == true)
                AR.s.Npc.RebuildSaveSnapshot();
            if (VillageManager.SaveSnapshotEnabled == true)
                AR.s.Village.RebuildSaveSnapshot();
            AR.s.Npc.RefreshSimulationSaveSnapshots(SaveScope.All, rebuild: true);
            void RecordOmission(string label, bool changed)
            {
                if (changed == false) return;
                sim.UnmarkedSimulationSaveChanges++;
                if (sim.UnmarkedSaveSamples.Count < 20)
                    sim.UnmarkedSaveSamples.Add($"{sim.SnapshotHour}h {label}: {string.Join(", ", sim.UnmarkedFieldScratch)}");
            }
            affinitySnapshot.DirtyObserver = key => RecordOmission($"Affinity {key.Lo}/{key.Hi}",
                AR.s.Npc.TryFindUnmarkedAffinitySaveChange(key, sim.UnmarkedFieldScratch));
            relationSnapshot.DirtyObserver = key => RecordOmission($"Relations {key}",
                AR.s.Npc.TryFindUnmarkedRelationSaveChange(key, sim.UnmarkedFieldScratch));
            partySnapshot.DirtyObserver = key => RecordOmission($"Party {key}",
                AR.s.Npc.TryFindUnmarkedPartySaveChange(key, sim.UnmarkedFieldScratch));
            populationSnapshot.DirtyObserver = key => RecordOmission("Population",
                AR.s.Npc.TryFindUnmarkedPopulationSaveChange(sim.UnmarkedFieldScratch));
        }

        private static void Swap(List<FieldSwap> swaps, object target, string fieldName, object? value)
        {
            FieldInfo field = GetField(target, fieldName);
            FieldSwap swap = new FieldSwap { Target = target, Field = field, Original = field.GetValue(target) };
            swaps.Add(swap);
            field.SetValue(target, value);
        }

        private static FieldInfo GetField(object target, string fieldName)
        {
            FieldInfo? field = target.GetType().GetField(fieldName,
                BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            if (field == null)
                throw new MissingFieldException(target.GetType().Name, fieldName);
            return field;
        }

        /// <summary>생존 NPC 장착 장비 수와 그중 온전한 기본 아이템이 아닌 수 (NPC_SHOP_EQUIPMENT D118·D119).</summary>
        private static void CountEquippedBasic(SimContext sim, out int equipped, out int nonBasic)
        {
            equipped = 0;
            nonBasic = 0;
            for (int i = 0; i < sim.NpcIds.Count; i++)
            {
                NpcSaveData npc = sim.Npcs[sim.NpcIds[i]];
                if (npc.Condition == CharacterConditions.Dead || npc.Activity.Phase == NpcActivityPhase.Dead)
                    continue;
                for (int s = 0; s < npc.InventoryEquip.Length; s++)
                {
                    ItemData? item = npc.InventoryEquip[s];
                    if (item?.Equipment == null)
                        continue;
                    equipped++;
                    if (item.Equipment.IsIntactBasicItem() == false)
                        nonBasic++;
                }
            }
        }

        private static T CloneJson<T>(T source)
        {
            string json = JsonConvert.SerializeObject(source, JsonSettings.Default);
            T? clone = JsonConvert.DeserializeObject<T>(json, JsonSettings.Default);
            if (clone == null)
                throw new InvalidOperationException($"Clone failed: {typeof(T).Name}");
            return clone;
        }

        // ================= 시뮬레이션 루프 (System_NpcSim 재현) =================

        private static void AdvanceVillageProduction(SimContext sim)
        {
            for (int i = 0; i < sim.VillageList.Count; i++)
            {
                VillageData village = sim.VillageList[i];
                int goldBefore = VillageResourceSimulation.GetAmount(village, GlobalEnum.ItemType.Gold);
                AR.s.Village.MarkSaveDirty(village.VillageId);
                int upkeepBurned = VillageResourceSimulation.AdvanceWholeHours(village, 1);
                // 같은 시간에 생산과 금고 유지비(D132 B-보조)가 섞이므로 생산 = 금고 변화 + 유지비로 지운 양, 소멸은 따로 센다.
                sim.Today.VaultUpkeepBurn += upkeepBurned;
                int produced = VillageResourceSimulation.GetAmount(village, GlobalEnum.ItemType.Gold) - goldBefore
                               + upkeepBurned;
                if (produced == 0)
                    continue;
                sim.Today.PopulationGold += produced;
                sim.TotalPopulationGold += produced;
                if (sim.VillageStats.TryGetValue(village.VillageId, out VillageStats? stats))
                    stats.PopulationGold += produced;
            }
        }

        private static void AdvanceParties(SimContext sim, float now)
        {
            List<int> partyIds = new List<int>(sim.Parties.Keys);
            partyIds.Sort();
            for (int i = 0; i < partyIds.Count; i++)
            {
                if (sim.Parties.TryGetValue(partyIds[i], out NpcPartyState? party) == false)
                    continue;
                NpcActivityState activity = party.Activity;
                if (NpcActivityResolver.IsExpedition(activity.Kind) == false || activity.Phase != NpcActivityPhase.Venturing)
                {
                    AR.s.Npc.DissolveParty(party.PartyId);
                    continue;
                }
                if (activity.PhaseEndGameTime > now)
                    continue;

                bool inDuel = false;
                for (int m = 0; m < party.MemberIds.Count; m++)
                {
                    if (AR.s.Npc.IsInPlayerDuel(party.MemberIds[m]) == true)
                        inDuel = true;
                }
                if (inDuel == true)
                    continue;

                if (activity.Kind == NpcActivityKind.DungeonDelve)
                    ResolvePartyDelve(sim, party, activity.PhaseEndGameTime);
                else
                    ResolvePartyHunt(sim, party, activity.PhaseEndGameTime);
            }
        }

        /// <summary>
        /// 이 NPC가 변경 표시 없이 바뀌었는지 본다. 직전 정리 뒤에 다른 NPC 처리·파티·소문 전파가 표시 없이 고친 것이 걸린다.
        /// </summary>
        private static void CheckUnmarkedVillageSaveChanges(SimContext sim, int hour)
        {
            for (int i = 0; i < sim.VillageList.Count; i++)
            {
                CheckUnmarkedVillageSaveChange(sim, sim.VillageList[i].VillageId, hour);
            }
        }

        private static void CheckUnmarkedVillageSaveChange(SimContext sim, int villageId, int hour)
        {
            if (AR.s.Village.TryFindUnmarkedSaveChange(villageId, sim.UnmarkedFieldScratch) == false)
                return;
            sim.UnmarkedVillageSaveChanges++;
            if (sim.UnmarkedSaveSamples.Count < 20)
                sim.UnmarkedSaveSamples.Add($"{hour}h Village {villageId}: {string.Join(", ", sim.UnmarkedFieldScratch)}");
        }

        private static void CheckUnmarkedSaveChange(SimContext sim, int entityId, int hour)
        {
            if (AR.s.Npc.TryFindUnmarkedSaveChange(entityId, sim.UnmarkedFieldScratch) == false)
                return;

            sim.UnmarkedSaveChanges++;
            for (int i = 0; i < sim.UnmarkedFieldScratch.Count; i++)
            {
                string field = sim.UnmarkedFieldScratch[i];
                sim.UnmarkedSaveFields.TryGetValue(field, out int count);
                sim.UnmarkedSaveFields[field] = count + 1;
            }
            if (sim.UnmarkedSaveSamples.Count < 20)
                sim.UnmarkedSaveSamples.Add($"{hour - 1}~{hour}h NPC {entityId}: {string.Join(", ", sim.UnmarkedFieldScratch)}");
        }

        private static string BuildSaveSnapshotOmissionReport(SimContext sim, int finalMismatches)
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("NPC save snapshot omissions: unmarked=").Append(sim.UnmarkedSaveChanges)
              .Append(" final=").Append(finalMismatches);
            List<string> fields = new List<string>(sim.UnmarkedSaveFields.Keys);
            fields.Sort(StringComparer.Ordinal);
            if (fields.Count > 0)
            {
                sb.Append("\nfields:");
                for (int i = 0; i < fields.Count; i++)
                    sb.Append(' ').Append(fields[i]).Append('=').Append(sim.UnmarkedSaveFields[fields[i]]);
            }
            for (int i = 0; i < sim.UnmarkedSaveSamples.Count; i++)
                sb.Append("\n  ").Append(sim.UnmarkedSaveSamples[i]);
            return sb.ToString();
        }

        private static void AdvanceNpc(SimContext sim, int entityId, float now)
        {
            NpcSaveData saveData = sim.Npcs[entityId];
            if (saveData.Condition == CharacterConditions.Dead)
                return;
            if (AR.s.Npc.IsCompanionInDungeon(entityId) == true || AR.s.Npc.IsInPlayerDuel(entityId) == true)
                return;

            float decisionTime = now;
            for (int i = 0; i < MaxPhaseTransitionsPerNpc; i++)
            {
                NpcActivityState activity = saveData.Activity;
                if (activity.Phase == NpcActivityPhase.Dead || activity.PartyId >= 0)
                    return;

                if (activity.Kind == NpcActivityKind.None && activity.Phase == NpcActivityPhase.Idle)
                {
                    // System_NpcSim과 같은 위치에서 표시한다 (활동 시작 직전).
                    AR.s.Npc.MarkSaveDirty(entityId);
                    StartNextActivity(sim, saveData, entityId, decisionTime);
                    continue;
                }

                if (activity.PhaseEndGameTime > now)
                    return;

                // System_NpcSim과 같다: 여기부터는 모든 경로가 이 NPC의 활동 상태를 바꾼다.
                AR.s.Npc.MarkSaveDirty(entityId);
                float transitionTime = activity.PhaseEndGameTime;
                if (transitionTime < activity.PhaseStartedGameTime)
                    transitionTime = now;
                decisionTime = transitionTime;

                if (activity.Phase == NpcActivityPhase.Wounded)
                {
                    SetIdle(saveData, transitionTime);
                    activity.RecoveredGameTime = transitionTime;
                    continue;
                }
                if (activity.Kind == NpcActivityKind.Rest)
                {
                    float restStartGameTime = activity.PhaseStartedGameTime;
                    // P4-0: 절도 조건 깔때기 — 실제 판정 직전 상태로 잰다 (소문 교환은 가방·관계·활동을 바꾸지 않으므로 앞에서 재도 같다)
                    MeasureTheftFunnel(sim, entityId, saveData, restStartGameTime, transitionTime);
                    // System_NpcSim과 같은 Rest 종료 공용 진입점 (D107) — 소문 교환 다음 절도 판정(P4a)까지 게임 코드 그대로.
                    // RNG는 Rest 시작 시각 기준이라 상태 전이 전에 부른다.
                    AR.s.Npc.ResolveRestEnd(entityId, saveData, restStartGameTime, transitionTime, sim.WorldSeed, true);
                    // P4a: 방금 절도가 있었으면 적용 직전 상태와 지금 상태를 대조한다
                    AuditPendingTheft(sim);
                    SetIdle(saveData, transitionTime);
                    continue;
                }
                if (activity.Kind == NpcActivityKind.Trade)
                {
                    SetIdle(saveData, transitionTime);
                    ResolveTrade(sim, saveData, entityId);
                    continue;
                }
                if (activity.Kind == NpcActivityKind.Hunt)
                {
                    ResolveSoloHunt(sim, saveData, entityId, transitionTime);
                    continue;
                }
                if (activity.Kind == NpcActivityKind.DungeonDelve)
                {
                    ResolveSoloDelve(sim, saveData, entityId, transitionTime);
                    continue;
                }
                if (activity.Kind == NpcActivityKind.Relocate)
                {
                    // System_NpcSim과 같은 이주 도착 공용 메서드 (D140)
                    sim.RelocationArrivals++;
                    AR.s.Npc.CompleteRelocation(entityId, saveData, transitionTime);
                    continue;
                }
                SetIdle(saveData, transitionTime);
            }
        }

        private static void SetIdle(NpcSaveData saveData, float gameTime)
        {
            AR.s.Npc.MarkSaveDirty(saveData.EntityId);
            NpcActivityState activity = saveData.Activity;
            activity.Phase = NpcActivityPhase.Idle;
            activity.Kind = NpcActivityKind.None;
            activity.TargetKey = string.Empty;
            activity.HasMovementPath = false;
            activity.PhaseStartedGameTime = gameTime;
            activity.PhaseEndGameTime = 0f;
            saveData.LastSimGameTime = gameTime;
        }

        private static void StartNextActivity(SimContext sim, NpcSaveData saveData, int entityId, float startGameTime)
        {
            bool reserved = AR.s.Npc.IsCompanionReserved(entityId);
            // System_NpcSim과 같은 순서: 사라진 터 체류 기한이 지났으면 효용 선택 전에 이주를 시작한다 (D139·D140). 선택 비중에는 넣지 않는다.
            if (reserved == false && AR.s.Npc.TryStartRelocation(entityId, saveData, startGameTime, sim.WorldSeed) == true)
            {
                sim.RelocationStarts++;
                sim.RelocationTravelHours += saveData.Activity.PhaseEndGameTime - startGameTime;
                return;
            }
            // 선택 시점의 "유망한 소문" 보유 여부 (Delve 비중 비교용). 같은 입력으로 SelectNextActivity 안에서도 다시 계산된다.
            bool promisingHolder = reserved == false &&
                                   AR.s.Npc.BuildActivityChoiceContext(entityId, saveData, startGameTime).HasPromisingRumor;
            NpcActivityKind kind = reserved == true
                ? NpcActivityKind.Rest
                : AR.s.Npc.SelectNextActivity(entityId, saveData, startGameTime, sim.WorldSeed);
            if (reserved == false)
            {
                if (promisingHolder == true)
                {
                    sim.HolderSelections++;
                    if (kind == NpcActivityKind.DungeonDelve)
                        sim.HolderDelve++;
                }
                else
                {
                    sim.NonHolderSelections++;
                    if (kind == NpcActivityKind.DungeonDelve)
                        sim.NonHolderDelve++;
                }
            }

            // 선택 비중은 효용 선택 결과만 센다 (동행 예약 강제 Rest와 파티 모집으로 끌려간 경우 제외).
            if (reserved == false)
            {
                NpcStats stats = sim.NpcStats[entityId];
                if (kind == NpcActivityKind.Hunt) { sim.Today.SelectHunt++; stats.Hunt++; }
                else if (kind == NpcActivityKind.Trade) { sim.Today.SelectTrade++; stats.Trade++; }
                else if (kind == NpcActivityKind.DungeonDelve) { sim.Today.SelectDelve++; stats.Delve++; }
                else { sim.Today.SelectRest++; stats.Rest++; }
            }

            if (kind == NpcActivityKind.Hunt &&
                AR.s.Npc.TryCreateHuntParty(entityId, kind, startGameTime, sim.WorldSeed, out NpcPartyState? party) &&
                party != null)
            {
                sim.PartyHuntStarts++;
                StartPartyHunt(party, startGameTime);
                return;
            }

            // DungeonDelve: System_NpcSim과 같은 순서 — 파티 우선, 아니면 솔로. 대상 던전은 실제 매니저 코드로 고른다.
            string delveDungeonId = string.Empty;
            if (kind == NpcActivityKind.DungeonDelve)
            {
                if (AR.s.Npc.TryCreateHuntParty(entityId, kind, startGameTime, sim.WorldSeed,
                        out NpcPartyState? delveParty) && delveParty != null)
                {
                    sim.PartyDelveStarts++;
                    StartPartyDelve(sim, delveParty, startGameTime);
                    return;
                }
                if (AR.s.Npc.TryPrepareDelveTarget(entityId, entityId, saveData.VillageId, saveData.Position,
                        startGameTime, sim.WorldSeed, out delveDungeonId, out _, out bool fromRumor) == false)
                    kind = NpcActivityKind.Rest;
                else if (fromRumor == true)
                    sim.ConfirmationSolo.Add(entityId);
            }

            if (kind == NpcActivityKind.Hunt)
                sim.SoloHuntStarts++;
            else if (kind == NpcActivityKind.DungeonDelve)
                sim.SoloDelveStarts++;

            NpcActivityState activity = saveData.Activity;
            activity.Kind = kind;
            activity.Phase = NpcActivityResolver.IsExpedition(kind) ? NpcActivityPhase.Venturing : NpcActivityPhase.Idle;
            activity.TargetKey = kind == NpcActivityKind.Hunt ? "hunt"
                : kind == NpcActivityKind.Trade ? "trade"
                : kind == NpcActivityKind.DungeonDelve ? delveDungeonId : "rest";
            activity.PhaseStartedGameTime = startGameTime;
            activity.PhaseEndGameTime = startGameTime + NpcActivityResolver.GetActivityDurationHours(kind);
            activity.HasMovementPath = false;
            activity.OutcomeSeedGameTime = startGameTime;
            saveData.LastSimGameTime = startGameTime;
        }

        private static void StartPartyHunt(NpcPartyState party, float startGameTime)
        {
            AR.s.Npc.MarkPartySaveDirty(party.PartyId);
            NpcActivityState activity = party.Activity;
            activity.PartyId = party.PartyId;
            activity.Kind = NpcActivityKind.Hunt;
            activity.Phase = NpcActivityPhase.Venturing;
            activity.TargetKey = "hunt";
            activity.PhaseStartedGameTime = startGameTime;
            activity.PhaseEndGameTime = startGameTime + NpcActivityResolver.HuntDurationHours;
            activity.HasMovementPath = false;
            activity.OutcomeSeedGameTime = startGameTime;
            for (int i = 0; i < party.MemberIds.Count; i++)
            {
                NpcSaveData? member = AR.s.Npc.GetSaveData(party.MemberIds[i]);
                if (member == null)
                    continue;
                AR.s.Npc.MarkSaveDirty(party.MemberIds[i]);
                member.Activity.PartyId = party.PartyId;
                member.Activity.Kind = NpcActivityKind.Hunt;
                member.Activity.Phase = NpcActivityPhase.Venturing;
                member.Activity.TargetKey = "hunt";
                member.Activity.HasMovementPath = false;
                member.LastSimGameTime = startGameTime;
            }
        }

        private static void StartPartyDelve(SimContext sim, NpcPartyState party, float startGameTime)
        {
            NpcSaveData? leader = AR.s.Npc.GetSaveData(party.LeaderId);
            bool fromRumor = false;
            if (leader == null || AR.s.Npc.TryPrepareDelveTarget(party.PartyId, party.LeaderId, leader.VillageId,
                    leader.Position, startGameTime, sim.WorldSeed, out string dungeonId, out _, out fromRumor) == false)
            {
                for (int i = 0; i < party.MemberIds.Count; i++)
                {
                    NpcSaveData? member = AR.s.Npc.GetSaveData(party.MemberIds[i]);
                    if (member == null)
                        continue;
                    AR.s.Npc.MarkSaveDirty(party.MemberIds[i]);
                    member.Activity.Kind = NpcActivityKind.None;
                    member.Activity.Phase = NpcActivityPhase.Idle;
                    member.Activity.TargetKey = string.Empty;
                }
                AR.s.Npc.DissolveParty(party.PartyId);
                return;
            }

            if (fromRumor == true)
                sim.ConfirmationParties.Add(party.PartyId);
            AR.s.Npc.MarkPartySaveDirty(party.PartyId);
            NpcActivityState activity = party.Activity;
            activity.PartyId = party.PartyId;
            activity.Kind = NpcActivityKind.DungeonDelve;
            activity.Phase = NpcActivityPhase.Venturing;
            activity.TargetKey = dungeonId;
            activity.PhaseStartedGameTime = startGameTime;
            activity.PhaseEndGameTime = startGameTime + NpcActivityResolver.DelveDurationHours;
            activity.HasMovementPath = false;
            activity.OutcomeSeedGameTime = startGameTime;
            for (int i = 0; i < party.MemberIds.Count; i++)
            {
                NpcSaveData? member = AR.s.Npc.GetSaveData(party.MemberIds[i]);
                if (member == null)
                    continue;
                AR.s.Npc.MarkSaveDirty(party.MemberIds[i]);
                member.Activity.PartyId = party.PartyId;
                member.Activity.Kind = NpcActivityKind.DungeonDelve;
                member.Activity.Phase = NpcActivityPhase.Venturing;
                member.Activity.TargetKey = dungeonId;
                member.Activity.HasMovementPath = false;
                member.LastSimGameTime = startGameTime;
            }
        }

        private static void ResolveSoloDelve(SimContext sim, NpcSaveData saveData, int entityId, float outcomeGameTime)
        {
            float powerBefore = AR.s.Npc.GetNpcPower(entityId);
            NpcDelveReport report = AR.s.Npc.ResolveSoloDelve(entityId, saveData, outcomeGameTime, sim.WorldSeed);
            CountDelveReport(sim, report);
            NpcHuntOutcome outcome = report.Outcomes.Count > 0 ? report.Outcomes[0] : NpcHuntOutcome.Failed;
            if (sim.ConfirmationSolo.Remove(entityId) == true)
            {
                sim.ConfirmationJudgments++;
                if (outcome == NpcHuntOutcome.Success)
                    sim.ConfirmationSuccess++;
            }
            if (outcome == NpcHuntOutcome.Dead)
            {
                MarkDead(sim, saveData, entityId, outcomeGameTime);
                return;
            }
            if (outcome == NpcHuntOutcome.Wounded)
            {
                CountTreatment(sim, saveData, report.Treated[0]);
                CountSevere(sim, report.Severe[0]);
            }
            if (AR.s.Npc.GetNpcPower(entityId) > powerBefore + PowerEpsilon)
                sim.Today.UpgradesDelve++;
        }

        private static void ResolvePartyDelve(SimContext sim, NpcPartyState party, float outcomeGameTime)
        {
            List<int> ids = new List<int>(party.MemberIds);
            float[] powerBefore = new float[ids.Count];
            for (int i = 0; i < ids.Count; i++)
                powerBefore[i] = AR.s.Npc.GetNpcPower(ids[i]);

            bool confirmation = sim.ConfirmationParties.Remove(party.PartyId);
            NpcDelveReport? report = AR.s.Npc.ResolvePartyDelve(party, outcomeGameTime, sim.WorldSeed);
            if (report == null)
                return;
            // System_NpcSim.ResolvePartyDelve와 같은 시점의 파티 호감도 변화 (D107)
            AR.s.Npc.ApplyPartyRelationshipOutcome(report.MemberIds, report.Outcomes, report.SuccessCount > 0,
                outcomeGameTime, NpcActivityKind.DungeonDelve, report.Completed);
            CountDelveReport(sim, report);
            if (confirmation == true)
            {
                for (int i = 0; i < report.Outcomes.Count; i++)
                {
                    sim.ConfirmationJudgments++;
                    if (report.Outcomes[i] == NpcHuntOutcome.Success)
                        sim.ConfirmationSuccess++;
                }
            }
            for (int i = 0; i < report.MemberIds.Count; i++)
            {
                int memberId = report.MemberIds[i];
                NpcSaveData member = sim.Npcs[memberId];
                if (report.Outcomes[i] == NpcHuntOutcome.Dead)
                {
                    MarkDead(sim, member, memberId, outcomeGameTime);
                    continue;
                }
                if (report.Outcomes[i] == NpcHuntOutcome.Wounded)
                {
                    CountTreatment(sim, member, report.Treated[i]);
                    CountSevere(sim, report.Severe[i]);
                }
                int before = ids.IndexOf(memberId);
                if (before >= 0 && AR.s.Npc.GetNpcPower(memberId) > powerBefore[before] + PowerEpsilon)
                    sim.Today.UpgradesDelve++;
            }
        }

        private static void CountDelveReport(SimContext sim, NpcDelveReport report)
        {
            DayRow today = sim.Today;
            today.ExpectedDeaths += report.ExpectedDeaths;
            today.DelveDungeonChangedFailures += report.DungeonChangedFailures;
            if (report.IsParty == true)
                sim.PartyDelveJudgments += report.Outcomes.Count;
            else
                sim.SoloDelveJudgments += report.Outcomes.Count;
            for (int i = 0; i < report.Outcomes.Count; i++)
            {
                today.DelveOutcomes++;
                if (report.Outcomes[i] == NpcHuntOutcome.Success)
                    today.DelveSuccess++;
                else if (report.Outcomes[i] == NpcHuntOutcome.Wounded)
                    today.Wounded++;
            }
            sim.VillageStats.TryGetValue(report.VillageId, out VillageStats? villageStats);
            if (villageStats != null)
                villageStats.Delves++;

            if (report.CompletionRollPassed == false)
                return;
            today.DelveCompletionRolls++;
            sim.CompletionResults.TryGetValue(report.CompletionResult, out int count);
            sim.CompletionResults[report.CompletionResult] = count + 1;
            if (report.Completed == false)
                return;

            today.NpcCompletions++;
            if (villageStats != null)
                villageStats.NpcCompletions++;
            // 보호 규칙과 독립된 검사: 완료된 던전(이전 세대 ID)에 플레이어 DungeonRecord가 있었는가
            for (int i = 0; i < sim.Dungeons.Dungeons.Count; i++)
            {
                if (sim.Dungeons.Dungeons[i].DungeonId == report.DungeonId)
                {
                    sim.CompletionsOnPlayerEntered++;
                    break;
                }
            }
            if (sim.CompletionExamples.Count < MaxExampleCases)
            {
                sim.CompletionExamples.Add("t=" + AR.s.Time.CurrentGameTime.ToString("F0", CultureInfo.InvariantCulture) +
                    " village=" + report.VillageId + " dungeon=" + report.DungeonId + " party=" + report.IsParty +
                    " members=" + string.Join("/", report.MemberIds) +
                    " capability=" + report.CompletionCapability.ToString("F3", CultureInfo.InvariantCulture) +
                    " chance=" + report.CompletionChance.ToString("P1", CultureInfo.InvariantCulture) +
                    " loot=" + report.LootCount);
            }
        }

        private static void ResolveTrade(SimContext sim, NpcSaveData saveData, int entityId)
        {
            if (saveData.VillageId < 0)
                return;

            float powerBefore = AR.s.Npc.GetNpcPower(entityId);
            int goldBefore = saveData.Gold;
            NpcTradeReport report = AR.s.Npc.TradeAtShop(entityId, saveData.VillageId);
            if (report.ShopAvailable == false)
                return;

            float powerAfter = AR.s.Npc.GetNpcPower(entityId);
            NpcStats npcStats = sim.NpcStats[entityId];
            DayRow today = sim.Today;
            today.TradesExecuted++;
            today.Sold += report.SoldCount;
            today.SoldGold += report.SoldGold;
            today.SellRejected += report.SellRejectedCount;
            if (report.Bought == true)
            {
                today.Bought++;
                today.BoughtGold += report.BoughtPrice;
                if (report.BoughtBasicItemId <= 0)
                    sim.NonBasicPurchases++;
            }

            if (sim.VillageStats.TryGetValue(saveData.VillageId, out VillageStats? villageStats))
            {
                villageStats.Sold += report.SoldCount;
                villageStats.SoldGold += report.SoldGold;
                villageStats.SellRejected += report.SellRejectedCount;
                if (report.Bought == true)
                {
                    villageStats.Bought++;
                    villageStats.BoughtGold += report.BoughtPrice;
                }
            }

            npcStats.SoldCount += report.SoldCount;
            npcStats.SoldGold += report.SoldGold;

            if (powerAfter > powerBefore + PowerEpsilon)
            {
                today.UpgradesTrade++;
                // 완료 기준 사례: 판매로 번 골드(이번 또는 이전 거래) → 구매 → 전투력·capability 상승
                if (npcStats.SoldCount > 0 && sim.Examples.Count < MaxExampleCases)
                {
                    float cap = NpcManager.GetNpcPowerCap(saveData);
                    sim.Examples.Add(
                        $"npc={entityId}({AR.s.Npc.GetDisplayName(entityId)}) v={saveData.VillageId} " +
                        $"t={AR.s.Time.CurrentGameTime:F0} 누적판매={npcStats.SoldCount}건/{npcStats.SoldGold}G " +
                        $"이번거래 판매={report.SoldCount}건/{report.SoldGold}G 구매={report.BoughtItemId}@{report.BoughtPrice}G " +
                        $"gold={goldBefore}->{saveData.Gold} power={powerBefore:F2}->{powerAfter:F2} cap={cap:F2} " +
                        $"capability={NpcActivityResolver.GetHuntCapability(powerBefore):F3}->" +
                        $"{NpcActivityResolver.GetHuntCapability(powerAfter):F3}");
                }
            }
        }

        private static void ResolveSoloHunt(SimContext sim, NpcSaveData saveData, int entityId, float outcomeGameTime)
        {
            NpcActivityState activity = saveData.Activity;
            float seedGameTime = activity.OutcomeSeedGameTime > 0f ? activity.OutcomeSeedGameTime : activity.PhaseStartedGameTime;
            float capability = AR.s.Npc.GetNpcHuntCapability(entityId);
            NpcHuntOutcome outcome = NpcActivityResolver.ResolveHunt(capability, entityId, seedGameTime, sim.WorldSeed);
            NpcActivityResolver.GetSoloHuntChances(capability, out float soloDeathChance, out _, out _);
            sim.Today.ExpectedDeaths += soloDeathChance;

            activity.LastHuntOutcome = outcome;
            activity.LastHuntFoodYield = 0;
            activity.LastOutcomeGameTime = outcomeGameTime;
            activity.Kind = NpcActivityKind.None;
            activity.TargetKey = string.Empty;
            activity.HasMovementPath = false;
            activity.PhaseStartedGameTime = outcomeGameTime;
            saveData.LastSimGameTime = outcomeGameTime;
            CountHuntOutcome(sim, outcome);
            sim.SoloHuntJudgments++;

            if (outcome == NpcHuntOutcome.Dead)
            {
                MarkDead(sim, saveData, entityId, outcomeGameTime);
                return;
            }
            if (outcome == NpcHuntOutcome.Wounded)
            {
                // 실제 System_NpcSim과 같은 공용 부상 처리 (중상·치료·상태 전이·기록, D91)
                AR.s.Npc.ApplyExpeditionWound(entityId, saveData, NpcActivityKind.Hunt, entityId, seedGameTime,
                    sim.WorldSeed, NpcActivityResolver.TreatmentSalt, NpcActivityResolver.SevereWoundHuntSalt,
                    outcomeGameTime, out bool treated, out bool severe);
                CountTreatment(sim, saveData, treated);
                CountSevere(sim, severe);
                return;
            }

            activity.Phase = NpcActivityPhase.Idle;
            activity.PhaseEndGameTime = 0f;
            if (outcome != NpcHuntOutcome.Success)
                return;

            if (saveData.VillageId >= 0)
            {
                int foodYield = NpcActivityResolver.GetHuntFoodYield(capability, entityId, seedGameTime, sim.WorldSeed);
                activity.LastHuntFoodYield = foodYield;
                ProduceFood(sim, saveData.VillageId, foodYield);
            }
            GrantLoot(sim, saveData, entityId, entityId, seedGameTime, NpcActivityResolver.HuntLootSalt, outcomeGameTime);
        }

        private static void ResolvePartyHunt(SimContext sim, NpcPartyState party, float outcomeGameTime)
        {
            List<NpcSaveData> members = new List<NpcSaveData>(party.MemberIds.Count);
            List<int> memberIds = new List<int>(party.MemberIds.Count);
            for (int i = 0; i < party.MemberIds.Count; i++)
            {
                NpcSaveData? member = AR.s.Npc.GetSaveData(party.MemberIds[i]);
                if (member == null || member.Condition == CharacterConditions.Dead)
                    continue;
                members.Add(member);
                memberIds.Add(party.MemberIds[i]);
            }
            if (members.Count < 2)
            {
                AR.s.Npc.DissolveParty(party.PartyId);
                return;
            }

            NpcActivityState partyActivity = party.Activity;
            float seedGameTime = partyActivity.OutcomeSeedGameTime;
            float[] capabilities = new float[members.Count];
            for (int i = 0; i < members.Count; i++)
                capabilities[i] = AR.s.Npc.GetNpcHuntCapability(memberIds[i]);
            float partyPower = NpcActivityResolver.GetPartyHuntPower(capabilities);

            NpcHuntOutcome[] outcomes = new NpcHuntOutcome[members.Count];
            int successCount = 0;
            for (int i = 0; i < members.Count; i++)
            {
                outcomes[i] = NpcActivityResolver.ResolvePartyMemberHunt(capabilities[i], partyPower, members.Count,
                    party.PartyId, seedGameTime, sim.WorldSeed, i);
                NpcActivityResolver.GetPartyMemberHuntChances(
                    NpcActivityResolver.GetPartyMemberCapability(capabilities[i], partyPower, members.Count),
                    out float memberDeathChance, out _, out _);
                sim.Today.ExpectedDeaths += memberDeathChance;
                if (outcomes[i] == NpcHuntOutcome.Success)
                    successCount++;
                CountHuntOutcome(sim, outcomes[i]);
                sim.PartyHuntJudgments++;
            }

            int foodYield = successCount > 0
                ? NpcActivityResolver.GetPartyHuntFoodYield(partyPower, party.PartyId, seedGameTime, sim.WorldSeed)
                : 0;

            for (int i = 0; i < members.Count; i++)
            {
                AR.s.Npc.MarkSaveDirty(memberIds[i]);
                NpcSaveData member = members[i];
                NpcActivityState activity = member.Activity;
                activity.LastHuntOutcome = outcomes[i];
                activity.LastOutcomeGameTime = outcomeGameTime;
                activity.HasMovementPath = false;
                activity.TargetKey = string.Empty;
                activity.PhaseStartedGameTime = outcomeGameTime;
                member.LastSimGameTime = outcomeGameTime;

                if (outcomes[i] == NpcHuntOutcome.Wounded)
                {
                    AR.s.Npc.ApplyExpeditionWound(memberIds[i], member, NpcActivityKind.Hunt, party.PartyId, seedGameTime,
                        sim.WorldSeed, NpcActivityResolver.PartyTreatmentSaltBase + i,
                        NpcActivityResolver.PartySevereWoundHuntSaltBase + i, outcomeGameTime, out bool treated,
                        out bool severe);
                    CountTreatment(sim, member, treated);
                    CountSevere(sim, severe);
                }
                else if (outcomes[i] != NpcHuntOutcome.Dead)
                {
                    activity.Phase = NpcActivityPhase.Idle;
                    activity.Kind = NpcActivityKind.None;
                    activity.PhaseEndGameTime = 0f;
                }
            }

            AR.s.Npc.DissolveParty(party.PartyId);
            if (foodYield > 0 && members[0].VillageId >= 0)
                ProduceFood(sim, members[0].VillageId, foodYield);

            for (int i = 0; i < members.Count; i++)
            {
                if (outcomes[i] == NpcHuntOutcome.Success)
                    GrantLoot(sim, members[i], memberIds[i], party.PartyId, seedGameTime,
                        NpcActivityResolver.PartyHuntLootSaltBase + i, outcomeGameTime);
            }
            // System_NpcSim.ResolvePartyHunt와 같은 시점(전리품 뒤, 사망 처리 전)의 파티 호감도 변화 (D107)
            AR.s.Npc.ApplyPartyRelationshipOutcome(memberIds, outcomes, successCount > 0, outcomeGameTime,
                NpcActivityKind.Hunt);
            for (int i = 0; i < members.Count; i++)
            {
                if (outcomes[i] == NpcHuntOutcome.Dead)
                    MarkDead(sim, members[i], memberIds[i], outcomeGameTime);
            }
        }

        private static void GrantLoot(SimContext sim, NpcSaveData saveData, int entityId, int rngOwnerId,
            float seedGameTime, int salt, float outcomeGameTime)
        {
            int zoneLevel = AR.s.Npc.GetNpcLootZoneLevel(saveData);
            ItemData? loot = NpcActivityResolver.RollHuntLoot(rngOwnerId, seedGameTime, sim.WorldSeed, salt, zoneLevel);
            if (loot == null)
                return;

            float powerBefore = AR.s.Npc.GetNpcPower(entityId);
            AR.s.Npc.GrantHuntLoot(entityId, saveData, loot, outcomeGameTime);
            if (AR.s.Npc.GetNpcPower(entityId) > powerBefore + PowerEpsilon)
                sim.Today.UpgradesLoot++;
        }

        private static void ProduceFood(SimContext sim, int villageId, int amount)
        {
            if (sim.Villages.TryGetValue(villageId, out VillageData? village) == false)
                return;
            AR.s.Village.MarkSaveDirty(villageId);
            int produced = VillageResourceSimulation.Produce(village, GlobalEnum.ItemType.Food, amount);
            sim.Today.FoodYieldRaw += amount;
            sim.Today.FoodInflow += produced;
            sim.TotalFoodInflow += produced;
            if (sim.VillageStats.TryGetValue(villageId, out VillageStats? stats))
                stats.FoodInflow += produced;
        }

        /// <summary>
        /// 사망 정리 축약판: 순회·마을 명단에서 빼고 소지 Gold는 소멸 (D46). 소문·호감도·유형 관계는 실제 MarkNpcDead와 같은
        /// 메서드로 정리한다(복제본이 끼워져 있다, D107). 연대기·Deed·고인 기억(DeceasedRelations)은 다루지 않는다.
        /// </summary>
        private static void MarkDead(SimContext sim, NpcSaveData saveData, int entityId, float when)
        {
            AR.s.Npc.MarkSaveDirty(entityId);
            if (saveData.Condition == CharacterConditions.Dead)
                return;
            sim.Today.Deaths++;
            sim.Today.GoldBurned += saveData.Gold;
            sim.TotalGoldBurned += saveData.Gold;
            saveData.Condition = CharacterConditions.Dead;
            saveData.Activity.Phase = NpcActivityPhase.Dead;
            saveData.Activity.Kind = NpcActivityKind.None;
            saveData.Activity.PhaseEndGameTime = 0f;
            saveData.LastSimGameTime = when;
            // 실제 MarkNpcDead와 같은 소문 정리 (D72) — 명단 제거 전
            AR.s.Npc.PurgeRumorsOnNpcDeath(entityId, saveData);
            AR.s.Npc.RemoveAllAffinityInvolving(entityId);
            AR.s.Npc.RemoveAllRelationsInvolving(entityId);
            if (sim.Villages.TryGetValue(saveData.VillageId, out VillageData? village))
                village.NpcEntityIds.Remove(entityId);
            AR.s.Npc.InvalidateNpcPower(entityId);
        }

        private static void CountHuntOutcome(SimContext sim, NpcHuntOutcome outcome)
        {
            sim.Today.HuntOutcomes++;
            if (outcome == NpcHuntOutcome.Success)
                sim.Today.HuntSuccess++;
            else if (outcome == NpcHuntOutcome.Wounded)
                sim.Today.Wounded++;
        }

        private static void CountSevere(SimContext sim, bool severe)
        {
            if (severe == true)
                sim.Today.SevereWounds++;
        }

        private static long SumWoundCount(SimContext sim)
        {
            long total = 0;
            for (int i = 0; i < sim.NpcIds.Count; i++)
                total += sim.Npcs[sim.NpcIds[i]].WoundCount;
            return total;
        }

        private static void CountTreatment(SimContext sim, NpcSaveData saveData, bool treated)
        {
            if (treated == false)
                return;
            sim.Today.Treatments++;
            if (sim.VillageStats.TryGetValue(saveData.VillageId, out VillageStats? stats))
                stats.Treatments++;
        }

        // ================= 집계 · 출력 =================

        private static void CollectStartPopulation(SimContext sim, List<int> cowardIds, out int cowardCourageMax,
            Dictionary<NpcAmbitionKind, int> ambitionCounts)
        {
            List<int> living = new List<int>();
            for (int i = 0; i < sim.NpcIds.Count; i++)
            {
                NpcSaveData saveData = sim.Npcs[sim.NpcIds[i]];
                if (saveData.Condition == CharacterConditions.Dead || saveData.Activity.Phase == NpcActivityPhase.Dead)
                    continue;
                living.Add(sim.NpcIds[i]);
                sim.CohortIds.Add(sim.NpcIds[i]);
                NpcAmbitionKind primary = NpcAmbitionEvaluator.Evaluate(saveData.NpcStat).Primary;
                ambitionCounts.TryGetValue(primary, out int count);
                ambitionCounts[primary] = count + 1;
            }

            living.Sort((a, b) =>
            {
                int courage = sim.Npcs[a].NpcStat.Courage.CompareTo(sim.Npcs[b].NpcStat.Courage);
                return courage != 0 ? courage : a.CompareTo(b);
            });
            int cowardCount = Mathf.Max(1, Mathf.CeilToInt(living.Count * CowardFraction));
            cowardCourageMax = 0;
            for (int i = 0; i < cowardCount && i < living.Count; i++)
            {
                cowardIds.Add(living[i]);
                cowardCourageMax = sim.Npcs[living[i]].NpcStat.Courage;
            }
        }

        private static DayRow RecordDay(SimContext sim, int day, float gameTime)
        {
            DayRow row = sim.Today;
            row.Day = day;
            row.GameTime = gameTime;
            RecordRumorDay(sim, row, gameTime);
            RecordGoldDay(sim, row);

            List<float> powers = new List<float>();
            double powerSum = 0d;
            double survivorStartSum = 0d;
            for (int i = 0; i < sim.NpcIds.Count; i++)
            {
                int entityId = sim.NpcIds[i];
                NpcSaveData saveData = sim.Npcs[entityId];
                if (saveData.Condition == CharacterConditions.Dead || saveData.Activity.Phase == NpcActivityPhase.Dead)
                    continue;

                float power = AR.s.Npc.GetNpcPower(entityId);
                powers.Add(power);
                powerSum += power;
                if (day == 0)
                    sim.StartPower[entityId] = power;
                if (sim.StartPower.TryGetValue(entityId, out float startPower))
                    survivorStartSum += startPower;
                row.NpcGold += saveData.Gold;

                float cap = NpcManager.GetNpcPowerCap(saveData);
                if (cap > 0f)
                {
                    float ratio = power / cap;
                    if (ratio > row.MaxCapRatio)
                        row.MaxCapRatio = ratio;
                    if (power > cap * (1f + PowerEpsilon))
                        row.CapExceed++;
                }
            }

            row.Living = powers.Count;
            if (powers.Count > 0)
            {
                powers.Sort();
                row.AvgPower = (float)(powerSum / powers.Count);
                row.SurvivorStartAvgPower = (float)(survivorStartSum / powers.Count);
                row.P98Power = powers[Mathf.Clamp(Mathf.FloorToInt((powers.Count - 1) * 0.98f), 0, powers.Count - 1)];
            }

            if (sim.CohortIds.Count > 0)
            {
                double cohortSum = 0d;
                for (int i = 0; i < sim.CohortIds.Count; i++)
                    cohortSum += AR.s.Npc.GetNpcPower(sim.CohortIds[i]);
                row.CohortAvgPower = (float)(cohortSum / sim.CohortIds.Count);
            }

            for (int i = 0; i < sim.VillageList.Count; i++)
                row.VillageGold += VillageResourceSimulation.GetAmount(sim.VillageList[i], GlobalEnum.ItemType.Gold);

            // 마을 요약의 종료값은 매일 덮어써 마지막 날 값이 남는다.
            for (int i = 0; i < sim.VillageList.Count; i++)
            {
                VillageData village = sim.VillageList[i];
                if (sim.VillageStats.TryGetValue(village.VillageId, out VillageStats? stats) == false)
                    continue;
                stats.GoldEnd = VillageResourceSimulation.GetAmount(village, GlobalEnum.ItemType.Gold);
                stats.NpcEnd = CountLivingInVillage(sim, village);
            }
            return row;
        }

        /// <summary>GOLD_BALANCE 일별 기록: 기록 시각의 통화량 비율·물가 지수, 그날의 생활비 카운터 증가분.</summary>
        private static void RecordGoldDay(SimContext sim, DayRow row)
        {
            row.GoldPriceTarget = GoldPriceRules.GetTarget(sim.GoldPrice, AR.s.Village.ComputeWorldGold());
            row.GoldPriceIndex = sim.GoldPrice.Index;
            NpcGoldFlowCounters flow = sim.GoldFlow;
            row.LivingSpendCount = flow.LivingSpendCount - sim.LivingCountAtDayStart;
            row.LivingSpendDeposit = flow.LivingSpendDeposited - sim.LivingDepositAtDayStart;
            row.LivingSpendBurn = flow.LivingSpendBurned - sim.LivingBurnAtDayStart;
            sim.LivingCountAtDayStart = flow.LivingSpendCount;
            sim.LivingDepositAtDayStart = flow.LivingSpendDeposited;
            sim.LivingBurnAtDayStart = flow.LivingSpendBurned;
        }

        /// <summary>사라진 터(폐허·합병)를 소속 마을로 가진 생존 NPC 수 (NPC_AMBITION D138~D141).</summary>
        private static int CountAbandonedSiteResidents(SimContext sim)
        {
            int count = 0;
            for (int i = 0; i < sim.NpcIds.Count; i++)
            {
                NpcSaveData saveData = sim.Npcs[sim.NpcIds[i]];
                if (saveData.Condition == CharacterConditions.Dead)
                    continue;
                if (sim.Villages.TryGetValue(saveData.VillageId, out VillageData? village) == true &&
                    AR.s.Village.IsAbandonedSite(village) == true)
                    count++;
            }
            return count;
        }

        /// <summary>
        /// 사라진 터 강제 이주 요약 (D139·D140). 체류 기한 72h + 이동(최대 약 9h)이 지나면 터 소속 생존 NPC는 0이어야 한다
        /// (4일 이상 실행 기준. 화면 안 NPC는 진단에서 모두 화면 밖으로 본다).
        /// </summary>
        private static void AppendRelocationSummary(StringBuilder sb, SimContext sim, int days)
        {
            int residentsEnd = CountAbandonedSiteResidents(sim);
            float averageHours = sim.RelocationStarts > 0 ? sim.RelocationTravelHours / sim.RelocationStarts : 0f;
            string expectation = days >= 4 ? " (4일 이상 실행 — 끝 0이어야 함)" : string.Empty;
            sb.AppendLine($"이주(사라진 터 → 가장 가까운 상점 마을): 시작 {sim.RelocationStarts} · 도착 {sim.RelocationArrivals} · " +
                          $"평균 이동 {averageHours:F1}h | 터 소속 생존 NPC 시작 {sim.AbandonedResidentsAtStart} → 끝 {residentsEnd}{expectation}");
        }

        private static int CountLivingInVillage(SimContext sim, VillageData village)
        {
            int count = 0;
            for (int i = 0; i < village.NpcEntityIds.Count; i++)
            {
                if (sim.Npcs.TryGetValue(village.NpcEntityIds[i], out NpcSaveData? saveData) &&
                    saveData.Condition != CharacterConditions.Dead)
                    count++;
            }
            return count;
        }

        private static string BuildDailyCsv(List<DayRow> rows)
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("day,game_time,living_npcs,avg_power,avg_power_change_pct,cohort_avg_power,cohort_avg_power_change_pct,survivor_start_avg_power,survivorship_pct,survivor_growth_pct,p98_power,cap_exceed,max_power_cap_ratio," +
                          "upgrades_loot,upgrades_trade,upgrades_delve,select_rest,select_hunt,select_trade,select_delve," +
                          "rest_pct,hunt_pct,trade_pct,delve_pct," +
                          "hunt_outcomes,hunt_success_pct,delve_outcomes,delve_success_pct,delve_completion_rolls,delve_dungeon_changed_failures,npc_completions,wounded,severe_wounds,treatments,deaths,expected_deaths,gold_burned,world_gold,village_gold,npc_gold," +
                          "population_gold_produced,world_gold_conservation_error,food_inflow,food_yield_raw,trades_executed," +
                          "npc_sold,npc_sold_gold,npc_sell_rejected,npc_bought,npc_bought_gold," +
                          "rumor_exchange_attempts,rumor_transfers,rumors_held,rumor_holders,promising_holders,rumor_stored_invalid,confirmation_delves," +
                          "gold_price_target,gold_price_index,living_spend_count,living_spend_deposit,living_spend_burn,vault_upkeep_burn");
            float baseAvg = rows.Count > 0 ? rows[0].AvgPower : 0f;
            float baseCohort = rows.Count > 0 ? rows[0].CohortAvgPower : 0f;
            long baseWorld = rows.Count > 0 ? rows[0].VillageGold + rows[0].NpcGold : 0;
            long cumulativeBurned = 0;
            long cumulativePopulation = 0;
            for (int i = 0; i < rows.Count; i++)
            {
                DayRow r = rows[i];
                if (i > 0)
                {
                    cumulativeBurned += r.GoldBurned + r.LivingSpendBurn + r.VaultUpkeepBurn;
                    cumulativePopulation += r.PopulationGold;
                }
                int selections = r.SelectRest + r.SelectHunt + r.SelectTrade + r.SelectDelve;
                long world = r.VillageGold + r.NpcGold;
                // 닫힌 경제 검산: 오늘 세계 Gold = 시작 + 인구 생산 누계 − 소멸 누계(사망 + 생활비 소멸 + 금고 유지비).
                // 0이 아니면 새는 경로가 있다.
                long conservationError = world - (baseWorld + cumulativePopulation - cumulativeBurned);
                sb.Append(r.Day).Append(',')
                  .Append(F(r.GameTime, 1)).Append(',')
                  .Append(r.Living).Append(',')
                  .Append(F(r.AvgPower, 3)).Append(',')
                  .Append(F(baseAvg > 0f ? (r.AvgPower / baseAvg - 1f) * 100f : 0f, 3)).Append(',')
                  .Append(F(r.CohortAvgPower, 3)).Append(',')
                  .Append(F(baseCohort > 0f ? (r.CohortAvgPower / baseCohort - 1f) * 100f : 0f, 3)).Append(',')
                  .Append(F(r.SurvivorStartAvgPower, 3)).Append(',')
                  .Append(F(baseAvg > 0f ? (r.SurvivorStartAvgPower / baseAvg - 1f) * 100f : 0f, 3)).Append(',')
                  .Append(F(r.SurvivorStartAvgPower > 0f ? (r.AvgPower / r.SurvivorStartAvgPower - 1f) * 100f : 0f, 3)).Append(',')
                  .Append(F(r.P98Power, 3)).Append(',')
                  .Append(r.CapExceed).Append(',')
                  .Append(F(r.MaxCapRatio, 4)).Append(',')
                  .Append(r.UpgradesLoot).Append(',')
                  .Append(r.UpgradesTrade).Append(',')
                  .Append(r.UpgradesDelve).Append(',')
                  .Append(r.SelectRest).Append(',')
                  .Append(r.SelectHunt).Append(',')
                  .Append(r.SelectTrade).Append(',')
                  .Append(r.SelectDelve).Append(',')
                  .Append(F(Pct(r.SelectRest, selections), 2)).Append(',')
                  .Append(F(Pct(r.SelectHunt, selections), 2)).Append(',')
                  .Append(F(Pct(r.SelectTrade, selections), 2)).Append(',')
                  .Append(F(Pct(r.SelectDelve, selections), 2)).Append(',')
                  .Append(r.HuntOutcomes).Append(',')
                  .Append(F(Pct(r.HuntSuccess, r.HuntOutcomes), 2)).Append(',')
                  .Append(r.DelveOutcomes).Append(',')
                  .Append(F(Pct(r.DelveSuccess, r.DelveOutcomes), 2)).Append(',')
                  .Append(r.DelveCompletionRolls).Append(',')
                  .Append(r.DelveDungeonChangedFailures).Append(',')
                  .Append(r.NpcCompletions).Append(',')
                  .Append(r.Wounded).Append(',')
                  .Append(r.SevereWounds).Append(',')
                  .Append(r.Treatments).Append(',')
                  .Append(r.Deaths).Append(',')
                  .Append(F((float)r.ExpectedDeaths, 3)).Append(',')
                  .Append(r.GoldBurned).Append(',')
                  .Append(world).Append(',')
                  .Append(r.VillageGold).Append(',')
                  .Append(r.NpcGold).Append(',')
                  .Append(r.PopulationGold).Append(',')
                  .Append(conservationError).Append(',')
                  .Append(r.FoodInflow).Append(',')
                  .Append(r.FoodYieldRaw).Append(',')
                  .Append(r.TradesExecuted).Append(',')
                  .Append(r.Sold).Append(',')
                  .Append(r.SoldGold).Append(',')
                  .Append(r.SellRejected).Append(',')
                  .Append(r.Bought).Append(',')
                  .Append(r.BoughtGold).Append(',')
                  .Append(r.RumorExchangeAttempts).Append(',')
                  .Append(r.RumorTransfers).Append(',')
                  .Append(r.RumorsHeld).Append(',')
                  .Append(r.RumorHolders).Append(',')
                  .Append(r.PromisingHolders).Append(',')
                  .Append(r.StoredInvalid).Append(',')
                  .Append(r.ConfirmationDelves).Append(',')
                  .Append(F(r.GoldPriceTarget, 4)).Append(',')
                  .Append(F(r.GoldPriceIndex, 4)).Append(',')
                  .Append(r.LivingSpendCount).Append(',')
                  .Append(r.LivingSpendDeposit).Append(',')
                  .Append(r.LivingSpendBurn).Append(',')
                  .Append(r.VaultUpkeepBurn).AppendLine();
            }
            return sb.ToString();
        }

        private static string BuildVillageCsv(SimContext sim)
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("village_id,has_shop,npc_start,npc_end,gold_start,gold_end,population_gold,npc_sold,npc_sold_gold," +
                          "npc_sell_rejected,npc_bought,npc_bought_gold,treatments,food_inflow,dungeon_slots,delves,npc_completions");
            List<int> ids = new List<int>(sim.VillageStats.Keys);
            ids.Sort();
            for (int i = 0; i < ids.Count; i++)
            {
                VillageStats v = sim.VillageStats[ids[i]];
                sb.Append(v.VillageId).Append(',').Append(v.HasShop ? 1 : 0).Append(',')
                  .Append(v.NpcStart).Append(',').Append(v.NpcEnd).Append(',')
                  .Append(v.GoldStart).Append(',').Append(v.GoldEnd).Append(',')
                  .Append(v.PopulationGold).Append(',')
                  .Append(v.Sold).Append(',').Append(v.SoldGold).Append(',')
                  .Append(v.SellRejected).Append(',')
                  .Append(v.Bought).Append(',').Append(v.BoughtGold).Append(',')
                  .Append(v.Treatments).Append(',').Append(v.FoodInflow).Append(',')
                  .Append(v.DungeonSlots).Append(',').Append(v.Delves).Append(',').Append(v.NpcCompletions).AppendLine();
            }
            return sb.ToString();
        }

        private static string BuildSummary(SimContext sim, List<DayRow> rows, List<int> cowardIds, int cowardCourageMax,
            Dictionary<NpcAmbitionKind, int> ambitionCounts, int days, long elapsedMs, string hashBefore,
            string hashAfter, CapturingLogHandler capture)
        {
            DayRow first = rows[0];
            DayRow last = rows[rows.Count - 1];
            int rest = 0, hunt = 0, trade = 0, delve = 0, lootUpgrades = 0, tradeUpgrades = 0, delveUpgrades = 0;
            int maxCapExceed = 0, delveOutcomes = 0, delveSuccess = 0, completionRolls = 0, completions = 0;
            int dungeonChangedFailures = 0, lateCompletions = 0;
            int lateStartDay = Mathf.Max(1, days - 14);
            int huntOutcomes = 0, huntSuccess = 0, treatments = 0, deaths = 0, sold = 0, rejected = 0, bought = 0;
            int woundedTotal = 0, severeTotal = 0;
            double expectedDeaths = 0d;
            float maxCapRatio = 0f;
            for (int i = 1; i < rows.Count; i++)
            {
                DayRow r = rows[i];
                rest += r.SelectRest; hunt += r.SelectHunt; trade += r.SelectTrade; delve += r.SelectDelve;
                lootUpgrades += r.UpgradesLoot; tradeUpgrades += r.UpgradesTrade; delveUpgrades += r.UpgradesDelve;
                delveOutcomes += r.DelveOutcomes; delveSuccess += r.DelveSuccess;
                completionRolls += r.DelveCompletionRolls; completions += r.NpcCompletions;
                dungeonChangedFailures += r.DelveDungeonChangedFailures;
                if (r.Day >= lateStartDay)
                    lateCompletions += r.NpcCompletions;
                huntOutcomes += r.HuntOutcomes; huntSuccess += r.HuntSuccess;
                treatments += r.Treatments; deaths += r.Deaths; expectedDeaths += r.ExpectedDeaths;
                woundedTotal += r.Wounded; severeTotal += r.SevereWounds;
                sold += r.Sold; rejected += r.SellRejected; bought += r.Bought;
                maxCapExceed = Mathf.Max(maxCapExceed, r.CapExceed);
                maxCapRatio = Mathf.Max(maxCapRatio, r.MaxCapRatio);
            }
            int selections = rest + hunt + trade + delve;

            int cowardRest = 0, cowardHunt = 0, cowardTrade = 0, cowardDelve = 0;
            for (int i = 0; i < cowardIds.Count; i++)
            {
                NpcStats stats = sim.NpcStats[cowardIds[i]];
                cowardRest += stats.Rest; cowardHunt += stats.Hunt; cowardTrade += stats.Trade; cowardDelve += stats.Delve;
            }
            int cowardSelections = cowardRest + cowardHunt + cowardTrade + cowardDelve;

            int villagesWithSlots = 0;
            foreach (VillageStats v in sim.VillageStats.Values)
            {
                if (v.DungeonSlots > 0)
                    villagesWithSlots++;
            }

            StringBuilder sb = new StringBuilder();
            sb.AppendLine($"[NpcAmbitionSim] {days}게임일 오프스크린 시뮬레이션 ({elapsedMs / 1000f:F1}s)");
            sb.AppendLine($"라이브 상태 해시 전후 동일: {hashBefore == hashAfter} ({hashBefore.Substring(0, 16)} / {hashAfter.Substring(0, 16)})");
            sb.AppendLine($"시뮬 중 에러 로그: {capture.ErrorCount}");
            for (int i = 0; i < capture.FirstErrors.Count; i++)
                sb.AppendLine($"  ! {capture.FirstErrors[i]}");
            sb.AppendLine($"생존 NPC: {first.Living} → {last.Living} (사망 {deaths}, 인구 보충은 시뮬 안 함)");
            sb.AppendLine($"사망률: 실제 {Pct(deaths, first.Living):F2}% / 기대값 {expectedDeaths:F1}명 " +
                          $"({(first.Living > 0 ? expectedDeaths * 100d / first.Living : 0d):F2}%)");
            sb.AppendLine($"평균 전투력: {first.AvgPower:F3} → {last.AvgPower:F3} " +
                          $"({(first.AvgPower > 0f ? (last.AvgPower / first.AvgPower - 1f) * 100f : 0f):+0.00;-0.00}%) " +
                          $"| 상위 2%: {first.P98Power:F3} → {last.P98Power:F3}");
            sb.AppendLine($"고정 집단 평균 전투력(시작 생존자, 사망자 포함 — 생존 편향 제외): {first.CohortAvgPower:F3} → " +
                          $"{last.CohortAvgPower:F3} " +
                          $"({(first.CohortAvgPower > 0f ? (last.CohortAvgPower / first.CohortAvgPower - 1f) * 100f : 0f):+0.00;-0.00}%)");
            sb.AppendLine($"생존자 평균 상승 분해: 생존 편향(생존자들의 시작 전투력 평균 {last.SurvivorStartAvgPower:F3}) " +
                          $"{(first.AvgPower > 0f ? (last.SurvivorStartAvgPower / first.AvgPower - 1f) * 100f : 0f):+0.00;-0.00}% × " +
                          $"생존자 본인 성장 {(last.SurvivorStartAvgPower > 0f ? (last.AvgPower / last.SurvivorStartAvgPower - 1f) * 100f : 0f):+0.00;-0.00}%");
            int totalUpgrades = lootUpgrades + tradeUpgrades + delveUpgrades;
            sb.AppendLine($"업그레이드 사건: Loot {lootUpgrades} + Trade {tradeUpgrades} + Delve {delveUpgrades} = {totalUpgrades} " +
                          $"(NPC당 {(first.Living > 0 ? totalUpgrades / (float)first.Living : 0f):F4})");
            CountEquippedBasic(sim, out int equippedEnd, out int nonBasicEnd);
            sb.AppendLine($"기본 장비(NPC_SHOP_EQUIPMENT): 기본 아이템 아닌 구매 {sim.NonBasicPurchases}건 | " +
                          $"생존 NPC 장착 장비 중 기본 아이템 아님: 시작 {sim.NonBasicEquippedAtStart}/{sim.EquippedAtStart} → " +
                          $"끝 {nonBasicEnd}/{equippedEnd} (모두 0이어야 함)");
            sb.AppendLine($"개인 상한 초과 NPC(일별 최대): {maxCapExceed} | 최대 전투력/상한 비율: {maxCapRatio:F4}");
            sb.AppendLine($"활동 선택 {selections}회: Rest {Pct(rest, selections):F2}% / Hunt {Pct(hunt, selections):F2}% / " +
                          $"Trade {Pct(trade, selections):F2}% / Delve {Pct(delve, selections):F2}%");
            sb.AppendLine($"겁 많은 NPC(용기 하위 {CowardFraction:P0}, {cowardIds.Count}명, 용기 ≤ {cowardCourageMax}): " +
                          $"Rest {Pct(cowardRest, cowardSelections):F2}% / Hunt {Pct(cowardHunt, cowardSelections):F2}% / " +
                          $"Trade {Pct(cowardTrade, cowardSelections):F2}% / Delve {Pct(cowardDelve, cowardSelections):F2}%");
            sb.AppendLine($"Hunt 판정 {huntOutcomes}건 성공률 {Pct(huntSuccess, huntOutcomes):F2}% | " +
                          $"Delve 판정 {delveOutcomes}건(파티원 개인 포함) 성공률 {Pct(delveSuccess, delveOutcomes):F2}% | 치료 {treatments}건");
            long woundCountEnd = SumWoundCount(sim);
            sb.AppendLine($"부상 등급(D91): 원정 부상 {woundedTotal}건 = 가벼운 {woundedTotal - severeTotal} / 중상 {severeTotal} " +
                          $"(중상 비율 {Pct(severeTotal, woundedTotal):F2}%) | 누적 부상 요약 합계 {sim.WoundCountStart} → {woundCountEnd} " +
                          $"(+{woundCountEnd - sim.WoundCountStart}, 사망자 포함)");
            sb.AppendLine($"NPC 던전 완료: {completions}건 (던전 슬롯 있는 마을 {villagesWithSlots}곳, 마을당 평균 " +
                          $"{(villagesWithSlots > 0 ? completions / (float)villagesWithSlots : 0f):F3}회 / {days}일) | " +
                          $"완료 확률 롤 통과 {completionRolls}건 | 마지막 15일(일 {lateStartDay}~{days}) 완료 {lateCompletions}건");
            sb.AppendLine($"  던전 변경으로 실패 전환(원정 중 목표 던전 세대 전진, 파티원 개인 포함): {dungeonChangedFailures}건");
            sb.Append("  완료 시도 결과:");
            foreach (KeyValuePair<NpcDungeonCompletionResult, int> pair in sim.CompletionResults)
                sb.Append($" {pair.Key}={pair.Value}");
            sb.AppendLine();
            sb.AppendLine($"  플레이어 입장 던전(DungeonRecord {sim.PlayerEnteredDungeonsAtStart}개)의 NPC 완료: {sim.CompletionsOnPlayerEntered}건");
            for (int i = 0; i < sim.CompletionExamples.Count; i++)
                sb.AppendLine($"  - {sim.CompletionExamples[i]}");
            sb.AppendLine($"거래: 판매 {sold}건, 매입 거절 {rejected}건, 구매 {bought}건");
            sb.AppendLine($"세계 Gold(마을+생존 NPC): {first.VillageGold + first.NpcGold} → {last.VillageGold + last.NpcGold} " +
                          $"(마을 {first.VillageGold} → {last.VillageGold}, NPC {first.NpcGold} → {last.NpcGold}, " +
                          $"인구 생산 +{sim.TotalPopulationGold}, 사망 소멸 -{sim.TotalGoldBurned})");

            sb.Append("주 야망 분포:");
            for (int k = (int)NpcAmbitionKind.Explorer; k <= (int)NpcAmbitionKind.Cautious; k++)
            {
                NpcAmbitionKind kind = (NpcAmbitionKind)k;
                ambitionCounts.TryGetValue(kind, out int count);
                sb.Append($" {kind}={count}");
            }
            sb.AppendLine();
            AppendAmbitionShares(sb, sim);

            sb.AppendLine($"판매 → 구매 → 전투력 상승 사례 ({sim.Examples.Count}건 표시):");
            for (int i = 0; i < sim.Examples.Count; i++)
                sb.AppendLine($"  - {sim.Examples[i]}");

            // 닫힌 경제 검산 (일별 CSV의 world_gold_conservation_error 최댓값)
            long baseWorld = first.VillageGold + first.NpcGold;
            long cumulativePopulation = 0, cumulativeBurned = 0, maxConservationError = 0;
            for (int i = 1; i < rows.Count; i++)
            {
                cumulativePopulation += rows[i].PopulationGold;
                cumulativeBurned += rows[i].GoldBurned + rows[i].LivingSpendBurn + rows[i].VaultUpkeepBurn;
                long error = rows[i].VillageGold + rows[i].NpcGold - (baseWorld + cumulativePopulation - cumulativeBurned);
                if (Math.Abs(error) > Math.Abs(maxConservationError))
                    maxConservationError = error;
            }
            sb.AppendLine($"닫힌 경제 검산 오차(일별 최대, 소멸 = 사망 + 생활비 소멸 + 금고 유지비): {maxConservationError}");
            AppendGoldBalanceSummary(sb, sim, rows);
            AppendRumorSummary(sb, sim, rows, days);
            AppendTopicSummary(sb, sim, days);
            AppendPartySummary(sb, sim, days, first.Living);
            AppendRelocationSummary(sb, sim, days);
            AppendTheftPrepSummary(sb, sim, days);
            AppendTheftSummary(sb, sim, days);
            AppendTheftLateSummary(sb, sim, days);

            // ---- P2 완료 기준 체크리스트 (§13, §10.1 기조 — 30일 실행 기준) ----
            TopicAudit ta = sim.Topics;
            long rumorTopics = ta.DungeonTopics + ta.ItemTopics + ta.BetrayalTopics;
            float delvePct = Pct(delve, selections);
            float huntPct = Pct(hunt, selections);
            float completionsPerVillage = villagesWithSlots > 0 ? completions / (float)villagesWithSlots : 0f;
            float avgPowerPct = first.AvgPower > 0f ? (last.AvgPower / first.AvgPower - 1f) * 100f : 0f;
            float cohortPowerPct = first.CohortAvgPower > 0f ? (last.CohortAvgPower / first.CohortAvgPower - 1f) * 100f : 0f;
            float deathPct = Pct(deaths, first.Living);
            int passCount = 0, failCount = 0;
            StringBuilder check = new StringBuilder();
            void Item(bool pass, string label, string value)
            {
                if (pass == true)
                    passCount++;
                else
                    failCount++;
                check.AppendLine($"  [{(pass ? "PASS" : "FAIL")}] {label}: {value}");
            }
            Item(rumorTopics > 0 && ta.TargetFail == 0 && ta.BearingMismatch == 0,
                "대화 소문 토픽의 대상(던전 슬롯·NPC·아이템)이 실제로 존재", $"{ta.TargetOk}/{rumorTopics} ({Pct(ta.TargetOk, rumorTopics):F2}%)");
            Item(ta.CollectMultiplePerType == 0 && ta.CollectDungeonTopicNpcs + ta.CollectItemTopicNpcs > 0,
                "실제 대화 경로(Collect)에 소문 토픽이 나옴", $"던전 {ta.CollectDungeonTopicNpcs}명 / 장비 {ta.CollectItemTopicNpcs}명");
            Item(sim.RumorCounters.ExposedInvalid == 0 && sim.AuditExposedFailures == 0,
                "노출·사용 시점 무효 소문 0 (대화 토픽 포함)", $"{sim.RumorCounters.ExposedInvalid} / 노출 {sim.RumorCounters.Exposed}");
            Item(ta.BoastItemNpcs > 0, "업그레이드 뒤 자랑 토픽(NotableItemAcquired)", $"{ta.BoastItemNpcs}명");
            Item(ta.ClearedDeedNpcs == 0 || ta.BoastClearedNpcs > 0, "던전 완료 뒤 자랑 토픽(DungeonCleared)",
                ta.ClearedDeedNpcs == 0 ? "완료 Deed 없음(해당 없음)" : $"{ta.BoastClearedNpcs}명 (Deed 보유 {ta.ClearedDeedNpcs}명)");
            Item(delvePct >= 4f && delvePct <= 6f, "Delve 선택 비중 약 5% (4~6%)", $"{delvePct:F2}%");
            Item(completionsPerVillage <= 0.5f, "NPC 던전 완료 마을당 0.5회 이하 (D63)", $"{completionsPerVillage:F3}회 / {days}일");
            Item(sim.CompletionsOnPlayerEntered == 0, sim.ProbePlayerEntered == true
                    ? "플레이어 입장 던전의 NPC 완료 0건 (Probe: 모든 마을 슬롯 0에 기록 주입)"
                    : "플레이어 입장 던전의 NPC 완료 0건 (실제 DungeonRecord 기준 — Probe 실행으로 확정)",
                $"{sim.CompletionsOnPlayerEntered}건 (기록 {sim.PlayerEnteredDungeonsAtStart}개)");
            Item(Mathf.Abs(avgPowerPct) <= 10f && Mathf.Abs(cohortPowerPct) <= 10f, "평균 전투력 평탄 (±10% 이내)",
                $"생존 {avgPowerPct:+0.00;-0.00}% / 고정 집단 {cohortPowerPct:+0.00;-0.00}%");
            Item(maxCapExceed == 0, "개인 상한 초과 0", $"{maxCapExceed}");
            // GOLD_BALANCE 본체(2026-10-03): 금고 Gold가 돌며 Trade가 늘어 기준을 43% → 38%로 개정했다.
            Item(huntPct >= 33f && huntPct <= 43f, "Hunt 선택 비중 38% ±5%p", $"{huntPct:F2}%");
            // 사망률은 참고치(§10.1)라 PASS/FAIL에 넣지 않는다 — 실제 사망 수는 표본이 작아(30일 약 10~20명) 흔들리므로 기대값을 같이 본다.
            double expectedDeathPct = first.Living > 0 ? expectedDeaths * 100d / first.Living : 0d;
            string deathNote = $"실제 {deathPct:F2}% / 기대값 {expectedDeathPct:F2}% (30일 0.5~1% 참고, 기간 비례 " +
                               $"{0.5f * days / 30f:F2}~{1f * days / 30f:F2}%)";
            Item(maxConservationError == 0, "닫힌 경제 (골드 생성 없음, 검산 오차 0)", $"{maxConservationError}");
            Item(hashBefore == hashAfter, "라이브 상태 해시 전후 동일", hashBefore == hashAfter ? "동일" : "불일치");
            Item(capture.ErrorCount == 0, "시뮬 중 에러 로그 0", $"{capture.ErrorCount}");
            sb.AppendLine($"==== P2 완료 기준 체크리스트 ({days}일{(days == 30 ? string.Empty : " — 수치 기준은 30일 실행으로 판정")}): " +
                          $"PASS {passCount} / FAIL {failCount} ====");
            sb.Append(check);
            sb.AppendLine($"  [참고] 사망률: {deathNote}");
            return sb.ToString();
        }

        /// <summary>매 시간 생존 NPC의 활동 상태를 센다 — 선택 비중과 별개인 "실제 시간 비중" (파티로 끌려간 원정 시간 포함).</summary>
        private static void SampleActivityStates(SimContext sim)
        {
            for (int i = 0; i < sim.NpcIds.Count; i++)
            {
                NpcSaveData saveData = sim.Npcs[sim.NpcIds[i]];
                if (saveData.Condition == CharacterConditions.Dead)
                    continue;
                NpcActivityState activity = saveData.Activity;
                if (activity.Phase == NpcActivityPhase.Dead)
                    continue;
                bool inParty = activity.PartyId >= 0;
                int slot;
                if (activity.Phase == NpcActivityPhase.Wounded)
                    slot = StateWounded;
                else if (activity.Kind == NpcActivityKind.Rest)
                    slot = StateRest;
                else if (activity.Kind == NpcActivityKind.Trade)
                    slot = StateTrade;
                else if (activity.Kind == NpcActivityKind.Hunt)
                    slot = inParty ? StatePartyHunt : StateSoloHunt;
                else if (activity.Kind == NpcActivityKind.DungeonDelve)
                    slot = inParty ? StatePartyDelve : StateSoloDelve;
                else
                    slot = StateIdle;
                sim.StateHours[slot]++;
            }
        }

        /// <summary>
        /// 원정 파티 통계 (D85). "원정 중 파티 비율"(정본) = 원정 판정(파티원 개인 판정 포함) 중 파티원 판정 비율 —
        /// 원정을 다녀온 NPC-회 중 파티로 다녀온 비율이다. 원정 시작(그룹 단위) 중 파티 원정 비율과 시간 표본 비율도 함께 적는다.
        /// </summary>
        private static void AppendPartySummary(StringBuilder sb, SimContext sim, int days, int livingStart)
        {
            NpcPartyCounters c = sim.PartyCounters;
            long huntJ = sim.SoloHuntJudgments + sim.PartyHuntJudgments;
            long delveJ = sim.SoloDelveJudgments + sim.PartyDelveJudgments;
            long partyJ = sim.PartyHuntJudgments + sim.PartyDelveJudgments;
            sb.AppendLine("==== 원정 파티 (D85) ====");
            sb.AppendLine($"원정 중 파티 비율(원정 판정 중 파티원 판정): Hunt {Pct(sim.PartyHuntJudgments, huntJ):F2}% " +
                          $"({sim.PartyHuntJudgments}/{huntJ}) / Delve {Pct(sim.PartyDelveJudgments, delveJ):F2}% " +
                          $"({sim.PartyDelveJudgments}/{delveJ}) / 합계 {Pct(partyJ, huntJ + delveJ):F2}%");
            long huntStarts = sim.SoloHuntStarts + sim.PartyHuntStarts;
            long delveStarts = sim.SoloDelveStarts + sim.PartyDelveStarts;
            sb.AppendLine($"원정 시작(그룹 단위) 중 파티 원정: Hunt {Pct(sim.PartyHuntStarts, huntStarts):F2}% " +
                          $"({sim.PartyHuntStarts}/{huntStarts}) / Delve {Pct(sim.PartyDelveStarts, delveStarts):F2}% " +
                          $"({sim.PartyDelveStarts}/{delveStarts})");
            for (int k = 0; k < 2; k++)
            {
                long parties = 0;
                for (int size = 2; size <= NpcManager.MaxPartySize; size++)
                    parties += c.PartiesBySize[k, size];
                sb.AppendLine($"{(k == 0 ? "Hunt" : "Delve")} 모집: 시도 {c.Attempts[k]} → 시도 롤 통과 {c.AttemptsPassed[k]} " +
                              $"({Pct(c.AttemptsPassed[k], c.Attempts[k]):F1}%) → 파티 {parties} / 수락자 0 {c.NobodyAccepted[k]} | " +
                              $"크기 1(솔로)={c.Attempts[k] - parties} 2={c.PartiesBySize[k, 2]} 3={c.PartiesBySize[k, 3]} 4={c.PartiesBySize[k, 4]}");
            }
            sb.Append($"후보 수 분포(시도 롤 통과 {c.CandidateSamples}회, 평균 {(c.CandidateSamples > 0 ? c.CandidateTotal / (double)c.CandidateSamples : 0d):F2}명):");
            string[] bucketLabels = { "0", "1", "2", "3", "4-5", "6-9", "10+" };
            for (int i = 0; i < bucketLabels.Length; i++)
                sb.Append($" {bucketLabels[i]}={Pct(c.CandidateBuckets[i], c.CandidateSamples):F1}%");
            sb.AppendLine();
            sb.Append("후보 탈락 사유:");
            for (int r = 1; r < (int)NpcPartyRejectReason.Count; r++)
                sb.Append($" {(NpcPartyRejectReason)r}={c.Rejections[r]}");
            sb.AppendLine();
            sb.Append("수락률(실측 / 평균 확률, 롤 수):");
            string[] acceptLabels = { "관계(친구·형제·배우자)", "호감<0", "호감0~49", "호감50+" };
            for (int b = 0; b < NpcPartyCounters.AcceptBucketCount; b++)
            {
                sb.Append($" {acceptLabels[b]} {Pct(c.Accepted[b], c.AcceptRolls[b]):F1}% / " +
                          $"{(c.AcceptRolls[b] > 0 ? c.AcceptChanceSum[b] * 100d / c.AcceptRolls[b] : 0d):F1}% (n={c.AcceptRolls[b]}) |");
            }
            sb.AppendLine();
            sb.AppendLine($"휴식 끊김 {c.RestInterrupted}회 (건너뛴 소문 교환 = 교환 롤이 통과했을 끊김 {c.RumorExchangesSkipped}회) | " +
                          $"이미 끝난 휴식 정상 종료 후 합류 {c.RestCompletedBeforeJoin}회");
            long total = 0;
            for (int i = 0; i < StateSlotCount; i++)
                total += sim.StateHours[i];
            long[] h = sim.StateHours;
            long expeditionHours = h[StateSoloHunt] + h[StatePartyHunt] + h[StateSoloDelve] + h[StatePartyDelve];
            sb.AppendLine($"실제 시간 비중(매 시간 생존 NPC 표본): Rest {Pct(h[StateRest], total):F2}% / " +
                          $"Hunt {Pct(h[StateSoloHunt] + h[StatePartyHunt], total):F2}% (솔로 {Pct(h[StateSoloHunt], total):F2} + 파티 {Pct(h[StatePartyHunt], total):F2}) / " +
                          $"Trade {Pct(h[StateTrade], total):F2}% / Delve {Pct(h[StateSoloDelve] + h[StatePartyDelve], total):F2}% " +
                          $"(솔로 {Pct(h[StateSoloDelve], total):F2} + 파티 {Pct(h[StatePartyDelve], total):F2}) / " +
                          $"부상 {Pct(h[StateWounded], total):F2}% / 대기 {Pct(h[StateIdle], total):F2}%");
            sb.AppendLine($"원정 시간 중 파티 비율: {Pct(h[StatePartyHunt] + h[StatePartyDelve], expeditionHours):F2}% | " +
                          $"마을 Food 유입 합계 {sim.TotalFoodInflow} (하루 {sim.TotalFoodInflow / (float)Mathf.Max(1, days):F0}, " +
                          $"NPC 1인당 {(livingStart > 0 ? sim.TotalFoodInflow / (float)livingStart : 0f):F2})");
        }

        private static void AppendAmbitionShares(StringBuilder sb, SimContext sim)
        {
            int kinds = (int)NpcAmbitionKind.Cautious + 1;
            int[] restByKind = new int[kinds];
            int[] huntByKind = new int[kinds];
            int[] tradeByKind = new int[kinds];
            int[] delveByKind = new int[kinds];
            for (int i = 0; i < sim.NpcIds.Count; i++)
            {
                int entityId = sim.NpcIds[i];
                int kind = (int)NpcAmbitionEvaluator.Evaluate(sim.Npcs[entityId].NpcStat).Primary;
                NpcStats stats = sim.NpcStats[entityId];
                restByKind[kind] += stats.Rest;
                huntByKind[kind] += stats.Hunt;
                tradeByKind[kind] += stats.Trade;
                delveByKind[kind] += stats.Delve;
            }
            sb.AppendLine("주 야망별 선택 비중 (Rest/Hunt/Trade/Delve %):");
            for (int k = (int)NpcAmbitionKind.Explorer; k < kinds; k++)
            {
                int total = restByKind[k] + huntByKind[k] + tradeByKind[k] + delveByKind[k];
                sb.AppendLine($"  {(NpcAmbitionKind)k}: {Pct(restByKind[k], total):F1} / {Pct(huntByKind[k], total):F1} / " +
                              $"{Pct(tradeByKind[k], total):F1} / {Pct(delveByKind[k], total):F1} (n={total})");
            }
        }

        // ================= P4-0 절도 준비 (D99·D100·D107) =================

        /// <summary>
        /// 절도 조건 깔때기 (P4-0, D99·D100). Rest 정상 종료 직후(ResolveRestEnd 다음, 상태 전이 전) — P4a가 절도를 판정할 자리 —
        /// 에서 판정·실행 없이 조건 충족 수만 센다: 도둑 후보(기회 야망) → 가방 빈칸 → 훔칠 물건이 있는 피해자 후보 ≥ 1.
        /// 피해자·물건·빈칸 판정은 P4a가 쓸 공용 메서드(NpcManager.IsTheftVictimCandidate·HasStealableBagItem·HasFreeBagSlot)를 부른다.
        /// 쿨다운(도둑 168h·같은 마을 72h)은 아직 게임 필드가 없어 여기서 가정해 따로 세고, 기본 확률별 가상 시도도 같이 센다
        /// (확률 = 기본 × 야망 배율, 성향 배율 제외, salt 170 롤 — 상태는 바꾸지 않는다).
        /// </summary>
        private static void MeasureTheftFunnel(SimContext sim, int thiefId, NpcSaveData thief, float restStartGameTime,
            float gameTime)
        {
            TheftFunnel all = sim.TheftAll;
            TheftFunnel? late = sim.CurrentDay >= sim.TheftLateStartDay ? sim.TheftLate : null;
            all.RestEnds++;
            if (late != null)
                late.RestEnds++;

            float multiplier = NpcManager.GetTheftAmbitionMultiplier(thief.NpcStat);
            if (multiplier <= 0f)
                return;
            int k = multiplier >= NpcManager.TheftPrimaryAmbitionMultiplier ? 0 : 1;
            all.Thieves[k]++;
            if (late != null)
                late.Thieves[k]++;

            if (NpcManager.HasFreeBagSlot(thief) == false)
                return;
            all.FreeSlot[k]++;
            if (late != null)
                late.FreeSlot[k]++;

            if (sim.Villages.TryGetValue(thief.VillageId, out VillageData? village) == false)
                return;
            int sameVillageWithItem = 0;
            int victims = 0;
            List<int> roster = village.NpcEntityIds;
            for (int i = 0; i < roster.Count; i++)
            {
                int candidateId = roster[i];
                if (candidateId == thiefId || sim.Npcs.TryGetValue(candidateId, out NpcSaveData? candidate) == false ||
                    candidate.Condition == CharacterConditions.Dead || NpcManager.HasStealableBagItem(candidate) == false)
                    continue;
                sameVillageWithItem++;
                if (AR.s.Npc.IsTheftVictimCandidate(thiefId, thief, candidateId) == true)
                    victims++;
            }
            all.SameVillageItemSum[k] += sameVillageWithItem;
            if (late != null)
                late.SameVillageItemSum[k] += sameVillageWithItem;
            if (victims == 0)
                return;
            all.WithVictim[k]++;
            all.VictimSum[k] += victims;
            if (late != null)
            {
                late.WithVictim[k]++;
                late.VictimSum[k] += victims;
            }

            // 가정 쿨다운: [0] = 통과마다 시도한다고 볼 때
            int villageId = thief.VillageId;
            if (IsTheftCooldownReady(sim, 0, thiefId, villageId, gameTime) == true)
            {
                RecordTheftAttempt(sim, 0, thiefId, villageId, gameTime);
                all.CooldownPass[k]++;
                if (late != null)
                    late.CooldownPass[k]++;
            }

            // 기본 확률별 가상 시도: 같은 롤 값(공통 난수)을 확률 후보마다 비교한다
            if (sim.CreateActivityRng == null)
                return;
            double roll = sim.CreateActivityRng(thiefId, restStartGameTime, sim.WorldSeed, TheftAttemptSalt).NextDouble();
            for (int c = 0; c < TheftBaseChances.Length; c++)
            {
                if (IsTheftCooldownReady(sim, c + 1, thiefId, villageId, gameTime) == false ||
                    roll >= TheftBaseChances[c] * multiplier)
                    continue;
                RecordTheftAttempt(sim, c + 1, thiefId, villageId, gameTime);
                all.VirtualAttempts[c, k]++;
                if (late != null)
                    late.VirtualAttempts[c, k]++;
            }
        }

        private static bool IsTheftCooldownReady(SimContext sim, int table, int thiefId, int villageId, float gameTime)
        {
            if (sim.TheftLastThiefAttempt[table].TryGetValue(thiefId, out float thiefTime) == true &&
                gameTime - thiefTime < TheftThiefCooldownHours)
                return false;
            return sim.TheftLastVillageAttempt[table].TryGetValue(villageId, out float villageTime) == false ||
                   gameTime - villageTime >= TheftVillageCooldownHours;
        }

        private static void RecordTheftAttempt(SimContext sim, int table, int thiefId, int villageId, float gameTime)
        {
            sim.TheftLastThiefAttempt[table][thiefId] = gameTime;
            sim.TheftLastVillageAttempt[table][villageId] = gameTime;
        }

        // ================= P4a 절도 독립 감사 (D99~D102·D106) =================

        /// <summary>
        /// 게임 관찰자(TheftDecidedObserver): 시도가 확정돼 적용하기 직전. 자격·쿨다운을 게임 판정과 다른 경로(복제본 원장 직접 조회,
        /// 진단 자체 쿨다운 표)로 다시 보고, 결과 대조에 쓸 적용 전 상태를 찍어 둔다. 상태는 바꾸지 않는다.
        /// </summary>
        private static void CaptureTheftSnapshot(SimContext sim, NpcTheftDecision decision, float gameTime)
        {
            sim.TheftObserved++;
            if (sim.PendingTheft != null)
                sim.TheftUnaudited++;
            sim.PendingTheft = null;
            if (sim.Npcs.TryGetValue(decision.ThiefId, out NpcSaveData? thief) == false ||
                sim.Npcs.TryGetValue(decision.VictimId, out NpcSaveData? victim) == false)
            {
                sim.TheftEligibilityFails++;
                AddTheftAuditFailure(sim, $"t={gameTime:F2} 도둑 {decision.ThiefId} 피해자 {decision.VictimId}: 복제본에 NPC 없음");
                return;
            }

            ItemData? item = decision.ItemSlot >= 0 && decision.ItemSlot < victim.Inventory.Count
                ? victim.Inventory[decision.ItemSlot]
                : null;
            string? ineligible = CheckTheftEligibility(sim, decision, thief, victim, item);
            if (ineligible != null)
            {
                sim.TheftEligibilityFails++;
                AddTheftAuditFailure(sim, $"t={gameTime:F2} 도둑 {decision.ThiefId} 피해자 {decision.VictimId}: 자격 {ineligible}");
            }

            // 쿨다운: 진단 자체 표 (게임의 LastTheftGameTime·마을 명단과 독립). 순회 순서로 시각이 조금 앞설 수 있어 절댓값으로 본다.
            if (sim.AuditLastThiefAttempt.TryGetValue(decision.ThiefId, out float thiefLast) == true &&
                Mathf.Abs(gameTime - thiefLast) < NpcManager.TheftThiefCooldownHours)
            {
                sim.TheftThiefCooldownViolations++;
                AddTheftAuditFailure(sim, $"t={gameTime:F2} 도둑 {decision.ThiefId}: 직전 시도 {thiefLast:F2}와 168h 미만");
            }
            if (sim.AuditLastVillageAttempt.TryGetValue(thief.VillageId, out float villageLast) == true &&
                Mathf.Abs(gameTime - villageLast) < NpcManager.TheftVillageCooldownHours)
            {
                sim.TheftVillageCooldownViolations++;
                AddTheftAuditFailure(sim, $"t={gameTime:F2} 마을 {thief.VillageId}: 직전 시도 {villageLast:F2}와 72h 미만");
            }
            sim.AuditLastThiefAttempt[decision.ThiefId] = gameTime;
            sim.AuditLastVillageAttempt[thief.VillageId] = gameTime;

            TheftAuditSnapshot snapshot = new TheftAuditSnapshot
            {
                Decision = decision,
                GameTime = gameTime,
                Day = sim.CurrentDay,
                ItemId = item != null ? item.Id : -1,
                ItemValue = item != null ? NpcManager.GetItemValue(item) : 0,
                VictimThiefAffinityBefore = ReadSimAffinity(sim, decision.VictimId, decision.ThiefId),
                GrudgeExistedBefore = CountDirectedRelation(sim, decision.VictimId, decision.VictimId, decision.ThiefId,
                    NpcRelationType.GrudgeAgainst) > 0,
                VictimRelationsBefore = CountSimRelations(sim, decision.VictimId),
                ThiefRelationsBefore = CountSimRelations(sim, decision.ThiefId),
                ThiefDeedsBefore = thief.Deeds.Count,
                ThiefTheftDeedsBefore = CountTheftDeeds(thief),
                ThiefNotableBefore = CountDeeds(thief, NpcDeedKind.NotableItemAcquired),
                VictimDeedsBefore = victim.Deeds.Count,
            };
            CollectSimKin(sim, decision.VictimId, decision.ThiefId, snapshot.KinIds);
            for (int i = 0; i < snapshot.KinIds.Count; i++)
                snapshot.KinAffinityBefore.Add(ReadSimAffinity(sim, snapshot.KinIds[i], decision.ThiefId));
            CollectHeldInstances(thief, snapshot.ThiefItemIdsBefore);
            sim.PendingTheft = snapshot;
        }

        /// <summary>
        /// 자격 독립 검사 (D99·D100): 게임의 IsTheftVictimCandidate·GetTheftThiefBlock을 부르지 않고 복제본 원장을 직접 본다.
        /// 문제가 없으면 null, 있으면 사유. 쿨다운은 CaptureTheftSnapshot이 진단 자체 표로 따로 본다.
        /// </summary>
        private static string? CheckTheftEligibility(SimContext sim, NpcTheftDecision d, NpcSaveData thief, NpcSaveData victim,
            ItemData? item)
        {
            if (IsSimLiving(sim, d.ThiefId) == false)
                return "도둑 사망";
            if (thief.VillageId < 0 || sim.Villages.TryGetValue(thief.VillageId, out VillageData? village) == false)
                return "도둑 소속 마을 없음";
            NpcAmbitionProfile ambition = NpcAmbitionEvaluator.Evaluate(thief.NpcStat);
            float expected = ambition.Primary == NpcAmbitionKind.Opportunist ? 1f
                : ambition.Secondary == NpcAmbitionKind.Opportunist ? 0.5f : 0f;
            if (expected <= 0f || Mathf.Approximately(expected, d.AmbitionMultiplier) == false)
                return $"야망 배율 {d.AmbitionMultiplier:F2} (기대 {expected:F2})";
            int freeSlots = 0;
            for (int i = 0; i < thief.Inventory.Count; i++)
            {
                if (thief.Inventory[i] == null)
                    freeSlots++;
            }
            if (freeSlots == 0)
                return "도둑 가방 빈칸 없음";

            if (IsSimLiving(sim, d.VictimId) == false)
                return "피해자 사망";
            if (victim.VillageId != thief.VillageId || village.NpcEntityIds.Contains(d.VictimId) == false)
                return "다른 마을";
            NpcActivityState activity = victim.Activity;
            if (activity.Phase != NpcActivityPhase.Idle || activity.PartyId >= 0 ||
                (activity.Kind != NpcActivityKind.None && activity.Kind != NpcActivityKind.Rest &&
                 activity.Kind != NpcActivityKind.Trade))
                return $"대기 아님 ({activity.Phase}/{activity.Kind}/파티 {activity.PartyId})";
            if (AR.s.Npc.IsCompanionInDungeon(d.VictimId) == true || AR.s.Npc.IsInPlayerDuel(d.VictimId) == true)
                return "동행·결투 중";
            for (int i = 0; i < TheftExcludedRelationTypes.Length; i++)
            {
                if (HasSimRelationBetween(sim, d.ThiefId, d.VictimId, TheftExcludedRelationTypes[i]) == true)
                    return $"제외 관계 {TheftExcludedRelationTypes[i]}";
            }
            int affinity = ReadSimAffinity(sim, d.ThiefId, d.VictimId);
            if (affinity >= NpcManager.TheftVictimAffinityExclusion)
                return $"호감도 {affinity}";
            if (item == null || item.ItemInstanceId == 0 || item.ItemInstanceId != d.ItemInstanceId)
                return "물건 불일치";
            Tables.ItemTable? table = AR.s.Data.GetItem(item.Id);
            if (table == null || table.Stackable == true)
                return "스택 아이템";
            return null;
        }

        // 피해자 제외 관계 (D99): 원한(방향 무관)·배우자·형제·부모자식(방향 무관)·친구
        private static readonly NpcRelationType[] TheftExcludedRelationTypes =
        {
            NpcRelationType.GrudgeAgainst, NpcRelationType.Spouse, NpcRelationType.Sibling, NpcRelationType.ParentOf,
            NpcRelationType.Friend,
        };

        /// <summary>
        /// Rest 종료 처리 직후: 적용 전 상태(CaptureTheftSnapshot)와 지금 복제본 상태를 대조한다 (현장 적발 / 안 들킴 규칙 100%).
        /// </summary>
        private static void AuditPendingTheft(SimContext sim)
        {
            TheftAuditSnapshot? snapshot = sim.PendingTheft;
            if (snapshot == null)
                return;
            sim.PendingTheft = null;

            NpcTheftDecision d = snapshot.Decision;
            NpcSaveData thief = sim.Npcs[d.ThiefId];
            NpcSaveData victim = sim.Npcs[d.VictimId];
            List<string> problems = new List<string>();
            if (thief.LastTheftGameTime != snapshot.GameTime)
                problems.Add($"도둑 LastTheftGameTime {thief.LastTheftGameTime:F2} (기대 {snapshot.GameTime:F2})");

            bool stayedInSlot = d.ItemSlot >= 0 && d.ItemSlot < victim.Inventory.Count &&
                                victim.Inventory[d.ItemSlot] != null &&
                                victim.Inventory[d.ItemSlot]!.ItemInstanceId == d.ItemInstanceId;
            bool victimHolds = HoldsInstance(victim, d.ItemInstanceId, out _);
            bool thiefHolds = HoldsInstance(thief, d.ItemInstanceId, out bool thiefEquipped);
            int affinityNow = ReadSimAffinity(sim, d.VictimId, d.ThiefId);
            TheftRecord record = new TheftRecord
            {
                Day = snapshot.Day,
                Primary = d.AmbitionMultiplier >= NpcManager.TheftPrimaryAmbitionMultiplier,
                Detected = d.Detected,
                Value = snapshot.ItemValue,
                KinCount = snapshot.KinIds.Count,
                ThiefId = d.ThiefId,
                VictimId = d.VictimId,
                ItemInstanceId = d.ItemInstanceId,
            };

            if (d.Detected == true)
            {
                if (stayedInSlot == false)
                    problems.Add("물건이 피해자 칸에 없음");
                if (thiefHolds == true)
                    problems.Add("도둑이 물건을 가짐");
                int expected = Mathf.Clamp(snapshot.VictimThiefAffinityBefore + NpcManager.TheftVictimAffinityPenalty,
                    NpcManager.AFFINITY_MIN, NpcManager.AFFINITY_MAX);
                if (affinityNow != expected)
                    problems.Add($"피해자↔도둑 호감도 {snapshot.VictimThiefAffinityBefore}→{affinityNow} (기대 {expected})");
                int grudgeAtVictim = CountDirectedRelation(sim, d.VictimId, d.VictimId, d.ThiefId, NpcRelationType.GrudgeAgainst);
                int grudgeAtThief = CountDirectedRelation(sim, d.ThiefId, d.VictimId, d.ThiefId, NpcRelationType.GrudgeAgainst);
                if (grudgeAtVictim != 1 || grudgeAtThief != 1)
                    problems.Add($"원한(피해자→도둑) 인덱스 {grudgeAtVictim}/{grudgeAtThief}건 (1/1이어야 함)");
                for (int i = 0; i < snapshot.KinIds.Count; i++)
                {
                    int kinExpected = Mathf.Clamp(snapshot.KinAffinityBefore[i] + NpcManager.TheftKinAffinityPenalty,
                        NpcManager.AFFINITY_MIN, NpcManager.AFFINITY_MAX);
                    int kinNow = ReadSimAffinity(sim, snapshot.KinIds[i], d.ThiefId);
                    if (kinNow != kinExpected)
                        problems.Add($"가족·친구 {snapshot.KinIds[i]}↔도둑 {snapshot.KinAffinityBefore[i]}→{kinNow} (기대 {kinExpected})");
                }
                CheckTheftDeed(problems, "피해자", victim, snapshot.VictimDeedsBefore, NpcDeedKind.CaughtThiefInAct, d.ThiefId,
                    snapshot.ItemId);
                CheckTheftDeed(problems, "도둑", thief, snapshot.ThiefDeedsBefore, NpcDeedKind.CaughtStealing, d.VictimId,
                    snapshot.ItemId);
                // P4b (D105): 피해자 직접 Betrayal 소문(현장 적발, 사건 시각 = 지금)
                if (HasBetrayalRumorOrCapDrop(victim, d.ThiefId, d.VictimId, NpcBetrayalKind.TheftCaughtInAct, snapshot.GameTime) == false)
                    problems.Add("피해자 Betrayal(현장 적발) 소문 없음");
                record.NewGrudge = snapshot.GrudgeExistedBefore == false;
                if (problems.Count == 0)
                    sim.TheftCaughtOk++;
            }
            else
            {
                if (victimHolds == true)
                    problems.Add("물건이 피해자에게 남음");
                if (thiefHolds == false)
                    problems.Add("도둑에게 물건 없음");
                record.Equipped = thiefEquipped;
                if (affinityNow != snapshot.VictimThiefAffinityBefore)
                    problems.Add($"피해자↔도둑 호감도 변화 {snapshot.VictimThiefAffinityBefore}→{affinityNow}");
                if (CountSimRelations(sim, d.VictimId) != snapshot.VictimRelationsBefore ||
                    CountSimRelations(sim, d.ThiefId) != snapshot.ThiefRelationsBefore)
                    problems.Add("유형 관계 수 변화");
                for (int i = 0; i < snapshot.KinIds.Count; i++)
                {
                    if (ReadSimAffinity(sim, snapshot.KinIds[i], d.ThiefId) != snapshot.KinAffinityBefore[i])
                        problems.Add($"가족·친구 {snapshot.KinIds[i]}↔도둑 호감도 변화");
                }
                // 도둑 쪽: 절도 Deed 없음. 새 Deed는 훔친 장비를 장착했을 때의 NotableItemAcquired(D83 기존 경로) 1건뿐이다.
                int notableAdded = CountDeeds(thief, NpcDeedKind.NotableItemAcquired) - snapshot.ThiefNotableBefore;
                if (CountTheftDeeds(thief) != snapshot.ThiefTheftDeedsBefore)
                    problems.Add("도둑 쪽 절도 Deed 생김");
                if (thief.Deeds.Count - snapshot.ThiefDeedsBefore != notableAdded || notableAdded != (thiefEquipped ? 1 : 0))
                    problems.Add($"도둑 Deed +{thief.Deeds.Count - snapshot.ThiefDeedsBefore} (NotableItemAcquired +{notableAdded}, 장착 {thiefEquipped})");
                if (thiefEquipped == true && notableAdded == 1)
                    sim.TheftThiefNotable++;
                CheckTheftDeed(problems, "피해자", victim, snapshot.VictimDeedsBefore, NpcDeedKind.ItemStolen, -1, snapshot.ItemId);
                // P4b (D103): 피해자 단서 {인스턴스, 실제 범인, 시각}
                bool clueFound = false;
                for (int i = 0; i < victim.TheftClues.Count; i++)
                {
                    NpcTheftClue clue = victim.TheftClues[i];
                    clueFound |= clue.ItemInstanceId == d.ItemInstanceId && clue.ThiefId == d.ThiefId &&
                                 clue.When == snapshot.GameTime;
                }
                if (clueFound == false || victim.TheftClues.Count > NpcSaveData.MaxTheftClues)
                    problems.Add($"피해자 단서 없음/상한 초과 ({victim.TheftClues.Count}건)");
                for (int i = 0; i < snapshot.ThiefItemIdsBefore.Count; i++)
                {
                    if (HoldsInstance(thief, snapshot.ThiefItemIdsBefore[i], out _) == false)
                        problems.Add($"도둑 기존 아이템 #{snapshot.ThiefItemIdsBefore[i]} 사라짐(버림)");
                }
                if (thiefEquipped == true)
                {
                    float power = NpcPowerEvaluator.Evaluate(thief);
                    float cap = NpcManager.GetNpcPowerCap(thief);
                    if (power > cap + PowerEpsilon)
                    {
                        sim.TheftCapExceed++;
                        problems.Add($"개인 상한 초과 {power:F3} > {cap:F3}");
                    }
                }
                if (problems.Count == 0)
                    sim.TheftStolenOk++;
            }

            if (problems.Count > 0)
            {
                sim.TheftAuditFailCount++;
                AddTheftAuditFailure(sim, $"t={snapshot.GameTime:F2} 도둑 {d.ThiefId} 피해자 {d.VictimId} " +
                                          $"{(d.Detected ? "현장 적발" : "안 들킴")}: {string.Join(", ", problems)}");
            }
            sim.TheftRecords.Add(record);
        }

        /// <summary>피해자는 최신 사연 교체, 도둑은 주요 이력 추가. 종류·아이템·참가자를 검증한다.</summary>
        private static void CheckTheftDeed(List<string> problems, string label, NpcSaveData owner, int deedsBefore,
            NpcDeedKind kind, int otherId, int itemId)
        {
            bool victim = NpcSaveData.IsTheftVictimStory(kind);
            int expectedCount = deedsBefore + (victim ? 0 : 1);
            if (owner.Deeds.Count != expectedCount)
            {
                problems.Add($"{label} 주요 이력 {deedsBefore}→{owner.Deeds.Count}건 (기대 {expectedCount})");
                return;
            }
            NpcDeedEntry? deed = victim ? owner.PersonalStories?.TheftVictim : owner.Deeds[owner.Deeds.Count - 1];
            if (deed == null)
            {
                problems.Add($"{label} 최신 절도 피해 사연 없음");
                return;
            }
            bool participantsOk = otherId < 0
                ? deed.Participants.Count == 0 && deed.ParticipantSnapshots.Count == 0
                : deed.Participants.Count == 1 && deed.Participants[0] == otherId && deed.ParticipantSnapshots.Count == 1;
            if (deed.Kind != kind || deed.ItemId != itemId || participantsOk == false)
                problems.Add($"{label} Deed {deed.Kind}/아이템 {deed.ItemId}/참가자 {deed.Participants.Count} (기대 {kind}/{itemId}/{(otherId < 0 ? 0 : 1)})");
        }

        /// <summary>
        /// 피해자가 그 사건의 Betrayal 소문(가해자·피해자·세부 종류·사건 시각)을 가졌는가. 상한 8이 모두 유망 소문이라
        /// 새 소문이 버려진 경우(DroppedByCap)는 규칙대로이므로 통과로 본다.
        /// </summary>
        private static bool HasBetrayalRumorOrCapDrop(NpcSaveData victim, int thiefId, int victimId, NpcBetrayalKind kind,
            float eventGameTime)
        {
            for (int i = 0; i < victim.Rumors.Count; i++)
            {
                NpcRumor rumor = victim.Rumors[i];
                if (rumor.Kind == NpcRumorKind.Betrayal && rumor.SubjectNpcId == thiefId && rumor.VictimNpcId == victimId &&
                    rumor.BetrayalKind == kind && rumor.EventGameTime == eventGameTime && rumor.SourceNpcId < 0)
                    return true;
            }
            if (victim.Rumors.Count < NpcManager.MaxRumorsPerNpc)
                return false;
            float now = AR.s.Time.CurrentGameTime;
            for (int i = 0; i < victim.Rumors.Count; i++)
            {
                if (AR.s.Npc.IsPromisingRumor(victim, victim.Rumors[i], now) == false)
                    return false;
            }
            return true;
        }

        /// <summary>
        /// 늦은 발각 직전 (관찰자 applied == false, D103). 발각 조건을 게임 판정과 다른 경로로 다시 본다: 피해자에게 그 인스턴스·범인의
        /// 단서가 있고(범인 생존, 14일 이내), 방금 받아들인 그 인스턴스의 ItemOwner/ItemSold 소문(주체 = 범인)을 갖고 있다. 대조용 상태를 찍는다.
        /// </summary>
        private static void CaptureDiscoverySnapshot(SimContext sim, NpcTheftDiscovery discovery)
        {
            if (sim.PendingDiscovery != null)
                sim.DiscoveryUnaudited++;
            sim.PendingDiscovery = null;
            if (sim.Npcs.TryGetValue(discovery.VictimId, out NpcSaveData? victim) == false ||
                sim.Npcs.TryGetValue(discovery.ThiefId, out NpcSaveData? thief) == false)
            {
                sim.DiscoveryTriggerFails++;
                AddDiscoveryAuditFailure(sim, $"t={discovery.GameTime:F2} 피해자 {discovery.VictimId} 도둑 {discovery.ThiefId}: 복제본에 NPC 없음");
                return;
            }

            bool clueOk = false;
            for (int i = 0; i < victim.TheftClues.Count; i++)
            {
                NpcTheftClue clue = victim.TheftClues[i];
                clueOk |= clue.ItemInstanceId == discovery.ItemInstanceId && clue.ThiefId == discovery.ThiefId &&
                          clue.When == discovery.TheftGameTime && IsSimLiving(sim, clue.ThiefId) == true &&
                          discovery.GameTime - clue.When < NpcManager.TheftClueLifetimeHours;
            }
            NpcRumorKind expectedKind = discovery.Path == NpcManager.TheftDiscoveryPathItemSold
                ? NpcRumorKind.ItemSold
                : NpcRumorKind.ItemOwner;
            bool rumorOk = false;
            for (int i = 0; i < victim.Rumors.Count; i++)
            {
                NpcRumor rumor = victim.Rumors[i];
                rumorOk |= rumor.Kind == expectedKind && rumor.SubjectNpcId == discovery.ThiefId &&
                           rumor.ItemInstanceId == discovery.ItemInstanceId && rumor.SourceNpcId >= 0;
            }
            if (clueOk == false || rumorOk == false)
            {
                sim.DiscoveryTriggerFails++;
                AddDiscoveryAuditFailure(sim, $"t={discovery.GameTime:F2} 피해자 {discovery.VictimId} 도둑 {discovery.ThiefId}: " +
                                              $"발각 조건 (단서 {clueOk}, 받은 {expectedKind} 소문 {rumorOk})");
            }

            TheftDiscoverySnapshot snapshot = new TheftDiscoverySnapshot
            {
                Discovery = discovery,
                AffinityBefore = ReadSimAffinity(sim, discovery.VictimId, discovery.ThiefId),
                GrudgeExistedBefore = CountDirectedRelation(sim, discovery.VictimId, discovery.VictimId, discovery.ThiefId,
                    NpcRelationType.GrudgeAgainst) > 0,
                VictimDeedsBefore = victim.Deeds.Count,
                ThiefDeedsBefore = thief.Deeds.Count,
            };
            CollectSimKin(sim, discovery.VictimId, discovery.ThiefId, snapshot.KinIds);
            for (int i = 0; i < snapshot.KinIds.Count; i++)
                snapshot.KinAffinityBefore.Add(ReadSimAffinity(sim, snapshot.KinIds[i], discovery.ThiefId));
            sim.PendingDiscovery = snapshot;
        }

        /// <summary>
        /// 늦은 발각 직후 (관찰자 applied == true): D102 관계(−40·원한 1건·가족친구 −10) + 단서 제거 + Deed 2종 + Betrayal(늦은 발각) 100% 대조.
        /// </summary>
        private static void AuditDiscovery(SimContext sim, NpcTheftDiscovery discovery)
        {
            TheftDiscoverySnapshot? snapshot = sim.PendingDiscovery;
            sim.PendingDiscovery = null;
            int path = Mathf.Clamp(discovery.Path, 0, 1);
            sim.DiscoveryCount[path]++;
            sim.DiscoveredInstances.Add(discovery.ItemInstanceId);
            if (snapshot == null || snapshot.Discovery.VictimId != discovery.VictimId ||
                snapshot.Discovery.ItemInstanceId != discovery.ItemInstanceId)
            {
                AddDiscoveryAuditFailure(sim, $"t={discovery.GameTime:F2} 피해자 {discovery.VictimId}: 직전 상태 없음");
                return;
            }

            NpcSaveData victim = sim.Npcs[discovery.VictimId];
            NpcSaveData thief = sim.Npcs[discovery.ThiefId];
            List<string> problems = new List<string>();
            int expected = Mathf.Clamp(snapshot.AffinityBefore + NpcManager.TheftVictimAffinityPenalty, NpcManager.AFFINITY_MIN,
                NpcManager.AFFINITY_MAX);
            int affinityNow = ReadSimAffinity(sim, discovery.VictimId, discovery.ThiefId);
            if (affinityNow != expected)
                problems.Add($"피해자↔도둑 호감도 {snapshot.AffinityBefore}→{affinityNow} (기대 {expected})");
            if (CountDirectedRelation(sim, discovery.VictimId, discovery.VictimId, discovery.ThiefId, NpcRelationType.GrudgeAgainst) != 1 ||
                CountDirectedRelation(sim, discovery.ThiefId, discovery.VictimId, discovery.ThiefId, NpcRelationType.GrudgeAgainst) != 1)
                problems.Add("원한(피해자→도둑) 1/1건 아님");
            for (int i = 0; i < snapshot.KinIds.Count; i++)
            {
                int kinExpected = Mathf.Clamp(snapshot.KinAffinityBefore[i] + NpcManager.TheftKinAffinityPenalty,
                    NpcManager.AFFINITY_MIN, NpcManager.AFFINITY_MAX);
                if (ReadSimAffinity(sim, snapshot.KinIds[i], discovery.ThiefId) != kinExpected)
                    problems.Add($"가족·친구 {snapshot.KinIds[i]} −10 아님");
            }
            for (int i = 0; i < victim.TheftClues.Count; i++)
            {
                if (victim.TheftClues[i].ItemInstanceId == discovery.ItemInstanceId &&
                    victim.TheftClues[i].ThiefId == discovery.ThiefId)
                    problems.Add("단서가 남음");
            }
            CheckTheftDeed(problems, "피해자", victim, snapshot.VictimDeedsBefore, NpcDeedKind.TheftDiscovered, discovery.ThiefId,
                discovery.ItemId);
            CheckTheftDeed(problems, "도둑", thief, snapshot.ThiefDeedsBefore, NpcDeedKind.TheftExposed, discovery.VictimId,
                discovery.ItemId);
            if (HasBetrayalRumorOrCapDrop(victim, discovery.ThiefId, discovery.VictimId, NpcBetrayalKind.TheftDiscovered,
                    discovery.TheftGameTime) == false)
                problems.Add("피해자 Betrayal(늦은 발각) 소문 없음");

            if (problems.Count == 0)
                sim.DiscoveryOk[path]++;
            else
                AddDiscoveryAuditFailure(sim, $"t={discovery.GameTime:F2} 피해자 {discovery.VictimId} 도둑 {discovery.ThiefId} " +
                                              $"경로 {path}: {string.Join(", ", problems)}");
            if (snapshot.GrudgeExistedBefore == false)
                sim.DiscoveryNewGrudges++;
            sim.DiscoveryKinTotal += snapshot.KinIds.Count;
        }

        private static void AddDiscoveryAuditFailure(SimContext sim, string message)
        {
            if (sim.DiscoveryAuditFailures.Count < MaxTheftAuditFailures)
                sim.DiscoveryAuditFailures.Add(message);
        }

        private static void AddTheftAuditFailure(SimContext sim, string message)
        {
            if (sim.TheftAuditFailures.Count < MaxTheftAuditFailures)
                sim.TheftAuditFailures.Add(message);
        }

        private static int ReadSimAffinity(SimContext sim, int a, int b)
        {
            if (a == b)
                return 0;
            return sim.Affinity.TryGetValue(new NpcPairKey(a, b), out NpcAffinityEntry value) ? value.Affinity : 0;
        }

        private static int CountSimRelations(SimContext sim, int entityId)
        {
            return sim.Relations.TryGetValue(entityId, out List<NpcRelationEntry>? list) ? list.Count : 0;
        }

        /// <summary>indexKey의 관계 인덱스에서 정확히 from → to 방향의 type 엔트리 수.</summary>
        private static int CountDirectedRelation(SimContext sim, int indexKey, int from, int to, NpcRelationType type)
        {
            if (sim.Relations.TryGetValue(indexKey, out List<NpcRelationEntry>? list) == false)
                return 0;
            int count = 0;
            for (int i = 0; i < list.Count; i++)
            {
                NpcRelationEntry entry = list[i];
                if (entry.Type == type && entry.FromEntityId == from && entry.ToEntityId == to)
                    count++;
            }
            return count;
        }

        private static bool HasSimRelationBetween(SimContext sim, int a, int b, NpcRelationType type)
        {
            return CountDirectedRelation(sim, a, a, b, type) > 0 || CountDirectedRelation(sim, a, b, a, type) > 0;
        }

        /// <summary>피해자의 가족(배우자·형제·부모자식 양방향)·친구 중 생존·도둑 아닌 NPC (중복 없음, D102) — 복제본 원장 직접 조회.</summary>
        private static void CollectSimKin(SimContext sim, int victimId, int thiefId, List<int> result)
        {
            result.Clear();
            if (sim.Relations.TryGetValue(victimId, out List<NpcRelationEntry>? list) == false)
                return;
            for (int i = 0; i < list.Count; i++)
            {
                NpcRelationEntry entry = list[i];
                if (entry.Type != NpcRelationType.Spouse && entry.Type != NpcRelationType.Sibling &&
                    entry.Type != NpcRelationType.ParentOf && entry.Type != NpcRelationType.Friend)
                    continue;
                int otherId = entry.FromEntityId == victimId ? entry.ToEntityId : entry.FromEntityId;
                if (otherId == thiefId || otherId == victimId || result.Contains(otherId) == true ||
                    IsSimLiving(sim, otherId) == false)
                    continue;
                result.Add(otherId);
            }
        }

        private static void CollectHeldInstances(NpcSaveData saveData, List<int> result)
        {
            result.Clear();
            for (int i = 0; i < saveData.Inventory.Count; i++)
            {
                ItemData? item = saveData.Inventory[i];
                if (item != null && item.ItemInstanceId != 0)
                    result.Add(item.ItemInstanceId);
            }
            for (int i = 0; i < saveData.InventoryEquip.Length; i++)
            {
                ItemData? item = saveData.InventoryEquip[i];
                if (item != null && item.ItemInstanceId != 0)
                    result.Add(item.ItemInstanceId);
            }
        }

        private static bool HoldsInstance(NpcSaveData saveData, int itemInstanceId, out bool equipped)
        {
            equipped = false;
            for (int i = 0; i < saveData.InventoryEquip.Length; i++)
            {
                if (saveData.InventoryEquip[i] != null && saveData.InventoryEquip[i]!.ItemInstanceId == itemInstanceId)
                {
                    equipped = true;
                    return true;
                }
            }
            for (int i = 0; i < saveData.Inventory.Count; i++)
            {
                if (saveData.Inventory[i] != null && saveData.Inventory[i]!.ItemInstanceId == itemInstanceId)
                    return true;
            }
            return false;
        }

        private static bool IsTheftDeedKind(NpcDeedKind kind)
        {
            return kind == NpcDeedKind.ItemStolen || kind == NpcDeedKind.CaughtThiefInAct || kind == NpcDeedKind.CaughtStealing ||
                   kind == NpcDeedKind.TheftDiscovered || kind == NpcDeedKind.TheftExposed;
        }

        private static int CountTheftDeeds(NpcSaveData saveData)
        {
            int count = saveData.PersonalStories?.TheftVictim != null ? 1 : 0;
            for (int i = 0; i < saveData.Deeds.Count; i++)
            {
                if (saveData.Deeds[i] != null && IsTheftDeedKind(saveData.Deeds[i].Kind) == true)
                    count++;
            }
            return count;
        }

        private static int CountDeeds(NpcSaveData saveData, NpcDeedKind kind)
        {
            int count = saveData.PersonalStories?.TheftVictim?.Kind == kind ? 1 : 0;
            for (int i = 0; i < saveData.Deeds.Count; i++)
            {
                if (saveData.Deeds[i] != null && saveData.Deeds[i].Kind == kind)
                    count++;
            }
            return count;
        }

        /// <summary>P4b 요약 (D103~D106): 단서·늦은 발각 경로별·깔때기·판매/배신 소문 + 독립 감사.</summary>
        private static void AppendTheftLateSummary(StringBuilder sb, SimContext sim, int days)
        {
            NpcTheftCounters c = sim.TheftCounters;
            NpcRumorCounters r = sim.RumorCounters;
            sb.AppendLine("==== P4b 늦은 발각 · 판매 소문 · 배신 소문 (D103~D106) ====");
            int cluesHeld = 0, clueHolders = 0, clueCapViolations = 0, soldHeld = 0, betrayalHeld = 0;
            for (int i = 0; i < sim.NpcIds.Count; i++)
            {
                if (IsSimLiving(sim, sim.NpcIds[i]) == false)
                    continue;
                NpcSaveData npc = sim.Npcs[sim.NpcIds[i]];
                cluesHeld += npc.TheftClues.Count;
                if (npc.TheftClues.Count > 0)
                    clueHolders++;
                if (npc.TheftClues.Count > NpcSaveData.MaxTheftClues)
                    clueCapViolations++;
                for (int k = 0; k < npc.Rumors.Count; k++)
                {
                    if (npc.Rumors[k].Kind == NpcRumorKind.ItemSold)
                        soldHeld++;
                    else if (npc.Rumors[k].Kind == NpcRumorKind.Betrayal)
                        betrayalHeld++;
                }
            }
            sb.AppendLine($"단서: 생성 {c.CluesCreated} / 상한(3) 교체 {c.CluesEvicted} / 14일 만료 정리 {c.CluesExpired} / 범인 사망 정리 {c.CluesThiefDead} | " +
                          $"끝 보유 {cluesHeld}건(NPC {clueHolders}명, 상한 초과 {clueCapViolations})");
            long late = c.LateDiscoveries[0] + c.LateDiscoveries[1];
            sb.AppendLine($"늦은 발각 {late}건 (30일 환산 {late * 30f / Mathf.Max(1, days):F0}) = 장착 소문 {c.LateDiscoveries[0]} / 판매 소문 {c.LateDiscoveries[1]} | " +
                          $"맞는 소문이 왔지만 단서 만료·범인 사망이라 발각 없음 {c.LateMatchInvalidClue}건");

            // 깔때기: 안 들킨 절도 → (도둑 장착 | 판매 소문 생성 | 그 밖: 도둑 가방에 보유·매입 거절) → 늦은 발각
            int stolen = 0, equipped = 0, equippedFound = 0, sold = 0, soldFound = 0, other = 0, otherFound = 0;
            for (int i = 0; i < sim.TheftRecords.Count; i++)
            {
                TheftRecord record = sim.TheftRecords[i];
                if (record.Detected == true)
                    continue;
                stolen++;
                bool found = sim.DiscoveredInstances.Contains(record.ItemInstanceId);
                if (record.Equipped == true)
                {
                    equipped++;
                    if (found == true)
                        equippedFound++;
                }
                else if (sim.SoldStolenInstances.Contains(record.ItemInstanceId) == true)
                {
                    sold++;
                    if (found == true)
                        soldFound++;
                }
                else
                {
                    other++;
                    if (found == true)
                        otherFound++;
                }
            }
            sb.AppendLine($"깔때기(이번 실행의 안 들킨 절도 {stolen}건): 도둑 장착 {equipped}(→ 발각 {equippedFound}) / 훔친 물건 판매 소문 {sold}(→ 발각 {soldFound}) / " +
                          $"그 밖(가방 보유·매입 거절·되도둑맞음) {other}(→ 발각 {otherFound})");
            sb.AppendLine($"소문: 판매(훔친 물건) 생성 {r.CreatedItemSold} · 교환 수용 {r.AcceptedItemSold} · 끝 보유 {soldHeld} | " +
                          $"배신 생성 현장 적발 {r.CreatedBetrayal[(int)NpcBetrayalKind.TheftCaughtInAct]} / 늦은 발각 " +
                          $"{r.CreatedBetrayal[(int)NpcBetrayalKind.TheftDiscovered]} · 교환 수용 {r.AcceptedBetrayal} · 끝 보유 {betrayalHeld}");
            sb.AppendLine($"늦은 발각의 관계 파장: 새 원한 쌍 {sim.DiscoveryNewGrudges} / 가족·친구 −10 {sim.DiscoveryKinTotal}명");

            TopicAudit ta = sim.Topics;
            int passCount = 0, failCount = 0;
            StringBuilder check = new StringBuilder();
            void Item(bool pass, string label, string value)
            {
                if (pass == true)
                    passCount++;
                else
                    failCount++;
                check.AppendLine($"  [{(pass ? "PASS" : "FAIL")}] {label}: {value}");
            }
            Item(sim.DiscoveryTriggerFails == 0, "늦은 발각 조건 독립 검사 실패 0 (유효 단서 + 받은 소문 주체 = 실제 범인)",
                $"{sim.DiscoveryTriggerFails}");
            Item(sim.DiscoveryOk[0] == sim.DiscoveryCount[0] && sim.DiscoveryOk[1] == sim.DiscoveryCount[1],
                "늦은 발각 결과: 호감도 −40·원한 1건·가족친구 −10·단서 제거·Deed 2종·Betrayal(늦은 발각)",
                $"장착 {sim.DiscoveryOk[0]}/{sim.DiscoveryCount[0]} · 판매 {sim.DiscoveryOk[1]}/{sim.DiscoveryCount[1]}");
            Item(c.LateDiscoveries[0] == sim.DiscoveryCount[0] && c.LateDiscoveries[1] == sim.DiscoveryCount[1] &&
                 sim.DiscoveryUnaudited == 0, "늦은 발각 수 일치 (게임 카운터 = 감사, 미감사 0)",
                $"{c.LateDiscoveries[0]}/{c.LateDiscoveries[1]} = {sim.DiscoveryCount[0]}/{sim.DiscoveryCount[1]}");
            Item(clueCapViolations == 0, "단서 상한 3 초과 0", $"{clueCapViolations}");
            Item(ta.ItemSoldTopics == 0 && ta.ThiefSilenceViolations == 0 && ta.TheftTopicLineFail == 0,
                "대화: 판매 소문 토픽 0 · 도둑 침묵 · 도난 토픽 문구(범인 이름/\"누가\")",
                $"판매 토픽 {ta.ItemSoldTopics} / 도둑 위반 {ta.ThiefSilenceViolations} / 문구 불일치 {ta.TheftTopicLineFail}");
            sb.AppendLine($"==== P4b 독립 감사: PASS {passCount} / FAIL {failCount} ====");
            sb.Append(check);
            for (int i = 0; i < sim.DiscoveryAuditFailures.Count; i++)
                sb.AppendLine($"    ! {sim.DiscoveryAuditFailures[i]}");
        }

        private static void AppendTheftSummary(StringBuilder sb, SimContext sim, int days)
        {
            NpcTheftCounters c = sim.TheftCounters;
            sb.AppendLine("==== P4a 절도 (D99~D102·D106) ====");
            sb.AppendLine($"판정한 Rest 종료 {c.RestEnds}회 (파티 합류 경로 제외) | 자격 없음(사망·마을 없음) " +
                          $"{c.Unqualified[(int)NpcTheftCheck.NotEligible]} / 기회 야망 아님 {c.Unqualified[(int)NpcTheftCheck.NoAmbition]}");
            string[] names = { "기회 주 야망", "기회 보조 야망" };
            long attempts = 0, caught = 0, equipped = 0, toBag = 0, invalid = 0;
            for (int k = 0; k < 2; k++)
            {
                long kAttempts = c.Checks[k, (int)NpcTheftCheck.Attempt];
                attempts += kAttempts;
                caught += c.Outcomes[k, (int)NpcTheftOutcome.CaughtInAct];
                equipped += c.Outcomes[k, (int)NpcTheftOutcome.StolenEquipped];
                toBag += c.Outcomes[k, (int)NpcTheftOutcome.StolenToBag];
                invalid += c.Outcomes[k, (int)NpcTheftOutcome.Invalid];
                sb.AppendLine($"  {names[k]}: 도둑 쿨다운 {c.Checks[k, (int)NpcTheftCheck.ThiefCooldown]} / 마을 쿨다운 " +
                              $"{c.Checks[k, (int)NpcTheftCheck.VillageCooldown]} / 빈칸 없음 {c.Checks[k, (int)NpcTheftCheck.NoFreeSlot]} / " +
                              $"시도 롤 실패 {c.Checks[k, (int)NpcTheftCheck.RollFailed]} / 후보 없음 {c.Checks[k, (int)NpcTheftCheck.NoVictim]} → " +
                              $"시도 {kAttempts} (현장 적발 {c.Outcomes[k, (int)NpcTheftOutcome.CaughtInAct]} / 안 들킴 장착 " +
                              $"{c.Outcomes[k, (int)NpcTheftOutcome.StolenEquipped]} · 가방 {c.Outcomes[k, (int)NpcTheftOutcome.StolenToBag]})");
            }

            int villagesWithNpcs = 0;
            foreach (VillageStats v in sim.VillageStats.Values)
            {
                if (v.NpcStart > 0)
                    villagesWithNpcs++;
            }
            int lateDays = days - sim.TheftLateStartDay + 1;
            int lateAttempts = 0;
            for (int i = 0; i < sim.TheftRecords.Count; i++)
            {
                if (sim.TheftRecords[i].Day >= sim.TheftLateStartDay)
                    lateAttempts++;
            }
            float attempts30 = attempts * 30f / Mathf.Max(1, days);
            float late30 = lateAttempts * 30f / Mathf.Max(1, lateDays);
            sb.AppendLine($"시도 합계 {attempts}건 → 30일 환산 {attempts30:F0}건 | 마지막 {lateDays}일(일 {sim.TheftLateStartDay}~{days}, " +
                          $"가방이 찬 정상 상태) {lateAttempts}건 → 30일 환산 {late30:F0}건 (목표 30일 60~120건, D100 · 초반 과다는 D109 ②)");
            sb.AppendLine($"  마을당 30일 환산(NPC 있는 마을 {villagesWithNpcs}곳): 전체 {(villagesWithNpcs > 0 ? attempts30 / villagesWithNpcs : 0f):F2}회 / " +
                          $"정상 상태 {(villagesWithNpcs > 0 ? late30 / villagesWithNpcs : 0f):F2}회 (목표 약 0.2~0.4회)");
            sb.AppendLine($"현장 적발 {caught}건 ({Pct(caught, attempts):F1}%) / 안 들킴 {equipped + toBag}건 (도둑 장착 {equipped} / 가방 {toBag}) | " +
                          $"시도 때 평균 피해자 후보 {(attempts > 0 ? c.VictimCandidateSum / (double)attempts : 0d):F2}명");

            List<int> targetValues = new List<int>(sim.TheftRecords.Count);
            for (int i = 0; i < sim.TheftRecords.Count; i++)
                targetValues.Add(sim.TheftRecords[i].Value);
            targetValues.Sort();
            long targetSum = 0;
            for (int i = 0; i < targetValues.Count; i++)
                targetSum += targetValues[i];
            sb.Append($"노린 물건 가치: 평균 {(targetValues.Count > 0 ? targetSum / (double)targetValues.Count : 0d):F1} / 중앙 " +
                      $"{(targetValues.Count > 0 ? targetValues[targetValues.Count / 2] : 0)} / 최대 {(targetValues.Count > 0 ? targetValues[targetValues.Count - 1] : 0)} | " +
                      $"훔친 물건(안 들킴) {c.StolenCount}개 평균 {(c.StolenCount > 0 ? c.StolenValueSum / (double)c.StolenCount : 0d):F1} / 최대 {c.StolenValueMax} | 분포");
            for (int b = 0; b < NpcTheftCounters.ValueBucketUpper.Length && b < BagValueBucketLabels.Length; b++)
                sb.Append($" {BagValueBucketLabels[b]}={c.StolenValueBuckets[b]}");
            sb.AppendLine();

            int newGrudges = 0, kinTotal = 0;
            for (int i = 0; i < sim.TheftRecords.Count; i++)
            {
                TheftRecord r = sim.TheftRecords[i];
                if (r.NewGrudge == true)
                    newGrudges++;
                if (r.Detected == true)
                    kinTotal += r.KinCount;
            }
            int grudgeIndex = Array.IndexOf(ReportedRelationTypes, NpcRelationType.GrudgeAgainst);
            sb.AppendLine($"새 원한 쌍(피해자→도둑) {newGrudges}쌍 | 원한 관계 수(둘 다 생존) {sim.SocialStart.RelationCounts[grudgeIndex]} → " +
                          $"{sim.SocialEnd.RelationCounts[grudgeIndex]} | 발각 파장 가족·친구 −10: {kinTotal}명 " +
                          $"(현장 적발 1건당 {(caught > 0 ? kinTotal / (double)caught : 0d):F2}명)");
            long hostile = sim.PartyCounters.Rejections[(int)NpcPartyRejectReason.Hostile];
            long hostileGrudge = sim.PartyCounters.HostileGrudge;
            sb.AppendLine($"적대 관계로 파티 후보 탈락: 합계 {hostile} = 원한 포함 {hostileGrudge} / 라이벌만 {hostile - hostileGrudge}");
            sb.AppendLine($"훔친 장비 장착 → 도둑 NotableItemAcquired(D83 기존 업그레이드 경로) {sim.TheftThiefNotable}건 — 절도 Deed가 아니라 자랑 기록");

            long[] bytesEnd = new long[DeedKindCount];
            int[] countEnd = new int[DeedKindCount];
            MeasureDeedJsonBytes(sim, bytesEnd, countEnd);
            sb.Append("Deed 종류별 건수 시작→끝(증가, KB):");
            for (int k = 0; k < DeedKindCount; k++)
            {
                if (sim.DeedCountStart[k] == 0 && countEnd[k] == 0)
                    continue;
                sb.Append($" {(NpcDeedKind)k} {sim.DeedCountStart[k]}→{countEnd[k]}(+{countEnd[k] - sim.DeedCountStart[k]}, " +
                          $"+{(bytesEnd[k] - sim.DeedBytesStart[k]) / 1024f:F1}KB)");
            }
            sb.AppendLine();

            // ---- 독립 감사 (게임 카운터와 별개로 복제본 상태로 판정) ----
            int auditCaught = 0, auditEquipped = 0, auditBag = 0;
            for (int i = 0; i < sim.TheftRecords.Count; i++)
            {
                TheftRecord r = sim.TheftRecords[i];
                if (r.Detected == true)
                    auditCaught++;
                else if (r.Equipped == true)
                    auditEquipped++;
                else
                    auditBag++;
            }
            int passCount = 0, failCount = 0;
            StringBuilder check = new StringBuilder();
            void Item(bool pass, string label, string value)
            {
                if (pass == true)
                    passCount++;
                else
                    failCount++;
                check.AppendLine($"  [{(pass ? "PASS" : "FAIL")}] {label}: {value}");
            }
            Item(attempts == sim.TheftObserved && sim.TheftObserved == sim.TheftRecords.Count && sim.TheftUnaudited == 0 &&
                 invalid == 0, "시도 수 일치 (게임 카운터 = 관찰 = 감사 기록, 적용 실패 0)",
                $"{attempts} / {sim.TheftObserved} / {sim.TheftRecords.Count}, 미감사 {sim.TheftUnaudited}, Invalid {invalid}");
            Item(sim.TheftEligibilityFails == 0, "도둑·피해자·물건 자격 독립 검사 실패 0", $"{sim.TheftEligibilityFails}");
            Item(sim.TheftThiefCooldownViolations == 0 && sim.TheftVillageCooldownViolations == 0,
                "쿨다운 위반 0 (같은 도둑 168h / 같은 마을 72h)",
                $"도둑 {sim.TheftThiefCooldownViolations} / 마을 {sim.TheftVillageCooldownViolations}");
            Item(sim.TheftCaughtOk == auditCaught, "현장 적발: 물건 그대로·호감도 −40·원한(피해자→도둑) 1건·가족친구 −10·양쪽 Deed",
                $"{sim.TheftCaughtOk}/{auditCaught} ({(auditCaught > 0 ? Pct(sim.TheftCaughtOk, auditCaught) : 100f):F1}%)");
            Item(sim.TheftStolenOk == auditEquipped + auditBag,
                "안 들킨 절도: 물건 이동·관계 불변·도둑 절도 Deed 없음·버림 없음·피해자 Deed",
                $"{sim.TheftStolenOk}/{auditEquipped + auditBag} ({(auditEquipped + auditBag > 0 ? Pct(sim.TheftStolenOk, auditEquipped + auditBag) : 100f):F1}%)");
            Item(sim.TheftCapExceed == 0, "훔친 장비 장착 시 개인 상한 초과 0 (D119 이후 장착 자체가 없음)", $"{sim.TheftCapExceed}");
            Item(caught == auditCaught && equipped == auditEquipped && toBag == auditBag,
                "결과 분류 일치 (게임 카운터 적발/장착/가방 = 감사)",
                $"{caught}/{equipped}/{toBag} = {auditCaught}/{auditEquipped}/{auditBag}");
            sb.AppendLine($"==== P4a 절도 독립 감사: PASS {passCount} / FAIL {failCount} ====");
            sb.Append(check);
            for (int i = 0; i < sim.TheftAuditFailures.Count; i++)
                sb.AppendLine($"    ! {sim.TheftAuditFailures[i]}");
        }

        /// <summary>끝 상태 측정 (복제본이 끼워진 동안): 가방·관계 지표, 시작 생존 집단의 끝 전투력(사망자는 마지막 장비).</summary>
        private static void CaptureEndState(SimContext sim)
        {
            sim.BagEnd = MeasureBags(sim);
            sim.SocialEnd = MeasureSocial(sim);
            for (int i = 0; i < sim.CohortIds.Count; i++)
                sim.EndPower[sim.CohortIds[i]] = AR.s.Npc.GetNpcPower(sim.CohortIds[i]);
        }

        private static bool IsSimLiving(SimContext sim, int entityId)
        {
            return sim.Npcs.TryGetValue(entityId, out NpcSaveData? npc) == true &&
                   npc.Condition != CharacterConditions.Dead && npc.Activity.Phase != NpcActivityPhase.Dead;
        }

        /// <summary>생존 NPC 가방 통계. 매각 판정은 실제 매입가(VillageManager.GetNpcSellPrice)로 본다 — 마을 복제본이 끼워진 동안 부른다.</summary>
        private static BagSnapshot MeasureBags(SimContext sim)
        {
            BagSnapshot bag = new BagSnapshot { Captured = true };
            for (int n = 0; n < sim.NpcIds.Count; n++)
            {
                int entityId = sim.NpcIds[n];
                if (IsSimLiving(sim, entityId) == false)
                    continue;
                NpcSaveData npc = sim.Npcs[entityId];
                bag.Living++;
                bag.JsonBytes += Encoding.UTF8.GetByteCount(JsonConvert.SerializeObject(npc.Inventory, Formatting.None,
                    JsonSettings.Default));
                int used = 0;
                bool hasStealable = false;
                List<ItemData?> inventory = npc.Inventory;
                for (int i = 0; i < inventory.Count; i++)
                {
                    ItemData? item = inventory[i];
                    if (item == null)
                        continue;
                    used++;
                    bag.Items++;
                    int value = NpcManager.GetItemValue(item);
                    bag.ValueSum += value;
                    bag.Values.Add(value);
                    if (value > bag.MaxValue)
                        bag.MaxValue = value;
                    for (int b = 0; b < BagValueBucketUpper.Length; b++)
                    {
                        if (value <= BagValueBucketUpper[b])
                        {
                            bag.ValueBuckets[b]++;
                            break;
                        }
                    }

                    bool sellable = false;
                    if (npc.VillageId < 0)
                        bag.NoVillage++;
                    else if (AR.s.Village.GetNpcSellPrice(npc.VillageId, item) >= 0)
                    {
                        bag.Sellable++;
                        sellable = true;
                    }
                    else
                        bag.Unsellable++;

                    if (NpcManager.IsStealableBagItem(item) == true)
                    {
                        hasStealable = true;
                        bag.Stealable++;
                        bag.StealableValueSum += value;
                        if (sellable == true)
                            bag.StealableSellable++;
                    }
                }
                bag.UsedSlotHistogram[Mathf.Clamp(used, 0, bag.UsedSlotHistogram.Length - 1)]++;
                if (hasStealable == true)
                    bag.NpcsWithStealable++;
                if (NpcManager.HasFreeBagSlot(npc) == true)
                    bag.NpcsWithFreeSlot++;
            }
            bag.Values.Sort();
            return bag;
        }

        /// <summary>관계 지표: 두 NPC가 모두 살아 있는 호감도 쌍(분포)과 유형 관계 수(엔트리 1건 = From 인덱스에서 1회).</summary>
        private static SocialSnapshot MeasureSocial(SimContext sim)
        {
            SocialSnapshot social = new SocialSnapshot { Captured = true, AffinityMin = int.MaxValue, AffinityMax = int.MinValue };
            foreach (KeyValuePair<NpcPairKey, NpcAffinityEntry> pair in sim.Affinity)
            {
                if (IsSimLiving(sim, pair.Key.Lo) == false || IsSimLiving(sim, pair.Key.Hi) == false)
                    continue;
                int affinity = pair.Value.Affinity;
                social.AffinityPairs++;
                social.AffinitySum += affinity;
                if (affinity <= -50)
                    social.AffinityLowPairs++;
                if (affinity >= 50)
                    social.AffinityHighPairs++;
                if (affinity < social.AffinityMin)
                    social.AffinityMin = affinity;
                if (affinity > social.AffinityMax)
                    social.AffinityMax = affinity;
            }
            if (social.AffinityPairs == 0)
            {
                social.AffinityMin = 0;
                social.AffinityMax = 0;
            }

            foreach (KeyValuePair<int, List<NpcRelationEntry>> pair in sim.Relations)
            {
                List<NpcRelationEntry> list = pair.Value;
                for (int i = 0; i < list.Count; i++)
                {
                    NpcRelationEntry entry = list[i];
                    if (entry.FromEntityId != pair.Key || IsSimLiving(sim, entry.FromEntityId) == false ||
                        IsSimLiving(sim, entry.ToEntityId) == false)
                        continue;
                    int index = Array.IndexOf(ReportedRelationTypes, entry.Type);
                    if (index >= 0)
                        social.RelationCounts[index]++;
                }
            }
            return social;
        }

        private static void AppendTheftPrepSummary(StringBuilder sb, SimContext sim, int days)
        {
            sb.AppendLine("==== P4-0 절도 준비 (D99·D100·D107) ====");
            AppendBagLine(sb, "가방(시작)", sim.BagStart);
            AppendBagLine(sb, "가방(끝)", sim.BagEnd);

            int lateDays = days - sim.TheftLateStartDay + 1;
            AppendTheftFunnel(sb, $"절도 조건 깔때기 전 구간 {days}일", sim.TheftAll, days);
            AppendTheftFunnel(sb, $"절도 조건 깔때기 마지막 {lateDays}일(일 {sim.TheftLateStartDay}~{days})", sim.TheftLate,
                lateDays);

            // 야망별(주 야망) 평균 전투력 변화: 시작 생존 고정 집단(사망자는 마지막 장비) / 끝 생존자만
            int kinds = (int)NpcAmbitionKind.Cautious + 1;
            double[] startSum = new double[kinds], endSum = new double[kinds];
            double[] survivorStartSum = new double[kinds], survivorEndSum = new double[kinds];
            int[] count = new int[kinds], survivorCount = new int[kinds];
            for (int i = 0; i < sim.CohortIds.Count; i++)
            {
                int entityId = sim.CohortIds[i];
                if (sim.StartPower.TryGetValue(entityId, out float start) == false ||
                    sim.EndPower.TryGetValue(entityId, out float end) == false)
                    continue;
                int kind = (int)NpcAmbitionEvaluator.Evaluate(sim.Npcs[entityId].NpcStat).Primary;
                startSum[kind] += start;
                endSum[kind] += end;
                count[kind]++;
                if (IsSimLiving(sim, entityId) == true)
                {
                    survivorStartSum[kind] += start;
                    survivorEndSum[kind] += end;
                    survivorCount[kind]++;
                }
            }
            sb.Append("주 야망별 평균 전투력 변화 (고정 집단 — 사망자는 마지막 장비 / 끝 생존자만):");
            for (int k = (int)NpcAmbitionKind.Explorer; k < kinds; k++)
            {
                double cohortPct = startSum[k] > 0d ? (endSum[k] / startSum[k] - 1d) * 100d : 0d;
                double survivorPct = survivorStartSum[k] > 0d ? (survivorEndSum[k] / survivorStartSum[k] - 1d) * 100d : 0d;
                sb.Append($" {(NpcAmbitionKind)k} {cohortPct:+0.00;-0.00}% / {survivorPct:+0.00;-0.00}% (n={count[k]}) |");
            }
            sb.AppendLine();

            SocialSnapshot s0 = sim.SocialStart, s1 = sim.SocialEnd;
            sb.Append("유형 관계 수 시작 → 끝 (둘 다 생존):");
            for (int i = 0; i < ReportedRelationTypes.Length; i++)
                sb.Append($" {ReportedRelationTypes[i]} {s0.RelationCounts[i]}→{s1.RelationCounts[i]}");
            sb.AppendLine($" | 적대(Rival·Grudge)로 파티 후보 탈락 {sim.PartyCounters.Rejections[(int)NpcPartyRejectReason.Hostile]}건");
            sb.AppendLine($"호감도 쌍(둘 다 생존) 시작 → 끝: {s0.AffinityPairs} → {s1.AffinityPairs}, 평균 " +
                          $"{(s0.AffinityPairs > 0 ? s0.AffinitySum / (double)s0.AffinityPairs : 0d):F2} → " +
                          $"{(s1.AffinityPairs > 0 ? s1.AffinitySum / (double)s1.AffinityPairs : 0d):F2}, " +
                          $"−50 이하 {s0.AffinityLowPairs} → {s1.AffinityLowPairs}, +50 이상 {s0.AffinityHighPairs} → {s1.AffinityHighPairs}, " +
                          $"최소/최대 {s0.AffinityMin}/{s0.AffinityMax} → {s1.AffinityMin}/{s1.AffinityMax}");
        }

        private static void AppendBagLine(StringBuilder sb, string label, BagSnapshot bag)
        {
            if (bag.Captured == false)
            {
                sb.AppendLine($"{label}: 측정 안 됨");
                return;
            }
            sb.Append($"{label}: 생존 {bag.Living}명, 사용 칸 분포 0~{bag.UsedSlotHistogram.Length - 1}:");
            for (int i = 0; i < bag.UsedSlotHistogram.Length; i++)
                sb.Append($" {Pct(bag.UsedSlotHistogram[i], bag.Living):F1}%");
            int median = bag.Values.Count > 0 ? bag.Values[bag.Values.Count / 2] : 0;
            sb.AppendLine($" | 훔칠 물건 보유 NPC {bag.NpcsWithStealable}명({Pct(bag.NpcsWithStealable, bag.Living):F1}%), " +
                          $"빈칸 있는 NPC {bag.NpcsWithFreeSlot}명({Pct(bag.NpcsWithFreeSlot, bag.Living):F1}%)");
            sb.AppendLine($"  아이템 {bag.Items}개: 비스택·인스턴스(훔칠 수 있음) {bag.Stealable} / 그 밖 {bag.Items - bag.Stealable} | " +
                          $"매각 가능 {bag.Sellable} / 불가 {bag.Unsellable} / 소속 마을 없음 {bag.NoVillage} | " +
                          $"훔칠 수 있는 것 중 매각 가능 {bag.StealableSellable} | 가방 JSON {bag.JsonBytes / 1024}KB");
            sb.Append($"  가치(BasePrice×수량): 평균 {(bag.Items > 0 ? bag.ValueSum / (double)bag.Items : 0d):F1} / 중앙 {median} / " +
                      $"최대 {bag.MaxValue} / 훔칠 수 있는 것 평균 {(bag.Stealable > 0 ? bag.StealableValueSum / (double)bag.Stealable : 0d):F1} | 분포");
            for (int b = 0; b < BagValueBucketLabels.Length; b++)
                sb.Append($" {BagValueBucketLabels[b]}={bag.ValueBuckets[b]}");
            sb.AppendLine();
        }

        private static void AppendTheftFunnel(StringBuilder sb, string label, TheftFunnel f, int windowDays)
        {
            float scale30 = 30f / Mathf.Max(1, windowDays);
            sb.AppendLine($"{label} (P4-0 깔때기 — 실제 판정 직전 상태, 가정 쿨다운은 진단 별도 표): Rest 정상 종료 {f.RestEnds}회 (파티 합류 경로 제외)");
            string[] names = { "기회 주 야망", "기회 보조 야망" };
            for (int k = 0; k < 2; k++)
            {
                sb.AppendLine($"  {names[k]}: 도둑 후보 {f.Thieves[k]} → 빈칸 {f.FreeSlot[k]} ({Pct(f.FreeSlot[k], f.Thieves[k]):F1}%) → " +
                              $"피해자 ≥ 1 {f.WithVictim[k]} ({Pct(f.WithVictim[k], f.FreeSlot[k]):F1}%, 평균 피해자 " +
                              $"{(f.WithVictim[k] > 0 ? f.VictimSum[k] / (double)f.WithVictim[k] : 0d):F2}명 / 같은 마을 물건 보유 생존 NPC 평균 " +
                              $"{(f.FreeSlot[k] > 0 ? f.SameVillageItemSum[k] / (double)f.FreeSlot[k] : 0d):F2}명) → " +
                              $"쿨다운 가정(도둑 {TheftThiefCooldownHours:F0}h·마을 {TheftVillageCooldownHours:F0}h, 통과 = 시도) {f.CooldownPass[k]}");
            }
            long passTotal = f.CooldownPass[0] + f.CooldownPass[1];
            sb.AppendLine($"  쿨다운 가정 통과 합계 {passTotal} (30일 환산 {passTotal * scale30:F0}) — 기본 확률 100%일 때의 상한");
            sb.Append("  가상 시도(쿨다운 포함, 확률 = 기본 × 야망 배율 1.0/0.5, 성향 배율 제외), 주+보조=합(30일 환산):");
            for (int c = 0; c < TheftBaseChances.Length; c++)
            {
                long total = f.VirtualAttempts[c, 0] + f.VirtualAttempts[c, 1];
                sb.Append($" {TheftBaseChances[c] * 100f:F0}% {f.VirtualAttempts[c, 0]}+{f.VirtualAttempts[c, 1]}={total}({total * scale30:F0}) |");
            }
            sb.AppendLine();
        }

        private static float Pct(long part, long total)
        {
            return total > 0 ? part * 100f / total : 0f;
        }

        private static string F(float value, int digits)
        {
            return value.ToString("F" + digits, CultureInfo.InvariantCulture);
        }

        // ================= 대화 토픽 · 자랑 Deed (P2c) =================

        /// <summary>
        /// 대화 토픽 검증 (P2c, D60·D61·D72). 소문을 가진 생존 NPC 전원의 야망 소문 토픽 후보를 실제 selector 코드
        /// (DialogueTopicSelector.CollectAmbitionRumorTopics)로 만들고, 각 토픽이 가리키는 대상(던전 슬롯·NPC·아이템)이
        /// 복제본에 실제로 있는지 IsRumorValid와 다른 경로로 대조한다. 방향 표현은 복제 슬롯 입구 좌표로 다시 계산해 맞춰 본다.
        /// 생존 NPC 전원에 대해 실제 대화 1회분 Collect도 돌려 자랑(Boast) 토픽 출처와 레퍼토리 포함률을 센다.
        /// UnityEngine.Random을 쓰지만 RunCore가 끝에서 상태를 되돌린다.
        /// </summary>
        private static void AuditDialogueTopics(SimContext sim)
        {
            TopicAudit a = sim.Topics;
            System.Diagnostics.Stopwatch watch = System.Diagnostics.Stopwatch.StartNew();
            MethodInfo? buildRepertoire = typeof(DialogueTopicSelector).GetMethod("BuildRepertoire",
                BindingFlags.NonPublic | BindingFlags.Static);
            List<DialogueTopic> scratch = new List<DialogueTopic>();
            List<DialogueTopic> applicable = new List<DialogueTopic>();
            int[] examplesPerKind = new int[4];
            AffinityTier[] tiers = { AffinityTier.Stranger, AffinityTier.Friend };

            for (int n = 0; n < sim.NpcIds.Count; n++)
            {
                int npcId = sim.NpcIds[n];
                NpcSaveData save = sim.Npcs[npcId];
                if (save.Condition == CharacterConditions.Dead || save.Activity.Phase == NpcActivityPhase.Dead)
                    continue;
                a.LivingNpcs++;

                if (save.Rumors.Count > 0)
                {
                    a.RumorHolders++;
                    AuditHeldRumors(sim, a, npcId, save);
                    scratch.Clear();
                    DialogueTopicSelector.CollectAmbitionRumorTopics(npcId, scratch);
                    if (scratch.Count > 0)
                        a.NpcsWithRumorTopic++;
                    for (int t = 0; t < scratch.Count; t++)
                        AuditRumorTopic(sim, a, npcId, save, scratch[t], examplesPerKind);
                }

                // 실제 대화 1회분 (Collect) — 종류마다 최대 1개, 자랑 토픽 출처
                NpcDialogueContext? ctx = NpcDialogueContextBuilder.Build(npcId, default);
                if (ctx == null)
                    continue;
                List<DialogueTopic> topics = DialogueTopicSelector.Collect(npcId, ctx, default);
                int dungeonCount = 0, itemCount = 0, betrayalCount = 0;
                DialogueTopic? boast = null;
                DialogueTopic? theftTopic = null;
                for (int t = 0; t < topics.Count; t++)
                {
                    if (topics[t].Type == DialogueTopicType.DungeonRumor)
                        dungeonCount++;
                    else if (topics[t].Type == DialogueTopicType.ItemOwnerRumor)
                        itemCount++;
                    else if (topics[t].Type == DialogueTopicType.BetrayalRumor)
                        betrayalCount++;
                    else if (topics[t].Type == DialogueTopicType.Boast)
                        boast = topics[t];
                    else if (topics[t].Type == DialogueTopicType.TheftVictim)
                        theftTopic = topics[t];
                    if (topics[t].Rumor != null && topics[t].Rumor!.Kind == NpcRumorKind.ItemSold)
                        a.ItemSoldTopics++;
                }
                if (dungeonCount > 0)
                    a.CollectDungeonTopicNpcs++;
                if (itemCount > 0)
                    a.CollectItemTopicNpcs++;
                if (betrayalCount > 0)
                    a.CollectBetrayalTopicNpcs++;
                if (dungeonCount + itemCount + betrayalCount > 1)
                    a.CollectMultiplePerType++;
                AuditTheftVictimTopic(a, npcId, save, ctx.NpcName, theftTopic);

                bool hasNotable = false, hasCleared = false;
                // 전체 주요 이력에서 시각으로 독립 대조한다. 같은 시각이면 던전 완료를 우선한다.
                NpcDeedKind latestBoastKind = NpcDeedKind.VentureFailed;
                float latestBoastTime = float.NegativeInfinity;
                for (int d = 0; d < save.Deeds.Count; d++)
                {
                    NpcDeedEntry deed = save.Deeds[d];
                    if (deed == null)
                        continue;
                    hasNotable |= deed.Kind == NpcDeedKind.NotableItemAcquired;
                    hasCleared |= deed.Kind == NpcDeedKind.DungeonCleared;
                    if ((deed.Kind == NpcDeedKind.NotableItemAcquired || deed.Kind == NpcDeedKind.DungeonCleared) &&
                        (deed.When > latestBoastTime ||
                         (deed.When == latestBoastTime && deed.Kind == NpcDeedKind.DungeonCleared)))
                    {
                        latestBoastKind = deed.Kind;
                        latestBoastTime = deed.When;
                    }
                }
                if (hasNotable == true)
                    a.NotableDeedNpcs++;
                if (hasCleared == true)
                    a.ClearedDeedNpcs++;
                if (boast != null && (hasNotable == true || hasCleared == true))
                {
                    NpcDeedKind kind = latestBoastKind;
                    if (kind == NpcDeedKind.NotableItemAcquired)
                        a.BoastItemNpcs++;
                    else if (kind == NpcDeedKind.DungeonCleared)
                        a.BoastClearedNpcs++;
                    if (a.BoastExamples.Count < MaxTopicExamplesPerKind * 2 &&
                        (kind == NpcDeedKind.DungeonCleared || a.BoastExamples.Count < MaxTopicExamplesPerKind))
                        a.BoastExamples.Add($"[{kind}] {ctx.NpcName}(#{npcId}): {boast.BaseLine}");
                }

                // 레퍼토리 포함률 (Select와 같은 티어 필터 → 실제 BuildRepertoire)
                if (buildRepertoire == null || (dungeonCount + itemCount + betrayalCount == 0 && boast == null && theftTopic == null))
                    continue;
                for (int k = 0; k < tiers.Length; k++)
                {
                    applicable.Clear();
                    for (int t = 0; t < topics.Count; t++)
                    {
                        if (topics[t].MinTier <= tiers[k] && topics[t].Type != DialogueTopicType.Greeting)
                            applicable.Add(topics[t]);
                    }
                    List<DialogueTopic>? repertoire = buildRepertoire.Invoke(null,
                        new object[] { npcId, applicable, tiers[k] }) as List<DialogueTopic>;
                    if (repertoire == null)
                        continue;
                    bool rumorIn = false, boastIn = false, theftIn = false;
                    for (int t = 0; t < repertoire.Count; t++)
                    {
                        rumorIn |= repertoire[t].Type == DialogueTopicType.DungeonRumor ||
                                   repertoire[t].Type == DialogueTopicType.ItemOwnerRumor ||
                                   repertoire[t].Type == DialogueTopicType.BetrayalRumor;
                        boastIn |= repertoire[t].Type == DialogueTopicType.Boast;
                        theftIn |= repertoire[t].Type == DialogueTopicType.TheftVictim;
                    }
                    if (theftTopic != null)
                    {
                        a.TheftRepertoireBase[k]++;
                        if (theftIn == true)
                            a.TheftRepertoireIn[k]++;
                    }
                    if (dungeonCount + itemCount + betrayalCount > 0)
                    {
                        a.RumorRepertoireBase[k]++;
                        if (rumorIn == true)
                            a.RumorRepertoireIn[k]++;
                    }
                    if (boast != null)
                    {
                        a.BoastRepertoireBase[k]++;
                        if (boastIn == true)
                            a.BoastRepertoireIn[k]++;
                    }
                }
            }
            watch.Stop();
            a.ElapsedMs = watch.ElapsedMilliseconds;
        }

        /// <summary>
        /// 피해자 도난 사연: 최신 사연 보유 NPC 수, 실제 대화 1회분에 도난 토픽이 나온 수, 문구(범인을 알면 이름, 모르면 "누가"),
        /// 도둑 쪽 Deed만 있는 NPC에게 도난 토픽이 나오지 않는지(도둑은 침묵).
        /// </summary>
        private static void AuditTheftVictimTopic(TopicAudit a, int npcId, NpcSaveData save, string npcName, DialogueTopic? topic)
        {
            NpcDeedEntry? latestVictimDeed = save.PersonalStories?.TheftVictim;
            bool hasThiefDeed = false;
            for (int d = 0; d < save.Deeds.Count; d++)
            {
                NpcDeedEntry deed = save.Deeds[d];
                if (deed == null)
                    continue;
                if (deed.Kind == NpcDeedKind.CaughtStealing || deed.Kind == NpcDeedKind.TheftExposed)
                    hasThiefDeed = true;
            }
            if (latestVictimDeed != null)
                a.TheftVictimDeedNpcs++;
            else if (hasThiefDeed == true)
            {
                a.ThiefOnlyNpcs++;
                if (topic != null)
                    a.ThiefSilenceViolations++;
            }
            if (topic == null || latestVictimDeed == null)
                return;

            a.TheftVictimTopicNpcs++;
            string item = string.IsNullOrEmpty(latestVictimDeed.ItemDisplayName) ? "물건" : latestVictimDeed.ItemDisplayName;
            bool lineOk = topic.BaseLine.Contains(item);
            if (latestVictimDeed.Kind == NpcDeedKind.ItemStolen)
            {
                a.TheftTopicAnonymous++;
                lineOk &= topic.BaseLine.StartsWith("누가", StringComparison.Ordinal);
            }
            else
            {
                a.TheftTopicNamed++;
                string thiefName = latestVictimDeed.ParticipantSnapshots.Count > 0
                    ? latestVictimDeed.ParticipantSnapshots[0].DisplayName
                    : NpcRumorPhrasing.NpcName(latestVictimDeed.Participants.Count > 0 ? latestVictimDeed.Participants[0] : -1);
                lineOk &= string.IsNullOrEmpty(thiefName) == false && topic.BaseLine.Contains(thiefName);
            }
            if (lineOk == false)
                a.TheftTopicLineFail++;
            if (a.TheftExamples.Count < MaxTopicExamplesPerKind)
                a.TheftExamples.Add($"[{latestVictimDeed.Kind}] {npcName}(#{npcId}): {topic.BaseLine}");
        }

        /// <summary>보유 소문 분류: 무효(토픽이 되면 안 됨) / 유효하지만 토픽에서 빠지는 것(자기 장비·다른 마을).</summary>
        private static void AuditHeldRumors(SimContext sim, TopicAudit a, int npcId, NpcSaveData save)
        {
            for (int r = 0; r < save.Rumors.Count; r++)
            {
                NpcRumor rumor = save.Rumors[r];
                a.HeldRumors++;
                if (AuditRumorValidity(sim, rumor, out _) == false)
                {
                    a.HeldInvalid++;
                    continue;
                }
                a.HeldValid++;
                if (rumor.Kind == NpcRumorKind.ItemOwner && rumor.SubjectNpcId == npcId)
                    a.ExcludedOwnItem++;
                else if (rumor.Kind == NpcRumorKind.DungeonInfo && rumor.VillageId != save.VillageId)
                    a.ExcludedOtherVillage++;
            }
        }

        /// <summary>토픽 1개가 가리키는 대상이 복제본에 실제로 있는가 + 문구에 대상 이름·방향이 들어갔는가.</summary>
        private static void AuditRumorTopic(SimContext sim, TopicAudit a, int npcId, NpcSaveData save, DialogueTopic topic,
            int[] examplesPerKind)
        {
            NpcRumor? rumor = topic.Rumor;
            string? failure = null;
            if (rumor == null)
            {
                a.TargetFail++;
                AddFailure(a, $"npc={npcId} {topic.Type} 근거 소문 없음");
                return;
            }

            if (AuditRumorValidity(sim, rumor, out int reason) == false)
                failure = $"독립 감사 무효(reason={reason})";
            if (rumor.SourceNpcId < 0)
                a.FirsthandTopics++;
            else if (NpcRumorPhrasing.HasNamedSource(rumor) == true)
                a.NamedSourceTopics++;
            else
                a.AnonymousSourceTopics++;

            bool subjectKnown = sim.Npcs.ContainsKey(rumor.SubjectNpcId);
            if (subjectKnown == true)
                a.SubjectKnown++;
            else
                a.SubjectUnknown++;
            string subjectName = NpcRumorPhrasing.NpcName(rumor.SubjectNpcId);
            // 본인이 당사자인 직접 소문은 1인칭("내가")이라 이름이 안 들어간다
            bool selfFirsthand = rumor.SubjectNpcId == npcId && rumor.SourceNpcId < 0;
            if (selfFirsthand == false && topic.BaseLine.Contains(subjectName) == false)
            {
                a.NameMissingInLine++;
                failure ??= $"문구에 당사자 이름({subjectName}) 없음";
            }

            int kindIndex;
            if (rumor.Kind == NpcRumorKind.DungeonInfo)
            {
                a.DungeonTopics++;
                if (rumor.Cleared == true)
                    a.ClearedTopics++;
                kindIndex = rumor.Cleared == true ? 1 : 0;
                if (rumor.VillageId != save.VillageId)
                    failure ??= "다른 마을 던전 소문이 토픽이 됨";
                DungeonSlot? slot = FindSimSlot(sim, rumor.VillageId, rumor.SlotIndex);
                if (slot == null)
                    failure ??= "슬롯 없음";
                if (topic.RumorPlace == NpcRumorPlace.Bearing)
                {
                    a.WithBearing++;
                    if (topic.RumorBearing >= 0 && topic.RumorBearing < 8)
                        a.BearingHistogram[topic.RumorBearing]++;
                    int expected = slot != null && slot.HasEntrance == true
                        ? ExpectedBearing(sim, rumor.VillageId, slot)
                        : -2;
                    if (expected != topic.RumorBearing ||
                        topic.BaseLine.Contains(NpcRumorPhrasing.BearingName(topic.RumorBearing)) == false)
                    {
                        a.BearingMismatch++;
                        failure ??= $"방향 불일치 (토픽 {topic.RumorBearing}, 입구 기준 {expected})";
                    }
                }
                else
                {
                    a.WithoutBearing++;
                    if (slot != null && slot.HasEntrance == true && ExpectedBearing(sim, rumor.VillageId, slot) >= 0)
                    {
                        a.BearingMismatch++;
                        failure ??= "입구가 있는데 방향 없음";
                    }
                }
            }
            else if (rumor.Kind == NpcRumorKind.Betrayal)
            {
                // P4b (D105·D106): 가해자·피해자 생존, 말하는 NPC는 둘 중 누구도 아님, 문구에 피해자·아이템 이름
                a.BetrayalTopics++;
                kindIndex = 3;
                if (rumor.SubjectNpcId == npcId || rumor.VictimNpcId == npcId)
                    failure ??= "가해자·피해자 본인이 배신 소문 토픽을 말함";
                if (IsSimLiving(sim, rumor.SubjectNpcId) == false || IsSimLiving(sim, rumor.VictimNpcId) == false)
                    failure ??= "가해자·피해자 없음/사망";
                string victimName = NpcRumorPhrasing.NpcName(rumor.VictimNpcId);
                if (topic.BaseLine.Contains(victimName) == false)
                    failure ??= $"문구에 피해자 이름({victimName}) 없음";
                string itemName = NpcRumorPhrasing.ItemName(rumor.ItemId);
                if (topic.BaseLine.Contains(itemName) == false)
                    failure ??= $"문구에 아이템 이름({itemName}) 없음";
            }
            else if (rumor.Kind != NpcRumorKind.ItemOwner)
            {
                a.ItemTopics++;
                kindIndex = 2;
                failure ??= $"토픽이 되면 안 되는 종류({rumor.Kind})";
            }
            else
            {
                a.ItemTopics++;
                kindIndex = 2;
                if (rumor.SubjectNpcId == npcId)
                    failure ??= "자기 장비 소문이 토픽이 됨";
                if (sim.Npcs.TryGetValue(rumor.SubjectNpcId, out NpcSaveData? owner) == false ||
                    owner.Condition == CharacterConditions.Dead)
                    failure ??= "소유자 없음/사망";
                else
                {
                    ItemData? item = rumor.EquipSlot >= 0 && rumor.EquipSlot < owner.InventoryEquip.Length
                        ? owner.InventoryEquip[rumor.EquipSlot]
                        : null;
                    if (item == null || item.ItemInstanceId != rumor.ItemInstanceId)
                        failure ??= "소유자 장비 불일치";
                    else if (item.Table != null && topic.BaseLine.Contains(item.Table.Name) == false)
                        failure ??= $"문구에 아이템 이름({item.Table.Name}) 없음";
                }
            }

            if (failure == null)
                a.TargetOk++;
            else
            {
                a.TargetFail++;
                AddFailure(a, $"npc={npcId} {topic.Type}: {failure} | {topic.BaseLine}");
            }

            if (examplesPerKind[kindIndex] < MaxTopicExamplesPerKind)
            {
                examplesPerKind[kindIndex]++;
                string place = topic.Type == DialogueTopicType.DungeonRumor
                    ? $" 장소={topic.RumorPlace}{(topic.RumorPlace == NpcRumorPlace.Bearing ? "/" + NpcRumorPhrasing.BearingName(topic.RumorBearing) : string.Empty)}" +
                      $" 대상=마을{rumor.VillageId}·슬롯{rumor.SlotIndex}·세대{rumor.Generation}"
                    : rumor.Kind == NpcRumorKind.Betrayal
                        ? $" 대상=가해자#{rumor.SubjectNpcId}·피해자#{rumor.VictimNpcId}·{rumor.BetrayalKind}"
                        : $" 대상=소유자#{rumor.SubjectNpcId}·슬롯{rumor.EquipSlot}·인스턴스{rumor.ItemInstanceId}";
                a.Examples.Add($"[{topic.Type}{(rumor.Cleared ? "/Cleared" : string.Empty)}] {NpcRumorPhrasing.NpcName(npcId)}(#{npcId}, 마을 {save.VillageId}):" +
                               $" \"{topic.BaseLine}\" —{place} Hops={rumor.Hops}");
            }
        }

        private static void AddFailure(TopicAudit a, string message)
        {
            if (a.Failures.Count < MaxExampleCases)
                a.Failures.Add(message);
        }

        private static DungeonSlot? FindSimSlot(SimContext sim, int villageId, int slotIndex)
        {
            if (sim.DungeonVillageSlots.TryGetValue(villageId, out VillageDungeonSlots? slots) == false)
                return null;
            for (int i = 0; i < slots.Slots.Count; i++)
            {
                if (slots.Slots[i] != null && slots.Slots[i].SlotIndex == slotIndex)
                    return slots.Slots[i];
            }
            return null;
        }

        /// <summary>복제 마을 중심 → 복제 슬롯 입구(타일 중심)의 8방위. 너무 가까우면 -1 (NpcManager와 독립 계산).</summary>
        private static int ExpectedBearing(SimContext sim, int villageId, DungeonSlot slot)
        {
            if (sim.Villages.TryGetValue(villageId, out VillageData? village) == false)
                return -3;
            float dx = slot.EntranceX + 0.5f - village.PositionX;
            float dy = slot.EntranceY + 0.5f - village.PositionY;
            if (dx * dx + dy * dy < NpcManager.RumorBearingMinDistance * NpcManager.RumorBearingMinDistance)
                return -1;
            // 북(위)에서 시계 방향 각도 → 45도 구간
            double degrees = Math.Atan2(dx, dy) * 180d / Math.PI;
            if (degrees < 0d)
                degrees += 360d;
            return (int)Math.Floor((degrees + 22.5d) / 45d) % 8;
        }

        /// <summary>주요 이력·최신 절도 피해 사연의 종류별 JSON 크기·개수.</summary>
        private static void MeasureDeedJsonBytes(SimContext sim, long[] bytes, int[] counts)
        {
            Array.Clear(bytes, 0, bytes.Length);
            Array.Clear(counts, 0, counts.Length);
            for (int i = 0; i < sim.NpcIds.Count; i++)
            {
                List<NpcDeedEntry> deeds = sim.Npcs[sim.NpcIds[i]].Deeds;
                for (int d = 0; d < deeds.Count; d++)
                {
                    NpcDeedEntry deed = deeds[d];
                    if (deed == null)
                        continue;
                    int kind = Mathf.Clamp((int)deed.Kind, 0, DeedKindCount - 1);
                    bytes[kind] += Encoding.UTF8.GetByteCount(JsonConvert.SerializeObject(deed, Formatting.None,
                        JsonSettings.Default)) + 1;
                    counts[kind]++;
                }
                NpcDeedEntry? victim = sim.Npcs[sim.NpcIds[i]].PersonalStories?.TheftVictim;
                if (victim != null)
                {
                    int kind = (int)victim.Kind;
                    bytes[kind] += Encoding.UTF8.GetByteCount(JsonConvert.SerializeObject(victim, Formatting.None,
                        JsonSettings.Default)) + 1;
                    counts[kind]++;
                }
            }
        }

        private static void AppendTopicSummary(StringBuilder sb, SimContext sim, int days)
        {
            TopicAudit a = sim.Topics;
            sb.AppendLine("---- 대화 토픽 · 자랑 (P2c) ----");
            sb.AppendLine($"검증 대상: 생존 NPC {a.LivingNpcs}명 중 소문 보유 {a.RumorHolders}명 → 소문 토픽 후보가 생긴 NPC {a.NpcsWithRumorTopic}명 " +
                          $"({a.ElapsedMs}ms)");
            sb.AppendLine($"보유 소문 {a.HeldRumors}개: 유효 {a.HeldValid} / 무효 {a.HeldInvalid}(토픽 제외) | 유효하지만 제외: 자기 장비 {a.ExcludedOwnItem}, " +
                          $"다른 마을 던전 {a.ExcludedOtherVillage}");
            long topics = a.DungeonTopics + a.ItemTopics + a.BetrayalTopics;
            sb.AppendLine($"소문 토픽 {topics}개: 던전 {a.DungeonTopics}(그중 완료 새 세대 {a.ClearedTopics}) / 장비 소유 {a.ItemTopics} / " +
                          $"배신 {a.BetrayalTopics}");
            sb.AppendLine($"  대상 실제 존재(독립 대조: 슬롯·세대 / 소유자 생존·장비 인스턴스 / 문구의 이름·아이템·방향): {a.TargetOk}/{topics} = " +
                          $"{Pct(a.TargetOk, topics):F2}% (실패 {a.TargetFail})");
            for (int i = 0; i < a.Failures.Count; i++)
                sb.AppendLine($"    ! {a.Failures[i]}");
            sb.Append($"  방향 표현(던전 토픽): 방향 있음 {a.WithBearing} ({Pct(a.WithBearing, a.DungeonTopics):F1}%) / 없음(\"이 근처\") {a.WithoutBearing} " +
                      $"| 불일치 {a.BearingMismatch} | 8방위 분포:");
            for (int k = 0; k < 8; k++)
                sb.Append($" {NpcRumorPhrasing.BearingName(k)} {a.BearingHistogram[k]}");
            sb.AppendLine();
            sb.AppendLine($"  당사자 NPC가 세이브에 있음 {a.SubjectKnown} / 없음(\"어떤 모험가\") {a.SubjectUnknown} | 문구에 이름 누락 {a.NameMissingInLine}");
            sb.AppendLine($"  말투(출처): 직접 겪음 {a.FirsthandTopics} / \"○○에게 들었는데\" {a.NamedSourceTopics} / \"누가 그러던데\" {a.AnonymousSourceTopics}");
            sb.AppendLine($"  실제 대화 1회분(Collect): 던전 소문 토픽 NPC {a.CollectDungeonTopicNpcs}명, 장비 소문 토픽 NPC {a.CollectItemTopicNpcs}명, " +
                          $"배신 소문 토픽 NPC {a.CollectBetrayalTopicNpcs}명, " +
                          $"야망 소문 토픽 2개 이상 {a.CollectMultiplePerType}명(0이어야 함 — 대화 1회분 최대 1개) | 판매 소문 토픽 {a.ItemSoldTopics}개(0이어야 함)");
            sb.AppendLine($"  레퍼토리 포함률(소문 토픽이 있는 NPC 중): 낯선 사이 {Pct(a.RumorRepertoireIn[0], a.RumorRepertoireBase[0]):F1}% " +
                          $"(n={a.RumorRepertoireBase[0]}) / 친구 {Pct(a.RumorRepertoireIn[1], a.RumorRepertoireBase[1]):F1}% (n={a.RumorRepertoireBase[1]})");
            sb.AppendLine("  예시:");
            for (int i = 0; i < a.Examples.Count; i++)
                sb.AppendLine($"    - {a.Examples[i]}");

            sb.AppendLine($"자랑(Boast) 토픽: NotableItemAcquired 기반 {a.BoastItemNpcs}명 (Deed 보유 {a.NotableDeedNpcs}명) / " +
                          $"DungeonCleared 기반 {a.BoastClearedNpcs}명 (Deed 보유 {a.ClearedDeedNpcs}명)");
            sb.AppendLine($"  레퍼토리 포함률(자랑 토픽이 있는 NPC 중): 낯선 사이 {Pct(a.BoastRepertoireIn[0], a.BoastRepertoireBase[0]):F1}% " +
                          $"(n={a.BoastRepertoireBase[0]}) / 친구 {Pct(a.BoastRepertoireIn[1], a.BoastRepertoireBase[1]):F1}% (n={a.BoastRepertoireBase[1]})");
            for (int i = 0; i < a.BoastExamples.Count; i++)
                sb.AppendLine($"    - {a.BoastExamples[i]}");
            sb.AppendLine($"도난 사연(TheftVictim, P4b): 최신 피해 사연 보유 {a.TheftVictimDeedNpcs}명 → 대화 1회분에 나옴 {a.TheftVictimTopicNpcs}명 " +
                          $"(범인 이름 {a.TheftTopicNamed} / \"누가\" {a.TheftTopicAnonymous}, 문구 불일치 {a.TheftTopicLineFail}) | " +
                          $"도둑 Deed만 있는 NPC {a.ThiefOnlyNpcs}명 중 도난 토픽 {a.ThiefSilenceViolations}명(0이어야 함)");
            sb.AppendLine($"  레퍼토리 포함률(도난 토픽이 있는 NPC 중): 낯선 사이 {Pct(a.TheftRepertoireIn[0], a.TheftRepertoireBase[0]):F1}% " +
                          $"(n={a.TheftRepertoireBase[0]}) / 친구 {Pct(a.TheftRepertoireIn[1], a.TheftRepertoireBase[1]):F1}% (n={a.TheftRepertoireBase[1]})");
            for (int i = 0; i < a.TheftExamples.Count; i++)
                sb.AppendLine($"    - {a.TheftExamples[i]}");

            long[] bytesEnd = new long[DeedKindCount];
            int[] countEnd = new int[DeedKindCount];
            MeasureDeedJsonBytes(sim, bytesEnd, countEnd);
            long totalStart = 0, totalEnd = 0;
            for (int k = 0; k < DeedKindCount; k++)
            {
                totalStart += sim.DeedBytesStart[k];
                totalEnd += bytesEnd[k];
            }
            int notable = (int)NpcDeedKind.NotableItemAcquired;
            int cleared = (int)NpcDeedKind.DungeonCleared;
            int wounded = (int)NpcDeedKind.Wounded;
            int severe = (int)NpcDeedKind.SevereWound;
            sb.AppendLine($"Deed JSON: 시작 {totalStart / 1024f:F0}KB → 끝 {totalEnd / 1024f:F0}KB (+{(totalEnd - totalStart) / 1024f:F0}KB) | " +
                          $"NotableItemAcquired {sim.DeedCountStart[notable]}→{countEnd[notable]}건 " +
                          $"(+{(bytesEnd[notable] - sim.DeedBytesStart[notable]) / 1024f:F1}KB, 1건 평균 " +
                          $"{(countEnd[notable] > 0 ? bytesEnd[notable] / (float)countEnd[notable] : 0f):F0}B) | " +
                          $"DungeonCleared {sim.DeedCountStart[cleared]}→{countEnd[cleared]}건 " +
                          $"(+{(bytesEnd[cleared] - sim.DeedBytesStart[cleared]) / 1024f:F1}KB) | " +
                          $"Wounded {sim.DeedCountStart[wounded]}→{countEnd[wounded]}건 (+{(bytesEnd[wounded] - sim.DeedBytesStart[wounded]) / 1024f:F0}KB) | " +
                          $"SevereWound {sim.DeedCountStart[severe]}→{countEnd[severe]}건 " +
                          $"(+{(bytesEnd[severe] - sim.DeedBytesStart[severe]) / 1024f:F1}KB, 1건 평균 " +
                          $"{(countEnd[severe] > 0 ? bytesEnd[severe] / (float)countEnd[severe] : 0f):F0}B)");
            // 종류별 1건 평균 크기 (끝 시점, 배열 쉼표 포함) — Deed 직렬화 크기 비교용
            sb.Append("  Deed 종류별 건수·1건 평균(끝):");
            for (int k = 0; k < DeedKindCount; k++)
            {
                if (countEnd[k] == 0)
                    continue;
                sb.Append($" {(NpcDeedKind)k} {countEnd[k]}건/{bytesEnd[k] / (float)countEnd[k]:F0}B");
            }
            sb.AppendLine();

            int party = (int)NpcDeedKind.PartyAffinityChanged;
            int sharedPairs = 0;
            long hunts = 0, delves = 0, clears = 0, sharedBytes = 0;
            foreach (var pair in sim.Affinity)
            {
                NpcSharedActivityStats? stats = pair.Value.SharedActivities;
                if (stats == null)
                    continue;
                sharedPairs++;
                hunts += stats.HuntCount;
                delves += stats.DungeonDelveCount;
                clears += stats.DungeonClearCount;
                sharedBytes += Encoding.UTF8.GetByteCount(JsonConvert.SerializeObject(stats, Formatting.None, JsonSettings.Default));
            }
            sb.AppendLine($"  공동 활동 통계: {sharedPairs}쌍 · 사냥 {hunts} · 던전 원정 {delves} · 클리어 {clears} · " +
                          $"{sharedBytes / 1024f:F1}KB | 구 파티 호감도 Deed {countEnd[party]}건 (0이어야 함)");
        }

        private static void AttachRumorProbes(SimContext sim, List<FieldSwap> swaps)
        {
            Swap(swaps, AR.s.Npc, "_rumorCounters", sim.RumorCounters);
            Swap(swaps, AR.s.Npc, "_partyCounters", sim.PartyCounters);
            Swap(swaps, AR.s.Npc, "_theftCounters", sim.TheftCounters);
            AR.s.Npc.RumorExposureAudit = rumor =>
            {
                bool valid = AuditRumorValidity(sim, rumor, out _);
                if (valid == false)
                    sim.AuditExposedFailures++;
                return valid;
            };
            AR.s.Npc.RumorCreatedObserver = (holderId, rumor) => TrackRumorEvent(sim, holderId, rumor);
            AR.s.Npc.TheftDecidedObserver = (decision, gameTime) => CaptureTheftSnapshot(sim, decision, gameTime);
            AR.s.Npc.TheftDiscoveryObserver = (discovery, applied) =>
            {
                if (applied == false)
                    CaptureDiscoverySnapshot(sim, discovery);
                else
                    AuditDiscovery(sim, discovery);
            };
        }

        private static void DetachRumorProbes()
        {
            if (AR.s?.Npc == null)
                return;
            AR.s.Npc.RumorExposureAudit = null;
            AR.s.Npc.RumorCreatedObserver = null;
            AR.s.Npc.TheftDecidedObserver = null;
            AR.s.Npc.TheftDiscoveryObserver = null;
        }

        /// <summary>
        /// IsRumorValid와 다른 경로의 독립 판정: DungeonInfo는 복제 슬롯의 DungeonId 문자열 끝(":마을:슬롯:세대")과 세대를,
        /// ItemOwner는 복제 NPC의 생존·장비 인스턴스를 직접 본다. reason: 0 유효, 1 세대 전진·슬롯 없음, 2 소유자 사망, 3 장비 교체.
        /// </summary>
        private static bool AuditRumorValidity(SimContext sim, NpcRumor rumor, out int reason)
        {
            reason = 0;
            if (rumor.Kind == NpcRumorKind.DungeonInfo)
            {
                if (sim.DungeonVillageSlots.TryGetValue(rumor.VillageId, out VillageDungeonSlots? slots) == false)
                {
                    reason = 1;
                    return false;
                }
                string suffix = ":" + rumor.VillageId + ":" + rumor.SlotIndex + ":" + rumor.Generation;
                for (int i = 0; i < slots.Slots.Count; i++)
                {
                    DungeonSlot slot = slots.Slots[i];
                    if (slot != null && slot.SlotIndex == rumor.SlotIndex)
                    {
                        bool ok = slot.Generation == rumor.Generation && slot.DungeonId.EndsWith(suffix, StringComparison.Ordinal);
                        reason = ok ? 0 : 1;
                        return ok;
                    }
                }
                reason = 1;
                return false;
            }
            if (rumor.Kind == NpcRumorKind.ItemOwner)
            {
                if (sim.Npcs.TryGetValue(rumor.SubjectNpcId, out NpcSaveData? owner) == false ||
                    owner.Condition == CharacterConditions.Dead)
                {
                    reason = 2;
                    return false;
                }
                ItemData? item = rumor.EquipSlot >= 0 && rumor.EquipSlot < owner.InventoryEquip.Length
                    ? owner.InventoryEquip[rumor.EquipSlot]
                    : null;
                bool ok = item != null && item.ItemInstanceId == rumor.ItemInstanceId;
                reason = ok ? 0 : 3;
                return ok;
            }
            // P4b: 판매 = 판매자 생존 (D104), 배신 = 가해자·피해자 둘 다 생존 (D105) — 복제본 원장을 직접 본다
            if (rumor.Kind == NpcRumorKind.ItemSold)
            {
                bool ok = IsSimLiving(sim, rumor.SubjectNpcId);
                reason = ok ? 0 : 2;
                return ok;
            }
            if (rumor.Kind == NpcRumorKind.Betrayal)
            {
                bool ok = rumor.BetrayalKind != NpcBetrayalKind.None && IsSimLiving(sim, rumor.SubjectNpcId) &&
                          IsSimLiving(sim, rumor.VictimNpcId);
                reason = ok ? 0 : 2;
                return ok;
            }
            reason = 1;
            return false;
        }

        private static void TrackRumorEvent(SimContext sim, int holderId, NpcRumor rumor)
        {
            int villageId = rumor.Kind == NpcRumorKind.DungeonInfo
                ? rumor.VillageId
                : sim.Npcs.TryGetValue(holderId, out NpcSaveData? holder) ? holder.VillageId : -1;
            if (villageId < 0)
                return;
            string key = (int)rumor.Kind + ":" + rumor.VillageId + ":" + rumor.SlotIndex + ":" + rumor.Generation + ":" +
                         rumor.SubjectNpcId + ":" + rumor.EquipSlot + ":" + rumor.ItemInstanceId + ":" + rumor.VictimNpcId + ":" +
                         rumor.EventGameTime.ToString("R", CultureInfo.InvariantCulture);
            if (sim.RumorEventIndex.ContainsKey(key) == true)
                return;   // 파티원 여럿이 같은 사건의 직접 소문을 받는다 — 사건 1건으로 센다
            RumorEventTrack track = new RumorEventTrack
            {
                Template = rumor.Clone(),
                VillageId = villageId,
                Category = rumor.Kind switch
                {
                    NpcRumorKind.ItemOwner => 2,
                    NpcRumorKind.ItemSold => 3,
                    NpcRumorKind.Betrayal => 4,
                    _ => rumor.Cleared == true ? 1 : 0,
                },
            };
            if (rumor.Kind == NpcRumorKind.ItemSold)
                sim.SoldStolenInstances.Add(rumor.ItemInstanceId);
            sim.RumorEventIndex.Add(key, track);
            sim.RumorEvents.Add(track);
            if (track.Category == 0)
            {
                sim.DelveEvents++;
                if (rumor.Value >= NpcManager.PromisingRumorValueMin)
                    sim.DelveEventsAboveValueMin++;
            }
        }

        /// <summary>사건 뒤 12h/1일/2일/3일에 그 마을 생존 NPC 중 그 사건을 아는 비율 (전달 횟수 무관).</summary>
        private static void UpdateRumorPropagation(SimContext sim, float now)
        {
            for (int e = 0; e < sim.RumorEvents.Count; e++)
            {
                RumorEventTrack track = sim.RumorEvents[e];
                while (track.NextCheckpoint < PropagationCheckpointHours.Length &&
                       now - track.Template.EventGameTime >= PropagationCheckpointHours[track.NextCheckpoint] - 0.001f)
                {
                    if (sim.Villages.TryGetValue(track.VillageId, out VillageData? village) == true)
                    {
                        int living = 0, knowing = 0;
                        for (int i = 0; i < village.NpcEntityIds.Count; i++)
                        {
                            if (sim.Npcs.TryGetValue(village.NpcEntityIds[i], out NpcSaveData? npc) == false ||
                                npc.Condition == CharacterConditions.Dead)
                                continue;
                            living++;
                            for (int r = 0; r < npc.Rumors.Count; r++)
                            {
                                NpcRumor held = npc.Rumors[r];
                                if (NpcManager.IsSameRumorTarget(held, track.Template) == true &&
                                    held.EventGameTime == track.Template.EventGameTime)
                                {
                                    knowing++;
                                    break;
                                }
                            }
                        }
                        if (living > 0)
                        {
                            double ratio = knowing / (double)living;
                            int c = track.Category, k = track.NextCheckpoint;
                            sim.PropagationSum[c, k] += ratio;
                            sim.PropagationSamples[c, k]++;
                            if (ratio > sim.PropagationMax[c, k])
                                sim.PropagationMax[c, k] = ratio;
                        }
                    }
                    track.NextCheckpoint++;
                }
            }
        }

        private static void RecordRumorDay(SimContext sim, DayRow row, float gameTime)
        {
            NpcRumorCounters counters = sim.RumorCounters;
            row.RumorExchangeAttempts = counters.ExchangeAttempts - sim.RumorAttemptsAtDayStart;
            row.RumorTransfers = counters.ExchangesWithTransfer - sim.RumorTransfersAtDayStart;
            row.ConfirmationDelves = (int)(counters.ConfirmationDelves - sim.ConfirmationsAtDayStart);
            sim.RumorAttemptsAtDayStart = counters.ExchangeAttempts;
            sim.RumorTransfersAtDayStart = counters.ExchangesWithTransfer;
            sim.ConfirmationsAtDayStart = counters.ConfirmationDelves;

            int[] reasons = new int[4];
            for (int i = 0; i < sim.NpcIds.Count; i++)
            {
                NpcSaveData npc = sim.Npcs[sim.NpcIds[i]];
                if (npc.Condition == CharacterConditions.Dead)
                    continue;
                if (npc.Rumors.Count > 0)
                    row.RumorHolders++;
                row.RumorsHeld += npc.Rumors.Count;
                for (int r = 0; r < npc.Rumors.Count; r++)
                {
                    if (AuditRumorValidity(sim, npc.Rumors[r], out int reason) == false)
                    {
                        row.StoredInvalid++;
                        reasons[reason]++;
                    }
                }
                if (AR.s.Npc.TryGetPromisingRumor(npc, gameTime, out _) == true)
                    row.PromisingHolders++;
            }
            if (row.StoredInvalid > sim.StoredInvalidMax)
                sim.StoredInvalidMax = row.StoredInvalid;
            for (int k = 0; k < reasons.Length; k++)
            {
                sim.StoredInvalidByReasonLast[k] = reasons[k];
                if (reasons[k] > sim.StoredInvalidByReasonMax[k])
                    sim.StoredInvalidByReasonMax[k] = reasons[k];
            }
        }

        /// <summary>NPC 세이브 JSON 크기(세이브와 같은 JsonSettings). rumorBytes = 소문 각각의 JSON 합.</summary>
        private static long MeasureNpcJsonBytes(SimContext sim, out long rumorBytes, out int rumorCount)
        {
            long total = 0;
            rumorBytes = 0;
            rumorCount = 0;
            for (int i = 0; i < sim.NpcIds.Count; i++)
            {
                NpcSaveData npc = sim.Npcs[sim.NpcIds[i]];
                total += Encoding.UTF8.GetByteCount(JsonConvert.SerializeObject(npc, Formatting.None, JsonSettings.Default));
                for (int r = 0; r < npc.Rumors.Count; r++)
                {
                    rumorBytes += Encoding.UTF8.GetByteCount(JsonConvert.SerializeObject(npc.Rumors[r], Formatting.None,
                        JsonSettings.Default)) + 1;   // + 배열 구분 쉼표
                    rumorCount++;
                }
            }
            return total;
        }

        /// <summary>GOLD_BALANCE 요약: 물가 지수 곡선·등급 배율·안정 확인(G2), 새 흐름(생활비·금고 유지비) 합계.</summary>
        private static void AppendGoldBalanceSummary(StringBuilder sb, SimContext sim, List<DayRow> rows)
        {
            if (rows.Count == 0)
                return;
            DayRow first = rows[0];
            DayRow last = rows[rows.Count - 1];
            long livingCount = 0, deposit = 0, burn = 0, vault = 0;
            for (int i = 1; i < rows.Count; i++)
            {
                livingCount += rows[i].LivingSpendCount;
                deposit += rows[i].LivingSpendDeposit;
                burn += rows[i].LivingSpendBurn;
                vault += rows[i].VaultUpkeepBurn;
            }
            float index = last.GoldPriceIndex;
            sb.AppendLine("---- 골드 밸런스 (GOLD_BALANCE) ----");
            sb.AppendLine($"물가 지수: {(GoldPriceRules.IndexEnabled ? "가격에 적용 중" : "측정만 (가격 배율 1 — 지수 꺼짐)")} | " +
                          $"기준 세계 Gold {sim.GoldPrice.BaselineWorldGold} | 통화량 비율 {first.GoldPriceTarget:F3} → {last.GoldPriceTarget:F3} | " +
                          $"지수(지연 {GoldPriceRules.IndexLagDays:F0}일) {first.GoldPriceIndex:F3} → {index:F3}");
            sb.AppendLine($"  끝 지수의 등급 배율: 흔함 ×{GoldPriceRules.GetMultiplier(index, GoldPriceClass.Common):F3} / " +
                          $"중간 ×{GoldPriceRules.GetMultiplier(index, GoldPriceClass.Mid):F3} / " +
                          $"희귀 ×{GoldPriceRules.GetMultiplier(index, GoldPriceClass.Rare):F3} / " +
                          $"서비스 ×{GoldPriceRules.GetMultiplier(index, GoldPriceClass.Service):F3}");
            int[] checkpoints = { 7, 30, 60, 120, 180, 240, 300, 360, 390, 420, 450, 600, 840 };
            sb.Append("  지수 곡선 (일: 지수 / 통화량 비율):");
            for (int i = 0; i < checkpoints.Length; i++)
            {
                int day = checkpoints[i];
                if (day < rows.Count)
                    sb.Append($" {day}일 {rows[day].GoldPriceIndex:F3}/{rows[day].GoldPriceTarget:F3}");
            }
            sb.AppendLine();
            if (rows.Count > 450 && rows[420].GoldPriceIndex > 0f)
            {
                float change = (rows[450].GoldPriceIndex / rows[420].GoldPriceIndex - 1f) * 100f;
                sb.AppendLine($"  안정 확인 (G2): 420일 지수 {rows[420].GoldPriceIndex:F3}, 420→450일 변화 {change:+0.00;-0.00}%");
            }
            sb.AppendLine($"  생활비: {livingCount}회, 마을 금고 입금 {deposit}, 소멸 {burn} | 금고 유지비 소멸 {vault} | " +
                          $"사망 소멸 {sim.TotalGoldBurned}");
        }

        private static void AppendRumorSummary(StringBuilder sb, SimContext sim, List<DayRow> rows, int days)
        {
            NpcRumorCounters c = sim.RumorCounters;
            sb.AppendLine("---- 소문 (P2b) ----");
            sb.AppendLine($"생성(직접): 원정 성공 {c.CreatedDelve} / 완료 새 세대 {c.CreatedCleared} / 장비 소유 {c.CreatedItemOwner} / " +
                          $"판매(훔친 물건) {c.CreatedItemSold} / 배신 {c.CreatedBetrayal[(int)NpcBetrayalKind.TheftCaughtInAct]}(현장 적발)+" +
                          $"{c.CreatedBetrayal[(int)NpcBetrayalKind.TheftDiscovered]}(늦은 발각) | 추적 사건 {sim.RumorEvents.Count}건 | " +
                          $"교환 수용 중 판매 {c.AcceptedItemSold} / 배신 {c.AcceptedBetrayal}");
            sb.AppendLine($"원정 성공 사건 {sim.DelveEvents}건 중 가치 ≥ {NpcManager.PromisingRumorValueMin}(유망 기준): " +
                          $"{sim.DelveEventsAboveValueMin}건 ({Pct(sim.DelveEventsAboveValueMin, sim.DelveEvents):F1}%)");
            sb.AppendLine($"교환: Rest 종료 {c.RestEnds}회, 교환 롤 통과 {c.ExchangeRollsPassed}회 ({Pct(c.ExchangeRollsPassed, c.RestEnds):F1}%), " +
                          $"상대 없음 {c.NoPeer}회");
            sb.AppendLine($"  교환 시도(상대 1명 단위) {c.ExchangeAttempts}건 = 하루 {c.ExchangeAttempts / (float)Mathf.Max(1, days):F0}건 | " +
                          $"소문이 오간 시도 {c.ExchangesWithTransfer}건 = 하루 {c.ExchangesWithTransfer / (float)Mathf.Max(1, days):F0}건 | " +
                          $"줄 것 없음 {c.NothingToOffer}건");
            sb.AppendLine($"  제안 {c.Offers} / 수용 {c.Accepted} / 호기심 거절 {c.RejectedByCuriosity} / 중복으로 건너뜀 {c.DuplicateSkips} / " +
                          $"상한 교체 {c.CapEvictions} / 상한 버림(새 소문) {c.CapDrops} / 보관 중복 {c.AddDuplicate}");
            long received = c.ReceivedByHops[0] + c.ReceivedByHops[1] + c.ReceivedByHops[2] + c.ReceivedByHops[3];
            sb.AppendLine($"  교환 수신 Hops: 1={Pct(c.ReceivedByHops[0], received):F1}% 2={Pct(c.ReceivedByHops[1], received):F1}% " +
                          $"3={Pct(c.ReceivedByHops[2], received):F1}% 4+={Pct(c.ReceivedByHops[3], received):F1}% (n={received})");

            // 보유 분포 (마지막 날)
            int living = 0;
            long held = 0, dungeonHeld = 0, itemHeld = 0, soldHeld = 0, betrayalHeld = 0;
            int[] histogram = new int[NpcManager.MaxRumorsPerNpc + 1];
            long[] hops = new long[4];
            for (int i = 0; i < sim.NpcIds.Count; i++)
            {
                NpcSaveData npc = sim.Npcs[sim.NpcIds[i]];
                if (npc.Condition == CharacterConditions.Dead)
                    continue;
                living++;
                held += npc.Rumors.Count;
                histogram[Mathf.Clamp(npc.Rumors.Count, 0, NpcManager.MaxRumorsPerNpc)]++;
                for (int r = 0; r < npc.Rumors.Count; r++)
                {
                    NpcRumor rumor = npc.Rumors[r];
                    if (rumor.Kind == NpcRumorKind.DungeonInfo)
                        dungeonHeld++;
                    else if (rumor.Kind == NpcRumorKind.ItemSold)
                        soldHeld++;
                    else if (rumor.Kind == NpcRumorKind.Betrayal)
                        betrayalHeld++;
                    else
                        itemHeld++;
                    hops[Mathf.Clamp(rumor.Hops, 0, 3)]++;
                }
            }
            sb.Append($"보유(마지막 날): NPC당 평균 {(living > 0 ? held / (float)living : 0f):F2}개 (던전 {dungeonHeld} / 장비 {itemHeld} / " +
                      $"판매 {soldHeld} / 배신 {betrayalHeld}) | 분포 0~8:");
            for (int k = 0; k < histogram.Length; k++)
                sb.Append(' ').Append(Pct(histogram[k], living).ToString("F1", CultureInfo.InvariantCulture)).Append('%');
            sb.AppendLine();
            sb.AppendLine($"  보유 Hops: 0={Pct(hops[0], held):F1}% 1={Pct(hops[1], held):F1}% 2={Pct(hops[2], held):F1}% 3+={Pct(hops[3], held):F1}%");

            for (int cat = 0; cat < RumorCategoryCount; cat++)
            {
                sb.Append($"전파 곡선 [{RumorCategoryNames[cat]}] (마을 생존 NPC 중 아는 비율 평균/최대):");
                for (int k = 0; k < PropagationCheckpointHours.Length; k++)
                {
                    int n = sim.PropagationSamples[cat, k];
                    double avg = n > 0 ? sim.PropagationSum[cat, k] / n : 0d;
                    sb.Append($" {PropagationCheckpointHours[k]:F0}h {avg * 100d:F1}%/{sim.PropagationMax[cat, k] * 100d:F0}% (n={n})");
                }
                sb.AppendLine();
            }

            int maxStoredInvalid = sim.StoredInvalidMax;
            sb.AppendLine($"노출 시점 무효(독립 감사): {c.ExposedInvalid} (노출·사용 {c.Exposed}건) — 0이어야 함");
            sb.AppendLine($"보관 중 무효(일말 감사): 최대 {maxStoredInvalid} / 마지막 날 {rows[rows.Count - 1].StoredInvalid} " +
                          $"(원인 최대: 세대 전진 {sim.StoredInvalidByReasonMax[1]}, 소유자 사망 {sim.StoredInvalidByReasonMax[2]}, " +
                          $"장비 교체 {sim.StoredInvalidByReasonMax[3]}) | 사용 전 검증으로 걸러짐 {c.FilteredInvalidAtUse}, 정리 삭제 {c.PrunedInvalid}");

            long promisingSum = 0, livingSum = 0;
            for (int i = 1; i < rows.Count; i++)
            {
                promisingSum += rows[i].PromisingHolders;
                livingSum += rows[i].Living;
            }
            float holderDelvePct = Pct(sim.HolderDelve, sim.HolderSelections);
            float nonHolderDelvePct = Pct(sim.NonHolderDelve, sim.NonHolderSelections);
            sb.AppendLine($"유망 소문 보유율(일말 평균): {Pct(promisingSum, livingSum):F1}% | 선택 시점 보유 비율 " +
                          $"{Pct(sim.HolderSelections, sim.HolderSelections + sim.NonHolderSelections):F1}%");
            sb.AppendLine($"Delve 비중: 보유자 {holderDelvePct:F2}% (n={sim.HolderSelections}) / 비보유자 {nonHolderDelvePct:F2}% " +
                          $"(n={sim.NonHolderSelections}) → {(nonHolderDelvePct > 0f ? holderDelvePct / nonHolderDelvePct : 0f):F2}배");
            long delveSelections = sim.HolderDelve + sim.NonHolderDelve;
            sb.AppendLine($"확인 원정: {c.ConfirmationDelves}회 (Delve 선택 {delveSelections}회 중 {Pct(c.ConfirmationDelves, delveSelections):F1}%) | 판정 {sim.ConfirmationJudgments}건 " +
                          $"성공률 {Pct(sim.ConfirmationSuccess, sim.ConfirmationJudgments):F1}%");

            long bytesEnd = MeasureNpcJsonBytes(sim, out long rumorBytes, out int rumorCount);
            sb.AppendLine($"NPC 세이브 JSON(복제본, 세이브와 같은 설정): 시작 {sim.NpcJsonBytesStart / 1024f:F0}KB → 끝 {bytesEnd / 1024f:F0}KB " +
                          $"({(sim.NpcJsonBytesStart > 0 ? (bytesEnd / (double)sim.NpcJsonBytesStart - 1d) * 100d : 0d):+0.0;-0.0}%, 전리품·Deed 증가 포함) | " +
                          $"소문 {rumorCount}개 = {rumorBytes / 1024f:F0}KB, 1개 평균 {(rumorCount > 0 ? rumorBytes / (float)rumorCount : 0f):F0}B");
        }

        /// <summary>
        /// 소문 규칙 검증 (P2b, D70~D79). 복제본(NPC·마을·던전)에서 생성·교환·무효 정리·상한 교체·재수신 차단·플레이어 완료 무효를 본다.
        /// 라이브 상태는 바꾸지 않으며 실행 전후 해시를 비교한다.
        /// </summary>
        public static string VerifyRumorRules()
        {
            if (EditorApplication.isPlaying == false || AR.s == null || AR.s.Npc == null || AR.s.Dungeon == null ||
                AR.s.Dungeon.IsInitialized == false)
            {
                const string notReady = "[RumorRules] 플레이 모드에서 GameScene이 로드된 뒤 실행하세요.";
                Debug.LogError(notReady);
                return notReady;
            }

            string hashBefore = ComputeLiveStateHash();
            UnityEngine.Random.State randomState = UnityEngine.Random.state;
            FieldInfo itemCounterField = GetField(AR.s.Item, "_instanceIdCounter");
            object? itemCounter = itemCounterField.GetValue(AR.s.Item);
            List<FieldSwap> swaps = new List<FieldSwap>();
            StringBuilder log = new StringBuilder();
            int failures = 0;
            string? error = null;

            void Check(bool condition, string label)
            {
                if (condition == false)
                    failures++;
                log.AppendLine((condition ? "  PASS " : "  FAIL ") + label);
            }

            SimContext sim = new SimContext();
            try
            {
                BuildClones(sim);
                sim.WorldSeed = AR.s.Map.mapSeed;
                SwapInClones(sim, swaps);
                AttachRumorProbes(sim, swaps);
                NpcManager npc = AR.s.Npc;
                DungeonManager dm = AR.s.Dungeon;
                float t0 = AR.s.Time.CurrentGameTime;

                // 깨끗한(NPC 완료·플레이어 기록 없음) 2슬롯 이상 마을 중 생존 NPC 4명 이상인 곳
                VillageDungeonSlots? slots = null;
                List<int> villagers = new List<int>();
                for (int v = 0; v < sim.Dungeons.Villages.Count && slots == null; v++)
                {
                    VillageDungeonSlots candidate = sim.Dungeons.Villages[v];
                    if (candidate.Slots.Count < 2 || candidate.LastNpcCompletionGameTime >= 0f ||
                        sim.Villages.TryGetValue(candidate.VillageId, out VillageData? village) == false)
                        continue;
                    bool clean = true;
                    for (int s = 0; s < candidate.Slots.Count; s++)
                    {
                        if (DungeonLayout.IsNpcReplaced(candidate.Slots[s]) == true)
                            clean = false;
                        for (int r = 0; r < sim.Dungeons.Dungeons.Count; r++)
                        {
                            if (sim.Dungeons.Dungeons[r].DungeonId == candidate.Slots[s].DungeonId)
                                clean = false;
                        }
                    }
                    villagers.Clear();
                    for (int i = 0; i < village.NpcEntityIds.Count; i++)
                    {
                        if (sim.Npcs.TryGetValue(village.NpcEntityIds[i], out NpcSaveData? n) == true &&
                            n.Condition != CharacterConditions.Dead)
                            villagers.Add(village.NpcEntityIds[i]);
                    }
                    if (clean == true && villagers.Count >= 4)
                        slots = candidate;
                }
                if (slots == null)
                    throw new InvalidOperationException("조건에 맞는 마을(2슬롯 이상, 깨끗함, 생존 NPC 4명 이상)을 찾지 못했습니다.");

                int villageId = slots.VillageId;
                int idA = villagers[0], idB = villagers[1], idC = villagers[2], idD = villagers[3];
                NpcSaveData a = sim.Npcs[idA], b = sim.Npcs[idB], c = sim.Npcs[idC], d = sim.Npcs[idD];
                a.Rumors.Clear(); b.Rumors.Clear(); c.Rumors.Clear(); d.Rumors.Clear();
                DungeonSlot slot0 = slots.Slots[0];
                DungeonSlot slot1 = slots.Slots[1];
                log.AppendLine($"  마을 {villageId} (슬롯 {slots.Slots.Count}), NPC A={idA} B={idB} C={idC} D={idD}");

                // ① 생성
                npc.AddFirsthandDelveRumor(idA, a, villageId, slot0.SlotIndex, slot0.Generation, idA, 50, t0);
                Check(a.Rumors.Count == 1 && npc.IsRumorValid(a.Rumors[0]) == true && a.Rumors[0].Hops == 0 &&
                      a.Rumors[0].SourceNpcId == -1 && npc.IsPromisingRumor(a, a.Rumors[0], t0) == false,
                    "생성: 직접 소문 1개, 유효, Hops 0, 직접 겪은 소문은 유망 아님");

                // ② 교환 → 들은 소문은 유망, 확인 원정 대상
                NpcRumor? give = npc.PickRumorToGive(a, b, t0 + 1f);
                bool received = give != null && npc.ReceiveRumor(b, give, idA, t0 + 1f) == true;
                Check(received == true && b.Rumors.Count == 1 && b.Rumors[0].SourceNpcId == idA && b.Rumors[0].Hops == 1,
                    "교환: B가 A에게 들음 (Source=A, Hops 1)");
                Check(b.Rumors.Count == 1 && npc.IsPromisingRumor(b, b.Rumors[0], t0 + 1f) == true,
                    "들은 원정 성공 보고(가치 50 ≥ 기준)는 유망");
                bool prepared = npc.TryPrepareDelveTarget(idB, idB, villageId, b.Position, t0 + 1f, sim.WorldSeed,
                    out string targetId, out _, out bool fromRumor);
                Check(prepared == true && fromRumor == true && targetId == slot0.DungeonId,
                    $"확인 원정: 소문 대상 슬롯으로 감 (target={targetId})");
                NpcActivityChoiceContext context = npc.BuildActivityChoiceContext(idB, b, t0 + 1f);
                Check(context.DelveAvailable == false || context.HasPromisingRumor == true,
                    "활동 선택 문맥에 유망 소문 필요가 켜짐");
                Check(npc.PickRumorToGive(a, b, t0 + 2f) == null, "같은 사건은 다시 주지 않음 (중복)");

                // ③ Checked 재수신 차단
                npc.MarkDelveRumorsChecked(b, villageId, slot0.SlotIndex);
                Check(b.Rumors[0].Checked == true && npc.IsPromisingRumor(b, b.Rumors[0], t0 + 2f) == false,
                    "확인 원정 뒤 Checked → 유망 아님");
                bool reReceived = npc.ReceiveRumor(b, a.Rumors[0], idA, t0 + 2f);
                Check(reReceived == false && b.Rumors.Count == 1 && b.Rumors[0].Checked == true,
                    "같은 사건을 다시 받아도 Checked가 되살아나지 않음");
                npc.AddFirsthandDelveRumor(idA, a, villageId, slot0.SlotIndex, slot0.Generation, idA, 60, t0 + 5f);
                NpcRumor? newer = npc.PickRumorToGive(a, b, t0 + 6f);
                bool newerReceived = newer != null && npc.ReceiveRumor(b, newer, idA, t0 + 6f) == true;
                Check(a.Rumors.Count == 1 && newerReceived == true && b.Rumors.Count == 1 && b.Rumors[0].Checked == false &&
                      Mathf.Approximately(b.Rumors[0].EventGameTime, t0 + 5f),
                    "대상별 1개: 더 새 사건은 같은 자리를 교체하고 Checked 해제");

                // ④ 대상별 1개 — 오래된 사건은 거부
                NpcRumor older = b.Rumors[0].Clone();
                older.EventGameTime = t0 - 10f;
                Check(npc.AddRumor(b, older, t0 + 6f) == NpcRumorAddResult.Duplicate && b.Rumors.Count == 1,
                    "대상별 1개: 같은 대상의 오래된 사건은 Duplicate");

                // ⑤ NPC 완료 → 옛 세대 소문 무효·노출 안 됨 → 정리 + 완료 소문(새 세대)
                NpcRumor? toC = npc.PickRumorToGive(b, c, t0 + 7f);
                if (toC != null)
                    npc.ReceiveRumor(c, toC, idB, t0 + 7f);
                NpcDungeonCompletionResult completion = dm.TryCompleteByNpc(villageId, slot0.DungeonId, t0 + 10f);
                Check(completion == NpcDungeonCompletionResult.Completed, $"NPC 완료 (결과 {completion})");
                Check(b.Rumors.Count == 1 && npc.IsRumorValid(b.Rumors[0]) == false && npc.PickRumorToGive(b, d, t0 + 10f) == null &&
                      npc.IsPromisingRumor(b, b.Rumors[0], t0 + 10f) == false,
                    "정리 전에도 옛 세대 소문은 무효 → 교환·유망에 노출 안 됨");
                NpcDelveReport report = new NpcDelveReport { VillageId = villageId, DungeonId = slot0.DungeonId };
                report.MemberIds.Add(idA);
                report.Outcomes.Add(NpcHuntOutcome.Success);
                npc.OnNpcDungeonCompleted(report, slot0.SlotIndex, idA, new[] { 30 }, t0 + 10f);
                bool bHasSlot0 = false, cHasSlot0 = false;
                for (int i = 0; i < b.Rumors.Count; i++)
                    bHasSlot0 |= b.Rumors[i].Kind == NpcRumorKind.DungeonInfo && b.Rumors[i].SlotIndex == slot0.SlotIndex;
                for (int i = 0; i < c.Rumors.Count; i++)
                    cHasSlot0 |= c.Rumors[i].Kind == NpcRumorKind.DungeonInfo && c.Rumors[i].SlotIndex == slot0.SlotIndex;
                Check(bHasSlot0 == false && cHasSlot0 == false, "NPC 완료 → 마을 명단의 옛 세대 소문 즉시 정리 (B·C)");
                Check(a.Rumors.Count == 1 && a.Rumors[0].Cleared == true && a.Rumors[0].Generation == slots.Slots[0].Generation &&
                      npc.IsRumorValid(a.Rumors[0]) == true && npc.IsPromisingRumor(a, a.Rumors[0], t0 + 10f) == false,
                    $"완료 소문: 새 세대({a.Rumors[0].Generation})를 가리키고 유효, 유망 아님");

                // ⑥ 장비 교체 → 제거
                int equipSlot = -1;
                for (int s = 0; s < d.InventoryEquip.Length && equipSlot < 0; s++)
                {
                    if (d.InventoryEquip[s] != null && d.InventoryEquip[s]!.ItemInstanceId != 0)
                        equipSlot = s;
                }
                if (equipSlot < 0)
                    throw new InvalidOperationException($"NPC {idD}에 장착 장비가 없습니다.");
                ItemData oldItem = d.InventoryEquip[equipSlot]!;
                npc.NotifyNpcEquipmentChanged(idD, d, (GlobalEnum.EquipSlotType)equipSlot, oldItem, t0 + 11f);
                NpcRumor? itemRumor = npc.PickRumorToGive(d, b, t0 + 11f);
                bool bGotItem = itemRumor != null && npc.ReceiveRumor(b, itemRumor, idD, t0 + 11f) == true;
                Check(bGotItem == true && itemRumor!.Kind == NpcRumorKind.ItemOwner, "장비 소유 소문 생성·전달");
                ItemData newItem = CloneJson(oldItem);
                newItem.OnLoadCompleted();
                newItem.ItemInstanceId = oldItem.ItemInstanceId + 50000000;
                d.EquipItem((GlobalEnum.EquipSlotType)equipSlot, newItem, out _);
                npc.NotifyNpcEquipmentChanged(idD, d, (GlobalEnum.EquipSlotType)equipSlot, newItem, t0 + 12f);
                bool bHasOldItem = false;
                for (int i = 0; i < b.Rumors.Count; i++)
                    bHasOldItem |= b.Rumors[i].Kind == NpcRumorKind.ItemOwner && b.Rumors[i].ItemInstanceId == oldItem.ItemInstanceId;
                int dItemRumors = 0;
                for (int i = 0; i < d.Rumors.Count; i++)
                {
                    if (d.Rumors[i].Kind == NpcRumorKind.ItemOwner && d.Rumors[i].EquipSlot == equipSlot &&
                        d.Rumors[i].ItemInstanceId == newItem.ItemInstanceId && npc.IsRumorValid(d.Rumors[i]) == true)
                        dItemRumors++;
                }
                Check(bHasOldItem == false && dItemRumors == 1, "장비 교체 → 옛 장비 소문 제거, 소유자에 새 장비 소문 1개");

                // ⑦ 소유자 사망 → 제거
                NpcRumor? itemRumor2 = npc.PickRumorToGive(d, b, t0 + 13f);
                if (itemRumor2 != null)
                    npc.ReceiveRumor(b, itemRumor2, idD, t0 + 13f);
                d.Condition = CharacterConditions.Dead;
                d.Activity.Phase = NpcActivityPhase.Dead;
                npc.PurgeRumorsOnNpcDeath(idD, d);
                bool bHasD = false;
                for (int i = 0; i < b.Rumors.Count; i++)
                    bHasD |= b.Rumors[i].Kind == NpcRumorKind.ItemOwner && b.Rumors[i].SubjectNpcId == idD;
                Check(itemRumor2 != null && bHasD == false && d.Rumors.Count == 0,
                    "소유자 사망 → 그 소유자의 장비 소문 제거, 사망자 본인 소문 비움");

                // ⑧ 상한 8 교체 순서 (유효한 장비 소유 대상 수집 — 사망자 D 제외, 마을 무관)
                List<NpcRumor> pool = new List<NpcRumor>();
                for (int i = 0; i < sim.NpcIds.Count && pool.Count < 12; i++)
                {
                    int ownerId = sim.NpcIds[i];
                    NpcSaveData owner = sim.Npcs[ownerId];
                    if (ownerId == idD || owner.Condition == CharacterConditions.Dead)
                        continue;
                    for (int s = 0; s < owner.InventoryEquip.Length && pool.Count < 12; s++)
                    {
                        ItemData? item = owner.InventoryEquip[s];
                        if (item == null || item.ItemInstanceId == 0)
                            continue;
                        pool.Add(new NpcRumor { Kind = NpcRumorKind.ItemOwner, SubjectNpcId = ownerId, EquipSlot = s,
                            ItemInstanceId = item.ItemInstanceId, SourceNpcId = idA, Hops = 1 });
                    }
                }
                if (pool.Count < 11)
                    throw new InvalidOperationException("상한 검증에 쓸 장비가 부족합니다.");
                c.Rumors.Clear();
                for (int i = 0; i < 8; i++)
                {
                    NpcRumor r = pool[i].Clone();
                    r.EventGameTime = t0 + 100f + i;
                    r.Value = 10 * (i + 1);
                    if (i == 5)
                        r.ItemInstanceId = -777;   // 무효
                    c.Rumors.Add(r);
                }
                NpcRumor ninth = pool[8].Clone();
                ninth.EventGameTime = t0 + 90f;   // 가장 오래됐지만 무효가 먼저 버려져야 한다
                NpcRumorAddResult add9 = npc.AddRumor(c, ninth, t0 + 200f);
                bool invalidGone = true;
                for (int i = 0; i < c.Rumors.Count; i++)
                    invalidGone &= c.Rumors[i].ItemInstanceId != -777;
                Check(add9 == NpcRumorAddResult.AddedWithEviction && c.Rumors.Count == 8 && invalidGone == true,
                    "상한 8: 무효부터 버림");
                NpcRumor tenth = pool[9].Clone();
                tenth.EventGameTime = t0 + 150f;
                NpcRumorAddResult add10 = npc.AddRumor(c, tenth, t0 + 200f);
                bool oldestGone = true;
                for (int i = 0; i < c.Rumors.Count; i++)
                    oldestGone &= Mathf.Approximately(c.Rumors[i].EventGameTime, t0 + 90f) == false;
                Check(add10 == NpcRumorAddResult.AddedWithEviction && c.Rumors.Count == 8 && oldestGone == true,
                    "상한 8: 다음은 유망 아닌 것 중 가장 오래된 것");
                // 유망 보호 + 같은 시각이면 가치 낮은 것
                c.Rumors.Clear();
                NpcRumor promising = new NpcRumor { Kind = NpcRumorKind.DungeonInfo, VillageId = villageId,
                    SlotIndex = slot1.SlotIndex, Generation = slot1.Generation, SubjectNpcId = idA, Value = 50,
                    EventGameTime = t0 + 150f, SourceNpcId = idA, Hops = 1 };
                c.Rumors.Add(promising);
                for (int i = 0; i < 7; i++)
                {
                    NpcRumor r = pool[i].Clone();
                    r.EventGameTime = t0 + 180f;
                    r.Value = 10 * (i + 1);
                    c.Rumors.Add(r);
                }
                bool promisingNow = npc.IsPromisingRumor(c, promising, t0 + 190f);
                NpcRumor lowValue = pool[7].Clone();
                lowValue.EventGameTime = t0 + 180f;
                lowValue.Value = 5;
                NpcRumorAddResult addLow = npc.AddRumor(c, lowValue, t0 + 190f);
                NpcRumor highValue = pool[8].Clone();
                highValue.EventGameTime = t0 + 180f;
                highValue.Value = 100;
                NpcRumorAddResult addHigh = npc.AddRumor(c, highValue, t0 + 190f);
                bool promisingKept = c.Rumors.Contains(promising);
                bool lowestGone = true;
                for (int i = 0; i < c.Rumors.Count; i++)
                    lowestGone &= c.Rumors[i].Value != 10;
                Check(promisingNow == true && addLow == NpcRumorAddResult.DroppedByCap && addHigh == NpcRumorAddResult.AddedWithEviction &&
                      promisingKept == true && lowestGone == true && c.Rumors.Count == 8,
                    "상한 8: 유망은 가장 오래돼도 지킴, 같은 시각이면 가치 낮은 것(새 소문 포함)을 버림");

                // ⑨ 플레이어 완료로 무효된 소문은 노출되지 않음 (플레이어 경로에는 훅 없음 — 노출 검증이 막는다)
                b.Rumors.Clear();
                NpcRumor slot1Rumor = promising.Clone();
                slot1Rumor.EventGameTime = t0 + 300f;
                npc.AddRumor(b, slot1Rumor, t0 + 300f);
                bool validBefore = npc.IsRumorValid(b.Rumors[0]);
                int listIndex1 = slots.Slots.IndexOf(slot1);
                bool originalHasEntrance = slot1.HasEntrance;
                if (slot1.HasEntrance == false)
                {
                    slot1.HasEntrance = true;
                    slot1.EntranceX = 12345;
                    slot1.EntranceY = 6789;
                }
                DungeonRecord record = new DungeonRecord
                {
                    DungeonId = slot1.DungeonId, VillageId = villageId, SlotIndex = slot1.SlotIndex,
                    Generation = slot1.Generation, DungeonTableId = slot1.DungeonTableId, CompletionGold = 100,
                    ReplacementCooldownHours = 12f,
                };
                record.Objects.Add(new DungeonObject { Id = 1, Kind = DungeonObjectKind.Boss });
                record.KilledObjects.Add(1);
                sim.Dungeons.Dungeons.Add(record);
                PlayerData player = CloneJson(AR.s.Data.Player);
                bool staged = DungeonCompletionTransaction.TryStage(sim.Dungeons, player, record, AR.s.Data.WorldSeed,
                    t0 + 301f, 12f, out DungeonCompletionTransaction transaction);
                bool invalidAfter = npc.IsRumorValid(b.Rumors[0]) == false;
                bool notGiven = npc.PickRumorToGive(b, a, t0 + 301f) == null;
                bool notPromising = npc.IsPromisingRumor(b, b.Rumors[0], t0 + 301f) == false;
                npc.TryPrepareDelveTarget(idB, idB, villageId, b.Position, t0 + 301f, sim.WorldSeed, out _, out _,
                    out bool fromRumorAfter);
                transaction?.Dispose();
                sim.Dungeons.Dungeons.Remove(record);
                slots.Slots[listIndex1].HasEntrance = originalHasEntrance;
                Check(validBefore == true && staged == true && invalidAfter == true && notGiven == true && notPromising == true &&
                      fromRumorAfter == false,
                    "플레이어 완료(스테이징) → 소문 무효, 교환·유망·확인 원정에 노출 안 됨");

                Check(sim.RumorCounters.ExposedInvalid == 0, $"노출 시점 독립 감사 무효 0 (노출 {sim.RumorCounters.Exposed}건)");
            }
            catch (Exception exception)
            {
                error = exception.ToString();
            }
            finally
            {
                DetachRumorProbes();
                for (int i = swaps.Count - 1; i >= 0; i--)
                    swaps[i].Field.SetValue(swaps[i].Target, swaps[i].Original);
                itemCounterField.SetValue(AR.s.Item, itemCounter);
                UnityEngine.Random.state = randomState;
            }

            string hashAfter = ComputeLiveStateHash();
            bool liveUnchanged = hashBefore == hashAfter;
            string result = $"[RumorRules] 실패 {failures}건, 라이브 상태 해시 전후 동일: {liveUnchanged}\n" + log +
                            (error != null ? "  ERROR " + error + "\n" : string.Empty);
            if (failures == 0 && liveUnchanged == true && error == null)
                Debug.Log(result);
            else
                Debug.LogError(result);
            return result;
        }

        // ================= 절도 강제 검증 (P4a) =================

        /// <summary>
        /// 절도 규칙 강제 검증 (P4a, D99~D102·D106). 현재 상태를 복제해 그 안에서만 실행하고 전후 라이브 해시가 같아야 한다.
        /// 게임 판정 코드를 복제하지 않는다 — 조건(GetTheftThiefBlock·IsTheftVictimCandidate·CollectTheftVictims)·결정(DecideTheft)·
        /// 적용(ApplyTheft)·Rest 종료 진입점(ResolveRestEnd)을 그대로 부르고, RNG 결과(발각 여부·피해자·물건)만 결정 값을 직접 만들어 강제한다.
        /// </summary>
        public static string VerifyTheftRules()
        {
            if (EditorApplication.isPlaying == false || AR.s == null || AR.s.Npc == null || AR.s.Village == null ||
                AR.s.Item == null || AR.s.Time == null || AR.s.Map == null)
            {
                const string notReady = "[TheftRules] 플레이 모드에서 GameScene이 로드된 뒤 실행하세요.";
                Debug.LogError(notReady);
                return notReady;
            }

            string hashBefore = ComputeLiveStateHash();
            UnityEngine.Random.State randomState = UnityEngine.Random.state;
            FieldInfo itemCounterField = GetField(AR.s.Item, "_instanceIdCounter");
            object? itemCounter = itemCounterField.GetValue(AR.s.Item);
            FieldInfo elapsedField = GetField(AR.s.Time, "_elapsedGameTime");
            FieldInfo accumulatorField = GetField(AR.s.Time, "_accumulator");
            object? elapsed = elapsedField.GetValue(AR.s.Time);
            object? accumulator = accumulatorField.GetValue(AR.s.Time);
            List<FieldSwap> swaps = new List<FieldSwap>();
            StringBuilder log = new StringBuilder();
            int failures = 0;
            string? error = null;

            void Check(bool condition, string label)
            {
                if (condition == false)
                    failures++;
                log.AppendLine((condition ? "  PASS " : "  FAIL ") + label);
            }

            // TradeAtShop은 매니저 시각을 읽는다. 주입한 사건 시각과 같은 시계를 쓰고 역행을 거부한다.
            void AdvanceGameTime(float gameTime)
            {
                if (gameTime < AR.s.Time.CurrentGameTime)
                    throw new InvalidOperationException("Theft diagnostic game time cannot move backwards.");
                elapsedField.SetValue(AR.s.Time, gameTime);
                accumulatorField.SetValue(AR.s.Time, 0f);
            }

            SimContext sim = new SimContext();
            try
            {
                BuildClones(sim);
                sim.WorldSeed = AR.s.Map.mapSeed;
                MethodInfo? createRng = typeof(NpcActivityResolver).GetMethod("CreateRng",
                    BindingFlags.NonPublic | BindingFlags.Static);
                if (createRng == null)
                    throw new MissingMethodException(nameof(NpcActivityResolver), "CreateRng");
                sim.CreateActivityRng = (Func<int, float, int, int, HistoryRng>)Delegate.CreateDelegate(
                    typeof(Func<int, float, int, int, HistoryRng>), createRng);
                SwapInClones(sim, swaps);
                AttachRumorProbes(sim, swaps);
                NpcManager npc = AR.s.Npc;
                float t0 = AR.s.Time.CurrentGameTime;
                int seed = sim.WorldSeed;

                // 생존 NPC(동행·결투 아님)가 가장 많은 마을 — 상점이 있는 마을을 먼저 본다(P4b 판매 경로를 실제 TradeAtShop으로 검증).
                // 가족·친구 역할 등은 다른 마을 NPC도 쓴다.
                VillageData? village = null;
                List<int> villagers = new List<int>();
                bool villageHasShop = false;
                for (int v = 0; v < sim.VillageList.Count; v++)
                {
                    List<int> living = new List<int>();
                    List<int> roster = sim.VillageList[v].NpcEntityIds;
                    for (int i = 0; i < roster.Count; i++)
                    {
                        if (IsSimLiving(sim, roster[i]) == true && npc.IsCompanionInDungeon(roster[i]) == false &&
                            npc.IsInPlayerDuel(roster[i]) == false)
                            living.Add(roster[i]);
                    }
                    bool hasShop = sim.VillageStats.TryGetValue(sim.VillageList[v].VillageId, out VillageStats? stats) == true &&
                                   stats.HasShop == true;
                    bool better = (hasShop == true && villageHasShop == false && living.Count >= 4) ||
                                  (hasShop == villageHasShop && living.Count > villagers.Count);
                    if (better == true)
                    {
                        village = sim.VillageList[v];
                        villagers = living;
                        villageHasShop = hasShop;
                    }
                }
                if (village == null || villagers.Count < 4)
                    throw new InvalidOperationException("생존 NPC 4명 이상인 마을을 찾지 못했습니다.");
                List<int> extras = new List<int>();
                for (int i = 4; i < villagers.Count; i++)
                    extras.Add(villagers[i]);
                for (int i = 0; i < sim.NpcIds.Count; i++)
                {
                    int id = sim.NpcIds[i];
                    if (IsSimLiving(sim, id) == true && sim.Npcs[id].VillageId != village.VillageId &&
                        npc.IsCompanionInDungeon(id) == false && npc.IsInPlayerDuel(id) == false)
                        extras.Add(id);
                }
                if (extras.Count < 15)
                    throw new InvalidOperationException("검증용 NPC가 부족합니다 (마을 밖 포함 19명 필요).");

                int thiefId = villagers[0], candidateId = villagers[1], caughtVictimId = villagers[2], bagVictimId = villagers[3];
                int directionVictimId = extras[0], equipVictimId = extras[1];
                int friendId = extras[2], siblingId = extras[3], childId = extras[4], parentId = extras[5],
                    deadSpouseId = extras[6], mentorId = extras[7];
                NpcSaveData thief = sim.Npcs[thiefId];
                log.AppendLine($"  마을 {village.VillageId} (생존 {villagers.Count}명), 도둑 {thiefId}, 후보 {candidateId}, " +
                               $"적발 피해자 {caughtVictimId}, 가방 피해자 {bagVictimId}, 방향 피해자 {directionVictimId}, 장착 피해자 {equipVictimId}");

                // 도둑 준비: 기회 주 야망(탐욕 100·정직 0), 시도 기록 없음, 가방 빈칸 1개 이상. 마을 명단 시도 기록도 비운다.
                thief.NpcStat = MakeTheftTestStat(100, 0, 0, 0);
                List<int> villageRoster = village.NpcEntityIds;
                for (int i = 0; i < villageRoster.Count; i++)
                {
                    if (sim.Npcs.TryGetValue(villageRoster[i], out NpcSaveData? member) == true)
                        member.LastTheftGameTime = -1f;
                }
                thief.EnsureSlotsInitialized();
                thief.Inventory[0] = null;

                // 훔칠 물건 견본: 장착 장비(인스턴스 있음, 비스택)의 복제본. 인스턴스 ID는 라이브와 겹치지 않는 대역을 쓴다.
                ItemData? template = null;
                for (int i = 0; i < sim.NpcIds.Count && template == null; i++)
                {
                    ItemData?[] equip = sim.Npcs[sim.NpcIds[i]].InventoryEquip;
                    for (int s = 0; s < equip.Length && template == null; s++)
                    {
                        if (equip[s] != null && equip[s]!.Equipment != null && equip[s]!.ItemInstanceId != 0)
                            template = equip[s];
                    }
                }
                if (template == null)
                    throw new InvalidOperationException("견본 장비를 찾지 못했습니다.");
                ItemData MakeCopy(ItemData source, int serial)
                {
                    ItemData copy = CloneJson(source);
                    copy.OnLoadCompleted();
                    copy.ItemInstanceId = 71000000 + serial;
                    return copy;
                }
                void PrepareVictim(int victimId, ItemData item)
                {
                    NpcSaveData victim = sim.Npcs[victimId];
                    victim.EnsureSlotsInitialized();
                    victim.Activity.Phase = NpcActivityPhase.Idle;
                    victim.Activity.Kind = NpcActivityKind.Rest;
                    victim.Activity.PartyId = -1;
                    RemoveSimRelationsBetween(sim, thiefId, victimId);
                    sim.Affinity.Remove(new NpcPairKey(thiefId, victimId));
                    victim.Inventory[0] = item;
                }
                // 조회 함수는 활동 보정까지 수행하지 않는다. 보정이 늦게 실행되면 캐시 게시 후 Activity가 달라진다.
                NpcSaveData slotQueryProbe = CloneJson(sim.Npcs[thiefId]);
                slotQueryProbe.Activity.Kind = NpcActivityKind.Hunt;
                slotQueryProbe.Activity.PhaseStartedGameTime = 12f;
                slotQueryProbe.Activity.OutcomeSeedGameTime = 0f;
                NpcManager.HasFreeBagSlot(slotQueryProbe);
                Check(slotQueryProbe.Activity.OutcomeSeedGameTime == 0f, "가방 빈칸 조회가 활동 저장 상태를 바꾸지 않음");

                void EnsureThiefFreeSlot()
                {
                    if (NpcManager.HasFreeBagSlot(thief) == false)
                        thief.Inventory[0] = null;
                }
                List<int> found = new List<int>();

                // ① 피해자 제외 규칙 (D99)
                NpcSaveData candidate = sim.Npcs[candidateId];
                PrepareVictim(candidateId, MakeCopy(template, 1));
                npc.CollectTheftVictims(thiefId, thief, found);
                Check(npc.IsTheftVictimCandidate(thiefId, thief, candidateId) == true && found.Contains(candidateId) == true,
                    "기본: 같은 마을 대기(Rest)·관계 없음·호감 0·훔칠 물건 있음 → 피해자 후보");
                (string Label, int From, int To, NpcRelationType Type)[] excluded =
                {
                    ("친구", thiefId, candidateId, NpcRelationType.Friend),
                    ("배우자", thiefId, candidateId, NpcRelationType.Spouse),
                    ("형제", candidateId, thiefId, NpcRelationType.Sibling),
                    ("부모(도둑→후보)", thiefId, candidateId, NpcRelationType.ParentOf),
                    ("자식(후보→도둑)", candidateId, thiefId, NpcRelationType.ParentOf),
                    ("원한(도둑→후보)", thiefId, candidateId, NpcRelationType.GrudgeAgainst),
                    ("원한(후보→도둑)", candidateId, thiefId, NpcRelationType.GrudgeAgainst),
                };
                for (int i = 0; i < excluded.Length; i++)
                {
                    npc.AddRelation(excluded[i].From, excluded[i].To, excluded[i].Type);
                    npc.CollectTheftVictims(thiefId, thief, found);
                    Check(npc.IsTheftVictimCandidate(thiefId, thief, candidateId) == false && found.Contains(candidateId) == false,
                        $"제외: {excluded[i].Label}");
                    RemoveSimRelationsBetween(sim, thiefId, candidateId);
                }
                npc.AddRelation(thiefId, candidateId, NpcRelationType.Rival);
                Check(npc.IsTheftVictimCandidate(thiefId, thief, candidateId) == true, "라이벌은 제외 대상이 아님 (D99 목록 밖)");
                RemoveSimRelationsBetween(sim, thiefId, candidateId);
                npc.SetAffinity(thiefId, candidateId, NpcManager.TheftVictimAffinityExclusion);
                bool atFifty = npc.IsTheftVictimCandidate(thiefId, thief, candidateId);
                npc.SetAffinity(thiefId, candidateId, NpcManager.TheftVictimAffinityExclusion - 1);
                bool atFortyNine = npc.IsTheftVictimCandidate(thiefId, thief, candidateId);
                sim.Affinity.Remove(new NpcPairKey(thiefId, candidateId));
                Check(atFifty == false && atFortyNine == true, "호감도 50 이상 제외, 49는 후보");
                candidate.Activity.Phase = NpcActivityPhase.Wounded;
                Check(npc.IsTheftVictimCandidate(thiefId, thief, candidateId) == false, "제외: 부상 중 (Phase Wounded)");
                candidate.Activity.Phase = NpcActivityPhase.Venturing;
                candidate.Activity.Kind = NpcActivityKind.Hunt;
                Check(npc.IsTheftVictimCandidate(thiefId, thief, candidateId) == false, "제외: 원정 중");
                candidate.Activity.Phase = NpcActivityPhase.Idle;
                candidate.Activity.Kind = NpcActivityKind.Trade;
                bool trade = npc.IsTheftVictimCandidate(thiefId, thief, candidateId);
                candidate.Activity.Kind = NpcActivityKind.None;
                bool none = npc.IsTheftVictimCandidate(thiefId, thief, candidateId);
                candidate.Activity.Kind = NpcActivityKind.Rest;
                Check(trade == true && none == true, "대기 Kind Trade·None도 후보");
                candidate.Activity.PartyId = 987654;
                Check(npc.IsTheftVictimCandidate(thiefId, thief, candidateId) == false, "제외: 파티 중");
                candidate.Activity.PartyId = -1;
                int candidateVillage = candidate.VillageId;
                candidate.VillageId = sim.Npcs[extras[extras.Count - 1]].VillageId != village.VillageId
                    ? sim.Npcs[extras[extras.Count - 1]].VillageId
                    : village.VillageId + 100000;
                Check(npc.IsTheftVictimCandidate(thiefId, thief, candidateId) == false, "제외: 다른 마을");
                candidate.VillageId = candidateVillage;
                CharacterConditions candidateCondition = candidate.Condition;
                candidate.Condition = CharacterConditions.Dead;
                Check(npc.IsTheftVictimCandidate(thiefId, thief, candidateId) == false, "제외: 사망");
                candidate.Condition = candidateCondition;
                List<ItemData?> candidateBag = new List<ItemData?>(candidate.Inventory);
                for (int i = 0; i < candidate.Inventory.Count; i++)
                    candidate.Inventory[i] = null;
                npc.CollectTheftVictims(thiefId, thief, found);
                Check(npc.IsTheftVictimCandidate(thiefId, thief, candidateId) == true && found.Contains(candidateId) == false,
                    "훔칠 물건이 없으면 피해자 목록에서 빠짐");
                for (int i = 0; i < candidateBag.Count; i++)
                    candidate.Inventory[i] = candidateBag[i];

                // ② 도둑 조건·확률 (D100·D101). 게임 시간이 짧은 세이브에서도 "몇 시간 전 시도" 시각이 음수(= 기록 없음)가 되지 않게 띄운다.
                float gameNow = t0 + 500f;
                Check(npc.GetTheftThiefBlock(thiefId, thief, gameNow, out float primaryMultiplier) == NpcTheftCheck.Attempt &&
                      Mathf.Approximately(primaryMultiplier, 1f), "도둑 조건 통과: 기회 주 야망 배율 1.0");
                global::ARPG.Component.NpcStatComponent thiefStat = thief.NpcStat;
                thief.NpcStat = MakeTheftTestStat(90, 10, 100, 100);
                Check(npc.GetTheftThiefBlock(thiefId, thief, gameNow, out float secondaryMultiplier) == NpcTheftCheck.Attempt &&
                      Mathf.Approximately(secondaryMultiplier, 0.5f), "기회 보조 야망 배율 0.5");
                thief.NpcStat = MakeTheftTestStat(0, 100, 100, 100);
                Check(npc.GetTheftThiefBlock(thiefId, thief, gameNow, out float noMultiplier) == NpcTheftCheck.NoAmbition &&
                      noMultiplier <= 0f, "기회 야망이 아니면 시도 없음");
                thief.NpcStat = thiefStat;
                thief.LastTheftGameTime = gameNow - 100f;
                NpcTheftCheck thiefRecent = npc.GetTheftThiefBlock(thiefId, thief, gameNow, out _);
                thief.LastTheftGameTime = gameNow - NpcManager.TheftThiefCooldownHours;
                NpcTheftCheck thiefElapsed = npc.GetTheftThiefBlock(thiefId, thief, gameNow, out _);
                thief.LastTheftGameTime = -1f;
                Check(thiefRecent == NpcTheftCheck.ThiefCooldown && thiefElapsed == NpcTheftCheck.Attempt,
                    "도둑 쿨다운: 100h 전 시도 → 차단, 168h 경과 → 통과");
                candidate.LastTheftGameTime = gameNow - 50f;
                NpcTheftCheck villageRecent = npc.GetTheftThiefBlock(thiefId, thief, gameNow, out _);
                candidate.LastTheftGameTime = gameNow - NpcManager.TheftVillageCooldownHours;
                NpcTheftCheck villageElapsed = npc.GetTheftThiefBlock(thiefId, thief, gameNow, out _);
                candidate.LastTheftGameTime = -1f;
                Check(villageRecent == NpcTheftCheck.VillageCooldown && villageElapsed == NpcTheftCheck.Attempt,
                    "같은 마을 쿨다운(명단 최근 시도): 50h 전 → 차단, 72h 경과 → 통과");
                List<ItemData?> thiefBag = new List<ItemData?>(thief.Inventory);
                for (int i = 0; i < thief.Inventory.Count; i++)
                {
                    if (thief.Inventory[i] == null)
                        thief.Inventory[i] = MakeCopy(template, 10 + i);
                }
                Check(npc.GetTheftThiefBlock(thiefId, thief, gameNow, out _) == NpcTheftCheck.NoFreeSlot,
                    "도둑 가방에 빈칸이 없으면 시도 없음");
                for (int i = 0; i < thiefBag.Count; i++)
                    thief.Inventory[i] = thiefBag[i];
                CharacterConditions thiefCondition = thief.Condition;
                thief.Condition = CharacterConditions.Dead;
                NpcTheftCheck deadCheck = npc.GetTheftThiefBlock(thiefId, thief, gameNow, out _);
                thief.Condition = thiefCondition;
                int thiefVillage = thief.VillageId;
                thief.VillageId = -1;
                NpcTheftCheck noVillageCheck = npc.GetTheftThiefBlock(thiefId, thief, gameNow, out _);
                thief.VillageId = thiefVillage;
                Check(deadCheck == NpcTheftCheck.NotEligible && noVillageCheck == NpcTheftCheck.NotEligible,
                    "사망·소속 마을 없음 → 자격 없음");
                // 기대값은 기본 확률 상수에서 계산한다 (D136에서 25% → 2%로 재조정).
                float theftBase = NpcManager.TheftBaseChance;
                Check(Mathf.Approximately(NpcManager.GetTheftAttemptChance(MakeTheftTestStat(100, 0, 0, 0), 1f), theftBase * 1.2f) &&
                      Mathf.Approximately(NpcManager.GetTheftAttemptChance(MakeTheftTestStat(0, 100, 0, 0), 0.5f), theftBase * 0.4f) &&
                      Mathf.Approximately(NpcManager.GetTheftAttemptChance(MakeTheftTestStat(50, 50, 0, 0), 1f), theftBase),
                    $"시도 확률 = {theftBase * 100f:0.#}% × 야망 × (1 + 0.2 × (탐욕 − 정직)/100): ×1.2 / ×0.4 / ×1");
                global::ARPG.Component.NpcStatComponent lowWatch = default;
                global::ARPG.Component.NpcStatComponent highWatch = new global::ARPG.Component.NpcStatComponent
                    { Curiosity = 100, Patience = 100 };
                Check(Mathf.Approximately(NpcManager.GetTheftDetectionChance(lowWatch), 0.25f) &&
                      Mathf.Approximately(NpcManager.GetTheftDetectionChance(highWatch), 0.75f),
                    "발각 확률 = 25% + 30% × 호기심 + 20% × 인내: 25% ~ 75%");

                // ③ 결정 (DecideTheft): 결정론·무상태, 같은 RNG 계약 (salt 170 시도 → 피해자 → 물건, 171 발각)
                int attemptCount = 0, rollFailedCount = 0, otherCount = 0;
                float attemptRestStart = float.NaN;
                const int decisionSamples = 400;
                for (int k = 0; k < decisionSamples; k++)
                {
                    float restStart = t0 - 4f - k * 0.37f;
                    NpcTheftCheck decisionResult = npc.DecideTheft(thiefId, thief, restStart, gameNow, seed, out _);
                    if (decisionResult == NpcTheftCheck.Attempt)
                    {
                        attemptCount++;
                        if (float.IsNaN(attemptRestStart) == true)
                            attemptRestStart = restStart;
                    }
                    else if (decisionResult == NpcTheftCheck.RollFailed)
                        rollFailedCount++;
                    else
                        otherCount++;
                }
                float expectedRate = NpcManager.GetTheftAttemptChance(thief.NpcStat, 1f);
                float attemptRate = attemptCount / (float)decisionSamples;
                Check(attemptCount > 0 && rollFailedCount > 0 && otherCount == 0 && Mathf.Abs(attemptRate - expectedRate) < 0.08f,
                    $"시도 롤: {decisionSamples}회 중 시도 {attemptCount} ({attemptRate:P1}, 기대 {expectedRate:P1}) / 롤 실패 {rollFailedCount}");
                if (float.IsNaN(attemptRestStart) == false)
                {
                    float lastBefore = thief.LastTheftGameTime;
                    npc.DecideTheft(thiefId, thief, attemptRestStart, gameNow, seed, out NpcTheftDecision first);
                    npc.DecideTheft(thiefId, thief, attemptRestStart, gameNow, seed, out NpcTheftDecision second);
                    NpcSaveData decidedVictim = sim.Npcs[first.VictimId];
                    npc.CollectTheftVictims(thiefId, thief, found);
                    Check(first.VictimId == second.VictimId && first.ItemSlot == second.ItemSlot &&
                          first.ItemInstanceId == second.ItemInstanceId && first.Detected == second.Detected &&
                          thief.LastTheftGameTime == lastBefore && decidedVictim.Inventory[first.ItemSlot] != null &&
                          decidedVictim.Inventory[first.ItemSlot]!.ItemInstanceId == first.ItemInstanceId,
                        "결정은 결정론적이고 상태를 바꾸지 않음");
                    bool detectedByContract = sim.CreateActivityRng!(thiefId, attemptRestStart, seed,
                        NpcActivityResolver.TheftDetectionSalt).NextDouble() < NpcManager.GetTheftDetectionChance(decidedVictim.NpcStat);
                    Check(found.Contains(first.VictimId) == true &&
                          NpcManager.IsStealableBagItem(decidedVictim.Inventory[first.ItemSlot]) == true &&
                          first.VictimCandidateCount == found.Count && first.Detected == detectedByContract,
                        $"결정 내용: 피해자 ∈ 후보({found.Count}명), 물건 훔칠 수 있음, 발각 = salt 171 롤 < 피해자 발각 확률 ({first.Detected})");

                    // Rest 종료 진입점: 파티 합류 경로(allowTheft false)는 절도 없음, 정상 종료는 결정 그대로 적용 + 독립 감사
                    int observedBefore = sim.TheftObserved;
                    npc.ResolveRestEnd(thiefId, thief, attemptRestStart, gameNow, seed, false);
                    Check(thief.LastTheftGameTime == lastBefore && sim.TheftObserved == observedBefore,
                        "ResolveRestEnd(allowTheft false — 파티 합류 경로)는 절도를 판정하지 않음");
                    int okBefore = sim.TheftCaughtOk + sim.TheftStolenOk;
                    npc.ResolveRestEnd(thiefId, thief, attemptRestStart, gameNow, seed, true);
                    AuditPendingTheft(sim);
                    bool appliedAsDecided = first.Detected == true
                        ? decidedVictim.Inventory[first.ItemSlot] != null &&
                          decidedVictim.Inventory[first.ItemSlot]!.ItemInstanceId == first.ItemInstanceId
                        : HoldsInstance(thief, first.ItemInstanceId, out _) == true;
                    Check(thief.LastTheftGameTime == gameNow && sim.TheftObserved == observedBefore + 1 &&
                          sim.TheftCaughtOk + sim.TheftStolenOk == okBefore + 1 && sim.TheftAuditFailCount == 0 &&
                          sim.TheftEligibilityFails == 0 && appliedAsDecided == true,
                        $"ResolveRestEnd(allowTheft true): 결정대로 적용({(first.Detected ? "현장 적발" : "안 들킴")}), 시도 시각 기록, 독립 감사 통과");
                    Check(npc.DecideTheft(thiefId, thief, attemptRestStart, gameNow + 1f, seed, out _) == NpcTheftCheck.ThiefCooldown,
                        "시도 직후 같은 도둑은 쿨다운");
                }
                else
                    Check(false, "시도 결정 표본을 찾지 못함");

                // 후보 없음 → 시도 아님, 쿨다운 기록 안 함 (명단을 도둑 1명으로 잠시 바꾼다)
                thief.LastTheftGameTime = -1f;
                village.NpcEntityIds = new List<int> { thiefId };
                float noVictimRestStart = float.NaN;
                for (int k = 0; k < decisionSamples && float.IsNaN(noVictimRestStart) == true; k++)
                {
                    float restStart = t0 - 4f - k * 0.37f;
                    if (npc.DecideTheft(thiefId, thief, restStart, gameNow, seed, out _) == NpcTheftCheck.NoVictim)
                        noVictimRestStart = restStart;
                }
                if (float.IsNaN(noVictimRestStart) == false)
                    npc.ResolveRestEnd(thiefId, thief, noVictimRestStart, gameNow, seed, true);
                village.NpcEntityIds = villageRoster;
                Check(float.IsNaN(noVictimRestStart) == false && thief.LastTheftGameTime < 0f && sim.PendingTheft == null,
                    "시도 롤을 통과해도 피해자 후보가 없으면 시도 아님 — 쿨다운 기록 안 함");

                // ④ 현장 적발 강제 (D101·D102·D106)
                NpcSaveData caughtVictim = sim.Npcs[caughtVictimId];
                ItemData caughtItem = MakeCopy(template, 100);
                PrepareVictim(caughtVictimId, caughtItem);
                int[] kinIds = { friendId, siblingId, childId, parentId, deadSpouseId, mentorId };
                for (int i = 0; i < kinIds.Length; i++)
                    RemoveSimRelationsBetween(sim, caughtVictimId, kinIds[i]);
                npc.AddRelation(caughtVictimId, friendId, NpcRelationType.Friend);
                npc.AddRelation(caughtVictimId, siblingId, NpcRelationType.Sibling);
                npc.AddRelation(siblingId, caughtVictimId, NpcRelationType.Friend);
                npc.AddRelation(caughtVictimId, childId, NpcRelationType.ParentOf);
                npc.AddRelation(parentId, caughtVictimId, NpcRelationType.ParentOf);
                npc.AddRelation(caughtVictimId, deadSpouseId, NpcRelationType.Spouse);
                npc.AddRelation(caughtVictimId, mentorId, NpcRelationType.MentorOf);
                npc.SetAffinity(friendId, thiefId, -95);
                npc.SetAffinity(siblingId, thiefId, 10);
                sim.Affinity.Remove(new NpcPairKey(childId, thiefId));
                npc.SetAffinity(parentId, thiefId, 0);
                npc.SetAffinity(deadSpouseId, thiefId, 5);
                npc.SetAffinity(mentorId, thiefId, 7);
                NpcSaveData deadSpouse = sim.Npcs[deadSpouseId];
                CharacterConditions deadSpouseCondition = deadSpouse.Condition;
                deadSpouse.Condition = CharacterConditions.Dead;
                List<int> kin = new List<int>();
                CollectSimKin(sim, caughtVictimId, thiefId, kin);
                List<int> kinBefore = new List<int>();
                for (int i = 0; i < kin.Count; i++)
                    kinBefore.Add(ReadSimAffinity(sim, kin[i], thiefId));
                int victimDeeds = caughtVictim.Deeds.Count, thiefDeeds = thief.Deeds.Count;
                NpcTheftDecision caughtDecision = new NpcTheftDecision
                {
                    ThiefId = thiefId, VictimId = caughtVictimId, ItemSlot = 0, ItemInstanceId = caughtItem.ItemInstanceId,
                    AmbitionMultiplier = 1f, VictimCandidateCount = 1, Detected = true,
                };
                float caughtTime = t0 + 20f;
                NpcTheftOutcome caughtOutcome = npc.ApplyTheft(caughtDecision, caughtTime);
                Check(caughtOutcome == NpcTheftOutcome.CaughtInAct && ReferenceEquals(caughtVictim.Inventory[0], caughtItem) &&
                      HoldsInstance(thief, caughtItem.ItemInstanceId, out _) == false,
                    "현장 적발: 물건은 피해자 가방 그 칸에 그대로, 도둑은 갖지 않음");
                Check(npc.GetAffinity(caughtVictimId, thiefId) == -40 && ReadSimAffinity(sim, caughtVictimId, thiefId) == -40,
                    "피해자↔도둑 호감도 0 → −40");
                Check(CountDirectedRelation(sim, caughtVictimId, caughtVictimId, thiefId, NpcRelationType.GrudgeAgainst) == 1 &&
                      CountDirectedRelation(sim, thiefId, caughtVictimId, thiefId, NpcRelationType.GrudgeAgainst) == 1 &&
                      CountDirectedRelation(sim, thiefId, thiefId, caughtVictimId, NpcRelationType.GrudgeAgainst) == 0,
                    "원한(피해자→도둑) 1건이 양쪽 인덱스에, 반대 방향 원한은 없음");
                Check(ReadSimAffinity(sim, friendId, thiefId) == NpcManager.AFFINITY_MIN, "친구: −95 → −100 (하한 클램프)");
                Check(ReadSimAffinity(sim, siblingId, thiefId) == 0, "형제이자 친구: 한 번만 −10 (10 → 0)");
                Check(ReadSimAffinity(sim, childId, thiefId) == -10 && ReadSimAffinity(sim, parentId, thiefId) == -10,
                    "부모자식 양방향(피해자가 부모 / 피해자가 자식) 모두 −10");
                Check(ReadSimAffinity(sim, deadSpouseId, thiefId) == 5, "사망한 배우자는 제외");
                Check(ReadSimAffinity(sim, mentorId, thiefId) == 7, "가족·친구가 아닌 관계(MentorOf)는 제외");
                bool kinExact = true;
                for (int i = 0; i < kin.Count; i++)
                {
                    int expected = Mathf.Clamp(kinBefore[i] + NpcManager.TheftKinAffinityPenalty, NpcManager.AFFINITY_MIN,
                        NpcManager.AFFINITY_MAX);
                    kinExact &= ReadSimAffinity(sim, kin[i], thiefId) == expected;
                }
                Check(kinExact == true && kin.Contains(deadSpouseId) == false && kin.Contains(mentorId) == false,
                    $"독립 계산한 피해자 가족·친구 {kin.Count}명 모두 정확히 −10 (기존 관계 포함)");
                NpcDeedEntry? victimDeed = caughtVictim.PersonalStories?.TheftVictim;
                NpcDeedEntry? thiefDeed = thief.Deeds.Count > 0 ? thief.Deeds[thief.Deeds.Count - 1] : null;
                string itemName = caughtItem.Table != null ? caughtItem.Table.Name : string.Empty;
                Check(caughtVictim.Deeds.Count == victimDeeds && victimDeed != null &&
                      victimDeed.Kind == NpcDeedKind.CaughtThiefInAct && victimDeed.Participants.Count == 1 &&
                      victimDeed.Participants[0] == thiefId && victimDeed.ItemId == caughtItem.Id &&
                      victimDeed.ItemDisplayName == itemName && Mathf.Approximately(victimDeed.When, caughtTime),
                    "피해자 Deed CaughtThiefInAct (참가자 = 도둑, 아이템 표시)");
                Check(thief.Deeds.Count == thiefDeeds + 1 && thiefDeed != null && thiefDeed.Kind == NpcDeedKind.CaughtStealing &&
                      thiefDeed.Participants.Count == 1 && thiefDeed.Participants[0] == caughtVictimId &&
                      thiefDeed.ItemId == caughtItem.Id,
                    "도둑 Deed CaughtStealing (참가자 = 피해자) — 발각 때만");
                Check(thief.LastTheftGameTime == caughtTime, "시도 시각 기록 (LastTheftGameTime)");
                NpcTheftOutcome caughtAgain = npc.ApplyTheft(caughtDecision, caughtTime + 1f);
                Check(caughtAgain == NpcTheftOutcome.CaughtInAct &&
                      CountDirectedRelation(sim, caughtVictimId, caughtVictimId, thiefId, NpcRelationType.GrudgeAgainst) == 1 &&
                      npc.GetAffinity(caughtVictimId, thiefId) == -80,
                    "같은 쌍 두 번째 적발: 원한 중복 없음, 호감도 −80");
                NpcTheftDecision staleDecision = caughtDecision;
                staleDecision.ItemInstanceId = caughtItem.ItemInstanceId + 1;
                float lastTheft = thief.LastTheftGameTime;
                int victimDeedsNow = caughtVictim.Deeds.Count;
                Check(npc.ApplyTheft(staleDecision, caughtTime + 2f) == NpcTheftOutcome.Invalid &&
                      thief.LastTheftGameTime == lastTheft && caughtVictim.Deeds.Count == victimDeedsNow,
                    "결정이 지금 상태와 맞지 않으면 Invalid — 아무것도 바꾸지 않음");
                deadSpouse.Condition = deadSpouseCondition;

                // ⑤ 원한 방향 구분 (D102): 도둑→피해자 원한이 이미 있어도 피해자→도둑 원한이 따로 추가된다
                ItemData directionItem = MakeCopy(template, 200);
                PrepareVictim(directionVictimId, directionItem);
                npc.AddRelation(thiefId, directionVictimId, NpcRelationType.GrudgeAgainst);
                Check(npc.IsTheftVictimCandidate(thiefId, thief, directionVictimId) == false,
                    "사전: 도둑→피해자 원한 (원한은 방향 무관하게 피해자 제외)");
                npc.ApplyTheft(new NpcTheftDecision
                {
                    ThiefId = thiefId, VictimId = directionVictimId, ItemSlot = 0, ItemInstanceId = directionItem.ItemInstanceId,
                    AmbitionMultiplier = 1f, VictimCandidateCount = 1, Detected = true,
                }, t0 + 30f);
                Check(CountDirectedRelation(sim, directionVictimId, directionVictimId, thiefId, NpcRelationType.GrudgeAgainst) == 1 &&
                      CountDirectedRelation(sim, directionVictimId, thiefId, directionVictimId, NpcRelationType.GrudgeAgainst) == 1,
                    "원한 방향 구분: 피해자→도둑 원한 추가, 도둑→피해자 원한 유지 (2건)");
                RemoveSimRelationsBetween(sim, friendId, mentorId);
                npc.AddRelation(friendId, mentorId, NpcRelationType.Friend);
                npc.AddRelation(mentorId, friendId, NpcRelationType.Friend);
                Check(CountDirectedRelation(sim, friendId, friendId, mentorId, NpcRelationType.Friend) +
                      CountDirectedRelation(sim, friendId, mentorId, friendId, NpcRelationType.Friend) == 1,
                    "다른 관계 종류(Friend)는 기존대로 방향 무관 중복 검사");

                // ⑥ 안 들킴 — 장착 불가(개인 상한) → 도둑 가방 (D101·D45)
                NpcSaveData bagVictim = sim.Npcs[bagVictimId];
                ItemData bagItem = MakeCopy(template, 300);
                PrepareVictim(bagVictimId, bagItem);
                EnsureThiefFreeSlot();
                float thiefInitialPower = thief.InitialPower;
                thief.InitialPower = 0.001f;   // 개인 상한을 아주 낮춰 업그레이드 장착을 막는다 (상한 규칙)
                npc.InvalidateNpcPower(thiefId);
                List<int> heldBefore = new List<int>();
                CollectHeldInstances(thief, heldBefore);
                List<int> bagKin = new List<int>();
                CollectSimKin(sim, bagVictimId, thiefId, bagKin);
                List<int> bagKinBefore = new List<int>();
                for (int i = 0; i < bagKin.Count; i++)
                    bagKinBefore.Add(ReadSimAffinity(sim, bagKin[i], thiefId));
                int bagAffinityBefore = ReadSimAffinity(sim, bagVictimId, thiefId);
                int bagVictimRelations = CountSimRelations(sim, bagVictimId), thiefRelations = CountSimRelations(sim, thiefId);
                int bagVictimDeeds = bagVictim.Deeds.Count, thiefDeedsBefore = thief.Deeds.Count;
                NpcTheftOutcome bagOutcome = npc.ApplyTheft(new NpcTheftDecision
                {
                    ThiefId = thiefId, VictimId = bagVictimId, ItemSlot = 0, ItemInstanceId = bagItem.ItemInstanceId,
                    AmbitionMultiplier = 1f, VictimCandidateCount = 1, Detected = false,
                }, t0 + 40f);
                bool inThiefBag = HoldsInstance(thief, bagItem.ItemInstanceId, out bool bagEquipped);
                Check(bagOutcome == NpcTheftOutcome.StolenToBag && HoldsInstance(bagVictim, bagItem.ItemInstanceId, out _) == false &&
                      inThiefBag == true && bagEquipped == false,
                    "안 들킴: 물건이 피해자 가방에서 도둑 가방으로 이동 (훔친 장비는 장착하지 않음, D119)");
                bool keptAll = true;
                for (int i = 0; i < heldBefore.Count; i++)
                    keptAll &= HoldsInstance(thief, heldBefore[i], out _);
                Check(keptAll == true, "도둑 가방 빈칸이 있으므로 버림 없음");
                bool bagKinSame = true;
                for (int i = 0; i < bagKin.Count; i++)
                    bagKinSame &= ReadSimAffinity(sim, bagKin[i], thiefId) == bagKinBefore[i];
                Check(ReadSimAffinity(sim, bagVictimId, thiefId) == bagAffinityBefore && bagKinSame == true &&
                      CountSimRelations(sim, bagVictimId) == bagVictimRelations && CountSimRelations(sim, thiefId) == thiefRelations,
                    "안 들킴: 호감도·유형 관계 변화 없음 (피해자·가족·친구)");
                NpcDeedEntry? stolenDeed = bagVictim.PersonalStories?.TheftVictim;
                Check(thief.Deeds.Count == thiefDeedsBefore && bagVictim.Deeds.Count == bagVictimDeeds && stolenDeed != null &&
                      stolenDeed.Kind == NpcDeedKind.ItemStolen && stolenDeed.Participants.Count == 0 &&
                      stolenDeed.ParticipantSnapshots.Count == 0 && stolenDeed.ItemId == bagItem.Id,
                    "안 들킴: 피해자 Deed ItemStolen(참가자 없음), 도둑 쪽 Deed 없음");
                thief.InitialPower = thiefInitialPower;
                npc.InvalidateNpcPower(thiefId);

                // ⑦ 안 들킴 — 업그레이드 → 바로 장착 (D45 문턱·상한, 출처 Theft). 장비를 모두 벗긴 뒤 원래 가장 강한 장비의 복제본을 훔친다.
                NpcSaveData equipVictim = sim.Npcs[equipVictimId];
                ItemData?[] scratch = new ItemData?[thief.InventoryEquip.Length];
                int bestSlot = -1;
                float bestPower = 0f;
                for (int s = 0; s < thief.InventoryEquip.Length; s++)
                {
                    ItemData? equipped = thief.InventoryEquip[s];
                    if (equipped == null || equipped.Equipment == null)
                        continue;
                    Array.Clear(scratch, 0, scratch.Length);
                    scratch[s] = equipped;
                    float power = NpcPowerEvaluator.EvaluateBreakdown(scratch, thief.SkillLevel).Total;
                    if (bestSlot < 0 || power > bestPower)
                    {
                        bestSlot = s;
                        bestPower = power;
                    }
                }
                ItemData? stolenEquipItem = null;
                if (bestSlot < 0)
                    Check(false, "도둑에게 장착 장비가 없어 장착 경로를 검증하지 못함");
                else
                {
                    ItemData equipItem = MakeCopy(thief.InventoryEquip[bestSlot]!, 400);
                    stolenEquipItem = equipItem;
                    for (int s = 0; s < thief.InventoryEquip.Length; s++)
                        thief.InventoryEquip[s] = null;
                    npc.InvalidateNpcPower(thiefId);
                    float strippedPower = NpcPowerEvaluator.Evaluate(thief);
                    thief.InitialPower = Mathf.Max(bestPower, 0.01f);
                    float cap = NpcManager.GetNpcPowerCap(thief);
                    float requiredRatio = strippedPower > cap * 0.8f ? 1.30f : 1.15f;   // D45: 상한 80% 초과면 +30%
                    Check(bestPower >= strippedPower * requiredRatio && bestPower <= cap,
                        $"사전: 문턱(×{requiredRatio:F2})·상한 충족 ({strippedPower:F3} → {bestPower:F3}, 상한 {cap:F3})");
                    EnsureThiefFreeSlot();
                    PrepareVictim(equipVictimId, equipItem);
                    int notableBefore = CountDeeds(thief, NpcDeedKind.NotableItemAcquired);
                    int theftDeedsBefore = CountTheftDeeds(thief);
                    NpcTheftOutcome equipOutcome = npc.ApplyTheft(new NpcTheftDecision
                    {
                        ThiefId = thiefId, VictimId = equipVictimId, ItemSlot = 0, ItemInstanceId = equipItem.ItemInstanceId,
                        AmbitionMultiplier = 1f, VictimCandidateCount = 1, Detected = false,
                    }, t0 + 50f);
                    bool held = HoldsInstance(thief, equipItem.ItemInstanceId, out bool equippedNow);
                    Check(equipOutcome == NpcTheftOutcome.StolenToBag && held == true && equippedNow == false &&
                          HoldsInstance(equipVictim, equipItem.ItemInstanceId, out _) == false &&
                          Mathf.Abs(NpcPowerEvaluator.Evaluate(thief) - strippedPower) <= PowerEpsilon,
                        "안 들킴 + 업그레이드 후보여도 가방행, 장착하지 않음 (NPC_SHOP_EQUIPMENT D119)");
                    bool hasOwnerRumor = false;
                    for (int i = 0; i < thief.Rumors.Count; i++)
                    {
                        NpcRumor rumor = thief.Rumors[i];
                        hasOwnerRumor |= rumor.Kind == NpcRumorKind.ItemOwner && rumor.SubjectNpcId == thiefId &&
                                         rumor.ItemInstanceId == equipItem.ItemInstanceId;
                    }
                    Check(hasOwnerRumor == false && CountDeeds(thief, NpcDeedKind.NotableItemAcquired) == notableBefore &&
                          CountTheftDeeds(thief) == theftDeedsBefore,
                        "훔친 장비로는 ItemOwner 소문·NotableItemAcquired가 생기지 않음, 도둑 쪽 절도 Deed 없음");
                    NpcDeedEntry? equipDeed = equipVictim.PersonalStories?.TheftVictim;
                    Check(equipDeed != null && equipDeed.Kind == NpcDeedKind.ItemStolen && equipDeed.Participants.Count == 0,
                        "피해자 Deed ItemStolen");

                    // 게임에서는 훔친 장비를 장착하지 않지만(D119) 장착 소문(ItemOwner)으로 늦게 발각되는 경로는 방어 코드로 남아 있다.
                    // ⑧-2 검증용으로 진단에서만 직접 장착해 소유자 본인의 ItemOwner 소문을 만든다.
                    for (int i = 0; i < thief.Inventory.Count; i++)
                    {
                        if (thief.Inventory[i] != null && thief.Inventory[i]!.ItemInstanceId == equipItem.ItemInstanceId)
                            thief.Inventory[i] = null;
                    }
                    thief.EquipItem((GlobalEnum.EquipSlotType)bestSlot, equipItem, out _);
                    npc.InvalidateNpcPower(thiefId);
                    npc.NotifyNpcEquipmentChanged(thiefId, thief, (GlobalEnum.EquipSlotType)bestSlot, equipItem, t0 + 50f);
                }

                // ================= P4b: 단서 · 늦은 발각 · 판매/배신 소문 · 대화 (D103~D106) =================
                float p4bTime = t0 + 60f;
                NpcTheftCounters theftCounters = npc.TheftCounters;
                float p4bInitialPower = thief.InitialPower;

                // ⑧-1 단서 (D103): 안 들킨 절도 → 피해자 단서, 상한 3 (가장 오래된 것부터 지움)
                Check(HasTestClue(bagVictim, bagItem.ItemInstanceId, thiefId, t0 + 40f) == true &&
                      (stolenEquipItem == null || HasTestClue(equipVictim, stolenEquipItem.ItemInstanceId, thiefId, t0 + 50f) == true),
                    "단서: 안 들킨 절도마다 피해자에게 {인스턴스, 실제 범인, 시각}");
                thief.InitialPower = 0.001f;   // 판매 검증용 (훔친 물건은 원래 장착되지 않는다, D119)
                npc.InvalidateNpcPower(thiefId);
                for (int i = 0; i < thief.Inventory.Count; i++)
                    thief.Inventory[i] = null;
                List<ItemData> capItems = new List<ItemData>();
                for (int n = 0; n < NpcSaveData.MaxTheftClues; n++)
                {
                    ItemData capItem = MakeCopy(template, 500 + n);
                    bagVictim.Inventory[0] = capItem;
                    AdvanceGameTime(p4bTime + n);
                    npc.ApplyTheft(new NpcTheftDecision
                    {
                        ThiefId = thiefId, VictimId = bagVictimId, ItemSlot = 0, ItemInstanceId = capItem.ItemInstanceId,
                        AmbitionMultiplier = 1f, VictimCandidateCount = 1, Detected = false,
                    }, p4bTime + n);
                    capItems.Add(capItem);
                }
                bool capKept = bagVictim.TheftClues.Count == NpcSaveData.MaxTheftClues &&
                               HasTestClue(bagVictim, bagItem.ItemInstanceId, thiefId, t0 + 40f) == false;
                for (int n = 0; n < capItems.Count; n++)
                    capKept &= HasTestClue(bagVictim, capItems[n].ItemInstanceId, thiefId, p4bTime + n);
                Check(capKept == true, "단서 상한 3: 네 번째 도난에서 가장 오래된 단서(첫 도난)가 지워짐");

                // ⑧-2 늦은 발각 — 장착 경로(방어 코드): 훔친 장비 장착(진단이 직접) → ItemOwner 소문 → 피해자가 받아들임 → 발각
                if (stolenEquipItem != null)
                {
                    NpcRumor? ownerRumor = null;
                    for (int i = 0; i < thief.Rumors.Count; i++)
                    {
                        NpcRumor rumor = thief.Rumors[i];
                        if (rumor.Kind == NpcRumorKind.ItemOwner && rumor.SubjectNpcId == thiefId &&
                            rumor.ItemInstanceId == stolenEquipItem.ItemInstanceId)
                            ownerRumor = rumor;
                    }
                    if (ownerRumor == null)
                        Check(false, "장착 경로: 도둑의 ItemOwner 소문을 찾지 못함");
                    else
                    {
                        RemoveSameTargetRumors(equipVictim, ownerRumor);
                        int discoveredOkBefore = sim.DiscoveryOk[0];
                        List<int> equipKin = new List<int>();
                        CollectSimKin(sim, equipVictimId, thiefId, equipKin);
                        List<int> equipKinBefore = new List<int>();
                        for (int i = 0; i < equipKin.Count; i++)
                            equipKinBefore.Add(ReadSimAffinity(sim, equipKin[i], thiefId));
                        int equipAffinityBefore = ReadSimAffinity(sim, equipVictimId, thiefId);
                        int equipVictimDeeds = equipVictim.Deeds.Count, thiefDeedsNow = thief.Deeds.Count;
                        long lateOwnerBefore = theftCounters.LateDiscoveries[NpcManager.TheftDiscoveryPathItemOwner];
                        AdvanceGameTime(p4bTime + 10f);
                        bool accepted = npc.ReceiveRumor(equipVictim, ownerRumor, thiefId, p4bTime + 10f);
                        bool kinOk = true;
                        for (int i = 0; i < equipKin.Count; i++)
                            kinOk &= ReadSimAffinity(sim, equipKin[i], thiefId) ==
                                     Mathf.Clamp(equipKinBefore[i] - 10, NpcManager.AFFINITY_MIN, NpcManager.AFFINITY_MAX);
                        Check(accepted == true && theftCounters.LateDiscoveries[NpcManager.TheftDiscoveryPathItemOwner] == lateOwnerBefore + 1 &&
                              ReadSimAffinity(sim, equipVictimId, thiefId) == Mathf.Clamp(equipAffinityBefore - 40, NpcManager.AFFINITY_MIN, NpcManager.AFFINITY_MAX) &&
                              CountDirectedRelation(sim, equipVictimId, equipVictimId, thiefId, NpcRelationType.GrudgeAgainst) == 1 &&
                              kinOk == true && HasTestClue(equipVictim, stolenEquipItem.ItemInstanceId, thiefId, t0 + 50f) == false,
                            $"늦은 발각(장착 소문): 받아들이는 순간 −40·원한·가족친구 −10({equipKin.Count}명)·단서 제거");
                        NpcDeedEntry? victimLate = equipVictim.PersonalStories?.TheftVictim;
                        NpcDeedEntry? thiefLate = thief.Deeds.Count > 0 ? thief.Deeds[thief.Deeds.Count - 1] : null;
                        Check(equipVictim.Deeds.Count == equipVictimDeeds && victimLate != null &&
                              victimLate.Kind == NpcDeedKind.TheftDiscovered && victimLate.Participants.Count == 1 &&
                              victimLate.Participants[0] == thiefId && victimLate.ItemId == stolenEquipItem.Id &&
                              thief.Deeds.Count == thiefDeedsNow + 1 && thiefLate != null && thiefLate.Kind == NpcDeedKind.TheftExposed &&
                              thiefLate.Participants.Count == 1 && thiefLate.Participants[0] == equipVictimId,
                            "늦은 발각 Deed: 피해자 TheftDiscovered(참가자 = 도둑) / 도둑 TheftExposed(참가자 = 피해자, 도둑 쪽 첫 기록)");
                        Check(HasBetrayalRumorOrCapDrop(equipVictim, thiefId, equipVictimId, NpcBetrayalKind.TheftDiscovered, t0 + 50f) == true &&
                              sim.DiscoveryOk[0] == discoveredOkBefore + 1,
                            "늦은 발각: 피해자 직접 Betrayal 소문(늦은 발각, 사건 시각 = 절도 시각) + 독립 감사 통과");
                    }
                }

                // ⑧-3 판매 소문 (D104): 훔친 물건 판매만 ItemSold. 판매 마을 명단의 유효 단서 (인스턴스, 판매자 = 범인)로 판별
                float saleTime = p4bTime + 11f;
                AdvanceGameTime(saleTime);
                float realNow = AR.s.Time.CurrentGameTime;
                Check(realNow == saleTime && realNow > p4bTime + capItems.Count - 1,
                    "판매·발각 시각은 도난 뒤이며 매니저 시각과 같음");
                ItemData normalItem = MakeCopy(template, 600);
                thief.Inventory[NpcSaveData.MaxTheftClues] = normalItem;
                long soldBefore = sim.RumorCounters.CreatedItemSold;
                bool viaShop = false;
                if (villageHasShop == true)
                {
                    AR.s.Village.DepositNpcGold(village.VillageId, 100000);   // 복제본 마을 — 매입 거절 없이 실제 판매 경로를 태운다
                    NpcTradeReport report = npc.TradeAtShop(thiefId, village.VillageId);
                    viaShop = report.SoldCount > 0;
                }
                for (int i = 0; i < thief.Inventory.Count; i++)
                {
                    // 상점이 없거나 팔리지 않은 것: 판매 확정 뒤 부르는 같은 진입점을 직접 부른다
                    ItemData? left = thief.Inventory[i];
                    if (left == null)
                        continue;
                    thief.Inventory[i] = null;
                    npc.NotifyNpcItemSold(thiefId, thief, village.VillageId, left, realNow);
                }
                NpcRumor? soldRumor = null;
                bool normalSold = false;
                int stolenSold = 0;
                for (int i = 0; i < thief.Rumors.Count; i++)
                {
                    NpcRumor rumor = thief.Rumors[i];
                    if (rumor.Kind != NpcRumorKind.ItemSold || rumor.SubjectNpcId != thiefId)
                        continue;
                    if (rumor.ItemInstanceId == normalItem.ItemInstanceId)
                        normalSold = true;
                    for (int n = 0; n < capItems.Count; n++)
                    {
                        if (rumor.ItemInstanceId == capItems[n].ItemInstanceId)
                        {
                            stolenSold++;
                            if (n == capItems.Count - 1)
                                soldRumor = rumor;
                        }
                    }
                }
                Check(stolenSold == capItems.Count && normalSold == false &&
                      sim.RumorCounters.CreatedItemSold == soldBefore + capItems.Count && soldRumor != null &&
                      soldRumor.SourceNpcId < 0 && soldRumor.ItemId == capItems[capItems.Count - 1].Id && npc.IsRumorValid(soldRumor) == true,
                    $"판매 소문: 훔친 물건 {capItems.Count}개만 ItemSold(판매자 본인이 처음 보유), 일반 판매는 없음 ({(viaShop ? "실제 TradeAtShop" : "판매 진입점 직접 호출")})");
                if (soldRumor != null)
                {
                    RemoveSameTargetRumors(bagVictim, soldRumor);
                    long lateSoldBefore = theftCounters.LateDiscoveries[NpcManager.TheftDiscoveryPathItemSold];
                    int soldOkBefore = sim.DiscoveryOk[1];
                    int bagAffinityNow = ReadSimAffinity(sim, bagVictimId, thiefId);
                    bool soldAccepted = npc.ReceiveRumor(bagVictim, soldRumor, thiefId, realNow);
                    Check(soldAccepted == true &&
                          theftCounters.LateDiscoveries[NpcManager.TheftDiscoveryPathItemSold] == lateSoldBefore + 1 &&
                          sim.DiscoveryOk[1] == soldOkBefore + 1 &&
                          ReadSimAffinity(sim, bagVictimId, thiefId) == Mathf.Clamp(bagAffinityNow - 40, NpcManager.AFFINITY_MIN, NpcManager.AFFINITY_MAX) &&
                          HasTestClue(bagVictim, capItems[capItems.Count - 1].ItemInstanceId, thiefId, p4bTime + capItems.Count - 1) == false &&
                          bagVictim.TheftClues.Count == NpcSaveData.MaxTheftClues - 1 &&
                          HasBetrayalRumorOrCapDrop(bagVictim, thiefId, bagVictimId, NpcBetrayalKind.TheftDiscovered,
                              p4bTime + capItems.Count - 1) == true,
                        "늦은 발각(판매 소문): 판 물건의 단서만 제거(나머지 2건 유지)·−40·Betrayal·독립 감사 통과");
                    Check(npc.IsStolenItemSale(thiefId, village.VillageId, normalItem.ItemInstanceId, realNow) == false &&
                          npc.IsStolenItemSale(thiefId, village.VillageId, capItems[0].ItemInstanceId, realNow) == true,
                        "훔친 물건 판별: 단서 없는 물건 false / 유효 단서가 있는 물건 true");
                }

                // ⑧-4 소문 주체가 실제 범인이 아니면 발각 없음 (도둑의 도둑): V5 → 도둑(T) → 두 번째 도둑(B)이 같은 물건을 다시 훔쳐 판다
                int chainVictimId = extras[8];
                NpcSaveData chainVictim = sim.Npcs[chainVictimId];
                int secondThiefId = candidateId;
                NpcSaveData secondThief = candidate;
                ItemData chainItem = MakeCopy(template, 700);
                PrepareVictim(chainVictimId, chainItem);
                AdvanceGameTime(p4bTime + 20f);
                npc.ApplyTheft(new NpcTheftDecision
                {
                    ThiefId = thiefId, VictimId = chainVictimId, ItemSlot = 0, ItemInstanceId = chainItem.ItemInstanceId,
                    AmbitionMultiplier = 1f, VictimCandidateCount = 1, Detected = false,
                }, p4bTime + 20f);
                float secondInitialPower = secondThief.InitialPower;
                secondThief.InitialPower = 0.001f;
                npc.InvalidateNpcPower(secondThiefId);
                secondThief.EnsureSlotsInitialized();
                secondThief.Inventory[0] = null;
                int chainSlot = -1;
                for (int i = 0; i < thief.Inventory.Count; i++)
                {
                    if (thief.Inventory[i] != null && thief.Inventory[i]!.ItemInstanceId == chainItem.ItemInstanceId)
                        chainSlot = i;
                }
                AdvanceGameTime(p4bTime + 21f);
                NpcTheftOutcome rethief = npc.ApplyTheft(new NpcTheftDecision
                {
                    ThiefId = secondThiefId, VictimId = thiefId, ItemSlot = chainSlot, ItemInstanceId = chainItem.ItemInstanceId,
                    AmbitionMultiplier = 1f, VictimCandidateCount = 1, Detected = false,
                }, p4bTime + 21f);
                for (int i = 0; i < secondThief.Inventory.Count; i++)
                {
                    if (secondThief.Inventory[i] != null && secondThief.Inventory[i]!.ItemInstanceId == chainItem.ItemInstanceId)
                        secondThief.Inventory[i] = null;
                }
                AdvanceGameTime(p4bTime + 22f);
                realNow = AR.s.Time.CurrentGameTime;
                Check(realNow > p4bTime + 21f, "재절도 뒤 판매·발각 시각 전진");
                npc.NotifyNpcItemSold(secondThiefId, secondThief, village.VillageId, chainItem, realNow);
                NpcRumor? soldBySecond = null;
                for (int i = 0; i < secondThief.Rumors.Count; i++)
                {
                    if (secondThief.Rumors[i].Kind == NpcRumorKind.ItemSold &&
                        secondThief.Rumors[i].ItemInstanceId == chainItem.ItemInstanceId)
                        soldBySecond = secondThief.Rumors[i];
                }
                Check(rethief == NpcTheftOutcome.StolenToBag && soldBySecond != null && soldBySecond.SubjectNpcId == secondThiefId &&
                      HasTestClue(chainVictim, chainItem.ItemInstanceId, thiefId, p4bTime + 20f) == true &&
                      HasTestClue(thief, chainItem.ItemInstanceId, secondThiefId, p4bTime + 21f) == true,
                    "사전: V5 단서(범인 = T), T 단서(범인 = B), B가 판매 → ItemSold(주체 B)");
                if (soldBySecond != null)
                {
                    long lateTotal = theftCounters.LateDiscoveries[0] + theftCounters.LateDiscoveries[1];
                    int chainAffinityT = ReadSimAffinity(sim, chainVictimId, thiefId);
                    int chainAffinityB = ReadSimAffinity(sim, chainVictimId, secondThiefId);
                    bool chainAccepted = npc.ReceiveRumor(chainVictim, soldBySecond, secondThiefId, realNow);
                    Check(chainAccepted == true && theftCounters.LateDiscoveries[0] + theftCounters.LateDiscoveries[1] == lateTotal &&
                          HasTestClue(chainVictim, chainItem.ItemInstanceId, thiefId, p4bTime + 20f) == true &&
                          ReadSimAffinity(sim, chainVictimId, thiefId) == chainAffinityT &&
                          ReadSimAffinity(sim, chainVictimId, secondThiefId) == chainAffinityB,
                        "소문 주체(B)가 단서의 실제 범인(T)이 아니면 발각 없음 — 단서·관계 그대로");
                    RemoveSameTargetRumors(thief, soldBySecond);
                    npc.ReceiveRumor(thief, soldBySecond, secondThiefId, realNow);
                    Check(theftCounters.LateDiscoveries[0] + theftCounters.LateDiscoveries[1] == lateTotal + 1 &&
                          CountDirectedRelation(sim, thiefId, thiefId, secondThiefId, NpcRelationType.GrudgeAgainst) == 1,
                        "같은 판매 소문을 T가 받으면 T의 단서(범인 = B)로 발각 → 원한(T→B)");
                }
                secondThief.InitialPower = secondInitialPower;
                npc.InvalidateNpcPower(secondThiefId);

                // ⑧-5 단서 만료(14일)·범인 사망 → 발각 없음, 피해자 Rest 종료 때 정리
                int expiryVictimId = extras[11];
                NpcSaveData expiryVictim = sim.Npcs[expiryVictimId];
                expiryVictim.TheftClues.Clear();
                const int expiredInstance = 72000001;
                npc.AddTheftClue(expiryVictim, expiredInstance, thiefId, realNow - NpcManager.TheftClueLifetimeHours - 1f);
                NpcRumor expiredSale = new NpcRumor
                {
                    Kind = NpcRumorKind.ItemSold, SubjectNpcId = thiefId, ItemInstanceId = expiredInstance, ItemId = template.Id,
                    Value = 10, EventGameTime = realNow,
                };
                long lateBeforeExpiry = theftCounters.LateDiscoveries[0] + theftCounters.LateDiscoveries[1];
                long invalidMatchBefore = theftCounters.LateMatchInvalidClue;
                bool expiredStolen = npc.IsStolenItemSale(thiefId, expiryVictim.VillageId, expiredInstance, realNow);
                npc.ReceiveRumor(expiryVictim, expiredSale, thiefId, realNow);
                Check(theftCounters.LateDiscoveries[0] + theftCounters.LateDiscoveries[1] == lateBeforeExpiry &&
                      theftCounters.LateMatchInvalidClue == invalidMatchBefore + 1 && expiryVictim.TheftClues.Count == 1 &&
                      expiredStolen == false,
                    "14일 지난 단서: 맞는 소문이 와도 발각 없음, 판매 소문 판별도 false (정리 전까지 단서는 남음)");
                long expiredBefore = theftCounters.CluesExpired;
                npc.ResolveRestEnd(expiryVictimId, expiryVictim, realNow - 4f, realNow, seed, false);
                Check(expiryVictim.TheftClues.Count == 0 && theftCounters.CluesExpired == expiredBefore + 1,
                    "피해자 Rest 종료 때 14일 지난 단서 정리");
                int deathVictimId = extras[12], deadThiefId = extras[13];
                NpcSaveData deathVictim = sim.Npcs[deathVictimId];
                NpcSaveData deadThief = sim.Npcs[deadThiefId];
                deathVictim.TheftClues.Clear();
                const int deadThiefInstance = 72000002;
                npc.AddTheftClue(deathVictim, deadThiefInstance, deadThiefId, realNow - 10f);
                CharacterConditions deadThiefCondition = deadThief.Condition;
                deadThief.Condition = CharacterConditions.Dead;
                NpcRumor deadSale = expiredSale.Clone();
                deadSale.SubjectNpcId = deadThiefId;
                deadSale.ItemInstanceId = deadThiefInstance;
                long lateBeforeDeath = theftCounters.LateDiscoveries[0] + theftCounters.LateDiscoveries[1];
                npc.ReceiveRumor(deathVictim, deadSale, deadThiefId, realNow);
                long thiefDeadBefore = theftCounters.CluesThiefDead;
                bool lateUnchanged = theftCounters.LateDiscoveries[0] + theftCounters.LateDiscoveries[1] == lateBeforeDeath;
                npc.ResolveRestEnd(deathVictimId, deathVictim, realNow - 4f, realNow, seed, false);
                Check(lateUnchanged == true && deathVictim.TheftClues.Count == 0 && theftCounters.CluesThiefDead == thiefDeadBefore + 1,
                    "범인 사망: 발각 없음, Rest 종료 때 단서 정리");
                deadThief.Condition = deadThiefCondition;

                // ⑧-6 Betrayal 유효성·같은 대상 중복 방지·현장 적발 Betrayal (D105)
                Check(HasBetrayalRumorOrCapDrop(caughtVictim, thiefId, caughtVictimId, NpcBetrayalKind.TheftCaughtInAct, caughtTime) == true,
                    "현장 적발도 발각 순간 피해자 직접 Betrayal 소문(현장 적발)을 만듦");
                NpcRumor? betrayal = null;
                for (int i = 0; i < caughtVictim.Rumors.Count; i++)
                {
                    if (caughtVictim.Rumors[i].Kind == NpcRumorKind.Betrayal && caughtVictim.Rumors[i].SubjectNpcId == thiefId)
                        betrayal = caughtVictim.Rumors[i];
                }
                int witnessId = -1;
                if (sim.Villages.TryGetValue(caughtVictim.VillageId, out VillageData? caughtVillage) == true)
                {
                    for (int i = 0; i < caughtVillage.NpcEntityIds.Count && witnessId < 0; i++)
                    {
                        int id = caughtVillage.NpcEntityIds[i];
                        if (id != caughtVictimId && id != thiefId && IsSimLiving(sim, id) == true)
                            witnessId = id;
                    }
                }
                if (witnessId < 0)
                    witnessId = extras[9];
                NpcSaveData witness = sim.Npcs[witnessId];
                if (betrayal == null)
                    Check(false, "현장 적발 Betrayal 소문을 찾지 못함");
                else
                {
                    CharacterConditions thiefCond = thief.Condition, victimCond = caughtVictim.Condition;
                    bool validBoth = npc.IsRumorValid(betrayal);
                    thief.Condition = CharacterConditions.Dead;
                    bool validThiefDead = npc.IsRumorValid(betrayal);
                    thief.Condition = thiefCond;
                    caughtVictim.Condition = CharacterConditions.Dead;
                    bool validVictimDead = npc.IsRumorValid(betrayal);
                    caughtVictim.Condition = victimCond;
                    Check(validBoth == true && validThiefDead == false && validVictimDead == false,
                        "Betrayal 유효 = 가해자·피해자 둘 다 생존 (어느 쪽이 죽어도 무효)");
                    witness.Rumors.Clear();
                    bool firstReceive = npc.ReceiveRumor(witness, betrayal, caughtVictimId, realNow);
                    bool secondReceive = npc.ReceiveRumor(witness, betrayal, caughtVictimId, realNow);
                    NpcRumor later = betrayal.Clone();
                    later.EventGameTime = betrayal.EventGameTime + 1f;
                    int witnessBetrayals = 0;
                    for (int i = 0; i < witness.Rumors.Count; i++)
                    {
                        if (witness.Rumors[i].Kind == NpcRumorKind.Betrayal)
                            witnessBetrayals++;
                    }
                    Check(firstReceive == true && secondReceive == false && witnessBetrayals == 1 &&
                          NpcManager.IsSameRumorTarget(betrayal, betrayal.Clone()) == true &&
                          NpcManager.IsSameRumorTarget(betrayal, later) == false,
                        "Betrayal 같은 대상 = (가해자, 피해자, 사건 시각): 재수신 Duplicate, 다른 사건 시각은 다른 대상");
                }
                if (soldRumor != null)
                    npc.ReceiveRumor(witness, soldRumor, thiefId, realNow);

                // ⑧-7 대화 (D106): 도난 사연(범인 이름 / "누가"), 도둑 침묵, 배신 소문 토픽, 판매 소문 토픽 없음
                if (stolenEquipItem != null)
                {
                    DialogueTopic? namedTopic = FindTopic(equipVictimId, DialogueTopicType.TheftVictim);
                    string thiefName = npc.GetDisplayName(thiefId);
                    Check(namedTopic != null && namedTopic.BaseLine.Contains(thiefName) &&
                          namedTopic.BaseLine.Contains(stolenEquipItem.Table != null ? stolenEquipItem.Table.Name : "물건") &&
                          namedTopic.BaseLine.Contains("훔쳐 갔소"),
                        $"도난 사연(늦은 발각): 범인 이름으로 말함 — \"{namedTopic?.BaseLine}\"");
                }
                DialogueTopic? anonymousTopic = FindTopic(chainVictimId, DialogueTopicType.TheftVictim);
                Check(anonymousTopic != null && anonymousTopic.BaseLine.StartsWith("누가", StringComparison.Ordinal),
                    $"도난 사연(범인 모름): \"누가\" — \"{anonymousTopic?.BaseLine}\"");
                DialogueTopic? caughtTopic = FindTopic(caughtVictimId, DialogueTopicType.TheftVictim);
                Check(caughtTopic != null && caughtTopic.BaseLine.Contains("훔치려다 걸렸소") &&
                      caughtTopic.BaseLine.Contains(npc.GetDisplayName(thiefId)),
                    $"도난 사연(현장 적발): \"{caughtTopic?.BaseLine}\"");
                int pureThiefId = -1;
                for (int i = 10; i < extras.Count && pureThiefId < 0; i++)
                {
                    int id = extras[i];
                    if (id == expiryVictimId || id == deathVictimId || id == deadThiefId || id == witnessId)
                        continue;
                    if (CountTheftDeeds(sim.Npcs[id]) == 0)
                        pureThiefId = id;
                }
                if (pureThiefId < 0)
                    throw new InvalidOperationException("절도 Deed가 없는 검증용 NPC를 찾지 못했습니다.");
                int pureVictimId = extras[14] != pureThiefId ? extras[14] : extras[extras.Count - 1];
                ItemData pureItem = MakeCopy(template, 800);
                sim.Npcs[pureVictimId].EnsureSlotsInitialized();
                sim.Npcs[pureVictimId].Inventory[0] = pureItem;
                AdvanceGameTime(p4bTime + 30f);
                npc.ApplyTheft(new NpcTheftDecision
                {
                    ThiefId = pureThiefId, VictimId = pureVictimId, ItemSlot = 0, ItemInstanceId = pureItem.ItemInstanceId,
                    AmbitionMultiplier = 1f, VictimCandidateCount = 1, Detected = true,
                }, p4bTime + 30f);
                Check(CountDeeds(sim.Npcs[pureThiefId], NpcDeedKind.CaughtStealing) == 1 &&
                      FindTopic(pureThiefId, DialogueTopicType.TheftVictim) == null,
                    "도둑은 침묵: 도둑 쪽 Deed(CaughtStealing·TheftExposed)로는 도난 토픽이 생기지 않음");
                List<DialogueTopic> rumorTopics = new List<DialogueTopic>();
                DialogueTopicSelector.CollectAmbitionRumorTopics(witnessId, rumorTopics);
                DialogueTopic? betrayalTopic = null;
                bool soldTopic = false;
                for (int i = 0; i < rumorTopics.Count; i++)
                {
                    if (rumorTopics[i].Type == DialogueTopicType.BetrayalRumor)
                        betrayalTopic = rumorTopics[i];
                    if (rumorTopics[i].Rumor != null && rumorTopics[i].Rumor!.Kind == NpcRumorKind.ItemSold)
                        soldTopic = true;
                }
                Check(betrayalTopic != null && betrayalTopic.BaseLine.Contains(npc.GetDisplayName(thiefId)) &&
                      betrayalTopic.BaseLine.Contains(npc.GetDisplayName(caughtVictimId)) &&
                      betrayalTopic.BaseLine.Contains("훔치려다 걸렸다"),
                    $"배신 소문 토픽(제3자, 피해자에게 직접 들음): \"{betrayalTopic?.BaseLine}\"");
                Check(soldTopic == false && (soldRumor == null || HoldsRumorKind(witness, NpcRumorKind.ItemSold) == true),
                    "판매 소문(ItemSold)은 보유해도 대화 토픽이 없음 (검사 창 전용)");
                rumorTopics.Clear();
                DialogueTopicSelector.CollectAmbitionRumorTopics(caughtVictimId, rumorTopics);
                bool victimOwnBetrayal = false;
                for (int i = 0; i < rumorTopics.Count; i++)
                {
                    if (rumorTopics[i].Type == DialogueTopicType.BetrayalRumor && rumorTopics[i].Rumor != null &&
                        (rumorTopics[i].Rumor!.VictimNpcId == caughtVictimId || rumorTopics[i].Rumor!.SubjectNpcId == caughtVictimId))
                        victimOwnBetrayal = true;
                }
                Check(victimOwnBetrayal == false, "피해자 본인의 배신 소문은 소문 토픽에서 빠짐 (도난 사연이 말함)");
                NpcDialogueContext? witnessContext = NpcDialogueContextBuilder.Build(witnessId, default);
                int ambitionTopics = 0;
                if (witnessContext != null)
                {
                    List<DialogueTopic> collected = DialogueTopicSelector.Collect(witnessId, witnessContext, default);
                    for (int i = 0; i < collected.Count; i++)
                    {
                        DialogueTopicType type = collected[i].Type;
                        if (type == DialogueTopicType.DungeonRumor || type == DialogueTopicType.ItemOwnerRumor ||
                            type == DialogueTopicType.BetrayalRumor)
                            ambitionTopics++;
                    }
                }
                Check(witnessContext != null && ambitionTopics <= 1, $"대화 1회분 야망 소문 토픽 최대 1개 (D82): {ambitionTopics}개");

                // ⑧-8 검사 창 문구 (새 Deed 2종·판매/배신 소문) — "알 수 없는" 없음
                Type? inspectType = Type.GetType("ARPG.UI.UINpcInspect, Assembly-CSharp");
                if (inspectType == null)
                    Check(false, "검사 창 타입을 찾지 못함");
                else
                {
                    object inspect = System.Runtime.Serialization.FormatterServices.GetUninitializedObject(inspectType);
                    MethodInfo? history = inspectType.GetMethod("GetHistoryDescription", BindingFlags.NonPublic | BindingFlags.Instance);
                    MethodInfo? describe = inspectType.GetMethod("DescribeRumor", BindingFlags.NonPublic | BindingFlags.Instance);
                    FieldInfo? entityField = inspectType.GetField("_entityId", BindingFlags.NonPublic | BindingFlags.Instance);
                    entityField?.SetValue(inspect, equipVictimId);
                    StringBuilder texts = new StringBuilder();
                    bool textsOk = history != null && describe != null;
                    if (textsOk == true)
                    {
                        NpcSaveData discoveredVictim = stolenEquipItem != null ? equipVictim : bagVictim;
                        entityField?.SetValue(inspect, discoveredVictim.EntityId);
                        NpcDeedEntry? victimStory = discoveredVictim.PersonalStories?.TheftVictim;
                        if (victimStory?.Kind == NpcDeedKind.TheftDiscovered)
                            texts.Append((string)history!.Invoke(inspect, new object[] { victimStory })).Append(" | ");
                        entityField?.SetValue(inspect, thiefId);
                        for (int i = 0; i < thief.Deeds.Count; i++)
                        {
                            if (thief.Deeds[i].Kind == NpcDeedKind.TheftExposed)
                            {
                                texts.Append((string)history!.Invoke(inspect, new object[] { thief.Deeds[i] })).Append(" | ");
                                break;
                            }
                        }
                        if (betrayal != null)
                            texts.Append((string)describe!.Invoke(inspect, new object[] { betrayal })).Append(" | ");
                        if (soldRumor != null)
                            texts.Append((string)describe!.Invoke(inspect, new object[] { soldRumor })).Append(" | ");
                        string all = texts.ToString();
                        textsOk = all.Contains("알 수 없는") == false &&
                                  (all.Contains("였음을 알게 됨") == true || all.Contains("이었음을 알게 됨") == true);
                        textsOk &= all.Contains("들통남") && all.Contains("상점에 팔았다") && all.Contains("훔치려다 걸림");
                    }
                    Check(textsOk == true, $"검사 창 문구: {texts}");
                }
                thief.InitialPower = p4bInitialPower;
                npc.InvalidateNpcPower(thiefId);

                // Betrayal 사망 정리 (D72): 피해자가 죽으면 같은 마을 명단의 그 Betrayal 소문이 지워진다 (마지막에 — 피해자 상태를 바꾼다)
                if (betrayal != null && witness.VillageId == caughtVictim.VillageId)
                {
                    caughtVictim.Condition = CharacterConditions.Dead;
                    caughtVictim.Activity.Phase = NpcActivityPhase.Dead;
                    npc.PurgeRumorsOnNpcDeath(caughtVictimId, caughtVictim);
                    Check(HoldsRumorKind(witness, NpcRumorKind.Betrayal) == false,
                        "피해자 사망 → 같은 마을 명단의 그 Betrayal 소문 정리 (D72)");
                }

                Check(sim.DiscoveryTriggerFails == 0 && sim.DiscoveryAuditFailures.Count == 0 && sim.DiscoveryUnaudited == 0,
                    $"늦은 발각 독립 감사 실패 0 (장착 {sim.DiscoveryOk[0]}/{sim.DiscoveryCount[0]}, 판매 {sim.DiscoveryOk[1]}/{sim.DiscoveryCount[1]})");
                for (int i = 0; i < sim.DiscoveryAuditFailures.Count; i++)
                    log.AppendLine($"    ! {sim.DiscoveryAuditFailures[i]}");

                Check(sim.TheftAuditFailCount == 0 && sim.TheftEligibilityFails == 0 && sim.TheftThiefCooldownViolations == 0 &&
                      sim.TheftVillageCooldownViolations == 0,
                    $"진입점 경로 독립 감사 실패 0 (관찰 {sim.TheftObserved}건)");
                for (int i = 0; i < sim.TheftAuditFailures.Count; i++)
                    log.AppendLine($"    ! {sim.TheftAuditFailures[i]}");
            }
            catch (Exception exception)
            {
                error = exception.ToString();
            }
            finally
            {
                DetachRumorProbes();
                for (int i = swaps.Count - 1; i >= 0; i--)
                    swaps[i].Field.SetValue(swaps[i].Target, swaps[i].Original);
                elapsedField.SetValue(AR.s.Time, elapsed);
                accumulatorField.SetValue(AR.s.Time, accumulator);
                itemCounterField.SetValue(AR.s.Item, itemCounter);
                UnityEngine.Random.state = randomState;
            }

            string hashAfter = ComputeLiveStateHash();
            bool liveUnchanged = hashBefore == hashAfter;
            bool timeUnchanged = Equals(elapsedField.GetValue(AR.s.Time), elapsed) == true &&
                Equals(accumulatorField.GetValue(AR.s.Time), accumulator) == true;
            string result = $"[TheftRules] 실패 {failures}건, 라이브 상태 해시 전후 동일: {liveUnchanged}, 게임 시각 복원: {timeUnchanged}\n" + log +
                            (error != null ? "  ERROR " + error + "\n" : string.Empty);
            if (failures == 0 && liveUnchanged == true && timeUnchanged == true && error == null)
                Debug.Log(result);
            else
                Debug.LogError(result);
            return result;
        }

        private static bool HasTestClue(NpcSaveData victim, int itemInstanceId, int thiefId, float when)
        {
            for (int i = 0; i < victim.TheftClues.Count; i++)
            {
                NpcTheftClue clue = victim.TheftClues[i];
                if (clue.ItemInstanceId == itemInstanceId && clue.ThiefId == thiefId && clue.When == when)
                    return true;
            }
            return false;
        }

        /// <summary>검증 준비: 받는 쪽이 같은 대상 소문을 이미 가져 Duplicate가 되지 않게 지운다.</summary>
        private static void RemoveSameTargetRumors(NpcSaveData holder, NpcRumor rumor)
        {
            for (int i = holder.Rumors.Count - 1; i >= 0; i--)
            {
                if (NpcManager.IsSameRumorTarget(holder.Rumors[i], rumor) == true)
                    holder.Rumors.RemoveAt(i);
            }
        }

        private static bool HoldsRumorKind(NpcSaveData holder, NpcRumorKind kind)
        {
            for (int i = 0; i < holder.Rumors.Count; i++)
            {
                if (holder.Rumors[i].Kind == kind)
                    return true;
            }
            return false;
        }

        /// <summary>실제 대화 1회분(Collect)에서 그 종류의 토픽. 없으면 null.</summary>
        private static DialogueTopic? FindTopic(int npcId, DialogueTopicType type)
        {
            NpcDialogueContext? context = NpcDialogueContextBuilder.Build(npcId, default);
            if (context == null)
                return null;
            List<DialogueTopic> topics = DialogueTopicSelector.Collect(npcId, context, default);
            for (int i = 0; i < topics.Count; i++)
            {
                if (topics[i].Type == type)
                    return topics[i];
            }
            return null;
        }

        /// <summary>절도 검증용 성향: 탐욕·정직·호기심·용기만 주고 나머지는 0 (야망 도출 §4.2로 주/보조/없음을 만든다).</summary>
        private static global::ARPG.Component.NpcStatComponent MakeTheftTestStat(int greed, int honesty, int curiosity, int courage)
        {
            return new global::ARPG.Component.NpcStatComponent
            {
                Greed = greed,
                Honesty = honesty,
                Curiosity = curiosity,
                Courage = courage,
            };
        }

        /// <summary>복제본 관계 원장에서 a·b 사이의 모든 유형 관계를 지운다 (검증 준비용 — 양쪽 인덱스).</summary>
        private static void RemoveSimRelationsBetween(SimContext sim, int a, int b)
        {
            RemoveFromIndex(a);
            RemoveFromIndex(b);

            void RemoveFromIndex(int key)
            {
                if (sim.Relations.TryGetValue(key, out List<NpcRelationEntry>? list) == false)
                    return;
                for (int i = list.Count - 1; i >= 0; i--)
                {
                    NpcRelationEntry entry = list[i];
                    if ((entry.FromEntityId == a && entry.ToEntityId == b) || (entry.FromEntityId == b && entry.ToEntityId == a))
                        list.RemoveAt(i);
                }
            }
        }

        // ================= 라이브 상태 해시 =================

        /// <summary>
        /// 실행 전후 비교용 라이브 상태 해시: NPC 세이브 전부, 마을 데이터(명단·엔티티 ID 포함), 파티, 파티/순회 커서,
        /// 전투력 캐시·장착 지연 목록, NPC 호감도·유형 관계 내용(D107), 게임 시간, 아이템 인스턴스 카운터, UnityEngine.Random 상태,
        /// 교체한 필드의 참조 동일성.
        /// </summary>
        private static string ComputeLiveStateHash()
        {
            StringBuilder sb = new StringBuilder();
            List<int> ids = new List<int>(AR.s.Npc.SaveDataView.Keys);
            ids.Sort();
            for (int i = 0; i < ids.Count; i++)
                sb.Append(ids[i]).Append(':').Append(JsonConvert.SerializeObject(AR.s.Npc.SaveDataView[ids[i]],
                    JsonSettings.Default)).Append(':').Append(AR.s.Npc.SaveDataView[ids[i]].IsSpawning).Append('\n');

            IReadOnlyList<VillageData> villages = AR.s.Village.GetAllVillages();
            for (int i = 0; i < villages.Count; i++)
            {
                VillageData v = villages[i];
                sb.Append(JsonConvert.SerializeObject(v, JsonSettings.Default)).Append('|')
                  .Append(string.Join(",", v.NpcEntityIds)).Append('|').Append(v.EntityId).Append('\n');
            }

            object npc = AR.s.Npc;
            object village = AR.s.Village;
            string[] npcFields = { "_npcSaveDict", "_npcPowerCache", "_deferredPurchaseUpgradeItemIds", "_npcPartyDict",
                "_simulationPartyIds", "_simulationWorkCursor", "_nextPartyId", "_simulationNpcIds", "_simulationCursor", "_narrative",
                "_npcAffinity", "_npcRelations", "_goldFlowCounters", "_saveSnapshot", "_dirtyNpcIds", "_dirtyNpcOrder",
                "_snapshotFailedIds", "_saveShardGenerations", "_snapshotFlushCount", "_snapshotFlushTotalMs", "_snapshotFlushMaxMs",
                "_affinitySaveSnapshot", "_relationSaveSnapshot", "_partySaveSnapshot", "_populationSaveSnapshot" };
            for (int i = 0; i < npcFields.Length; i++)
            {
                object? value = GetField(npc, npcFields[i]).GetValue(npc);
                sb.Append(npcFields[i]).Append('=').Append(DescribeFieldValue(value)).Append('\n');
            }
            // D107: 호감도·유형 관계 내용 (참조 동일성은 위 줄, 내용은 Dictionary 열거 순서와 무관하게 정렬해 본다)
            sb.Append("snapshot=").Append(JsonConvert.SerializeObject(GetField(npc, "_saveSnapshot").GetValue(npc), JsonSettings.Default)).Append('\n');
            sb.Append("snapshotDirty=").Append(JsonConvert.SerializeObject(GetField(npc, "_dirtyNpcOrder").GetValue(npc), JsonSettings.Default)).Append('\n');
            sb.Append("shardGenerations=").Append(JsonConvert.SerializeObject(GetField(npc, "_saveShardGenerations").GetValue(npc), JsonSettings.Default)).Append('\n');
            sb.Append("simulationSnapshots=").Append(AR.s.Npc.DescribeSimulationSaveSnapshots()).Append('\n');
            sb.Append("worldSimSnapshot=").Append(AR.s.History.DescribeSaveSnapshot()).Append('\n');
            sb.Append("worldSimSnapshotRef=").Append(DescribeFieldValue(GetField(AR.s.History, "_settlementSaveSnapshot").GetValue(AR.s.History))).Append('\n');
            sb.Append("worldSim=").Append(JsonConvert.SerializeObject(AR.s.Data.WorldSim, JsonSettings.Default)).Append('\n');
            sb.Append("population=").Append(DescribeFieldValue(AR.s.Data.NpcPopulation)).Append('|')
                .Append(JsonConvert.SerializeObject(AR.s.Data.NpcPopulation, JsonSettings.Default)).Append('\n');
            sb.Append("affinity=").Append(DescribeAffinity(GetField(npc, "_npcAffinity").GetValue(npc))).Append('\n');
            sb.Append("relations=").Append(DescribeRelations(GetField(npc, "_npcRelations").GetValue(npc))).Append('\n');
            string[] villageFields = { "_villages", "_villageList", "_saveSnapshot", "_dirtyVillageIds", "_dirtyVillageOrder",
                "_snapshotFailedIds", "_snapshotFlushCount", "_snapshotFlushTotalMs", "_snapshotFlushMaxMs", "_snapshotDirtyObserver", "_saveSnapshotGeneration" };
            sb.Append("villageSnapshot=").Append(JsonConvert.SerializeObject(GetField(village, "_saveSnapshot").GetValue(village), JsonSettings.Default)).Append('\n');
            sb.Append("villageSnapshotDirty=").Append(JsonConvert.SerializeObject(GetField(village, "_dirtyVillageOrder").GetValue(village), JsonSettings.Default)).Append('\n');
            for (int i = 0; i < villageFields.Length; i++)
            {
                object? value = GetField(village, villageFields[i]).GetValue(village);
                sb.Append(villageFields[i]).Append('=').Append(DescribeFieldValue(value)).Append('\n');
            }

            // 던전 저장 상태와 DungeonManager의 슬롯 색인·NPC 완료 플래그 (P2a: NPC 완료가 슬롯 세대를 바꾸므로)
            sb.Append("dungeons=").Append(JsonConvert.SerializeObject(AR.s.Data.Dungeons, JsonSettings.Default)).Append('\n');
            object dungeon = AR.s.Dungeon;
            string[] dungeonFields = { "_villageSlots", "_entranceIndexDirty", "_npcCompletionRevision",
                "_savedNpcCompletionRevision", "_busy" };
            for (int i = 0; i < dungeonFields.Length; i++)
            {
                object? value = GetField(dungeon, dungeonFields[i]).GetValue(dungeon);
                sb.Append(dungeonFields[i]).Append('=').Append(DescribeFieldValue(value)).Append('\n');
            }
            object worldData = GetField(AR.s.Data, "_worldData").GetValue(AR.s.Data)!;
            sb.Append("worldData.Dungeons=").Append(DescribeFieldValue(GetField(worldData, "Dungeons").GetValue(worldData)))
              .Append('\n');
            // GOLD_BALANCE: 세계 물가 지수 상태 (참조 + 내용)
            sb.Append("worldData.GoldPrice=").Append(DescribeFieldValue(GetField(worldData, "GoldPrice").GetValue(worldData)))
              .Append('|').Append(JsonConvert.SerializeObject(AR.s.Data.GoldPrice, JsonSettings.Default)).Append('\n');

            sb.Append("time=").Append(GetField(AR.s.Time, "_elapsedGameTime").GetValue(AR.s.Time)).Append('/')
              .Append(GetField(AR.s.Time, "_accumulator").GetValue(AR.s.Time)).Append('\n');
            sb.Append("itemCounter=").Append(AR.s.Item.NextItemInstanceId).Append('\n');
            sb.Append("random=").Append(JsonUtility.ToJson(UnityEngine.Random.state)).Append('\n');

            using SHA256 sha = SHA256.Create();
            byte[] hash = sha.ComputeHash(Encoding.UTF8.GetBytes(sb.ToString()));
            StringBuilder hex = new StringBuilder(hash.Length * 2);
            for (int i = 0; i < hash.Length; i++)
                hex.Append(hash[i].ToString("x2", CultureInfo.InvariantCulture));
            return hex.ToString();
        }

        /// <summary>호감도 내용: (Lo, Hi) 순으로 정렬한 "Lo:Hi=값" 목록.</summary>
        private static string DescribeAffinity(object? value)
        {
            Dictionary<NpcPairKey, NpcAffinityEntry>? affinity = value as Dictionary<NpcPairKey, NpcAffinityEntry>;
            if (affinity == null)
                return "null";
            List<KeyValuePair<NpcPairKey, NpcAffinityEntry>> pairs = new List<KeyValuePair<NpcPairKey, NpcAffinityEntry>>(affinity);
            pairs.Sort((a, b) => a.Key.Lo != b.Key.Lo ? a.Key.Lo.CompareTo(b.Key.Lo) : a.Key.Hi.CompareTo(b.Key.Hi));
            StringBuilder sb = new StringBuilder(pairs.Count * 16);
            for (int i = 0; i < pairs.Count; i++)
                sb.Append(pairs[i].Key.Lo).Append(':').Append(pairs[i].Key.Hi).Append('=').Append(JsonConvert.SerializeObject(pairs[i].Value, JsonSettings.Default)).Append(',');
            return sb.ToString();
        }

        /// <summary>유형 관계 내용: 키(EntityId) 순으로, 각 인덱스 목록은 원래 순서대로 "From>To:Type:OriginEventId".</summary>
        private static string DescribeRelations(object? value)
        {
            Dictionary<int, List<NpcRelationEntry>>? relations = value as Dictionary<int, List<NpcRelationEntry>>;
            if (relations == null)
                return "null";
            List<int> keys = new List<int>(relations.Keys);
            keys.Sort();
            StringBuilder sb = new StringBuilder(keys.Count * 32);
            for (int i = 0; i < keys.Count; i++)
            {
                sb.Append(keys[i]).Append('[');
                List<NpcRelationEntry> list = relations[keys[i]];
                for (int j = 0; j < list.Count; j++)
                {
                    NpcRelationEntry entry = list[j];
                    sb.Append(entry.FromEntityId).Append('>').Append(entry.ToEntityId).Append(':').Append((int)entry.Type)
                      .Append(':').Append(entry.OriginEventId).Append(',');
                }
                sb.Append(']');
            }
            return sb.ToString();
        }

        private static string DescribeFieldValue(object? value)
        {
            if (value == null)
                return "null";
            if (value is int || value is float || value is double || value is long || value is bool)
                return Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty;

            // 참조 동일성 + 내용 요약. 파티·캐시는 JSON으로 내용까지 본다.
            int identity = System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(value);
            string content = value is System.Collections.ICollection collection ? collection.Count.ToString() : string.Empty;
            if (value is Dictionary<int, NpcPartyState> || value is Dictionary<int, float> ||
                value is Dictionary<int, List<int>> || value is List<int> || value is HashSet<int>)
                content = JsonConvert.SerializeObject(value, JsonSettings.Default);
            return $"#{identity}:{content}";
        }
    }
}
