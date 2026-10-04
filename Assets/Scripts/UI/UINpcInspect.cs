#nullable enable
using System.Collections.Generic;
using ARPG.Base;
using ARPG.Component;
using ARPG.Data;
using ARPG.Npc;
using ARPG.Tables;
using ARPG.Utility;
using ARPG.Npc.Narrative;
using Cysharp.Threading.Tasks;
using Unity.AppUI.UI;
using UnityEngine;
using UnityEngine.UIElements;
using Text = Unity.AppUI.UI.Text;
using Button = Unity.AppUI.UI.Button;
using GE = GlobalEnum;

namespace ARPG.UI
{
    /// <summary>
    /// NPC 검사 패널 — 장비/인벤토리/성향/스탯 풀 노출.
    /// 읽기 전용: 슬롯 호버 시 툴팁만, 클릭은 무시.
    /// 진입점: ServiceUIRouter (E키 근접 시).
    /// </summary>
    public class UINpcInspect : UIBaseForm
    {
        private UIDocument? _document;
        private IconButton? _closeBtn;
        private Text? _nameText;
        private Text? _jobText;
        private Text? _descText;
        private VisualElement? _equipGrid;
        private VisualElement? _invGrid;
        private VisualElement? _statList;
        private VisualElement? _personalityList;
        private VisualElement? _relationList;
        private VisualElement? _activityList;
        private ListView? _historyList;
        private readonly List<NpcDeedEntry> _displayedDeeds = new();

        private int _entityId = -1;
        private NpcSaveData? _saveData;
        private VisualElement? _lastRoot;

        // 내러티브 검증 스파이크 — 대화 버튼 연결용
        private Text? _dialogueLabel;
        private VisualElement? _giftList;
        private Button? _companionBtn;
        private Button? _duelBtn;
        private static readonly bool _enableWishSpike = false;

        public override void Initialize(string inName, bool isForm = false)
        {
            base.Initialize(inName, isForm);

            _document = GetComponent<UIDocument>();
            if (_document == null)
            {
                Debug.LogError("[UINpcInspect] UIDocument 컴포넌트 없음 — prefab 확인 필요");
                return;
            }

            EnsureBound();
        }

        private void EnsureBound()
        {
            if (_document == null) return;
            VisualElement root = _document.rootVisualElement;
            if (root == null) return;
            if (root == _lastRoot) return;

            _lastRoot = root;

            _closeBtn = root.Q<IconButton>("close-btn");
            _nameText = root.Q<Text>("name-text");
            _jobText = root.Q<Text>("job-text");
            _descText = root.Q<Text>("desc-text");
            _equipGrid = root.Q<VisualElement>("equip-grid");
            _invGrid = root.Q<VisualElement>("inv-grid");
            _statList = root.Q<VisualElement>("stat-list");
            _personalityList = root.Q<VisualElement>("personality-list");
            _relationList = root.Q<VisualElement>("relation-list");
            _activityList = root.Q<VisualElement>("activity-list");
            _historyList = root.Q<ListView>("history-list");

            if (_historyList != null)
            {
                _historyList.makeItem = MakeHistoryRow;
                _historyList.bindItem = BindHistoryRow;
                _historyList.fixedItemHeight = 48;
                _historyList.selectionType = SelectionType.None;
                _historyList.itemsSource = _displayedDeeds;
            }

            if (_closeBtn != null) _closeBtn.clicked += () => Close();

            BuildInteractionBar(root);
        }

        public void Bind(int entityId)
        {
            EnsureBound();
            _entityId = entityId;
            _saveData = AR.s.Npc?.GetSaveData(entityId);

            if (_saveData == null)
            {
                Debug.LogWarning($"[UINpcInspect] Bind 실패 — entityId={entityId} saveData 없음");
                return;
            }

            RefreshAll();
        }

        public override void OnOpen()
        {
            base.OnOpen();
            EnsureBound();
            if (_saveData != null)
                RefreshAll();
        }

        private void RefreshAll()
        {
            if (_saveData == null) return;

            NpcTable? table = AR.s.Data?.GetNpc(_saveData.NpcTableId);
            // 인스턴스 고유 이름 우선. 구 세이브/미시드 방어로 테이블 이름 → #EntityId 폴백.
            string name = string.IsNullOrEmpty(_saveData.Name) == false
                ? _saveData.Name
                : (table?.Name ?? $"NPC #{_entityId}");
            string job = _saveData.JobType != GE.JobType.None
                ? _saveData.JobType.ToString()
                : (table?.JobType.ToString() ?? string.Empty);

            if (_nameText != null) _nameText.text = name;
            if (_jobText != null) _jobText.text = job;
            if (_descText != null) _descText.text = _saveData.Description ?? string.Empty;

            RebuildEquipGrid();
            RebuildInventoryGrid();
            RebuildPersonality();
            RebuildRelations();
            RebuildActivity();
            RebuildRumors();
            RebuildHistory();
            RebuildStats();
            RefreshCompanionButton();
            RefreshDuelButton();
        }

        private void RebuildHistory()
        {
            if (_historyList == null || _saveData == null)
                return;

            _displayedDeeds.Clear();
            for (int i = _saveData.Deeds.Count - 1; i >= 0; i--)
            {
                NpcDeedEntry entry = _saveData.Deeds[i];
                if (entry == null)
                    continue;
                _displayedDeeds.Add(entry);
            }

            NpcDeedEntry? theft = _saveData.PersonalStories?.TheftVictim;
            if (theft != null)
            {
                int index = 0;
                while (index < _displayedDeeds.Count && _displayedDeeds[index].When > theft.When)
                    index++;
                _displayedDeeds.Insert(index, theft);
            }

            _historyList.Rebuild();
        }

