#nullable enable
using System.Collections.Generic;
using ARPG.Creature;
using ARPG.Data;
using ARPG.Dungeon;
using ARPG.Tables;
using UnityEngine;

namespace ARPG.Npc
{
    /// <summary>
    /// DungeonDelve 판정 결과 (NPC_AMBITION_DESIGN §7.1, D55·D58). 호출자(System_NpcSim·진단)가 사망 정리·관계 변화·알림에 쓴다.
    /// </summary>
    public sealed class NpcDelveReport
    {
        public readonly List<int> MemberIds = new();
        public readonly List<NpcHuntOutcome> Outcomes = new();
        public readonly List<bool> Treated = new();
        // 판정별 중상 여부 (D91). 부상이 아니면 false.
        public readonly List<bool> Severe = new();
        public int VillageId = -1;
        public string DungeonId = string.Empty;
        public bool IsParty;
        public int SuccessCount;
        // 판정마다 사망 확률을 더한 기대값 (진단용)
        public double ExpectedDeaths;
        public int LootCount;
        // 원정 중 목표 던전이 바뀌어(세대 전진) Success가 실패로 전환된 판정 수 (D67 개정)
        public int DungeonChangedFailures;
        public float CompletionCapability;
        public float CompletionChance;
        // 완료 확률 롤을 통과했는가. 통과해도 보호 규칙에 막히면 CompletionResult가 Completed가 아니다.
        public bool CompletionRollPassed;
        public NpcDungeonCompletionResult CompletionResult = NpcDungeonCompletionResult.NotInitialized;

        public bool Completed => CompletionRollPassed == true && CompletionResult == NpcDungeonCompletionResult.Completed;
    }

    /// <summary>
    /// NPC 야망 P2a — DungeonDelve 원정과 NPC 던전 완료 (NPC_AMBITION_DESIGN §7.1, D49·D55~D58).
    /// 판정·보상은 여기서 확정하고, 사망 정리(MarkNpcDead)·파티 호감도·월드 피드는 호출자 몫으로 남긴다
    /// (진단 도구가 같은 코드를 복제 상태에서 그대로 부를 수 있게 하기 위해서다).
    /// 보상은 전리품뿐이다 — 골드·Food를 만들지 않는다 (D33·D34·D55).
    /// </summary>
    public partial class NpcManager
    {
        /// <summary>소속 마을에 원정할 던전 슬롯이 있는가 (D56 후보 조건, D57 입구 무관).</summary>
        public bool HasDelveOpportunity(NpcSaveData saveData)
        {
            return saveData.VillageId >= 0 && AR.s.Dungeon != null &&
                   AR.s.Dungeon.HasNpcDelveTarget(saveData.VillageId) == true;
        }

        /// <summary>
        /// 원정 대상 던전과 경로 목표를 고른다. rumorHolderId(솔로 = 본인, 파티 = 리더)가 유망한 소문을 가졌으면 그 슬롯으로 가는
        /// 확인 원정이고 (D76, RNG 없음), 아니면 결정론(salt 56)으로 균등 선택한다. 입구가 있으면 입구 좌표,
        /// 없으면 Hunt와 같은 방식의 마을 주변 좌표를 목표로 쓴다 (입구는 표현일 뿐 판정과 무관, D57).
        /// </summary>
        public bool TryPrepareDelveTarget(int rngOwnerId, int rumorHolderId, int villageId, Vector2 origin,
            float startGameTime, int worldSeed, out string dungeonId, out Vector2 targetPosition, out bool fromRumor)
        {
            dungeonId = string.Empty;
            targetPosition = origin;
            fromRumor = false;
            if (AR.s.Dungeon == null)
                return false;

            int slotCount = AR.s.Dungeon.GetNpcDelveSlotCount(villageId);
            if (slotCount <= 0)
                return false;

            int slotIndex = -1;
            int rumorSlot = PickConfirmationDelveSlot(rumorHolderId, villageId, startGameTime);
            if (rumorSlot >= 0)
            {
                slotIndex = AR.s.Dungeon.GetNpcDelveSlotListIndex(villageId, rumorSlot);
                fromRumor = slotIndex >= 0;
            }
            if (slotIndex < 0)
                slotIndex = NpcActivityResolver.PickDelveSlotIndex(rngOwnerId, startGameTime, worldSeed, slotCount);
            if (AR.s.Dungeon.TryGetNpcDelveSlot(villageId, slotIndex, out dungeonId, out _, out bool hasEntrance,
                    out Vector2 entrancePosition) == false)
                return false;

            if (hasEntrance == true)
            {
                targetPosition = entrancePosition;
                return true;
            }

            float minX = AR.s.Map.minChunkX * AR.s.Map.chunkSize;
            float maxX = (AR.s.Map.maxChunkX + 1) * AR.s.Map.chunkSize - 1f;
            float minY = AR.s.Map.minChunkY * AR.s.Map.chunkSize;
            float maxY = (AR.s.Map.maxChunkY + 1) * AR.s.Map.chunkSize - 1f;
            targetPosition = NpcActivityResolver.GetHuntTargetPosition(origin, rngOwnerId, startGameTime, worldSeed,
                minX, maxX, minY, maxY);
            return true;
        }

