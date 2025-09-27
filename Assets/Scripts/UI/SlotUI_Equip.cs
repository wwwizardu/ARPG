#nullable enable
using UnityEngine.UI;
using UnityEngine;
using ARPG.Data;

namespace ARPG.UI
{
    public class SlotUI_Equip : SlotUI
    {
        [SerializeField] private GlobalEnum.EquipSlotType _equipSlotType = GlobalEnum.EquipSlotType.Max;
        [SerializeField] Image _BG_EquipType;



        public override void Initialize(int inSlotIndex)
        {
            base.Initialize(inSlotIndex);
        }

        public override void Reset()
        {
            base.Reset();

            _BG_EquipType.gameObject.SetActive(true);
        }

        public bool CanEquip(Data.ItemData inItem)
        {
            if (inItem == null)
                return false;

            Tables.ItemTable? itemTable = AR.s.Data.GetItem(inItem.Id);
            if (itemTable == null)
            {
                Debug.LogError($"[SlotUI_Equip] CanEquip - ItemTable not found for ID: {inItem.Id}");
                return false;
            }

            if (itemTable.Equipment.EquipType != _equipSlotType)
                return false;

            return true;
        }

        public bool HasItem()
        {
            return _itemData != null;
        }

        public override void SetItem(ItemData inItem)
        {
            base.SetItem(inItem);

        }

        public override void Refresh()
        {
            base.Refresh();

        }
    }
}