        private static VisualElement MakeHistoryRow()
        {
            VisualElement row = new();
            row.AddToClassList("npc-history-row");
            Text description = new();
            description.name = "history-description";
            description.AddToClassList("npc-history-description");
            Text time = new();
            time.name = "history-time";
            time.AddToClassList("npc-history-time");
            row.Add(description);
            row.Add(time);
            return row;
        }

        private void BindHistoryRow(VisualElement row, int index)
        {
            if (index < 0 || index >= _displayedDeeds.Count)
                return;

            NpcDeedEntry entry = _displayedDeeds[index];
            Text? description = row.Q<Text>("history-description");
            Text? time = row.Q<Text>("history-time");
            if (description != null)
                description.text = GetHistoryDescription(entry);
            if (time != null)
                time.text = $"게임 시간 {entry.When:F1}";
        }

        private string GetHistoryDescription(NpcDeedEntry entry)
        {
            return entry.Kind switch
            {
                // 변화량은 그 상대와의 원정 누적, 시각은 마지막 원정 (D108 — 상대별 1건)
                NpcDeedKind.PartyAffinityChanged =>
                    $"{GetOtherParticipantName(entry)}와 함께 원정하며 쌓인 호감도 {FormatSignedValue(entry.AffinityDelta)}",
                // Activity: P2c부터 Delve 부상만 DungeonDelve로 기록한다. None(구 기록 포함)은 사냥.
                // 가벼운 부상은 Deed가 없고(D91) 활동 목록의 "부상" 한 줄로 요약된다. Wounded는 로드 때 합쳐져 보통 남아 있지 않다.
                // 중상·공격받은 부상 Deed는 최소 정보(시각·가해자)만 가진다 (D91 후속).
                NpcDeedKind.SevereWound =>
                    $"중상 · {NpcRumorPhrasing.AgoPhrase(entry.When, AR.s.Time.CurrentGameTime)}",
                NpcDeedKind.AttackedWound =>
                    $"{GetOtherParticipantName(entry)}에게 공격받아 부상 · {NpcRumorPhrasing.AgoPhrase(entry.When, AR.s.Time.CurrentGameTime)}",
                NpcDeedKind.Wounded => entry.Treated == true
                    ? $"{GetWoundedActivityLabel(entry)} 중 부상 · 치료받음 · {entry.RecoveryEndGameTime:F1}시간까지 회복"
                    : $"{GetWoundedActivityLabel(entry)} 중 부상 · {entry.RecoveryEndGameTime:F1}시간까지 회복",
                NpcDeedKind.NotableItemAcquired => string.IsNullOrEmpty(entry.ItemDisplayName)
                    ? "주요 아이템 획득"
                    : $"{entry.ItemDisplayName} 획득",
                NpcDeedKind.DuelWon => $"{GetOtherParticipantName(entry)}와 결투에서 승리",
                NpcDeedKind.DuelLost => $"{GetOtherParticipantName(entry)}와 결투에서 패배",
                NpcDeedKind.DuelDrawn => $"{GetOtherParticipantName(entry)}와 결투에서 무승부",
                NpcDeedKind.Fell => "사망",
                NpcDeedKind.VentureFailed => "원정 실패",
                NpcDeedKind.DungeonCleared => CountOtherParticipants(entry) switch
                {
                    0 => "던전 공략 완료 (혼자)",
                    1 => $"{NpcRumorPhrasing.Josa(GetOtherParticipantName(entry), "과", "와")} 함께 던전 공략 완료",
                    int n => $"{GetOtherParticipantName(entry)} 외 {n - 1}명과 함께 던전 공략 완료",
                },
                // 절도 (D106): 피해자 — 안 들킨 도난(범인 모름)·현장 적발, 도둑 — 현장 적발 (안 들킨 절도는 도둑 쪽 기록 없음)
                NpcDeedKind.ItemStolen => $"누군가에게 {NpcRumorPhrasing.Josa(GetDeedItemName(entry), "을", "를")} 도둑맞음",
                NpcDeedKind.CaughtThiefInAct =>
                    $"{NpcRumorPhrasing.Josa(GetOtherParticipantName(entry), "이", "가")} " +
                    $"{NpcRumorPhrasing.Josa(GetDeedItemName(entry), "을", "를")} 훔치려다 걸림",
                NpcDeedKind.CaughtStealing =>
                    $"{GetOtherParticipantName(entry)}의 {NpcRumorPhrasing.Josa(GetDeedItemName(entry), "을", "를")} 훔치려다 걸림",
                // 늦은 발각 (D103·D106): 피해자 — 범인을 알게 됨, 도둑 — 들통남 (도둑 쪽 기록은 이때 처음 생김)
                NpcDeedKind.TheftDiscovered =>
                    $"{NpcRumorPhrasing.Josa(GetDeedItemName(entry), "을", "를")} 훔쳐 간 것이 " +
                    $"{GetOtherParticipantName(entry)}{(EndsWithFinalConsonant(GetOtherParticipantName(entry)) ? "이었음" : "였음")}을 알게 됨",
                NpcDeedKind.TheftExposed =>
                    $"{GetOtherParticipantName(entry)}의 {NpcRumorPhrasing.Josa(GetDeedItemName(entry), "을", "를")} 훔친 것이 들통남",
                _ => "알 수 없는 사건",
            };
        }