        /// <summary>
        /// 솔로 Delve 종료 판정. 상태 전이(Idle/Wounded)·치료·전리품·완료까지 적용한다.
        /// 결과가 Dead면 상태를 건드리지 않고 돌려준다 — 호출자가 MarkNpcDead로 정리한다.
        /// </summary>
        public NpcDelveReport ResolveSoloDelve(int entityId, NpcSaveData saveData, float outcomeGameTime, int worldSeed)
        {
            MarkSaveDirty(entityId);
            NpcActivityState activity = saveData.Activity;
            float seedGameTime = activity.OutcomeSeedGameTime > 0f
                ? activity.OutcomeSeedGameTime
                : activity.PhaseStartedGameTime;

            NpcDelveReport report = new NpcDelveReport
            {
                VillageId = saveData.VillageId,
                DungeonId = activity.TargetKey ?? string.Empty,
                IsParty = false,
            };

            float capability = GetNpcHuntCapability(entityId);
            NpcHuntOutcome outcome = NpcActivityResolver.ResolveDelve(capability, entityId, seedGameTime, worldSeed);
            NpcActivityResolver.GetSoloDelveChances(capability, out float deathChance, out _, out _);
            report.ExpectedDeaths += deathChance;
            // D67: 돌아왔을 때 목표 던전이 소속 마을 현재 슬롯에 없으면 원정 실패. 결과 롤은 이미 끝난 뒤라 난수열은 그대로다.
            bool hasTable = TryGetDelveTable(report.VillageId, report.DungeonId, out DungeonTable? table);
            if (hasTable == false && outcome == NpcHuntOutcome.Success)
            {
                outcome = NpcHuntOutcome.Failed;
                report.DungeonChangedFailures++;
            }
            report.MemberIds.Add(entityId);
            report.Outcomes.Add(outcome);
            // 소문 대상 슬롯 (P2b). 원정 중 던전이 바뀌었으면 없다.
            bool hasSlotRef = TryGetDelveSlotRef(report.VillageId, report.DungeonId, out int slotIndex, out int generation);
            // 돌아온 참가자는 그 슬롯 소문을 확인한 것으로 표시한다 (D76). 사망자는 사망 정리에서 소문이 비워진다.
            if (outcome != NpcHuntOutcome.Dead && hasSlotRef == true)
                MarkDelveRumorsChecked(saveData, report.VillageId, slotIndex);

            activity.Kind = NpcActivityKind.None;
            activity.TargetKey = string.Empty;
            activity.HasMovementPath = false;
            activity.PhaseStartedGameTime = outcomeGameTime;
            saveData.LastSimGameTime = outcomeGameTime;

            bool treated = false;
            if (outcome == NpcHuntOutcome.Dead)
            {
                report.Treated.Add(false);
                report.Severe.Add(false);
                return report;
            }

            if (outcome == NpcHuntOutcome.Wounded)
            {
                ApplyExpeditionWound(entityId, saveData, NpcActivityKind.DungeonDelve, entityId, seedGameTime, worldSeed,
                    NpcActivityResolver.DelveTreatmentSalt, NpcActivityResolver.SevereWoundDelveSalt, outcomeGameTime,
                    out treated, out bool severe);
                report.Treated.Add(treated);
                report.Severe.Add(severe);
                return report;
            }

            report.Treated.Add(false);
            report.Severe.Add(false);
            activity.Phase = NpcActivityPhase.Idle;
            activity.PhaseEndGameTime = 0f;
            if (outcome != NpcHuntOutcome.Success)
                return report;

            report.SuccessCount = 1;
            int lootValue = GrantDelveLoot(report, entityId, saveData, table, entityId, seedGameTime, worldSeed,
                NpcActivityResolver.DelveLootSalt, outcomeGameTime);
            // 원정 성공 + 전리품 → 직접 겪은 DungeonInfo 소문 (D71)
            if (lootValue >= 0 && hasSlotRef == true)
                AddFirsthandDelveRumor(entityId, saveData, report.VillageId, slotIndex, generation, entityId, lootValue,
                    outcomeGameTime);

            report.CompletionCapability = capability;
            TryCompleteDungeon(report, entityId, seedGameTime, worldSeed, NpcActivityResolver.DelveCompletionSalt,
                outcomeGameTime);
            if (report.Completed == true)
            {
                int rewardValue = GrantCompletionReward(report, entityId, saveData, table, entityId, seedGameTime,
                    worldSeed, NpcActivityResolver.DelveCompletionLootSalt, outcomeGameTime);
                AddDungeonClearedDeed(entityId, report.MemberIds, report.Outcomes, outcomeGameTime);
                // NPC 완료 → 옛 세대 소문 정리 + "끝냈다" 소문 (대상 = 새 세대, D71·D72)
                if (hasSlotRef == true)
                    OnNpcDungeonCompleted(report, slotIndex, entityId, new[] { rewardValue }, outcomeGameTime);
            }
            return report;
        }

