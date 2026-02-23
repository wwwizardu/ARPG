#nullable enable
using UnityEngine.UI;
using UnityEngine;
using ARPG.Data;
using UnityEngine.EventSystems;
using System;

namespace ARPG.UI
{
    public class SlotUI_Equip : SlotUI
    {
        [SerializeField] private GlobalEnum.EquipSlotType _equipSlotType = GlobalEnum.EquipSlotType.Max;
        [SerializeField] Image _BG_EquipType;

        public GlobalEnum.EquipSlotType EquipSlotType { get { return _equipSlotType; } }


        public override void Initialize(int inSlotIndex, Action<SlotUI, PointerEventData> onClick)
        {
            base.Initialize(inSlotIndex, onClick);
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

            if (itemTable.Equipment.EquipType != Utility.EquipHelper.SlotToEquipmentType(_equipSlotType))
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

        protected override void OnEnter(UnityEngine.EventSystems.PointerEventData eventData)
        {
            if (_itemData != null)
            {
                AR.s.UI.ShowTooltip_Equipment(_itemData, _rectTransform);
            }
        }

        protected override void OnExit(UnityEngine.EventSystems.PointerEventData eventData)
        {
            AR.s.UI.HideTooltip();
        }

        protected override void OnClick(PointerEventData eventData)
        {
            _onClick?.Invoke(this, eventData);
        }
    }
}