        private static string GetWoundedActivityLabel(NpcDeedEntry entry)
        {
            return entry.Activity == NpcActivityKind.DungeonDelve ? "던전 원정" : "사냥";
        }

        private static string GetDeedItemName(NpcDeedEntry entry)
        {
            return string.IsNullOrEmpty(entry.ItemDisplayName) ? "물건" : entry.ItemDisplayName;
        }

        // "○○였음/○○이었음" 받침 판정 (NpcRumorPhrasing.Josa와 같은 규칙 — 한글이 아니면 받침 없음)
        private static bool EndsWithFinalConsonant(string word)
        {
            return NpcRumorPhrasing.Josa(word, "1", "0").EndsWith("1");
        }

        private int CountOtherParticipants(NpcDeedEntry entry)
        {
            int count = 0;
            for (int i = 0; i < entry.Participants.Count; i++)
            {
                if (entry.Participants[i] != _entityId)
                    count++;
            }
            return count;
        }

        private string GetOtherParticipantName(NpcDeedEntry entry)
        {
            for (int i = 0; i < entry.ParticipantSnapshots.Count; i++)
            {
                NpcDeedParticipant participant = entry.ParticipantSnapshots[i];
                if (participant.EntityId == _entityId)
                    continue;
                if (string.IsNullOrEmpty(participant.DisplayName) == false)
                    return participant.DisplayName;
            }

            for (int i = 0; i < entry.Participants.Count; i++)
            {
                int participantId = entry.Participants[i];
                if (participantId == _entityId)
                    continue;

                NpcSaveData? other = AR.s.Npc?.GetSaveData(participantId);
                if (other != null)
                {
                    if (string.IsNullOrEmpty(other.Name) == false)
                        return other.Name;
                    return AR.s.Data?.GetNpc(other.NpcTableId)?.Name ?? $"NPC #{participantId}";
                }
            }

            return "동료";
        }

        private static string FormatSignedValue(int value)
        {
            return value >= 0 ? $"+{value}" : value.ToString();
        }

        private void RebuildActivity()
        {
            if (_activityList == null || _saveData == null)
                return;

            _activityList.Clear();

            NpcActivityState activity = _saveData.Activity;
            AddActivityRow("현재 활동", GetActivityLabel(activity));

            if (activity.PhaseEndGameTime > AR.s.Time.CurrentGameTime &&
                (activity.Kind != NpcActivityKind.None || activity.Phase == NpcActivityPhase.Wounded))
            {
                float remainingHours = activity.PhaseEndGameTime - AR.s.Time.CurrentGameTime;
                AddActivityRow("남은 시간", $"{remainingHours:F1}시간");
            }

            // 원정 부상 누적 요약 (D91): "N회 (최근: 어제, 던전 원정 중)". 중상은 행적 목록에 따로 나온다.
            if (_saveData.WoundCount > 0)
            {
                string ago = NpcRumorPhrasing.AgoPhrase(_saveData.LastWoundGameTime, AR.s.Time.CurrentGameTime);
                string where = _saveData.LastWoundActivity == NpcActivityKind.DungeonDelve ? "던전 원정 중" : "사냥 중";
                AddActivityRow("부상", $"{_saveData.WoundCount}회 (최근: {ago}, {where})");
            }

            if (activity.LastHuntOutcome == NpcHuntOutcome.None)
                return;

            AddActivityRow("마지막 사냥", GetHuntOutcomeLabel(activity.LastHuntOutcome));
            if (activity.LastHuntOutcome == NpcHuntOutcome.Success)
                AddActivityRow("획득 식량", $"+{activity.LastHuntFoodYield}");
        }

        /// <summary>
        /// 보유 소문 읽기 전용 목록 (NPC_AMBITION_DESIGN P2b, D79 — 검증용 최소 표시, 문구는 P2c에서 다듬는다).
        /// 대상이 사라진 소문은 보여 주지 않는다 (D72: 노출 직전 검증).
        /// </summary>
        private void RebuildRumors()
        {
            if (_activityList == null || _saveData == null || AR.s.Npc == null)
                return;

            int shown = 0;
            List<NpcRumor> rumors = _saveData.Rumors;
            for (int i = 0; i < rumors.Count; i++)
            {
                NpcRumor rumor = rumors[i];
                if (AR.s.Npc.IsRumorValid(rumor) == false)
                    continue;
                AR.s.Npc.RecordRumorExposure(rumor);
                AddActivityRow(shown == 0 ? "소문" : string.Empty, DescribeRumor(rumor));
                shown++;
            }
            if (shown == 0)
                AddActivityRow("소문", "없음");
        }

