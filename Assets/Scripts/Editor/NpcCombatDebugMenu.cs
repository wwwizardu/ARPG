#nullable enable
using ARPG.Component;
using ARPG.Data;
using ARPG.Npc;
using UnityEditor;
using UnityEngine;

namespace ARPG.EditorTools
{
    /// <summary>
    /// NPC 실전투 B1·B2·B4·B5 검증용 디버그 메뉴 (플레이 모드 전용).
    /// 강제 사망은 HP를 0으로 만들어 실제 전투 사망과 같은 경로(HpCheck → DestroyTag → EntityDestroy)를 태운다.
    /// 동행 메뉴는 대화 UI와 같은 NpcManager API(RequestCompanion/CancelCompanion)를 호출한다.
    /// </summary>
    public static class NpcCombatDebugMenu
    {
        [MenuItem("ARPG/Npc/Debug/Kill Nearest NPC (Combat Death)", false, 430)]
        public static void KillNearestNpc()
        {
            int npcId = FindNearestActiveNpc();
            if (npcId < 0)
                return;

            ComponentManager cm = AR.s.Component;
            if (cm.TryGetComponent<StatComponent>(npcId, out StatComponent stat) == false)
            {
                Debug.LogWarning($"[NpcCombatDebug] StatComponent 없음 - EntityId: {npcId}");
                return;
            }

            stat.SetCurrentHp(npcId, 0);
            cm.SetComponent(npcId, stat);
            Debug.Log($"[NpcCombatDebug] 전투 사망 강제 - {AR.s.Npc.GetDisplayName(npcId)} (EntityId: {npcId})");
        }

        [MenuItem("ARPG/Npc/Debug/Log Nearest NPC State", false, 431)]
        public static void LogNearestNpcState()
        {
            int npcId = FindNearestActiveNpc();
            if (npcId < 0)
                return;

            LogNpcState(npcId);
        }

        [MenuItem("ARPG/Npc/Debug/Request Companion (Nearest NPC)", false, 432)]
        public static void RequestCompanionNearest()
        {
            int npcId = FindNearestActiveNpc();
            if (npcId < 0)
                return;

            bool accepted = AR.s.Npc.RequestCompanion(npcId, out string message);
            Debug.Log($"[NpcCombatDebug] 동행 요청 {(accepted == true ? "수락" : "거절")} - {message}");
        }

        [MenuItem("ARPG/Npc/Debug/Cancel Companion Reservation", false, 433)]
        public static void CancelCompanion()
        {
            int npcId = AR.s.Data.NpcCompanion.ReservedNpcId;
            bool cancelled = AR.s.Npc.CancelCompanion(npcId, out string message);
            Debug.Log($"[NpcCombatDebug] 동행 취소 {(cancelled == true ? "완료" : "불가")} - {message}");
        }

        [MenuItem("ARPG/Npc/Debug/Log Companion State", false, 434)]
        public static void LogCompanionState()
        {
            NpcCompanionState state = AR.s.Data.NpcCompanion;
            Debug.Log($"[NpcCombatDebug] 동행 상태 - ReservedNpcId: {state.ReservedNpcId}, IsInDungeon: {state.IsInDungeon}, " +
                      $"DungeonInside: {AR.s.Dungeon.IsInside}");
            if (state.ReservedNpcId >= 0)
                LogNpcState(state.ReservedNpcId);
        }

        [MenuItem("ARPG/Npc/Debug/Request Duel (Nearest NPC)", false, 440)]
        public static void RequestDuelNearest()
        {
            int npcId = FindNearestActiveNpc();
            if (npcId < 0)
                return;

            bool accepted = AR.s.Npc.RequestDuel(npcId, out string message);
            Debug.Log($"[NpcCombatDebug] 결투 신청 {(accepted == true ? "수락" : "불가")} - {message}");
        }

        [MenuItem("ARPG/Npc/Debug/Log Duel State", false, 441)]
        public static void LogDuelState()
        {
            ComponentManager cm = AR.s.Component;
            SparseSet<DuelComponent> duelPool = cm.GetComponentPool<DuelComponent>();
            Debug.Log($"[NpcCombatDebug] 결투 참가자 수: {duelPool.Count}, PlayerInDuel: {AR.s.Npc.IsPlayerInDuel}");
            for (int i = 0; i < duelPool.Count; i++)
            {
                int entityId = duelPool.GetEntityId(i);
                DuelComponent duel = duelPool.GetByIndex(i);
                string hp = cm.TryGetComponent<StatComponent>(entityId, out StatComponent stat) == true
                    ? $"{stat.CurrentHp}/{stat.FinalMaxHp}"
                    : "(없음)";
                Debug.Log($"[NpcCombatDebug] - EntityId: {entityId}, Opponent: {duel.OpponentId}, " +
                          $"Elapsed: {duel.ElapsedSeconds:F1}s, PendingLoser: {duel.PendingLoserId}, HP: {hp}");
                if (cm.HasComponent<NpcTag>(entityId) == true)
                    LogNpcState(entityId);
            }
        }

