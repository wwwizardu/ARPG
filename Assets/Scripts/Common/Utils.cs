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

    public static GlobalEnum.EquipmentTypeMask CategoryToEquipmentTypeMask(GlobalEnum.ItemCategory inCategory)
    {
        switch (inCategory)
        {
            case GlobalEnum.ItemCategory.Helmet: return GlobalEnum.EquipmentTypeMask.Helmet;
            case GlobalEnum.ItemCategory.Armor: return GlobalEnum.EquipmentTypeMask.Armor;
            case GlobalEnum.ItemCategory.Gloves: return GlobalEnum.EquipmentTypeMask.Gloves;
            case GlobalEnum.ItemCategory.Boots: return GlobalEnum.EquipmentTypeMask.Boots;
            case GlobalEnum.ItemCategory.Belt: return GlobalEnum.EquipmentTypeMask.Belt;
            case GlobalEnum.ItemCategory.Necklace: return GlobalEnum.EquipmentTypeMask.Necklace;
            case GlobalEnum.ItemCategory.Ring: return GlobalEnum.EquipmentTypeMask.Ring;
            case GlobalEnum.ItemCategory.Earring: return GlobalEnum.EquipmentTypeMask.Earring;
            default: return GlobalEnum.EquipmentTypeMask.Weapon;
        }
    }

    public static bool IsValidEquipmentTypeMask(GlobalEnum.EquipmentTypeMask mask, bool allowNone = false)
    {
        if (mask == GlobalEnum.EquipmentTypeMask.None)
            return allowNone;

        int bits = (int)mask;
        int allBits = (int)GlobalEnum.EquipmentTypeMask.All;
        return (bits & ~allBits) == 0;
    }

    public static bool IsSingleEquipmentTypeMask(GlobalEnum.EquipmentTypeMask mask)
    {
        int bits = (int)mask;
        int allBits = (int)GlobalEnum.EquipmentTypeMask.All;

        if (bits == 0)
            return false;
        if ((bits & ~allBits) != 0)
            return false;

        return (bits & (bits - 1)) == 0;
    }

    public static bool ContainsEquipmentType(GlobalEnum.EquipmentTypeMask allowed, GlobalEnum.EquipmentTypeMask singleType)
    {
        if (IsSingleEquipmentTypeMask(singleType) == false)
            return false;

        return (allowed & singleType) != 0;
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