        private string DescribeRumor(NpcRumor rumor)
        {
            // P2c: 대화 토픽과 같은 낱말(NpcRumorPhrasing) — 장소는 이 NPC 마을 중심 기준 방향, 출처·시점 포함. 슬롯·세대는 검증용으로 남긴다.
            string source = rumor.SourceNpcId < 0
                ? "직접 겪음"
                : $"{ResolveNpcName(rumor.SourceNpcId)}에게 들음({rumor.Hops}단계)";
            string ago = NpcRumorPhrasing.AgoPhrase(rumor.EventGameTime, AR.s.Time.CurrentGameTime);
            string subject = ResolveNpcName(rumor.SubjectNpcId);
            if (rumor.Kind == NpcRumorKind.DungeonInfo)
            {
                int bearing = -1;
                NpcRumorPlace place = NpcRumorPlace.Unavailable;
                if (AR.s.Npc != null && _saveData != null)
                    place = AR.s.Npc.ResolveRumorDungeonPlace(rumor, _saveData.VillageId, out bearing);
                string what = rumor.Cleared == true
                    ? $"{subject} 일행이 끝낸 자리의 새 던전"
                    : $"{subject}의 원정 성공 (가치 {rumor.Value})";
                string checkedMark = rumor.Checked == true ? " · 확인함" : string.Empty;
                return $"{NpcRumorPhrasing.PlaceCore(place, bearing)} 던전[슬롯 {rumor.SlotIndex}·세대 {rumor.Generation}] · " +
                       $"{what} · {source} · {ago}{checkedMark}";
            }

            // P4b (D104·D105): 판매 소문은 검사 창에만, 배신 소문은 가해자·피해자·세부 종류. 아이템 이름은 소문에 남긴 테이블 Id로 읽는다.
            if (rumor.Kind == NpcRumorKind.ItemSold)
                return $"{NpcRumorPhrasing.Josa(subject, "이", "가")} " +
                       $"{NpcRumorPhrasing.Josa(NpcRumorPhrasing.ItemName(rumor.ItemId), "을", "를")} 상점에 팔았다 · {source} · {ago}";
            if (rumor.Kind == NpcRumorKind.Betrayal)
            {
                string what = rumor.BetrayalKind == NpcBetrayalKind.TheftCaughtInAct ? "훔치려다 걸림" : "훔친 것이 드러남";
                return $"{NpcRumorPhrasing.Josa(subject, "이", "가")} {ResolveNpcName(rumor.VictimNpcId)}의 " +
                       $"{NpcRumorPhrasing.Josa(NpcRumorPhrasing.ItemName(rumor.ItemId), "을", "를")} {what} · {source} · {ago}";
            }

            string itemName = "장비";
            if (AR.s.Npc != null && AR.s.Npc.TryGetRumorItem(rumor, out ItemData? item) == true && item != null &&
                item.Table != null)
                itemName = item.Table.Name;
            return $"{subject}의 {itemName} (가치 {rumor.Value}) · {source} · {ago}";
        }

        private void AddActivityRow(string label, string value)
        {
            if (_activityList == null)
                return;

            VisualElement row = new();
            row.AddToClassList("npc-stat-row");
            Text lab = new() { text = label };
            lab.AddToClassList("npc-stat-label");
            lab.style.whiteSpace = WhiteSpace.Normal;
            Text val = new() { text = value };
            val.AddToClassList("npc-stat-value");
            row.Add(lab);
            row.Add(val);
            _activityList.Add(row);
        }

        private static string GetActivityLabel(NpcActivityState activity)
        {
            if (activity.Phase == NpcActivityPhase.Dead)
                return "사망";
            if (activity.Phase == NpcActivityPhase.Wounded)
                return "부상 회복 중";
            if (activity.Kind == NpcActivityKind.Hunt)
                return "사냥 중";
            if (activity.Kind == NpcActivityKind.Rest)
                return "휴식 중";
            if (activity.Kind == NpcActivityKind.Trade)
                return "거래 중";
            if (activity.Kind == NpcActivityKind.DungeonDelve)
                return "던전 원정 중";
            if (activity.Kind == NpcActivityKind.Relocate)
                return "이주 중";
            return "대기";
        }

        private static string GetHuntOutcomeLabel(NpcHuntOutcome outcome) => outcome switch
        {
            NpcHuntOutcome.Success => "성공",
            NpcHuntOutcome.Failed => "실패",
            NpcHuntOutcome.Wounded => "부상",
            NpcHuntOutcome.Dead => "사망",
            _ => "없음",
        };

        private void RebuildEquipGrid()
        {
            if (_equipGrid == null || _saveData == null) return;
            _equipGrid.Clear();

            int slotMax = (int)GE.EquipSlotType.Max;
            for (int i = 0; i < slotMax; i++)
            {
                GE.EquipSlotType slot = (GE.EquipSlotType)i;
                ItemData? item = _saveData.InventoryEquip[i];
                _equipGrid.Add(BuildSlotElement(item, slot.ToString()));
            }
        }

        private void RebuildInventoryGrid()
        {
            if (_invGrid == null || _saveData == null) return;
            _invGrid.Clear();

            for (int i = 0; i < _saveData.Inventory.Count; i++)
            {
                ItemData? item = _saveData.Inventory[i];
                _invGrid.Add(BuildSlotElement(item, $"Inv {i + 1}"));
            }
        }

        private VisualElement BuildSlotElement(ItemData? item, string emptyLabel)
        {
            VisualElement slot = new();
            slot.AddToClassList("npc-slot");

            VisualElement icon = new();
            icon.AddToClassList("npc-slot-icon");

            if (item != null && item.Table != null && string.IsNullOrEmpty(item.Table.SpriteName) == false)
            {
                Sprite? sprite = AR.s.Data?.GetSprite(item.Table.SpriteName);
                if (sprite != null)
                    icon.style.backgroundImage = new StyleBackground(sprite);
            }
            else
            {
                // 빈 슬롯 — 레이블만 표시
                Text emptyText = new() { text = emptyLabel };
                emptyText.AddToClassList("npc-slot-empty");
                icon.Add(emptyText);
            }

            slot.Add(icon);

            // 호버 툴팁 — 변경 불가, 호버만 (Q4=변경 불가)
            if (item != null)
            {
                VisualElement slotRef = slot;
                slot.userData = item;
                slot.RegisterCallback<MouseEnterEvent>(_ => AR.s.Tooltip.Show(slotRef.userData as ItemData, GetSlotScreenRect(slotRef)));
                slot.RegisterCallback<MouseLeaveEvent>(_ => AR.s.Tooltip.Hide());
            }

            return slot;
        }