        [MenuItem("ARPG/Npc/Debug/Fast-forward Duel to 1s Left", false, 442)]
        public static void FastForwardDuel()
        {
            ComponentManager cm = AR.s.Component;
            SparseSet<DuelComponent> duelPool = cm.GetComponentPool<DuelComponent>();
            for (int i = 0; i < duelPool.Count; i++)
            {
                int entityId = duelPool.GetEntityId(i);
                DuelComponent duel = duelPool.GetByIndex(i);
                duel.ElapsedSeconds = Mathf.Max(duel.ElapsedSeconds, NpcManager.DuelTimeLimitSeconds - 1f);
                cm.SetComponent(entityId, duel);
            }
            Debug.Log($"[NpcCombatDebug] 결투 시간 빨리 감기 - 참가자 {duelPool.Count}명");
        }

        [MenuItem("ARPG/Npc/Debug/Set Player HP 0 (Duel Defeat Test)", false, 443)]
        public static void SetPlayerHpZero()
        {
            ComponentManager cm = AR.s.Component;
            int playerId = AR.s.Data.CurrentPlayerEntityId;
            if (cm.TryGetComponent<StatComponent>(playerId, out StatComponent stat) == false)
                return;

            stat.SetCurrentHp(playerId, 0);
            cm.SetComponent(playerId, stat);
            Debug.Log("[NpcCombatDebug] 플레이어 HP 0 강제");
        }

        [MenuItem("ARPG/Npc/Debug/Pickup Nearest Drop (Nearest NPC)", false, 450)]
        public static void PickupNearestDrop()
        {
            int npcId = FindNearestActiveNpc();
            if (npcId < 0)
                return;

            bool started = AR.s.Npc.TryStartPickupNearestDrop(npcId, out string message);
            Debug.Log($"[NpcCombatDebug] 드랍 줍기 {(started == true ? "시작" : "불가")} - {message}");
        }

        [MenuItem("ARPG/Npc/Debug/Log Nearest NPC Inventory", false, 451)]
        public static void LogNearestNpcInventory()
        {
            int npcId = FindNearestActiveNpc();
            if (npcId < 0)
                return;

            NpcSaveData? saveData = AR.s.Npc.GetSaveData(npcId);
            if (saveData == null)
            {
                Debug.LogWarning($"[NpcCombatDebug] NpcSaveData 없음 - EntityId: {npcId}");
                return;
            }

            Debug.Log($"[NpcCombatDebug] {AR.s.Npc.GetDisplayName(npcId)} (EntityId: {npcId}) 인벤토리 " +
                      $"{saveData.Inventory.Count}칸, PickingUp: {AR.s.Npc.IsPickingUpDrop(npcId)}");
            for (int i = 0; i < saveData.Inventory.Count; i++)
            {
                ItemData? item = saveData.Inventory[i];
                string text = item == null
                    ? "(빈 칸)"
                    : $"{(item.Table != null ? item.Table.Name : "?")} Id: {item.Id}, Instance: {item.ItemInstanceId}, Qty: {item.Quantity}";
                Debug.Log($"[NpcCombatDebug] - [{i}] {text}");
            }
        }

        [MenuItem("ARPG/Npc/Debug/Pickup Nearest Drop (Nearest NPC)", true)]
        [MenuItem("ARPG/Npc/Debug/Log Nearest NPC Inventory", true)]
        [MenuItem("ARPG/Npc/Debug/Request Duel (Nearest NPC)", true)]
        [MenuItem("ARPG/Npc/Debug/Log Duel State", true)]
        [MenuItem("ARPG/Npc/Debug/Fast-forward Duel to 1s Left", true)]
        [MenuItem("ARPG/Npc/Debug/Set Player HP 0 (Duel Defeat Test)", true)]
        [MenuItem("ARPG/Npc/Debug/Kill Nearest NPC (Combat Death)", true)]
        [MenuItem("ARPG/Npc/Debug/Log Nearest NPC State", true)]
        [MenuItem("ARPG/Npc/Debug/Request Companion (Nearest NPC)", true)]
        [MenuItem("ARPG/Npc/Debug/Cancel Companion Reservation", true)]
        [MenuItem("ARPG/Npc/Debug/Log Companion State", true)]
        private static bool ValidatePlayMode()
        {
            return Application.isPlaying;
        }

