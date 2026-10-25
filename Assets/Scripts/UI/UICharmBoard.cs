#nullable enable
using System;
using System.Collections.Generic;
using ARPG.Base;
using ARPG.Data;
using ARPG.Item;
using ARPG.Message;
using ARPG.Utility;
using Unity.AppUI.UI;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;
using Label = UnityEngine.UIElements.Label;
using Text = Unity.AppUI.UI.Text;

namespace ARPG.UI
{
    /// <summary>
    /// 참 판 창 (PLAYER_LEVEL_CHARM_DESIGN.md §8.4~§8.7). P키 또는 캐릭터 창 [참 판] 버튼으로 연다.
    /// 왼쪽: 판 자리 3×3 (자리마다 판의 3×3칸이 보여 전체 9×9칸), 오른쪽: 가방(판·참만)과 옵션 합계.
    /// 끌어다 놓기: 가방 판 → 열린 자리(있으면 서로 바꿈), 자리 판 → 가방, 가방 참 → 자리의 판 칸,
    /// 박힌 참 → 다른 칸·다른 판·가방. 끄는 중 R키·우클릭으로 90도 회전. 박힌 참 우클릭은 가방으로.
    /// 데이터 변경은 PlayerManager(참 판 자리)가 맡고, 이 창은 CharmBoardChangedMessage를 받아 다시 그린다.
    /// </summary>
    public partial class UICharmBoard : UIBaseForm
    {
        private const float GHOST_CELL_SIZE = 30f;
        private const float DRAG_THRESHOLD_SQ = 16f;
        private static readonly Color[] CHARM_COLORS =
        {
            new Color(0.35f, 0.62f, 0.95f, 1f),
            new Color(0.88f, 0.62f, 0.25f, 1f),
            new Color(0.62f, 0.42f, 0.88f, 1f),
            new Color(0.30f, 0.75f, 0.62f, 1f),
            new Color(0.90f, 0.42f, 0.52f, 1f),
            new Color(0.75f, 0.75f, 0.32f, 1f),
        };

        private sealed class CellRef
        {
            public int Slot;
            public int Cell;
            public VisualElement Element = null!;
            public Label Label = null!;
        }

        private sealed class SlotRef
        {
            public int Slot;
            public VisualElement Element = null!;
            public VisualElement CellsRoot = null!;
            public Label LockLabel = null!;
            public readonly CellRef[] Cells = new CellRef[CharmRules.CELL_COUNT];
        }

        private sealed class BagRef
        {
            public int InventoryIndex;
        }

        private enum DragKind { None, BagBoard, BagCharm, SlotBoard, PlacedCharm }

        private UIDocument? _document;
        private VisualElement? _lastRoot;
        private VisualElement? _root;
        private VisualElement? _boardGrid;
        private VisualElement? _bagGrid;
        private VisualElement? _bagScroll;
        private VisualElement? _ghost;
        private Text? _totalsText;
        private Text? _statusText;
        private IconButton? _closeBtn;
        private readonly SlotRef[] _slots = new SlotRef[PlayerLevelRules.CHARM_BOARD_SLOT_COUNT];

        // 드래그 상태
        private DragKind _dragKind = DragKind.None;
        private int _dragSlot = -1;
        private int _dragIndex = -1;            // 가방 칸 번호 또는 박힌 참 번호
        private int _dragRotation;
        private int _dragPointerId = -1;
        private bool _dragActive;
        private Vector2 _dragStartPos;
        private Vector2 _lastPointerPos;
        private readonly List<CellRef> _highlighted = new List<CellRef>();
        private SlotRef? _highlightedSlot;
        private readonly List<int> _cellScratch = new List<int>();

