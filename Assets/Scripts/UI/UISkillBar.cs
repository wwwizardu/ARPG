#nullable enable
using System;
using System.Globalization;
using ARPG.Base;
using ARPG.Component;
using ARPG.Data;
using ARPG.Manager;
using ARPG.Message;
using ARPG.Utility;
using UnityEngine;
using UnityEngine.UIElements;

namespace ARPG.UI
{
    [RequireComponent(typeof(UIDocument))]
    public partial class UISkillBar : UIBaseForm
    {
        [SerializeField, Range(0.5f, 1.25f)]
        [Tooltip("스킬 바 전체 배율. 1 = 원래 크기, 0.8 = 80%. 실행 중에도 조절할 수 있습니다.")]
        private float _uiScale = 0.8f;

        private sealed class SlotView
        {
            public VisualElement _element = null!;
            public VisualElement _icon = null!;
            public Label _name = null!;
            public VisualElement _cooldownFill = null!;
            public Label _cooldownText = null!;
            public ItemData? _book;
            public int _skillId;
            public int _cooldownTenths = -1;
            public int _stateFlags = -1;
        }

        private readonly SlotView?[] _slots = new SlotView?[GlobalEnum.PLAYER_SKILL_SLOT_COUNT];
        private AR? _ar;
        private UIDocument? _document;
        private VisualElement? _lastRoot;
        private VisualElement? _panel;
        private Button? _settingsButton;
        private SlotView? _hoveredSlot;
        // 경험치 바 (PLAYER_LEVEL_CHARM_DESIGN §8.1)
        private VisualElement? _expBar;
        private VisualElement? _expFill;
        private Label? _expLevel;
        private Label? _expPoints;
        private bool _expHovered;
        private MessageManager? _messageManager;
        private TooltipManager? _tooltipManager;
        private Action<SkillBookChangedMessage> _skillBookChangedHandler = null!;
        private Action<PlayerProgressChangedMessage> _progressChangedHandler = null!;
        private EventCallback<PointerEnterEvent> _expPointerEnterHandler = null!;
        private EventCallback<PointerLeaveEvent> _expPointerLeaveHandler = null!;
        private Action _settingsClickedHandler = null!;
        private EventCallback<PointerEnterEvent> _pointerEnterHandler = null!;
        private EventCallback<PointerLeaveEvent> _pointerLeaveHandler = null!;
        private bool _initialized;
        private bool _isOpen;
        private int _playerEntityId = -1;
        private float _appliedScale = -1f;

        public float UIScale => _uiScale;

        public void SetUIScale(float scale)
        {
            _uiScale = Mathf.Clamp(scale, 0.5f, 1.25f);
            ApplyScale();
        }

        public override void Initialize(string inName, bool isForm = false)
        {
            base.Initialize(inName, isForm);
            _isBase = true;
            if (_initialized == true) return;

            _ar = AR.s;
            _document = GetComponent<UIDocument>();
            _skillBookChangedHandler = OnSkillBookChanged;
            _progressChangedHandler = OnProgressChanged;
            _expPointerEnterHandler = OnExpPointerEnter;
            _expPointerLeaveHandler = OnExpPointerLeave;
            _settingsClickedHandler = OnSettingsClicked;
            _pointerEnterHandler = OnSlotPointerEnter;
            _pointerLeaveHandler = OnSlotPointerLeave;
            _initialized = true;
        }

        public override void OnOpen()
        {
            base.OnOpen();
            if (_ar == null || _isOpen == true) return;

            _isOpen = true;
            _messageManager = _ar.Message;
            _tooltipManager = _ar.Tooltip;
            _messageManager.Subscribe(_skillBookChangedHandler);
            _messageManager.Subscribe(_progressChangedHandler);
            EnsureBound();
            RefreshAll();
            RefreshRuntimeState();
            RefreshExp();
        }

        public override void OnClose()
        {
            StopObserving();
            base.OnClose();
        }

        public bool IsPointerOver(Vector2 screenPosition)
        {
            return _isOpen == true && isActiveAndEnabled == true && _panel != null
                && GetScreenRect(_panel).Contains(screenPosition);
        }

        protected override void OnDestroy()
        {
            StopObserving();
            ReleaseBindings();
            base.OnDestroy();
        }

        private void OnDisable()
        {
            StopObserving();
        }

