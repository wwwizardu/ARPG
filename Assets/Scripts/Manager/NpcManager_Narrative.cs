#nullable enable
using System.Collections.Generic;
using ARPG.Creature;
using ARPG.Data;
using ARPG.Dungeon;
using ARPG.Npc.Narrative;
using ARPG.Tables;
using ARPG.Village;
using UnityEngine;

namespace ARPG.Npc
{
    /// <summary>
    /// 공통 콘텐츠 실행 원장 (NPC_NARRATIVE_CONTENT_DESIGN §1·§7.1). NpcManager가 단독으로 수정하고,
    /// 저장 루트(AR.s.Data.Narrative)의 같은 객체를 사전으로 조회한다. 사건 진행은 이 원장에만 둔다.
    /// 즉시 저장하지 않는 변경은 revision을 올려 Dungeons·시뮬 묶음을 싣는 다음 저장에 Narrative가 함께 실리게 한다.
    /// </summary>
    public partial class NpcManager
    {
        // 던전 세대에 소문 콘텐츠를 붙일 확률 (사용자 결정 2026-10-10). 진단으로 조정하는 시작값이다.
        public const int DungeonRumorAttachPercent = 40;
        // DungeonLayout.Hash 채널. 입구 후보(101+)·레이아웃(11·29·31·37·43)·콘텐츠 선택(419)과 겹치지 않는다.
        private const int DungeonRumorAttachChannel = 47;
        private const int RumorOwnerChannel = 53;

        private readonly Dictionary<string, NarrativeContentInstance> _narrativeInstances = new();
        private readonly Dictionary<string, NarrativeDefinitionSnapshot> _narrativeDefinitions = new();
        private readonly Dictionary<string, NarrativePolicySnapshot> _narrativePolicies = new();
        private readonly Dictionary<string, NarrativeSelectionRecord> _narrativeSelections = new();
        // 소문 주인 후보 scratch. 사용할 때마다 비운다.
        private readonly List<int> _narrativeCandidateScratch = new();
        private long _narrativeRevision;
        private long _savedNarrativeRevision;
        // 마을 주민이 배치된 뒤에만 소문 선정·주인 선택을 한다 (새 월드는 던전 슬롯이 NPC보다 먼저 생긴다).
        private bool _narrativeOwnerSelectionReady;

        public long NarrativeRevision => _narrativeRevision;
        public bool HasUnsavedNarrativeChange => _narrativeRevision != _savedNarrativeRevision;
        public bool NarrativeOwnerSelectionReady => _narrativeOwnerSelectionReady;

        /// <summary>저장 성공 직후 DataManager가 캡처했던 revision만 확정한다. 저장 도중 생긴 변경은 다음 저장에 남는다.</summary>
        public void MarkNarrativeSaved(long capturedRevision)
        {
            if (capturedRevision > _savedNarrativeRevision)
                _savedNarrativeRevision = capturedRevision;
        }

        /// <summary>마을 주민 배치가 끝났다. 이후 세대 선정·소문 주인 선택이 동작한다.</summary>
        public void SetNarrativeOwnerSelectionReady() => _narrativeOwnerSelectionReady = true;

        /// <summary>NPC가 살아 있는가 (말할 수 있는 상대·소문 주인 후보·동행 공로 판정 공용).</summary>
        public bool IsNpcAlive(int npcId)
        {
            NpcSaveData? npc = GetSaveData(npcId);
            return npc != null && npc.Condition != CharacterConditions.Dead && npc.Activity.Phase != NpcActivityPhase.Dead;
        }

        public NarrativeContentInstance? GetNarrativeInstance(string? instanceId)
            => string.IsNullOrEmpty(instanceId) == false && _narrativeInstances.TryGetValue(instanceId, out NarrativeContentInstance? instance)
                ? instance : null;

        /// <summary>그 던전 세대의 소문 인스턴스. 일반 던전이면 null.</summary>
        public NarrativeContentInstance? GetDungeonNarrative(string dungeonId)
            => GetNarrativeInstance(NarrativeContentInstance.DungeonRumorId(dungeonId));

        public NarrativeContentDefinition? GetNarrativeContent(NarrativeContentInstance? instance)
            => instance != null && _narrativeDefinitions.TryGetValue(instance.DefinitionHash, out NarrativeDefinitionSnapshot? snapshot)
                ? snapshot.Content : null;