        // 초기화 때 한 번 만드는 콜백 (런타임 재생성 금지)
        private EventCallback<PointerDownEvent> _cellPointerDownHandler = null!;
        private EventCallback<PointerEnterEvent> _cellPointerEnterHandler = null!;
        private EventCallback<PointerDownEvent> _bagPointerDownHandler = null!;
        private EventCallback<PointerEnterEvent> _bagPointerEnterHandler = null!;
        private EventCallback<PointerLeaveEvent> _pointerLeaveHandler = null!;
        private EventCallback<PointerMoveEvent> _rootPointerMoveHandler = null!;
        private EventCallback<PointerUpEvent> _rootPointerUpHandler = null!;
        private EventCallback<PointerDownEvent> _rootPointerDownHandler = null!;
        private EventCallback<PointerCaptureOutEvent> _rootCaptureOutHandler = null!;
        private Action<CharmBoardChangedMessage> _boardChangedHandler = null!;
        private Action<PlayerProgressChangedMessage> _progressChangedHandler = null!;
        private Action _closeClickedHandler = null!;

        public override void Initialize(string inName, bool isForm = false)
        {
            base.Initialize(inName, isForm);
            _cellPointerDownHandler = OnCellPointerDown;
            _cellPointerEnterHandler = OnCellPointerEnter;
            _bagPointerDownHandler = OnBagPointerDown;
            _bagPointerEnterHandler = OnBagPointerEnter;
            _pointerLeaveHandler = OnPointerLeave;
            _rootPointerMoveHandler = OnRootPointerMove;
            _rootPointerUpHandler = OnRootPointerUp;
            _rootPointerDownHandler = OnRootPointerDown;
            _rootCaptureOutHandler = OnRootCaptureOut;
            _boardChangedHandler = OnBoardChanged;
            _progressChangedHandler = OnProgressChanged;
            _closeClickedHandler = OnClickClose;

            _document = GetComponent<UIDocument>();
            if (_document == null)
            {
                Debug.LogError("[UICharmBoard] UIDocument 컴포넌트 없음 — prefab 확인 필요");
                return;
            }
            EnsureBound();
        }

        public override void OnOpen()
        {
            ClearControllerSelection();
            base.OnOpen();
            EnsureBound();
            AR.s.Message.Subscribe(_boardChangedHandler);
            AR.s.Message.Subscribe(_progressChangedHandler);
            SetStatus(string.Empty);
            RefreshAll();
        }

        public override void OnClose()
        {
            CancelDrag();
            AR.s.Message.Unsubscribe(_boardChangedHandler);
            AR.s.Message.Unsubscribe(_progressChangedHandler);
            AR.s.Tooltip.Hide();
            base.OnClose();
        }

        /// <summary>P키를 다시 누르면 닫는다 (끄는 중이면 무시).</summary>
        public override bool UpdateInput(Input.ArpgInput inInput)
        {
            if (_dragKind == DragKind.None && AR.s.UI.Input.KeyboardShortcutPressed("CharmBoard") == true)
            {
                Close();
                return true;
            }
            return base.UpdateInput(inInput);
        }

        private void Update()
        {
            // 끄는 중 R키로 90도 회전 (§8.6)
            if (_dragActive == true && (_dragKind == DragKind.BagCharm || _dragKind == DragKind.PlacedCharm)
                && Keyboard.current != null && Keyboard.current.rKey.wasPressedThisFrame == true)
            {
                RotateDrag();
            }
        }

        // ========== 바인딩 ==========

        private void EnsureBound()
        {
            VisualElement? root = _document?.rootVisualElement;
            if (root == null || root == _lastRoot)
                return;

            if (_closeBtn != null) _closeBtn.clicked -= _closeClickedHandler;
            _lastRoot = root;
            _root = root.Q<VisualElement>("root") ?? root;
            _boardGrid = root.Q<VisualElement>("board-grid");
            _bagGrid = root.Q<VisualElement>("bag-grid");
            _bagScroll = root.Q<VisualElement>("bag-scroll");
            _ghost = root.Q<VisualElement>("drag-ghost");
            _totalsText = root.Q<Text>("totals-text");
            _statusText = root.Q<Text>("status-text");
            _closeBtn = root.Q<IconButton>("close-btn");
            if (_closeBtn != null) _closeBtn.clicked += _closeClickedHandler;

            _root.RegisterCallback(_rootPointerMoveHandler);
            _root.RegisterCallback(_rootPointerUpHandler);
            _root.RegisterCallback(_rootPointerDownHandler, TrickleDown.TrickleDown);
            _root.RegisterCallback(_rootCaptureOutHandler);
            BuildBoardGrid();
        }

