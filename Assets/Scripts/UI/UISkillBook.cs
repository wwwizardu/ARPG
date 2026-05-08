#nullable enable
using ARPG.Base;
using ARPG.Data;
using ARPG.Message;
using ARPG.Tables;
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

        // 페이지 편집 모달 (SKILL_RUNE_DESIGN §7.2)
        private VisualElement? _pageModalBackdrop;
        private VisualElement? _pageSlotsRow;
        private VisualElement? _pageCapacityFill;
        private Text? _pageCapacityText;
        private VisualElement? _pageInvGrid;
        private Text? _pageModalStatus;
        private int _pageModalSkillBookSlot = -1;

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

                // 우클릭 → 페이지 편집 모달
                slot.RegisterCallback<PointerDownEvent>(evt =>
                {
                    if (evt.button != 1) return; // 우클릭만
                    OpenPageEditor(slotIndex);
                    evt.StopPropagation();
                });

                // 호버 콜백은 1회 등록(슬롯 element는 재생성되지 않음).
                // 책 정보는 RefreshSkillSlots에서 slot.userData에 셋팅.
                VisualElement slotRef = slot;
                slotRef.RegisterCallback<MouseEnterEvent>(_ => AR.s.Tooltip.Show(slotRef.userData as ItemData, GetSlotScreenRect(slotRef)));
                slotRef.RegisterCallback<MouseLeaveEvent>(_ => AR.s.Tooltip.Hide());

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
                bookSlot.RegisterCallback<MouseEnterEvent>(_ => AR.s.Tooltip.Show(bookSlotRef.userData as ItemData, GetSlotScreenRect(bookSlotRef)));
                bookSlot.RegisterCallback<MouseLeaveEvent>(_ => AR.s.Tooltip.Hide());
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

        /// <summary>
        /// UI Toolkit VisualElement → Screen Space Rect (Y up, 좌하단 원점). 글로벌 툴팁 anchor용.
        /// worldBound는 panel-local(Y down)이라 panel root 사이즈 비율로 screen 좌표로 매핑하고 Y를 반전.
        /// </summary>
        private static Rect GetSlotScreenRect(VisualElement slot)
        {
            if (slot == null) return Rect.zero;
            IPanel? panel = slot.panel;
            if (panel == null) return Rect.zero;

            VisualElement visualTree = panel.visualTree;
            float panelW = visualTree.resolvedStyle.width;
            float panelH = visualTree.resolvedStyle.height;
            if (panelW <= 0f || panelH <= 0f) return Rect.zero;

            float scaleX = Screen.width / panelW;
            float scaleY = Screen.height / panelH;

            Rect bound = slot.worldBound;
            float xMin = bound.xMin * scaleX;
            float xMax = bound.xMax * scaleX;
            float yMaxScreen = Screen.height - bound.yMin * scaleY; // panel 상단(yMin) → screen 상단(yMax)
            float yMinScreen = Screen.height - bound.yMax * scaleY; // panel 하단(yMax) → screen 하단(yMin)

            return new Rect(xMin, yMinScreen, xMax - xMin, yMaxScreen - yMinScreen);
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

        // ========== 페이지 편집 모달 (SKILL_RUNE_DESIGN §7.2) ==========

        private void OpenPageEditor(int skillBookSlotIndex)
        {
            if (_lastRoot == null) return;
            if (AR.s.PlayerSkill == null) return;

            ItemData? book = AR.s.PlayerSkill.GetEquippedBook(skillBookSlotIndex);
            if (book == null || book.SkillBook == null || book.Table == null)
            {
                SetStatus("빈 슬롯입니다 — 책을 먼저 장착하세요.");
                return;
            }

            EndDrag();
            AR.s.Tooltip.Hide();
            ClosePageEditor();

            _pageModalSkillBookSlot = skillBookSlotIndex;

            // ----- backdrop (외부 클릭 시 닫힘) -----
            VisualElement backdrop = new();
            backdrop.AddToClassList("skillpage-modal-backdrop");
            backdrop.RegisterCallback<ClickEvent>(evt =>
            {
                if (evt.target == backdrop) ClosePageEditor();
            });
            _lastRoot.Add(backdrop);
            _pageModalBackdrop = backdrop;

            // ----- panel -----
            VisualElement panel = new();
            panel.AddToClassList("skillpage-modal-panel");
            backdrop.Add(panel);

            // header
            VisualElement header = new();
            header.AddToClassList("skillpage-modal-header");
            string skillName = book.SkillBook.Table?.Name ?? "?";
            string itemName = book.Table.Name ?? "";
            Text title = new() { text = $"{itemName} — {skillName}" };
            title.AddToClassList("skillpage-modal-title");
            header.Add(title);

            IconButton closeBtn = new() { icon = "x" };
            closeBtn.AddToClassList("skillbook-close-icon");
            closeBtn.clicked += ClosePageEditor;
            header.Add(closeBtn);
            panel.Add(header);

            // body
            VisualElement body = new();
            body.AddToClassList("skillpage-modal-body");
            panel.Add(body);

            VisualElement left = new();
            left.AddToClassList("skillpage-modal-left");
            body.Add(left);

            VisualElement right = new();
            right.AddToClassList("skillpage-modal-right");
            body.Add(right);

            // 좌측: 페이지 슬롯들 + 페이지 용량 게이지
            Text leftTitle = new() { text = "장착된 페이지" };
            leftTitle.AddToClassList("skillbook-section-title");
            left.Add(leftTitle);

            VisualElement slotsRow = new();
            slotsRow.AddToClassList("skillpage-slot-row");
            left.Add(slotsRow);
            _pageSlotsRow = slotsRow;

            VisualElement barBg = new();
            barBg.AddToClassList("skillpage-capacity-bar-bg");
            VisualElement fill = new();
            fill.AddToClassList("skillpage-capacity-bar-fill");
            barBg.Add(fill);
            left.Add(barBg);
            _pageCapacityFill = fill;

            Text capText = new() { text = "" };
            capText.AddToClassList("skillpage-capacity-text");
            left.Add(capText);
            _pageCapacityText = capText;

            Text status = new() { text = "" };
            status.AddToClassList("skillbook-status");
            status.style.marginTop = 8;
            left.Add(status);
            _pageModalStatus = status;

            // 우측: 인벤토리의 페이지 목록
            Text rightTitle = new() { text = "보유 페이지 (클릭=장착)" };
            rightTitle.AddToClassList("skillbook-section-title");
            right.Add(rightTitle);

            ScrollView invScroll = new();
            invScroll.AddToClassList("skillpage-inv-scroll");
            right.Add(invScroll);

            VisualElement invGrid = new();
            invGrid.AddToClassList("skillpage-inv-grid");
            invScroll.Add(invGrid);
            _pageInvGrid = invGrid;

            RebuildPageEditor();
        }

        private void ClosePageEditor()
        {
            if (_pageModalBackdrop != null)
            {
                _pageModalBackdrop.RemoveFromHierarchy();
                _pageModalBackdrop = null;
            }
            _pageSlotsRow = null;
            _pageCapacityFill = null;
            _pageCapacityText = null;
            _pageInvGrid = null;
            _pageModalStatus = null;
            _pageModalSkillBookSlot = -1;
            AR.s.Tooltip.Hide();
        }

        private void RebuildPageEditor()
        {
            if (_pageModalSkillBookSlot < 0) return;
            if (AR.s.PlayerSkill == null) return;

            ItemData? book = AR.s.PlayerSkill.GetEquippedBook(_pageModalSkillBookSlot);
            if (book == null || book.SkillBook == null || book.Table == null)
            {
                ClosePageEditor();
                return;
            }

            int slotCount = AR.s.PlayerSkill.GetPageSlots(book);
            int capacity = AR.s.PlayerSkill.GetPageCapacity(book);
            int used = AR.s.PlayerSkill.GetUsedPageCost(book);

            BuildPageSlotsRow(book, slotCount);
            UpdateCapacityBar(used, capacity);
            BuildPageInventoryGrid();
        }

        private void BuildPageSlotsRow(ItemData book, int slotCount)
        {
            if (_pageSlotsRow == null) return;
            _pageSlotsRow.Clear();

            var pages = book.SkillBook?.SocketedPages;
            int filled = pages?.Count ?? 0;

            for (int i = 0; i < slotCount; i++)
            {
                int slotIdx = i;
                VisualElement slot = new();
                slot.AddToClassList("skillpage-slot");

                if (i < filled)
                {
                    ItemData pageItem = pages![i];
                    SkillEffectTable? effect = pageItem.SkillPage?.Table;
                    if (effect != null && pageItem.Table != null)
                    {
                        slot.AddToClassList("skillpage-slot-filled");

                        Text costLabel = new() { text = effect.PageCost.ToString() };
                        costLabel.AddToClassList("skillpage-slot-cost");
                        slot.Add(costLabel);

                        VisualElement icon = new();
                        icon.AddToClassList("skillpage-slot-icon");
                        if (string.IsNullOrEmpty(pageItem.Table.SpriteName) == false)
                        {
                            Sprite? sprite = AR.s.Data.GetSprite(pageItem.Table.SpriteName);
                            if (sprite != null) icon.style.backgroundImage = new StyleBackground(sprite);
                        }
                        slot.Add(icon);

                        slot.userData = pageItem;
                        VisualElement slotRef = slot;
                        slot.RegisterCallback<MouseEnterEvent>(_ =>
                        {
                            if (slotRef.userData is ItemData item)
                                AR.s.Tooltip.Show(item, GetSlotScreenRect(slotRef));
                        });
                        slot.RegisterCallback<MouseLeaveEvent>(_ => AR.s.Tooltip.Hide());

                        slot.RegisterCallback<ClickEvent>(_ => UnsocketAt(slotIdx));
                    }
                }

                _pageSlotsRow.Add(slot);
            }
        }

        private void UpdateCapacityBar(int used, int capacity)
        {
            if (_pageCapacityFill == null || _pageCapacityText == null) return;

            float ratio = capacity > 0 ? Mathf.Clamp01((float)used / capacity) : 0f;
            _pageCapacityFill.style.width = new Length(ratio * 100f, LengthUnit.Percent);

            _pageCapacityFill.RemoveFromClassList("skillpage-capacity-bar-fill-over");
            if (used > capacity)
            {
                _pageCapacityFill.AddToClassList("skillpage-capacity-bar-fill-over");
            }

            _pageCapacityText.text = $"페이지 용량: {used} / {capacity}";
        }

        private void BuildPageInventoryGrid()
        {
            if (_pageInvGrid == null) return;
            _pageInvGrid.Clear();

            var inventory = AR.s.Player?.Inventory;
            if (inventory == null || inventory.Items == null)
            {
                AddEmptyText(_pageInvGrid, "인벤토리 비어있음");
                return;
            }

            int count = 0;
            for (int i = 0; i < inventory.Items.Count; i++)
            {
                ItemData? item = inventory.Items[i];
                if (item == null || item.Table == null) continue;
                if (item.Table.ItemType != GlobalEnum.ItemType.SkillPage) continue;
                if (item.SkillPage == null || item.SkillPage.SkillEffectId <= 0) continue;

                int invSlot = i;
                int effectId = item.SkillPage.SkillEffectId;
                SkillEffectTable? effect = AR.s.Data?.GetSkillEffect(effectId);

                VisualElement slot = new();
                slot.AddToClassList("skillpage-inv-item");

                int tier = item.Table.Tier;
                if (1 <= tier && tier <= 3)
                {
                    slot.AddToClassList($"skillpage-tier-{tier}");
                }

                VisualElement icon = new();
                icon.AddToClassList("skillpage-inv-item-icon");
                if (string.IsNullOrEmpty(item.Table.SpriteName) == false)
                {
                    Sprite? sprite = AR.s.Data.GetSprite(item.Table.SpriteName);
                    if (sprite != null) icon.style.backgroundImage = new StyleBackground(sprite);
                }
                slot.Add(icon);

                if (effect != null)
                {
                    Text costLabel = new() { text = effect.PageCost.ToString() };
                    costLabel.AddToClassList("skillpage-inv-item-cost");
                    slot.Add(costLabel);
                }

                slot.userData = item;
                VisualElement slotRef = slot;
                slot.RegisterCallback<MouseEnterEvent>(_ => AR.s.Tooltip.Show(slotRef.userData as ItemData, GetSlotScreenRect(slotRef)));
                slot.RegisterCallback<MouseLeaveEvent>(_ => AR.s.Tooltip.Hide());

                slot.RegisterCallback<ClickEvent>(_ => SocketFromInventory(invSlot));
                _pageInvGrid.Add(slot);
                count++;
            }

            if (count == 0)
            {
                AddEmptyText(_pageInvGrid, "보유한 스킬 페이지가 없습니다");
            }
        }

        private void SocketFromInventory(int inventorySlotIndex)
        {
            if (_pageModalSkillBookSlot < 0 || AR.s.PlayerSkill == null) return;

            bool ok = AR.s.PlayerSkill.SocketSkillPage(_pageModalSkillBookSlot, inventorySlotIndex);
            if (ok == false)
            {
                SetPageModalStatus("장착 실패 — 슬롯/용량/중복을 확인하세요.");
                return;
            }
            SetPageModalStatus("장착 완료");
            RebuildPageEditor();
        }

        private void UnsocketAt(int pageIndex)
        {
            if (_pageModalSkillBookSlot < 0 || AR.s.PlayerSkill == null) return;

            bool ok = AR.s.PlayerSkill.UnsocketSkillPage(_pageModalSkillBookSlot, pageIndex);
            if (ok == false)
            {
                SetPageModalStatus("해제 실패 — 인벤토리가 가득 찼는지 확인하세요.");
                return;
            }
            SetPageModalStatus("페이지 해제");
            RebuildPageEditor();
        }

        private void SetPageModalStatus(string msg)
        {
            if (_pageModalStatus != null) _pageModalStatus.text = msg;
        }
    }
}