        public NarrativePolicySnapshot? GetNarrativePolicy(NarrativeContentInstance? instance)
            => instance != null && _narrativePolicies.TryGetValue(instance.PolicyHash, out NarrativePolicySnapshot? policy) ? policy : null;

        /// <summary>입구 징후 자막. 소문 던전의 입구가 있는 동안 늘 같은 문장이다 (§3.1). 일반 던전은 빈 값.</summary>
        public string GetNarrativeSignText(string dungeonId)
        {
            NarrativeContentInstance? instance = GetDungeonNarrative(dungeonId);
            NarrativeContentDefinition? content = GetNarrativeContent(instance);
            return instance != null && instance.IsOpen == true && content != null ? content.SignText : string.Empty;
        }

        /// <summary>원장 인스턴스 목록 (읽기 전용으로만 쓴다).</summary>
        public IReadOnlyList<NarrativeContentInstance> NarrativeInstances => AR.s.Data.Narrative.Instances;

        // ================= 세대 선정 (§4.1 소문 주인) =================

        /// <summary>
        /// 던전 슬롯 하나의 현재 세대에 콘텐츠를 정한다 (DungeonManager가 슬롯마다 부른다). 같은 세대는 다시 고르지 않는다.
        /// 이미 내부가 생성된(방문한) 던전은 콘텐츠 없이 둔다 (§7.7). 40%면 소문 콘텐츠를 붙이고 마을 주민 중 소문 주인을 고른다.
        /// 마을 주민 배치 전에는 아무것도 하지 않는다. 새 선정 기록을 만들었으면 true.
        /// </summary>
        public bool EnsureDungeonNarrative(int villageId, DungeonSlot slot, bool interiorExists, DungeonTable? table)
        {
            if (_narrativeOwnerSelectionReady == false || string.IsNullOrEmpty(slot.DungeonId) == true)
                return false;
            if (_narrativeSelections.TryGetValue(slot.DungeonId, out NarrativeSelectionRecord? existing) == true)
            {
                // 그때 고를 주민이 없었던 소문은 다음 기회에 다시 고른다.
                NarrativeContentInstance? pending = GetNarrativeInstance(existing.InstanceId);
                if (pending != null)
                    TryAssignRumorOwner(pending);
                return false;
            }

            var selection = new NarrativeSelectionRecord
            {
                DungeonId = slot.DungeonId, VillageId = villageId, SlotIndex = slot.SlotIndex, Generation = slot.Generation,
            };
            NarrativeContentInstance? instance = null;
            int worldSeed = AR.s.Data.WorldSeed;
            uint roll = DungeonLayout.Hash(worldSeed, villageId, slot.SlotIndex, slot.Generation, DungeonRumorAttachChannel);
            if (interiorExists == false && roll % 100u < DungeonRumorAttachPercent)
            {
                NarrativeDefinitionSnapshot? definition = AR.s.Data.NarrativeCatalog.SelectDungeonRumor(worldSeed, villageId,
                    slot.SlotIndex, slot.Generation, table);
                NarrativePolicySnapshot? policy = AR.s.Data.NarrativeCatalog.CopyCurrentPolicy();
                if (definition != null && policy != null)
                    instance = CreateDungeonRumorInstance(villageId, slot, definition, policy);
            }
            if (instance != null)
                selection.InstanceId = instance.InstanceId;
            AR.s.Data.Narrative.Selections.Add(selection);
            _narrativeSelections.Add(selection.DungeonId, selection);
            AR.s.Data.MarkNarrativeSelectionSaveDirty(selection.DungeonId);
            _narrativeRevision++;
            if (instance != null)
                TryAssignRumorOwner(instance);
            return true;
        }

        /// <summary>마을에 새 주민이 등록됐다 (보충·이주). 주인을 못 고른 그 마을의 소문 던전을 다시 본다.</summary>
        public void RetryPendingRumorOwners(int villageId)
        {
            if (_narrativeOwnerSelectionReady == false || villageId < 0)
                return;
            List<NarrativeContentInstance> instances = AR.s.Data.Narrative.Instances;
            for (int i = 0; i < instances.Count; i++)
            {
                NarrativeContentInstance instance = instances[i];
                if (instance.Kind == NarrativeContentKind.DungeonRumor && instance.VillageId == villageId && instance.OwnerNpcId < 0)
                    TryAssignRumorOwner(instance);
            }
        }

