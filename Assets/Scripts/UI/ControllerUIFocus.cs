#nullable enable
using System.Collections.Generic;
using ARPG.Base;
using ARPG.Input;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace ARPG.UI
{
    /// <summary>UIManager-owned directional focus for Toolkit controls, item cells and legacy menu buttons.</summary>
    public sealed class ControllerUIFocus
    {
        private readonly List<VisualElement> _candidates = new();
        private UIBase? _owner;
        private VisualElement? _focused;
        private VisualElement? _lastRoot;
        private VisualElement? _lastScope;
        private float _nextMove;
        private int _legacyIndex;
        private readonly List<UnityEngine.UI.Button> _legacy = new();
        private StyleSheet? _focusStyle;
        public void Initialize() { _focusStyle = Resources.Load<StyleSheet>("ControllerFocus"); }

        public void Reset()
        {
            _focused?.RemoveFromClassList("controller-focused");
            _focused = null; _owner = null; _lastRoot = null; _lastScope = null; _candidates.Clear(); _legacy.Clear();
        }

        public void Update(UIBase? owner, ArpgInput input)
        {
            if (owner == null) { if (_owner != null) Reset(); return; }
            if (EventSystem.current != null) EventSystem.current.sendNavigationEvents = false;
            if (owner is UISettings settings && settings.IsCapturing == true) return;
            bool changed = _owner != owner;
            if (changed == true) { Reset(); _owner = owner; _legacyIndex = 0; }
            Vector2 direction = input.UI.Navigate.ReadValue<Vector2>();
            bool move = direction.sqrMagnitude > 0.25f && Time.unscaledTime >= _nextMove;
            if (direction.sqrMagnitude < 0.1f) _nextMove = 0f;
            bool submit = input.UI.Submit.WasPressedThisFrame();
            bool secondary = input.IsGamepad == true && Gamepad.current?.buttonNorth.wasPressedThisFrame == true;
            UIDocument? document = owner.GetComponent<UIDocument>();
            if (document == null)
            {
                if (changed == true || move == true || submit == true) UpdateLegacy(owner, direction, move, submit);
                return;
            }
            VisualElement root = document.rootVisualElement;
            if (_focusStyle != null && root.styleSheets.Contains(_focusStyle) == false) root.styleSheets.Add(_focusStyle);
            if (root != _lastRoot) { _focused = null; _lastRoot = root; changed = true; }
            VisualElement scope = root;
            if (owner is UISettings settingsWindow && settingsWindow.ControllerFocusRoot != null) scope = settingsWindow.ControllerFocusRoot;
            else if (owner is UISkillBook skillWindow && skillWindow.ControllerFocusRoot != null) scope = skillWindow.ControllerFocusRoot;
            if (scope != _lastScope) { _lastScope = scope; _focused = null; changed = true; }
            if (changed == false && move == false && submit == false && secondary == false && _focused?.panel != null) return;
            _candidates.Clear(); Collect(scope);
            if (_candidates.Count == 0) return;
            if (_focused == null || _candidates.Contains(_focused) == false) SetFocus(_candidates[0]);
            if (move == true)
            {
                _nextMove = Time.unscaledTime + 0.18f;
                if (_focused is Slider slider && Mathf.Abs(direction.x) > Mathf.Abs(direction.y)) slider.value += Mathf.Sign(direction.x) * (slider.highValue - slider.lowValue) * 0.05f;
                else if (_focused is DropdownField dropdown && Mathf.Abs(direction.x) > Mathf.Abs(direction.y)) dropdown.index = (dropdown.index + (direction.x > 0 ? 1 : dropdown.choices.Count - 1)) % dropdown.choices.Count;
                else Move(direction);
            }
            if (_focused == null) return;
            if (secondary == true)
            {
                if (owner is UICharmBoard charmBoard) charmBoard.ControllerSecondary(_focused);
                else if (owner is UICharacter character && character.ControllerActivate(_focused, true) == true) { }
                else if (owner is UISkillBook skillBook && skillBook.ControllerActivate(_focused, true) == true) { }
                else Activate(_focused, true);
            }
            if (submit == true)
            {
                if (owner is UICharmBoard charmBoard) charmBoard.ControllerActivate(_focused);
                else if (owner is UICharacter character && character.ControllerActivate(_focused, false) == true) { }
                else if (owner is UISkillBook skillBook && skillBook.ControllerActivate(_focused, false) == true) { }
                else Activate(_focused, false);
            }
        }

        private void Collect(VisualElement element)
        {
            if (element.resolvedStyle.display == DisplayStyle.None || element.resolvedStyle.visibility != Visibility.Visible || element.enabledInHierarchy == false) return;
            bool control = element is Button || element is Unity.AppUI.UI.Button || element is Unity.AppUI.UI.IconButton
                || element is Toggle || element is Slider || element is DropdownField
                || element.ClassListContains("char-inv-slot") || element.ClassListContains("char-equip-slot")
                || element.ClassListContains("skillbook-inv-item") || element.ClassListContains("skillbook-slot")
                || element.ClassListContains("shop-sell-slot") || element.ClassListContains("forge-inv-slot-equipment")
                || element.ClassListContains("charm-cell") || element.ClassListContains("charm-bag-item")
                || element.ClassListContains("charm-board-slot") || element.ClassListContains("skillpage-inv-item") || element.ClassListContains("skillpage-slot");
            if (control == true && element.worldBound.width > 0f && element.worldBound.height > 0f)
            { element.focusable = true; _candidates.Add(element); return; }
            for (int i = 0; i < element.childCount; i++) Collect(element[i]);
        }

        private void Move(Vector2 direction)
        {
            if (_focused == null) return;
            Vector2 origin = _focused.worldBound.center;
            Vector2 axis = Mathf.Abs(direction.x) > Mathf.Abs(direction.y) ? new Vector2(Mathf.Sign(direction.x), 0) : new Vector2(0, -Mathf.Sign(direction.y));
            VisualElement? best = null; float score = float.MaxValue;
            for (int i = 0; i < _candidates.Count; i++)
            {
                Vector2 delta = _candidates[i].worldBound.center - origin;
                float ahead = Vector2.Dot(delta, axis);
                if (ahead <= 1f) continue;
                float perpendicular = Mathf.Abs(delta.x * axis.y - delta.y * axis.x);
                float value = ahead + perpendicular * 4f;
                if (value < score) { score = value; best = _candidates[i]; }
            }
            if (best != null) SetFocus(best);
        }

        private void SetFocus(VisualElement element)
        {
            if (_focused != null)
            {
                using PointerLeaveEvent leave = PointerLeaveEvent.GetPooled(new Event { type = EventType.MouseMove, mousePosition = _focused.worldBound.center });
                leave.target = _focused; _focused.SendEvent(leave);
                using MouseLeaveEvent mouseLeave = MouseLeaveEvent.GetPooled(new Event { type = EventType.MouseMove, mousePosition = _focused.worldBound.center });
                mouseLeave.target = _focused; _focused.SendEvent(mouseLeave);
            }
            _focused?.RemoveFromClassList("controller-focused"); _focused = element;
            element.AddToClassList("controller-focused"); element.Focus();
            using (PointerEnterEvent enter = PointerEnterEvent.GetPooled(new Event { type = EventType.MouseMove, mousePosition = element.worldBound.center })) { enter.target = element; element.SendEvent(enter); }
            using (MouseEnterEvent enter = MouseEnterEvent.GetPooled(new Event { type = EventType.MouseMove, mousePosition = element.worldBound.center })) { enter.target = element; element.SendEvent(enter); }
            for (VisualElement? parent = element.parent; parent != null; parent = parent.parent)
                if (parent is ScrollView scroll) { scroll.ScrollTo(element); break; }
        }

        private void UpdateLegacy(UIBase owner, Vector2 direction, bool move, bool submit)
        {
            _legacy.Clear(); owner.GetComponentsInChildren(false, _legacy);
            for (int i = _legacy.Count - 1; i >= 0; i--) if (_legacy[i].interactable == false) _legacy.RemoveAt(i);
            if (_legacy.Count == 0) return;
            _legacyIndex = Mathf.Clamp(_legacyIndex, 0, _legacy.Count - 1);
            if (move == true)
            {
                _nextMove = Time.unscaledTime + 0.18f;
                int delta = direction.y > 0.1f || direction.x < -0.1f ? -1 : 1;
                _legacyIndex = (_legacyIndex + delta + _legacy.Count) % _legacy.Count;
            }
            if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(_legacy[_legacyIndex].gameObject);
            if (submit == true) _legacy[_legacyIndex].onClick.Invoke();
        }

        private static void Activate(VisualElement element, bool secondary)
        {
            if (element is Toggle toggle) { if (secondary == false) toggle.value = toggle.value == false; return; }
            if (element is DropdownField dropdown) { if (secondary == false && dropdown.choices.Count > 0) dropdown.index = (dropdown.index + 1) % dropdown.choices.Count; return; }
            bool pointer = element.ClassListContains("char-inv-slot") || element.ClassListContains("char-equip-slot") || secondary == true;
            if (pointer == true)
            {
                Event down = new() { type = EventType.MouseDown, button = secondary == true ? 1 : 0, mousePosition = element.worldBound.center };
                using (PointerDownEvent evt = PointerDownEvent.GetPooled(down)) { evt.target = element; element.SendEvent(evt); }
                Event up = new() { type = EventType.MouseUp, button = secondary == true ? 1 : 0, mousePosition = element.worldBound.center };
                using (PointerUpEvent evt = PointerUpEvent.GetPooled(up)) { evt.target = element; element.SendEvent(evt); }
                return;
            }
            if (element is Button || element is Unity.AppUI.UI.Button || element is Unity.AppUI.UI.IconButton)
            { using NavigationSubmitEvent evt = NavigationSubmitEvent.GetPooled(); evt.target = element; element.SendEvent(evt); }
            else { using ClickEvent evt = ClickEvent.GetPooled(); evt.target = element; element.SendEvent(evt); }
        }
    }
}
