#nullable enable
using System;
using ARPG.Base;
using ARPG.Data;
using ARPG.Input;
using UnityEngine;
using UnityEngine.UIElements;

namespace ARPG.UI
{
    [RequireComponent(typeof(UIDocument))]
    public sealed class UIQuickSlots : UIBaseForm
    {
        private VisualElement _root = null!;
        private VisualElement _inventory = null!;
        private Label _status = null!;
        private readonly Button[] _slots = new Button[4];
        private int _selectedSlot;
        private float _refreshAt;
        private int _inventorySignature;
        private int _displayRevision = -1;
        private int _dragItem;
        private Vector2 _dragStart;
        private bool _suppressClick;
        private int _pendingItemId;

        public override void OnOpen()
        {
            _pendingItemId = 0;
            base.OnOpen();
            _root = GetComponent<UIDocument>().rootVisualElement.Q("quickslots-root");
            _root.Clear();
            VisualElement panel = new(); panel.AddToClassList("settings-panel"); _root.Add(panel);
            Label title = new("아이템 퀵슬롯"); title.AddToClassList("settings-title"); panel.Add(title);
            panel.Add(new Label("등록할 슬롯을 선택하고 인벤토리의 소모품을 선택하세요. 마우스로 끌어다 놓아도 됩니다."));
            VisualElement row = new(); row.AddToClassList("settings-row"); panel.Add(row);
            for (int i = 0; i < 4; i++)
            {
                int slot = i;
                _slots[i] = new Button(() => SelectSlot(slot)); row.Add(_slots[i]);
                _slots[i].AddToClassList("quickitem-register-slot");
            }
            ScrollView scroll = new(); scroll.AddToClassList("settings-content"); panel.Add(scroll); _inventory = scroll.contentContainer;
            _status = new Label(); _status.AddToClassList("settings-status"); panel.Add(_status);
            VisualElement footer = new(); footer.AddToClassList("settings-row"); panel.Add(footer);
            footer.Add(new Button(() => { AR.s.Player.RegisterQuickItem(_selectedSlot, 0); RefreshSlots(); }) { text = "선택 슬롯 해제" });
            footer.Add(new Button(() => Close()) { text = "닫기" });
            _inventorySignature = int.MinValue; _displayRevision = -1; Refresh();
        }

        public override void OnClose() { _dragItem = 0; base.OnClose(); }
        public void SelectSlot(int slot)
        {
            _selectedSlot = Mathf.Clamp(slot, 0, 3);
            if (_pendingItemId > 0) { Register(_pendingItemId); _pendingItemId = 0; }
            if (_slots[0] != null) RefreshSlots();
        }
        public void SelectItem(int itemId) { _pendingItemId = itemId; _status.text = "등록할 퀵슬롯을 선택하세요."; }

        private void Update()
        {
            if (_root == null || Time.unscaledTime < _refreshAt) return;
            _refreshAt = Time.unscaledTime + 0.15f; Refresh();
        }

        private void Refresh()
        {
            int signature = 17;
            var items = AR.s.Player.Inventory.Items;
            for (int i = 0; i < items.Count; i++)
                if (items[i] is ItemData item && item.Table?.ItemType == GlobalEnum.ItemType.Consumable)
                    signature = unchecked(signature * 31 + item.Id * 7 + item.Quantity);
            if (signature != _inventorySignature) { _inventorySignature = signature; RebuildInventory(); }
            if (_displayRevision != AR.s.UI.Input.DisplayRevision) { _displayRevision = AR.s.UI.Input.DisplayRevision; RefreshSlots(); }
        }

        private void RefreshSlots()
        {
            for (int i = 0; i < 4; i++)
            {
                int id = AR.s.Data.Player._quickItemIds[i];
                var table = AR.s.Data.GetItem(id);
                _slots[i].text = $"{AR.s.UI.Input.Display(GameInputBindings.ItemNames[i])}\n{table?.Name ?? "빈 슬롯"}";
                _slots[i].EnableInClassList("quickitem-selected", i == _selectedSlot);
            }
        }

        private void RebuildInventory()
        {
            _inventory.Clear();
            var items = AR.s.Player.Inventory.Items;
            int count = 0;
            for (int i = 0; i < items.Count; i++)
            {
                ItemData? item = items[i];
                if (item?.Table == null || item.Table.ItemType != GlobalEnum.ItemType.Consumable
                    || (item.Table.RestoreHpPercent <= 0 && item.Table.RestoreMpPercent <= 0)) continue;
                int itemId = item.Id;
                Button button = new(() => { if (_suppressClick == true) { _suppressClick = false; return; } Register(itemId); });
                button.text = $"{item.Table.Name}   ×{item.Quantity} — {item.Table.Description}";
                button.AddToClassList("quickitem-inventory-item");
                button.userData = itemId;
                button.RegisterCallback<PointerDownEvent>(OnDragStart);
                button.RegisterCallback<PointerUpEvent>(OnDragEnd);
                _inventory.Add(button); count++;
            }
            if (count == 0) _inventory.Add(new Label("등록할 수 있는 소모품이 없습니다. 상인에게 회복 물약을 구매할 수 있습니다."));
        }

        private void Register(int itemId)
        {
            _status.text = AR.s.Player.RegisterQuickItem(_selectedSlot, itemId) == true ? $"슬롯 {_selectedSlot + 1}에 등록했습니다." : "등록할 수 없는 아이템입니다.";
            RefreshSlots();
        }

        private void OnDragStart(PointerDownEvent evt)
        {
            if (evt.button != 0 || evt.currentTarget is VisualElement element == false) return;
            element = (VisualElement)evt.currentTarget;
            _dragItem = element.userData is int id ? id : 0; _dragStart = evt.position;
            element.CapturePointer(evt.pointerId);
        }

        private void OnDragEnd(PointerUpEvent evt)
        {
            if (_dragItem == 0) return;
            int itemId = _dragItem; _dragItem = 0;
            if (evt.currentTarget is VisualElement element) element.ReleasePointer(evt.pointerId);
            if (((Vector2)evt.position - _dragStart).sqrMagnitude < 36f) return;
            _suppressClick = true;
            for (int i = 0; i < 4; i++)
                if (_slots[i].worldBound.Contains(evt.position) == true) { _selectedSlot = i; Register(itemId); break; }
            evt.StopPropagation();
        }
    }
}
