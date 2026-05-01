#nullable enable
using System.Collections.Generic;
using ARPG.Base;
using ARPG.Component;
using ARPG.Npc;
using ARPG.Tables;
using ARPG.Village;
using Unity.AppUI.UI;
using UnityEngine;
using UnityEngine.UIElements;
using Button = Unity.AppUI.UI.Button;
using Text = Unity.AppUI.UI.Text;

namespace ARPG.UI
{
    /// <summary>
    /// 여관 UI — 방문자 메인 + Rest/Save 보조.
    /// INN_HIRING_DESIGN.md §4.3 레이아웃을 따름.
    ///
    /// 메인: 방문자 카드 리스트 (초상화/이름/잔여시간/희망 직업/숙련도/소개/Hire 버튼)
    /// 보조: 휴식(+6h)/세이브 — 기존 동작 유지
    ///
    /// Inn 세트 미완성 시 메인 영역에 안내문, Rest 비활성.
    /// </summary>
    public class UIInn : UIBaseForm
    {
        private const int REST_HOURS = 6;
        private const float REMAINING_WARN_HOURS = 12f;
        private const float REMAINING_URGENT_HOURS = 6f;

        private UIDocument? _document;
        private Button? _restBtn;
        private Button? _saveBtn;
        private IconButton? _closeBtn;
        private Text? _statusText;
        private Text? _costText;
        private Text? _slotCountText;
        private VisualElement? _visitorList;

        private int _innEntityId = -1;
        private int _villageId = -1;
        private bool _hasInnSet = false;
        private VisualElement? _lastRoot;

        public override void Initialize(string inName, bool isForm = false)
        {
            base.Initialize(inName, isForm);

            _document = GetComponent<UIDocument>();
            if (_document == null)
            {
                Debug.LogError("[UIInn] UIDocument 컴포넌트 없음 — prefab 확인 필요");
                return;
            }

            EnsureBound();
        }

        /// <summary>
        /// UIDocument는 SetActive 토글 시마다 rootVisualElement를 재구성하므로
        /// root 변경을 감지해 매번 재바인딩.
        /// </summary>
        private void EnsureBound()
        {
            if (_document == null) return;
            VisualElement root = _document.rootVisualElement;
            if (root == null) return;
            if (root == _lastRoot) return;

            _lastRoot = root;

            _restBtn = root.Q<Button>("rest-btn");
            _saveBtn = root.Q<Button>("save-btn");
            _closeBtn = root.Q<IconButton>("close-btn");
            _statusText = root.Q<Text>("status-text");
            _costText = root.Q<Text>("cost-text");
            _slotCountText = root.Q<Text>("slot-count-text");
            _visitorList = root.Q<VisualElement>("visitor-list");

            if (_restBtn != null) _restBtn.clicked += OnClickRest;
            if (_saveBtn != null) _saveBtn.clicked += OnClickSave;
            if (_closeBtn != null) _closeBtn.clicked += () => Close();
        }

        public void Bind(int innEntityId)
        {
            EnsureBound();
            _innEntityId = innEntityId;

            if (AR.s.Component.TryGetComponent<PlacedObjectComponent>(_innEntityId, out var po) == false)
            {
                Debug.LogWarning($"[UIInn] Bind 실패 — entityId={innEntityId} PlacedObject 없음");
                return;
            }
            _villageId = po.VillageId;

            // Inn 세트는 마을 전체 검사 (anchor 무시)
            _hasInnSet = AR.s.Village.HasObjectSet(_villageId, ObjectSetType.Inn);

            // 만료된 방문자 정리 (UI 진입 시 한 번 — 화면에 만료된 카드가 보이지 않도록)
            if (_villageId >= 0)
                AR.s.Npc.EvictExpiredVisitors(_villageId);

            RefreshAll();
        }

        /// <summary>
        /// 테스트용: 실제 InnBed 없이 UI만 띄울 때 호출. 세트 활성 상태로 가정.
        /// </summary>
        public void BindForTest()
        {
            EnsureBound();
            _innEntityId = -1;
            _villageId = -1;
            _hasInnSet = true;
            RefreshAll();
            Debug.Log("[UIInn] BindForTest — 테스트 모드로 UI 열림");
        }

        public override void OnOpen()
        {
            base.OnOpen();
            EnsureBound();
            RefreshAll();
        }

        // ========== 액션 ==========

        public void OnClickRest()
        {
            if (_hasInnSet == false) return;
            // TODO: Gold 차감 (Stage 기반 가격)
            // TODO: HP/MP 100% 회복
            // TODO: AR.s.Time에 +6h 진행 (게임시간)
            Debug.Log($"[Inn] v{_villageId} 휴식 (+{REST_HOURS}h)");
        }

        public void OnClickSave()
        {
            // TODO: AR.s.Data.Save() 호출
            Debug.Log($"[Inn] v{_villageId} 세이브");
        }

