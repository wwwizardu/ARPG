#nullable enable
using ARPG.Data;
using UnityEngine.UIElements;

namespace ARPG.UI
{
    public partial class UISkillBook
    {
        private ItemData? _controllerSelectedBook;
        private int _bindingDisplayRevision = -1;
        public VisualElement? ControllerFocusRoot => _pageModalBackdrop;

        public override bool UpdateInput(Input.ArpgInput input)
        {
            if (_pageModalBackdrop != null && input.UI.Cancel.WasReleasedThisFrame() == true)
            { ClosePageEditor(); return true; }
            if (_bindingDisplayRevision != input.DisplayRevision)
            { _bindingDisplayRevision = input.DisplayRevision; RefreshSkillSlots(); }
            return base.UpdateInput(input);
        }
        public bool ControllerActivate(VisualElement element, bool secondary)
        {
            if (element.ClassListContains("skillbook-inv-item") == true && element.userData is ItemData book)
            {
                _controllerSelectedBook = book; SetStatus("장착할 스킬 슬롯을 선택하세요."); return true;
            }
            if (element.name.StartsWith("slot-", System.StringComparison.Ordinal) == false
                || int.TryParse(element.name.Substring(5), out int slot) == false || slot < 0 || slot >= 10) return false;
            if (secondary == true) { OpenPageEditor(slot); return true; }
            if (_controllerSelectedBook != null)
            {
                int index = AR.s.Player.Inventory.Items.IndexOf(_controllerSelectedBook);
                if (index >= 0 && AR.s.PlayerSkill.EquipSkillBook(slot, index) == true) _controllerSelectedBook = null;
                else SetStatus("스킬북을 장착할 수 없습니다.");
            }
            else OnClickSkillSlot(slot);
            return true;
        }
    }
}
