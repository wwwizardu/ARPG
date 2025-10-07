using System.Collections.Generic;
using ARPG.Base;
using UnityEngine;

namespace ARPG.UI
{
    public class UITooltip : UIBaseForm
    {
        [SerializeField] protected RectTransform _tooltipRect;
        [SerializeField] protected TMPro.TextMeshProUGUI _textName;

        public RectTransform TooltipRect => _tooltipRect;

        public void SetPosition(Vector2 inPosition)
        {
            _tooltipRect.anchoredPosition = inPosition;
        }
    }
}