        /// <summary>
        /// 지정 NPC의 진영과 영구 사망 관련 저장 상태를 로그로 출력한다.
        /// </summary>
        public static void LogNpcState(int npcId)
        {
            NpcSaveData? saveData = AR.s.Npc.GetSaveData(npcId);
            if (saveData == null)
            {
                Debug.LogWarning($"[NpcCombatDebug] NpcSaveData 없음 - EntityId: {npcId}");
                return;
            }

            string faction = AR.s.Component.TryGetComponent<FactionComponent>(npcId, out FactionComponent factionComponent) == true
                ? factionComponent.FactionId.ToString()
                : "(없음)";
            ComponentManager cm = AR.s.Component;
            string aiState = cm.TryGetComponent<AIStateComponent>(npcId, out AIStateComponent aiStateComponent) == true
                ? aiStateComponent.CurrentState.ToString()
                : "(없음)";
            string position = cm.TryGetComponent<TransformComponent>(npcId, out TransformComponent transform) == true
                ? transform.Position.ToString()
                : $"(비실체화) saved {saveData.Position}";
            string lastDeed = saveData.Deeds.Count > 0
                ? saveData.Deeds[saveData.Deeds.Count - 1].Kind.ToString()
                : "(없음)";
            // 마을 안 걷기 (NPC_SIMULATION_DESIGN §3.1): 출발·귀환·여관 가기와 목표 지점
            string villageWalk = cm.TryGetComponent<NpcVillageWalkComponent>(npcId, out NpcVillageWalkComponent walk) == true
                ? $"{walk.Kind}→{walk.Target}"
                : "(없음)";

            Debug.Log($"[NpcCombatDebug] {AR.s.Npc.GetDisplayName(npcId)} (EntityId: {npcId}) " +
                      $"Faction: {faction}, Condition: {saveData.Condition}, Phase: {saveData.Activity.Phase}, " +
                      $"IsActive: {saveData.IsActive}, VillageId: {saveData.VillageId}, LastDeed: {lastDeed}, " +
                      $"AIState: {aiState}, CombatTag: {cm.HasComponent<NpcCombatEnabledTag>(npcId)}, " +
                      $"PausedTag: {cm.HasComponent<NpcActivityPausedTag>(npcId)}, VillageWalk: {villageWalk}, " +
                      $"InnGuest: {AR.s.Npc.IsRecoveringInInn(saveData)}, " +
                      $"Duel: {cm.HasComponent<DuelComponent>(npcId)}, Position: {position}");
        }

        private static int FindNearestActiveNpc()
        {
            if (Application.isPlaying == false)
            {
                Debug.LogWarning("[NpcCombatDebug] 플레이 모드에서만 동작합니다.");
                return -1;
            }

            ComponentManager cm = AR.s.Component;
            int playerId = AR.s.Data.CurrentPlayerEntityId;
            if (cm.TryGetComponent<TransformComponent>(playerId, out TransformComponent playerTransform) == false)
            {
                Debug.LogWarning("[NpcCombatDebug] 플레이어 TransformComponent 없음");
                return -1;
            }

            SparseSet<NpcTag> npcPool = cm.GetComponentPool<NpcTag>();
            int bestId = -1;
            float bestSqrDistance = float.MaxValue;
            for (int i = 0; i < npcPool.Count; i++)
            {
                int npcId = npcPool.GetEntityId(i);
                if (cm.HasComponent<DestroyTag>(npcId) == true)
                    continue;
                if (cm.TryGetComponent<TransformComponent>(npcId, out TransformComponent npcTransform) == false)
                    continue;

                float sqrDistance = (npcTransform.Position - playerTransform.Position).sqrMagnitude;
                if (sqrDistance < bestSqrDistance)
                {
                    bestSqrDistance = sqrDistance;
                    bestId = npcId;
                }
            }

            if (bestId < 0)
                Debug.LogWarning("[NpcCombatDebug] 활성 NPC 없음");

            return bestId;
        }
    }
}