        private NarrativeContentInstance CreateDungeonRumorInstance(int villageId, DungeonSlot slot,
            NarrativeDefinitionSnapshot definition, NarrativePolicySnapshot policy)
        {
            var instance = new NarrativeContentInstance
            {
                InstanceId = NarrativeContentInstance.DungeonRumorId(slot.DungeonId),
                ContentId = definition.Content.ContentId,
                Kind = NarrativeContentKind.DungeonRumor,
                DefinitionHash = definition.Hash,
                PolicyHash = policy.Hash,
                CreatedGameTime = AR.s.Time.CurrentGameTime,
                DungeonId = slot.DungeonId,
                VillageId = villageId,
                SlotIndex = slot.SlotIndex,
                Generation = slot.Generation,
                TargetMonsterId = definition.Content.TargetId,
            };
            AddNarrativeInstance(instance, definition, policy, out _, out _);
            return instance;
        }

        /// <summary>
        /// 새 사건을 원장에 넣는다. 정의·정책 스냅샷은 해시별로 한 번만 넣고, 이번에 새로 넣었는지 돌려준다 (저장 실패 롤백용).
        /// </summary>
        private void AddNarrativeInstance(NarrativeContentInstance instance, NarrativeDefinitionSnapshot definition, NarrativePolicySnapshot policy,
            out bool addedDefinition, out bool addedPolicy)
        {
            NarrativeSaveRoot root = AR.s.Data.Narrative;
            addedDefinition = _narrativeDefinitions.ContainsKey(definition.Hash) == false;
            if (addedDefinition == true)
            {
                root.Definitions.Add(definition);
                _narrativeDefinitions.Add(definition.Hash, definition);
                AR.s.Data.MarkNarrativeDefinitionSaveDirty(definition.Hash);
            }
            addedPolicy = _narrativePolicies.ContainsKey(policy.Hash) == false;
            if (addedPolicy == true)
            {
                root.Policies.Add(policy);
                _narrativePolicies.Add(policy.Hash, policy);
                AR.s.Data.MarkNarrativePolicySaveDirty(policy.Hash);
            }
            root.Instances.Add(instance);
            _narrativeInstances.Add(instance.InstanceId, instance);
            AR.s.Data.MarkNarrativeInstanceSaveDirty(instance.InstanceId, NarrativeSaveGroup.All);
            _narrativeRevision++;
        }

        /// <summary>AddNarrativeInstance를 되돌린다. 그때 새로 넣은 정의·정책만 함께 지운다. 저장 캐시는 삭제를 원본 목록에서 알아낸다.</summary>
        private void RemoveNarrativeInstance(NarrativeContentInstance instance, bool removeDefinition, bool removePolicy)
        {
            NarrativeSaveRoot root = AR.s.Data.Narrative;
            root.Instances.Remove(instance);
            _narrativeInstances.Remove(instance.InstanceId);
            if (removeDefinition == true)
            {
                for (int i = root.Definitions.Count - 1; i >= 0; i--)
                    if (root.Definitions[i].Hash == instance.DefinitionHash)
                        root.Definitions.RemoveAt(i);
                _narrativeDefinitions.Remove(instance.DefinitionHash);
            }
            if (removePolicy == true)
            {
                for (int i = root.Policies.Count - 1; i >= 0; i--)
                    if (root.Policies[i].Hash == instance.PolicyHash)
                        root.Policies.RemoveAt(i);
                _narrativePolicies.Remove(instance.PolicyHash);
            }
            _narrativeRevision++;
        }