        private void LateUpdate()
        {
            if (_isOpen == false || _ar == null || _ar.IsInitialized == false) return;

            EnsureBound();
            int playerEntityId = _ar.Data.CurrentPlayerEntityId;
            RefreshKeysAndQuickItems();
            if (_playerEntityId != playerEntityId)
            {
                _playerEntityId = playerEntityId;
                RefreshAll();
                RefreshExp();
            }
            RefreshRuntimeState();

            if (_hoveredSlot != null)
            {
                if (_ar.UI.CurrentUI != null) HideTooltip();
                else _tooltipManager?.UpdatePosition(GetScreenRect(_hoveredSlot._element));
            }
            else if (_expHovered == true && _expBar != null)
            {
                if (_ar.UI.CurrentUI != null) HideTooltip();
                else _tooltipManager?.UpdatePosition(GetScreenRect(_expBar));
            }
        }

        private void EnsureBound()
        {
            VisualElement? root = _document?.rootVisualElement;
            if (root == null) return;
            if (root == _lastRoot)
            {
                ApplyScale();
                return;
            }

            ReleaseBindings();
            _lastRoot = root;
            // 배율·포인터 판정은 경험치 바까지 포함한 묶음 기준이다
            _panel = root.Q<VisualElement>("skillbar-hud") ?? root.Q<VisualElement>("skillbar-panel");
            BindQuickItemBar(root);
            ApplyScale();
            _expBar = root.Q<VisualElement>("exp-bar");
            _expFill = root.Q<VisualElement>("exp-fill");
            _expLevel = root.Q<Label>("exp-level");
            _expPoints = root.Q<Label>("exp-points");
            if (_expBar != null)
            {
                _expBar.tooltip = string.Empty;
                _expBar.RegisterCallback(_expPointerEnterHandler);
                _expBar.RegisterCallback(_expPointerLeaveHandler);
            }
            _settingsButton = root.Q<Button>("settings-btn");
            if (_settingsButton != null) _settingsButton.clicked += _settingsClickedHandler;

            for (int i = 0; i < _slots.Length; i++)
            {
                VisualElement? element = root.Q<VisualElement>($"slot-{i}");
                VisualElement? icon = root.Q<VisualElement>($"icon-{i}");
                Label? name = root.Q<Label>($"name-{i}");
                VisualElement? fill = root.Q<VisualElement>($"cooldown-fill-{i}");
                Label? text = root.Q<Label>($"cooldown-text-{i}");
                if (element == null || icon == null || name == null || fill == null || text == null)
                {
                    Debug.LogError($"[UISkillBar] Missing visual elements for slot {i}.");
                    continue;
                }

                SlotView slot = new() { _element = element, _icon = icon, _name = name, _cooldownFill = fill, _cooldownText = text };
                _slots[i] = slot;
                element.userData = slot;
                element.tooltip = string.Empty;
                element.RegisterCallback(_pointerEnterHandler);
                element.RegisterCallback(_pointerLeaveHandler);
            }
            RefreshAll();
        }

        private void ReleaseBindings()
        {
            HideTooltip();
            if (_settingsButton != null) _settingsButton.clicked -= _settingsClickedHandler;
            if (_expBar != null)
            {
                _expBar.UnregisterCallback(_expPointerEnterHandler);
                _expBar.UnregisterCallback(_expPointerLeaveHandler);
            }
            _expBar = null;
            _expFill = null;
            _expLevel = null;
            _expPoints = null;
            for (int i = 0; i < _slots.Length; i++)
            {
                SlotView? slot = _slots[i];
                if (slot == null) continue;
                slot._element.UnregisterCallback(_pointerEnterHandler);
                slot._element.UnregisterCallback(_pointerLeaveHandler);
                slot._element.userData = null;
                _slots[i] = null;
            }
            _settingsButton = null;
            _panel = null;
            _lastRoot = null;
            _appliedScale = -1f;
        }

        private void ApplyScale()
        {
            if (_panel == null) return;
            _uiScale = Mathf.Clamp(_uiScale, 0.5f, 1.25f);
            if (_appliedScale == _uiScale) return;

            _panel.style.scale = new Scale(new Vector3(_uiScale, _uiScale, 1f));
            _appliedScale = _uiScale;
        }

        private void StopObserving()
        {
            if (_messageManager != null)
            {
                _messageManager.Unsubscribe(_skillBookChangedHandler);
                _messageManager.Unsubscribe(_progressChangedHandler);
            }
            _messageManager = null;
            _isOpen = false;
            _playerEntityId = -1;
            HideTooltip();
        }

