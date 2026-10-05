#nullable enable
using System;
using System.Collections.Generic;
using ARPG.Component;
using ARPG.Creature;
using ARPG.Data;
using ARPG.History;
using ARPG.Tables;
using ARPG.Village;
using UnityEngine;

namespace ARPG.Npc
{
    /// <summary>
    /// NPC 야망 P4 — NPC 간 절도 (NPC_AMBITION_DESIGN §7.6, D98~D107).
    /// P4a: Rest 종료 공용 진입점(ResolveRestEnd)의 소문 교환 다음에 1회 판정한다 (D100). 파티 합류로 정리하는 휴식은 판정하지 않는다.
    /// - 결정(DecideTheft)과 적용(ApplyTheft)을 나눈다. 결정은 상태를 바꾸지 않고, 진단 강제 검증은 같은 적용 메서드를 부른다.
    /// - 들키면(현장 적발) 물건은 옮겨지지 않고 관계가 틀어진다 (D101·D102). 안 들키면 물건이 도둑에게 가고 관계는 그대로다.
    /// - 골드는 생기지도 옮겨지지도 않는다 (D34). 즉시 저장하지 않는다 (D47).
    /// P4b: 안 들킨 절도는 피해자에게 단서를 남기고(D103), 그 인스턴스의 ItemOwner·ItemSold 소문이 피해자에게 닿으면 늦게 발각된다.
    /// 훔친 물건 판매는 ItemSold 소문(D104)을, 발각(현장·늦은)은 Betrayal 소문(D105)을 피해자 직접 소문으로 만든다.
    /// 상태는 NpcSaveData(LastTheftGameTime·가방·Deed·단서·소문)와 호감도·관계에 있다. 이 파일의 인스턴스 필드는 휘발성 진단 카운터·
    /// 진단 관찰자와 매번 비우는 스크래치 버퍼뿐이다.
    /// </summary>
    public partial class NpcManager
    {
        // 피해자 제외 호감도 (D99): 도둑과 호감도가 이 값 이상이면 훔치지 않는다.
        public const int TheftVictimAffinityExclusion = 50;
        // 야망 배율 (D100): 기회(Opportunist) 주 야망 1.0, 보조 야망 0.5, 그 밖 0
        public const float TheftPrimaryAmbitionMultiplier = 1f;
        public const float TheftSecondaryAmbitionMultiplier = 0.5f;
        // 시도 확률 (D100) = 기본 × 야망 배율 × 성향 배율. 성향 배율 = 1 + 0.2 × (탐욕 − 정직)/100 → 0.8~1.2.
        // 튜닝 목표 세계 30일 시도 60~120건 (빈도는 가방이 찬 정상 상태 구간으로 본다, D109 ②).
        // GOLD_BALANCE D136: 생활비로 가방 빈칸이 늘어 0.25 → 0.015. 2% 실측은 Run(30) 144건으로 상한을 넘어 1.5%로 내렸다.
        public const float TheftBaseChance = 0.015f;
        public const float TheftDispositionWeight = 0.20f;
        // 쿨다운 (D100): 도둑 168h(LastTheftGameTime, 시도 기준), 같은 마을 72h(마을 명단의 최근 시도 시각). 세계 상한은 없다.
        public const float TheftThiefCooldownHours = 168f;
        public const float TheftVillageCooldownHours = 72f;
        // 발각 확률 (D101, 피해자 성향) = 0.25 + 0.30 × 호기심/100 + 0.20 × 인내/100 → 25~75%. 도둑 성향·증인은 넣지 않는다.
        public const float TheftDetectionBaseChance = 0.25f;
        public const float TheftDetectionCuriosityWeight = 0.30f;
        public const float TheftDetectionPatienceWeight = 0.20f;
        // 발각 결과 호감도 (D102): 피해자↔도둑 −40, 피해자의 가족·친구(1단계, 생존, 도둑 제외)↔도둑 −10
        public const int TheftVictimAffinityPenalty = -40;
        public const int TheftKinAffinityPenalty = -10;
        // 절도로 얻은 물건의 획득 출처 (진단 로그 구분용, GrantAcquiredItem source)
        private const string TheftEquipSource = "Theft";
        // 도난 단서 수명 (D103): 14일. 범인 사망 때도 지운다. 물건을 더 갖고 있지 않은 것은 삭제 조건이 아니다(판매 소문으로 발각 가능).
        public const float TheftClueLifetimeHours = 336f;
        // 늦은 발각 경로 (진단 카운터 인덱스): 0 = 장착(ItemOwner 소문), 1 = 판매(ItemSold 소문)
        public const int TheftDiscoveryPathItemOwner = 0;
        public const int TheftDiscoveryPathItemSold = 1;

        // 절도 진단 카운터 (휘발성). Reset에서 새로 만든다. 진단 도구는 시뮬레이션 동안 새 인스턴스로 바꿔 끼운다.
        private NpcTheftCounters _theftCounters = new();
        // 피해자 후보·발각 파장 대상 스크래치 버퍼 — 사용 전에 매번 Clear하므로 호출 사이에 상태를 남기지 않는다.
        private readonly List<int> _theftVictimScratch = new();
        private readonly List<int> _theftKinScratch = new();

