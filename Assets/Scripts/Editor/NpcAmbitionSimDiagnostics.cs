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
    /// - 활동 선택(SelectNextActivity)·Trade 기회·거래(TradeAtShop)·치료·전리품·자동 장착·파티 구성은 실제 매니저 코드를 그대로 호출한다.
    /// - System_NpcSim의 상태 전이 루프는 여기서 같은 순서로 재현한다 (1게임시간 해상도). 사망 정리(관계·연대기·Deed),
    ///   화면 안 연출, 전역 인구 보충(TryReplenishGlobalPopulation), 마을 건설 소비는 시뮬레이션하지 않는다.
    ///   마을 인구 자원 생산(VillageResourceSimulation.AdvanceWholeHours)은 모든 마을에 시간당 1회 적용한다.
    /// - 시뮬레이션은 한 프레임 안에서 끝나므로 그 사이 다른 시스템·저장이 돌지 않는다. 끝나면 모든 필드를 되돌리고
    ///   실행 전후 라이브 상태 해시가 같은지 검증한다. 시간·아이템 인스턴스 카운터·UnityEngine.Random 상태도 되돌린다.
    /// 출력: {프로젝트}/Logs/NpcAmbitionSim/ (.gitignore의 /Logs/ — git에 잡히지 않음)
    /// </summary>
    public static class NpcAmbitionSimDiagnostics
    {
        private const float PowerEpsilon = 0.0001f;
        private const float CowardFraction = 0.10f;
        private const int MaxExampleCases = 5;
        private const int MaxPhaseTransitionsPerNpc = 4;
        private const int DeedKindCount = (int)NpcDeedKind.DungeonCleared + 1;
        private const int MaxTopicExamplesPerKind = 4;

        [MenuItem("ARPG/Npc/Diagnostics/Ambition Sim (30 days)", false, 431)]
        public static void Run30Days() => Run(30);

        [MenuItem("ARPG/Npc/Diagnostics/Ambition Sim (7 days)", false, 432)]
        public static void Run7Days() => Run(7);

        [MenuItem("ARPG/Npc/Diagnostics/Verify Rumor Rules", false, 434)]
        public static void RunVerifyRumorRules() => VerifyRumorRules();

        // 전파 곡선 측정 시점 (사건 뒤 게임시간)
        private static readonly float[] PropagationCheckpointHours = { 12f, 24f, 48f, 72f };

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

            // 소문 (P2b)
            public NpcRumorCounters RumorCounters = new NpcRumorCounters();
            public long RumorAttemptsAtDayStart;
            public long RumorTransfersAtDayStart;
            public long ConfirmationsAtDayStart;
            public readonly Dictionary<string, RumorEventTrack> RumorEventIndex = new();
            public readonly List<RumorEventTrack> RumorEvents = new();
            // [분류, 측정 시점] → 비율 합·최대·표본 수
            public readonly double[,] PropagationSum = new double[3, 4];
            public readonly double[,] PropagationMax = new double[3, 4];
            public readonly int[,] PropagationSamples = new int[3, 4];
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
            public TopicAudit Topics = new TopicAudit();
        }

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
            days = Mathf.Clamp(days, 1, 120);

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
            List<int> cowardIds = new List<int>();
            int cowardCourageMax = 0;
            Dictionary<NpcAmbitionKind, int> ambitionCounts = new Dictionary<NpcAmbitionKind, int>();

            try
            {
                // 복제는 로그 핸들러 교체 전에 한다 — 복제 실패가 콘솔에 보이도록.
                BuildClones(sim);
                if (probePlayerEntered == true)
                    InjectPlayerEnteredProbe(sim);
                sim.WorldSeed = AR.s.Map.mapSeed;
                CollectStartPopulation(sim, cowardIds, out cowardCourageMax, ambitionCounts);

                sim.NpcJsonBytesStart = MeasureNpcJsonBytes(sim, out _, out _);
                sim.ProbePlayerEntered = probePlayerEntered;
                MeasureDeedJsonBytes(sim, sim.DeedBytesStart, sim.DeedCountStart);
                Debug.unityLogger.logHandler = capture;
                SwapInClones(sim, swaps);
                AttachRumorProbes(sim, swaps);

                rows.Add(RecordDay(sim, 0, startGameTime));
                sim.Today = new DayRow();
                int totalHours = days * 24;
                for (int hour = 1; hour <= totalHours; hour++)
                {
                    float now = startGameTime + hour;
                    elapsedField.SetValue(AR.s.Time, now);
                    accumulatorField.SetValue(AR.s.Time, 0f);

                    AdvanceVillageProduction(sim);
                    AdvanceParties(sim, now);
                    for (int i = 0; i < sim.NpcIds.Count; i++)
                        AdvanceNpc(sim, sim.NpcIds[i], now);
                    UpdateRumorPropagation(sim, now);

                    if (hour % 24 == 0)
                    {
                        rows.Add(RecordDay(sim, hour / 24, now));
                        sim.Today = new DayRow();
                    }
                }

                // P2c: 복제본이 끼워진 채로(시각 = 시뮬 마지막) 대화 토픽을 실제 selector 코드로 만들어 검증한다.
                AuditDialogueTopics(sim);
            }
            catch (Exception exception)
            {
                failure = exception.ToString();
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
                Debug.unityLogger.logHandler = originalLogHandler;
            }
            watch.Stop();

            string hashAfter = ComputeLiveStateHash();
            bool liveUnchanged = hashBefore == hashAfter;

            if (failure != null)
            {
                string failed = $"[NpcAmbitionSim] 실패 (라이브 상태 복원 {(liveUnchanged ? "확인" : "불일치!")}): {failure}";
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
        }

        private static void SwapInClones(SimContext sim, List<FieldSwap> swaps)
        {
            object npc = AR.s.Npc;
            object village = AR.s.Village;
            List<int> partyIds = new List<int>(sim.Parties.Keys);
            partyIds.Sort();
            object? nextPartyId = GetField(npc, "_nextPartyId").GetValue(npc);

            Swap(swaps, npc, "_npcSaveDict", sim.Npcs);
            Swap(swaps, npc, "_npcPowerCache", new Dictionary<int, float>());
            Swap(swaps, npc, "_deferredLootUpgradeItemIds", new Dictionary<int, List<int>>());
            Swap(swaps, npc, "_npcPartyDict", sim.Parties);
            Swap(swaps, npc, "_simulationPartyIds", partyIds);
            Swap(swaps, npc, "_simulationWorkCursor", 0);
            Swap(swaps, npc, "_nextPartyId", nextPartyId);
            // P2c: 시뮬 중 Deed·소문 변경이 라이브 대화 캐시를 지우지 않게 (InvalidateDialogues) 빈 서사 런타임으로 바꿔 끼운다.
            Swap(swaps, npc, "_narrative", new NpcNarrativeRuntime());
            Swap(swaps, village, "_villages", sim.Villages);
            Swap(swaps, village, "_villageList", sim.VillageList);

            // 던전: DataManager의 WorldData.Dungeons와 DungeonManager의 마을 슬롯 색인·NPC 완료 런타임 플래그.
            // 시뮬은 한 프레임 안의 동기 실행이라 진행 중인 입구 배치 저장(_busy)이 끼어들 수 없다 — 그동안만 false로 본다.
            object worldData = GetField(AR.s.Data, "_worldData").GetValue(AR.s.Data)!;
            Swap(swaps, worldData, "Dungeons", sim.Dungeons);
            object dungeon = AR.s.Dungeon;
            Swap(swaps, dungeon, "_villageSlots", sim.DungeonVillageSlots);
            Swap(swaps, dungeon, "_entranceIndexDirty", false);
            Swap(swaps, dungeon, "_npcCompletionRevision", 0L);
            Swap(swaps, dungeon, "_savedNpcCompletionRevision", 0L);
            Swap(swaps, dungeon, "_busy", false);
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
                VillageResourceSimulation.AdvanceWholeHours(village, 1);
                int produced = VillageResourceSimulation.GetAmount(village, GlobalEnum.ItemType.Gold) - goldBefore;
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
                    StartNextActivity(sim, saveData, entityId, decisionTime);
                    continue;
                }

                if (activity.PhaseEndGameTime > now)
                    return;

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
                    // System_NpcSim과 같이 Rest 종료 시점에 소문 교환 (D74). RNG는 Rest 시작 시각 기준.
                    AR.s.Npc.ExchangeRumorsOnRestEnd(entityId, saveData, activity.PhaseStartedGameTime, transitionTime,
                        sim.WorldSeed);
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
                SetIdle(saveData, transitionTime);
            }
        }

        private static void SetIdle(NpcSaveData saveData, float gameTime)
        {
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

            if (kind == NpcActivityKind.Hunt && AR.s.Npc.TryCreateHuntParty(entityId, out NpcPartyState? party) &&
                party != null)
            {
                StartPartyHunt(party, startGameTime);
                return;
            }

            // DungeonDelve: System_NpcSim과 같은 순서 — 파티 우선, 아니면 솔로. 대상 던전은 실제 매니저 코드로 고른다.
            string delveDungeonId = string.Empty;
            if (kind == NpcActivityKind.DungeonDelve)
            {
                if (AR.s.Npc.TryCreateHuntParty(entityId, out NpcPartyState? delveParty, NpcActivityKind.DungeonDelve) &&
                    delveParty != null)
                {
                    StartPartyDelve(sim, delveParty, startGameTime);
                    return;
                }
                if (AR.s.Npc.TryPrepareDelveTarget(entityId, entityId, saveData.VillageId, saveData.Position,
                        startGameTime, sim.WorldSeed, out delveDungeonId, out _, out bool fromRumor) == false)
                    kind = NpcActivityKind.Rest;
                else if (fromRumor == true)
                    sim.ConfirmationSolo.Add(entityId);
            }

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
                    member.Activity.Kind = NpcActivityKind.None;
                    member.Activity.Phase = NpcActivityPhase.Idle;
                    member.Activity.TargetKey = string.Empty;
                }
                AR.s.Npc.DissolveParty(party.PartyId);
                return;
            }

            if (fromRumor == true)
                sim.ConfirmationParties.Add(party.PartyId);
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
                CountTreatment(sim, saveData, report.Treated[0]);
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
                    CountTreatment(sim, member, report.Treated[i]);
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

            if (outcome == NpcHuntOutcome.Dead)
            {
                MarkDead(sim, saveData, entityId, outcomeGameTime);
                return;
            }
            if (outcome == NpcHuntOutcome.Wounded)
            {
                activity.Phase = NpcActivityPhase.Wounded;
                float recovery = AR.s.Npc.ResolveWoundedTreatment(entityId, saveData, entityId, seedGameTime,
                    sim.WorldSeed, NpcActivityResolver.TreatmentSalt, out bool treated);
                activity.PhaseEndGameTime = outcomeGameTime + recovery;
                CountTreatment(sim, saveData, treated);
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
            }

            int foodYield = successCount > 0
                ? NpcActivityResolver.GetPartyHuntFoodYield(partyPower, party.PartyId, seedGameTime, sim.WorldSeed)
                : 0;

            for (int i = 0; i < members.Count; i++)
            {
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
                    activity.Phase = NpcActivityPhase.Wounded;
                    activity.Kind = NpcActivityKind.None;
                    float recovery = AR.s.Npc.ResolveWoundedTreatment(memberIds[i], member, party.PartyId, seedGameTime,
                        sim.WorldSeed, NpcActivityResolver.PartyTreatmentSaltBase + i, out bool treated);
                    activity.PhaseEndGameTime = outcomeGameTime + recovery;
                    CountTreatment(sim, member, treated);
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
            int produced = VillageResourceSimulation.Produce(village, GlobalEnum.ItemType.Food, amount);
            sim.Today.FoodYieldRaw += amount;
            sim.Today.FoodInflow += produced;
            if (sim.VillageStats.TryGetValue(villageId, out VillageStats? stats))
                stats.FoodInflow += produced;
        }

        /// <summary>사망 정리 축약판: 순회·마을 명단에서 빼고 소지 Gold는 소멸 (D46). 관계·연대기·Deed는 다루지 않는다.</summary>
        private static void MarkDead(SimContext sim, NpcSaveData saveData, int entityId, float when)
        {
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
                          "hunt_outcomes,hunt_success_pct,delve_outcomes,delve_success_pct,delve_completion_rolls,delve_dungeon_changed_failures,npc_completions,wounded,treatments,deaths,expected_deaths,gold_burned,world_gold,village_gold,npc_gold," +
                          "population_gold_produced,world_gold_conservation_error,food_inflow,food_yield_raw,trades_executed," +
                          "npc_sold,npc_sold_gold,npc_sell_rejected,npc_bought,npc_bought_gold," +
                          "rumor_exchange_attempts,rumor_transfers,rumors_held,rumor_holders,promising_holders,rumor_stored_invalid,confirmation_delves");
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
                    cumulativeBurned += r.GoldBurned;
                    cumulativePopulation += r.PopulationGold;
                }
                int selections = r.SelectRest + r.SelectHunt + r.SelectTrade + r.SelectDelve;
                long world = r.VillageGold + r.NpcGold;
                // 닫힌 경제 검산: 오늘 세계 Gold = 시작 + 인구 생산 누계 − 사망 소멸 누계. 0이 아니면 새는 경로가 있다.
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
                  .Append(r.ConfirmationDelves).AppendLine();
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
            sb.AppendLine($"개인 상한 초과 NPC(일별 최대): {maxCapExceed} | 최대 전투력/상한 비율: {maxCapRatio:F4}");
            sb.AppendLine($"활동 선택 {selections}회: Rest {Pct(rest, selections):F2}% / Hunt {Pct(hunt, selections):F2}% / " +
                          $"Trade {Pct(trade, selections):F2}% / Delve {Pct(delve, selections):F2}%");
            sb.AppendLine($"겁 많은 NPC(용기 하위 {CowardFraction:P0}, {cowardIds.Count}명, 용기 ≤ {cowardCourageMax}): " +
                          $"Rest {Pct(cowardRest, cowardSelections):F2}% / Hunt {Pct(cowardHunt, cowardSelections):F2}% / " +
                          $"Trade {Pct(cowardTrade, cowardSelections):F2}% / Delve {Pct(cowardDelve, cowardSelections):F2}%");
            sb.AppendLine($"Hunt 판정 {huntOutcomes}건 성공률 {Pct(huntSuccess, huntOutcomes):F2}% | " +
                          $"Delve 판정 {delveOutcomes}건(파티원 개인 포함) 성공률 {Pct(delveSuccess, delveOutcomes):F2}% | 치료 {treatments}건");
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
                cumulativeBurned += rows[i].GoldBurned;
                long error = rows[i].VillageGold + rows[i].NpcGold - (baseWorld + cumulativePopulation - cumulativeBurned);
                if (Math.Abs(error) > Math.Abs(maxConservationError))
                    maxConservationError = error;
            }
            sb.AppendLine($"닫힌 경제 검산 오차(일별 최대): {maxConservationError}");
            AppendRumorSummary(sb, sim, rows, days);
            AppendTopicSummary(sb, sim, days);

            // ---- P2 완료 기준 체크리스트 (§13, §10.1 기조 — 30일 실행 기준) ----
            TopicAudit ta = sim.Topics;
            long rumorTopics = ta.DungeonTopics + ta.ItemTopics;
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
            Item(huntPct >= 38f && huntPct <= 48f, "Hunt 선택 비중 43% ±5%p", $"{huntPct:F2}%");
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
            int[] examplesPerKind = new int[3];
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
                int dungeonCount = 0, itemCount = 0;
                DialogueTopic? boast = null;
                for (int t = 0; t < topics.Count; t++)
                {
                    if (topics[t].Type == DialogueTopicType.DungeonRumor)
                        dungeonCount++;
                    else if (topics[t].Type == DialogueTopicType.ItemOwnerRumor)
                        itemCount++;
                    else if (topics[t].Type == DialogueTopicType.Boast)
                        boast = topics[t];
                }
                if (dungeonCount > 0)
                    a.CollectDungeonTopicNpcs++;
                if (itemCount > 0)
                    a.CollectItemTopicNpcs++;
                if (dungeonCount + itemCount > 1)
                    a.CollectMultiplePerType++;

                bool hasNotable = false, hasCleared = false;
                for (int d = 0; d < save.Deeds.Count; d++)
                {
                    NpcDeedEntry deed = save.Deeds[d];
                    if (deed == null)
                        continue;
                    hasNotable |= deed.Kind == NpcDeedKind.NotableItemAcquired;
                    hasCleared |= deed.Kind == NpcDeedKind.DungeonCleared;
                }
                if (hasNotable == true)
                    a.NotableDeedNpcs++;
                if (hasCleared == true)
                    a.ClearedDeedNpcs++;
                if (boast != null && boast.CacheVariant >= 0 && boast.CacheVariant < save.Deeds.Count)
                {
                    NpcDeedKind kind = save.Deeds[boast.CacheVariant].Kind;
                    if (kind == NpcDeedKind.NotableItemAcquired)
                        a.BoastItemNpcs++;
                    else if (kind == NpcDeedKind.DungeonCleared)
                        a.BoastClearedNpcs++;
                    if (a.BoastExamples.Count < MaxTopicExamplesPerKind * 2 &&
                        (kind == NpcDeedKind.DungeonCleared || a.BoastExamples.Count < MaxTopicExamplesPerKind))
                        a.BoastExamples.Add($"[{kind}] {ctx.NpcName}(#{npcId}): {boast.BaseLine}");
                }

                // 레퍼토리 포함률 (Select와 같은 티어 필터 → 실제 BuildRepertoire)
                if (buildRepertoire == null || (dungeonCount + itemCount == 0 && boast == null))
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
                    bool rumorIn = false, boastIn = false;
                    for (int t = 0; t < repertoire.Count; t++)
                    {
                        rumorIn |= repertoire[t].Type == DialogueTopicType.DungeonRumor ||
                                   repertoire[t].Type == DialogueTopicType.ItemOwnerRumor;
                        boastIn |= repertoire[t].Type == DialogueTopicType.Boast;
                    }
                    if (dungeonCount + itemCount > 0)
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

        /// <summary>Deed 종류별 JSON 크기·개수 (세이브와 같은 JsonSettings, 배열 쉼표 포함).</summary>
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
            long topics = a.DungeonTopics + a.ItemTopics;
            sb.AppendLine($"소문 토픽 {topics}개: 던전 {a.DungeonTopics}(그중 완료 새 세대 {a.ClearedTopics}) / 장비 소유 {a.ItemTopics}");
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
                          $"야망 소문 토픽 2개 이상 {a.CollectMultiplePerType}명(0이어야 함 — 대화 1회분 최대 1개)");
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
            sb.AppendLine($"Deed JSON: 시작 {totalStart / 1024f:F0}KB → 끝 {totalEnd / 1024f:F0}KB (+{(totalEnd - totalStart) / 1024f:F0}KB) | " +
                          $"NotableItemAcquired {sim.DeedCountStart[notable]}→{countEnd[notable]}건 " +
                          $"(+{(bytesEnd[notable] - sim.DeedBytesStart[notable]) / 1024f:F1}KB, 1건 평균 " +
                          $"{(countEnd[notable] > 0 ? bytesEnd[notable] / (float)countEnd[notable] : 0f):F0}B) | " +
                          $"DungeonCleared {sim.DeedCountStart[cleared]}→{countEnd[cleared]}건 " +
                          $"(+{(bytesEnd[cleared] - sim.DeedBytesStart[cleared]) / 1024f:F1}KB) | " +
                          $"Wounded {sim.DeedCountStart[wounded]}→{countEnd[wounded]}건 (+{(bytesEnd[wounded] - sim.DeedBytesStart[wounded]) / 1024f:F0}KB)");
        }

        // ================= 소문 (P2b) =================

        private static void AttachRumorProbes(SimContext sim, List<FieldSwap> swaps)
        {
            Swap(swaps, AR.s.Npc, "_rumorCounters", sim.RumorCounters);
            AR.s.Npc.RumorExposureAudit = rumor =>
            {
                bool valid = AuditRumorValidity(sim, rumor, out _);
                if (valid == false)
                    sim.AuditExposedFailures++;
                return valid;
            };
            AR.s.Npc.RumorCreatedObserver = (holderId, rumor) => TrackRumorEvent(sim, holderId, rumor);
        }

        private static void DetachRumorProbes()
        {
            if (AR.s?.Npc == null)
                return;
            AR.s.Npc.RumorExposureAudit = null;
            AR.s.Npc.RumorCreatedObserver = null;
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
                         rumor.SubjectNpcId + ":" + rumor.EquipSlot + ":" + rumor.ItemInstanceId + ":" +
                         rumor.EventGameTime.ToString("R", CultureInfo.InvariantCulture);
            if (sim.RumorEventIndex.ContainsKey(key) == true)
                return;   // 파티원 여럿이 같은 사건의 직접 소문을 받는다 — 사건 1건으로 센다
            RumorEventTrack track = new RumorEventTrack
            {
                Template = rumor.Clone(),
                VillageId = villageId,
                Category = rumor.Kind == NpcRumorKind.ItemOwner ? 2 : rumor.Cleared == true ? 1 : 0,
            };
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

        private static void AppendRumorSummary(StringBuilder sb, SimContext sim, List<DayRow> rows, int days)
        {
            NpcRumorCounters c = sim.RumorCounters;
            sb.AppendLine("---- 소문 (P2b) ----");
            sb.AppendLine($"생성(직접): 원정 성공 {c.CreatedDelve} / 완료 새 세대 {c.CreatedCleared} / 장비 소유 {c.CreatedItemOwner} " +
                          $"| 추적 사건 {sim.RumorEvents.Count}건");
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
            long held = 0, dungeonHeld = 0, itemHeld = 0;
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
                    else
                        itemHeld++;
                    hops[Mathf.Clamp(rumor.Hops, 0, 3)]++;
                }
            }
            sb.Append($"보유(마지막 날): NPC당 평균 {(living > 0 ? held / (float)living : 0f):F2}개 (던전 {dungeonHeld} / 장비 {itemHeld}) | 분포 0~8:");
            for (int k = 0; k < histogram.Length; k++)
                sb.Append(' ').Append(Pct(histogram[k], living).ToString("F1", CultureInfo.InvariantCulture)).Append('%');
            sb.AppendLine();
            sb.AppendLine($"  보유 Hops: 0={Pct(hops[0], held):F1}% 1={Pct(hops[1], held):F1}% 2={Pct(hops[2], held):F1}% 3+={Pct(hops[3], held):F1}%");

            string[] categoryNames = { "원정 성공", "완료 새 세대", "장비 소유" };
            for (int cat = 0; cat < 3; cat++)
            {
                sb.Append($"전파 곡선 [{categoryNames[cat]}] (마을 생존 NPC 중 아는 비율 평균/최대):");
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

        // ================= 라이브 상태 해시 =================

        /// <summary>
        /// 실행 전후 비교용 라이브 상태 해시: NPC 세이브 전부, 마을 데이터(명단·엔티티 ID 포함), 파티, 파티/순회 커서,
        /// 전투력 캐시·장착 지연 목록, 게임 시간, 아이템 인스턴스 카운터, UnityEngine.Random 상태, 교체한 필드의 참조 동일성.
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
            string[] npcFields = { "_npcSaveDict", "_npcPowerCache", "_deferredLootUpgradeItemIds", "_npcPartyDict",
                "_simulationPartyIds", "_simulationWorkCursor", "_nextPartyId", "_simulationNpcIds", "_simulationCursor", "_narrative" };
            for (int i = 0; i < npcFields.Length; i++)
            {
                object? value = GetField(npc, npcFields[i]).GetValue(npc);
                sb.Append(npcFields[i]).Append('=').Append(DescribeFieldValue(value)).Append('\n');
            }
            string[] villageFields = { "_villages", "_villageList" };
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

        private static string DescribeFieldValue(object? value)
        {
            if (value == null)
                return "null";
            if (value is int || value is float || value is long || value is bool)
                return Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty;

            // 참조 동일성 + 내용 요약. 파티·캐시는 JSON으로 내용까지 본다.
            int identity = System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(value);
            string content = value is System.Collections.ICollection collection ? collection.Count.ToString() : string.Empty;
            if (value is Dictionary<int, NpcPartyState> || value is Dictionary<int, float> ||
                value is Dictionary<int, List<int>> || value is List<int>)
                content = JsonConvert.SerializeObject(value, JsonSettings.Default);
            return $"#{identity}:{content}";
        }
    }
}
