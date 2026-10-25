#nullable enable
using ARPG.Data;
using UnityEngine.UIElements;

namespace ARPG.UI
{
    public partial class UICharacter
    {
        private int _controllerMoveIndex = -1;
        private ItemData? _controllerMoveItem;
        private System.Action _quickSlotsClickedHandler = null!;
        private int _controllerBindingRevision = -1;

        private void OnOpenQuickSlots() => AR.s.UI.Show<UIQuickSlots>(AddressablePath.QuickSlots, UIManager.Layer.Main);

        private void RefreshControllerBindingLabels(Input.ArpgInput input)
        {
            if (_controllerBindingRevision == input.DisplayRevision) return;
            _controllerBindingRevision = input.DisplayRevision;
            var button = _lastRoot?.Q<Unity.AppUI.UI.Button>("charm-board-btn");
            if (button != null) button.title = "참 판 (" + input.Display("CharmBoard") + ")";
        }

        public bool ControllerActivate(VisualElement element, bool secondary)
        {
            if (element.userData is not SlotView view || element.ClassListContains("char-inv-slot") == false) return false;
            ItemData? item = AR.s.Player.Inventory.GetItemBySlotIndex(view.Index);
            if (secondary == true)
            {
                _controllerMoveIndex = view.Index; _controllerMoveItem = item;
                AR.s.UI.SetNotify(item == null ? "빈 칸입니다." : "놓을 인벤토리 칸을 선택하세요."); return true;
            }
            if (_controllerMoveItem != null)
            {
                if (ReferenceEquals(AR.s.Player.Inventory.GetItemBySlotIndex(_controllerMoveIndex), _controllerMoveItem) == true)
                    AR.s.Player.Inventory.MoveItem(_controllerMoveIndex, view.Index);
                _controllerMoveIndex = -1; _controllerMoveItem = null; SyncSlots(false); return true;
            }
            if (item?.Table?.ItemType == GlobalEnum.ItemType.Consumable)
            {
                AR.s.UI.Show<UIQuickSlots>(AddressablePath.QuickSlots, UIManager.Layer.Main)?.SelectItem(item.Id); return true;
            }
            return false;
        }
    }
}