        public NpcTheftCounters TheftCounters => _theftCounters;

        /// <summary>
        /// 진단 전용: 절도 시도가 확정돼 적용하기 직전에 (결정, 시각)으로 호출된다. 독립 감사가 적용 전 상태를 찍는 데 쓴다.
        /// 기본 null (게임 중에는 쓰지 않는다).
        /// </summary>
        public Action<NpcTheftDecision, float>? TheftDecidedObserver { get; set; }

        /// <summary>
        /// 진단 전용: 늦은 발각을 적용하기 직전(false)과 직후(true)에 호출된다. 독립 감사가 전후 상태를 대조한다. 기본 null.
        /// </summary>
        public Action<NpcTheftDiscovery, bool>? TheftDiscoveryObserver { get; set; }

        // ================= 조건 =================

        /// <summary>절도 시도 확률의 야망 배율 (D100). 0이면 도둑 후보가 아니다.</summary>
        public static float GetTheftAmbitionMultiplier(in NpcStatComponent stat)
        {
            NpcAmbitionProfile ambition = NpcAmbitionEvaluator.Evaluate(stat);
            if (ambition.Primary == NpcAmbitionKind.Opportunist)
                return TheftPrimaryAmbitionMultiplier;
            if (ambition.Secondary == NpcAmbitionKind.Opportunist)
                return TheftSecondaryAmbitionMultiplier;
            return 0f;
        }

        /// <summary>시도 확률 (D100) = 기본 25% × 야망 배율 × 성향 배율(1 + 0.2 × (탐욕 − 정직)/100).</summary>
        public static float GetTheftAttemptChance(in NpcStatComponent stat, float ambitionMultiplier)
        {
            float disposition = 1f + TheftDispositionWeight *
                (Mathf.Clamp01(stat.Greed / 100f) - Mathf.Clamp01(stat.Honesty / 100f));
            return TheftBaseChance * ambitionMultiplier * disposition;
        }

        /// <summary>발각 확률 (D101, 피해자 성향만) = 25% + 30% × 호기심/100 + 20% × 인내/100.</summary>
        public static float GetTheftDetectionChance(in NpcStatComponent victimStat)
        {
            return TheftDetectionBaseChance +
                   TheftDetectionCuriosityWeight * Mathf.Clamp01(victimStat.Curiosity / 100f) +
                   TheftDetectionPatienceWeight * Mathf.Clamp01(victimStat.Patience / 100f);
        }

        /// <summary>
        /// 도둑 조건 (D99·D100). 값싼 순서대로 본다: 생존·소속 마을 → 야망 배율 > 0 → 도둑 쿨다운 168h → 같은 마을 쿨다운 72h →
        /// 가방 빈칸. 모두 통과하면 Attempt(아직 시도 확정 아님 — 확률 롤과 피해자가 남았다). 상태를 바꾸지 않는다.
        /// </summary>
        public NpcTheftCheck GetTheftThiefBlock(int thiefId, NpcSaveData thief, float gameTime, out float ambitionMultiplier)
        {
            // 판정만 하므로 표시하지 않는다. HasFreeBagSlot의 칸 보정은 생성·로드 때 끝나 있어 여기서 바뀌는 게 없다.
            ambitionMultiplier = 0f;
            if (thief.Condition == CharacterConditions.Dead || thief.Activity.Phase == NpcActivityPhase.Dead ||
                thief.VillageId < 0)
                return NpcTheftCheck.NotEligible;
            VillageData? village = AR.s.Village.GetVillage(thief.VillageId);
            if (village == null)
                return NpcTheftCheck.NotEligible;

            ambitionMultiplier = GetTheftAmbitionMultiplier(thief.NpcStat);
            if (ambitionMultiplier <= 0f)
                return NpcTheftCheck.NoAmbition;
            if (thief.LastTheftGameTime >= 0f && gameTime - thief.LastTheftGameTime < TheftThiefCooldownHours)
                return NpcTheftCheck.ThiefCooldown;
            float villageLast = GetVillageLastTheftGameTime(village);
            if (villageLast >= 0f && gameTime - villageLast < TheftVillageCooldownHours)
                return NpcTheftCheck.VillageCooldown;
            if (HasFreeBagSlot(thief) == false)
                return NpcTheftCheck.NoFreeSlot;
            return NpcTheftCheck.Attempt;
        }

        /// <summary>같은 마을 쿨다운의 기준 (D100): 마을 명단의 LastTheftGameTime 최댓값. 시도가 없었으면 -1.</summary>
        public float GetVillageLastTheftGameTime(VillageData village)
        {
            float last = -1f;
            List<int> roster = village.NpcEntityIds;
            for (int i = 0; i < roster.Count; i++)
            {
                if (_npcSaveDict.TryGetValue(roster[i], out NpcSaveData? member) == true && member.LastTheftGameTime > last)
                    last = member.LastTheftGameTime;
            }
            return last;
        }

