#nullable enable
using ARPG.Data;
using ARPG.Item;
using UnityEngine.UIElements;

namespace ARPG.UI
{
    public partial class UICharmBoard
    {
        private int _controllerBagIndex = -1;
        private int _controllerSourceSlot = -1;
        private int _controllerPlacement = -1;
        private int _controllerRotation;

        public void ControllerActivate(VisualElement element)
        {
            if (element.userData is BagRef bag)
            {
                _controllerBagIndex = bag.InventoryIndex; _controllerSourceSlot = -1; _controllerPlacement = -1; _controllerRotation = 0;
                SetStatus("판·참을 선택했습니다. 놓을 칸을 선택하세요. 보조 버튼으로 회전합니다."); return;
            }
            if (element.userData is not CellRef cell)
            {
                using NavigationSubmitEvent evt = NavigationSubmitEvent.GetPooled(); evt.target = element; element.SendEvent(evt); return;
            }
            bool success = false; string reason = string.Empty;
            if (_controllerBagIndex >= 0)
            {
                ItemData? item = AR.s.Player.Inventory.GetItemBySlotIndex(_controllerBagIndex);
                if (item?.CharmBoard != null) success = AR.s.Player.TryEquipCharmBoard(cell.Slot, _controllerBagIndex, out reason);
                else if (item?.Charm != null) success = AR.s.Player.TryPlaceCharm(cell.Slot, _controllerBagIndex, cell.Cell, _controllerRotation, out reason);
            }
            else if (_controllerSourceSlot >= 0 && _controllerPlacement >= 0)
                success = AR.s.Player.TryMoveCharm(_controllerSourceSlot, _controllerPlacement, cell.Slot, cell.Cell, _controllerRotation, out reason);
            else
            {
                CharmBoardData? board = AR.s.Data.Player._charmBoardSlots[cell.Slot]?.CharmBoard;
                int placement = board == null ? -1 : CharmRules.FindPlacementAtCell(board, cell.Cell, _cellScratch);
                if (placement >= 0)
                {
                    _controllerSourceSlot = cell.Slot; _controllerPlacement = placement; _controllerRotation = board!.Charms[placement].Rotation;
                    SetStatus("참을 선택했습니다. 옮길 칸을 선택하세요. 보조 버튼으로 회전합니다.");
                }
                return;
            }
            if (success == true) ClearControllerSelection();
            SetStatus(success == true ? "배치했습니다." : reason);
        }

        public void ControllerSecondary(VisualElement element)
        {
            if (_controllerBagIndex >= 0 || _controllerPlacement >= 0)
            {
                _controllerRotation = (_controllerRotation + 1) % 4;
                SetStatus($"참 회전: {_controllerRotation * 90}°. 놓을 칸을 선택하세요."); return;
            }
            if (element.userData is not CellRef cell) return;
            CharmBoardData? board = AR.s.Data.Player._charmBoardSlots[cell.Slot]?.CharmBoard;
            if (board == null) return;
            int placement = CharmRules.FindPlacementAtCell(board, cell.Cell, _cellScratch);
            string reason;
            bool success = placement >= 0 ? AR.s.Player.TryRemoveCharm(cell.Slot, placement, out reason) : AR.s.Player.TryUnequipCharmBoard(cell.Slot, out reason);
            SetStatus(success == true ? "가방으로 옮겼습니다." : reason);
        }

        private void ClearControllerSelection()
        { _controllerBagIndex = -1; _controllerSourceSlot = -1; _controllerPlacement = -1; _controllerRotation = 0; }
    }
}