        /// <summary>
        /// 소문 주인을 고른다 (§4.1). 후보 = 그 마을에 살아 있는 주민(동행 중·결투 중 제외), ID 순서로 정렬한 뒤
        /// 월드 시드 + 던전 세대의 고정 무작위로 1명. 결과는 저장되므로 다시 불러와도 같다. 후보가 없으면 false.
        /// </summary>
        private bool TryAssignRumorOwner(NarrativeContentInstance instance)
        {
            if (_narrativeOwnerSelectionReady == false || instance.OwnerNpcId >= 0 || instance.IsOpen == false)
                return false;
            VillageData? village = AR.s.Village.GetVillage(instance.VillageId);
            if (village == null)
                return false;
            _narrativeCandidateScratch.Clear();
            for (int i = 0; i < village.NpcEntityIds.Count; i++)
            {
                int npcId = village.NpcEntityIds[i];
                NpcSaveData? npc = GetSaveData(npcId);
                if (npc == null || npc.VillageId != instance.VillageId || IsNpcAlive(npcId) == false ||
                    IsCompanionReserved(npcId) == true || IsCompanionInDungeon(npcId) == true || IsInPlayerDuel(npcId) == true)
                    continue;
                _narrativeCandidateScratch.Add(npcId);
            }
            if (_narrativeCandidateScratch.Count == 0)
                return false;
            _narrativeCandidateScratch.Sort();
            uint hash = DungeonLayout.Hash(AR.s.Data.WorldSeed, instance.VillageId, instance.SlotIndex, instance.Generation, RumorOwnerChannel);
            int ownerId = _narrativeCandidateScratch[(int)(hash % (uint)_narrativeCandidateScratch.Count)];
            _narrativeCandidateScratch.Clear();

            MarkNarrativeInstanceDirty(instance, NarrativeSaveGroup.Target);
            instance.OwnerNpcId = ownerId;
            // 주인이 아는 것은 위치와 징후다. 원정 소문(가치 있음)을 이미 알면 AddRumor가 그쪽을 유지한다.
            float now = AR.s.Time.CurrentGameTime;
            AddRumor(GetSaveData(ownerId)!, new NpcRumor
            {
                Kind = NpcRumorKind.DungeonInfo, IsDiscovery = true, InstanceId = instance.InstanceId,
                VillageId = instance.VillageId, SlotIndex = instance.SlotIndex, Generation = instance.Generation,
                EventGameTime = now, Value = 0, SourceNpcId = -1, SubjectNpcId = -1,
            }, now);
            return true;
        }

        // ================= 던전 결과 (§3.1·§7.4) =================

        /// <summary>보스 완료 커밋 전에 바꾼 원장·NPC 상태. 저장이 실패하면 RollbackDungeonCompletion으로 되돌린다.</summary>
        public sealed class NarrativeCompletionStage
        {
            internal NarrativeContentInstance? InstanceBefore;
            internal NpcSaveData? Requester;
            internal NpcNarrativeRequest? RequestBefore;
            internal NpcSaveData? Companion;
            internal List<NpcRumor>? CompanionRumorsBefore;
            public bool Resolved { get; internal set; }
        }

        /// <summary>
        /// 보스 처치로 던전이 닫힐 때 부른다 (완료 저장과 같은 커밋). 플레이어 측 처치면 소문을 해결로 기록하고,
        /// 아니면 미해결 종료로 기록해 부탁 칸을 비운다. 던전 안에 있던 동행은 행적 소문을 직접 겪은 것으로 받는다.
        /// </summary>
        public NarrativeCompletionStage StageDungeonCompletion(DungeonRecord record, int entranceX, int entranceY,
            int companionNpcId, float now)
        {
            var stage = new NarrativeCompletionStage();
            NarrativeContentInstance? instance = GetDungeonNarrative(record.DungeonId);
            bool playerParty = record.BossKilledByPlayerParty;
            if (instance != null && instance.IsOpen == true)
            {
                stage.InstanceBefore = instance.Clone();
                MarkNarrativeInstanceDirty(instance, NarrativeSaveGroup.All);
                instance.HasEntranceContext = true;
                instance.EntranceX = entranceX;
                instance.EntranceY = entranceY;
                instance.ProgressGameTime = now;
                instance.Revision++;
                if (playerParty == true)
                {
                    instance.Progress = NarrativeRumorProgress.Resolved;
                    stage.Resolved = true;
                }
                else
                {
                    instance.Ended = true;
                    stage.Requester = ReleaseRequestOnEnd(instance, out stage.RequestBefore);
                }
            }
            if (playerParty == true && companionNpcId >= 0 && IsNpcAlive(companionNpcId) == true)
            {
                NpcSaveData companion = GetSaveData(companionNpcId)!;
                stage.Companion = companion;
                stage.CompanionRumorsBefore = new List<NpcRumor>(companion.Rumors);
                companion.Rumors = new List<NpcRumor>(companion.Rumors);
                AddRumor(companion, CreateDeedRumor(stage.Resolved == true ? PlayerDeedKind.RumorResolved : PlayerDeedKind.DungeonCleared,
                    stage.Resolved == true ? instance!.InstanceId : string.Empty, record.VillageId, record.SlotIndex, record.Generation,
                    now, record.CompletionGold, -1, false), now);
                _rumorCounters.CreatedPlayerDeed++;
            }
            return stage;
        }

