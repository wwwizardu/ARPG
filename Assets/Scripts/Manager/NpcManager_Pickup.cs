#nullable enable
using UnityEngine;
using ARPG.AI;
using ARPG.Component;
using ARPG.Creature;
using ARPG.Data;
using ARPG.Item;

namespace ARPG.Npc
{
    /// <summary>
    /// NPC 드랍 줍기 (NPC_COMBAT_DESIGN §6, B5).
    /// NPC 주변 8타일 안의 가장 가까운 월드 드랍 1개로 이동해 주워 자기 인벤토리(NpcSaveData.Inventory)에 넣는다.
    /// 진행 상태는 NpcPickupComponent가 소유하고(저장하지 않음), 이동·도착 판정은 System_NpcPickup이 한다.
    /// 평소 동행 중에는 줍지 않는다(D16) — 시작 진입점 TryStartPickupNearestDrop은 디버그 명령과 이후 C1 배신 규칙(유형 1 전리품 가로채기)만 호출한다.
    /// 대상은 바닥 드랍뿐이다 — 플레이어 인벤토리·장비의 아이템은 대상이 아니다(AMBITION §8.2).
    /// 주운 아이템은 다음 전체 저장 때 NPC 파티션에 기록된다(그 사이 비정상 종료 시 사라질 수 있음을 허용). 드랍 제거는 즉시 저장해 복제를 막는다.
    /// </summary>
    public partial class NpcManager
    {
        /// <summary>줍기 대상 탐색 반경 (타일) — NPC 주변 8타일 [§6 기본값].</summary>
        public const float PickupSearchRange = 8f;
        /// <summary>드랍과 이 거리 안에 들어오면 줍는다.</summary>
        public const float PickupReachDistance = 1f;
        /// <summary>시작 후 이 시간 안에 줍지 못하면 취소한다.</summary>
        public const float PickupTimeLimitSeconds = 15f;
        /// <summary>길찾기가 이 시간 동안 연속 실패하면 취소한다.</summary>
        public const float PickupPathFailSeconds = 3f;

        /// <summary>드랍 줍기 진행 중인 NPC인지.</summary>
        public bool IsPickingUpDrop(int npcId)
        {
            return npcId >= 0 && AR.s.Component.HasComponent<NpcPickupComponent>(npcId) == true;
        }

        /// <summary>
        /// 줍기 시작 진입점 (디버그 명령, 이후 C1 배신 규칙). 현재 맵 컨텍스트(월드/던전)에서 NPC 주변 8타일 안의 가장 가까운 드랍 1개를 대상으로 잡는다.
        /// 다음 경우에는 시작하지 않는다: 실체화되지 않음·사망, 원정 중 화면 안 대기(NpcActivityPausedTag — 사냥 경로 이동 계약 유지),
        /// 결투 중, 이미 줍는 중, 던전 전환 중, 범위 안 드랍 없음, 인벤토리에 자리가 없음.
        /// </summary>
        public bool TryStartPickupNearestDrop(int npcId, out string message)
        {
            ComponentManager cm = AR.s.Component;
            NpcSaveData? saveData = GetSaveData(npcId);
            if (saveData == null)
            {
                message = "알 수 없는 NPC입니다.";
                return false;
            }
            if (saveData.Condition == CharacterConditions.Dead || saveData.Activity.Phase == NpcActivityPhase.Dead)
            {
                message = "세상을 떠난 NPC입니다.";
                return false;
            }
            if (cm.HasComponent<NpcTag>(npcId) == false || cm.HasComponent<AIStateComponent>(npcId) == false ||
                cm.HasComponent<DestroyTag>(npcId) == true ||
                cm.TryGetComponent<TransformComponent>(npcId, out TransformComponent transform) == false)
            {
                message = "실체화되지 않은 NPC입니다.";
                return false;
            }
            if (AR.s.Dungeon.IsBusy == true)
            {
                message = "던전 전환 중에는 줍지 않습니다.";
                return false;
            }
            if (cm.HasComponent<NpcActivityPausedTag>(npcId) == true)
            {
                message = "원정 중(화면 안 대기)인 NPC는 줍지 않습니다.";
                return false;
            }
            if (cm.HasComponent<DuelComponent>(npcId) == true)
            {
                message = "결투 중인 NPC는 줍지 않습니다.";
                return false;
            }
            if (cm.HasComponent<NpcPickupComponent>(npcId) == true)
            {
                message = "이미 줍는 중입니다.";
                return false;
            }

            if (AR.s.Item.TryFindNearestContextDrop(transform.Position, PickupSearchRange,
                    out int instanceId, out Vector2 dropPosition) == false)
            {
                message = $"주변 {PickupSearchRange:F0}타일 안에 드랍이 없습니다.";
                return false;
            }

            ItemData? item = AR.s.Item.GetContextDropItem(instanceId);
            if (item == null)
            {
                message = "드랍 정보를 찾을 수 없습니다.";
                return false;
            }
            if (CreateNpcInventoryView(saveData).CanAddItem(item) == false)
            {
                message = "인벤토리가 가득 차 줍지 않습니다.";
                return false;
            }

            // 줍는 동안 AI 상태 핸들러는 System_AI_Behavior가 건너뛴다. 진행 중이던 이동을 멈추고 System_NpcPickup에 넘긴다.
            AIStateHelper.StopMovement(npcId);
            cm.AddComponent(npcId, new NpcPickupComponent
            {
                TargetItemInstanceId = instanceId,
                TargetPosition = dropPosition,
                ElapsedSeconds = 0f,
                PathFailedSeconds = 0f,
            });

            message = $"{GetDisplayName(npcId)}이(가) {GetItemDisplayName(item)}을(를) 주우러 갑니다.";
            Debug.Log($"[NpcPickup] 시작 - Npc: {npcId}, ItemInstance: {instanceId}, Drop: {dropPosition}, " +
                      $"Distance: {(dropPosition - transform.Position).magnitude:F2}");
            return true;
        }

