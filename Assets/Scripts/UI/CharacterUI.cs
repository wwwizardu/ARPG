#nullable enable
using System;
using ARPG.Base;
using ARPG.Message;
using ARPG.Skill.Combat;
using ARPG.Tables;
using ARPG.Utility;
using TMPro;
using UnityEngine;

namespace ARPG.UI
{
    public class CharacterUI : UIBase
    {
        [SerializeField] private TextMeshProUGUI[] _textStat;
        [SerializeField] private SlotUI_Equip[] _slots;

        private Data.ItemData?[]? _equippedItems = null;

        public void Initialize(Action<SlotUI, UnityEngine.EventSystems.PointerEventData> OnClickSlot)
        {
            base.Initialize("UI/CharacterUI", false);

            if (_slots == null || _slots.Length == 0)
            {
                Debug.LogError("[CharacterUI] Equip slots not assigned");
                return;
            }

            _equippedItems = AR.s.Data.Player._inventoryEquip;

            for (int i = 0; i < _slots.Length; i++)
            {
                _slots[i].Initialize(i, OnClickSlot);
            }

            // 스탯 재계산 완료 메시지 구독
            AR.s.Message.Subscribe<StatRecalculatedMessage>(OnStatRecalculated);

            UpdateCharacterStat();
        }
        
        public void OnLoadCompleted()
        {
            if (_slots == null || _equippedItems == null)
            {
                Debug.LogError("[UIInventory] OnLoadCompleted - _slotUIs or _items is null");
                return;
            }

            if(_slots.Length != _equippedItems.Length)
            {
                Debug.LogError("[UICharacter] OnLoadCompleted - _slots length does not match _equippedItems length");
                return;
            }

            for (int i = 0; i < _slots.Length; i++)
            {
                if (_slots[i] != null && _equippedItems[i] != null)
                {
                    _slots[i]!.SetItem(_equippedItems[i]!);
                }
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

            // 장비 아이템 데이터에 세팅
            if (AR.s.Data.Player.EquipItem(inEquipType, inItem, out replacedItem) == false)
                return false;

            int playerEntityId = AR.s.Data.Player.PlayerId;

            // 교체되는 아이템이 있으면 기존 modifier 제거
            if (replacedItem != null)
            {
                EquipHelper.RemoveEquipmentModifiers(playerEntityId, replacedItem.ItemInstanceId);
            }

            // 새 장비의 modifier 적용
            EquipHelper.ApplyEquipmentModifiers(playerEntityId, inItem);

            // 새로운 아이템을 슬롯에 장착
            _slots[(int)inEquipType].SetItem(inItem);

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

            // 해제된 장비의 modifier 제거
            int playerEntityId = AR.s.Data.Player.PlayerId;
            if (unequippedItem != null)
            {
                EquipHelper.RemoveEquipmentModifiers(playerEntityId, unequippedItem.ItemInstanceId);
            }

            // 슬롯을 초기화하여 아이템 해제
            _slots[(int)inEquipType].Reset();

            return true;
        }

        protected override void OnDestroy()
        {
            base.OnDestroy();

            if (AR.s != null && AR.s.Message != null)
            {
                AR.s.Message.Unsubscribe<StatRecalculatedMessage>(OnStatRecalculated);
            }
        }

        private void OnStatRecalculated(StatRecalculatedMessage msg)
        {
            UpdateCharacterStat();
        }

        private void UpdateCharacterStat()
        {
            if(AR.s.Component.TryGetComponent<Component.StatComponent>(AR.s.Data.Player.PlayerId, out var _statComponent) == false)
            {
                Debug.LogError("[CharacterUI] UpdateCharacterStat - StatComponent not found");
                return;
            }

            _textStat[0].text = $"힘 {_statComponent.FinalStr}";
            _textStat[1].text = $"민첩 {_statComponent.FinalDex}";
            _textStat[2].text = $"지능 {_statComponent.FinalInt}";
            _textStat[3].text = $"체력 {_statComponent.CurrentHp}/{_statComponent.FinalMaxHp}";
            _textStat[4].text = $"마나 {_statComponent.CurrentMp}/{_statComponent.FinalMaxMp}";
            _textStat[5].text = $"체력 재생 {_statComponent.FinalHpGeneration}";
            _textStat[6].text = $"마나 재생 {_statComponent.FinalMpGeneration}";

            var est = DamageCalculator.Calculate(_statComponent);
            _textStat[7].text = $"물리 데미지 {est.PhysMin} - {est.PhysMax}";
            _textStat[8].text = $"화염 데미지 {est.FireMin} - {est.FireMax}";
            _textStat[9].text = $"냉기 데미지 {est.IceMin} - {est.IceMax}";
            _textStat[10].text = $"번개 데미지 {est.LightningMin} - {est.LightningMax}";
            _textStat[11].text = $"독 데미지 {est.PoisonMin} - {est.PoisonMax}";

            _textStat[12].text = $"치명타 확률 {_statComponent.FinalCriRate}%";
            _textStat[13].text = $"치명타 피해 {_statComponent.FinalCriDamage}%";
            _textStat[14].text = $"이동 속도 {_statComponent.FinalMoveSpeed}";
            _textStat[15].text = $"공격 속도 {_statComponent.FinalAttackSpeed}";
            _textStat[16].text = $"시전 속도 {_statComponent.FinalCastSpeed}";
            _textStat[17].text = $"방어력 {_statComponent.FinalDefense}";
            _textStat[18].text = $"화염 저항 {_statComponent.FinalFireResist}";
            _textStat[19].text = $"냉기 저항 {_statComponent.FinalIceResist}";
            _textStat[20].text = $"번개 저항 {_statComponent.FinalLightningResist}";
            _textStat[21].text = $"독 저항 {_statComponent.FinalPoisonResist}";
            _textStat[22].text = $"행운 {_statComponent.FinalLuck}";
        }
    }
}


