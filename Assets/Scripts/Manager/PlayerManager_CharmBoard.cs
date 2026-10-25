#nullable enable
using System.Collections.Generic;
using ARPG.Component;
using ARPG.Data;
using ARPG.Item;
using ARPG.Message;
using ARPG.Tables;
using ARPG.Utility;
using GE = GlobalEnum;

namespace ARPG.Manager
{
    /// <summary>
    /// 참 판 자리 (PLAYER_LEVEL_CHARM_DESIGN.md §6·§8.5·§8.6). 판 자리 9칸은 PlayerData가 갖고, 이 파일은
    /// 판 끼우기·빼기, 자리에 끼운 판의 참 넣기·옮기기·빼기, 참 옵션의 스탯 반영을 맡는다.
    /// 참은 자리에 끼운 판에만 넣고 뺀다 — 가방의 판은 고치지 않는다 (LV21).
    /// </summary>
    public partial class PlayerManager
    {
        public const string FAIL_LOCKED = "잠긴 자리입니다";
        public const string FAIL_BAG_FULL = "가방이 가득 찼습니다";
        public const string FAIL_CANNOT_PLACE = "놓을 수 없는 자리입니다";
        public const string FAIL_INVALID = "올바른 대상이 아닙니다";

        // 사용 직전 비우는 칸 번호 스크래치 (상태 없음)
        private readonly List<int> _charmCellScratch = new List<int>();

        public ItemData? GetCharmBoardInSlot(int slotIndex)
        {
            PlayerData? data = AR.s.Data.Player;
            if (data == null || slotIndex < 0 || slotIndex >= data._charmBoardSlots.Length)
                return null;
            return data._charmBoardSlots[slotIndex];
        }

        public bool IsCharmBoardSlotUnlocked(int slotIndex)
        {
            PlayerData? data = AR.s.Data.Player;
            return data != null && PlayerLevelRules.IsCharmBoardSlotUnlocked(slotIndex, data.Level);
        }

        /// <summary>가방의 판을 열린 자리에 끼운다. 자리에 판이 있으면 서로 바꾼다 — 빠진 판은 그 가방 칸으로 간다 (§8.5).</summary>
        public bool TryEquipCharmBoard(int slotIndex, int inventorySlotIndex, out string failReason)
        {
            failReason = FAIL_INVALID;
            PlayerData? data = AR.s.Data.Player;
            if (data == null || IsValidCharmBoardSlot(data, slotIndex) == false)
                return false;
            if (IsCharmBoardSlotUnlocked(slotIndex) == false)
            {
                failReason = FAIL_LOCKED;
                return false;
            }
            if (inventorySlotIndex < 0 || inventorySlotIndex >= data._inventory.Count)
                return false;

            ItemData? board = data._inventory[inventorySlotIndex];
            if (board?.CharmBoard == null)
                return false;

            data._inventory[inventorySlotIndex] = data._charmBoardSlots[slotIndex];
            data._charmBoardSlots[slotIndex] = board;
            OnCharmBoardsChanged(data, slotIndex);
            return true;
        }

        /// <summary>자리의 판을 박힌 참과 함께 가방 빈칸으로 뺀다. 가방이 꽉 차면 막는다 (§8.5).</summary>
        public bool TryUnequipCharmBoard(int slotIndex, out string failReason)
        {
            failReason = FAIL_INVALID;
            PlayerData? data = AR.s.Data.Player;
            if (data == null || IsValidCharmBoardSlot(data, slotIndex) == false)
                return false;

            ItemData? board = data._charmBoardSlots[slotIndex];
            if (board == null)
                return false;

            int emptyIndex = FindEmptyInventorySlot(data);
            if (emptyIndex < 0)
            {
                failReason = FAIL_BAG_FULL;
                return false;
            }

            data._inventory[emptyIndex] = board;
            data._charmBoardSlots[slotIndex] = null;
            OnCharmBoardsChanged(data, slotIndex);
            return true;
        }

