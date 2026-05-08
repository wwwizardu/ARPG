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
        private TextField? _skillInput;
        private Button? _addItemBtn;
        private Button? _addGoldBtn;
        private Button? _addSkillBookBtn;
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
            _skillInput = root.Q<TextField>("skill-input");
            _addItemBtn = root.Q<Button>("add-item-btn");
            _addGoldBtn = root.Q<Button>("add-gold-btn");
            _addSkillBookBtn = root.Q<Button>("add-skillbook-btn");
            _closeBtn = root.Q<IconButton>("close-btn");
            _popupText = root.Q<Text>("popup-text");

            if (_addItemBtn != null) _addItemBtn.clicked += OnAddItem;
            if (_addGoldBtn != null) _addGoldBtn.clicked += OnAddGold;
            if (_addSkillBookBtn != null) _addSkillBookBtn.clicked += OnAddSkillBook;
            if (_closeBtn != null) _closeBtn.clicked += () => Close();
        }

        public void OnAddItem()
        {
            if (_itemInput == null || string.IsNullOrEmpty(_itemInput.value))
                return;

            // 입력 형식: "itemId" 또는 "itemId,skillId" (스킬북 전용)
            string raw = _itemInput.value.Trim();
            int explicitSkillId = 0;
            int commaIdx = raw.IndexOf(',');
            if (commaIdx >= 0)
            {
                if (int.TryParse(raw.Substring(commaIdx + 1).Trim(), out int sid))
                    explicitSkillId = sid;
                raw = raw.Substring(0, commaIdx).Trim();
            }

            if (int.TryParse(raw, out int itemId) == false)
            {
                ShowPopup("아이템 ID는 숫자. 스킬북은 'itemId,skillId' 형식 가능");
                return;
            }

            var table = AR.s.Data.GetItem(itemId);
            if (table == null)
            {
                ShowPopup($"ItemTable not found. Id: {itemId}");
                return;
            }

            // SkillBook + explicitSkillId 케이스만 직접 처리(특정 스킬을 책에 박아 넣는 치트 의미).
            // 그 외는 ItemManager 공통 팩토리에 위임 — SkillBook/SkillPage 인스턴스 데이터가 자동으로 채워짐.
            Data.ItemData? itemData;
            if (table.ItemType == GlobalEnum.ItemType.SkillBook && explicitSkillId > 0)
            {
                itemData = AR.s.Item.CreateSkillBook(itemId, explicitSkillId);
            }
            else
            {
                itemData = AR.s.Item.CreateInventoryItemData(itemId);
            }

            if (itemData == null)
            {
                ShowPopup($"아이템 생성 실패 — Id: {itemId}, 로그 확인");
                return;
            }

            int slotIndex = AR.s.Player.Inventory.AddItem(itemData);
            if (slotIndex >= 0)
            {
                string skillTag = itemData.SkillBook != null ? $", SkillId: {itemData.SkillBook.SkillId}" : string.Empty;
                ShowPopup($"AddItem success. Id: {itemId}, Name: {table.Name}{skillTag}");
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

        /// <summary>
        /// 스킬 ID 입력 → SkillTable.Tier 자동 룩업 → 매칭 등급 책으로 스킬북 생성 → 인벤토리에 추가.
        /// 가장 빠른 스킬북 테스트 경로.
        /// </summary>
        public void OnAddSkillBook()
        {
            if (_skillInput == null || string.IsNullOrEmpty(_skillInput.value))
                return;

            if (int.TryParse(_skillInput.value.Trim(), out int skillId) == false)
            {
                ShowPopup("스킬 ID는 숫자로 입력해주세요");
                return;
            }

            var skillTable = AR.s.Data?.GetSkill(skillId);
            if (skillTable == null)
            {
                ShowPopup($"SkillTable not found. SkillId: {skillId}");
                return;
            }

            Data.ItemData? book = AR.s.Item.CreateSkillBookForSkill(skillId);
            if (book == null)
            {
                ShowPopup($"스킬북 생성 실패 — SkillTable.Tier({skillTable.Tier})에 매칭되는 책 ItemId 없음. SkillTable.Tier 또는 ItemTable 확인");
                return;
            }

            int slotIndex = AR.s.Player.Inventory.AddItem(book);
            if (slotIndex >= 0)
            {
                string bookName = book.Table?.Name ?? "?";
                ShowPopup($"스킬북 생성 — {bookName} (Tier {skillTable.Tier}) + {skillTable.Name}(SkillId {skillId})");
            }
            else
            {
                ShowPopup("인벤토리가 가득 찼습니다");
            }
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
