#nullable enable
using System.Collections.Generic;
using ARPG.Data;
using ARPG.Factory;
using ARPG.Message;
using ARPG.Tables;
using UnityEngine;

namespace ARPG.Manager
{
    /// <summary>
    /// 플레이어 스킬 슬롯(스킬북) 장착/해제 매니저 (SKILLBOOK_DESIGN.md §4).
    /// 인벤토리 ↔ 스킬북 슬롯 간 이동 + 슬롯에 대응되는 ECS 스킬 엔티티 갱신을 책임진다.
    ///
    /// 데이터 소스: PlayerData._skillBookSlots / PlayerData._inventory
    /// ECS 동기화: EntityFactory.CreateSkill / RemoveSkill (결정적 ID 기반)
    /// </summary>
    public class PlayerSkillManager
    {
        // ========== 조회 ==========

        public ItemData? GetEquippedBook(int slotIndex)
        {
            if (IsValidSlot(slotIndex) == false) return null;
            return AR.s?.Data?.Player?._skillBookSlots[slotIndex];
        }

        /// <summary>슬롯에 장착된 스킬 ID. 비어 있으면 0.</summary>
        public int GetEquippedSkillId(int slotIndex)
        {
            ItemData? book = GetEquippedBook(slotIndex);
            return book?.SkillBook?.SkillId ?? 0;
        }

        public int GetUsedPageCost(ItemData? book)
        {
            if (book?.SkillBook?.SocketedPages == null)
                return 0;

            int total = 0;
            List<ItemData> pages = book.SkillBook.SocketedPages;
            for (int i = 0; i < pages.Count; i++)
            {
                SkillEffectTable? effect = pages[i].SkillPage?.Table;
                if (effect != null)
                    total += Mathf.Max(0, effect.PageCost);
            }

            return total;
        }

        public int GetPageCapacity(ItemData? book)
        {
            if (book?.Table == null || book.SkillBook == null)
                return 0;

            SkillBookTable? pageTable = AR.s.Data?.GetSkillBook(book.Table.Tier);
            if (pageTable == null)
                return 0;

            return pageTable.PageCapacity + book.SkillBook.PageCapacityBonus;
        }

        /// <summary>
        /// 책의 유효 페이지 슬롯 수 = 등급 기본값 + 인스턴스 roll 보너스(PageSlotsBonus).
        /// v1에서는 PageSlotsBonus가 0 고정이지만, 필드는 미리 노출하여 후속 페이즈의 인스턴스 roll에 대비한다.
        /// </summary>
        public int GetPageSlots(ItemData? book)
        {
            if (book?.Table == null || book.SkillBook == null)
                return 0;

            SkillBookTable? pageTable = AR.s.Data?.GetSkillBook(book.Table.Tier);
            if (pageTable == null)
                return 0;

            return pageTable.PageSlots + book.SkillBook.PageSlotsBonus;
        }

        public bool CanSocketSkillPage(ItemData? book, int skillEffectId, out string reason)
        {
            reason = string.Empty;

            if (IsValidSkillBook(book) == false)
            {
                reason = "유효한 스킬북이 아님";
                return false;
            }

            SkillEffectTable? effect = AR.s.Data?.GetSkillEffect(skillEffectId);
            if (effect == null)
            {
                reason = "스킬 페이지 효과 없음";
                return false;
            }
            if (effect.PageCost <= 0)
            {
                reason = "페이지 비용 없음";
                return false;
            }

            ItemData validBook = book!;
            SkillBookData skillBook = validBook.SkillBook!;
            ItemTable bookTable = validBook.Table!;

            SkillBookTable? pageTable = AR.s.Data?.GetSkillBook(bookTable.Tier);
            if (pageTable == null || pageTable.PageSlots <= 0 || pageTable.PageCapacity <= 0)
            {
                reason = "스킬북 페이지 설정 없음";
                return false;
            }

            skillBook.SocketedPages ??= new List<ItemData>();
            List<ItemData> socketedPages = skillBook.SocketedPages;
            int effectiveSlots = pageTable.PageSlots + skillBook.PageSlotsBonus;
            if (socketedPages.Count >= effectiveSlots)
            {
                reason = "빈 페이지 슬롯 없음";
                return false;
            }

            for (int i = 0; i < socketedPages.Count; i++)
            {
                if (socketedPages[i].SkillPage?.SkillEffectId == skillEffectId)
                {
                    reason = "이미 장착된 페이지";
                    return false;
                }
            }

            int used = GetUsedPageCost(book);
            int capacity = pageTable.PageCapacity + skillBook.PageCapacityBonus;
            if (used + effect.PageCost > capacity)
            {
                reason = "페이지 용량 부족";
                return false;
            }

            return true;
        }

