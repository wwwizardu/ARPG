#nullable enable
using System.Collections.Generic;
using System.Text;
using ARPG.Creature;
using ARPG.Data;
using ARPG.Npc;
using ARPG.Npc.Narrative;
using ARPG.Village;
using UnityEngine;

namespace ARPG.EditorTools
{
    /// <summary>
    /// NPC 채광·제작·상점 위탁·장비 분실과 사연 지표 (NPC_OCCUPATIONS_PERSONAL_STORIES_PLAN 1단계 §11 검증). Ambition Sim 요약에 붙는다.
    /// 게임 매니저의 카운터(NpcOccupationCounters·VillageOccupationShopCounters)와 시뮬 끝의 복제본 상태를 직접 읽어 센다.
    /// 목표 수치는 아직 정하지 않았다 — PASS/FAIL은 "순환이 실제로 이어지는가·규칙이 지켜지는가"만 본다.
    /// </summary>
    public static partial class NpcAmbitionSimDiagnostics
    {
        /// <summary>시작 시점 상태 (지금은 쓰지 않는 자리 — 기존 세이브의 숙련은 첫 선택 때 정해지므로 끝 상태로 분포를 잰다).</summary>
        private sealed class OccupationStartState
        {
        }

        private static string BuildOccupationSummary(SimContext sim, int days)
        {
            NpcOccupationCounters c = sim.Occupation;
            VillageOccupationShopCounters shop = sim.OccupationShop;
            StringBuilder sb = new StringBuilder();
            sb.AppendLine($"==== NPC 채광·제작·장비 순환과 사연 (NPC_OCCUPATIONS 1단계, {days}일) ====");

            // ---- 숙련 분포 (끝 상태, 생존 NPC) ----
            int living = 0, initialized = 0;
            int[] zoneCount = new int[3];
            long[] zoneMining = new long[3], zoneSmithing = new long[3];
            int[] zoneTinMiners = new int[3], zoneIronMiners = new int[3], zoneIronSmiths = new int[3];
            long capSum = 0;
            int bagCrafts = 0, pendingLoss = 0, pendingLossOver7 = 0;
            float pendingDaysSum = 0f;
            int npcWithLoss = 0, npcWithLife = 0, namedStories = 0, refilledByCraft = 0;
            int[] lifeKinds = new int[5];
            int lossRumorHolders = 0, heardLossRumors = 0;
            float now = AR.s.Time.CurrentGameTime;
            for (int i = 0; i < sim.NpcIds.Count; i++)
            {
                NpcSaveData npc = sim.Npcs[sim.NpcIds[i]];
                if (npc.Condition == CharacterConditions.Dead || npc.Activity.Phase == NpcActivityPhase.Dead)
                    continue;
                living++;
                if (npc.LifeSkillsInitialized == true)
                {
                    initialized++;
                    int zone = AR.s.Npc.GetNpcLootZoneLevel(npc);
                    int b = zone >= 10 ? 2 : zone >= 5 ? 1 : 0;
                    zoneCount[b]++;
                    zoneMining[b] += npc.MiningSkill;
                    zoneSmithing[b] += npc.SmithingSkill;
                    if (npc.MiningSkill >= NpcOccupationRules.TinRequiredSkill) zoneTinMiners[b]++;
                    if (npc.MiningSkill >= NpcOccupationRules.IronRequiredSkill) zoneIronMiners[b]++;
                    if (npc.SmithingSkill >= NpcOccupationRules.IronRequiredSkill) zoneIronSmiths[b]++;
                    capSum += npc.LifeSkillCap;
                }
                for (int s = 0; s < npc.Inventory.Count; s++)
                {
                    ItemData? item = npc.Inventory[s];
                    if (item?.Equipment != null && item.Equipment.MakerNpcId == npc.EntityId)
                        bagCrafts++;
                }
                NpcEquipmentLoss? loss = npc.PersonalStories?.Loss;
                if (loss != null)
                {
                    npcWithLoss++;
                    if (loss.RefillGameTime < 0f)
                    {
                        pendingLoss++;
                        pendingDaysSum += (now - loss.LostGameTime) / 24f;
                        if (now - loss.LostGameTime >= NpcOccupationRules.HardshipHours)
                            pendingLossOver7++;
                    }
                    else if (loss.RefillMakerId > 0)
                    {
                        refilledByCraft++;
                        namedStories++;
                    }
                }
                NpcLifeStory? life = npc.PersonalStories?.Life;
                if (life != null)
                {
                    npcWithLife++;
                    int k = (int)life.Kind;
                    if (k >= 0 && k < lifeKinds.Length)
                        lifeKinds[k]++;
                    if (string.IsNullOrEmpty(life.OtherName) == false)
                        namedStories++;
                }
                bool holds = false;
                for (int r = 0; r < npc.Rumors.Count; r++)
                {
                    if (npc.Rumors[r].Kind != NpcRumorKind.EquipmentLost)
                        continue;
                    holds = true;
                    if (npc.Rumors[r].SourceNpcId >= 0)
                        heardLossRumors++;
                }
                if (holds == true)
                    lossRumorHolders++;
            }

            sb.AppendLine($"숙련: 생존 {living}명 중 정해짐 {initialized}명, 합산 상한 평균 {(initialized > 0 ? capSum / (float)initialized : 0f):F1}");
            string[] zoneLabels = { "구역 1~4", "구역 5~9", "구역 10+" };
            for (int b = 0; b < 3; b++)
            {
                if (zoneCount[b] == 0)
                    continue;
                sb.AppendLine($"  {zoneLabels[b]} {zoneCount[b]}명: 채광 평균 {zoneMining[b] / (float)zoneCount[b]:F1} · 대장 평균 " +
                              $"{zoneSmithing[b] / (float)zoneCount[b]:F1} | 주석 캘 수 있음 {zoneTinMiners[b]} · 철 캘 수 있음 {zoneIronMiners[b]} · " +
                              $"철 장비 만들 수 있음 {zoneIronSmiths[b]}");
            }
            sb.AppendLine($"숙련 상승: 채광 {c.MiningSkillGains}회 · 대장 {c.SmithingSkillGains}회");

            // ---- 채광·광석 ----
            sb.AppendLine($"채광: {c.MiningTrips}회, 광석 {c.OreMined}개 (주석 {c.TinMined} · 철 {c.IronMined}, 가방이 차 못 받음 {c.OreMinedNoRoom})");
            int stockVillages = 0;
            long stockTotal = 0;
            int shelfTotal = 0, shelfOverSlots = 0, makerOverLimit = 0, stockOverCap = 0;
            for (int v = 0; v < sim.VillageList.Count; v++)
            {
                VillageData village = sim.VillageList[v];
                if (village.OreStock.Count > 0)
                    stockVillages++;
                foreach (KeyValuePair<int, int> pair in village.OreStock)
                {
                    stockTotal += pair.Value;
                    if (pair.Value > NpcOccupationRules.OreStockCap)
                        stockOverCap++;
                }
                shelfTotal += village.Consignments.Count;
                if (village.Consignments.Count > NpcOccupationRules.ConsignmentSlotsPerVillage)
                    shelfOverSlots++;
                Dictionary<int, int> perMaker = new Dictionary<int, int>();
                for (int k = 0; k < village.Consignments.Count; k++)
                {
                    int maker = village.Consignments[k].MakerNpcId;
                    if (maker <= 0)
                        continue;
                    perMaker.TryGetValue(maker, out int count);
                    perMaker[maker] = count + 1;
                    if (count + 1 > NpcOccupationRules.ConsignmentsPerMaker)
                        makerOverLimit++;
                }
            }
            sb.AppendLine($"광석 판매: {c.OreSold}개 / {c.OreSoldGold}G, 거절 (재고 상한 {c.OreSellRejectedCap} · 금고 부족 {c.OreSellRejectedGold}), " +
                          $"제작용으로 남김 {c.OreReserved}개, 재입고 감소 {shop.OreDecayed}개, 플레이어 판매 {shop.PlayerOreSold}개");
            sb.AppendLine($"광석 구매(제작자): {c.OreBought}개 / {c.OreBoughtGold}G, 재고 없음 {c.OreBuyNoStock} · 돈 부족 {c.OreBuyNoMoney}");
            sb.AppendLine($"끝 상점 광석 재고: {stockTotal}개 ({stockVillages}개 마을)");

            // ---- 제작·위탁 ----
            sb.AppendLine($"제작: 완료 {c.CraftsCompleted}회 (장비 잃은 이웃을 보고 만든 것 {c.CraftsForLoss}), 조건이 깨져 중단 {c.CraftsAborted}");
            sb.AppendLine($"위탁: 맡김 {c.Consigned}개 (자리 없음 {c.ConsignNoSlot}), NPC가 삼 {c.ConsignmentSoldToNpc}개 / {c.ConsignmentSoldGold}G " +
                          $"(그중 업그레이드 {c.ConsignmentUpgrades}), 상점 매입 {shop.ConsignmentBuyouts} (금고 부족으로 미룸 {shop.ConsignmentBuyoutsDeferred}), " +
                          $"정산 {c.ProceedsSettled}건 / {c.ProceedsSettledGold}G");
            sb.AppendLine($"끝 위탁 칸 {shelfTotal}개, 미정산 대금 {AR.s.Village.SumPendingConsignmentProceeds()}G, 가방의 위탁 전 제작품 {bagCrafts}개");

            // ---- 분실·보충 ----
            float refillDays = c.Refills > 0 ? c.RefillHoursSum / 24f / c.Refills : 0f;
            sb.AppendLine($"분실: 중상 {c.SevereWounds}건 → 판정 {c.LossRolls}건 → 잃음 {c.Losses}건 " +
                          $"(건너뜀: 이미 빈 칸 {c.LossSkippedPending} · 금속 장비 없음 {c.LossSkippedNoMetal} · 화면 안 {c.LossSkippedMaterialized})");
            sb.AppendLine($"보충: {c.Refills}건 (위탁 제작품 {c.RefillsFromConsignment} · 상점 기본 장비 {c.RefillsFromShop}, 실체화 대기 {c.RefillsDeferred}), " +
                          $"평균 {refillDays:F1}일 만에 채움 | 못 산 이유: 매물 없음 {c.RefillBlockedNoCandidate} · 돈 부족 {c.RefillBlockedNoMoney}");
            sb.AppendLine($"끝에 아직 빈 칸: {pendingLoss}명 (평균 {(pendingLoss > 0 ? pendingDaysSum / pendingLoss : 0f):F1}일째, 7일 이상 {pendingLossOver7}명)");

            // ---- 사연 (주 목적) ----
            sb.AppendLine($"사연 기록: 생활 칸 {c.LifeStories}건 (제작 자랑 {c.CraftSoldStories} · 고생담 {c.HardshipStories} · " +
                          $"이정표 {c.LifeStories - c.CraftSoldStories - c.HardshipStories}), 분실 소문 {c.LossRumors}건");
            sb.AppendLine($"끝 생존 NPC: 분실 사연 {npcWithLoss}명 (제작품으로 채움 {refilledByCraft}), 생활 사연 {npcWithLife}명 " +
                          $"(제작 자랑 {lifeKinds[1]} · 채광 이정표 {lifeKinds[2]} · 제작 이정표 {lifeKinds[3]} · 고생담 {lifeKinds[4]}), " +
                          $"다른 NPC 이름이 든 사연 {namedStories}건, 분실 소문 보유 {lossRumorHolders}명 (전해 들은 것 {heardLossRumors}건)");

            // 사연이 실제 대화 화제 후보에 오르는가 (실제 selector 코드)
            int topicChecked = 0, lossTopicOk = 0, lossTopicFail = 0, lifeTopicOk = 0, lifeTopicMissing = 0;
            List<DialogueTopic> topics = new List<DialogueTopic>();
            string? lossExample = null, lifeExample = null;
            for (int i = 0; i < sim.NpcIds.Count; i++)
            {
                int id = sim.NpcIds[i];
                NpcSaveData npc = sim.Npcs[id];
                if (npc.Condition == CharacterConditions.Dead || npc.PersonalStories == null)
                    continue;
                NpcEquipmentLoss? loss = npc.PersonalStories.Loss;
                NpcLifeStory? life = npc.PersonalStories.Life;
                if (loss == null && life == null)
                    continue;
                topics.Clear();
                DialogueTopicSelector.CollectPersonalTopics(topics, id, npc, AR.s.Npc, now);
                topicChecked++;
                if (loss != null && loss.LostGameTime == npc.LastWoundGameTime)
                {
                    DialogueTopic? wound = topics.Find(t => t.Type == DialogueTopicType.RecentHunt);
                    if (wound != null)
                    {
                        if (wound.BaseLine.Contains(loss.ItemName) == true)
                        {
                            lossTopicOk++;
                            lossExample ??= $"{AR.s.Npc.GetDisplayName(id)}: {wound.BaseLine}";
                        }
                        else
                            lossTopicFail++;
                    }
                }
                if (life != null)
                {
                    DialogueTopic? lifeTopic = topics.Find(t => t.Type == DialogueTopicType.LifeStory);
                    if (lifeTopic != null)
                    {
                        lifeTopicOk++;
                        lifeExample ??= $"{AR.s.Npc.GetDisplayName(id)}: {lifeTopic.BaseLine}";
                    }
                    else
                        lifeTopicMissing++;
                }
            }
            sb.AppendLine($"대화 화제: 검사 {topicChecked}명 — 부상 이야기에 잃은 장비가 나옴 {lossTopicOk} (빠짐 {lossTopicFail}), " +
                          $"생활 사연 화제 {lifeTopicOk} (더 새 사연에 밀림 {lifeTopicMissing})");
            if (lossExample != null)
                sb.AppendLine($"  예) {lossExample}");
            if (lifeExample != null)
                sb.AppendLine($"  예) {lifeExample}");

            // ---- 확인 항목 ----
            int pass = 0, fail = 0;
            StringBuilder check = new StringBuilder();
            void Item(bool ok, string label, string value)
            {
                if (ok == true) pass++; else fail++;
                check.AppendLine($"  [{(ok ? "PASS" : "FAIL")}] {label}: {value}");
            }
            Item(c.RefillsFromConsignment > 0 && c.CraftSoldStories > 0,
                "순환 한 바퀴 (분실 → 위탁 제작품으로 보충 → 제작자 정산·자랑)", $"위탁품 보충 {c.RefillsFromConsignment}건, 제작 자랑 {c.CraftSoldStories}건");
            Item(c.MiningTrips > 0 && c.OreSold > 0 && c.OreBought > 0, "광석이 상점을 거쳐 제작자에게 감 (채광 → 판매 → 구매)",
                $"채광 {c.MiningTrips}회, 판매 {c.OreSold}개, 구매 {c.OreBought}개");
            Item(shelfOverSlots == 0 && makerOverLimit == 0, "위탁 칸 상한 (마을당 8칸·제작자당 2개)", $"초과 마을 {shelfOverSlots}, 초과 제작자 {makerOverLimit}");
            Item(stockOverCap == 0, "광석 재고 상한 (광종별 20개)", $"초과 {stockOverCap}");
            Item(npcWithLoss + npcWithLife > 0 && namedStories > 0, "새 사연을 가진 NPC가 있고 다른 NPC 이름이 들어간 사연이 있음",
                $"사연 보유 {npcWithLoss + npcWithLife}명, 이름이 든 사연 {namedStories}건");
            Item(lossTopicFail == 0, "부상 이야기에 같은 원정에서 잃은 장비가 나옴", $"빠짐 {lossTopicFail}");
            sb.AppendLine($"---- 1단계 확인: PASS {pass} / FAIL {fail} ----");
            sb.Append(check);
            return sb.ToString();
        }
    }
}
