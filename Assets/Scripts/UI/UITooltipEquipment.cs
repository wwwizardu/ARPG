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
        }
    }
}

