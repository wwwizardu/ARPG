#nullable enable
using UnityEngine;

public class TooltipEquipmentArmor : MonoBehaviour
{
    [SerializeField] public TMPro.TextMeshProUGUI TextArmor;

    public void Show(bool isShow, ARPG.Data.ItemData? inItemData)
    {
        TextArmor.gameObject.SetActive(isShow);


    }
}
