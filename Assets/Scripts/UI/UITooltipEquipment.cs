using System.Collections.Generic;
using ARPG.Data;
using ARPG.UI;
using UnityEngine;

namespace ARPG.UI
{
    public class UITooltipEquipment : UITooltip
    {
        [SerializeField] private TMPro.TextMeshProUGUI _textQuality;
        [SerializeField] private TooltipEquipmentWeapon _weapon;
        [SerializeField] private TooltipEquipmentArmor _armor;

        [SerializeField] private TMPro.TextMeshProUGUI _textRequirement;

        [SerializeField] private List<TMPro.TextMeshProUGUI> _textStat;

        public void SetEquipmentData(ItemData inItemData)
        {
            _textName.text = inItemData.Table.Name;

            if (inItemData.Equipment?.StatData == null)
                return;

            SetEquipment(inItemData);

            int statIndex = 0;

            // Prefix 옵션들을 _textStat에 추가
            foreach (var stat in inItemData.Equipment.StatData.Prefix)
            {
                if (statIndex >= _textStat.Count)
                    break;

                _textStat[statIndex].text = $"{stat.Type}: +{stat.Value}";
                _textStat[statIndex].gameObject.SetActive(true);
                statIndex++;
            }

            // Postfix 옵션들을 _textStat에 추가
            foreach (var stat in inItemData.Equipment.StatData.Postfix)
            {
                if (statIndex >= _textStat.Count)
                    break;

                _textStat[statIndex].text = $"{stat.Type}: +{stat.Value}";
                _textStat[statIndex].gameObject.SetActive(true);
                statIndex++;
            }

            // 남은 텍스트는 비활성화
            for (int i = statIndex; i < _textStat.Count; i++)
            {
                _textStat[i].gameObject.SetActive(false);
            }
        }

        private void SetEquipment(ItemData inItemData)
        {
            if (inItemData.Equipment.Table.EquipType == GlobalEnum.EquipSlotType.WeaponLeft ||
            inItemData.Equipment.Table.EquipType == GlobalEnum.EquipSlotType.WeaponRight)
            {
                _weapon.Show(true, inItemData);
                _armor.Show(false, null);
            }
            else
            {
                _weapon.Show(false, null);
                _armor.Show(true, inItemData);
            }
        }
    }
}

