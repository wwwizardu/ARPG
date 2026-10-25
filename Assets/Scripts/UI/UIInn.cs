#nullable enable
using System.Collections.Generic;
using ARPG.Base;
using ARPG.Component;
using ARPG.Npc;
using ARPG.Village;
using Unity.AppUI.UI;
using UnityEngine;
using UnityEngine.UIElements;
using Button = Unity.AppUI.UI.Button;
using Text = Unity.AppUI.UI.Text;

namespace ARPG.UI
{
    /// <summary>
    /// 여관 UI — 휴식(+6h)/세이브 스텁과 여관에서 쉬는 NPC 목록.
    /// VILLAGE_NPC_BOUNDARY_DESIGN §5에 따라 여관은 방문자 고용이 없는 무인 서비스 건물이다.
    /// 목록은 그 마을 소속으로 원정 부상에서 회복 중인 NPC다 (NPC_SIMULATION_DESIGN §3.1 D16). 회복이 빨리 끝나는 순으로 버튼을 보여 주고,
    /// 고르면 UINpcInspect를 연다 — 여관 안 NPC는 실체화되지 않아도 대화할 수 있다. 버튼이라 ControllerUIFocus가 패드로 고른다.
    ///
    /// Inn 세트 미완성 시 Rest 비활성. 손님 목록은 세트와 관계없이 보여 준다.
    /// </summary>
    public class UIInn : UIBaseForm
    {
        private const int REST_HOURS = 6;

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
        private readonly List<int> _guestIds = new();

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

        // ========== 갱신 ==========

        private void RefreshAll()
        {
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

            RefreshGuests();
        }

        /// <summary>
        /// 구 방문자 영역 → 여관에서 쉬는 NPC 목록 (NPC_SIMULATION_DESIGN §3.1 D16). 한 줄 = "이름 — 회복까지 N시간" 버튼.
        /// 창을 열 때 다시 만든다. 열어 둔 사이 회복한 NPC를 골라도 NPC 창은 그대로 열린다.
        /// </summary>
        private void RefreshGuests()
        {
            _guestIds.Clear();
            NpcManager? npcManager = AR.s.Npc;
            if (_villageId >= 0 && npcManager != null)
                npcManager.CollectInnGuests(_villageId, _guestIds);

            if (_slotCountText != null)
                _slotCountText.text = $"쉬고 있는 손님 {_guestIds.Count}명";

            if (_visitorList == null)
                return;

            _visitorList.Clear();
            if (npcManager == null || _guestIds.Count == 0)
            {
                AddEmptySlot(_visitorList, "쉬고 있는 손님이 없습니다");
                return;
            }

            ScrollView scroll = new ScrollView(ScrollViewMode.Vertical);
            scroll.AddToClassList("inn-guest-scroll");
            float now = AR.s.Time.CurrentGameTime;
            for (int i = 0; i < _guestIds.Count; i++)
            {
                int npcId = _guestIds[i];
                NpcSaveData? guest = npcManager.GetSaveData(npcId);
                if (guest == null)
                    continue;

                Button guestBtn = new() { title = $"{npcManager.GetDisplayName(npcId)} — {FormatRecovery(guest, now)}" };
                guestBtn.AddToClassList("inn-guest-btn");
                guestBtn.clicked += () => OnClickGuest(npcId);
                scroll.Add(guestBtn);
            }
            _visitorList.Add(scroll);
        }

        private static string FormatRecovery(NpcSaveData guest, float now)
        {
            float remainingHours = guest.Activity.PhaseEndGameTime - now;
            if (remainingHours < 1f)
                return "곧 회복";
            return $"회복까지 {Mathf.CeilToInt(remainingHours)}시간";
        }

        /// <summary>손님을 고르면 NPC 창을 연다. UIManager.Show가 정렬값을 올려 여관 창 위에 보인다.</summary>
        private void OnClickGuest(int npcId)
        {
            UINpcInspect? ui = AR.s.UI.Show<UINpcInspect>(AddressablePath.NpcInspect, UIManager.Layer.Main);
            ui?.Bind(npcId);
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
    }
}