        /// <summary>
        /// 파티 Delve 종료 판정. 파티원별 결과(동료 지원 반영)·상태 전이·치료·파티 해산·전리품·완료까지 적용한다.
        /// 사망한 파티원 정리, 파티 호감도, 알림은 호출자 몫이다. 살아 있는 파티원이 2명 미만이면 파티만 해산하고 null.
        /// </summary>
        public NpcDelveReport? ResolvePartyDelve(NpcPartyState party, float outcomeGameTime, int worldSeed)
        {
            List<NpcSaveData> members = new List<NpcSaveData>(party.MemberIds.Count);
            NpcDelveReport report = new NpcDelveReport { IsParty = true };
            for (int i = 0; i < party.MemberIds.Count; i++)
            {
                int memberId = party.MemberIds[i];
                NpcSaveData? member = GetSaveData(memberId);
                if (member == null || member.Condition == CharacterConditions.Dead)
                    continue;
                members.Add(member);
                report.MemberIds.Add(memberId);
            }

            if (members.Count < 2)
            {
                DissolveParty(party.PartyId);
                return null;
            }

            NpcActivityState partyActivity = party.Activity;
            float seedGameTime = partyActivity.OutcomeSeedGameTime > 0f
                ? partyActivity.OutcomeSeedGameTime
                : partyActivity.PhaseStartedGameTime;
            report.VillageId = members[0].VillageId;
            report.DungeonId = partyActivity.TargetKey ?? string.Empty;

            float[] capabilities = new float[members.Count];
            for (int i = 0; i < members.Count; i++)
                capabilities[i] = GetNpcHuntCapability(report.MemberIds[i]);
            float partyPower = NpcActivityResolver.GetPartyHuntPower(capabilities);
            // D67: 목표 던전이 바뀌었으면 성공한 파티원만 실패로 전환한다 (부상·사망 결과는 그대로).
            bool hasTable = TryGetDelveTable(report.VillageId, report.DungeonId, out DungeonTable? table);
            bool hasSlotRef = TryGetDelveSlotRef(report.VillageId, report.DungeonId, out int slotIndex, out int generation);

            for (int i = 0; i < members.Count; i++)
            {
                NpcHuntOutcome outcome = NpcActivityResolver.ResolvePartyMemberDelve(capabilities[i], partyPower,
                    members.Count, party.PartyId, seedGameTime, worldSeed, i);
                NpcActivityResolver.GetPartyMemberDelveChances(
                    NpcActivityResolver.GetPartyMemberCapability(capabilities[i], partyPower, members.Count),
                    out float deathChance, out _, out _);
                report.ExpectedDeaths += deathChance;
                if (hasTable == false && outcome == NpcHuntOutcome.Success)
                {
                    outcome = NpcHuntOutcome.Failed;
                    report.DungeonChangedFailures++;
                }
                report.Outcomes.Add(outcome);
                if (outcome == NpcHuntOutcome.Success)
                    report.SuccessCount++;
            }

            for (int i = 0; i < members.Count; i++)
            {
                MarkSaveDirty(report.MemberIds[i]);
                NpcSaveData member = members[i];
                NpcActivityState memberActivity = member.Activity;
                memberActivity.HasMovementPath = false;
                memberActivity.TargetKey = string.Empty;
                memberActivity.PhaseStartedGameTime = outcomeGameTime;
                member.LastSimGameTime = outcomeGameTime;

                bool treated = false;
                bool severe = false;
                if (report.Outcomes[i] == NpcHuntOutcome.Wounded)
                {
                    ApplyExpeditionWound(report.MemberIds[i], member, NpcActivityKind.DungeonDelve, party.PartyId,
                        seedGameTime, worldSeed, NpcActivityResolver.PartyDelveTreatmentSaltBase + i,
                        NpcActivityResolver.PartySevereWoundDelveSaltBase + i, outcomeGameTime, out treated, out severe);
                }
                else if (report.Outcomes[i] != NpcHuntOutcome.Dead)
                {
                    memberActivity.Phase = NpcActivityPhase.Idle;
                    memberActivity.Kind = NpcActivityKind.None;
                    memberActivity.PhaseEndGameTime = 0f;
                }
                report.Treated.Add(treated);
                report.Severe.Add(severe);
                // 돌아온 파티원은 그 슬롯 소문을 확인한 것으로 표시한다 (D76)
                if (report.Outcomes[i] != NpcHuntOutcome.Dead && hasSlotRef == true)
                    MarkDelveRumorsChecked(member, report.VillageId, slotIndex);
            }

            DissolveParty(party.PartyId);
            if (report.SuccessCount <= 0)
                return report;

            // 분배 규칙은 P3 — Hunt와 같이 Success를 받은 파티원 각자가 전리품을 롤한다.
            float completionCapabilitySum = 0f;
            for (int i = 0; i < members.Count; i++)
            {
                if (report.Outcomes[i] != NpcHuntOutcome.Success)
                    continue;
                int lootValue = GrantDelveLoot(report, report.MemberIds[i], members[i], table, party.PartyId,
                    seedGameTime, worldSeed, NpcActivityResolver.PartyDelveLootSaltBase + i, outcomeGameTime);
                // 원정 성공 + 전리품 → 파티원마다 직접 겪은 DungeonInfo 소문 (당사자 = 리더, D71)
                if (lootValue >= 0 && hasSlotRef == true)
                    AddFirsthandDelveRumor(report.MemberIds[i], members[i], report.VillageId, slotIndex, generation,
                        party.LeaderId, lootValue, outcomeGameTime);
                completionCapabilitySum += NpcActivityResolver.GetPartyMemberCapability(capabilities[i], partyPower,
                    members.Count);
            }

            // 완료 확률은 보스까지 간(성공한) 파티원의 판정 capability 평균 — Hunt처럼 동료 지원이 반영된 값 (D55).
            report.CompletionCapability = completionCapabilitySum / report.SuccessCount;
            TryCompleteDungeon(report, party.PartyId, seedGameTime, worldSeed,
                NpcActivityResolver.PartyDelveCompletionSalt, outcomeGameTime);
            if (report.Completed == false)
                return report;

            int[] rewardValues = new int[members.Count];
            for (int i = 0; i < members.Count; i++)
            {
                if (report.Outcomes[i] != NpcHuntOutcome.Success)
                    continue;
                rewardValues[i] = GrantCompletionReward(report, report.MemberIds[i], members[i], table, party.PartyId,
                    seedGameTime, worldSeed, NpcActivityResolver.PartyDelveCompletionLootSaltBase + i, outcomeGameTime);
                AddDungeonClearedDeed(report.MemberIds[i], report.MemberIds, report.Outcomes, outcomeGameTime);
            }
            // NPC 완료 → 옛 세대 소문 정리 + "끝냈다" 소문 (대상 = 새 세대, 당사자 = 리더, D71·D72)
            if (hasSlotRef == true)
                OnNpcDungeonCompleted(report, slotIndex, party.LeaderId, rewardValues, outcomeGameTime);
            return report;
        }

