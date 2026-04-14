#nullable enable
using UnityEngine;

public class TooltipEquipmentArmor : MonoBehaviour
{
    [SerializeField] public TMPro.TextMeshProUGUI [] TextArmor;

    public void Show(bool isShow, ARPG.Data.ItemData? inItemData)
    {
        if (isShow == false || inItemData?.Equipment == null)
        {
            for (int i = 0; i < TextArmor.Length; i++)
            {
                TextArmor[i].gameObject.SetActive(false);
            }
            return;
        }

        var mods = inItemData.Equipment.Mods;
        int textIndex = 0;

        for (int i = 0; i < mods.Count; i++)
        {
            var mod = mods[i];
            if (mod.Table == null)
                continue;

            if (mod.Slot != GlobalEnum.ModSlot.Implicit)
                continue;

            if (textIndex >= TextArmor.Length)
                break;

            string text = mod.Value2 > 0
                ? $"{mod.Table.Name}: {mod.Value1}~{mod.Value2}"
                : $"{mod.Table.Name}: {mod.Value1}";

            TextArmor[textIndex].text = text;
            TextArmor[textIndex].gameObject.SetActive(true);
            textIndex++;
        }

        for (int i = textIndex; i < TextArmor.Length; i++)
        {
            TextArmor[i].gameObject.SetActive(false);
        }
    }
}
