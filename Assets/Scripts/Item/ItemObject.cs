#nullable enable
using ARPG.Base;
using ARPG.Component;
using ARPG.Data;
using ARPG.Tables;
using ARPG.Utility;
using TMPro;
using UnityEngine;

namespace ARPG.Item
{
    public class ItemObject : EntityBase
    {
        [SerializeField] private GameObject _visual;
        [SerializeField] private TextMeshPro _text;
        [SerializeField] private SpriteRenderer _sr;
        private ItemData _itemData = null!;
        private ItemTable _table = null!;
        
        public ItemData ItemData { get { return _itemData; } }
        
        public void Initialize(ItemData inItemData)
        {
            if (inItemData.Table == null)
            {
                Debug.LogError($"[Monster] DropItemObjectAsync - itemTable is null, itemId({inItemData.Id})");
                return;
            }

            _table = inItemData.Table;
            _itemData = inItemData;

            InitializeECSComponents();

            Refresh();
        }

        public override void InitializeECSComponents()
        {
            base.InitializeECSComponents();

            WorldItemComponent worldItem = new WorldItemComponent
            {
                ItemTableId = _itemData.Id,
                EntityId = _entityId,
                Quantity = _itemData.Quantity,
                DropTime = Time.time,
                ExpireTime = WorldItemHelper.DEFAULT_EXPIRE_TIME,
                AutoPickupEnabled = false,
                AutoPickupRange = 0f
            };
            AR.s.Component.AddComponent(_entityId, worldItem);

            Debug.Log($"[ItemObject] Entity created - EntityId: {_entityId}, ItemId: {_itemData.Id}, Qty: {_itemData.Quantity}");
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

