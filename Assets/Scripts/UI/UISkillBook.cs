#nullable enable
using ARPG.Base;
using ARPG.Data;
using ARPG.Message;
using Unity.AppUI.UI;
using UnityEngine;
using UnityEngine.UIElements;
using Text = Unity.AppUI.UI.Text;

namespace ARPG.UI
{
    /// <summary>
    /// 스킬북 장착 UI (SKILLBOOK_DESIGN.md §5).
    /// 좌: 인벤토리(스킬북 필터) / 우: 5×2 장착 슬롯 (PLAYER_SKILL_SLOT_COUNT=10).
    ///
    /// 호버 툴팁은 글로벌 매니저(AR.s.Tooltip)에 위임 — 어떤 ItemData든 동일 호출.
    /// 데이터 변경 알림: SkillBookChangedMessage 구독.
    /// </summary>
    public class UISkillBook : UIBaseForm
    {
        // 슬롯별 키 라벨 (디자인 §2.3)
        private static readonly string[] SLOT_KEY_LABELS =
        {
            "LMB", "SPACE", "1", "2", "3", "4", "5", "6", "7", "8"
        };

        private UIDocument? _document;
        private IconButton? _closeBtn;
        private Text? _statusText;
        private VisualElement? _inventoryGrid;
        private VisualElement?[] _slotElements = new VisualElement?[GlobalEnum.PLAYER_SKILL_SLOT_COUNT];
        private VisualElement? _lastRoot;

        public override void Initialize(string inName, bool isForm = false)
        {
            base.Initialize(inName, isForm);

            _document = GetComponent<UIDocument>();
            if (_document == null)
            {
                Debug.LogError("[UISkillBook] UIDocument 컴포넌트 없음 — prefab 확인 필요");
                return;
            }

            EnsureBound();
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

            _closeBtn = root.Q<IconButton>("close-btn");
            _statusText = root.Q<Text>("status-text");
            _inventoryGrid = root.Q<VisualElement>("inventory-grid");

            for (int i = 0; i < _slotElements.Length; i++)
            {
                _slotElements[i] = root.Q<VisualElement>($"slot-{i}");
            }

            if (_closeBtn != null) _closeBtn.clicked += () => Close();

            for (int i = 0; i < _slotElements.Length; i++)
            {
                int slotIndex = i; // 클로저 캡처 방지
                VisualElement? slot = _slotElements[i];
                if (slot == null) continue;

                slot.RegisterCallback<ClickEvent>(_ => OnClickSkillSlot(slotIndex));

                // 호버 콜백은 1회 등록(슬롯 element는 재생성되지 않음).
                // 책 정보는 RefreshSkillSlots에서 slot.userData에 셋팅.
                VisualElement slotRef = slot;
                slotRef.RegisterCallback<MouseEnterEvent>(_ => AR.s.Tooltip.Show(slotRef.userData as ItemData, GetMouseScreenPos()));
                slotRef.RegisterCallback<MouseLeaveEvent>(_ => AR.s.Tooltip.Hide());
                slotRef.RegisterCallback<MouseMoveEvent>(_ => AR.s.Tooltip.UpdatePosition(GetMouseScreenPos()));
            }
        }

        public override void OnOpen()
        {
            base.OnOpen();
            EnsureBound();
            AR.s.Message.Subscribe<SkillBookChangedMessage>(OnSkillBookChanged);
            ClearStatus();
            RefreshAll();
        }

        public override void OnClose()
        {
            AR.s.Message.Unsubscribe<SkillBookChangedMessage>(OnSkillBookChanged);
            AR.s.Tooltip.Hide();
            base.OnClose();
        }

        // ========== 메시지 수신 ==========

        private void OnSkillBookChanged(SkillBookChangedMessage msg)
        {
            // 슬롯 하나만 갱신해도 되지만 인벤토리 측 표시도 변하므로 전체 리빌드
            RefreshAll();
        }

        // ========== 클릭 핸들러 ==========

        private void OnClickInventoryBook(int inventorySlotIndex)
        {
            if (AR.s.PlayerSkill == null) return;

            // 첫 빈 스킬 슬롯 탐색
            int targetSlot = FindFirstEmptySkillSlot();
            if (targetSlot < 0)
            {
                SetStatus("스킬 슬롯이 모두 차 있습니다. 슬롯을 클릭해 해제 후 다시 시도하세요.");
                return;
            }

            bool success = AR.s.PlayerSkill.EquipSkillBook(targetSlot, inventorySlotIndex);
            if (success == false)
            {
                SetStatus("장착 실패 — 책 정보를 확인하세요.");
            }
            // 성공 시 SkillBookChangedMessage 수신 → RefreshAll 자동 호출
        }

        private void OnClickSkillSlot(int slotIndex)
        {
            if (AR.s.PlayerSkill == null) return;

            ItemData? equipped = AR.s.PlayerSkill.GetEquippedBook(slotIndex);
            if (equipped == null) return; // 빈 슬롯 클릭은 무시

            bool success = AR.s.PlayerSkill.UnequipSkillBook(slotIndex);
            if (success == false)
            {
                SetStatus("해제 실패 — 인벤토리에 빈 슬롯이 없습니다.");
            }
        }