        /// <summary>가방의 참을 자리에 끼운 판에 넣는다. 판 경계·막힌 칸·다른 참과 겹치면 막는다 (§6.4).</summary>
        public bool TryPlaceCharm(int slotIndex, int inventorySlotIndex, int anchorCell, int rotation, out string failReason)
        {
            failReason = FAIL_INVALID;
            PlayerData? data = AR.s.Data.Player;
            ItemData? board = GetEditableBoard(data, slotIndex, ref failReason);
            if (data == null || board?.CharmBoard == null)
                return false;
            if (inventorySlotIndex < 0 || inventorySlotIndex >= data._inventory.Count)
                return false;

            ItemData? charm = data._inventory[inventorySlotIndex];
            if (charm?.Charm == null)
                return false;

            if (CharmRules.CanPlace(board.CharmBoard, charm.Charm.Shape, rotation, anchorCell, -1, _charmCellScratch) == false)
            {
                failReason = FAIL_CANNOT_PLACE;
                return false;
            }

            board.CharmBoard.Charms.Add(new CharmPlacement { Charm = charm, AnchorCell = anchorCell, Rotation = NormalizeRotation(rotation) });
            data._inventory[inventorySlotIndex] = null;
            OnCharmBoardsChanged(data, slotIndex);
            return true;
        }

        /// <summary>박힌 참을 같은 판 안이나 다른 판으로 옮긴다 (§8.6).</summary>
        public bool TryMoveCharm(int fromSlotIndex, int placementIndex, int toSlotIndex, int anchorCell, int rotation,
            out string failReason)
        {
            failReason = FAIL_INVALID;
            PlayerData? data = AR.s.Data.Player;
            ItemData? fromBoard = GetEditableBoard(data, fromSlotIndex, ref failReason);
            ItemData? toBoard = GetEditableBoard(data, toSlotIndex, ref failReason);
            if (data == null || fromBoard?.CharmBoard == null || toBoard?.CharmBoard == null)
                return false;
            if (placementIndex < 0 || placementIndex >= fromBoard.CharmBoard.Charms.Count)
                return false;

            CharmPlacement placement = fromBoard.CharmBoard.Charms[placementIndex];
            if (placement.Charm?.Charm == null)
                return false;

            int ignoreIndex = ReferenceEquals(fromBoard, toBoard) ? placementIndex : -1;
            if (CharmRules.CanPlace(toBoard.CharmBoard, placement.Charm.Charm.Shape, rotation, anchorCell, ignoreIndex,
                    _charmCellScratch) == false)
            {
                failReason = FAIL_CANNOT_PLACE;
                return false;
            }

            fromBoard.CharmBoard.Charms.RemoveAt(placementIndex);
            placement.AnchorCell = anchorCell;
            placement.Rotation = NormalizeRotation(rotation);
            toBoard.CharmBoard.Charms.Add(placement);
            OnCharmBoardsChanged(data, fromSlotIndex == toSlotIndex ? fromSlotIndex : -1);
            return true;
        }

        /// <summary>박힌 참을 가방 빈칸으로 되돌린다. 가방이 꽉 차면 막는다 (§8.6).</summary>
        public bool TryRemoveCharm(int slotIndex, int placementIndex, out string failReason)
        {
            failReason = FAIL_INVALID;
            PlayerData? data = AR.s.Data.Player;
            ItemData? board = GetEditableBoard(data, slotIndex, ref failReason);
            if (data == null || board?.CharmBoard == null)
                return false;
            if (placementIndex < 0 || placementIndex >= board.CharmBoard.Charms.Count)
                return false;

            int emptyIndex = FindEmptyInventorySlot(data);
            if (emptyIndex < 0)
            {
                failReason = FAIL_BAG_FULL;
                return false;
            }

            CharmPlacement placement = board.CharmBoard.Charms[placementIndex];
            board.CharmBoard.Charms.RemoveAt(placementIndex);
            data._inventory[emptyIndex] = placement.Charm;
            OnCharmBoardsChanged(data, slotIndex);
            return true;
        }

