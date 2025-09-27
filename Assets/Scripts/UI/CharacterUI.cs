#nullable enable
using ARPG.Base;
using ARPG.Tables;
using UnityEngine;

namespace ARPG.UI
{
    public class CharacterUI : UIBase
    {
        [SerializeField] private SlotUI_Equip[] _slots;

        public override void Initialize(string inName, bool isForm = false)
        {
            base.Initialize(inName, isForm);

            if (_slots == null || _slots.Length == 0)
            {
                Debug.LogError("[CharacterUI] Equip slots not assigned");
                return;
            }

            for (int i = 0; i < _slots.Length; i++)
            {
                _slots[i].Initialize(i);
            }
        }

        // 지정된 장비 슬롯에 아이템을 장착하는 함수
        // inEquipType: 장착할 슬롯 타입 (무기, 방어구 등)
        // inItem: 장착할 아이템 데이터
        // replacedItem: 기존에 장착되어 있던 아이템 (있는 경우)
        // 반환값: 장착 성공 시 true, 실패 시 false
        public bool EquipItem(GlobalEnum.EquipSlotType inEquipType, Data.ItemData inItem, out Data.ItemData? replacedItem)
        {
            replacedItem = null;

            // 아이템이 null인 경우 장착 실패
            if (inItem == null)
                return false;

            // 해당 슬롯에 아이템을 장착할 수 있는지 확인
            if (_slots[(int)inEquipType].CanEquip(inItem) == false)
                return false;

            // 기존에 장착된 아이템이 있는 경우 교체할 아이템으로 설정
            if (_slots[(int)inEquipType].HasItem() == true)
            {
                replacedItem = _slots[(int)inEquipType].GetItem();
            }

            // 새로운 아이템을 슬롯에 장착
            _slots[(int)inEquipType].SetItem(inItem);
            return true;
        }
        
        // 지정된 슬롯 인덱스의 장비를 해제하는 함수
        // slotIndex: 해제할 슬롯의 인덱스
        // unequippedItem: 해제된 아이템 데이터
        // 반환값: 해제 성공 시 true, 실패 시 false
        public bool UnequipItem(int slotIndex, out Data.ItemData? unequippedItem)
        {
            unequippedItem = null;

            // 해당 슬롯에 장착된 아이템이 없는 경우 해제 실패
            if (_slots[slotIndex].HasItem() == false)
                return false;

            // 장착된 아이템을 가져와서 반환값으로 설정
            unequippedItem = _slots[slotIndex].GetItem();
            // 슬롯을 초기화하여 아이템 해제
            _slots[slotIndex].Reset();

            return true;
        }

    }
}


