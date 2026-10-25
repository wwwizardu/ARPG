#nullable enable
using ARPG.Data;
using ARPG.NamedThreat;
using ARPG.Npc.Narrative;
using ARPG.Village;
using UnityEngine;

namespace ARPG.Npc
{
    /// <summary>
    /// 네임드 위협의 NPC 쪽 (NAMED_THREAT_DESIGN §6·§8·§10.2·§12.1): 사냥 마주침 판정과 기록(Deed·피해자·소문), 소문 주인·먼 소문 배정,
    /// 부탁 인스턴스(narrative 원장)와 위치 표현. 세계 기록(칭호·단계·영역)은 NamedThreatManager가 가지며 여기서는 읽기·기록 API만 부른다.
    /// 마주침 판정은 System_NpcSim과 진단(NpcAmbitionSimDiagnostics)이 같은 메서드를 부른다 (진단 복제 함정, AMBITION P4).
    /// </summary>
    public partial class NpcManager
    {
        // ================= 사냥 마주침 (§6.1·§6.2) =================

        /// <summary>
        /// 사냥이 끝날 때 결과 판정 앞에 한 번 굴리는 마주침 판정. 그 마을(주 마을)에 활성 네임드가 있을 때만 굴린다.
        /// 솔로 = NPC EntityId·salt 210, 파티 = PartyId·salt 212 (파티당 한 번). 마주쳤으면 그 네임드 ID, 아니면 0.
        /// 주 마을이 아닌 NPC, 사냥 외 활동은 전혀 바뀌지 않는다.
        /// </summary>
        public int RollNamedThreatEncounter(int villageId, int rngOwnerId, float seedGameTime, int worldSeed, int salt)
        {
            NamedThreatRecord? record = AR.s.NamedThreat != null ? AR.s.NamedThreat.GetActiveForVillage(villageId) : null;
            if (record == null)
                return 0;
            return NamedThreatRules.RollEncounter(record.Stage, rngOwnerId, seedGameTime, worldSeed, salt) == true ? record.ThreatId : 0;
        }

        /// <summary>
        /// 마주친 NPC 한 명의 기록 (§6.3). 부상 = Deed MauledByThreat(칭호 스냅샷) + 네임드 피해자 목록, 도망 = 기록 없음.
        /// 결과와 상관없이 그 네임드 소문을 직접 겪은 것으로 얻는다(§6.2 — 목격자도 소문을 퍼뜨린다). 부상 처리(ApplyExpeditionWound)는 호출자가 먼저 한다.
        /// </summary>
        public void RecordNamedThreatEncounter(int entityId, NpcSaveData saveData, int threatId, bool wounded, bool severe, float when)
        {
            NamedThreatRecord? record = AR.s.NamedThreat != null ? AR.s.NamedThreat.GetActive(threatId) : null;
            if (record == null)
                return;
            AR.s.NamedThreat!.RecordEncounter(record, entityId, ResolveNpcDisplayName(saveData), saveData.VillageId, wounded, severe, when);
            if (wounded == true)
            {
                MarkSaveDirty(entityId, NpcSaveGroup.Deeds | NpcSaveGroup.Stories);
                var entry = new NpcDeedEntry(NpcDeedKind.MauledByThreat, -1, NpcKnowledgeLevel.Lived, when);
                entry.ParticipantSnapshots.Add(new NpcDeedParticipant(-1, record.Title));
                saveData.RecordDeed(entry);
                _narrative.InvalidateDialogues(entityId);
            }
            GiveNamedThreatRumor(entityId, record, false, when);
        }

        // ================= 소문 주인·먼 소문 (§8.1·§8.2) =================

        /// <summary>소문 주인 후보 수 = 그 마을에 살아 있는 주민(동행·결투 중 제외). 0이면 그 마을에 네임드가 나오지 않는다 (§4.2).</summary>
        public int CountNamedThreatResidents(int villageId)
        {
            VillageData? village = AR.s.Village.GetVillage(villageId);
            if (village == null)
                return 0;
            int count = 0;
            for (int i = 0; i < village.NpcEntityIds.Count; i++)
                if (IsNamedThreatResident(village.NpcEntityIds[i], villageId) == true)
                    count++;
            return count;
        }