        /// <summary>
        /// 열린 자리에 끼운 판의 참 옵션을 스탯 보정(출처 CharmBoard, SourceId = 판 ItemInstanceId)으로 다시 건다 (§6.1·§9).
        /// 생성 직후·판·참 변경·레벨업(자리 열림) 때 부른다.
        /// </summary>
        public void ApplyCharmBoardModifiersToEntity(int entityId)
        {
            PlayerData? data = AR.s.Data.Player;
            if (data == null)
                return;

            DataManager dm = AR.s.Data;
            dm.RemoveModifiersBySourceType(entityId, StatModifierSource.CharmBoard);
            for (int slot = 0; slot < data._charmBoardSlots.Length; slot++)
            {
                ItemData? board = data._charmBoardSlots[slot];
                if (board?.CharmBoard == null || PlayerLevelRules.IsCharmBoardSlotUnlocked(slot, data.Level) == false)
                    continue;

                List<CharmPlacement> charms = board.CharmBoard.Charms;
                for (int i = 0; i < charms.Count; i++)
                {
                    CharmData? charm = charms[i].Charm?.Charm;
                    if (charm == null)
                        continue;
                    for (int m = 0; m < charm.Mods.Count; m++)
                    {
                        ModInstance mod = charm.Mods[m];
                        if (mod.Table == null)
                            mod.OnLoadCompleted();
                        if (mod.Table == null || mod.Value1 <= 0)
                            continue;
                        // 장비 옵션과 같은 적용: 고정 수치는 더하기, % 증가(치명타 확률 증가)는 증가율 묶음 (EquipHelper)
                        StatModifierType type;
                        if (mod.Table.EffectType == GE.ModEffectType.FlatStat)
                            type = StatModifierType.Add;
                        else if (mod.Table.EffectType == GE.ModEffectType.IncreasedStat)
                            type = StatModifierType.Multiply;
                        else
                            continue;
                        dm.AddStatModifier(entityId, StatModifierSource.CharmBoard, board.ItemInstanceId,
                            mod.Table.TargetStat, type, mod.Value1, tags: mod.Table.Tags);
                    }
                }
            }
            AR.s.Component.AddComponent(entityId, new StatDirtyTag());
        }

        private ItemData? GetEditableBoard(PlayerData? data, int slotIndex, ref string failReason)
        {
            if (data == null || IsValidCharmBoardSlot(data, slotIndex) == false)
                return null;
            if (IsCharmBoardSlotUnlocked(slotIndex) == false)
            {
                failReason = FAIL_LOCKED;
                return null;
            }
            return data._charmBoardSlots[slotIndex];
        }

        private void OnCharmBoardsChanged(PlayerData data, int slotIndex)
        {
            AR.s.Data.MarkPlayerSaveDirty(data, PlayerSaveGroup.Inventory | PlayerSaveGroup.CharmBoards);
            if (_myPlayer != null)
                ApplyCharmBoardModifiersToEntity(_myPlayer.EntityId);
            AR.s.Message.Broadcast(new CharmBoardChangedMessage { SlotIndex = slotIndex });
        }

        private static bool IsValidCharmBoardSlot(PlayerData data, int slotIndex)
        {
            return slotIndex >= 0 && slotIndex < data._charmBoardSlots.Length;
        }

        private static int FindEmptyInventorySlot(PlayerData data)
        {
            for (int i = 0; i < data._inventory.Count; i++)
            {
                if (data._inventory[i] == null)
                    return i;
            }
            return -1;
        }

        private static int NormalizeRotation(int rotation)
        {
            return ((rotation % CharmRules.ROTATION_COUNT) + CharmRules.ROTATION_COUNT) % CharmRules.ROTATION_COUNT;
        }
    }
}
