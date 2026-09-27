#nullable enable
using ARPG.Component;
using ARPG.Npc;
using UnityEditor;
using UnityEngine;

namespace ARPG.EditorTools
{
    /// <summary>
    /// NPC 실전투 B1·B2 검증용 디버그 메뉴 (플레이 모드 전용).
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

            Debug.Log($"[NpcCombatDebug] {AR.s.Npc.GetDisplayName(npcId)} (EntityId: {npcId}) " +
                      $"Faction: {faction}, Condition: {saveData.Condition}, Phase: {saveData.Activity.Phase}, " +
                      $"IsActive: {saveData.IsActive}, VillageId: {saveData.VillageId}, LastDeed: {lastDeed}, " +
                      $"AIState: {aiState}, CombatTag: {cm.HasComponent<NpcCombatEnabledTag>(npcId)}, " +
                      $"PausedTag: {cm.HasComponent<NpcActivityPausedTag>(npcId)}, Position: {position}");
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