        /// <summary>그 마을 주민 중 고정 무작위 1명 (ID 순 정렬 뒤 hash로 고른다). 후보가 없으면 -1.</summary>
        public int PickNamedThreatResident(int villageId, ulong hash)
        {
            VillageData? village = AR.s.Village.GetVillage(villageId);
            if (village == null)
                return -1;
            _narrativeCandidateScratch.Clear();
            for (int i = 0; i < village.NpcEntityIds.Count; i++)
            {
                int npcId = village.NpcEntityIds[i];
                if (IsNamedThreatResident(npcId, villageId) == true)
                    _narrativeCandidateScratch.Add(npcId);
            }
            if (_narrativeCandidateScratch.Count == 0)
                return -1;
            _narrativeCandidateScratch.Sort();
            int picked = _narrativeCandidateScratch[(int)(hash % (ulong)_narrativeCandidateScratch.Count)];
            _narrativeCandidateScratch.Clear();
            return picked;
        }

        private bool IsNamedThreatResident(int npcId, int villageId)
        {
            NpcSaveData? npc = GetSaveData(npcId);
            return npc != null && npc.VillageId == villageId && IsNpcAlive(npcId) == true &&
                   IsCompanionReserved(npcId) == false && IsCompanionInDungeon(npcId) == false && IsInPlayerDuel(npcId) == false;
        }

        /// <summary>
        /// 그 NPC에게 네임드 소문을 준다 (직접 앎, 출처 없음). distant = 주변 마을의 먼 소문(주 마을 방향만 안다, 부탁하지 않음).
        /// 소문에는 대상(네임드 ID)만 넣고 사실은 표시할 때 네임드 기록에서 읽는다 (§8.3). 교환 순위용 가치는 100 (§8.1).
        /// </summary>
        public void GiveNamedThreatRumor(int npcId, NamedThreatRecord record, bool distant, float when)
        {
            NpcSaveData? npc = GetSaveData(npcId);
            if (npc == null || IsNpcAlive(npcId) == false)
                return;
            var rumor = new NpcRumor
            {
                Kind = NpcRumorKind.NamedThreat, ThreatId = record.ThreatId, IsDistant = distant,
                VillageId = record.PrimaryVillageId, SlotIndex = -1, Generation = -1,
                EventGameTime = when, Value = NamedThreatRules.RumorValue, SourceNpcId = -1, SubjectNpcId = -1,
            };
            AddRumor(npc, rumor, when);
            _rumorCounters.CreatedNamedThreat++;
            RumorCreatedObserver?.Invoke(npcId, rumor);
        }

        // ================= 부탁 인스턴스 (§8.4·§10.3) =================

        /// <summary>출현 때 narrative 원장에 그 네임드의 부탁 인스턴스를 만든다. 부탁 칸에는 부탁 대사가 실제로 표시된 뒤에 넣는다.</summary>
        public NarrativeContentInstance? CreateNamedThreatNarrative(NamedThreatRecord record, int contentId)
        {
            string instanceId = NarrativeContentInstance.NamedThreatId(record.ThreatId);
            NarrativeContentInstance? existing = GetNarrativeInstance(instanceId);
            if (existing != null)
                return existing;
            NarrativeDefinitionSnapshot? definition = AR.s.Data.NarrativeCatalog.CopyNamedThreat(contentId);
            NarrativePolicySnapshot? policy = AR.s.Data.NarrativeCatalog.CopyCurrentPolicy();
            if (definition == null || policy == null)
            {
                Debug.LogWarning($"[NamedThreat] Narrative content {contentId} is missing; threat {record.ThreatId} has no request.");
                return null;
            }
            var instance = new NarrativeContentInstance
            {
                InstanceId = instanceId,
                ContentId = definition.Content.ContentId,
                Kind = NarrativeContentKind.NamedThreat,
                DefinitionHash = definition.Hash,
                PolicyHash = policy.Hash,
                CreatedGameTime = AR.s.Time.CurrentGameTime,
                VillageId = record.PrimaryVillageId,
                ThreatId = record.ThreatId,
                OwnerNpcId = record.RumorOwnerNpcId,
            };
            AddNarrativeInstance(instance, definition, policy, out _, out _);
            return instance;
        }

