#nullable enable
using ARPG.Base;
using ARPG.Component;
using ARPG.Village;
using Unity.AppUI.UI;
using UnityEngine;
using UnityEngine.UIElements;
using Button = Unity.AppUI.UI.Button;
using Text = Unity.AppUI.UI.Text;

namespace ARPG.UI
{
    /// <summary>
    /// Phase D: 여관 UI (InnBed 진입, HasObjectSet(Inn) 확인 — 마을 전체 + Hearth 필요).
    /// 기능:
    ///   1. 세이브 (AR.s.Data.Save())
    ///   2. 휴식 (HP/MP 100% + 게임시간 +6h)
    ///   3. (Phase F+) 빠른 이동 — 다른 마을 InnBed로
    ///
    /// 비용 (Tier별 차등):
    ///   - Hamlet: 10G / Village: 25G / Town: 50G
    ///
    /// UI Toolkit 기반 (App UI 컴포넌트 사용). prefab의 UIDocument 컴포넌트 필요.
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
        }

        public int GetRestCost()
        {
            // 마을 Stage 기반 차등 (Hamlet 10 / Village 25 / Town 50 / City 100)
            // TODO: VillageData.Stage 조회 후 dispatch
            return 10;
        }
    }
}
