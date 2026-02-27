using System.Collections;
using ARPG.Base;
using TMPro;
using UnityEngine;

namespace ARPG.UI
{
    /// <summary>
    /// 치트 UI (개발용)
    /// - HP 회복, 스킬 쿨타임 초기화, 아이템 획득 등 다양한 치트 기능 제공
    /// - 개발 중 테스트 편의성을 위해 구현
    /// </summary>

    public class UICheat : UIBaseForm
    {
        [SerializeField] private TextMeshProUGUI _textPopup;

        [SerializeField] private TMP_InputField _inputFieldAddItem;

        private Coroutine _popupCoroutine;
        private readonly WaitForSeconds _popupWait = new WaitForSeconds(2f);

        public void OnAddItem()
        {
            if (_inputFieldAddItem == null || string.IsNullOrEmpty(_inputFieldAddItem.text))
                return;

            if (int.TryParse(_inputFieldAddItem.text, out int itemId) == false)
                return;

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

        public override void OnClose()
        {
            base.OnClose();

            if (_popupCoroutine != null)
            {
                StopCoroutine(_popupCoroutine);
                _popupCoroutine = null;
            }

            if (_textPopup != null)
            {
                _textPopup.gameObject.SetActive(false);
            }
        }

        private void ShowPopup(string message)
        {
            if (_textPopup == null)
                return;

            if (_popupCoroutine != null)
            {
                StopCoroutine(_popupCoroutine);
            }

            _popupCoroutine = StartCoroutine(ShowPopupCoroutine(message));
        }

        private IEnumerator ShowPopupCoroutine(string message)
        {
            _textPopup.text = message;
            _textPopup.gameObject.SetActive(true);

            yield return _popupWait;

            _textPopup.gameObject.SetActive(false);
            _popupCoroutine = null;
        }
    }
}
