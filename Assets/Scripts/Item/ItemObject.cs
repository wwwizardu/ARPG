using ARPG.Data;
using ARPG.Tables;
using UnityEngine;

namespace ARPG.Item
{
    public class ItemObject : MonoBehaviour
    {
        private ItemData _itemData;
        private ItemTable _table;

        public void Initialize()
        {

        }

        public void Reset()
        {

        }

        public virtual bool Pickup()
        {
            Debug.Log("[ItemObject] Pickup()");
            return false;
        }

    }

}