        /// <summary>판 자리 9칸 × 판 칸 9개를 한 번 만든다. 자리 번호는 3×3 왼쪽 위부터 행 순서 (PlayerLevelRules와 같다).</summary>
        private void BuildBoardGrid()
        {
            if (_boardGrid == null)
                return;

            _boardGrid.Clear();
            for (int row = 0; row < 3; row++)
            {
                VisualElement slotRow = new VisualElement();
                slotRow.AddToClassList("charm-slot-row");
                _boardGrid.Add(slotRow);
                for (int col = 0; col < 3; col++)
                {
                    int slotIndex = row * 3 + col;
                    SlotRef slot = new SlotRef { Slot = slotIndex };
                    slot.Element = new VisualElement { userData = slot };
                    slot.Element.AddToClassList("charm-slot");
                    slot.CellsRoot = new VisualElement();
                    slot.LockLabel = new Label();
                    slot.LockLabel.AddToClassList("charm-lock-label");
                    slot.Element.Add(slot.CellsRoot);
                    slot.Element.Add(slot.LockLabel);
                    slot.Element.RegisterCallback(_cellPointerEnterHandler);
                    slot.Element.RegisterCallback(_pointerLeaveHandler);

                    for (int cellRow = 0; cellRow < CharmRules.BOARD_SIZE; cellRow++)
                    {
                        VisualElement cellRowElement = new VisualElement();
                        cellRowElement.AddToClassList("charm-cell-row");
                        slot.CellsRoot.Add(cellRowElement);
                        for (int cellCol = 0; cellCol < CharmRules.BOARD_SIZE; cellCol++)
                        {
                            CellRef cell = new CellRef { Slot = slotIndex, Cell = cellRow * CharmRules.BOARD_SIZE + cellCol };
                            cell.Element = new VisualElement { userData = cell };
                            cell.Element.AddToClassList("charm-cell");
                            cell.Label = new Label { pickingMode = PickingMode.Ignore };
                            cell.Label.AddToClassList("charm-cell-label");
                            cell.Element.Add(cell.Label);
                            cell.Element.RegisterCallback(_cellPointerDownHandler);
                            cell.Element.RegisterCallback(_cellPointerEnterHandler);
                            cell.Element.RegisterCallback(_pointerLeaveHandler);
                            cellRowElement.Add(cell.Element);
                            slot.Cells[cell.Cell] = cell;
                        }
                    }

                    _slots[slotIndex] = slot;
                    slotRow.Add(slot.Element);
                }
            }
        }

        // ========== 갱신 ==========

        private void OnBoardChanged(CharmBoardChangedMessage message)
        {
            RefreshAll();
        }

        private void OnProgressChanged(PlayerProgressChangedMessage message)
        {
            if (message.LeveledUp == true)
                RefreshAll();
        }

        private void RefreshAll()
        {
            PlayerData? data = AR.s.Data.Player;
            if (data == null || _boardGrid == null)
                return;

            for (int s = 0; s < _slots.Length; s++)
                RefreshSlot(_slots[s], data);
            RefreshBag(data);
            if (_totalsText != null)
            {
                string totals = CharmTooltipText.BuildTotals(data._charmBoardSlots, data.Level);
                _totalsText.text = string.IsNullOrEmpty(totals) ? "끼운 판의 참 옵션이 없습니다" : totals;
            }
        }

