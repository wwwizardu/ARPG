using UnityEngine.UI;
using UnityEngine;
using ARPG.Data;

namespace ARPG.UI
{
    public class SlotUI_Equip : SlotUI
    {
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

        public void EquipItem(ItemData inItem)
        {

        }

        public void UnEquipItem()
        {

        }
    }
}