        public void RollbackDungeonCompletion(NarrativeCompletionStage stage)
        {
            if (stage.InstanceBefore != null)
                ReplaceNarrativeInstance(stage.InstanceBefore);
            if (stage.Requester != null)
            {
                MarkSaveDirty(stage.Requester.EntityId, NpcSaveGroup.NarrativeRequest);
                stage.Requester.NarrativeRequest = stage.RequestBefore;
                _narrative.InvalidateDialogues(stage.Requester.EntityId);
            }
            if (stage.Companion != null && stage.CompanionRumorsBefore != null)
            {
                MarkSaveDirty(stage.Companion.EntityId, NpcSaveGroup.Rumors);
                stage.Companion.Rumors = stage.CompanionRumorsBefore;
                _narrative.InvalidateDialogues(stage.Companion.EntityId);
            }
        }

        /// <summary>
        /// NPC 원정 완료로 입구가 교체될 때 부른다 (슬롯을 바꾸기 전). 플레이어 결과가 없으므로 미해결 종료로 기록하고
        /// 부탁 칸을 비운다. 즉시 저장하지 않으며 NPC 완료 슬롯 변경과 같은 다음 저장에 실린다.
        /// </summary>
        public void EndDungeonNarrativeUnresolved(string dungeonId, bool hadEntrance, int entranceX, int entranceY, float now)
        {
            NarrativeContentInstance? instance = GetDungeonNarrative(dungeonId);
            if (instance == null || instance.IsOpen == false)
                return;
            MarkNarrativeInstanceDirty(instance, NarrativeSaveGroup.All);
            if (hadEntrance == true)
            {
                instance.HasEntranceContext = true;
                instance.EntranceX = entranceX;
                instance.EntranceY = entranceY;
            }
            instance.Ended = true;
            instance.ProgressGameTime = now;
            instance.Revision++;
            ReleaseRequestOnEnd(instance, out _);
        }

        // 미해결 종료: 칸을 비우고, 부탁한 NPC가 살아 있으면 다음 대화에서 알린다 (§4.1).
        private NpcSaveData? ReleaseRequestOnEnd(NarrativeContentInstance instance, out NpcNarrativeRequest? requestBefore)
        {
            requestBefore = null;
            if (instance.RequesterNpcId < 0)
                return null;
            instance.EndNoticePending = IsNpcAlive(instance.RequesterNpcId);
            NpcSaveData? requester = GetSaveData(instance.RequesterNpcId);
            if (requester == null || requester.NarrativeRequest == null || requester.NarrativeRequest.InstanceId != instance.InstanceId)
                return null;
            requestBefore = requester.NarrativeRequest;
            MarkSaveDirty(requester.EntityId, NpcSaveGroup.NarrativeRequest);
            requester.NarrativeRequest = null;
            _narrative.InvalidateDialogues(requester.EntityId);
            return requester;
        }

        /// <summary>칸의 주인이 죽으면 보고 대상이 없으므로 보답·전파 없이 칸을 비운다 (§4.1). 사망 체크포인트와 함께 저장된다.</summary>
        private void ClearNarrativeRequestOnDeath(int entityId, NpcSaveData saveData)
        {
            if (saveData.NarrativeRequest == null)
                return;
            MarkSaveDirty(entityId, NpcSaveGroup.NarrativeRequest);
            saveData.NarrativeRequest = null;
        }

        // ================= 행적 소문 =================

        private static NpcRumor CreateDeedRumor(PlayerDeedKind kind, string instanceId, int villageId, int slotIndex, int generation,
            float eventGameTime, int value, int sourceNpcId, bool reacted)
        {
            // 가치 = 던전 행적은 그 던전의 완료 골드, 소망 행적은 소망 아이템 가격 (사용자 결정 2026-10-10). 교환에서 건넬 소문을 고를 때 쓰인다.
            return new NpcRumor
            {
                Kind = NpcRumorKind.PlayerDeed, DeedKind = kind, InstanceId = instanceId,
                VillageId = villageId, SlotIndex = slotIndex, Generation = generation,
                EventGameTime = eventGameTime, Value = Mathf.Max(0, value), SourceNpcId = sourceNpcId, SubjectNpcId = -1,
                Reacted = reacted,
            };
        }

        // ================= 수명·정리 =================

