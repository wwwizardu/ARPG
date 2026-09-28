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
using ARPG.Npc;
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

        [MenuItem("ARPG/Npc/Diagnostics/Ambition Sim (30 days)", false, 431)]
        public static void Run30Days() => Run(30);

        [MenuItem("ARPG/Npc/Diagnostics/Ambition Sim (7 days)", false, 432)]
        public static void Run7Days() => Run(7);

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
            public int HuntOutcomes;
            public int HuntSuccess;
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
        }

        private sealed class NpcStats
        {
            public int Rest;
            public int Hunt;
            public int Trade;
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
            public int WorldSeed;
            public DayRow Today = new DayRow();
            public long TotalGoldBurned;
            public long TotalPopulationGold;
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
        public static string Run(int days)
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
                sim.WorldSeed = AR.s.Map.mapSeed;
                CollectStartPopulation(sim, cowardIds, out cowardCourageMax, ambitionCounts);

                Debug.unityLogger.logHandler = capture;
                SwapInClones(sim, swaps);

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

                    if (hour % 24 == 0)
                    {
                        rows.Add(RecordDay(sim, hour / 24, now));
                        sim.Today = new DayRow();
                    }
                }
            }
            catch (Exception exception)
            {
                failure = exception.ToString();
            }
            finally
            {
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

        // ================= 복제 · 교체 =================

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
            Swap(swaps, village, "_villages", sim.Villages);
            Swap(swaps, village, "_villageList", sim.VillageList);
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
            FieldInfo? field = target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
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
                if (activity.Kind != NpcActivityKind.Hunt || activity.Phase != NpcActivityPhase.Venturing)
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
            NpcActivityKind kind = reserved == true
                ? NpcActivityKind.Rest
                : AR.s.Npc.SelectNextActivity(entityId, saveData, startGameTime, sim.WorldSeed);

            // 선택 비중은 효용 선택 결과만 센다 (동행 예약 강제 Rest와 파티 모집으로 끌려간 경우 제외).
            if (reserved == false)
            {
                NpcStats stats = sim.NpcStats[entityId];
                if (kind == NpcActivityKind.Hunt) { sim.Today.SelectHunt++; stats.Hunt++; }
                else if (kind == NpcActivityKind.Trade) { sim.Today.SelectTrade++; stats.Trade++; }
                else { sim.Today.SelectRest++; stats.Rest++; }
            }

            if (kind == NpcActivityKind.Hunt && AR.s.Npc.TryCreateHuntParty(entityId, out NpcPartyState? party) &&
                party != null)
            {
                StartPartyHunt(party, startGameTime);
                return;
            }

            NpcActivityState activity = saveData.Activity;
            activity.Kind = kind;
            activity.Phase = kind == NpcActivityKind.Hunt ? NpcActivityPhase.Venturing : NpcActivityPhase.Idle;
            activity.TargetKey = kind == NpcActivityKind.Hunt ? "hunt" : kind == NpcActivityKind.Trade ? "trade" : "rest";
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
                          "upgrades_loot,upgrades_trade,select_rest,select_hunt,select_trade,rest_pct,hunt_pct,trade_pct," +
                          "hunt_outcomes,hunt_success_pct,wounded,treatments,deaths,expected_deaths,gold_burned,world_gold,village_gold,npc_gold," +
                          "population_gold_produced,world_gold_conservation_error,food_inflow,food_yield_raw,trades_executed," +
                          "npc_sold,npc_sold_gold,npc_sell_rejected,npc_bought,npc_bought_gold");
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
                int selections = r.SelectRest + r.SelectHunt + r.SelectTrade;
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
                  .Append(r.SelectRest).Append(',')
                  .Append(r.SelectHunt).Append(',')
                  .Append(r.SelectTrade).Append(',')
                  .Append(F(Pct(r.SelectRest, selections), 2)).Append(',')
                  .Append(F(Pct(r.SelectHunt, selections), 2)).Append(',')
                  .Append(F(Pct(r.SelectTrade, selections), 2)).Append(',')
                  .Append(r.HuntOutcomes).Append(',')
                  .Append(F(Pct(r.HuntSuccess, r.HuntOutcomes), 2)).Append(',')
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
                  .Append(r.BoughtGold).AppendLine();
            }
            return sb.ToString();
        }

        private static string BuildVillageCsv(SimContext sim)
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("village_id,has_shop,npc_start,npc_end,gold_start,gold_end,population_gold,npc_sold,npc_sold_gold," +
                          "npc_sell_rejected,npc_bought,npc_bought_gold,treatments,food_inflow");
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
                  .Append(v.Treatments).Append(',').Append(v.FoodInflow).AppendLine();
            }
            return sb.ToString();
        }

        private static string BuildSummary(SimContext sim, List<DayRow> rows, List<int> cowardIds, int cowardCourageMax,
            Dictionary<NpcAmbitionKind, int> ambitionCounts, int days, long elapsedMs, string hashBefore,
            string hashAfter, CapturingLogHandler capture)
        {
            DayRow first = rows[0];
            DayRow last = rows[rows.Count - 1];
            int rest = 0, hunt = 0, trade = 0, lootUpgrades = 0, tradeUpgrades = 0, maxCapExceed = 0;
            int huntOutcomes = 0, huntSuccess = 0, treatments = 0, deaths = 0, sold = 0, rejected = 0, bought = 0;
            double expectedDeaths = 0d;
            float maxCapRatio = 0f;
            for (int i = 1; i < rows.Count; i++)
            {
                DayRow r = rows[i];
                rest += r.SelectRest; hunt += r.SelectHunt; trade += r.SelectTrade;
                lootUpgrades += r.UpgradesLoot; tradeUpgrades += r.UpgradesTrade;
                huntOutcomes += r.HuntOutcomes; huntSuccess += r.HuntSuccess;
                treatments += r.Treatments; deaths += r.Deaths; expectedDeaths += r.ExpectedDeaths;
                sold += r.Sold; rejected += r.SellRejected; bought += r.Bought;
                maxCapExceed = Mathf.Max(maxCapExceed, r.CapExceed);
                maxCapRatio = Mathf.Max(maxCapRatio, r.MaxCapRatio);
            }
            int selections = rest + hunt + trade;

            int cowardRest = 0, cowardHunt = 0, cowardTrade = 0;
            for (int i = 0; i < cowardIds.Count; i++)
            {
                NpcStats stats = sim.NpcStats[cowardIds[i]];
                cowardRest += stats.Rest; cowardHunt += stats.Hunt; cowardTrade += stats.Trade;
            }
            int cowardSelections = cowardRest + cowardHunt + cowardTrade;

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
            sb.AppendLine($"업그레이드 사건: Loot {lootUpgrades} + Trade {tradeUpgrades} = {lootUpgrades + tradeUpgrades} " +
                          $"(NPC당 {(first.Living > 0 ? (lootUpgrades + tradeUpgrades) / (float)first.Living : 0f):F4})");
            sb.AppendLine($"개인 상한 초과 NPC(일별 최대): {maxCapExceed} | 최대 전투력/상한 비율: {maxCapRatio:F4}");
            sb.AppendLine($"활동 선택 {selections}회: Rest {Pct(rest, selections):F2}% / Hunt {Pct(hunt, selections):F2}% / " +
                          $"Trade {Pct(trade, selections):F2}%");
            sb.AppendLine($"겁 많은 NPC(용기 하위 {CowardFraction:P0}, {cowardIds.Count}명, 용기 ≤ {cowardCourageMax}): " +
                          $"Rest {Pct(cowardRest, cowardSelections):F2}% / Hunt {Pct(cowardHunt, cowardSelections):F2}% / " +
                          $"Trade {Pct(cowardTrade, cowardSelections):F2}%");
            sb.AppendLine($"Hunt 판정 {huntOutcomes}건 성공률 {Pct(huntSuccess, huntOutcomes):F2}% | 치료 {treatments}건");
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
            return sb.ToString();
        }

        private static void AppendAmbitionShares(StringBuilder sb, SimContext sim)
        {
            int kinds = (int)NpcAmbitionKind.Cautious + 1;
            int[] restByKind = new int[kinds];
            int[] huntByKind = new int[kinds];
            int[] tradeByKind = new int[kinds];
            for (int i = 0; i < sim.NpcIds.Count; i++)
            {
                int entityId = sim.NpcIds[i];
                int kind = (int)NpcAmbitionEvaluator.Evaluate(sim.Npcs[entityId].NpcStat).Primary;
                NpcStats stats = sim.NpcStats[entityId];
                restByKind[kind] += stats.Rest;
                huntByKind[kind] += stats.Hunt;
                tradeByKind[kind] += stats.Trade;
            }
            sb.AppendLine("주 야망별 선택 비중 (Rest/Hunt/Trade %):");
            for (int k = (int)NpcAmbitionKind.Explorer; k < kinds; k++)
            {
                int total = restByKind[k] + huntByKind[k] + tradeByKind[k];
                sb.AppendLine($"  {(NpcAmbitionKind)k}: {Pct(restByKind[k], total):F1} / {Pct(huntByKind[k], total):F1} / " +
                              $"{Pct(tradeByKind[k], total):F1} (n={total})");
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
                "_simulationPartyIds", "_simulationWorkCursor", "_nextPartyId", "_simulationNpcIds", "_simulationCursor" };
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
            if (value is int || value is float)
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