        /// <summary>
        /// DungeonCleared Deed (D58·D61). 참가자는 본인 + 함께 성공한 파티원. 대화 토픽 연결은 P2c.
        /// </summary>
        public void AddDungeonClearedDeed(int entityId, IReadOnlyList<int> memberIds, IReadOnlyList<NpcHuntOutcome> outcomes,
            float when)
        {
            MarkSaveDirty(entityId);
            if (_npcSaveDict.TryGetValue(entityId, out NpcSaveData? saveData) == false)
                return;

            NpcDeedEntry entry = new NpcDeedEntry(NpcDeedKind.DungeonCleared, -1, NpcKnowledgeLevel.Did, when);
            AddDeedParticipant(entry, entityId);
            for (int i = 0; i < memberIds.Count; i++)
            {
                if (memberIds[i] != entityId && outcomes[i] == NpcHuntOutcome.Success)
                    AddDeedParticipant(entry, memberIds[i]);
            }
            saveData.RecordDeed(entry);
            _narrative.InvalidateDialogues(entityId);
        }

        /// <summary>
        /// 원정 대상 던전의 테이블. 목표 DungeonId가 소속 마을 현재 슬롯에 없으면(원정 중 세대 전진) false —
        /// 호출자는 성공 결과를 실패로 전환한다 (D67 개정, 기본 테이블 폴백 없음).
        /// </summary>
        private static bool TryGetDelveTable(int villageId, string dungeonId, out DungeonTable? table)
        {
            table = null;
            if (AR.s.Dungeon == null || AR.s.Dungeon.TryGetNpcDelveTable(villageId, dungeonId, out DungeonTable found) == false)
                return false;
            table = found;
            return true;
        }