        /// <summary>로드한 원장으로 조회 사전을 만들고 가리키는 사건이 없는 부탁 칸을 비운다. NPC 로드 뒤에 부른다.</summary>
        private void InitializeNarrative()
        {
            ClearNarrativeIndexes();
            NarrativeSaveRoot root = AR.s.Data.Narrative;
            for (int i = 0; i < root.Definitions.Count; i++) _narrativeDefinitions[root.Definitions[i].Hash] = root.Definitions[i];
            for (int i = 0; i < root.Policies.Count; i++) _narrativePolicies[root.Policies[i].Hash] = root.Policies[i];
            for (int i = 0; i < root.Instances.Count; i++) _narrativeInstances[root.Instances[i].InstanceId] = root.Instances[i];
            for (int i = 0; i < root.Selections.Count; i++) _narrativeSelections[root.Selections[i].DungeonId] = root.Selections[i];
            foreach (KeyValuePair<int, NpcSaveData> pair in _npcSaveDict)
            {
                NpcNarrativeRequest? request = pair.Value.NarrativeRequest;
                if (request == null || _narrativeInstances.ContainsKey(request.InstanceId) == true)
                    continue;
#if UNITY_EDITOR
                Debug.LogWarning($"[Narrative] NPC {pair.Key} request slot points to a missing instance {request.InstanceId}; cleared.");
#endif
                MarkSaveDirty(pair.Key, NpcSaveGroup.NarrativeRequest);
                pair.Value.NarrativeRequest = null;
            }
        }

        private void ResetNarrative()
        {
            ClearNarrativeIndexes();
            _narrativeRevision = 0;
            _savedNarrativeRevision = 0;
            _narrativeOwnerSelectionReady = false;
            _narrativeBusy = false;
        }

        private void ClearNarrativeIndexes()
        {
            _narrativeInstances.Clear();
            _narrativeDefinitions.Clear();
            _narrativePolicies.Clear();
            _narrativeSelections.Clear();
            _narrativeCandidateScratch.Clear();
        }

        /// <summary>
        /// 더 쓰이지 않는 기록을 지운다 (DungeonManager가 세대 갱신 뒤 부른다). 던전 소문은 현재 세대의 사건(같은 세대 재선정 방지)·보고 대기·
        /// 재생 대기·알림 대기·부탁 칸이 가리키는 사건·해결 뒤 14일 안의 사건(아직 반응하지 않은 행적의 정책 고정)을 남긴다.
        /// 소망은 주인이 살아 있는 동안(같은 콘텐츠를 다시 주지 않는 기록), 주인이 죽었으면 충족 뒤 14일 안일 때만 남긴다.
        /// 현재 세대가 아닌 선정 기록과 아무 사건도 참조하지 않는 정의·정책도 지운다. 무언가 바뀌었으면 true.
        /// </summary>
        public bool PruneNarrativeLedger(float now)
        {
            if (AR.s.Dungeon == null || AR.s.Dungeon.IsInitialized == false)
                return false;
            NarrativeSaveRoot root = AR.s.Data.Narrative;
            bool changed = false;
            var requested = new HashSet<string>();
            foreach (KeyValuePair<int, NpcSaveData> pair in _npcSaveDict)
                if (pair.Value.NarrativeRequest != null && IsNpcAlive(pair.Key) == true)
                    requested.Add(pair.Value.NarrativeRequest.InstanceId);
            for (int i = root.Instances.Count - 1; i >= 0; i--)
            {
                NarrativeContentInstance instance = root.Instances[i];
                bool current = false;
                if (instance.Kind == NarrativeContentKind.DungeonRumor)
                {
                    current = AR.s.Dungeon.TryGetNpcDungeonSlotGeneration(instance.VillageId, instance.SlotIndex, out int generation) &&
                              generation == instance.Generation;
                    if (instance.IsOpen == true && current == false)
                    {
                        // 원장 갱신 없이 세대가 넘어간 사건은 미해결 종료로 고친다 (플레이어 결과가 없다).
                        EndDungeonNarrativeUnresolved(instance.DungeonId, false, 0, 0, now);
                        changed = true;
                    }
                }
                if (IsNarrativeInstanceNeeded(instance, current, requested, now) == true)
                    continue;
                root.Instances.RemoveAt(i);
                _narrativeInstances.Remove(instance.InstanceId);
                changed = true;
            }
            for (int i = root.Selections.Count - 1; i >= 0; i--)
            {
                NarrativeSelectionRecord selection = root.Selections[i];
                // 현재 세대의 선정 기록은 남긴다. 그 세대의 사건도 위에서 남겼다.
                if (AR.s.Dungeon.TryGetNpcDungeonSlotGeneration(selection.VillageId, selection.SlotIndex, out int generation) &&
                    generation == selection.Generation)
                    continue;
                root.Selections.RemoveAt(i);
                _narrativeSelections.Remove(selection.DungeonId);
                changed = true;
            }
            var definitions = new HashSet<string>();
            var policies = new HashSet<string>();
            for (int i = 0; i < root.Instances.Count; i++)
            {
                definitions.Add(root.Instances[i].DefinitionHash);
                policies.Add(root.Instances[i].PolicyHash);
            }
            for (int i = root.Definitions.Count - 1; i >= 0; i--)
            {
                if (definitions.Contains(root.Definitions[i].Hash) == true) continue;
                _narrativeDefinitions.Remove(root.Definitions[i].Hash);
                root.Definitions.RemoveAt(i);
                changed = true;
            }
            for (int i = root.Policies.Count - 1; i >= 0; i--)
            {
                if (policies.Contains(root.Policies[i].Hash) == true) continue;
                _narrativePolicies.Remove(root.Policies[i].Hash);
                root.Policies.RemoveAt(i);
                changed = true;
            }
            if (changed == true)
                _narrativeRevision++;
            return changed;
        }

