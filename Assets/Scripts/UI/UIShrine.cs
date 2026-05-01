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
    /// Phase D: 제단 UI. 1회용 단기 버프(30분 게임시간) + 12시간 게임시간 쿨다운.
    /// 쿨다운은 PlacedObjectComponent.LastUseGameTime (엔티티 단위).
    /// 마을당 Shrine 1개 강제 (BuildableItemTable.MaxPerVillage = 1) — 어뷰징 차단.
    ///
    /// 버프 3종 (BuffTable Id 매핑은 Step 10b에서 결정):
    ///   - 가호의 빛: 받는 데미지 -10%
    ///   - 사냥꾼의 정확: 치명타 +10%
    ///   - 헤르메스의 발: 이속 +20%
    ///
    /// UI Toolkit 기반 (App UI 컴포넌트 사용). prefab의 UIDocument 컴포넌트 필요.
    /// </summary>
    public class UIShrine : UIBaseForm
    {
        private const float COOLDOWN_HOURS = 12f;
        private const float BUFF_DURATION_HOURS = 0.5f; // 30분 게임시간

        public enum ShrineBuff { ProtectionLight, HuntersAim, HermesFeet }

        private UIDocument? _document;
        private Button? _buff1Btn;
        private Button? _buff2Btn;
        private Button? _buff3Btn;
        private IconButton? _closeBtn;
        private Text? _statusText;
        private Text? _cooldownText;

        private int _shrineEntityId = -1;
        private int _villageId = -1;
        private float _lastUseGameTime;
        private bool _isOnCooldown;
        private VisualElement? _lastRoot;

        public override void Initialize(string inName, bool isForm = false)
        {
            base.Initialize(inName, isForm);

            _document = GetComponent<UIDocument>();
            if (_document == null)
            {
                Debug.LogError("[UIShrine] UIDocument 컴포넌트 없음 — prefab 확인 필요");
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

            _buff1Btn = root.Q<Button>("buff1-btn");
            _buff2Btn = root.Q<Button>("buff2-btn");
            _buff3Btn = root.Q<Button>("buff3-btn");
            _closeBtn = root.Q<IconButton>("close-btn");
            _statusText = root.Q<Text>("status-text");
            _cooldownText = root.Q<Text>("cooldown-text");

            if (_buff1Btn != null) _buff1Btn.clicked += () => OnClickBuff(ShrineBuff.ProtectionLight);
            if (_buff2Btn != null) _buff2Btn.clicked += () => OnClickBuff(ShrineBuff.HuntersAim);
            if (_buff3Btn != null) _buff3Btn.clicked += () => OnClickBuff(ShrineBuff.HermesFeet);
            if (_closeBtn != null) _closeBtn.clicked += () => Close();
        }

        public void Bind(int shrineEntityId)
        {
            EnsureBound();
            _shrineEntityId = shrineEntityId;

            if (AR.s.Component.TryGetComponent<PlacedObjectComponent>(_shrineEntityId, out var po) == false)
            {
                Debug.LogWarning($"[UIShrine] Bind 실패 — entityId={shrineEntityId} PlacedObject 없음");
                return;
            }
            _villageId = po.VillageId;
            _lastUseGameTime = po.LastUseGameTime;

            CheckCooldown();
            RefreshAll();
        }

        /// <summary>
        /// 테스트용: 실제 Shrine 엔티티 없이 UI만 띄울 때.
        /// </summary>
        public void BindForTest()
        {
            EnsureBound();
            _shrineEntityId = -1;
            _villageId = -1;
            _lastUseGameTime = -100f; // 충분히 과거 → 쿨다운 해제 상태
            _isOnCooldown = false;
            RefreshAll();
            Debug.Log("[UIShrine] BindForTest — 테스트 모드로 UI 열림");
        }

        public override void OnOpen()
        {
            base.OnOpen();
            EnsureBound();
            if (_shrineEntityId >= 0)
            {
                CheckCooldown();
                RefreshAll();
            }
        }

        // ========== 액션 ==========

        public void OnClickBuff(ShrineBuff buff)
        {
            CheckCooldown();
            if (_isOnCooldown)
            {
                float remain = COOLDOWN_HOURS - (AR.s.Time.CurrentGameTime - _lastUseGameTime);
                AR.s.UI.SetNotify($"제단 쿨다운 ({remain:F1}h 남음)");
                return;
            }

            // TODO: Gold 차감 (Stage 기반)
            // TODO: BuffTable Id 매핑 후 AR.s.Buff.ApplyBuff(player, buffId, BUFF_DURATION_HOURS)
            float now = AR.s.Time.CurrentGameTime;
            if (_shrineEntityId >= 0)
                UpdateLastUseGameTime(now);
            else
                _lastUseGameTime = now; // 테스트 모드

            Debug.Log($"[Shrine] v{_villageId} 버프 {buff} 적용 + {COOLDOWN_HOURS}h 쿨다운 시작");
            CheckCooldown();
            RefreshAll();
        }

        // ========== 내부 상태 ==========

        private void CheckCooldown()
        {
            float now = AR.s.Time.CurrentGameTime;
            _isOnCooldown = (now - _lastUseGameTime) < COOLDOWN_HOURS;
        }

        private void UpdateLastUseGameTime(float now)
        {
            if (AR.s.Component.TryGetComponent<PlacedObjectComponent>(_shrineEntityId, out var po) == false)
                return;
            po.LastUseGameTime = now;
            AR.s.Component.SetComponent(_shrineEntityId, po);
            _lastUseGameTime = now;
        }

        // ========== 갱신 ==========

        private void RefreshAll()
        {
            if (_statusText != null)
            {
                _statusText.text = _isOnCooldown
                    ? "쿨다운 중 — 사용 불가"
                    : "가호 받기 가능";
            }

            if (_cooldownText != null)
            {
                if (_isOnCooldown)
                {
                    float remain = COOLDOWN_HOURS - (AR.s.Time.CurrentGameTime - _lastUseGameTime);
                    _cooldownText.text = $"남은 쿨다운: {remain:F1}h";
                }
                else
                {
                    _cooldownText.text = "지속시간: 30분 / 쿨다운: 12h";
                }
            }

            bool canUse = _isOnCooldown == false;
            if (_buff1Btn != null) _buff1Btn.SetEnabled(canUse);
            if (_buff2Btn != null) _buff2Btn.SetEnabled(canUse);
            if (_buff3Btn != null) _buff3Btn.SetEnabled(canUse);
        }
    }
}