        /// <summary>
        /// 피해자 후보 (D99): 도둑과 같은 마을의 대기 NPC — 생존, Phase Idle, Kind None/Rest/Trade, 파티 아님,
        /// 던전 동행·플레이어 결투 아님. 부상 중인 NPC는 Phase가 Idle이 아니라 자동으로 빠진다. 실체화된 NPC도 포함한다.
        /// 제외: 도둑과 원한(방향 무관)·가족(배우자·형제·부모자식)·친구 관계, 도둑과 호감도 50 이상.
        /// 훔칠 물건이 있는지는 보지 않는다 (HasStealableBagItem).
        /// </summary>
        public bool IsTheftVictimCandidate(int thiefId, NpcSaveData thief, int candidateId)
        {
            if (candidateId == thiefId || thief.VillageId < 0 ||
                _npcSaveDict.TryGetValue(candidateId, out NpcSaveData? candidate) == false)
                return false;
            if (candidate.Condition == CharacterConditions.Dead || candidate.VillageId != thief.VillageId)
                return false;
            NpcActivityState activity = candidate.Activity;
            if (activity.Phase != NpcActivityPhase.Idle || activity.PartyId >= 0)
                return false;
            if (activity.Kind != NpcActivityKind.None && activity.Kind != NpcActivityKind.Rest &&
                activity.Kind != NpcActivityKind.Trade)
                return false;
            if (IsCompanionInDungeon(candidateId) == true || IsInPlayerDuel(candidateId) == true)
                return false;
            if (GetRelation(thiefId, candidateId, NpcRelationType.GrudgeAgainst) != null ||
                GetRelation(thiefId, candidateId, NpcRelationType.Spouse) != null ||
                GetRelation(thiefId, candidateId, NpcRelationType.Sibling) != null ||
                GetRelation(thiefId, candidateId, NpcRelationType.ParentOf) != null ||
                GetRelation(thiefId, candidateId, NpcRelationType.Friend) != null)
                return false;
            return GetAffinity(thiefId, candidateId) < TheftVictimAffinityExclusion;
        }

        /// <summary>
        /// 훔칠 물건이 있는 피해자 후보를 마을 명단 순서(소문 교환과 같은 결정론적 순서)로 모은다 (D99). result는 먼저 비운다.
        /// </summary>
        public void CollectTheftVictims(int thiefId, NpcSaveData thief, List<int> result)
        {
            result.Clear();
            VillageData? village = thief.VillageId >= 0 ? AR.s.Village.GetVillage(thief.VillageId) : null;
            if (village == null)
                return;
            List<int> roster = village.NpcEntityIds;
            for (int i = 0; i < roster.Count; i++)
            {
                int candidateId = roster[i];
                if (IsTheftVictimCandidate(thiefId, thief, candidateId) == true &&
                    HasStealableBagItem(_npcSaveDict[candidateId]) == true)
                    result.Add(candidateId);
            }
        }

        /// <summary>훔칠 수 있는 물건 (D99): 가방의 비스택 아이템(인스턴스 있음). 장착품은 대상이 아니다.</summary>
        public static bool IsStealableBagItem(ItemData? item)
        {
            if (item == null || item.ItemInstanceId == 0)
                return false;
            ItemTable? table = item.Table ?? AR.s.Data.GetItem(item.Id);
            return table != null && table.Stackable == false;
        }

        /// <summary>가방에 훔칠 수 있는 물건이 하나라도 있는가 (D99).</summary>
        public static bool HasStealableBagItem(NpcSaveData saveData)
        {
            List<ItemData?> inventory = saveData.Inventory;
            for (int i = 0; i < inventory.Count; i++)
            {
                if (IsStealableBagItem(inventory[i]) == true)
                    return true;
            }
            return false;
        }

        /// <summary>도둑 가방에 빈칸이 있는가 (D99: 없으면 시도하지 않는다).</summary>
        public static bool HasFreeBagSlot(NpcSaveData saveData)
        {
            // 생성·로드에서 칸 보정을 마친다. 조회 중 Activity 초기값까지 보정하면 변경 표시 없이 저장 상태가 바뀐다.
            return FindEmptyInventorySlot(saveData) >= 0;
        }

        /// <summary>
        /// 물건 선택 (D99): 피해자 가방의 훔칠 수 있는 물건 중 가중치 랜덤. 가중치 = 1 + 가치 / 후보 중 최고 가치
        /// (가장 비싼 물건도 최대 2배까지만 잘 뽑힘). 최고 가치가 0이면 균등. roll은 [0, 1). 후보가 없으면 -1.
        /// </summary>
        public static int PickTheftItemSlot(NpcSaveData victim, double roll)
        {
            List<ItemData?> inventory = victim.Inventory;
            int maxValue = 0;
            int candidates = 0;
            for (int i = 0; i < inventory.Count; i++)
            {
                ItemData? item = inventory[i];
                if (IsStealableBagItem(item) == false)
                    continue;
                candidates++;
                maxValue = Mathf.Max(maxValue, GetItemValue(item!));
            }
            if (candidates == 0)
                return -1;

            double totalWeight = 0d;
            for (int i = 0; i < inventory.Count; i++)
            {
                if (IsStealableBagItem(inventory[i]) == true)
                    totalWeight += GetTheftItemWeight(GetItemValue(inventory[i]!), maxValue);
            }

            double target = roll * totalWeight;
            double accumulated = 0d;
            int lastSlot = -1;
            for (int i = 0; i < inventory.Count; i++)
            {
                if (IsStealableBagItem(inventory[i]) == false)
                    continue;
                accumulated += GetTheftItemWeight(GetItemValue(inventory[i]!), maxValue);
                lastSlot = i;
                if (target < accumulated)
                    return i;
            }
            return lastSlot;
        }