        private void RebuildPersonality()
        {
            if (_personalityList == null) return;
            _personalityList.Clear();

            // NpcStatComponent (성향)은 ECS 활성 NPC만 갖고 있음. 비활성/방문자(saveData만 있음)는 표시 불가.
            if (_entityId < 0 || AR.s.Component == null)
                return;
            if (AR.s.Component.TryGetComponent<NpcStatComponent>(_entityId, out var nsc) == false)
                return;

            AddPersonalityRow("친화력", nsc.Friendliness);
            AddPersonalityRow("정직함", nsc.Honesty);
            AddPersonalityRow("탐욕", nsc.Greed);
            AddPersonalityRow("충성심", nsc.Loyalty);
            AddPersonalityRow("용기", nsc.Courage);
            AddPersonalityRow("호기심", nsc.Curiosity);
            AddPersonalityRow("자존심", nsc.Pride);
            AddPersonalityRow("인내심", nsc.Patience);

            // 야망은 성격에서 결정론으로 도출하는 표시 전용 값이다 (NPC_AMBITION_DESIGN §4.2, 저장하지 않음)
            NpcAmbitionProfile ambition = NpcAmbitionEvaluator.Evaluate(nsc);
            AddPersonalityTextRow("야망", $"{GetAmbitionLabel(ambition.Primary)} (보조: {GetAmbitionLabel(ambition.Secondary)})");
        }

        private static string GetAmbitionLabel(NpcAmbitionKind ambition) => ambition switch
        {
            NpcAmbitionKind.Explorer => "탐험",
            NpcAmbitionKind.Glory => "명예",
            NpcAmbitionKind.Wealth => "부",
            NpcAmbitionKind.Comrade => "동료",
            NpcAmbitionKind.Opportunist => "기회",
            NpcAmbitionKind.Cautious => "안전",
            _ => "없음",
        };

        private void AddPersonalityTextRow(string label, string value)
        {
            if (_personalityList == null) return;
            VisualElement row = new();
            row.AddToClassList("npc-stat-row");
            Text lab = new() { text = label };
            lab.AddToClassList("npc-stat-label");
            lab.style.whiteSpace = WhiteSpace.Normal;
            Text val = new() { text = value };
            val.AddToClassList("npc-stat-value");
            row.Add(lab);
            row.Add(val);
            _personalityList.Add(row);
        }

        private void AddPersonalityRow(string label, int value)
        {
            if (_personalityList == null) return;
            VisualElement row = new();
            row.AddToClassList("npc-stat-row");
            Text lab = new() { text = label };
            lab.AddToClassList("npc-stat-label");
            Text val = new() { text = value.ToString() };
            val.AddToClassList("npc-stat-value");
            row.Add(lab);
            row.Add(val);
            _personalityList.Add(row);
        }

        // R4: 관계 탭 — 가족/친구/원한 + 호감도. 데이터 정본은 NpcManager (활성/비활성 무관 조회 가능).
        private void RebuildRelations()
        {
            if (_relationList == null) return;
            _relationList.Clear();

            NpcSaveData? saveData = _saveData;
            if (_entityId < 0 || saveData == null || AR.s.Npc == null)
                return;

            var relations = AR.s.Npc.GetRelations(_entityId);
            List<NpcAffinityEntry> sharedActivities = AR.s.Npc.GetSharedActivities(_entityId);
            if (relations.Count == 0 && saveData.DeceasedRelations.Count == 0 &&
                sharedActivities.Count == 0 && saveData.SharedActivityMemories.Count == 0)
            {
                AddRelationRow("관계 없음", string.Empty);
                return;
            }

            for (int i = 0; i < relations.Count; i++)
            {
                NpcRelationEntry rel = relations[i];
                bool isFrom = rel.FromEntityId == _entityId;
                int otherId = isFrom ? rel.ToEntityId : rel.FromEntityId;
                string label = $"{RelationLabel(rel.Type, isFrom)} {ResolveNpcName(otherId)}";
                int affinity = AR.s.Npc.GetAffinity(_entityId, otherId);
                AddRelationRow(label, affinity.ToString());
            }

            for (int i = 0; i < saveData.DeceasedRelations.Count; i++)
            {
                DeceasedRelationMemory memory = saveData.DeceasedRelations[i];
                string label = $"고인 {RelationLabel(memory.RelationType, memory.WasFromEntity)} {memory.DeceasedNpcName}";
                AddRelationRow(label, "사망");
            }
            for (int i = 0; i < sharedActivities.Count; i++)
            {
                NpcAffinityEntry entry = sharedActivities[i];
                int otherId = entry.NpcA == _entityId ? entry.NpcB : entry.NpcA;
                AddRelationRow($"원정 동료 {ResolveNpcName(otherId)}", $"호감도 {entry.Affinity}");
                AddSharedActivityRow(entry.SharedActivities!);
            }
            for (int i = 0; i < saveData.SharedActivityMemories.Count; i++)
            {
                NpcSharedActivityMemory memory = saveData.SharedActivityMemories[i];
                bool deceased = AR.s.Npc.GetSaveData(memory.OtherNpcId)?.Condition == ARPG.Creature.CharacterConditions.Dead;
                AddRelationRow($"{(deceased ? "고인 동료" : "동료")} {memory.OtherNpcName}",
                    memory.LastAffinity.HasValue ? $"마지막 호감도 {memory.LastAffinity.Value}" : string.Empty);
                AddSharedActivityRow(memory.Activities);
            }
        }

