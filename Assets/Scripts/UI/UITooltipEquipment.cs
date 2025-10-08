using System.Collections.Generic;
using ARPG.Data;
using ARPG.UI;
using UnityEngine;

namespace ARPG.UI
{
    public class UITooltipEquipment : UITooltip
    {
        [SerializeField] private List<TMPro.TextMeshProUGUI> _textStat;

        public void SetEquipmentData(ItemData inItemData)
        {
            _textName.text = inItemData.Table.Name;

            if (inItemData.Equipment == null)
                return;

            int statIndex = 0;

            // Prefix 옵션들을 _textStat에 추가
            foreach (var stat in inItemData.Equipment.Prefix)
            {
                if (statIndex >= _textStat.Count)
                    break;

                _textStat[statIndex].text = $"{stat.Type}: +{stat.Value}";
                _textStat[statIndex].gameObject.SetActive(true);
                statIndex++;
            }

            // Postfix 옵션들을 _textStat에 추가
            foreach (var stat in inItemData.Equipment.Postfix)
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
    }
}