        private void OnSkillBookChanged(SkillBookChangedMessage message)
        {
            if (message.SlotIndex < 0 || message.SlotIndex >= _slots.Length) return;
            RefreshSlot(message.SlotIndex);
            RefreshRuntimeState();
        }

        private void OnProgressChanged(PlayerProgressChangedMessage message)
        {
            RefreshExp();
            if (_expHovered == true && _expBar != null) ShowExpTooltip();
            if (message.LeveledUp == false || _ar == null) return;

            // 레벨업 순간 (§8.1): 머리 위 글자, 판 자리가 열리는 레벨이면 알림
            Base.EntityBase? player = _ar.MyPlayer;
            if (player != null && _ar.Component.TryGetComponent<TransformComponent>(player.EntityId, out var transform) == true)
                _ar.FloatingText.ShowLevelUpText(transform.Position, message.Level);
            if (message.CharmBoardSlotOpened == true)
                _ar.UI.SetNotify($"참 판 자리가 열렸습니다 ({_ar.UI.Input.Display("CharmBoard")})");
        }

        /// <summary>경험치 바 채움·레벨·남은 포인트 (§8.1). 남은 포인트가 0이면 숨긴다.</summary>
        private void RefreshExp()
        {
            PlayerData? data = _ar?.Data.Player;
            if (data == null) return;

            int required = PlayerLevelRules.GetRequiredExp(data.Level);
            float fraction = required > 0 ? Mathf.Clamp01(data.Exp / (float)required) : 1f;
            if (_expFill != null) _expFill.style.width = Length.Percent(fraction * 100f);
            if (_expLevel != null) _expLevel.text = $"Lv {data.Level}";
            if (_expPoints != null)
            {
                bool hasPoints = data.UnspentAttributePoints > 0;
                _expPoints.text = hasPoints ? $"능력치 포인트 {data.UnspentAttributePoints} ({_ar.UI.Input.Display("Inventory")})" : string.Empty;
                _expPoints.EnableInClassList("skillbar-exp-points-visible", hasPoints);
            }
        }

        private void OnExpPointerEnter(PointerEnterEvent evt)
        {
            _expHovered = true;
            ShowExpTooltip();
        }

        private void OnExpPointerLeave(PointerLeaveEvent evt)
        {
            if (_expHovered == true) _tooltipManager?.Hide();
            _expHovered = false;
        }

        /// <summary>"레벨 N — 현재 / 필요 경험치 (x%)" (§8.1)</summary>
        private void ShowExpTooltip()
        {
            PlayerData? data = _ar?.Data.Player;
            if (_ar == null || data == null || _expBar == null || _ar.UI.CurrentUI != null) return;

            int required = PlayerLevelRules.GetRequiredExp(data.Level);
            string body = required > 0
                ? $"{data.Exp:N0} / {required:N0} 경험치 ({data.Exp * 100f / required:0.0}%)"
                : "최고 레벨";
            _tooltipManager?.ShowText($"레벨 {data.Level}", body, GetScreenRect(_expBar));
        }

        private void RefreshAll()
        {
            for (int i = 0; i < _slots.Length; i++) RefreshSlot(i);
        }

        private void RefreshSlot(int slotIndex)
        {
            SlotView? slot = _slots[slotIndex];
            if (slot == null || _ar == null) return;

            slot._book = _ar.PlayerSkill.GetEquippedBook(slotIndex);
            slot._skillId = slot._book?.SkillBook?.SkillId ?? 0;
            bool equipped = slot._skillId > 0;
            slot._element.EnableInClassList("skillbar-slot-empty", equipped == false);
            slot._element.EnableInClassList("skillbar-slot-equipped", equipped);
            slot._name.text = equipped ? (slot._book?.SkillBook?.Table ?? _ar.Data.GetSkill(slot._skillId))?.Name ?? "?" : "—";

            string? spriteName = slot._book?.Table?.SpriteName;
            Sprite? sprite = string.IsNullOrEmpty(spriteName) == false ? _ar.Data.GetSprite(spriteName) : null;
            slot._icon.style.backgroundImage = sprite != null ? new StyleBackground(sprite) : new StyleBackground(StyleKeyword.None);
            SetRuntimeState(slot, 0f, 0f, false, false);
            if (_hoveredSlot == slot) ShowTooltip(slot);
        }

