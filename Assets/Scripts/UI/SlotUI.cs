#nullable enable
using ARPG.Data;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ARPG.UI
{
    public class SlotUI : MonoBehaviour
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

        public virtual void Refresh()
        {
            if (_itemData == null)
            {
                Reset();
                return;
            }

            _BG.gameObject.SetActive(false);
            _Icon.gameObject.SetActive(true);
            // _Icon.sprite = AR.s.Data.GetItemIcon(_itemData.Id);

            if (_itemData.Quantity > 1)
            {
                _TextQuantity.gameObject.SetActive(true);
                _TextQuantity.text = _itemData.Quantity.ToString();
            }
            else
            {
                _TextQuantity.gameObject.SetActive(false);
            }
        }
    }
}