        private static double GetTheftItemWeight(int value, int maxValue)
        {
            return maxValue > 0 ? 1d + value / (double)maxValue : 1d;
        }

        // ================= 결정 · 적용 =================

        /// <summary>
        /// 절도 결정 (D99~D101). 상태를 바꾸지 않는다 — 같은 입력이면 같은 결정이다.
        /// 도둑 조건(GetTheftThiefBlock)을 통과하면 결정론 RNG(도둑 EntityId, Rest 시작 시각, worldSeed)로:
        /// salt 170 난수열에서 ① 시도 확률 → ② 피해자(후보 중 균등) → ③ 물건(가치 가중) 순으로 뽑고, salt 171로 ④ 발각을 굴린다.
        /// Attempt면 decision이 채워진다. 그 밖의 값은 시도하지 않은 이유다(후보 없음도 시도 아님 — 쿨다운을 기록하지 않는다).
        /// </summary>
        public NpcTheftCheck DecideTheft(int thiefId, NpcSaveData thief, float restStartGameTime, float gameTime,
            int worldSeed, out NpcTheftDecision decision)
        {
            decision = new NpcTheftDecision { ThiefId = thiefId, VictimId = -1, ItemSlot = -1 };
            NpcTheftCheck block = GetTheftThiefBlock(thiefId, thief, gameTime, out float ambitionMultiplier);
            decision.AmbitionMultiplier = ambitionMultiplier;
            if (block != NpcTheftCheck.Attempt)
                return block;

            HistoryRng attemptRng = NpcActivityResolver.CreateRng(thiefId, restStartGameTime, worldSeed,
                NpcActivityResolver.TheftAttemptSalt);
            if (attemptRng.NextDouble() >= GetTheftAttemptChance(thief.NpcStat, ambitionMultiplier))
                return NpcTheftCheck.RollFailed;

            CollectTheftVictims(thiefId, thief, _theftVictimScratch);
            int candidateCount = _theftVictimScratch.Count;
            if (candidateCount == 0)
                return NpcTheftCheck.NoVictim;
            int victimId = _theftVictimScratch[attemptRng.Range(0, candidateCount)];
            _theftVictimScratch.Clear();

            NpcSaveData victim = _npcSaveDict[victimId];
            int itemSlot = PickTheftItemSlot(victim, attemptRng.NextDouble());
            if (itemSlot < 0)
                return NpcTheftCheck.NoVictim;

            HistoryRng detectionRng = NpcActivityResolver.CreateRng(thiefId, restStartGameTime, worldSeed,
                NpcActivityResolver.TheftDetectionSalt);
            decision.VictimId = victimId;
            decision.ItemSlot = itemSlot;
            decision.ItemInstanceId = victim.Inventory[itemSlot]!.ItemInstanceId;
            decision.VictimCandidateCount = candidateCount;
            decision.Detected = detectionRng.NextDouble() < GetTheftDetectionChance(victim.NpcStat);
            return NpcTheftCheck.Attempt;
        }

        /// <summary>
        /// 절도 적용 (D100~D102·D106). 시도가 확정됐으므로 도둑 LastTheftGameTime을 기록한다.
        /// - 들킴(현장 적발): 물건은 피해자 가방에 그대로. 피해자↔도둑 −40, 원한(피해자→도둑), 피해자의 가족·친구↔도둑 −10.
        ///   피해자·도둑 양쪽에 Deed.
        /// - 안 들킴: 물건을 피해자 가방에서 빼 도둑에게 준다 — 전리품 장착 규칙(D45)을 그대로 쓴다(업그레이드면 장착, 아니면 가방).
        ///   피해자에게만 Deed(범인 모름). 관계는 바꾸지 않는다(늦은 발각은 P4b).
        /// 진단 강제 검증이 결정만 바꿔 같은 메서드를 부른다. 결정이 지금 상태와 맞지 않으면 아무것도 바꾸지 않고 Invalid.
        /// </summary>
        public NpcTheftOutcome ApplyTheft(in NpcTheftDecision decision, float gameTime)
        {
            MarkSaveDirty(decision.ThiefId);
            MarkSaveDirty(decision.VictimId);
            if (_npcSaveDict.TryGetValue(decision.ThiefId, out NpcSaveData? thief) == false ||
                _npcSaveDict.TryGetValue(decision.VictimId, out NpcSaveData? victim) == false ||
                decision.ThiefId == decision.VictimId)
                return NpcTheftOutcome.Invalid;
            if (decision.ItemSlot < 0 || decision.ItemSlot >= victim.Inventory.Count)
                return NpcTheftOutcome.Invalid;
            ItemData? item = victim.Inventory[decision.ItemSlot];
            if (item == null || item.ItemInstanceId != decision.ItemInstanceId || IsStealableBagItem(item) == false)
                return NpcTheftOutcome.Invalid;

            thief.LastTheftGameTime = gameTime;
            string itemName = item.Table != null ? item.Table.Name : string.Empty;

            if (decision.Detected == true)
            {
                ApplyTheftDetectedRelations(decision.VictimId, decision.ThiefId);
                AddTheftDeed(decision.VictimId, NpcDeedKind.CaughtThiefInAct, NpcKnowledgeLevel.Lived, decision.ThiefId,
                    item.Id, itemName, gameTime);
                AddTheftDeed(decision.ThiefId, NpcDeedKind.CaughtStealing, NpcKnowledgeLevel.Did, decision.VictimId,
                    item.Id, itemName, gameTime);
                // 발각되는 순간 피해자가 직접 겪은 Betrayal 소문 (D105, 세부 = 현장 적발)
                AddBetrayalRumor(decision.VictimId, victim, decision.ThiefId, NpcBetrayalKind.TheftCaughtInAct,
                    item.ItemInstanceId, item.Id, GetItemValue(item), gameTime, gameTime);
                return NpcTheftOutcome.CaughtInAct;
            }

            victim.Inventory[decision.ItemSlot] = null;
            // 도둑 가방에 빈칸이 있으므로(D99) 버림 없이 들어간다. 훔친 장비는 장착하지 않고 팔기만 한다
            // (NPC_SHOP_EQUIPMENT D119 — D110의 장착 경로는 없어졌다). StolenEquipped 결과는 더 이상 나오지 않는다.
            GrantAcquiredItem(decision.ThiefId, thief, item, gameTime, TheftEquipSource);
            AddTheftDeed(decision.VictimId, NpcDeedKind.ItemStolen, NpcKnowledgeLevel.Lived, -1, item.Id, itemName, gameTime);
            // 피해자는 범인을 모르지만 단서가 남는다 (D103) — 그 인스턴스의 판매 소문이 닿으면 늦게 발각된다.
            AddTheftClue(victim, item.ItemInstanceId, decision.ThiefId, gameTime);
            return NpcTheftOutcome.StolenToBag;
        }

