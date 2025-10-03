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
        private ItemData _itemData;
        private ItemTable _table;

        public void Initialize()
        {

        }

        public void Reset()
        {

        }

        public void SetItem(ItemTable inItem)
        {
            _table = inItem;

            Refresh();
        }

        public virtual bool Pickup()
        {
            Debug.Log("[ItemObject] Pickup()");
            return false;
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

