using ARPG.Component;
using ARPG.Npc;
using UnityEngine;

namespace ARPG.Systems
{
    /// <summary>
    /// 결투 진행 시스템 (NPC_COMBAT_DESIGN §7.2, B4).
    /// DuelComponent를 가진 참가자를 순회하며 다음 순서로 종료 조건을 본다. 종료 처리 자체는 NpcManager.EndPlayerDuel이 한다.
    /// 1. HP 0 패배 확정(System_HpCheck가 PendingLoserId 기록) → 승패 종료 [D22]
    /// 2. 상대 참가자 소멸(청크 이탈 비실체화 등) → 무승부 (기술적 정리)
    /// 3. 진행 시간 누적 → 실제 시간 3분 경과 시 무승부 [D27]
    /// 게임이 멈춘 동안(GameplayPaused)은 SystemManager가 이 시스템을 돌리지 않으므로 시간이 세지지 않는다.
    /// 종료는 컴포넌트를 제거하므로 틱당 1건만 처리하고 빠져나간다(결투는 한 번에 1건).
    /// </summary>
    public class System_Duel : IFixedUpdateSystem
    {
        /// <summary>
        /// Priority 255: HP Check(250)가 패배를 확정한 직후 같은 틱에 종료 처리
        /// </summary>
        public int Priority => 255;

        public void OnCreate()
        {
            Debug.Log("[System_Duel] Created");
        }

        public void OnReset()
        {
            Debug.Log("[System_Duel] Reset");
        }

        public void OnFixedUpdate(float inFixedDeltaTime)
        {
            ComponentManager cm = AR.s.Component;
            SparseSet<DuelComponent> duelPool = cm.GetComponentPool<DuelComponent>();
            if (duelPool == null || duelPool.Count == 0)
                return;

            for (int i = duelPool.Count - 1; i >= 0; i--)
            {
                int entityId = duelPool.GetEntityId(i);
                DuelComponent duel = duelPool.GetByIndex(i);

                if (duel.PendingLoserId >= 0)
                {
                    AR.s.Npc.EndPlayerDuel(entityId, DuelEndReason.Defeat, duel.PendingLoserId);
                    return;
                }

                if (IsParticipantPresent(cm, entityId) == false || IsParticipantPresent(cm, duel.OpponentId) == false ||
                    cm.TryGetComponent<DuelComponent>(duel.OpponentId, out DuelComponent opponentDuel) == false ||
                    opponentDuel.OpponentId != entityId)
                {
                    AR.s.Npc.EndPlayerDuel(entityId, DuelEndReason.ParticipantLost, -1);
                    return;
                }

                duel.ElapsedSeconds += inFixedDeltaTime;
                cm.SetComponent(entityId, duel);
                if (duel.ElapsedSeconds >= NpcManager.DuelTimeLimitSeconds)
                {
                    AR.s.Npc.EndPlayerDuel(entityId, DuelEndReason.Timeout, -1);
                    return;
                }
            }
        }

        private static bool IsParticipantPresent(ComponentManager cm, int entityId)
        {
            return cm.HasComponent<TransformComponent>(entityId) == true &&
                   cm.HasComponent<StatComponent>(entityId) == true &&
                   cm.HasComponent<DestroyTag>(entityId) == false;
        }
    }
}