        // ========== 장착 ==========

        /// <summary>
        /// 인벤토리 슬롯의 스킬북을 스킬 슬롯에 장착. 기존 책이 있으면 해당 인벤토리 슬롯으로 스왑.
        /// 성공 시 ECS 스킬 엔티티 재생성 + 세이브.
        /// </summary>
        public bool EquipSkillBook(int slotIndex, int inventorySlotIndex)
        {
            if (IsValidSlot(slotIndex) == false)
            {
                Debug.LogWarning($"[PlayerSkillManager] EquipSkillBook - Invalid slotIndex({slotIndex})");
                return false;
            }

            PlayerData? data = AR.s?.Data?.Player;
            if (data == null)
            {
                Debug.LogError("[PlayerSkillManager] EquipSkillBook - PlayerData not available");
                return false;
            }

            if (inventorySlotIndex < 0 || inventorySlotIndex >= data._inventory.Count)
            {
                Debug.LogWarning($"[PlayerSkillManager] EquipSkillBook - Invalid inventorySlotIndex({inventorySlotIndex})");
                return false;
            }

            ItemData? newBook = data._inventory[inventorySlotIndex];
            if (IsValidSkillBook(newBook) == false)
            {
                Debug.LogWarning($"[PlayerSkillManager] EquipSkillBook - Inventory slot({inventorySlotIndex}) is not a valid SkillBook");
                return false;
            }

            int newSkillId = newBook!.SkillBook!.SkillId;
            // SKILLBOOK_DESIGN.md §2.5: 같은 SkillId 다중 슬롯 장착 허용 (유저의 빌드 선택). 검증 안 함.

            // 스왑: 기존 슬롯 책 ↔ 인벤토리 슬롯 (oldBook이 null이면 인벤토리 슬롯이 비워짐)
            ItemData? oldBook = data._skillBookSlots[slotIndex];
            data._inventory[inventorySlotIndex] = oldBook;
            data._skillBookSlots[slotIndex] = newBook;

            RecreatePlayerSkillEntity(slotIndex, newSkillId);
            BroadcastSkillBookChanged(slotIndex, newSkillId);

            AR.s?.Data?.Save();
            return true;
        }