        private void AddSharedActivityRow(NpcSharedActivityStats stats)
        {
            string when = NpcRumorPhrasing.AgoPhrase(stats.LastGameTime, AR.s.Time.CurrentGameTime);
            AddRelationRow($"{stats.Describe()} · 최근 {when}", string.Empty);
        }

        private void AddRelationRow(string label, string value)
        {
            if (_relationList == null) return;
            VisualElement row = new();
            row.AddToClassList("npc-stat-row");
            Text lab = new() { text = label };
            lab.AddToClassList("npc-stat-label");
            lab.style.whiteSpace = WhiteSpace.Normal;
            Text val = new() { text = value };
            val.AddToClassList("npc-stat-value");
            row.Add(lab);
            row.Add(val);
            _relationList.Add(row);
        }

        private string ResolveNpcName(int otherId)
        {
            NpcSaveData? other = AR.s.Npc?.GetSaveData(otherId);
            if (other == null)
                return $"NPC #{otherId}";
            if (string.IsNullOrEmpty(other.Name) == false)
                return other.Name;
            NpcTable? t = AR.s.Data?.GetNpc(other.NpcTableId);
            return t?.Name ?? $"NPC #{otherId}";
        }

        private static string RelationLabel(NpcRelationType type, bool isFrom) => type switch
        {
            NpcRelationType.Spouse => "배우자",
            NpcRelationType.Sibling => "형제",
            NpcRelationType.Friend => "친구",
            NpcRelationType.Rival => "라이벌",
            NpcRelationType.ParentOf => isFrom ? "자녀" : "부모",
            NpcRelationType.MentorOf => isFrom ? "제자" : "스승",
            NpcRelationType.GrudgeAgainst => isFrom ? "원한" : "원한 대상",
            _ => "관계",
        };

        private void RebuildStats()
        {
            if (_statList == null) return;
            _statList.Clear();

            if (_entityId < 0 || AR.s.Component == null)
                return;
            if (AR.s.Component.TryGetComponent<StatComponent>(_entityId, out var sc) == false)
                return;

            AddStatRow("힘", sc.FinalStr.ToString());
            AddStatRow("민첩", sc.FinalDex.ToString());
            AddStatRow("지능", sc.FinalInt.ToString());
            AddStatRow("체력", $"{sc.CurrentHp}/{sc.FinalMaxHp}");
            AddStatRow("마나", $"{sc.CurrentMp}/{sc.FinalMaxMp}");
            AddStatRow("체력 재생", sc.FinalHpGeneration.ToString());
            AddStatRow("마나 재생", sc.FinalMpGeneration.ToString());
            AddStatRow("물리 공격", $"{sc.FinalAttackMin} - {sc.FinalAttackMax}");
            AddStatRow("화염 공격", $"{sc.FinalFireAttackMin} - {sc.FinalFireAttackMax}");
            AddStatRow("냉기 공격", $"{sc.FinalIceAttackMin} - {sc.FinalIceAttackMax}");
            AddStatRow("번개 공격", $"{sc.FinalLightningAttackMin} - {sc.FinalLightningAttackMax}");
            AddStatRow("독 공격", $"{sc.FinalPoisonAttackMin} - {sc.FinalPoisonAttackMax}");
            AddStatRow("치명타 확률", $"{sc.FinalCriRate}%");
            AddStatRow("치명타 피해", $"{sc.FinalCriDamage}%");
            AddStatRow("공격 속도", $"{sc.FinalAttackSpeed}");
            AddStatRow("시전 속도", $"{sc.FinalCastSpeed}");
            AddStatRow("이동 속도", $"{sc.FinalMoveSpeedValue:F2}");
            AddStatRow("방어력", sc.FinalDefense.ToString());
            AddStatRow("화염 저항", $"{sc.FinalFireResist}% / {sc.FinalMaxFireResist}%");
            AddStatRow("냉기 저항", $"{sc.FinalIceResist}% / {sc.FinalMaxIceResist}%");
            AddStatRow("번개 저항", $"{sc.FinalLightningResist}% / {sc.FinalMaxLightningResist}%");
            AddStatRow("독 저항", $"{sc.FinalPoisonResist}% / {sc.FinalMaxPoisonResist}%");
            AddStatRow("회피", $"{sc.FinalEvasion}%");
            AddStatRow("막기", $"{sc.FinalBlockChance}%");
            AddStatRow("막기 감소", $"{sc.FinalBlockReduction}%");
            AddStatRow("스킬 데미지", $"{sc.FinalSkillDamage}%");
            AddStatRow("쿨타임 감소", $"{sc.FinalCooldownReduction}%");
            AddStatRow("생명력 흡수", $"{sc.FinalLifeSteal}%");
            AddStatRow("가시", sc.FinalThorns.ToString());
            AddStatRow("행운", sc.FinalLuck.ToString());
            AddStatRow("출혈률", sc.FinalBloodingRate.ToString());
            AddStatRow("점화율", sc.FinalIgniteRate.ToString());
        }