        public void OnClickHire(int entityId)
        {
            if (_villageId < 0)
            {
                Debug.Log($"[Inn] (테스트모드) 고용 entity={entityId}");
                return;
            }

            if (AR.s.Npc.HireVisitor(entityId, out string failReason))
            {
                Debug.Log($"[Inn] 고용 성공 entity={entityId}");
                RefreshAll();
            }
            else
            {
                Debug.Log($"[Inn] 고용 실패 entity={entityId} reason={failReason}");
                RefreshAll();
            }
        }

        // ========== 갱신 ==========

        private void RefreshAll()
        {
            // 보조 영역
            if (_statusText != null)
            {
                _statusText.text = _hasInnSet
                    ? "휴식 가능 — 여관 세트 활성"
                    : "여관 세트 미완성 (Bed + Hearth 필요)";
            }

            if (_costText != null)
            {
                _costText.text = $"휴식 비용: {GetRestCost()}G";
            }

            if (_restBtn != null)
            {
                _restBtn.SetEnabled(_hasInnSet);
            }

            // 메인 영역 — 방문자 카드 리스트
            RebuildVisitorList();
        }

        private void RebuildVisitorList()
        {
            if (_visitorList == null) return;
            _visitorList.Clear();

            int capacity = _villageId >= 0 ? AR.s.Npc.GetInnCapacity(_villageId) : 2;
            int filled = _villageId >= 0 ? AR.s.Npc.GetInnVisitorCount(_villageId) : 0;

            if (_slotCountText != null)
            {
                string headerPrefix = _hasInnSet ? "세트 활성" : "세트 미완성";
                _slotCountText.text = $"{headerPrefix} — 손님 {filled}/{capacity}";
            }

            if (_hasInnSet == false)
            {
                AddEmptySlot(_visitorList, "여관 세트(Bed + Hearth)를 완성하면 손님이 옵니다");
                return;
            }

            // 방문자 카드
            if (_villageId >= 0)
            {
                List<int> visitors = AR.s.Npc.GetInnVisitors(_villageId);
                for (int i = 0; i < visitors.Count; i++)
                {
                    int entityId = visitors[i];
                    AddVisitorCard(_visitorList, entityId);
                }
            }

            // 빈 슬롯 placeholder
            int emptySlots = capacity - filled;
            for (int i = 0; i < emptySlots; i++)
            {
                AddEmptySlot(_visitorList, "(빈 슬롯 — 곧 새 손님이 도착합니다)");
            }
        }

        private void AddVisitorCard(VisualElement parent, int entityId)
        {
            NpcSaveData? saveData = AR.s.Npc.GetSaveData(entityId);
            if (saveData == null) return;

            Tables.NpcTable? npcTable = AR.s.Data.GetNpc(saveData.NpcTableId);
            if (npcTable == null) return;

            VisualElement card = new();
            card.AddToClassList("inn-visitor-card");

            // 초상화 — placeholder 색상만 (실 초상화 자산 도입 시 NpcSaveData에 SpriteName 필드 추가)
            VisualElement portrait = new();
            portrait.AddToClassList("inn-visitor-portrait");
            card.Add(portrait);

            // 정보 영역
            VisualElement info = new();
            info.AddToClassList("inn-visitor-info");

            // 이름 + 잔여시간 (한 줄)
            VisualElement nameRow = new();
            nameRow.AddToClassList("inn-visitor-name-row");
            Text nameText = new() { text = npcTable.Name };
            nameText.AddToClassList("inn-visitor-name");
            nameRow.Add(nameText);

            float remaining = AR.s.Npc.GetVisitorRemainingHours(entityId);
            int remainingInt = Mathf.CeilToInt(remaining);
            Text timeText = new() { text = $"⏱ {remainingInt}h 남음" };
            timeText.AddToClassList("inn-visitor-time");
            if (remaining < REMAINING_URGENT_HOURS)
            {
                timeText.text = $"⚠ {remainingInt}h 남음";
                timeText.AddToClassList("inn-visitor-time-urgent");
            }
            else if (remaining < REMAINING_WARN_HOURS)
            {
                timeText.AddToClassList("inn-visitor-time-warn");
            }
            nameRow.Add(timeText);
            info.Add(nameRow);

            // 직업 + 숙련도
            string jobLabel = JobTypeToKorean(npcTable.JobType);
            string skillStars = SkillLevelToStars(saveData.SkillLevel);
            Text metaText = new() { text = $"희망 직업: {jobLabel}  ·  숙련도 {skillStars}" };
            metaText.AddToClassList("inn-visitor-meta");
            info.Add(metaText);

            // 한 줄 소개 — NpcSaveData.Description (생성 시 직업 풀에서 랜덤 선택, 인스턴스별 고정)
            if (string.IsNullOrEmpty(saveData.Description) == false)
            {
                Text descText = new() { text = $"\"{saveData.Description}\"" };
                descText.AddToClassList("inn-visitor-desc");
                info.Add(descText);
            }

            // 액션 행 (실패 사유 + 고용 버튼)
            VisualElement actionRow = new();
            actionRow.AddToClassList("inn-visitor-action-row");

            int hireCost = CalculateHireCostForUI(saveData);
            int playerGold = AR.s.Data.Player?.Gold ?? 0;
            bool canAfford = playerGold >= hireCost;

            if (canAfford == false)
            {
                Text failText = new() { text = $"골드 부족 ({playerGold}G / {hireCost}G)" };
                failText.AddToClassList("inn-visitor-fail-text");
                actionRow.Add(failText);
            }

            int capturedId = entityId;
            Button hireBtn = new() { title = $"Hire {hireCost}G" };
            hireBtn.size = Size.S;
            hireBtn.variant = ButtonVariant.Accent;
            hireBtn.AddToClassList("inn-visitor-hire-btn");
            hireBtn.SetEnabled(canAfford);
            hireBtn.clicked += () => OnClickHire(capturedId);
            actionRow.Add(hireBtn);

            info.Add(actionRow);
            card.Add(info);

            parent.Add(card);
        }