        private void RefreshSlot(SlotRef? slot, PlayerData data)
        {
            if (slot == null)
                return;

            bool unlocked = PlayerLevelRules.IsCharmBoardSlotUnlocked(slot.Slot, data.Level);
            ItemData? board = data._charmBoardSlots[slot.Slot];
            slot.Element.EnableInClassList("charm-slot-locked", unlocked == false);
            slot.Element.EnableInClassList("charm-slot-empty", unlocked == true && board == null);
            slot.CellsRoot.style.display = unlocked == true ? DisplayStyle.Flex : DisplayStyle.None;
            slot.LockLabel.style.display = unlocked == true ? DisplayStyle.None : DisplayStyle.Flex;
            slot.LockLabel.text = $"잠김\n{PlayerLevelRules.GetCharmBoardSlotUnlockLevel(slot.Slot)}레벨";

            CharmBoardData? boardData = board?.CharmBoard;
            for (int c = 0; c < slot.Cells.Length; c++)
            {
                CellRef cell = slot.Cells[c];
                bool blocked = boardData != null && (boardData.BlockedMask & (1 << c)) != 0;
                int placement = boardData != null ? CharmRules.FindPlacementAtCell(boardData, c, _cellScratch) : -1;
                cell.Element.EnableInClassList("charm-cell-blocked", blocked);
                cell.Element.style.backgroundColor = placement >= 0
                    ? new StyleColor(CHARM_COLORS[placement % CHARM_COLORS.Length])
                    : new StyleColor(StyleKeyword.Null);
                cell.Element.style.opacity = boardData == null ? 0.35f : 1f;
                cell.Label.text = blocked == true ? "X" : string.Empty;
            }
        }

        private void RefreshBag(PlayerData data)
        {
            if (_bagGrid == null)
                return;

            _bagGrid.Clear();
            for (int i = 0; i < data._inventory.Count; i++)
            {
                ItemData? item = data._inventory[i];
                if (item == null || (item.CharmBoard == null && item.Charm == null))
                    continue;

                VisualElement element = new VisualElement { userData = new BagRef { InventoryIndex = i } };
                element.AddToClassList("charm-bag-item");
                if (item.CharmBoard != null)
                    element.Add(BuildMiniBoard(item.CharmBoard));
                else
                    element.Add(BuildMiniShape(item.Charm!.Shape, 0, 10f));
                if (item.Charm != null)
                {
                    Label label = new Label($"Lv{item.Charm.ItemLevel}") { pickingMode = PickingMode.Ignore };
                    label.AddToClassList("charm-bag-label");
                    element.Add(label);
                }
                element.RegisterCallback(_bagPointerDownHandler);
                element.RegisterCallback(_bagPointerEnterHandler);
                element.RegisterCallback(_pointerLeaveHandler);
                _bagGrid.Add(element);
            }

            if (_bagGrid.childCount == 0)
            {
                Label empty = new Label("가방에 판이나 참이 없습니다") { pickingMode = PickingMode.Ignore };
                empty.AddToClassList("charm-bag-label");
                _bagGrid.Add(empty);
            }
        }

        /// <summary>가방의 판 아이콘: 막힌 칸과 박힌 참이 보이는 작은 3×3 그림 (§8.4).</summary>
        private VisualElement BuildMiniBoard(CharmBoardData board)
        {
            int occupied = CharmRules.GetOccupiedMask(board, -1, _cellScratch);
            VisualElement grid = new VisualElement { pickingMode = PickingMode.Ignore };
            grid.AddToClassList("charm-mini-grid");
            for (int row = 0; row < CharmRules.BOARD_SIZE; row++)
            {
                VisualElement rowElement = new VisualElement { pickingMode = PickingMode.Ignore };
                rowElement.AddToClassList("charm-mini-row");
                for (int col = 0; col < CharmRules.BOARD_SIZE; col++)
                {
                    int bit = 1 << (row * CharmRules.BOARD_SIZE + col);
                    VisualElement cell = new VisualElement { pickingMode = PickingMode.Ignore };
                    cell.AddToClassList("charm-mini-cell");
                    cell.EnableInClassList("charm-mini-cell-blocked", (board.BlockedMask & bit) != 0);
                    cell.EnableInClassList("charm-mini-cell-charm", (occupied & bit) != 0);
                    rowElement.Add(cell);
                }
                grid.Add(rowElement);
            }
            return grid;
        }

