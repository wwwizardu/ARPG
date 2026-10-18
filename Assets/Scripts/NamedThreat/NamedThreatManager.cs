#nullable enable
using System;
using System.Collections.Generic;
using ARPG.Component;
using ARPG.Data;
using ARPG.Npc;
using ARPG.Npc.Narrative;
using ARPG.Tables;
using ARPG.Village;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace ARPG.NamedThreat
{
    /// <summary>
    /// 네임드 위협 소유자 (NAMED_THREAT_DESIGN §12.1). 세계 저장 기록(WorldSaveRoot.NamedThreats)을 단독으로 수정하고,
    /// 출현·단계·사라짐(감독), 엔티티 생성·제거(존재), 흔적 자막, 처치 묶음 저장을 맡는다.
    /// NPC 쪽 지식(소문·Deed·부탁 칸)과 narrative 원장은 NpcManager가 가진다 — 여기서는 NpcManager API만 부른다.
    /// 지속 상태는 모두 저장 기록에 있고, 이 클래스의 필드는 엔티티 색인·재시도 시각·scratch 같은 런타임 값뿐이다 (Reset에서 비운다).
    /// </summary>
    public partial class NamedThreatManager
    {
        // 화면 안 몬스터와 같은 활성화 거리 (MonsterManager 기본값)
        private const float ActivationDistance = 25f;
        private const float DeactivationDistance = 30f;
        // 처치 묶음 저장이 실패하면 이 간격(실제 초)으로 다시 저장한다 (던전 완료 재시도와 같은 3초)
        private const float KillSaveRetrySeconds = 3f;
        // 마주침·단계·들음으로 바뀐 기록은 World 파트만 이 간격(실제 초) 안에서 한 번 저장한다 (NPC 쪽은 D47대로 다음 저장에 실린다)
        private const float WorldSaveIntervalSeconds = 30f;

        private bool _initialized;
        private int _lifetime;
        // 네임드 ID → 실체화된 몬스터 EntityId (화면 근처에만 있다)
        private readonly Dictionary<int, int> _entityByThreat = new();
        // 생성 요청 중인 네임드 (Addressables 비동기)
        private readonly HashSet<int> _spawningThreats = new();
        // 이번 배회 지점에서 걸을 수 있는 칸을 못 찾은 네임드 → 그 배회 지점 번호 (§5.4: 이번 지점에서는 만들지 않는다)
        private readonly Dictionary<int, int> _failedSpawnStep = new();
        private bool _killSavePending;
        private float _nextKillSaveTime;
        // 출현·사라짐: 기록·소문·부탁 인스턴스를 한 번에 (World·NpcState·Narrative, §10.4)
        private bool _recordSavePending;
        // 마주침·단계 전이·들음: World만
        private bool _worldSavePending;
        private float _nextWorldSaveTime;
        // 흔적 자막 (§8.5): 지금 들어가 있는 영역의 네임드와 다음 갱신 시각(실제 시간)
        private int _trailThreatId;
        private float _nextTrailTime;
        // scratch — 사용 직전에 비운다
        private readonly int[] _candidateOrderScratch = new int[NamedThreatRules.PlacementCandidateCount];
        private readonly List<NamedThreatTable> _templateScratch = new();
        private readonly List<VillageData> _villageScratch = new();
        private readonly List<int> _threatIdScratch = new();

        public bool IsInitialized => _initialized;
        public NamedThreatWorldState State => AR.s.Data.NamedThreats;
        public IReadOnlyList<NamedThreatRecord> ActiveThreats => AR.s.Data.NamedThreats.Active;

        public void Initialize()
        {
            ApplyLoadCorrections(AR.s.Time != null ? AR.s.Time.CurrentGameTime : 0f);
#if UNITY_EDITOR
            ValidateInvariants("load");
#endif
            _initialized = true;
        }

        public void Reset()
        {
            _lifetime++;
            _initialized = false;
            _entityByThreat.Clear();
            _spawningThreats.Clear();
            _failedSpawnStep.Clear();
            _killSavePending = false;
            _nextKillSaveTime = 0f;
            _recordSavePending = false;
            _worldSavePending = false;
            _nextWorldSaveTime = 0f;
            _trailThreatId = 0;
            _nextTrailTime = 0f;
            _templateScratch.Clear();
            _villageScratch.Clear();
            _threatIdScratch.Clear();
        }

        // ================= 조회 =================

        public NamedThreatRecord? GetActive(int threatId)
        {
            List<NamedThreatRecord> active = State.Active;
            for (int i = 0; i < active.Count; i++)
                if (active[i].ThreatId == threatId)
                    return active[i];
            return null;
        }

        /// <summary>활성 또는 끝난 지 14게임일 안의 기록. 없으면 null.</summary>
        public NamedThreatRecord? FindRecord(int threatId)
        {
            if (threatId <= 0)
                return null;
            NamedThreatRecord? active = GetActive(threatId);
            if (active != null)
                return active;
            List<NamedThreatRecord> ended = State.Ended;
            for (int i = 0; i < ended.Count; i++)
                if (ended[i].ThreatId == threatId)
                    return ended[i];
            return null;
        }

        public bool IsActive(int threatId) => threatId > 0 && GetActive(threatId) != null;

        /// <summary>그 마을에 붙은 활성 네임드 (마을당 최대 1마리, NT2). 없으면 null.</summary>
        public NamedThreatRecord? GetActiveForVillage(int villageId)
        {
            if (villageId < 0)
                return null;
            List<NamedThreatRecord> active = State.Active;
            for (int i = 0; i < active.Count; i++)
                if (active[i].PrimaryVillageId == villageId)
                    return active[i];
            return null;
        }

        public string GetTitle(int threatId)
        {
            NamedThreatRecord? record = FindRecord(threatId);
            return record != null ? record.Title : string.Empty;
        }

        public NamedThreatTable? GetTemplate(NamedThreatRecord record) => AR.s.Data.GetNamedThreat(record.TemplateId);

        /// <summary>소식창에 쓰는 이름: 플레이어가 그 네임드 소문을 들었으면 칭호, 아니면 "큰 짐승" (§6.5).</summary>
        public static string GetFeedName(NamedThreatRecord record)
            => record.PlayerHeard == true && string.IsNullOrEmpty(record.Title) == false ? record.Title : "큰 짐승";

        /// <summary>흔적 문구 (템플릿 문구 행의 SignText). 없으면 기본 문구.</summary>
        public string GetSignText(NamedThreatRecord record)
        {
            NamedThreatTable? template = GetTemplate(record);
            NarrativeContentDefinition? content = template != null ? AR.s.Data.NarrativeCatalog.FindNamedThreatContent(template.NarrativeContentId) : null;
            return content != null && string.IsNullOrWhiteSpace(content.SignText) == false
                ? content.SignText.Trim()
                : "큰 짐승이 지나간 자국이 남아 있다.";
        }

        /// <summary>전리품 판매 기준가 (행적 소문 가치, §9.3). 없으면 0.</summary>
        public int GetTrophyValue(int threatId)
        {
            NamedThreatRecord? record = FindRecord(threatId);
            NamedThreatTable? template = record != null ? GetTemplate(record) : null;
            ItemTable? trophy = template != null ? AR.s.Data.GetItem(template.TrophyItemId) : null;
            return trophy != null ? Mathf.Max(0, trophy.BasePrice) : 0;
        }

        // ================= 변경 =================

        /// <summary>기록 안의 목록·값을 바꾸기 전에 부른다 (SAVE_SNAPSHOT S4 — 변경 함수의 입구에서 표시).</summary>
        public void MarkDirty() => AR.s.Data.MarkWorldSaveDirty(WorldSaveGroup.NamedThreat);

        /// <summary>그 네임드 화제가 플레이어에게 실제로 표시됐다 (§6.5). 소식창이 칭호·강조 줄을 쓰기 시작한다.</summary>
        public void MarkPlayerHeard(int threatId)
        {
            NamedThreatRecord? record = GetActive(threatId);
            if (record == null || record.PlayerHeard == true)
                return;
            MarkDirty();
            record.PlayerHeard = true;
            _worldSavePending = true;
        }

        /// <summary>
        /// 주 마을 NPC가 사냥 중 마주쳤다 (§6.2·§6.3). 칭호 공개, 부상이면 피해 수·피해자 목록. 사망은 없다.
        /// NPC 쪽 기록(Deed·소문)은 NpcManager.RecordNamedThreatEncounter가 같은 호출 안에서 한다. 단계 전이는 다음 감독 판정이 본다.
        /// </summary>
        public void RecordEncounter(NamedThreatRecord record, int npcId, string npcName, int villageId, bool wounded, bool severe,
            float when)
        {
            MarkDirty();
            record.TitleRevealed = true;
            _worldSavePending = true;
            if (wounded == false)
                return;
            record.HarmCount++;
            if (record.Victims.Count >= NamedThreatRecord.MaxVictims)
                record.Victims.RemoveAt(0);
            record.Victims.Add(new NamedThreatVictim { NpcId = npcId, Name = npcName, Severe = severe, When = when, VillageId = villageId });
        }

        // ================= 처치 (§9.1) =================

        /// <summary>
        /// HP가 0이 된 엔티티가 네임드면 기록을 끝내고 처치 묶음을 확정한다 (System_HpCheck가 드랍 처리 전에 부른다).
        /// 몬스터 사망·드랍·파괴는 원래 경로가 그대로 처리한다. 네임드가 아니면 아무것도 하지 않는다.
        /// </summary>
        public void HandleDeath(int entityId, KillCreditComponent credit)
        {
            ComponentManager cm = AR.s.Component;
            if (_initialized == false || cm.TryGetComponent<NamedThreatComponent>(entityId, out NamedThreatComponent named) == false)
                return;
            if (_entityByThreat.TryGetValue(named.ThreatId, out int tracked) == true && tracked == entityId)
                _entityByThreat.Remove(named.ThreatId);
            NamedThreatRecord? record = GetActive(named.ThreatId);
            if (record == null)
                return;

            float now = AR.s.Time.CurrentGameTime;
            Vector2 position = cm.TryGetComponent<TransformComponent>(entityId, out TransformComponent transform) == true
                ? transform.Position : record.Territory;
            bool player = credit.PlayerParty;
            EndRecord(record, NamedThreatState.Slain, now, player);
            State.NextAppearGameTime = Mathf.Max(State.NextAppearGameTime, now + NamedThreatRules.AppearIntervalHours);
            AR.s.Npc.EndNamedThreatNarrative(record.ThreatId, player, now);

            NamedThreatTable? template = GetTemplate(record);
            if (player == true && template != null && template.TrophyItemId > 0)
                DropTrophyAsync(template.TrophyItemId, new Vector3(position.x, position.y, -0.01f)).Forget();
            if (player == true)
            {
                AR.s.UI.SetNotify($"{Josa(record.Title, "을", "를")} 쓰러뜨렸습니다.");
                AR.s.UI.PushWorldFeed($"{Josa(record.Title, "을", "를")} 쓰러뜨렸다는 소식이 퍼집니다.", true);
            }
            else if (record.PlayerHeard == true)
                AR.s.UI.PushWorldFeed($"누군가 {Josa(record.Title, "을", "를")} 잡았다는 소문이 돕니다.", true);
#if UNITY_EDITOR
            ValidateInvariants("kill");
#endif
            Debug.Log($"[NamedThreat] Slain id={record.ThreatId} title={record.Title} playerParty={player} at {now:F1}h");
            // 처치 묶음 저장 (§9.1): World·Player·NpcState·Narrative를 한 번에. 실패하면 던전 완료처럼 다시 시도한다.
            _killSavePending = true;
            _nextKillSaveTime = 0f;
            TryCommitKillSaveAsync().Forget();
        }

        private static async UniTaskVoid DropTrophyAsync(int itemId, Vector3 position)
        {
            try
            {
                if (await AR.s.Item.CreateItem(itemId, 1, position) == false)
                    Debug.LogWarning($"[NamedThreat] Trophy drop failed: item {itemId}");
            }
            catch (Exception ex) { Debug.LogError("[NamedThreat] Trophy drop error: " + ex); }
        }

        /// <summary>남은 저장 요청을 처리한다 (감독 시스템이 매 판정마다 부른다). 처치 묶음 → 출현·사라짐 → World 순으로 하나만.</summary>
        public void RetryPendingSaves()
        {
            if (_killSavePending == true)
            {
                if (Time.unscaledTime >= _nextKillSaveTime)
                    TryCommitKillSaveAsync().Forget();
                return;
            }
            if (AR.s.Data.IsSaving == true)
                return;
            if (_recordSavePending == true)
                TryCommitRecordSaveAsync(SaveScope.World | SaveScope.NpcState | SaveScope.Narrative).Forget();
            else if (_worldSavePending == true && Time.unscaledTime >= _nextWorldSaveTime)
                TryCommitRecordSaveAsync(SaveScope.World).Forget();
        }

        private async UniTaskVoid TryCommitKillSaveAsync()
        {
            int lifetime = _lifetime;
            _nextKillSaveTime = Time.unscaledTime + KillSaveRetrySeconds;
            bool saved = false;
            try
            {
                saved = await AR.s.Data.SaveAsync(SaveScope.Player | SaveScope.World | SaveScope.Map | SaveScope.NpcState |
                                                  SaveScope.Narrative);
            }
            catch (Exception ex) { Debug.LogError("[NamedThreat] Kill save error: " + ex); }
            if (lifetime != _lifetime)
                return;
            if (saved == true)
                _killSavePending = false;
            else
                Debug.Log("[NamedThreat] 처치 저장 실패 — 잠시 뒤 다시 저장합니다.");
        }

        private async UniTaskVoid TryCommitRecordSaveAsync(SaveScope scope)
        {
            int lifetime = _lifetime;
            bool full = (scope & SaveScope.NpcState) != 0;
            if (full == true)
                _recordSavePending = false;
            _worldSavePending = false;
            _nextWorldSaveTime = Time.unscaledTime + WorldSaveIntervalSeconds;
            bool saved = false;
            try
            {
                saved = await AR.s.Data.SaveAsync(scope);
            }
            catch (Exception ex) { Debug.LogError("[NamedThreat] Record save error: " + ex); }
            if (lifetime != _lifetime || saved == true)
                return;
            // 결투 중·던전 전환 중 등으로 거절됐으면 다음 판정에서 다시 요청한다
            if (full == true)
                _recordSavePending = true;
            else
                _worldSavePending = true;
        }

        // 기록을 끝난 목록으로 옮긴다 (처치·사라짐 공통). 소문은 무효가 되어 다음 표시·교환 때 정리된다 (§7.2·§8.3).
        private void EndRecord(NamedThreatRecord record, NamedThreatState state, float now, bool playerParty)
        {
            MarkDirty();
            State.Active.Remove(record);
            record.State = state;
            record.EndedGameTime = now;
            record.SlainByPlayerParty = playerParty;
            State.Ended.Add(record);
            _failedSpawnStep.Remove(record.ThreatId);
            if (_trailThreatId == record.ThreatId)
                _trailThreatId = 0;
        }

        // ================= 로드 보정 (§10.1) =================

        private void ApplyLoadCorrections(float now)
        {
            NamedThreatWorldState state = State;
            state.Sanitize();
            bool changed = false;
            List<NamedThreatRecord> active = state.Active;
            for (int i = 0; i < active.Count; i++)
            {
                NamedThreatRecord record = active[i];
                bool invalid = AR.s.Data.GetNamedThreat(record.TemplateId) == null || string.IsNullOrEmpty(record.Title) == true ||
                               AR.s.Village.GetVillage(record.PrimaryVillageId) == null;
                // 한 마을에 활성 네임드가 둘 이상이면(손상된 세이브) 먼저 나온 것만 남긴다.
                for (int j = 0; j < active.Count && invalid == false; j++)
                {
                    NamedThreatRecord other = active[j];
                    if (j != i && other.PrimaryVillageId == record.PrimaryVillageId &&
                        (other.AppearedGameTime < record.AppearedGameTime ||
                         (other.AppearedGameTime == record.AppearedGameTime && other.ThreatId < record.ThreatId)))
                        invalid = true;
                }
                if (invalid == true)
                {
                    _threatIdScratch.Add(record.ThreatId);
                    continue;
                }
                // 없는 NPC는 소문 주인·먼 소문 목록에서 뺀다 (다음 감독 판정에서 다시 고른다).
                if (record.RumorOwnerNpcId >= 0 && AR.s.Npc.GetSaveData(record.RumorOwnerNpcId) == null)
                {
                    record.RumorOwnerNpcId = -1;
                    changed = true;
                }
                for (int f = record.FarRumorNpcIds.Count - 1; f >= 0; f--)
                {
                    if (AR.s.Npc.GetSaveData(record.FarRumorNpcIds[f]) != null)
                        continue;
                    record.FarRumorNpcIds.RemoveAt(f);
                    changed = true;
                }
            }
            for (int i = 0; i < _threatIdScratch.Count; i++)
            {
                NamedThreatRecord? record = GetActive(_threatIdScratch[i]);
                if (record == null)
                    continue;
                Debug.LogWarning($"[NamedThreat] Load correction: ended invalid threat {record.ThreatId} (template {record.TemplateId}, village {record.PrimaryVillageId}).");
                EndRecord(record, NamedThreatState.Vanished, now, false);
                AR.s.Npc.EndNamedThreatNarrative(record.ThreatId, false, now);
                changed = true;
            }
            _threatIdScratch.Clear();
            if (PruneEndedRecords(now) == true)
                changed = true;
            if (changed == true)
                MarkDirty();
        }

        // 끝난 지 14게임일이 지난 기록을 지운다 (§10.1). 칭호 사용 기록(UsedTitles)은 남긴다.
        private bool PruneEndedRecords(float now)
        {
            List<NamedThreatRecord> ended = State.Ended;
            bool changed = false;
            for (int i = ended.Count - 1; i >= 0; i--)
            {
                if (now - ended[i].EndedGameTime <= NamedThreatRules.EndedRetentionHours)
                    continue;
                if (changed == false)
                    MarkDirty();
                ended.RemoveAt(i);
                changed = true;
            }
            return changed;
        }

#if UNITY_EDITOR
        // 가벼운 상시 검사 (§13): 로드·단계 전이·처치 때 기록 불변식만 본다. 에디터 전용.
        private void ValidateInvariants(string when)
        {
            List<NamedThreatRecord> active = State.Active;
            for (int i = 0; i < active.Count; i++)
            {
                NamedThreatRecord record = active[i];
                if (record.Stage < 1 || record.Stage > NamedThreatRules.MaxStage)
                    Debug.LogError($"[NamedThreat] Invariant({when}): threat {record.ThreatId} stage {record.Stage} out of range.");
                if (string.IsNullOrEmpty(record.Title) == true)
                    Debug.LogError($"[NamedThreat] Invariant({when}): threat {record.ThreatId} has no title.");
                if (AR.s.Village.GetVillage(record.PrimaryVillageId) == null)
                    Debug.LogError($"[NamedThreat] Invariant({when}): threat {record.ThreatId} primary village {record.PrimaryVillageId} missing.");
                for (int j = i + 1; j < active.Count; j++)
                    if (active[j].PrimaryVillageId == record.PrimaryVillageId)
                        Debug.LogError($"[NamedThreat] Invariant({when}): village {record.PrimaryVillageId} has two active threats ({record.ThreatId}, {active[j].ThreatId}).");
            }
        }
#endif

        /// <summary>받침에 맞는 조사 (NpcRumorPhrasing.Josa와 같다).</summary>
        public static string Josa(string word, string withFinal, string withoutFinal) => NpcRumorPhrasing.Josa(word, withFinal, withoutFinal);
    }
}