        /// <summary>System_NpcPickup이 드랍에 도착한 NPC에 대해 호출한다. 인벤토리에 넣고 월드에서 드랍을 제거한다.</summary>
        public void CompleteNpcPickup(int npcId)
        {
            ComponentManager cm = AR.s.Component;
            if (cm.TryGetComponent<NpcPickupComponent>(npcId, out NpcPickupComponent pickup) == false)
                return;

            NpcSaveData? saveData = GetSaveData(npcId);
            if (saveData == null)
            {
                CancelNpcPickup(npcId, "NpcSaveData 없음");
                return;
            }

            ItemData? item = AR.s.Item.GetContextDropItem(pickup.TargetItemInstanceId);
            string itemName = item != null ? GetItemDisplayName(item) : "(알 수 없음)";
            if (AR.s.Item.PickupContextDropInto(pickup.TargetItemInstanceId, CreateNpcInventoryView(saveData)) == false)
            {
                CancelNpcPickup(npcId, item == null ? "대상 드랍이 사라짐" : "인벤토리가 가득 참");
                return;
            }

            FinishNpcPickup(npcId);
            Debug.Log($"[NpcPickup] 완료 - {GetDisplayName(npcId)} (EntityId: {npcId}), " +
                      $"ItemInstance: {pickup.TargetItemInstanceId}, Item: {itemName}");
            AR.s.UI.SetNotify($"{GetDisplayName(npcId)}이(가) {itemName}을(를) 주웠습니다.");
        }

        /// <summary>줍기를 취소한다 (대상 소멸·결투 시작·제한 시간 등). 인벤토리·드랍은 바뀌지 않는다.</summary>
        public void CancelNpcPickup(int npcId, string reason)
        {
            if (AR.s.Component.HasComponent<NpcPickupComponent>(npcId) == false)
                return;

            FinishNpcPickup(npcId);
            Debug.Log($"[NpcPickup] 취소 - Npc: {npcId}, Reason: {reason}");
        }

        /// <summary>진행 컴포넌트를 떼고 이동을 멈춘다. AI는 줍기 전 상태 핸들러부터 다시 돈다.</summary>
        private static void FinishNpcPickup(int npcId)
        {
            AR.s.Component.RemoveComponent<NpcPickupComponent>(npcId);
            AIStateHelper.StopMovement(npcId);
        }

        /// <summary>NpcSaveData.Inventory 리스트를 그대로 감싼 Inventory (같은 리스트를 수정한다 — 플레이어와 같은 AddItem 규칙 재사용).</summary>
        private Inventory CreateNpcInventoryView(NpcSaveData saveData)
        {
            saveData.EnsureSlotsInitialized();
            Inventory inventory = new Inventory();
            inventory.Initialize(saveData.Inventory, GlobalEnum.NPC_INVENTORY_SLOTCOUNT_MAX);
            inventory.Changed = () => MarkSaveDirty(saveData.EntityId, NpcSaveGroup.Inventory);
            return inventory;
        }

        private static string GetItemDisplayName(ItemData item)
        {
            return item.Table != null ? item.Table.Name : $"Item {item.Id}";
        }
    }
}