        /// <summary>
        /// 인벤토리의 스킬 페이지를 장착된 스킬북에 삽입. 페이지 아이템은 소비되고 책에는 SkillEffectId만 저장된다.
        /// </summary>
        public bool SocketSkillPage(int skillBookSlotIndex, int inventorySlotIndex)
        {
            if (IsValidSlot(skillBookSlotIndex) == false)
            {
                Debug.LogWarning($"[PlayerSkillManager] SocketSkillPage - Invalid skillBookSlotIndex({skillBookSlotIndex})");
                return false;
            }

            PlayerData? data = AR.s?.Data?.Player;
            if (data == null)
            {
                Debug.LogError("[PlayerSkillManager] SocketSkillPage - PlayerData not available");
                return false;
            }

            if (inventorySlotIndex < 0 || inventorySlotIndex >= data._inventory.Count)
            {
                Debug.LogWarning($"[PlayerSkillManager] SocketSkillPage - Invalid inventorySlotIndex({inventorySlotIndex})");
                return false;
            }

            ItemData? book = data._skillBookSlots[skillBookSlotIndex];
            ItemData? pageItem = data._inventory[inventorySlotIndex];
            if (IsValidSkillPage(pageItem) == false)
            {
                Debug.LogWarning($"[PlayerSkillManager] SocketSkillPage - Inventory slot({inventorySlotIndex}) is not a valid SkillPage");
                return false;
            }

            int skillEffectId = pageItem!.SkillPage!.SkillEffectId;
            if (CanSocketSkillPage(book, skillEffectId, out string reason) == false)
            {
                Debug.LogWarning($"[PlayerSkillManager] SocketSkillPage - {reason}. SkillBookSlot({skillBookSlotIndex}), SkillEffectId({skillEffectId})");
                return false;
            }

            book!.SkillBook!.SocketedPages.Add(pageItem!);
            data._inventory[inventorySlotIndex] = null;

            RecreatePlayerSkillEntity(skillBookSlotIndex, book.SkillBook.SkillId);
            BroadcastSkillBookChanged(skillBookSlotIndex, book.SkillBook.SkillId);
            AR.s?.Data?.Save();
            return true;
        }

        /// <summary>
        /// 장착된 페이지를 제거해 인벤토리로 되돌린다. v1은 SkillEffectId 기준으로 페이지 아이템을 재생성한다.
        /// </summary>
        public bool UnsocketSkillPage(int skillBookSlotIndex, int pageIndex)
        {
            if (IsValidSlot(skillBookSlotIndex) == false)
            {
                Debug.LogWarning($"[PlayerSkillManager] UnsocketSkillPage - Invalid skillBookSlotIndex({skillBookSlotIndex})");
                return false;
            }

            PlayerData? data = AR.s?.Data?.Player;
            if (data == null)
                return false;

            ItemData? book = data._skillBookSlots[skillBookSlotIndex];
            if (IsValidSkillBook(book) == false || book!.SkillBook!.SocketedPages == null)
                return false;

            List<ItemData> socketedPages = book.SkillBook.SocketedPages;
            if (pageIndex < 0 || pageIndex >= socketedPages.Count)
            {
                Debug.LogWarning($"[PlayerSkillManager] UnsocketSkillPage - Invalid pageIndex({pageIndex})");
                return false;
            }

            int invSlot = FindFirstEmptyInventorySlot(data);
            if (invSlot < 0)
            {
                Debug.LogWarning("[PlayerSkillManager] UnsocketSkillPage - Inventory is full");
                return false;
            }

            ItemData pageItem = socketedPages[pageIndex];
            socketedPages.RemoveAt(pageIndex);
            data._inventory[invSlot] = pageItem;

            RecreatePlayerSkillEntity(skillBookSlotIndex, book.SkillBook.SkillId);
            BroadcastSkillBookChanged(skillBookSlotIndex, book.SkillBook.SkillId);
            AR.s?.Data?.Save();
            return true;
        }

        // ========== 슬롯 간 스왑 ==========

        /// <summary>
        /// 장착 슬롯 간 책 위치 교환. 한쪽이 비어 있어도 동작(이동).
        /// 성공 시 ECS 스킬 엔티티 양쪽 재구성 + 세이브.
        /// </summary>
        public bool SwapSkillSlots(int slotA, int slotB)
        {
            if (IsValidSlot(slotA) == false || IsValidSlot(slotB) == false)
            {
                Debug.LogWarning($"[PlayerSkillManager] SwapSkillSlots - Invalid slot ({slotA}, {slotB})");
                return false;
            }
            if (slotA == slotB) return false;

            PlayerData? data = AR.s?.Data?.Player;
            if (data == null) return false;

            ItemData? bookA = data._skillBookSlots[slotA];
            ItemData? bookB = data._skillBookSlots[slotB];
            if (bookA == null && bookB == null) return false;

            data._skillBookSlots[slotA] = bookB;
            data._skillBookSlots[slotB] = bookA;

            int playerEntityId = AR.s!.Data!.CurrentPlayerEntityId;
            if (playerEntityId >= 0)
            {
                EntityFactory.RemoveSkill(playerEntityId, slotA);
                EntityFactory.RemoveSkill(playerEntityId, slotB);

                int newSkillIdA = bookB?.SkillBook?.SkillId ?? 0;
                int newSkillIdB = bookA?.SkillBook?.SkillId ?? 0;
                if (newSkillIdA > 0) EntityFactory.CreateSkill(playerEntityId, slotA, newSkillIdA);
                if (newSkillIdB > 0) EntityFactory.CreateSkill(playerEntityId, slotB, newSkillIdB);
            }

            BroadcastSkillBookChanged(slotA, bookB?.SkillBook?.SkillId ?? 0);
            BroadcastSkillBookChanged(slotB, bookA?.SkillBook?.SkillId ?? 0);

            AR.s?.Data?.Save();
            return true;
        }

