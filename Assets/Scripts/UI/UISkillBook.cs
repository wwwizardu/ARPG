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

        // 드래그앤드롭 상태
        private enum DragSource { None, Inventory, Equipped }
        private DragSource _dragSrcKind = DragSource.None;
        private int _dragSrcIndex = -1;
        private int _dragPointerId = -1;
        private VisualElement? _dragCaptureTarget;
        private VisualElement? _dragGhost;
        private VisualElement? _dragHighlight;
        private Vector2 _dragStartPos;
        private bool _dragActive;
        private bool _suppressClick; // 드래그 직후 ClickEvent 무시용
        private const float DRAG_THRESHOLD_SQ = 16f; // 4px
        private const float GHOST_HALF_SIZE = 32f;

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

                slot.RegisterCallback<ClickEvent>(_ =>
                {
                    if (_suppressClick) { _suppressClick = false; return; }
                    OnClickSkillSlot(slotIndex);
                });

                // 호버 콜백은 1회 등록(슬롯 element는 재생성되지 않음).
                // 책 정보는 RefreshSkillSlots에서 slot.userData에 셋팅.
                VisualElement slotRef = slot;
                slotRef.RegisterCallback<MouseEnterEvent>(_ => AR.s.Tooltip.Show(slotRef.userData as ItemData, GetMouseScreenPos()));
                slotRef.RegisterCallback<MouseLeaveEvent>(_ => AR.s.Tooltip.Hide());
                slotRef.RegisterCallback<MouseMoveEvent>(_ => AR.s.Tooltip.UpdatePosition(GetMouseScreenPos()));

                // 드래그앤드롭 (장착 슬롯이 소스)
                slotRef.RegisterCallback<PointerDownEvent>(evt => OnDragPointerDown(evt, DragSource.Equipped, slotIndex, slotRef));
                slotRef.RegisterCallback<PointerMoveEvent>(OnDragPointerMove);
                slotRef.RegisterCallback<PointerUpEvent>(OnDragPointerUp);
                slotRef.RegisterCallback<PointerCaptureOutEvent>(OnDragPointerCaptureOut);
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
            EndDrag(); // 드래그 도중 닫혀도 안전하게 정리
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
                bookSlot.RegisterCallback<ClickEvent>(_ =>
                {
                    if (_suppressClick) { _suppressClick = false; return; }
                    OnClickInventoryBook(invSlotIndex);
                });

                // 드래그앤드롭 (인벤토리 책이 소스)
                bookSlot.RegisterCallback<PointerDownEvent>(evt => OnDragPointerDown(evt, DragSource.Inventory, invSlotIndex, bookSlotRef));
                bookSlot.RegisterCallback<PointerMoveEvent>(OnDragPointerMove);
                bookSlot.RegisterCallback<PointerUpEvent>(OnDragPointerUp);
                bookSlot.RegisterCallback<PointerCaptureOutEvent>(OnDragPointerCaptureOut);

                _inventoryGrid.Add(bookSlot);
                bookCount++;
            }

            if (bookCount == 0)
            {
                AddEmptyText(_inventoryGrid, "보유한 스킬북이 없습니다");
            }
        }

        // ========== 드래그앤드롭 ==========

        private void OnDragPointerDown(PointerDownEvent evt, DragSource srcKind, int srcIndex, VisualElement target)
        {
            if (evt.button != 0) return; // 좌클릭만
            if (_dragSrcKind != DragSource.None) return; // 이미 진행 중

            // 빈 장착 슬롯에서는 드래그 시작하지 않음(클릭 의미만 가짐)
            if (srcKind == DragSource.Equipped && AR.s.PlayerSkill?.GetEquippedBook(srcIndex) == null) return;

            _dragSrcKind = srcKind;
            _dragSrcIndex = srcIndex;
            _dragPointerId = evt.pointerId;
            _dragStartPos = evt.position;
            _dragCaptureTarget = target;
            _dragActive = false;

            target.CapturePointer(evt.pointerId);
        }

        private void OnDragPointerMove(PointerMoveEvent evt)
        {
            if (_dragSrcKind == DragSource.None) return;
            if (evt.pointerId != _dragPointerId) return;

            Vector2 pos = evt.position;
            if (_dragActive == false)
            {
                Vector2 delta = pos - _dragStartPos;
                if (delta.sqrMagnitude < DRAG_THRESHOLD_SQ) return;
                StartDrag();
            }

            if (_dragGhost != null)
            {
                _dragGhost.style.left = pos.x - GHOST_HALF_SIZE;
                _dragGhost.style.top = pos.y - GHOST_HALF_SIZE;
            }
            UpdateDropHighlight(pos);
        }

        private void OnDragPointerUp(PointerUpEvent evt)
        {
            if (_dragSrcKind == DragSource.None) return;
            if (evt.pointerId != _dragPointerId) return;

            if (_dragActive)
            {
                HandleDrop(evt.position);
                _suppressClick = true; // 직후 발생할 ClickEvent 무시
            }

            EndDrag();
        }

        private void OnDragPointerCaptureOut(PointerCaptureOutEvent evt)
        {
            // 캡처가 외부 사유로 풀렸을 때 안전 정리
            if (_dragSrcKind == DragSource.None) return;
            if (evt.pointerId != _dragPointerId) return;
            EndDrag();
        }

        private void StartDrag()
        {
            _dragActive = true;
            AR.s.Tooltip.Hide();

            Sprite? sprite = GetDragSourceSprite();
            _dragGhost = new VisualElement();
            _dragGhost.AddToClassList("skillbook-drag-ghost");
            _dragGhost.pickingMode = PickingMode.Ignore;
            if (sprite != null) _dragGhost.style.backgroundImage = new StyleBackground(sprite);
            _lastRoot?.Add(_dragGhost);
        }

        private void HandleDrop(Vector2 pointerPos)
        {
            if (_lastRoot == null) return;
            VisualElement? picked = _lastRoot.panel.Pick(pointerPos);
            if (picked == null) return;

            int targetSlot = FindSlotIndexFromElement(picked);
            if (targetSlot >= 0)
            {
                DropOnEquipSlot(targetSlot);
                return;
            }

            if (IsInsideInventoryGrid(picked))
            {
                DropOnInventory();
            }
        }

        private void DropOnEquipSlot(int targetSlot)
        {
            if (AR.s.PlayerSkill == null) return;

            if (_dragSrcKind == DragSource.Inventory)
            {
                bool ok = AR.s.PlayerSkill.EquipSkillBook(targetSlot, _dragSrcIndex);
                if (ok == false) SetStatus("장착 실패 — 책 정보를 확인하세요.");
            }
            else if (_dragSrcKind == DragSource.Equipped)
            {
                if (_dragSrcIndex == targetSlot) return;
                bool ok = AR.s.PlayerSkill.SwapSkillSlots(_dragSrcIndex, targetSlot);
                if (ok == false) SetStatus("이동 실패");
            }
        }

        private void DropOnInventory()
        {
            if (_dragSrcKind != DragSource.Equipped) return; // 인벤→인벤은 의미 없음
            if (AR.s.PlayerSkill == null) return;

            bool ok = AR.s.PlayerSkill.UnequipSkillBook(_dragSrcIndex);
            if (ok == false) SetStatus("해제 실패 — 인벤토리에 빈 슬롯이 없습니다.");
        }

        private void EndDrag()
        {
            if (_dragGhost != null)
            {
                _dragGhost.RemoveFromHierarchy();
                _dragGhost = null;
            }

            ClearDropHighlight();

            if (_dragCaptureTarget != null && _dragPointerId >= 0)
            {
                if (_dragCaptureTarget.HasPointerCapture(_dragPointerId))
                {
                    _dragCaptureTarget.ReleasePointer(_dragPointerId);
                }
            }

            _dragSrcKind = DragSource.None;
            _dragSrcIndex = -1;
            _dragPointerId = -1;
            _dragCaptureTarget = null;
            _dragActive = false;
        }

        private void UpdateDropHighlight(Vector2 pointerPos)
        {
            if (_lastRoot == null) return;
            VisualElement? picked = _lastRoot.panel.Pick(pointerPos);
            VisualElement? newTarget = null;

            if (picked != null)
            {
                int slotIdx = FindSlotIndexFromElement(picked);
                if (slotIdx >= 0) newTarget = _slotElements[slotIdx];
            }

            if (newTarget == _dragHighlight) return;

            if (_dragHighlight != null) _dragHighlight.RemoveFromClassList("skillbook-slot-droptarget");
            if (newTarget != null) newTarget.AddToClassList("skillbook-slot-droptarget");
            _dragHighlight = newTarget;
        }

        private void ClearDropHighlight()
        {
            if (_dragHighlight != null)
            {
                _dragHighlight.RemoveFromClassList("skillbook-slot-droptarget");
                _dragHighlight = null;
            }
        }

        private int FindSlotIndexFromElement(VisualElement element)
        {
            VisualElement? cur = element;
            while (cur != null)
            {
                for (int i = 0; i < _slotElements.Length; i++)
                {
                    if (_slotElements[i] == cur) return i;
                }
                cur = cur.parent;
            }
            return -1;
        }

        private bool IsInsideInventoryGrid(VisualElement element)
        {
            VisualElement? cur = element;
            while (cur != null)
            {
                if (cur == _inventoryGrid) return true;
                cur = cur.parent;
            }
            return false;
        }

        private Sprite? GetDragSourceSprite()
        {
            ItemData? item = null;
            if (_dragSrcKind == DragSource.Inventory)
            {
                var inv = AR.s.Player?.Inventory;
                if (inv != null && inv.Items != null && _dragSrcIndex >= 0 && _dragSrcIndex < inv.Items.Count)
                    item = inv.Items[_dragSrcIndex];
            }
            else if (_dragSrcKind == DragSource.Equipped)
            {
                item = AR.s.PlayerSkill?.GetEquippedBook(_dragSrcIndex);
            }

            if (item?.Table == null || string.IsNullOrEmpty(item.Table.SpriteName)) return null;
            return AR.s.Data.GetSprite(item.Table.SpriteName);
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