        /// <summary>참 모양 그림 (회전 반영). 가방 아이콘과 끄는 그림자가 같이 쓴다.</summary>
        private VisualElement BuildMiniShape(GlobalEnum.CharmShape shape, int rotation, float cellSize)
        {
            CharmRules.TryGetCells(shape, rotation, 0, _cellScratch);
            int maxRow = 0;
            int maxCol = 0;
            for (int i = 0; i < _cellScratch.Count; i++)
            {
                maxRow = Mathf.Max(maxRow, _cellScratch[i] / CharmRules.BOARD_SIZE);
                maxCol = Mathf.Max(maxCol, _cellScratch[i] % CharmRules.BOARD_SIZE);
            }

            VisualElement grid = new VisualElement { pickingMode = PickingMode.Ignore };
            grid.AddToClassList("charm-mini-grid");
            for (int row = 0; row <= maxRow; row++)
            {
                VisualElement rowElement = new VisualElement { pickingMode = PickingMode.Ignore };
                rowElement.AddToClassList("charm-mini-row");
                for (int col = 0; col <= maxCol; col++)
                {
                    VisualElement cell = new VisualElement { pickingMode = PickingMode.Ignore };
                    cell.AddToClassList("charm-mini-cell");
                    cell.style.width = cellSize;
                    cell.style.height = cellSize;
                    bool filled = _cellScratch.Contains(row * CharmRules.BOARD_SIZE + col);
                    cell.EnableInClassList("charm-mini-cell-charm", filled);
                    cell.EnableInClassList("charm-mini-cell-off", filled == false);
                    rowElement.Add(cell);
                }
                grid.Add(rowElement);
            }
            return grid;
        }

        // ========== 툴팁 ==========

        private void OnCellPointerEnter(PointerEnterEvent evt)
        {
            if (_dragActive == true || evt.currentTarget is not VisualElement element)
                return;

            PlayerData? data = AR.s.Data.Player;
            if (data == null)
                return;

            int slotIndex = element.userData is CellRef cellRef ? cellRef.Slot
                : element.userData is SlotRef slotRef ? slotRef.Slot : -1;
            if (slotIndex < 0)
                return;

            Rect rect = GetScreenRect(element);
            if (PlayerLevelRules.IsCharmBoardSlotUnlocked(slotIndex, data.Level) == false)
            {
                AR.s.Tooltip.ShowText("잠긴 자리", $"{PlayerLevelRules.GetCharmBoardSlotUnlockLevel(slotIndex)}레벨에 열립니다", rect);
                return;
            }

            ItemData? board = data._charmBoardSlots[slotIndex];
            if (board?.CharmBoard == null)
            {
                AR.s.Tooltip.ShowText("빈 자리", "가방의 판을 끌어다 놓으세요", rect);
                return;
            }

            // 칸 위면 박힌 참, 아니면 판 툴팁 (자리 테두리의 진입은 칸 진입이 이어서 덮는다)
            if (element.userData is CellRef cell)
            {
                int placement = CharmRules.FindPlacementAtCell(board.CharmBoard, cell.Cell, _cellScratch);
                if (placement >= 0)
                {
                    ItemData charm = board.CharmBoard.Charms[placement].Charm;
                    AR.s.Tooltip.ShowCharm(charm, $"참 판 자리 {slotIndex + 1}의 판에 박혀 있음", rect);
                    return;
                }
            }
            AR.s.Tooltip.Show(board, rect);
        }

        private void OnBagPointerEnter(PointerEnterEvent evt)
        {
            if (_dragActive == true || evt.currentTarget is not VisualElement element || element.userData is not BagRef bag)
                return;

            ItemData? item = AR.s.Player.Inventory.GetItemBySlotIndex(bag.InventoryIndex);
            if (item != null)
                AR.s.Tooltip.Show(item, GetScreenRect(element));
        }

        private void OnPointerLeave(PointerLeaveEvent evt)
        {
            if (_dragActive == false)
                AR.s.Tooltip.Hide();
        }

        // ========== 드래그 ==========