        // ================= 단서 · 늦은 발각 (D103) =================

        /// <summary>
        /// 도난 단서를 피해자에게 남긴다 (D103). 최대 3건 — 넘치면 가장 오래된 것(시각이 가장 이른 것)을 지운다.
        /// </summary>
        public void AddTheftClue(NpcSaveData victim, int itemInstanceId, int thiefId, float when)
        {
            MarkSaveDirty(victim.EntityId);
            if (itemInstanceId == 0 || thiefId < 0)
                return;
            List<NpcTheftClue> clues = victim.TheftClues;
            while (clues.Count >= NpcSaveData.MaxTheftClues)
            {
                int oldest = 0;
                for (int i = 1; i < clues.Count; i++)
                {
                    if (clues[i].When < clues[oldest].When)
                        oldest = i;
                }
                clues.RemoveAt(oldest);
                _theftCounters.CluesEvicted++;
            }
            clues.Add(new NpcTheftClue(itemInstanceId, thiefId, when));
            _theftCounters.CluesCreated++;
        }

        /// <summary>단서가 아직 유효한가 (D103): 범인이 살아 있고 절도 뒤 14일이 지나지 않았다.</summary>
        public bool IsTheftClueValid(NpcTheftClue clue, float now)
        {
            return now - clue.When < TheftClueLifetimeHours && IsLivingRumorNpc(clue.ThiefId) == true;
        }

        /// <summary>피해자의 Rest 종료 때 (ResolveRestEnd): 범인 사망·14일 경과 단서를 지운다 (D103).</summary>
        private void PruneTheftClues(NpcSaveData victim, float now)
        {
            MarkSaveDirty(victim.EntityId);
            List<NpcTheftClue> clues = victim.TheftClues;
            for (int i = clues.Count - 1; i >= 0; i--)
            {
                NpcTheftClue clue = clues[i];
                if (IsTheftClueValid(clue, now) == true)
                    continue;
                if (IsLivingRumorNpc(clue.ThiefId) == false)
                    _theftCounters.CluesThiefDead++;
                else
                    _theftCounters.CluesExpired++;
                clues.RemoveAt(i);
            }
        }

        /// <summary>
        /// 늦은 발각 판정 (D103, ReceiveRumor가 ItemOwner·ItemSold 소문을 받아들인 직후). 단서 중 같은 인스턴스이고 소문 주체가
        /// 그 단서의 실제 범인인 것이 있으면 발각이다(RNG 없음). 단서가 만료·범인 사망이면 발각하지 않는다(정리는 Rest 종료 때).
        /// 주체가 실제 범인이 아니면(도둑의 물건을 다시 훔친 NPC 등) 발각하지 않는다.
        /// </summary>
        private void TryDiscoverTheftFromRumor(NpcSaveData victim, NpcRumor rumor, float now)
        {
            List<NpcTheftClue> clues = victim.TheftClues;
            for (int i = 0; i < clues.Count; i++)
            {
                NpcTheftClue clue = clues[i];
                if (clue.ItemInstanceId != rumor.ItemInstanceId || clue.ThiefId != rumor.SubjectNpcId)
                    continue;
                if (IsTheftClueValid(clue, now) == false)
                {
                    _theftCounters.LateMatchInvalidClue++;
                    return;
                }
                int path = rumor.Kind == NpcRumorKind.ItemSold ? TheftDiscoveryPathItemSold : TheftDiscoveryPathItemOwner;
                int itemId = rumor.ItemId;
                if (rumor.Kind == NpcRumorKind.ItemOwner && TryGetRumorItem(rumor, out ItemData? owned) == true && owned != null)
                    itemId = owned.Id;
                ApplyTheftDiscovery(victim.EntityId, victim, i, path, itemId, rumor.Value, now);
                return;
            }
        }

