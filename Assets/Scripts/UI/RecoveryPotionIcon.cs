#nullable enable
using ARPG.Tables;
using UnityEngine;
using UnityEngine.UIElements;

namespace ARPG.UI
{
    public static class RecoveryPotionIcon
    {
        public static void Draw(VisualElement host, ItemTable? table)
        {
            host.Clear();
            if (table == null || table.ItemType != GlobalEnum.ItemType.Consumable) return;
            Color liquid = table.RestoreHpPercent > 0 ? new Color(0.85f, 0.2f, 0.24f) : new Color(0.2f, 0.48f, 0.95f);
            VisualElement bottle = new() { pickingMode = PickingMode.Ignore };
            bottle.style.position = Position.Absolute; bottle.style.width = 24; bottle.style.height = 28;
            bottle.style.left = Length.Percent(50); bottle.style.top = Length.Percent(50);
            bottle.style.marginLeft = -12; bottle.style.marginTop = -10;
            bottle.style.backgroundColor = liquid;
            bottle.style.borderTopLeftRadius = 6; bottle.style.borderTopRightRadius = 6;
            bottle.style.borderBottomLeftRadius = 5; bottle.style.borderBottomRightRadius = 5;
            VisualElement neck = new() { pickingMode = PickingMode.Ignore };
            neck.style.position = Position.Absolute; neck.style.width = 10; neck.style.height = 9;
            neck.style.left = 7; neck.style.top = -7; neck.style.backgroundColor = new Color(0.8f, 0.71f, 0.5f);
            bottle.Add(neck); host.Add(bottle);
        }
    }
}
