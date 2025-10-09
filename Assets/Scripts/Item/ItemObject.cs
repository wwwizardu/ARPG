#nullable enable
using ARPG.Data;
using ARPG.Tables;
using TMPro;
using UnityEngine;

namespace ARPG.Item
{
    public class ItemObject : MonoBehaviour
    {
        [SerializeField] private GameObject _visual;
        [SerializeField] private TextMeshPro _text;
        [SerializeField] private SpriteRenderer _sr;
        private ItemData _itemData = null!;
        private ItemTable _table = null!;

        public ItemData ItemData { get { return _itemData; } }

        public void Initialize()
        {

        }

        public void Reset()
        {

        }

        public bool SetItem(ItemTable inTable, int inId, int inInstanceId, EquipmentData? inEquipment, int inQuantity)
        {
            ItemData inItemData = new ItemData()
            {
                Table = inTable,
                Id = inId,
                ItemInstanceId = inInstanceId,
                Equipment = inEquipment,
                Quantity = inQuantity,
            };

            return SetItem(inItemData);
        }

        public bool SetItem(ItemData inItemData)
        {
            if (inItemData.Table == null)
            {
                Debug.LogError($"[Monster] DropItemObjectAsync - itemTable is null, itemId({inItemData.Id})");
                return false;
            }

            _table = inItemData.Table;
            _itemData = inItemData;

            Refresh();

            return true;
        }

        private void Refresh()
        {
            if (_table == null)
            {
                _visual.SetActive(false);
                return;
            }

            Sprite itemSprite = AR.s.Data.GetSprite(_table.SpriteName);
            if (itemSprite == null)
            {
                Debug.LogError($"[Monster] DropItemObjectAsync - itemSprite is null, SpriteName({_table.SpriteName})");
                return;
            }

            _visual.SetActive(true);
            _sr.sprite = itemSprite;
            _text.text = _table.Name;
        }
    }

}

