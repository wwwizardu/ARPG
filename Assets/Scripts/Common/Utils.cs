using UnityEngine;

public static class Utils
{
    public static bool IsZero(this Vector2 inVector)
    {
        if (inVector.x < -0.01f || 0.01f < inVector.x)
            return false;
        if (inVector.y < -0.01f || 0.01f < inVector.y)
            return false;

        return true;
    }

    public static GlobalEnum.EquipmentType CategoryToEquipmentType(GlobalEnum.ItemCategory inCategory)
    {
        switch (inCategory)
        {
            case GlobalEnum.ItemCategory.Helmet: return GlobalEnum.EquipmentType.Helmet;
            case GlobalEnum.ItemCategory.Armor: return GlobalEnum.EquipmentType.Armor;
            case GlobalEnum.ItemCategory.Gloves: return GlobalEnum.EquipmentType.Gloves;
            case GlobalEnum.ItemCategory.Boots: return GlobalEnum.EquipmentType.Boots;
            default: return GlobalEnum.EquipmentType.Weapon;
        }
    }

    public static bool IsApparel(this GlobalEnum.ItemCategory inCategory)
    {
        switch (inCategory)
        {
            case GlobalEnum.ItemCategory.Helmet:
            case GlobalEnum.ItemCategory.Armor:
            case GlobalEnum.ItemCategory.Gloves:
            case GlobalEnum.ItemCategory.Boots:
            case GlobalEnum.ItemCategory.Shield:
                return true;
            default:
                return false;
        }
    }
}