        private void OnCellPointerDown(PointerDownEvent evt)
        {
            if (evt.currentTarget is not VisualElement element || element.userData is not CellRef cell)
                return;
            if (_dragActive == true)
                return;

            PlayerData? data = AR.s.Data.Player;
            ItemData? board = data != null ? data._charmBoardSlots[cell.Slot] : null;
            if (board?.CharmBoard == null)
                return;

            int placement = CharmRules.FindPlacementAtCell(board.CharmBoard, cell.Cell, _cellScratch);
            // 박힌 참 우클릭: 가방으로 (§8.6)
            if (evt.button == 1)
            {
                if (placement >= 0)
                {
                    if (AR.s.Player.TryRemoveCharm(cell.Slot, placement, out string failReason) == false)
                        SetStatus(failReason);
                    else
                        SetStatus(string.Empty);
                }
                evt.StopPropagation();
                return;
            }
            if (evt.button != 0)
                return;

            if (placement >= 0)
            {
                CharmPlacement placed = board.CharmBoard.Charms[placement];
                BeginPendingDrag(DragKind.PlacedCharm, cell.Slot, placement, placed.Rotation, evt);
            }
            else
            {
                BeginPendingDrag(DragKind.SlotBoard, cell.Slot, -1, 0, evt);
            }
        }

        private void OnBagPointerDown(PointerDownEvent evt)
        {
            if (evt.button != 0 || _dragActive == true)
                return;
            if (evt.currentTarget is not VisualElement element || element.userData is not BagRef bag)
                return;

            ItemData? item = AR.s.Player.Inventory.GetItemBySlotIndex(bag.InventoryIndex);
            if (item?.CharmBoard != null)
                BeginPendingDrag(DragKind.BagBoard, -1, bag.InventoryIndex, 0, evt);
            else if (item?.Charm != null)
                BeginPendingDrag(DragKind.BagCharm, -1, bag.InventoryIndex, 0, evt);
        }

        private void BeginPendingDrag(DragKind kind, int slot, int index, int rotation, PointerDownEvent evt)
        {
            _dragKind = kind;
            _dragSlot = slot;
            _dragIndex = index;
            _dragRotation = rotation;
            _dragPointerId = evt.pointerId;
            _dragStartPos = evt.position;
            _lastPointerPos = evt.position;
            _dragActive = false;
            _root?.CapturePointer(evt.pointerId);
        }

        /// <summary>끄는 중 우클릭은 회전 (§8.6).</summary>
        private void OnRootPointerDown(PointerDownEvent evt)
        {
            if (_dragActive == false || evt.button != 1)
                return;
            if (_dragKind == DragKind.BagCharm || _dragKind == DragKind.PlacedCharm)
                RotateDrag();
            evt.StopPropagation();
        }

        private void OnRootPointerMove(PointerMoveEvent evt)
        {
            if (_dragKind == DragKind.None || evt.pointerId != _dragPointerId)
                return;

            _lastPointerPos = evt.position;
            if (_dragActive == false)
            {
                if (((Vector2)evt.position - _dragStartPos).sqrMagnitude < DRAG_THRESHOLD_SQ)
                    return;
                _dragActive = true;
                AR.s.Tooltip.Hide();
                BuildGhost();
            }
            UpdateDragVisuals();
        }

        private void OnRootPointerUp(PointerUpEvent evt)
        {
            if (_dragKind == DragKind.None || evt.pointerId != _dragPointerId || evt.button != 0)
                return;

            if (_dragActive == true)
                PerformDrop(evt.position);
            CancelDrag();
        }

        private void OnRootCaptureOut(PointerCaptureOutEvent evt)
        {
            if (_dragKind != DragKind.None)
                CancelDrag();
        }

        private void RotateDrag()
        {
            _dragRotation = (_dragRotation + 1) % CharmRules.ROTATION_COUNT;
            BuildGhost();
            UpdateDragVisuals();
        }