        /// <summary>
        /// 소문 주인이 바뀌었다 (이전 주인이 다른 이유로 죽음, §8.1·§8.4). 부탁한 NPC가 죽었으면 부탁을 비워 새 주인이 다시 부탁할 수 있게 한다.
        /// </summary>
        public void SetNamedThreatNarrativeOwner(int threatId, int ownerNpcId)
        {
            NarrativeContentInstance? instance = GetNarrativeInstance(NarrativeContentInstance.NamedThreatId(threatId));
            if (instance == null || instance.IsOpen == false)
                return;
            MarkNarrativeInstanceDirty(instance, NarrativeSaveGroup.Target | NarrativeSaveGroup.Introductions);
            instance.OwnerNpcId = ownerNpcId;
            if (instance.RequesterNpcId >= 0 && IsNpcAlive(instance.RequesterNpcId) == false)
            {
                instance.RequesterNpcId = -1;
                instance.RequestedGameTime = -1f;
            }
        }

        /// <summary>
        /// 네임드가 끝났다 (§7.2·§9.1). 플레이어 측 처치면 해결(부탁한 NPC에게 보고, 소문 전 처치면 소문 주인에게 직접 말하기),
        /// 아니면(사라짐·이론상의 다른 처치) 미해결 종료 — 부탁 칸을 비우고 부탁한 NPC가 다음 대화에서 끝 알림을 한다. 보답·행적 소문은 없다.
        /// </summary>
        public void EndNamedThreatNarrative(int threatId, bool playerParty, float now)
        {
            NarrativeContentInstance? instance = GetNarrativeInstance(NarrativeContentInstance.NamedThreatId(threatId));
            if (instance == null || instance.IsOpen == false)
                return;
            MarkNarrativeInstanceDirty(instance, NarrativeSaveGroup.All);
            instance.ProgressGameTime = now;
            instance.Revision++;
            if (playerParty == true)
            {
                instance.Progress = NarrativeRumorProgress.Resolved;
                return;
            }
            instance.Ended = true;
            ReleaseRequestOnEnd(instance);
        }

        // ================= 위치·이름 표현 (§8.3) =================

        /// <summary>
        /// 영역 위치 표현: "이 마을 북쪽으로 약 56걸음 떨어진". 뒤에 "사냥터"·"숲"·"들판"을 붙여 쓴다.
        /// 말하는 NPC가 다른 마을로 이주했으면 "예전 마을"로 말한다. 네임드가 옮겨 가지 않으므로 끝날 때까지 같은 말이다 (NT12).
        /// </summary>
        public static string DescribeThreatLocation(NamedThreatRecord record, int speakerVillageId)
        {
            VillageData? village = AR.s.Village.GetVillage(record.PrimaryVillageId);
            if (village == null)
                return "근처";
            Vector2 offset = record.Territory - village.Position;
            int bearing = NamedThreatRules.GetBearing(village.Position, record.Territory);
            string prefix = speakerVillageId == record.PrimaryVillageId ? "이 마을" : "예전 마을";
            string direction = bearing >= 0 ? NpcRumorPhrasing.BearingName(bearing) + "으로" : "가까이";
            return $"{prefix} {direction} 약 {Mathf.RoundToInt(offset.magnitude)}걸음 떨어진";
        }

        /// <summary>
        /// 먼 소문 위치: 말하는 NPC의 마을 중심에서 주 마을 중심을 본 방향·걸음 수 (§8.2). 영역 위치는 모른다.
        /// "여기서 남동쪽으로 약 300걸음 가면 있는 마을"
        /// </summary>
        public static string DescribeDistantThreatVillage(NamedThreatRecord record, int speakerVillageId)
        {
            VillageData? speaker = AR.s.Village.GetVillage(speakerVillageId);
            VillageData? primary = AR.s.Village.GetVillage(record.PrimaryVillageId);
            if (speaker == null || primary == null)
                return "어느 마을";
            int bearing = NamedThreatRules.GetBearing(speaker.Position, primary.Position);
            string direction = bearing >= 0 ? NpcRumorPhrasing.BearingName(bearing) + "으로" : "가까이";
            return $"여기서 {direction} 약 {Mathf.RoundToInt((primary.Position - speaker.Position).magnitude)}걸음 가면 있는 마을";
        }

        /// <summary>문구 슬롯을 채운다: {location} = 영역 위치, {threatTitle} = 칭호 (§11.3).</summary>
        public static string FillNamedThreatSlots(string text, NamedThreatRecord record, int speakerVillageId)
            => text.Replace("{location}", DescribeThreatLocation(record, speakerVillageId)).Replace("{threatTitle}", record.Title);

