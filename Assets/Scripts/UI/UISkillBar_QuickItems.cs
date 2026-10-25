#nullable enable
using ARPG.Input;
using ARPG.Systems;
using UnityEngine;
using UnityEngine.UIElements;

namespace ARPG.UI
{
    public partial class UISkillBar
    {
        private readonly Label?[] _quickLabels = new Label?[4];
        private readonly VisualElement?[] _quickIcons = new VisualElement?[4];
        private readonly Button?[] _quickButtons = new Button?[4];
        private readonly int[] _shownQuickIds = { -1, -1, -1, -1 };
        private float _nextQuickRefresh;
        private int _keysRevision = -1;

        private void BindQuickItemBar(VisualElement root)
        {
            VisualElement? hud = root.Q<VisualElement>("skillbar-hud");
            if (hud == null) return;
            VisualElement row = new() { name = "quickitem-hud" };
            row.style.flexDirection = FlexDirection.Row;
            row.style.justifyContent = Justify.Center;
            row.style.marginTop = 6;
            for (int i = 0; i < 4; i++)
            {
                int slot = i;
                Button button = new(() =>
                {
                    if (AR.s.UI.IsLock == true) return;
                    AR.s.UI.Show<UIQuickSlots>(AddressablePath.QuickSlots, UIManager.Layer.Main)?.SelectSlot(slot);
                });
                button.style.width = 170; button.style.height = 82;
                button.style.marginLeft = 4; button.style.marginRight = 4;
                button.style.backgroundColor = new Color(0.12f, 0.12f, 0.14f, 0.96f);
                VisualElement icon = new() { pickingMode = PickingMode.Ignore };
                icon.style.width = 28; icon.style.height = 36; icon.style.alignSelf = Align.Center;
                Label label = new() { pickingMode = PickingMode.Ignore };
                label.style.fontSize = 16; label.style.whiteSpace = WhiteSpace.Normal; label.style.unityTextAlign = TextAnchor.MiddleCenter;
                button.Add(icon); button.Add(label); row.Add(button);
                _quickButtons[i] = button; _quickLabels[i] = label; _quickIcons[i] = icon;
                _shownQuickIds[i] = -1;
            }
            hud.Add(row); _keysRevision = -1; _nextQuickRefresh = 0f;
        }

        private void RefreshKeysAndQuickItems()
        {
            if (_lastRoot == null || _ar == null) return;
            if (_keysRevision != _ar.UI.Input.DisplayRevision)
            {
                _keysRevision = _ar.UI.Input.DisplayRevision;
                if (_settingsButton != null) _settingsButton.text = _ar.UI.Input.Display("SkillBook") + " 스킬 설정";
                RefreshExp();
                for (int i = 0; i < 10; i++)
                {
                    Label? key = _lastRoot.Q<Label>($"key-{i}");
                    if (key != null) key.text = _ar.UI.Input.Display(GameInputBindings.SkillNames[i]);
                }
            }
            if (Time.unscaledTime < _nextQuickRefresh) return;
            _nextQuickRefresh = Time.unscaledTime + 0.1f;
            var player = _ar.Data.Player;
            if (player == null) return;
            for (int i = 0; i < 4; i++)
            {
                int id = player._quickItemIds[i];
                var table = _ar.Data.GetItem(id);
                int count = _ar.Player.Inventory.GetTotalQuantity(id);
                float remaining = System_QuickItem.Remaining(_ar.Data.CurrentPlayerEntityId, id);
                Label? label = _quickLabels[i]; Button? button = _quickButtons[i]; VisualElement? icon = _quickIcons[i];
                if (label == null || button == null || icon == null) continue;
                string cooldown = remaining > 0f ? $" · {remaining:0.0}s" : string.Empty;
                label.text = $"{_ar.UI.Input.Display(GameInputBindings.ItemNames[i])}\n{table?.Name ?? "빈 슬롯"} ×{count}{cooldown}";
                button.style.opacity = count > 0 ? 1f : 0.55f;
                button.tooltip = table?.Description ?? "소모품을 등록하려면 선택하세요.";
                if (_shownQuickIds[i] != id)
                {
                    _shownQuickIds[i] = id;
                    Sprite? sprite = table == null || string.IsNullOrEmpty(table.SpriteName) == true ? null : _ar.Data.GetSprite(table.SpriteName);
                    icon.style.backgroundImage = sprite == null ? StyleKeyword.None : new StyleBackground(sprite);
                    RecoveryPotionIcon.Draw(icon, table);
                }
            }
        }
    }
}