        /// <summary>원정 대상 슬롯의 번호·세대 (소문 대상, P2b). 원정 중 세대가 넘어갔으면 false.</summary>
        private static bool TryGetDelveSlotRef(int villageId, string dungeonId, out int slotIndex, out int generation)
        {
            slotIndex = -1;
            generation = -1;
            return AR.s.Dungeon != null &&
                   AR.s.Dungeon.TryGetNpcDelveSlotRef(villageId, dungeonId, out slotIndex, out generation) == true;
        }

        /// <summary>원정 성공 전리품: DungeonTable.MonsterLevel 기준 일반 롤 1회 (D49·D55·D65 개정). 받은 전리품의 가치, 없으면 -1.</summary>
        private int GrantDelveLoot(NpcDelveReport report, int entityId, NpcSaveData saveData, DungeonTable? table,
            int rngOwnerId, float seedGameTime, int worldSeed, int lootSalt, float outcomeGameTime)
        {
            int monsterLevel = table != null ? Mathf.Max(1, table.MonsterLevel) : 1;
            ItemData? loot = NpcActivityResolver.RollHuntLoot(rngOwnerId, seedGameTime, worldSeed, lootSalt,
                monsterLevel);
            if (loot == null)
                return -1;
            // 가치는 넣기 전에 잰다 — 가방이 가득 차 버려지거나 장착돼도 "얻었다"는 사실은 같다.
            int value = GetItemValue(loot);
            GrantHuntLoot(entityId, saveData, loot, outcomeGameTime);
            report.LootCount++;
            return value;
        }

        /// <summary>NPC 완료 보상: 골드 없이 BossLevel 기준 일반 롤 1회, 항상 (D58·D65 개정). 받은 전리품의 가치, 없으면 -1.</summary>
        private int GrantCompletionReward(NpcDelveReport report, int entityId, NpcSaveData saveData,
            DungeonTable? table, int rngOwnerId, float seedGameTime, int worldSeed, int salt, float outcomeGameTime)
        {
            int bossLevel = table != null ? Mathf.Max(1, table.BossLevel) : 1;
            ItemData? loot = NpcActivityResolver.RollHuntLoot(rngOwnerId, seedGameTime, worldSeed, salt, bossLevel);
            if (loot == null)
                return -1;
            int value = GetItemValue(loot);
            GrantHuntLoot(entityId, saveData, loot, outcomeGameTime);
            report.LootCount++;
            return value;
        }

        /// <summary>
        /// 완료 확률(D55)을 굴리고, 통과하면 슬롯 단위 NPC 완료 진입점을 부른다. 보호 규칙은 DungeonManager가 판정한다.
        /// </summary>
        private static void TryCompleteDungeon(NpcDelveReport report, int rngOwnerId, float seedGameTime, int worldSeed,
            int salt, float outcomeGameTime)
        {
            report.CompletionChance = NpcActivityResolver.GetDelveCompletionChance(report.CompletionCapability);
            report.CompletionRollPassed = NpcActivityResolver.RollDelveCompletion(report.CompletionChance, rngOwnerId,
                seedGameTime, worldSeed, salt);
            if (report.CompletionRollPassed == false || AR.s.Dungeon == null || report.VillageId < 0)
                return;

            report.CompletionResult = AR.s.Dungeon.TryCompleteByNpc(report.VillageId, report.DungeonId, outcomeGameTime);
            Debug.Log($"[NpcAmbition] DungeonCompletion owner={rngOwnerId} party={report.IsParty} " +
                      $"village={report.VillageId} dungeon={report.DungeonId} capability={report.CompletionCapability:F3} " +
                      $"chance={report.CompletionChance:P1} result={report.CompletionResult} t={outcomeGameTime:F2}");
        }
    }
}