        /// <summary>
        /// 늦은 발각 적용 (D103). 결과는 현장 적발과 같은 관계 변화(D102) + 단서 제거 + 늦은 발각 Deed 2종(D106) +
        /// 피해자 직접 Betrayal 소문(D105, 세부 = 늦은 발각, 사건 시각 = 절도 시각). 진단 강제 검증도 이 경로를 ReceiveRumor로 부른다.
        /// </summary>
        private void ApplyTheftDiscovery(int victimId, NpcSaveData victim, int clueIndex, int path, int itemId, int value,
            float now)
        {
            MarkSaveDirty(victimId);
            NpcTheftClue clue = victim.TheftClues[clueIndex];
            NpcTheftDiscovery discovery = new NpcTheftDiscovery
            {
                VictimId = victimId,
                ThiefId = clue.ThiefId,
                ItemInstanceId = clue.ItemInstanceId,
                ItemId = itemId,
                Path = path,
                TheftGameTime = clue.When,
                GameTime = now,
            };
            TheftDiscoveryObserver?.Invoke(discovery, false);

            victim.TheftClues.RemoveAt(clueIndex);
            ApplyTheftDetectedRelations(victimId, clue.ThiefId);
            string itemName = itemId >= 0 ? AR.s.Data.GetItem(itemId)?.Name ?? string.Empty : string.Empty;
            AddTheftDeed(victimId, NpcDeedKind.TheftDiscovered, NpcKnowledgeLevel.Lived, clue.ThiefId, itemId, itemName, now);
            AddTheftDeed(clue.ThiefId, NpcDeedKind.TheftExposed, NpcKnowledgeLevel.Did, victimId, itemId, itemName, now);
            AddBetrayalRumor(victimId, victim, clue.ThiefId, NpcBetrayalKind.TheftDiscovered, clue.ItemInstanceId, itemId,
                value, clue.When, now);
            _theftCounters.LateDiscoveries[path]++;

            TheftDiscoveryObserver?.Invoke(discovery, true);
        }

        // ================= 판매 소문 · Betrayal 소문 (D104·D105) =================

        /// <summary>
        /// 훔친 물건 판매인가 (D104): 판매 마을 명단 NPC의 유효한 단서 중 (그 인스턴스, 실제 범인 = 판매자)가 있다. 새 저장 필드 없이 판별한다.
        /// </summary>
        public bool IsStolenItemSale(int sellerId, int villageId, int itemInstanceId, float now)
        {
            VillageData? village = villageId >= 0 ? AR.s.Village.GetVillage(villageId) : null;
            if (village == null || itemInstanceId == 0)
                return false;
            List<int> roster = village.NpcEntityIds;
            for (int i = 0; i < roster.Count; i++)
            {
                if (_npcSaveDict.TryGetValue(roster[i], out NpcSaveData? member) == false || member.TheftClues.Count == 0)
                    continue;
                List<NpcTheftClue> clues = member.TheftClues;
                for (int c = 0; c < clues.Count; c++)
                {
                    if (clues[c].ItemInstanceId == itemInstanceId && clues[c].ThiefId == sellerId &&
                        IsTheftClueValid(clues[c], now) == true)
                        return true;
                }
            }
            return false;
        }

        /// <summary>
        /// NPC가 상점에 아이템을 판 직후 (TradeAtShop 판매 확정 뒤). 훔친 물건일 때만 판매자 본인이 처음 가진 ItemSold 소문을 만든다
        /// (D104 — ItemOwner처럼 교환으로 퍼진다). 일반 판매는 소문이 없다.
        /// </summary>
        public void NotifyNpcItemSold(int sellerId, NpcSaveData seller, int villageId, ItemData item, float now)
        {
            MarkSaveDirty(sellerId);
            if (item == null || IsStolenItemSale(sellerId, villageId, item.ItemInstanceId, now) == false)
                return;
            NpcRumor rumor = new NpcRumor
            {
                Kind = NpcRumorKind.ItemSold,
                SubjectNpcId = sellerId,
                ItemInstanceId = item.ItemInstanceId,
                ItemId = item.Id,
                Value = Math.Max(0, GetItemValue(item)),
                EventGameTime = now,
            };
            AddRumor(seller, rumor, now);
            _rumorCounters.CreatedItemSold++;
            RumorCreatedObserver?.Invoke(sellerId, rumor);
        }