        private void CancelDrag()
        {
            if (_root != null && _dragPointerId >= 0 && _root.HasPointerCapture(_dragPointerId) == true)
                _root.ReleasePointer(_dragPointerId);
            _dragKind = DragKind.None;
            _dragActive = false;
            _dragPointerId = -1;
            _dragSlot = -1;
            _dragIndex = -1;
            ClearHighlight();
            if (_ghost != null)
            {
                _ghost.Clear();
                _ghost.style.display = DisplayStyle.None;
            }
        }

        private void BuildGhost()
        {
            if (_ghost == null)
                return;

            _ghost.Clear();
            if (TryGetDraggedCharmShape(out GlobalEnum.CharmShape shape) == true)
            {
                _ghost.Add(BuildMiniShape(shape, _dragRotation, GHOST_CELL_SIZE));
            }
            else
            {
                CharmBoardData? board = GetDraggedBoard();
                if (board != null)
                    _ghost.Add(BuildMiniBoard(board));
            }
            _ghost.style.display = DisplayStyle.Flex;
        }

        private void UpdateDragVisuals()
        {
            if (_ghost != null && _root != null)
            {
                Vector2 local = _root.WorldToLocal(_lastPointerPos);
                _ghost.style.left = local.x - GHOST_CELL_SIZE * 0.5f;
                _ghost.style.top = local.y - GHOST_CELL_SIZE * 0.5f;
            }

            ClearHighlight();
            object? target = PickTarget(_lastPointerPos, out VisualElement? _);
            if (TryGetDraggedCharmShape(out GlobalEnum.CharmShape shape) == true)
            {
                if (target is CellRef cell)
                    HighlightPlacement(cell, shape);
            }
            else if (_dragKind == DragKind.BagBoard)
            {
                SlotRef? slot = target is CellRef c ? _slots[c.Slot] : target as SlotRef;
                if (slot != null && AR.s.Player.IsCharmBoardSlotUnlocked(slot.Slot) == true)
                {
                    slot.Element.AddToClassList("charm-slot-drop");
                    _highlightedSlot = slot;
                }
            }
        }

        /// <summary>놓을 수 있으면 초록, 막힌 칸·다른 참과 겹치거나 판 경계를 넘으면 빨강 (§8.6).</summary>
        private void HighlightPlacement(CellRef cell, GlobalEnum.CharmShape shape)
        {
            PlayerData? data = AR.s.Data.Player;
            ItemData? board = data != null ? data._charmBoardSlots[cell.Slot] : null;
            if (board?.CharmBoard == null || AR.s.Player.IsCharmBoardSlotUnlocked(cell.Slot) == false)
                return;

            int ignore = _dragKind == DragKind.PlacedCharm && _dragSlot == cell.Slot ? _dragIndex : -1;
            bool ok = CharmRules.CanPlace(board.CharmBoard, shape, _dragRotation, cell.Cell, ignore, _cellScratch);
            if (CharmRules.TryGetCells(shape, _dragRotation, cell.Cell, _cellScratch) == false)
            {
                Mark(_slots[cell.Slot].Cells[cell.Cell], false);
                return;
            }
            for (int i = 0; i < _cellScratch.Count; i++)
                Mark(_slots[cell.Slot].Cells[_cellScratch[i]], ok);
        }

        private void Mark(CellRef cell, bool ok)
        {
            cell.Element.AddToClassList(ok == true ? "charm-cell-ok" : "charm-cell-bad");
            _highlighted.Add(cell);
        }

        private void ClearHighlight()
        {
            for (int i = 0; i < _highlighted.Count; i++)
            {
                _highlighted[i].Element.RemoveFromClassList("charm-cell-ok");
                _highlighted[i].Element.RemoveFromClassList("charm-cell-bad");
            }
            _highlighted.Clear();
            _highlightedSlot?.Element.RemoveFromClassList("charm-slot-drop");
            _highlightedSlot = null;
        }

