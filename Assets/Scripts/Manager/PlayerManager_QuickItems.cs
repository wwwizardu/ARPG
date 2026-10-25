#nullable enable
using ARPG.Data;

namespace ARPG.Manager
{
    public partial class PlayerManager
    {
        public bool RegisterQuickItem(int slot, int itemId)
        {
            if (slot < 0 || slot >= 4 || _playerData == null) return false;
            if (itemId != 0)
            {
                var table = AR.s.Data.GetItem(itemId);
                if (table == null || table.ItemType != GlobalEnum.ItemType.Consumable
                    || (table.RestoreHpPercent <= 0 && table.RestoreMpPercent <= 0)
                    || _inventory.GetTotalQuantity(itemId) <= 0) return false;
            }
            AR.s.Data.MarkPlayerSaveDirty(_playerData, PlayerSaveGroup.Inventory);
            _playerData._quickItemIds[slot] = itemId;
            return true;
        }
    }
}