        private void RefreshRuntimeState()
        {
            if (_ar == null) return;
            ComponentManager cm = AR.s.Component;
            int ownerId = _ar.Data.CurrentPlayerEntityId;
            bool hasStat = cm.TryGetComponent<StatComponent>(ownerId, out var ownerStat);

            for (int i = 0; i < _slots.Length; i++)
            {
                SlotView? slot = _slots[i];
                if (slot == null || slot._skillId <= 0) continue;

                int skillEntityId = ownerId >= 0 ? EntityManager.GetDeterministicId(ownerId, EntityIdCategory.Skill, i) : -1;
                if (cm.TryGetComponent<SkillComponent>(skillEntityId, out var skill) == false
                    || skill.SkillId != slot._skillId
                    || cm.TryGetComponent<SkillStateComponent>(skillEntityId, out var state) == false)
                {
                    SetRuntimeState(slot, 0f, 0f, false, false);
                    continue;
                }

                bool insufficientMana = hasStat == true && skill.Table != null && ownerStat.CurrentMp < skill.Table.Mana;
                SetRuntimeState(slot, state.CooldownRemaining, state.CooldownDuration,
                    state.IsRunning == true || skill.IsToggleActive == true, insufficientMana);
            }
        }

        private static void SetRuntimeState(SlotView slot, float remaining, float duration, bool active, bool insufficientMana)
        {
            bool coolingDown = remaining > 0f;
            int flags = (coolingDown ? 1 : 0) | (active ? 2 : 0) | (insufficientMana ? 4 : 0);
            if (slot._stateFlags != flags)
            {
                slot._stateFlags = flags;
                slot._element.EnableInClassList("skillbar-slot-cooldown", coolingDown);
                slot._element.EnableInClassList("skillbar-slot-active", active);
                slot._element.EnableInClassList("skillbar-slot-no-mana", insufficientMana);
            }

            int tenths = coolingDown ? Mathf.CeilToInt(remaining * 10f) : 0;
            if (slot._cooldownTenths != tenths)
            {
                slot._cooldownTenths = tenths;
                slot._cooldownText.text = tenths > 0 ? (tenths * 0.1f).ToString("0.0", CultureInfo.InvariantCulture) : string.Empty;
            }
            if (coolingDown == true)
            {
                float fraction = duration > 0f ? Mathf.Clamp01(remaining / duration) : 1f;
                slot._cooldownFill.style.height = Length.Percent(fraction * 100f);
            }
        }

        private void OnSettingsClicked()
        {
            if (_ar == null || _ar.UI.IsLock == true || _ar.UI.CurrentUI != null) return;
            HideTooltip();
            _ar.UI.Show<UISkillBook>(AddressablePath.SkillBook, UIManager.Layer.Main);
        }

        private void OnSlotPointerEnter(PointerEnterEvent evt)
        {
            if (evt.currentTarget is VisualElement element && element.userData is SlotView slot)
            {
                _hoveredSlot = slot;
                ShowTooltip(slot);
            }
        }

        private void OnSlotPointerLeave(PointerLeaveEvent evt)
        {
            if (evt.currentTarget == _hoveredSlot?._element) HideTooltip();
        }

        private void ShowTooltip(SlotView slot)
        {
            if (_ar == null || _ar.UI.CurrentUI != null) return;
            _tooltipManager?.Show(slot._book, GetScreenRect(slot._element));
        }

        private void HideTooltip()
        {
            if (_hoveredSlot != null || _expHovered == true) _tooltipManager?.Hide();
            _hoveredSlot = null;
            _expHovered = false;
        }

        private static Rect GetScreenRect(VisualElement element)
        {
            IPanel? panel = element.panel;
            if (panel == null) return Rect.zero;
            float panelWidth = panel.visualTree.resolvedStyle.width;
            float panelHeight = panel.visualTree.resolvedStyle.height;
            if (panelWidth <= 0f || panelHeight <= 0f) return Rect.zero;

            Rect bounds = element.worldBound;
            float scaleX = Screen.width / panelWidth;
            float scaleY = Screen.height / panelHeight;
            return new Rect(bounds.xMin * scaleX, Screen.height - bounds.yMax * scaleY,
                bounds.width * scaleX, bounds.height * scaleY);
        }
    }
}
