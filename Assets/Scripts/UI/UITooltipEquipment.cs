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

            if (inItemData.Equipment == null)
                return;

            SetEquipment(inItemData);

            int statIndex = 0;
            var mods = inItemData.Equipment.Mods;

            // 1) Implicit slot 먼저 출력 — 유니크/특수 옵션. 골드 컬러로 차별 표시.
            for (int i = 0; i < mods.Count; i++)
            {
                var mod = mods[i];
                if (mod.Table == null)
                    continue;
                if (mod.Slot != GlobalEnum.ModSlot.Implicit)
                    continue;
                if (statIndex >= _textStat.Count)
                    break;

                string body = mod.Value2 > 0
                    ? $"{mod.Table.Name}: {mod.Value1}~{mod.Value2}"
                    : $"{mod.Table.Name}: {mod.Value1}";

                _textStat[statIndex].text = $"<color=#FFD700>{body}</color>";
                _textStat[statIndex].gameObject.SetActive(true);
                statIndex++;
            }

            // 2) Prefix/Postfix slot 출력 — 랜덤 옵션. 일반색.
            // Base slot은 무기/방어구 베이스 스탯 패널에서 처리하므로 여기선 skip.
            for (int i = 0; i < mods.Count; i++)
            {
                var mod = mods[i];
                if (mod.Table == null)
                    continue;
                if (mod.Slot != GlobalEnum.ModSlot.Prefix && mod.Slot != GlobalEnum.ModSlot.Postfix)
                    continue;
                if (statIndex >= _textStat.Count)
                    break;

                string text = mod.Value2 > 0
                    ? $"{mod.Table.Name}: +{mod.Value1}~{mod.Value2}"
                    : $"{mod.Table.Name}: +{mod.Value1}";

                _textStat[statIndex].text = text;
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
            if (inItemData.Equipment.EquipType == GlobalEnum.EquipmentTypeMask.Weapon)
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