        // ========== 해제 ==========

        /// <summary>
        /// 스킬 슬롯의 책을 인벤토리로 반환. 인벤토리 빈 슬롯 없으면 거부.
        /// 성공 시 ECS 스킬 엔티티 제거 + 세이브.
        /// </summary>
        public bool UnequipSkillBook(int slotIndex)
        {
            if (IsValidSlot(slotIndex) == false)
            {
                Debug.LogWarning($"[PlayerSkillManager] UnequipSkillBook - Invalid slotIndex({slotIndex})");
                return false;
            }

            PlayerData? data = AR.s?.Data?.Player;
            if (data == null) return false;

            ItemData? book = data._skillBookSlots[slotIndex];
            if (book == null) return false; // 이미 비어 있음

            int invSlot = FindFirstEmptyInventorySlot(data);
            if (invSlot < 0)
            {
                Debug.LogWarning("[PlayerSkillManager] UnequipSkillBook - Inventory is full");
                return false;
            }

            data._inventory[invSlot] = book;
            data._skillBookSlots[slotIndex] = null;

            RecreatePlayerSkillEntity(slotIndex, 0);
            BroadcastSkillBookChanged(slotIndex, 0);

            AR.s?.Data?.Save();
            return true;
        }

        // ========== 검증 헬퍼 ==========

        private static bool IsValidSlot(int slotIndex)
        {
            return slotIndex >= 0 && slotIndex < GlobalEnum.PLAYER_SKILL_SLOT_COUNT;
        }

        private static bool IsValidSkillBook(ItemData? item)
        {
            if (item == null) return false;
            if (item.Table == null || item.Table.ItemType != GlobalEnum.ItemType.SkillBook) return false;
            if (item.SkillBook == null || item.SkillBook.SkillId <= 0) return false;
            return true;
        }

        private static bool IsValidSkillPage(ItemData? item)
        {
            if (item == null) return false;
            if (item.Table == null || item.Table.ItemType != GlobalEnum.ItemType.SkillPage) return false;
            if (item.SkillPage == null || item.SkillPage.SkillEffectId <= 0) return false;
            return true;
        }

        private static int FindFirstEmptyInventorySlot(PlayerData data)
        {
            for (int i = 0; i < data._inventory.Count; i++)
            {
                if (data._inventory[i] == null)
                    return i;
            }

            return -1;
        }

        private static void RecreatePlayerSkillEntity(int slotIndex, int skillId)
        {
            int playerEntityId = AR.s!.Data!.CurrentPlayerEntityId;
            if (playerEntityId < 0)
                return;

            EntityFactory.RemoveSkill(playerEntityId, slotIndex);
            if (skillId > 0)
            {
                EntityFactory.CreateSkill(playerEntityId, slotIndex, skillId);
            }
        }

        private static void BroadcastSkillBookChanged(int slotIndex, int skillId)
        {
            AR.s.Message?.Broadcast(new SkillBookChangedMessage
            {
                SlotIndex = slotIndex,
                NewSkillId = skillId,
            });
        }
    }
}
