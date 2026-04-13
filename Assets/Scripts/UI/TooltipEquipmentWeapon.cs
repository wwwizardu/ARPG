#nullable enable
using UnityEngine;

public class TooltipEquipmentWeapon : MonoBehaviour
{
    [SerializeField] public TMPro.TextMeshProUGUI TextDamage;
    [SerializeField] public TMPro.TextMeshProUGUI TextDamageFire;
    [SerializeField] public TMPro.TextMeshProUGUI TextDamageIce;
    [SerializeField] public TMPro.TextMeshProUGUI TextDamageLightning;
    [SerializeField] public TMPro.TextMeshProUGUI TextDamagePosition;
    [SerializeField] public TMPro.TextMeshProUGUI TexttCriticalRate;
    [SerializeField] public TMPro.TextMeshProUGUI TexttAttackSpeed;

    public void Show(bool isShow, ARPG.Data.ItemData? inItemData)
    {
        if (isShow == false)
        {
            TextDamage.gameObject.SetActive(isShow);
            TextDamageFire.gameObject.SetActive(isShow);
            TextDamageIce.gameObject.SetActive(isShow);
            TextDamageLightning.gameObject.SetActive(isShow);
            TextDamagePosition.gameObject.SetActive(isShow);
            TexttCriticalRate.gameObject.SetActive(isShow);
            TexttAttackSpeed.gameObject.SetActive(isShow);
        }
        else
        {
            if(inItemData != null)
            {
                TexttCriticalRate.gameObject.SetActive(inItemData?.Equipment != null);
                TexttAttackSpeed.gameObject.SetActive(inItemData?.Equipment != null);
                    
                if (inItemData?.Equipment == null)
                {
                    TextDamage.gameObject.SetActive(false);
                    TextDamageFire.gameObject.SetActive(false);
                    TextDamageIce.gameObject.SetActive(false);
                    TextDamageLightning.gameObject.SetActive(false);
                    TextDamagePosition.gameObject.SetActive(false);
                }
                else
                {
                    bool isPhysicsDamage = inItemData.Equipment.IsPhysicsDamage();
                    TextDamage.gameObject.SetActive(isPhysicsDamage);

                    if (isPhysicsDamage == true)
                    {
                        var physicsDamage = inItemData.Equipment.GetPhysicsDamage();
                        TextDamage.text = $"물리 피해: {physicsDamage.Item1}~{physicsDamage.Item2}";
                    }

                    TextDamageFire.gameObject.SetActive(false);
                    TextDamageIce.gameObject.SetActive(false);
                    TextDamageLightning.gameObject.SetActive(false);
                    TextDamagePosition.gameObject.SetActive(false);

                    TexttCriticalRate.text = $"치명타 확률: {inItemData.Equipment.GetCriticalRate()}%"; 
                    TexttAttackSpeed.text = $"초당 공격 횟수: {inItemData.Equipment.GetAttackSpeed()}";
                }
            }
        }
    }
}
