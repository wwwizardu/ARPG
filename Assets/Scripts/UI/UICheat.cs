#nullable enable
using System.Collections;
using ARPG.Base;
using Unity.AppUI.UI;
using UnityEngine;
using UnityEngine.UIElements;
using Button = Unity.AppUI.UI.Button;
using Text = Unity.AppUI.UI.Text;
using TextField = UnityEngine.UIElements.TextField;

namespace ARPG.UI
{
    /// <summary>
    /// 치트 UI (개발용) — UI Toolkit + App UI 기반.
    /// - 아이템 ID 입력 → 인벤토리에 추가
    /// - 골드 수량 입력 → PlayerData.Gold에 가산
    /// </summary>
    public class UICheat : UIBaseForm
    {
        private const string POPUP_VISIBLE_CLASS = "cheat-popup--visible";

        private UIDocument? _document;
        private TextField? _itemInput;
        private TextField? _goldInput;
        private Button? _addItemBtn;
        private Button? _addGoldBtn;
        private IconButton? _closeBtn;
        private Text? _popupText;

        private VisualElement? _lastRoot;
        private Coroutine? _popupCoroutine;
        private readonly WaitForSeconds _popupWait = new WaitForSeconds(2f);

        public override void Initialize(string inName, bool isForm = false)
        {
            base.Initialize(inName, isForm);

            _document = GetComponent<UIDocument>();
            if (_document == null)
            {
                Debug.LogError("[UICheat] UIDocument 컴포넌트 없음 — prefab 확인 필요");
                return;
            }

            EnsureBound();
        }

        public override void OnOpen()
        {
            base.OnOpen();
            EnsureBound();
            HidePopup();
        }

        public override void OnClose()
        {
            base.OnClose();

            if (_popupCoroutine != null)
            {
                StopCoroutine(_popupCoroutine);
                _popupCoroutine = null;
            }

            HidePopup();
        }

        /// <summary>
        /// UIDocument는 SetActive 토글 시마다 rootVisualElement를 재구성하므로
        /// root 변경을 감지해 매번 재바인딩.
        /// </summary>
        private void EnsureBound()
        {
            if (_document == null) return;
            VisualElement root = _document.rootVisualElement;
            if (root == null) return;
            if (root == _lastRoot) return;

            _lastRoot = root;

            _itemInput = root.Q<TextField>("item-input");
            _goldInput = root.Q<TextField>("gold-input");
            _addItemBtn = root.Q<Button>("add-item-btn");
            _addGoldBtn = root.Q<Button>("add-gold-btn");
            _closeBtn = root.Q<IconButton>("close-btn");
            _popupText = root.Q<Text>("popup-text");

            if (_addItemBtn != null) _addItemBtn.clicked += OnAddItem;
            if (_addGoldBtn != null) _addGoldBtn.clicked += OnAddGold;
            if (_closeBtn != null) _closeBtn.clicked += () => Close();
        }

        public void OnAddItem()
        {
            if (_itemInput == null || string.IsNullOrEmpty(_itemInput.value))
                return;

            if (int.TryParse(_itemInput.value, out int itemId) == false)
            {
                ShowPopup("아이템 ID는 숫자로 입력해주세요");
                return;
            }

            var table = AR.s.Data.GetItem(itemId);
            if (table == null)
            {
                ShowPopup($"ItemTable not found. Id: {itemId}");
                return;
            }

            var itemData = new Data.ItemData
            {
                Id = itemId,
                Quantity = 1,
                Table = table
            };

            int slotIndex = AR.s.Player.Inventory.AddItem(itemData);
            if (slotIndex >= 0)
            {
                ShowPopup($"AddItem success. Id: {itemId}, Name: {table.Name}");
            }
            else
            {
                ShowPopup($"AddItem failed. Id: {itemId}, Name: {table.Name}");
            }
        }

        public void OnAddGold()
        {
            if (_goldInput == null || string.IsNullOrEmpty(_goldInput.value))
                return;

            if (int.TryParse(_goldInput.value, out int amount) == false)
            {
                ShowPopup("골드 수량은 숫자로 입력해주세요");
                return;
            }

            if (AR.s.Data.Player == null)
            {
                ShowPopup("PlayerData가 없습니다");
                return;
            }

            AR.s.Data.Player.Gold += amount;
            ShowPopup($"AddGold {amount}G — 현재 {AR.s.Data.Player.Gold}G");
        }

        private void ShowPopup(string message)
        {
            if (_popupText == null)
                return;

            if (_popupCoroutine != null)
            {
                StopCoroutine(_popupCoroutine);
            }

            _popupCoroutine = StartCoroutine(ShowPopupCoroutine(message));
        }

        private IEnumerator ShowPopupCoroutine(string message)
        {
            if (_popupText == null) yield break;

            _popupText.text = message;
            _popupText.AddToClassList(POPUP_VISIBLE_CLASS);

            yield return _popupWait;

            HidePopup();
            _popupCoroutine = null;
        }

        private void HidePopup()
        {
            if (_popupText == null) return;
            _popupText.RemoveFromClassList(POPUP_VISIBLE_CLASS);
        }
    }
}