        private static void AddEmptySlot(VisualElement parent, string message)
        {
            VisualElement slot = new();
            slot.AddToClassList("inn-visitor-card-empty");

            Text msg = new() { text = message };
            msg.AddToClassList("inn-visitor-empty-text");
            slot.Add(msg);

            parent.Add(slot);
        }

        public int GetRestCost()
        {
            // 마을 Stage 기반 차등 (Hamlet 10 / Village 25 / Town 50 / City 100)
            // TODO: VillageData.Stage 조회 후 dispatch
            return 10;
        }

        // ========== UI 헬퍼 ==========

        /// <summary>
        /// UI에서만 비용을 미리 보여주기 위한 사전 계산 — 실제 차감은 NpcManager.HireVisitor 내부에서 수행.
        /// 표 동기화는 INN_HIRING_DESIGN.md §2.7과 NpcManager.CalculateHireCost를 함께 갱신할 것.
        /// </summary>
        private int CalculateHireCostForUI(NpcSaveData saveData)
        {
            if (_villageId < 0) return 0;
            VillageData? village = AR.s.Village.GetVillage(_villageId);
            if (village == null) return 0;

            int baseCost = village.Stage switch
            {
                VillageStage.Settlement => 0,
                VillageStage.Hamlet     => 50,
                VillageStage.Village    => 150,
                VillageStage.Town       => 400,
                VillageStage.City       => 1000,
                _ => 0,
            };

            GlobalEnum.JobType desiredJob = saveData.JobType;
            if (desiredJob == GlobalEnum.JobType.None)
            {
                NpcTable? npcTable = AR.s.Data.GetNpc(saveData.NpcTableId);
                if (npcTable != null) desiredJob = npcTable.JobType;
            }

            return baseCost + GetJobBonusCostForUI(desiredJob);
        }

        private static int GetJobBonusCostForUI(GlobalEnum.JobType jobType)
        {
            // NpcManager.GetJobBonusCost와 동기화 — 변경 시 두 곳 다 수정.
            return jobType switch
            {
                GlobalEnum.JobType.None       => 0,
                GlobalEnum.JobType.Gatherer   => 0,
                GlobalEnum.JobType.Woodcutter => 10,
                GlobalEnum.JobType.Farmer     => 20,
                GlobalEnum.JobType.Hunter     => 30,
                GlobalEnum.JobType.Miner      => 30,
                GlobalEnum.JobType.Builder    => 40,
                GlobalEnum.JobType.Guard      => 50,
                GlobalEnum.JobType.Merchant   => 50,
                GlobalEnum.JobType.Blacksmith => 100,
                GlobalEnum.JobType.Scholar    => 120,
                GlobalEnum.JobType.Chief      => 200,
                _ => 0,
            };
        }

        private static string JobTypeToKorean(GlobalEnum.JobType type)
        {
            return type switch
            {
                GlobalEnum.JobType.None       => "미정",
                GlobalEnum.JobType.Farmer     => "농부",
                GlobalEnum.JobType.Blacksmith => "대장장이",
                GlobalEnum.JobType.Merchant   => "상인",
                GlobalEnum.JobType.Hunter     => "사냥꾼",
                GlobalEnum.JobType.Builder    => "건축가",
                GlobalEnum.JobType.Scholar    => "학자",
                GlobalEnum.JobType.Guard      => "경비병",
                GlobalEnum.JobType.Chief      => "촌장",
                GlobalEnum.JobType.Woodcutter => "벌목꾼",
                GlobalEnum.JobType.Miner      => "광부",
                GlobalEnum.JobType.Gatherer   => "채집꾼",
                _ => type.ToString(),
            };
        }

        private static string SkillLevelToStars(int skillLevel)
        {
            // 0~100 → 0~3 별
            int stars = Mathf.Clamp(skillLevel / 34, 0, 3);
            return stars switch
            {
                0 => "☆☆☆",
                1 => "★☆☆",
                2 => "★★☆",
                _ => "★★★",
            };
        }

    }
}
