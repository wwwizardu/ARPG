#nullable enable
using ARPG.Data;
using ARPG.Factory;
using ARPG.Message;
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

        // ========== 장착 ==========

        /// <summary>
        /// 인벤토리 슬롯의 스킬북을 스킬 슬롯에 장착. 기존 책이 있으면 해당 인벤토리 슬롯으로 스왑.
        /// 같은 SkillId가 다른 슬롯에 이미 있으면 거부.
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

            // ECS 스킬 엔티티 재생성
            int playerEntityId = AR.s!.Data!.CurrentPlayerEntityId;
            if (playerEntityId >= 0)
            {
                EntityFactory.RemoveSkill(playerEntityId, slotIndex);
                EntityFactory.CreateSkill(playerEntityId, slotIndex, newSkillId);
            }

            AR.s.Message?.Broadcast(new SkillBookChangedMessage
            {
                SlotIndex = slotIndex,
                NewSkillId = newSkillId,
            });

            AR.s.Data.Save();
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

            AR.s.Message?.Broadcast(new SkillBookChangedMessage
            {
                SlotIndex = slotA,
                NewSkillId = bookB?.SkillBook?.SkillId ?? 0,
            });
            AR.s.Message?.Broadcast(new SkillBookChangedMessage
            {
                SlotIndex = slotB,
                NewSkillId = bookA?.SkillBook?.SkillId ?? 0,
            });

            AR.s.Data.Save();
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

            // 인벤토리에 빈 슬롯 찾기
            int invSlot = -1;
            for (int i = 0; i < data._inventory.Count; i++)
            {
                if (data._inventory[i] == null)
                {
                    invSlot = i;
                    break;
                }
            }

            if (invSlot < 0)
            {
                Debug.LogWarning("[PlayerSkillManager] UnequipSkillBook - Inventory is full");
                return false;
            }

            data._inventory[invSlot] = book;
            data._skillBookSlots[slotIndex] = null;

            int playerEntityId = AR.s!.Data!.CurrentPlayerEntityId;
            if (playerEntityId >= 0)
            {
                EntityFactory.RemoveSkill(playerEntityId, slotIndex);
            }

            AR.s.Message?.Broadcast(new SkillBookChangedMessage
            {
                SlotIndex = slotIndex,
                NewSkillId = 0,
            });

            AR.s.Data.Save();
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
    }
}