        private void PerformDrop(Vector2 position)
        {
            object? target = PickTarget(position, out VisualElement? _);
            bool overBag = target is BagRef || ReferenceEquals(target, _bagScroll);
            string failReason = string.Empty;
            bool success = false;

            switch (_dragKind)
            {
                case DragKind.BagCharm:
                    if (target is CellRef cell)
                        success = AR.s.Player.TryPlaceCharm(cell.Slot, _dragIndex, cell.Cell, _dragRotation, out failReason);
                    else
                        return;
                    break;

                case DragKind.PlacedCharm:
                    if (target is CellRef moveCell)
                        success = AR.s.Player.TryMoveCharm(_dragSlot, _dragIndex, moveCell.Slot, moveCell.Cell, _dragRotation, out failReason);
                    else if (overBag == true)
                        success = AR.s.Player.TryRemoveCharm(_dragSlot, _dragIndex, out failReason);
                    else
                        return;
                    break;

                case DragKind.BagBoard:
                    SlotRef? slot = target is CellRef boardCell ? _slots[boardCell.Slot] : target as SlotRef;
                    if (slot == null)
                        return;
                    success = AR.s.Player.TryEquipCharmBoard(slot.Slot, _dragIndex, out failReason);
                    break;

                case DragKind.SlotBoard:
                    if (overBag == false)
                        return;
                    success = AR.s.Player.TryUnequipCharmBoard(_dragSlot, out failReason);
                    break;
            }

            SetStatus(success == true ? string.Empty : failReason);
        }

        /// <summary>위치 아래의 대상 (칸·자리·가방 항목·가방 칸). 끄는 그림자는 포인터를 받지 않는다.</summary>
        private object? PickTarget(Vector2 position, out VisualElement? picked)
        {
            picked = _root?.panel?.Pick(position);
            VisualElement? current = picked;
            while (current != null)
            {
                if (current.userData is CellRef || current.userData is SlotRef || current.userData is BagRef)
                    return current.userData;
                if (ReferenceEquals(current, _bagScroll))
                    return _bagScroll;
                current = current.parent;
            }
            return null;
        }

        private bool TryGetDraggedCharmShape(out GlobalEnum.CharmShape shape)
        {
            shape = GlobalEnum.CharmShape.Single;
            PlayerData? data = AR.s.Data.Player;
            if (data == null)
                return false;

            if (_dragKind == DragKind.BagCharm)
            {
                ItemData? item = AR.s.Player.Inventory.GetItemBySlotIndex(_dragIndex);
                if (item?.Charm == null)
                    return false;
                shape = item.Charm.Shape;
                return true;
            }
            if (_dragKind == DragKind.PlacedCharm)
            {
                CharmBoardData? board = data._charmBoardSlots[_dragSlot]?.CharmBoard;
                if (board == null || _dragIndex < 0 || _dragIndex >= board.Charms.Count)
                    return false;
                CharmData? charm = board.Charms[_dragIndex].Charm?.Charm;
                if (charm == null)
                    return false;
                shape = charm.Shape;
                return true;
            }
            return false;
        }

        private CharmBoardData? GetDraggedBoard()
        {
            PlayerData? data = AR.s.Data.Player;
            if (data == null)
                return null;
            if (_dragKind == DragKind.BagBoard)
                return AR.s.Player.Inventory.GetItemBySlotIndex(_dragIndex)?.CharmBoard;
            if (_dragKind == DragKind.SlotBoard && _dragSlot >= 0)
                return data._charmBoardSlots[_dragSlot]?.CharmBoard;
            return null;
        }

        // ========== 기타 ==========

        private void OnClickClose()
        {
            Close();
        }

        private void SetStatus(string text)
        {
            if (_statusText != null)
                _statusText.text = text;
        }

        private static Rect GetScreenRect(VisualElement element)
        {
            IPanel? panel = element.panel;
            if (panel == null) return Rect.zero;
            float panelW = panel.visualTree.resolvedStyle.width;
            float panelH = panel.visualTree.resolvedStyle.height;
            if (panelW <= 0f || panelH <= 0f) return Rect.zero;

            float scaleX = Screen.width / panelW;
            float scaleY = Screen.height / panelH;
            Rect bound = element.worldBound;
            return new Rect(bound.xMin * scaleX, Screen.height - bound.yMax * scaleY,
                bound.width * scaleX, bound.height * scaleY);
        }
    }
}
