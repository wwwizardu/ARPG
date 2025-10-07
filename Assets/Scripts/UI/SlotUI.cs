#nullable enable
using ARPG.Data;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

using UnityEngine.EventSystems;
using ARPG.Base;

namespace ARPG.UI
{
    public class SlotUI : UIBase, IPointerEnterHandler, IPointerExitHandler
    {
        public enum UISlotType
        {
            None,
            Item,
            Skill,
            Equipment
        }

        [SerializeField] protected UISlotType _slotType = UISlotType.None;
        [SerializeField] protected Image _BG;
        [SerializeField] protected Image _Icon;
        [SerializeField] protected TextMeshProUGUI _TextQuantity;

        protected int _slotIndex = -1;

        protected Data.ItemData? _itemData = null;

        public int SlotIndex { get { return _slotIndex; } }
        public UISlotType SlotType { get { return _slotType; } }

        public virtual void Initialize(int inSlotIndex)
        {
            base.Initialize($"Slot_{inSlotIndex}", false);

            Reset();

            _slotIndex = inSlotIndex;
        }

        public virtual void Reset()
        {
            _itemData = null;

            _BG.gameObject.SetActive(true);
            _Icon.gameObject.SetActive(false);
            _TextQuantity.gameObject.SetActive(false);
        }

        public Data.ItemData? GetItem()
        {
            return _itemData;
        }

        public virtual void SetItem(ItemData inItem)
        {
            _itemData = inItem;

            Refresh();
        }

        // 마우스가 UI 요소 위로 들어올 때 (툴팁 활성화)
        public void OnPointerEnter(PointerEventData eventData)
        {
            if (_itemData?.Table == null)
                return;

            if (_itemData.Table.ItemType == GlobalEnum.ItemType.Equipment)
            {
                AR.s.UI.ShowTooltip_Equipment(_itemData, _rectTransform);
            }
        }

        // 마우스가 UI 요소 밖으로 나갈 때 (툴팁 비활성화)
        public void OnPointerExit(PointerEventData eventData)
        {
            //OnExit(eventData);
            AR.s.UI.HideTooltip();
        }

        public virtual void Refresh()
        {
            if (_itemData == null)
            {
                Reset();
                return;
            }

            _BG.gameObject.SetActive(true);
            _Icon.gameObject.SetActive(true);

            if (_itemData.Table == null)
            {
                Debug.LogError($"[SlotUI] Refresh - itemTable is null, itemId({_itemData.Id})");
                _Icon.sprite = null;
                return;
            }

            _Icon.sprite = AR.s.Data.GetSprite(_itemData.Table.SpriteName);

            if (1 <= _itemData.Quantity)
            {
                _TextQuantity.gameObject.SetActive(true);
                _TextQuantity.text = _itemData.Quantity.ToString();
            }
            else
            {
                _TextQuantity.gameObject.SetActive(false);
            }
        }

        protected virtual void OnEnter(PointerEventData eventData)
        {
            Debug.Log($"[SlotUI] OnEnter - SlotIndex({_slotIndex}), ItemId({(_itemData != null ? _itemData.Id.ToString() : "null")})");
        }

        protected virtual void OnExit(PointerEventData eventData)
        {
            Debug.Log($"[SlotUI] OnExit - SlotIndex({_slotIndex}), ItemId({(_itemData != null ? _itemData.Id.ToString() : "null")})");
        }
    }
}


