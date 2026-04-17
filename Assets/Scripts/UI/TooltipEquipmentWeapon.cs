#nullable enable
using ARPG.Data;
using UnityEngine;

public class TooltipEquipmentWeapon : MonoBehaviour
{
    [SerializeField] public TMPro.TextMeshProUGUI TextDamage;
    [SerializeField] public TMPro.TextMeshProUGUI TextDamageFire;
    [SerializeField] public TMPro.TextMeshProUGUI TextDamageIce;
    [SerializeField] public TMPro.TextMeshProUGUI TextDamageLightning;
    [SerializeField] public TMPro.TextMeshProUGUI TextDamagePosition;  // 독(Poison) 데미지 텍스트
    [SerializeField] public TMPro.TextMeshProUGUI TexttCriticalRate;
    [SerializeField] public TMPro.TextMeshProUGUI TexttAttackSpeed;

    public void Show(bool isShow, ItemData? inItemData)
    {
        if (isShow == false || inItemData == null || inItemData.Equipment == null)
        {
            HideAll();
            return;
        }

        // EquipmentData.WeaponStats 캐시에서 Local 파이프라인 완료된 최종 스탯 조회
        WeaponStatCache stats = inItemData.Equipment.WeaponStats;

        UpdateDamageText(TextDamage, "물리 피해", stats.Physics);
        UpdateDamageText(TextDamageFire, "화염 피해", stats.Fire);
        UpdateDamageText(TextDamageIce, "냉기 피해", stats.Ice);
        UpdateDamageText(TextDamageLightning, "번개 피해", stats.Lightning);
        UpdateDamageText(TextDamagePosition, "독 피해", stats.Poison);

        bool hasCrit = stats.CriRate > 0;
        TexttCriticalRate.gameObject.SetActive(hasCrit);
        if (hasCrit)
            TexttCriticalRate.text = $"치명타 확률: {stats.CriRate}%";

        bool hasAttackSpeed = stats.AttackSpeed > 0f;
        TexttAttackSpeed.gameObject.SetActive(hasAttackSpeed);
        if (hasAttackSpeed)
            TexttAttackSpeed.text = $"초당 공격 횟수: {stats.AttackSpeed:F2}";
    }

    /// <summary>
    /// 데미지 범위가 있으면 표시, 없으면 숨김
    /// </summary>
    private void UpdateDamageText(TMPro.TextMeshProUGUI target, string label, DamageRange range)
    {
        bool hasDamage = range.Max > 0;
        target.gameObject.SetActive(hasDamage);
        if (hasDamage)
            target.text = $"{label}: {range.Min}~{range.Max}";
    }

    private void HideAll()
    {
        TextDamage.gameObject.SetActive(false);
        TextDamageFire.gameObject.SetActive(false);
        TextDamageIce.gameObject.SetActive(false);
        TextDamageLightning.gameObject.SetActive(false);
        TextDamagePosition.gameObject.SetActive(false);
        TexttCriticalRate.gameObject.SetActive(false);
        TexttAttackSpeed.gameObject.SetActive(false);
    }
}
