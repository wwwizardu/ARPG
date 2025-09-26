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

        private int _slotIndex = -1;


        public int SlotIndex { get { return _slotIndex; } }
        public UISlotType SlotType { get { return _slotType; } }

        public virtual void Initialize(int inSlotIndex)
        {
            Reset();

            _slotIndex = inSlotIndex;
        }

        public virtual void Reset()
        {
            _BG.gameObject.SetActive(true);
            _Icon.gameObject.SetActive(false);
            _TextQuantity.gameObject.SetActive(false);
        }
    }
}


