#nullable enable
using ARPG.Data;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

using UnityEngine.EventSystems;
using ARPG.Base;
using System;

namespace ARPG.UI
{
    public class SlotUI : UIBase, IPointerEnterHandler, IPointerExitHandler, IPointerClickHandler
    {
        public enum UISlotType
        {
            None,
            Inventory,
            Skill,
            Equipment
        }

        [SerializeField] protected UISlotType _slotType = UISlotType.None;
        [SerializeField] protected Image _BG;
        [SerializeField] protected Image _Icon;
        [SerializeField] protected TextMeshProUGUI _TextQuantity;

        protected int _slotIndex = -1;

        protected Data.ItemData? _itemData = null;

        protected Action<SlotUI, PointerEventData>? _onClick = null;

        public int SlotIndex { get { return _slotIndex; } }
        public UISlotType SlotType { get { return _slotType; } }

        public virtual void Initialize(int inSlotIndex, Action<SlotUI, PointerEventData> onClick)
        {
            base.Initialize($"Slot_{inSlotIndex}", false);

            Reset();

            _slotIndex = inSlotIndex;
            _onClick = onClick;
        }

        public virtual void Reset()
        {
            _itemData = null;

            _BG.gameObject.SetActive(true);
            _Icon.gameObject.SetActive(false);
            _TextQuantity.gameObject.SetActive(false);
        }

        public Data.ItemData? GetItem()
        {
            return _itemData;
        }

        public virtual void SetItem(ItemData inItem)
        {
            _itemData = inItem;

            Refresh();
        }

        // 마우스가 UI 요소 위로 들어올 때 (툴팁 활성화)
        public void OnPointerEnter(PointerEventData eventData)
        {
            if (_itemData?.Table == null)
                return;

            if (_itemData.Table.ItemType == GlobalEnum.ItemType.Equipment)
            {
                AR.s.UI.ShowTooltip_Equipment(_itemData, _rectTransform);
            }
            else if (_itemData.Table.ItemType == GlobalEnum.ItemType.SkillBook
                  || _itemData.Table.ItemType == GlobalEnum.ItemType.SkillPage)
            {
                AR.s.Tooltip.Show(_itemData, GetSlotScreenRect(_rectTransform));
            }
        }

        /// <summary>
        /// uGUI RectTransform → Screen Space Rect (Y up, 좌하단 원점). 글로벌 툴팁 anchor용.
        /// Screen Space - Overlay 캔버스에서는 world == screen이라 변환 불필요,
        /// Screen Space - Camera는 UI 카메라로 WorldToScreenPoint 변환.
        /// </summary>
        protected static Rect GetSlotScreenRect(RectTransform rt)
        {
            if (rt == null) return Rect.zero;

            Vector3[] corners = new Vector3[4];
            rt.GetWorldCorners(corners);

            Camera? uiCam = AR.s?.UI?.UICamera;
            Canvas? canvas = rt.GetComponentInParent<Canvas>();
            bool useCamera = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay && uiCam != null;

            Vector3 bl = useCamera ? (Vector3)uiCam!.WorldToScreenPoint(corners[0]) : corners[0];
            Vector3 tr = useCamera ? (Vector3)uiCam!.WorldToScreenPoint(corners[2]) : corners[2];

            float xMin = Mathf.Min(bl.x, tr.x);
            float xMax = Mathf.Max(bl.x, tr.x);
            float yMin = Mathf.Min(bl.y, tr.y);
            float yMax = Mathf.Max(bl.y, tr.y);
            return new Rect(xMin, yMin, xMax - xMin, yMax - yMin);
        }

        // 마우스가 UI 요소 밖으로 나갈 때 (툴팁 비활성화)
        public void OnPointerExit(PointerEventData eventData)
        {
            //OnExit(eventData);
            AR.s.UI.HideTooltip();
            AR.s.Tooltip.Hide();
        }

        // 마우스 클릭 시
        public void OnPointerClick(PointerEventData eventData)
        {
            OnClick(eventData);
        }

        public virtual void Refresh()
        {
            if (_itemData == null)
            {
                Reset();
                return;
            }

            _BG.gameObject.SetActive(true);
            _Icon.gameObject.SetActive(true);

            if (_itemData.Table == null)
            {
                Debug.LogError($"[SlotUI] Refresh - itemTable is null, itemId({_itemData.Id})");
                _Icon.sprite = null;
                return;
            }

            _Icon.sprite = AR.s.Data.GetSprite(_itemData.Table.SpriteName);

            if (1 <= _itemData.Quantity)
            {
                _TextQuantity.gameObject.SetActive(true);
                _TextQuantity.text = _itemData.Quantity.ToString();
            }
            else
            {
                _TextQuantity.gameObject.SetActive(false);
            }
        }

        protected virtual void OnEnter(PointerEventData eventData)
        {
            Debug.Log($"[SlotUI] OnEnter - SlotIndex({_slotIndex}), ItemId({(_itemData != null ? _itemData.Id.ToString() : "null")})");
        }

        protected virtual void OnExit(PointerEventData eventData)
        {
            Debug.Log($"[SlotUI] OnExit - SlotIndex({_slotIndex}), ItemId({(_itemData != null ? _itemData.Id.ToString() : "null")})");
        }

        protected virtual void OnClick(PointerEventData eventData)
        {
            _onClick?.Invoke(this, eventData);
        }
    }
}