        private void AddStatRow(string label, string value)
        {
            if (_statList == null) return;
            VisualElement row = new();
            row.AddToClassList("npc-stat-row");
            Text lab = new() { text = label };
            lab.AddToClassList("npc-stat-label");
            lab.style.whiteSpace = WhiteSpace.Normal;
            Text val = new() { text = value };
            val.AddToClassList("npc-stat-value");
            row.Add(lab);
            row.Add(val);
            _statList.Add(row);
        }

        // ── 상호작용 (허브 버튼) ──────────────────────────────────────────
        // UINpcInspect가 상호작용 허브 역할. 버튼을 눌러 각 상호작용 함수를 호출하거나
        // 전용 UI(거래·선물)를 연다. 실제 관계 반영/서브 UI는 다음 단계(TODO).

        // 상호작용 바/대화 라벨/선물 리스트는 UXML(panel 내부)에 자리가 정의돼 있다.
        // 버튼만 코드로 생성해 컨테이너에 넣고, 라벨/리스트는 UXML 요소를 잡아 재사용한다.
        private void BuildInteractionBar(VisualElement root)
        {
            _dialogueLabel = root.Q<Text>("dialogue-label");
            if (_dialogueLabel != null) _dialogueLabel.text = string.Empty;
            _giftList = root.Q<VisualElement>("gift-list");

            VisualElement? bar = root.Q<VisualElement>("interaction-bar");
            if (bar == null)
            {
                Debug.LogError("[UINpcInspect] interaction-bar 컨테이너 없음 — UXML 확인 필요");
                return;
            }
            bar.Clear();

            Button talkBtn = new() { title = "대화" };
            talkBtn.variant = ButtonVariant.Accent;
            talkBtn.clicked += OnClickTalk;
            bar.Add(talkBtn);

            Button tradeBtn = new() { title = "거래" };
            tradeBtn.clicked += OnClickTrade;
            bar.Add(tradeBtn);

            Button giftBtn = new() { title = "선물" };
            giftBtn.clicked += OnClickGift;
            bar.Add(giftBtn);

            // 임시 동행 요청 — 던전 전용 (NPC_COMBAT_DESIGN §4.5). 예약 여부에 따라 요청/취소로 바뀐다.
            _companionBtn = new() { title = "동행 요청" };
            _companionBtn.clicked += OnClickCompanion;
            bar.Add(_companionBtn);

            // 결투 신청 (NPC_COMBAT_DESIGN §7). 어떤 NPC에게든·던전 안에서도 신청할 수 있다 [D19]. B4는 항상 수락(스텁).
            _duelBtn = new() { title = "결투 신청" };
            _duelBtn.clicked += OnClickDuel;
            bar.Add(_duelBtn);
        }

        private void RefreshDuelButton()
        {
            if (_duelBtn == null || AR.s.Npc == null)
                return;

            // 결투는 한 번에 1건 — 진행 중이면 메뉴를 띄우지 않는다
            _duelBtn.style.display = AR.s.Npc.IsPlayerInDuel == true ? DisplayStyle.None : DisplayStyle.Flex;
        }

        private void OnClickDuel()
        {
            if (_entityId < 0 || AR.s.Npc == null)
                return;

            bool accepted = AR.s.Npc.RequestDuel(_entityId, out string message);
            if (_dialogueLabel != null)
                _dialogueLabel.text = message;
            if (accepted == false)
            {
                AR.s.UI.SetNotify(message);
                return;
            }

            // 신청한 자리에서 바로 싸우므로 패널을 닫는다 [D20]
            Close();
        }

        private void RefreshCompanionButton()
        {
            if (_companionBtn == null || AR.s.Npc == null)
                return;

            // 던전 안 동행은 퇴장으로만 끝나므로 던전 안에서는 메뉴를 띄우지 않는다
            _companionBtn.style.display = AR.s.Dungeon.IsInside == true ? DisplayStyle.None : DisplayStyle.Flex;
            _companionBtn.title = AR.s.Npc.IsCompanionReserved(_entityId) == true ? "동행 취소" : "동행 요청";
        }

        private void OnClickCompanion()
        {
            if (_entityId < 0 || AR.s.Npc == null)
                return;

            string message;
            if (AR.s.Npc.IsCompanionReserved(_entityId) == true)
                AR.s.Npc.CancelCompanion(_entityId, out message);
            else
                AR.s.Npc.RequestCompanion(_entityId, out message);

            if (_dialogueLabel != null)
                _dialogueLabel.text = message;
            RefreshCompanionButton();
        }

        private void OnClickTalk()
        {
            if (_entityId < 0) return;
            OnClickTalkAsync().Forget();
        }