        /// <summary>
        /// 발각되는 순간 피해자가 직접 겪은 Betrayal 소문 (D105). 가해자 = SubjectNpcId, 피해자 = VictimNpcId. 가치 = 노린 물건 가치
        /// (ItemOwner와 같은 관례 — 교환 우선순위·상한 교체에 쓰인다). 듣는 사람의 호감도·파티는 바꾸지 않는다(D74).
        /// </summary>
        private void AddBetrayalRumor(int victimId, NpcSaveData victim, int thiefId, NpcBetrayalKind kind, int itemInstanceId,
            int itemId, int value, float eventGameTime, float now)
        {
            MarkSaveDirty(victimId);
            NpcRumor rumor = new NpcRumor
            {
                Kind = NpcRumorKind.Betrayal,
                BetrayalKind = kind,
                SubjectNpcId = thiefId,
                VictimNpcId = victimId,
                ItemInstanceId = itemInstanceId,
                ItemId = itemId,
                Value = Math.Max(0, value),
                EventGameTime = eventGameTime,
            };
            AddRumor(victim, rumor, now);
            _rumorCounters.CreatedBetrayal[(int)kind]++;
            RumorCreatedObserver?.Invoke(victimId, rumor);
        }

        /// <summary>Rest 종료 때 1회 (ResolveRestEnd, allowTheft == true일 때만): 결정 → 적용, 진단 카운터 기록.</summary>
        private void ResolveTheftOnRestEnd(int thiefId, NpcSaveData thief, float restStartGameTime, float gameTime,
            int worldSeed)
        {
            NpcTheftCounters counters = _theftCounters;
            counters.RestEnds++;
            NpcTheftCheck check = DecideTheft(thiefId, thief, restStartGameTime, gameTime, worldSeed,
                out NpcTheftDecision decision);
            if (check == NpcTheftCheck.NotEligible || check == NpcTheftCheck.NoAmbition)
            {
                counters.Unqualified[(int)check]++;
                return;
            }

            int ambitionIndex = decision.AmbitionMultiplier >= TheftPrimaryAmbitionMultiplier ? 0 : 1;
            counters.Checks[ambitionIndex, (int)check]++;
            if (check != NpcTheftCheck.Attempt)
                return;

            counters.VictimCandidateSum += decision.VictimCandidateCount;
            ItemData targetItem = _npcSaveDict[decision.VictimId].Inventory[decision.ItemSlot]!;
            int value = GetItemValue(targetItem);
            TheftDecidedObserver?.Invoke(decision, gameTime);

            NpcTheftOutcome outcome = ApplyTheft(decision, gameTime);
            counters.Outcomes[ambitionIndex, (int)outcome]++;
            if (outcome == NpcTheftOutcome.StolenEquipped || outcome == NpcTheftOutcome.StolenToBag)
                counters.RecordStolenValue(value);
        }

        /// <summary>
        /// 발각 결과 — 관계 (D102). 현장 적발·늦은 발각(P4b) 공용. 피해자↔도둑 −40, 원한(피해자→도둑, 방향까지 비교해 중복 검사),
        /// 피해자의 가족(배우자·형제·부모자식 — ParentOf는 양방향)·친구 중 생존·도둑 아닌 NPC ↔ 도둑 −10 (한 사람에 한 번).
        /// 더 번지지 않고 가족·친구에게는 Deed를 남기지 않는다.
        /// </summary>
        private void ApplyTheftDetectedRelations(int victimId, int thiefId)
        {
            ModifyAffinity(victimId, thiefId, TheftVictimAffinityPenalty);
            AddRelation(victimId, thiefId, NpcRelationType.GrudgeAgainst);

            _theftKinScratch.Clear();
            IReadOnlyList<NpcRelationEntry> relations = GetRelations(victimId);
            for (int i = 0; i < relations.Count; i++)
            {
                NpcRelationEntry entry = relations[i];
                if (entry.Type != NpcRelationType.Spouse && entry.Type != NpcRelationType.Sibling &&
                    entry.Type != NpcRelationType.ParentOf && entry.Type != NpcRelationType.Friend)
                    continue;
                int kinId = entry.FromEntityId == victimId ? entry.ToEntityId : entry.FromEntityId;
                if (kinId == thiefId || kinId == victimId || _theftKinScratch.Contains(kinId) == true)
                    continue;
                if (_npcSaveDict.TryGetValue(kinId, out NpcSaveData? kin) == false ||
                    kin.Condition == CharacterConditions.Dead || kin.Activity.Phase == NpcActivityPhase.Dead)
                    continue;
                _theftKinScratch.Add(kinId);
            }
            for (int i = 0; i < _theftKinScratch.Count; i++)
                ModifyAffinity(_theftKinScratch[i], thiefId, TheftKinAffinityPenalty);
            _theftKinScratch.Clear();
        }

        /// <summary>
        /// 절도 Deed (D106). 새 필드 없이 Kind·When·노린 아이템(ItemId·표시 이름 스냅샷)·상대(있을 때만, 본인 제외)만 남긴다.
        /// HowKnown은 피해자 Lived(기본값 — 저장 안 함), 도둑 Did. 기본값 필드는 저장하지 않는다 (D93).
        /// </summary>
        private void AddTheftDeed(int entityId, NpcDeedKind kind, NpcKnowledgeLevel howKnown, int otherId, int itemId,
            string itemName, float when)
        {
            MarkSaveDirty(entityId);
            if (_npcSaveDict.TryGetValue(entityId, out NpcSaveData? saveData) == false)
                return;

            NpcDeedEntry entry = new NpcDeedEntry(kind, -1, howKnown, when)
            {
                ItemId = itemId,
                ItemDisplayName = itemName ?? string.Empty,
            };
            AddDeedParticipant(entry, otherId);
            saveData.RecordDeed(entry);
            _narrative.InvalidateDialogues(entityId);
        }