        private bool IsNarrativeInstanceNeeded(NarrativeContentInstance instance, bool current, HashSet<string> requested, float now)
        {
            if (instance.Kind == NarrativeContentKind.Wish)
            {
                // 주인이 살아 있으면 열린 소망·재생 대기·"이미 받은 콘텐츠" 기록이라 남긴다 (같은 콘텐츠는 NPC당 한 번).
                if (IsNpcAlive(instance.OwnerNpcId) == true) return true;
                // 주인이 죽었어도 충족 뒤 14일 안이면 아직 반응하지 않은 이웃이 이 사건의 정책을 쓴다.
                return instance.WishState == WishState.Fulfilled &&
                       now - instance.ProgressGameTime <= NarrativeReactionRules.DeedReactionWindowHours;
            }
            if (current == true) return true;
            if (requested.Contains(instance.InstanceId) == true) return true;
            if (instance.Progress == NarrativeRumorProgress.Resolved && instance.Reported == false && IsNpcAlive(instance.OwnerNpcId) == true)
                return true;   // 보고(기한 없음) 또는 소문 전 해결 말하기 대기
            if (instance.Reported == true && instance.ReportDisplayed == false && IsNpcAlive(instance.ReportNpcId) == true)
                return true;   // 표시 취소된 보고의 재생
            if (instance.EndNoticePending == true && IsNpcAlive(instance.RequesterNpcId) == true)
                return true;
            // 해결 행적에 아직 반응하지 않은 NPC가 이 사건의 정책을 쓴다 (§5.2). 반응 기한과 같은 14일.
            return instance.Progress == NarrativeRumorProgress.Resolved &&
                   now - instance.ProgressGameTime <= NarrativeReactionRules.DeedReactionWindowHours;
        }

        private void ReplaceNarrativeInstance(NarrativeContentInstance restored)
        {
            List<NarrativeContentInstance> instances = AR.s.Data.Narrative.Instances;
            for (int i = 0; i < instances.Count; i++)
                if (instances[i].InstanceId == restored.InstanceId)
                    instances[i] = restored;
            _narrativeInstances[restored.InstanceId] = restored;
            AR.s.Data.MarkNarrativeInstanceSaveDirty(restored.InstanceId, NarrativeSaveGroup.All);
            _narrativeRevision++;
        }

        // 변경 함수의 입구에서 표시한다 (SAVE_SNAPSHOT_DESIGN S4). 사건 revision도 함께 올린다.
        private void MarkNarrativeInstanceDirty(NarrativeContentInstance instance, NarrativeSaveGroup groups)
        {
            AR.s.Data.MarkNarrativeInstanceSaveDirty(instance.InstanceId, groups);
            _narrativeRevision++;
        }

        private static bool IsGloryAmbition(NpcSaveData npc)
        {
            NpcAmbitionProfile profile = NpcAmbitionEvaluator.Evaluate(npc.NpcStat);
            return profile.Primary == NpcAmbitionKind.Glory || profile.Secondary == NpcAmbitionKind.Glory;
        }
    }
}