        // 스파이크: 대화 클릭 → 백본 문맥 기반 LLM 대화(+Friend↑ 소망) 생성.
        private async UniTask OnClickTalkAsync()
        {
            // 첫 만남·재방문 판정은 이번 Talk가 관계와 만남 횟수를 올리기 전 스냅샷을 사용한다.
            PlayerRelation relation = AR.s.Npc.GetPlayerRelation(_entityId);
            float talkGameTime = AR.s.Time.CurrentGameTime;
            AffinityTier tier = DialogueTopicSelector.GetAffinityTier(relation.Affinity);
            InteractionResult interaction = InteractionHelper.Resolve(_entityId, InteractionType.Talk);

            // 야외 몬스터는 세계의 상시 배경이다. 토픽 선택기에서 긴급이 아닌 낮은 우선순위 화제로 취급한다.
            ThreatSeed threat = new()
            {
                NearbyMonsters = true,
                Note = "마을 밖에는 몬스터가 흔히 돌아다녀 여행자는 주의해야 한다.",
            };

            if (_dialogueLabel != null) _dialogueLabel.text = $"[{tier}] 생성 중…";

            DialogueResult r = await NpcNarrativeService.GetDialogueAsync(_entityId, relation, talkGameTime, threat);
            string line = $"[{tier}/{r.Topic}/{r.Source}] {r.Text}\n호감도 +{interaction.AffinityDelta}, 친밀도 +{interaction.IntimacyDelta}";

            if (_enableWishSpike && tier >= AffinityTier.Friend)
            {
                NpcWish? wish = await NpcNarrativeService.GetOrGenerateWishAsync(_entityId, tier, threat);
                if (wish != null)
                {
                    string fb = wish.IsFallback ? " [폴백]" : string.Empty;
                    line += $"\n(소망 {wish.Kind}→{wish.TargetRef}{fb}) \"{wish.RevealDialogue}\"";
                }
            }

            if (_dialogueLabel != null) _dialogueLabel.text = line;
            Debug.Log($"[Narrative] npc={_entityId} {line}");
        }

        private void OnClickTrade()
        {
            if (_entityId < 0) return;
            InteractionResult result = InteractionHelper.Resolve(_entityId, InteractionType.Trade);
            if (result.Kind == ResultKind.OpenTrade)
            {
                // TODO: UINpcTrade 오픈 (NPC 인벤토리 기반 매매) — AR.s.UI.Show<UINpcTrade>(...).Bind(_entityId).
                Debug.Log($"[UINpcInspect] 거래 → 거래 UI 오픈 예정 (npc={_entityId})");
            }
        }

        private void OnClickGift()
        {
            if (_entityId < 0) return;
            BuildGiftSelection();
        }

        private void BuildGiftSelection()
        {
            if (_giftList == null || AR.s.Player == null)
                return;

            _giftList.Clear();
            _giftList.style.display = DisplayStyle.Flex;
            _giftList.Add(new Text { text = "선물할 아이템을 선택하세요." });

            List<ItemData?> items = AR.s.Player.Inventory.Items;
            int selectableCount = 0;
            for (int i = 0; i < items.Count; i++)
            {
                ItemData? item = items[i];
                if (item == null || item.Table == null)
                    continue;

                int slotIndex = i;
                Button itemButton = new() { title = $"{item.Table.Name} x{item.Quantity}" };
                itemButton.clicked += () => OnGiftItemSelected(slotIndex);
                _giftList.Add(itemButton);
                selectableCount++;
            }

            if (selectableCount == 0)
                _giftList.Add(new Text { text = "선물할 아이템이 없습니다." });
        }

        private void OnGiftItemSelected(int slotIndex)
        {
            if (_entityId < 0 || AR.s.Player == null)
                return;

            ItemData? item = AR.s.Player.Inventory.GetItemBySlotIndex(slotIndex);
            if (item == null)
                return;

            if (AR.s.Player.Inventory.RemoveItem(slotIndex, 1, out _) == false)
            {
                if (_dialogueLabel != null)
                    _dialogueLabel.text = "선물을 전달하지 못했습니다.";
                return;
            }

            OnGiftItemSelected(item);
            if (_giftList != null)
            {
                _giftList.Clear();
                _giftList.style.display = DisplayStyle.None;
            }
        }

        /// <summary>선물 아이템 선택 UI가 아이템을 확정하면 호출 (ResolveGift 호출부만 배선).</summary>
        private void OnGiftItemSelected(ItemData item)
        {
            if (_entityId < 0) return;
            GiftResult result = InteractionHelper.ResolveGift(_entityId, item);
            if (_dialogueLabel != null)
                _dialogueLabel.text = $"선물을 건넸습니다. 호감도 +{result.AffinityDelta}, 친밀도 +{result.IntimacyDelta}";
            Debug.Log($"[UINpcInspect] 선물 처리 → 반응={result.Tier}, ΔAff={result.AffinityDelta}, ΔInt={result.IntimacyDelta}");
        }

        /// <summary>
        /// UI Toolkit VisualElement → Screen Space Rect. 툴팁 anchor용.
        /// UIShopMerchant/UISkillBook의 동명 헬퍼와 동일 정책 (TODO: 공통 유틸 추출).
        /// </summary>
        private static Rect GetSlotScreenRect(VisualElement slot)
        {
            if (slot == null) return Rect.zero;
            IPanel? panel = slot.panel;
            if (panel == null) return Rect.zero;

            VisualElement visualTree = panel.visualTree;
            float panelW = visualTree.resolvedStyle.width;
            float panelH = visualTree.resolvedStyle.height;
            if (panelW <= 0f || panelH <= 0f) return Rect.zero;

            float scaleX = Screen.width / panelW;
            float scaleY = Screen.height / panelH;

            Rect bound = slot.worldBound;
            float xMin = bound.xMin * scaleX;
            float xMax = bound.xMax * scaleX;
            float yMaxScreen = Screen.height - bound.yMin * scaleY;
            float yMinScreen = Screen.height - bound.yMax * scaleY;

            return new Rect(xMin, yMinScreen, xMax - xMin, yMaxScreen - yMinScreen);
        }
    }
}
