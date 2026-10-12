#nullable enable
using UnityEngine.UI;
using UnityEngine;
using ARPG.Data;
using UnityEngine.EventSystems;
using System;

namespace ARPG.UI
{
    public class SlotUI_Equip : SlotUI
    {
        private const float CompareHighlightThickness = 3f;
        private static readonly Color CompareHighlightColor = new Color(1f, 0.84f, 0.4f, 1f);

        [SerializeField] private GlobalEnum.EquipSlotType _equipSlotType = GlobalEnum.EquipSlotType.Max;
        [SerializeField] Image _BG_EquipType = null!;

        // 인벤토리 장비 비교 대상 테두리. 처음 강조할 때 만든다.
        private GameObject? _compareHighlight = null;

        public GlobalEnum.EquipSlotType EquipSlotType { get { return _equipSlotType; } }

        /// <summary>인벤토리 장비 비교 중 비교 대상 슬롯 테두리를 켜고 끈다.</summary>
        public void SetCompareHighlight(bool isOn)
        {
            if (isOn == false)
            {
                if (_compareHighlight != null)
                    _compareHighlight.SetActive(false);
                return;
            }

            if (_compareHighlight == null)
                _compareHighlight = CreateCompareHighlight();
            _compareHighlight.transform.SetAsLastSibling();
            _compareHighlight.SetActive(true);
        }


        public override void Initialize(int inSlotIndex, Action<SlotUI, PointerEventData> onClick)
        {
            base.Initialize(inSlotIndex, onClick);
        }

        public override void Reset()
        {
            base.Reset();

            _BG_EquipType.gameObject.SetActive(true);
        }

        public bool CanEquip(Data.ItemData inItem)
        {
            if (inItem == null)
                return false;

            Tables.ItemTable? itemTable = AR.s.Data.GetItem(inItem.Id);
            if (itemTable == null)
            {
                Debug.LogError($"[SlotUI_Equip] CanEquip - ItemTable not found for ID: {inItem.Id}");
                return false;
            }

            if (inItem.Equipment == null)
                return false;

            GlobalEnum.EquipmentTypeMask allowedMask = Utility.EquipHelper.SlotToEquipmentTypeMask(_equipSlotType);
            if (Utils.ContainsEquipmentType(allowedMask, inItem.Equipment.EquipType) == false)
                return false;

            return true;
        }

        public bool HasItem()
        {
            return _itemData != null;
        }

        public override void SetItem(ItemData inItem)
        {
            base.SetItem(inItem);

        }

        public override void Refresh()
        {
            base.Refresh();

        }

        protected override void OnEnter(UnityEngine.EventSystems.PointerEventData eventData)
        {
            if (_itemData != null)
            {
                AR.s.Tooltip.Show(_itemData, GetSlotScreenRect(_rectTransform));
            }
        }

        protected override void OnExit(UnityEngine.EventSystems.PointerEventData eventData)
        {
            AR.s.Tooltip.Hide();
        }

        protected override void OnClick(PointerEventData eventData)
        {
            _onClick?.Invoke(this, eventData);
        }

        // 슬롯 바깥쪽에 네 변을 붙인 테두리. 레이캐스트를 받지 않아 슬롯 호버·클릭에 영향이 없다.
        private GameObject CreateCompareHighlight()
        {
            GameObject root = new GameObject("CompareHighlight", typeof(RectTransform));
            RectTransform rootRect = (RectTransform)root.transform;
            rootRect.SetParent(transform, false);
            rootRect.anchorMin = Vector2.zero;
            rootRect.anchorMax = Vector2.one;
            rootRect.offsetMin = Vector2.zero;
            rootRect.offsetMax = Vector2.zero;

            float t = CompareHighlightThickness;
            CreateHighlightEdge(rootRect, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 0f), new Vector2(t * 2f, t));   // 위
            CreateHighlightEdge(rootRect, new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0.5f, 1f), new Vector2(t * 2f, t));   // 아래
            CreateHighlightEdge(rootRect, new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(1f, 0.5f), new Vector2(t, 0f));       // 왼쪽
            CreateHighlightEdge(rootRect, new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(0f, 0.5f), new Vector2(t, 0f));       // 오른쪽
            return root;
        }

        private static void CreateHighlightEdge(RectTransform parent, Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot, Vector2 sizeDelta)
        {
            GameObject edge = new GameObject("Edge", typeof(RectTransform), typeof(Image));
            RectTransform rect = (RectTransform)edge.transform;
            rect.SetParent(parent, false);
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.pivot = pivot;
            rect.sizeDelta = sizeDelta;
            rect.anchoredPosition = Vector2.zero;

            Image image = edge.GetComponent<Image>();
            image.color = CompareHighlightColor;
            image.raycastTarget = false;
        }
    }
}