        // ================= 부탁·보고 대화 (§8.4·§9.3) =================

        // 부탁 화제 (고정): 작성 문구 그대로(LLM 우회). 칭호가 공개된 뒤에만 만든다(IsRequestable).
        private DialogueTopic? BuildNamedThreatRequestTopic(NpcSaveData save, NarrativeContentInstance instance, NarrativeContentDefinition content)
        {
            NamedThreatRecord? record = AR.s.NamedThreat != null ? AR.s.NamedThreat.GetActive(instance.ThreatId) : null;
            if (record == null)
                return null;
            var topic = new DialogueTopic
            {
                Type = DialogueTopicType.NamedThreatRequest, Category = DialogueTopicCategory.Rumor, Pinned = true,
                BaseLine = FillNamedThreatSlots(content.IntroText, record, save.VillageId),
                NarrativeInstanceId = instance.InstanceId, NamedThreatId = record.ThreatId,
            };
            topic.Facts.Add("알려진 흔적: " + content.SignText);
            return topic;
        }

        // 진행 확인 화제 (고정): "아직 소식이 없소? 그놈은 여전히 ○○ 사냥터를 돌아다닌다오."
        private DialogueTopic? BuildNamedThreatProgressTopic(NpcSaveData save, NarrativeContentInstance held)
        {
            NamedThreatRecord? record = AR.s.NamedThreat != null ? AR.s.NamedThreat.GetActive(held.ThreatId) : null;
            if (record == null)
                return null;
            var topic = new DialogueTopic
            {
                Type = DialogueTopicType.NamedThreatRequestProgress, Category = DialogueTopicCategory.Rumor, Pinned = true,
                BaseLine = $"아직 소식이 없소? 그놈은 여전히 {DescribeThreatLocation(record, save.VillageId)} 사냥터를 돌아다닌다오.",
                NarrativeInstanceId = held.InstanceId, NamedThreatId = record.ThreatId,
            };
            topic.Facts.Add($"이 NPC가 플레이어에게 {record.Title} 사냥을 부탁했고 아직 해결되지 않았다.");
            return topic;
        }

        // 끝 알림 (§7.2): 사라졌으면 "어디론가 사라졌나 보오", 이론상의 다른 처치면 "누군가 잡았다더군". 보답은 없다.
        private static string NamedThreatEndNotice(NarrativeContentInstance instance)
        {
            NamedThreatRecord? record = AR.s.NamedThreat != null ? AR.s.NamedThreat.FindRecord(instance.ThreatId) : null;
            return record != null && record.State == NamedThreatState.Slain
                ? "누군가 그놈을 잡았다더군. 신경 써 줘서 고맙네."
                : "그놈이 요즘 통 안 보인다더군. 어디론가 사라졌나 보오.";
        }

        // 처치 행적 소문 (§9.3): 대상 = 그 네임드의 부탁 인스턴스. 슬롯·세대 칸은 쓰지 않는다.
        private static NpcRumor CreateThreatDeedRumor(NarrativeContentInstance instance, int value, int sourceNpcId, bool reacted)
        {
            NpcRumor rumor = CreateDeedRumor(PlayerDeedKind.ThreatSlain, instance.InstanceId, instance.VillageId, -1, -1,
                instance.ProgressGameTime, value, sourceNpcId, reacted);
            rumor.ThreatId = instance.ThreatId;
            return rumor;
        }

        /// <summary>그 네임드에게 다친 NPC인가 (관련도 피해자 ×3, §9.3). 마지막 부상 또는 네임드 기록의 피해자 목록으로 본다.</summary>
        public bool IsNamedThreatVictim(NpcSaveData npc, int threatId)
        {
            if (threatId <= 0)
                return false;
            if (npc.LastWoundThreatId == threatId)
                return true;
            NamedThreatRecord? record = AR.s.NamedThreat != null ? AR.s.NamedThreat.FindRecord(threatId) : null;
            return record != null && record.HasVictim(npc.EntityId) == true;
        }

        private static string ThreatTitleOrDefault(int threatId)
        {
            string title = AR.s.NamedThreat != null ? AR.s.NamedThreat.GetTitle(threatId) : string.Empty;
            return string.IsNullOrEmpty(title) == false ? title : "그 짐승";
        }
    }
}