        private void ResetTheftRuntime()
        {
            _theftCounters = new NpcTheftCounters();
            _theftVictimScratch.Clear();
            _theftKinScratch.Clear();
            TheftDecidedObserver = null;
            TheftDiscoveryObserver = null;
        }
    }

    /// <summary>늦은 발각 1건 (진단 관찰자용). Path: 0 장착(ItemOwner), 1 판매(ItemSold).</summary>
    public struct NpcTheftDiscovery
    {
        public int VictimId;
        public int ThiefId;
        public int ItemInstanceId;
        public int ItemId;
        public int Path;
        public float TheftGameTime;
        public float GameTime;
    }

    /// <summary>절도 판정 결과 (DecideTheft). Attempt만 시도이고 나머지는 시도하지 않은 이유다 (진단 카운터 인덱스).</summary>
    public enum NpcTheftCheck
    {
        Attempt,
        NotEligible,       // 사망·소속 마을 없음
        NoAmbition,        // 기회 야망 아님 (야망 배율 0)
        ThiefCooldown,     // 도둑 168h
        VillageCooldown,   // 같은 마을 72h
        NoFreeSlot,        // 도둑 가방 빈칸 없음
        RollFailed,        // 시도 확률 롤 실패
        NoVictim,          // 훔칠 물건이 있는 피해자 후보 없음
        Count,
    }

    /// <summary>절도 적용 결과 (ApplyTheft).</summary>
    public enum NpcTheftOutcome
    {
        Invalid,           // 결정이 지금 상태와 맞지 않음 — 아무것도 바꾸지 않음
        CaughtInAct,       // 현장 적발 — 물건은 피해자에게 남음
        StolenEquipped,    // 안 들킴 — 도둑이 바로 장착
        StolenToBag,       // 안 들킴 — 도둑 가방 (실체화 중 장착 지연 포함)
        Count,
    }

    /// <summary>절도 결정 1건 (DecideTheft → ApplyTheft). 진단 강제 검증은 이 값을 직접 만들어 적용한다.</summary>
    public struct NpcTheftDecision
    {
        public int ThiefId;
        public int VictimId;
        public int ItemSlot;              // 피해자 가방 칸
        public int ItemInstanceId;        // 그 칸의 아이템 인스턴스 (적용 직전 대조)
        public float AmbitionMultiplier;  // 1.0 기회 주 야망 / 0.5 보조
        public int VictimCandidateCount;  // 결정 시점 피해자 후보 수
        public bool Detected;             // 현장 적발
    }

    /// <summary>
    /// 절도 진단 카운터 (휘발성, 세이브 대상 아님). NpcManager 인스턴스가 소유하고 Reset에서 새로 만든다.
    /// 진단 도구가 시뮬레이션 동안 새 인스턴스로 바꿔 끼워 읽는다. 야망 인덱스 0 = 기회 주 야망, 1 = 보조 야망.
    /// </summary>
    public sealed class NpcTheftCounters
    {
        // 훔친 물건 가치(BasePrice × 수량) 구간 상한: 0 / 1-9 / 10-19 / 20-49 / 50-99 / 100-199 / 200+
        public static readonly int[] ValueBucketUpper = { 0, 9, 19, 49, 99, 199, int.MaxValue };

        public long RestEnds;                                                   // 판정한 Rest 종료 (파티 합류 경로 제외)
        public readonly long[] Unqualified = new long[(int)NpcTheftCheck.Count];   // NotEligible·NoAmbition (야망 구분 없음)
        public readonly long[,] Checks = new long[2, (int)NpcTheftCheck.Count];    // 야망별 판정 결과 (Attempt = 시도)
        public readonly long[,] Outcomes = new long[2, (int)NpcTheftOutcome.Count];
        public long VictimCandidateSum;                                          // 시도 때 피해자 후보 수 합
        public long StolenCount;
        public long StolenValueSum;
        public int StolenValueMax;
        public readonly long[] StolenValueBuckets = new long[ValueBucketUpper.Length];
        // P4b 단서·늦은 발각 (D103)
        public long CluesCreated;
        public long CluesEvicted;            // 상한 3을 넘어 가장 오래된 단서를 지움
        public long CluesExpired;            // Rest 종료 정리: 14일 경과
        public long CluesThiefDead;          // Rest 종료 정리: 범인 사망
        public long LateMatchInvalidClue;    // 맞는 소문이 왔지만 단서가 만료·범인 사망이라 발각 없음
        public readonly long[] LateDiscoveries = new long[2];   // 경로: 0 장착(ItemOwner), 1 판매(ItemSold)

        public void RecordStolenValue(int value)
        {
            StolenCount++;
            StolenValueSum += value;
            if (value > StolenValueMax)
                StolenValueMax = value;
            for (int i = 0; i < ValueBucketUpper.Length; i++)
            {
                if (value <= ValueBucketUpper[i])
                {
                    StolenValueBuckets[i]++;
                    return;
                }
            }
        }
    }
}