        private int FindFirstEmptySkillSlot()
        {
            if (AR.s.PlayerSkill == null) return -1;
            for (int i = 0; i < GlobalEnum.PLAYER_SKILL_SLOT_COUNT; i++)
            {
                if (AR.s.PlayerSkill.GetEquippedBook(i) == null) return i;
            }
            return -1;
        }

        // ========== 갱신 ==========

        private void RefreshAll()
        {
            if (_lastRoot == null) return;
            RefreshSkillSlots();
            RebuildInventoryGrid();
        }

        private void RefreshSkillSlots()
        {
            for (int i = 0; i < _slotElements.Length; i++)
            {
                VisualElement? slot = _slotElements[i];
                if (slot == null) continue;

                slot.Clear();
                slot.RemoveFromClassList("skillbook-slot-equipped");
                slot.userData = null;

                // 키 라벨 (항상 표시)
                Text keyLabel = new() { text = i < SLOT_KEY_LABELS.Length ? SLOT_KEY_LABELS[i] : i.ToString() };
                keyLabel.AddToClassList("skillbook-slot-keylabel");
                slot.Add(keyLabel);

                ItemData? book = AR.s.PlayerSkill?.GetEquippedBook(i);
                if (book == null) continue;

                slot.AddToClassList("skillbook-slot-equipped");
                slot.userData = book; // 호버 시 AR.s.Tooltip이 참조

                // 아이콘 (책 표지: ItemTable.SpriteName)
                VisualElement icon = new();
                icon.AddToClassList("skillbook-slot-icon");
                if (book.Table != null && string.IsNullOrEmpty(book.Table.SpriteName) == false)
                {
                    Sprite? sprite = AR.s.Data.GetSprite(book.Table.SpriteName);
                    if (sprite != null) icon.style.backgroundImage = new StyleBackground(sprite);
                }
                slot.Add(icon);

                // 스킬명 (SkillTable.Name)
                string skillName = book.SkillBook?.Table?.Name ?? string.Empty;
                if (string.IsNullOrEmpty(skillName) == false)
                {
                    Text nameText = new() { text = skillName };
                    nameText.AddToClassList("skillbook-slot-skillname");
                    slot.Add(nameText);
                }
            }
        }

        private void RebuildInventoryGrid()
        {
            if (_inventoryGrid == null) return;
            _inventoryGrid.Clear();

            var inventory = AR.s.Player?.Inventory;
            if (inventory == null || inventory.Items == null)
            {
                AddEmptyText(_inventoryGrid, "인벤토리 비어있음");
                return;
            }

            int bookCount = 0;
            for (int i = 0; i < inventory.Items.Count; i++)
            {
                ItemData? item = inventory.Items[i];
                if (item == null) continue;
                if (item.Table == null || item.Table.ItemType != GlobalEnum.ItemType.SkillBook) continue;
                if (item.SkillBook == null || item.SkillBook.SkillId <= 0) continue;

                int invSlotIndex = i; // 클로저 캡처
                VisualElement bookSlot = new();
                bookSlot.AddToClassList("skillbook-inv-item");

                // 아이콘 (책 표지)
                VisualElement icon = new();
                icon.AddToClassList("skillbook-inv-item-icon");
                if (string.IsNullOrEmpty(item.Table.SpriteName) == false)
                {
                    Sprite? sprite = AR.s.Data.GetSprite(item.Table.SpriteName);
                    if (sprite != null) icon.style.backgroundImage = new StyleBackground(sprite);
                }
                bookSlot.Add(icon);

                // Tier 라벨 (좌상단)
                if (item.Table.Tier > 0)
                {
                    Text tierLabel = new() { text = $"T{item.Table.Tier}" };
                    tierLabel.AddToClassList("skillbook-inv-item-tier");
                    bookSlot.Add(tierLabel);
                }

                bookSlot.userData = item;
                VisualElement bookSlotRef = bookSlot;
                bookSlot.RegisterCallback<MouseEnterEvent>(_ => AR.s.Tooltip.Show(bookSlotRef.userData as ItemData, GetMouseScreenPos()));
                bookSlot.RegisterCallback<MouseLeaveEvent>(_ => AR.s.Tooltip.Hide());
                bookSlot.RegisterCallback<MouseMoveEvent>(_ => AR.s.Tooltip.UpdatePosition(GetMouseScreenPos()));
                bookSlot.RegisterCallback<ClickEvent>(_ => OnClickInventoryBook(invSlotIndex));

                _inventoryGrid.Add(bookSlot);
                bookCount++;
            }

            if (bookCount == 0)
            {
                AddEmptyText(_inventoryGrid, "보유한 스킬북이 없습니다");
            }
        }

        // ========== 헬퍼 ==========

        private static Vector2 GetMouseScreenPos()
        {
            return (Vector2)UnityEngine.Input.mousePosition;
        }

        private void SetStatus(string msg)
        {
            if (_statusText != null) _statusText.text = msg;
        }

        private void ClearStatus()
        {
            if (_statusText != null) _statusText.text = string.Empty;
        }

        private static void AddEmptyText(VisualElement parent, string msg)
        {
            Text emptyText = new() { text = msg };
            emptyText.AddToClassList("skillbook-empty-text");
            parent.Add(emptyText);
        }
    }
}
