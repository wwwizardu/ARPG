#nullable enable
using System;
using ARPG.Base;
using ARPG.Tables;
using TMPro;
using UnityEngine;

namespace ARPG.UI
{
    public class CharacterUI : UIBase
    {
        [SerializeField] private TextMeshProUGUI[] _textStat;
        [SerializeField] private SlotUI_Equip[] _slots;

        public void Initialize(Action<SlotUI, UnityEngine.EventSystems.PointerEventData> OnClickSlot)
        {
            base.Initialize("UI/CharacterUI", false);

            if (_slots == null || _slots.Length == 0)
            {
                Debug.LogError("[CharacterUI] Equip slots not assigned");
                return;
            }

            for (int i = 0; i < _slots.Length; i++)
            {
                _slots[i].Initialize(i, OnClickSlot);
            }

            UpdateCharacterStat();
        }

        public void OnLoadCompleted()
        {

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

            // 장비 아이템 데이터에 세팅
            if (AR.s.Data.Player.EquipItem(inEquipType, inItem, out replacedItem) == false)
                return false;

            // 새로운 아이템을 슬롯에 장착
            _slots[(int)inEquipType].SetItem(inItem);

            // 캐릭터 스탯 업데이트
            UpdateCharacterStat();
            
            return true;
        }

        // 지정된 슬롯 인덱스의 장비를 해제하는 함수
        // slotIndex: 해제할 슬롯의 인덱스
        // unequippedItem: 해제된 아이템 데이터
        // 반환값: 해제 성공 시 true, 실패 시 false
        public bool UnequipItem(GlobalEnum.EquipSlotType inEquipType, out Data.ItemData? unequippedItem)
        {
            unequippedItem = null;

            // 장비 아이템 데이터에 세팅
            if (AR.s.Data.Player.UnequipItem(inEquipType, out unequippedItem) == false)
                return false;

            // 슬롯을 초기화하여 아이템 해제
            _slots[(int)inEquipType].Reset();

            // 캐릭터 스탯 업데이트
            UpdateCharacterStat();

            return true;
        }

        private void UpdateCharacterStat()
        {
            AR.s.MyPlayer?.Stat.UpdateEquipmentStat();

            ARPG.Creature.Stats? stat = AR.s.MyPlayer?.Stat.GetStats();
            if (stat == null)
                return;

            _textStat[(int)GlobalEnum.Stat.Str].text = $"힘 {stat.Str}";
            _textStat[(int)GlobalEnum.Stat.Dex].text = $"민첩 {stat.Dex}";
            _textStat[(int)GlobalEnum.Stat.Int].text = $"지능 {stat.Int}";
            _textStat[(int)GlobalEnum.Stat.Hp].text = $"체력 {stat.CurrentHp}/{stat.MaxHp}";
            _textStat[(int)GlobalEnum.Stat.Mp].text = $"마나 {stat.CurrentMp}/{stat.MaxMp}";
            _textStat[(int)GlobalEnum.Stat.HpGeneration].text = $"체력 재생 {stat.HpGeneration}";
            _textStat[(int)GlobalEnum.Stat.MpGeneration].text = $"마나 재생 {stat.MpGeneration}";
            _textStat[(int)GlobalEnum.Stat.AttackMin].text = $"공격력 {stat.AttackMin} - {stat.AttackMax}";
            _textStat[(int)GlobalEnum.Stat.AttackMax].gameObject.SetActive(false);
            _textStat[(int)GlobalEnum.Stat.CriRate].text = $"치명타 확률 {stat.CriRate}%";
            _textStat[(int)GlobalEnum.Stat.CriDamage].text = $"치명타 피해 {stat.CriDamage}%";
            _textStat[(int)GlobalEnum.Stat.MoveSpeed].text = $"이동 속도 {stat.MoveSpeed}";
            _textStat[(int)GlobalEnum.Stat.AttackSpeed].text = $"공격 속도 {stat.AttackSpeed}";
            _textStat[(int)GlobalEnum.Stat.CastSpeed].text = $"시전 속도 {stat.CastSpeed}";
            _textStat[(int)GlobalEnum.Stat.Hp].text = $"방어력 {stat.Defense}";
            _textStat[(int)GlobalEnum.Stat.Hp].text = $"화염 저항 {stat.FireResist}";
            _textStat[(int)GlobalEnum.Stat.Hp].text = $"냉기 저항 {stat.IceResist}";
            _textStat[(int)GlobalEnum.Stat.Hp].text = $"번개 저항 {stat.LightningResist}";
            _textStat[(int)GlobalEnum.Stat.Hp].text = $"독 저항 {stat.PoisonResist}";
            _textStat[(int)GlobalEnum.Stat.Hp].text = $"행운 {stat.Luck}";
        }
    }
}


