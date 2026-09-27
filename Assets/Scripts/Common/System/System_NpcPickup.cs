using ARPG.AI;
using ARPG.Component;
using ARPG.Npc;
using UnityEngine;

namespace ARPG.Systems
{
    /// <summary>
    /// NPC 드랍 줍기 진행 시스템 (NPC_COMBAT_DESIGN §6, B5).
    /// NpcPickupComponent를 가진 NPC를 드랍 위치로 이동시키고, 도착하면 NpcManager.CompleteNpcPickup으로 줍기를 확정한다.
    /// 줍기 시작은 NpcManager.TryStartPickupNearestDrop(디버그 명령·이후 C1 배신 규칙)만 한다 — 평소 동행 중 자동 줍기는 없다(D16).
    /// 다음 경우에는 줍지 않고 취소한다: 대상 드랍이 사라짐(플레이어가 먼저 주움·컨텍스트 전환), NPC가 결투를 시작함,
    /// 원정 대기(NpcActivityPausedTag)가 붙음, 제한 시간 초과, 길찾기 연속 실패.
    /// </summary>
    public class System_NpcPickup : IFixedUpdateSystem
    {
        /// <summary>
        /// Priority 75: AI Behavior(50) 이후 — 줍기 중인 NPC는 AI 핸들러를 건너뛰므로 이동을 여기서 소유한다.
        /// Pathfinding(80) 이전 — 같은 틱에 Goal이 A*에 반영되도록.
        /// </summary>
        public int Priority => 75;

        public void OnCreate()
        {
            Debug.Log("[System_NpcPickup] Created");
        }

        public void OnReset()
        {
            Debug.Log("[System_NpcPickup] Reset");
        }

        public void OnFixedUpdate(float inFixedDeltaTime)
        {
            ComponentManager cm = AR.s.Component;
            SparseSet<NpcPickupComponent> pickupPool = cm.GetComponentPool<NpcPickupComponent>();
            if (pickupPool == null || pickupPool.Count == 0)
                return;

            float reachSqr = NpcManager.PickupReachDistance * NpcManager.PickupReachDistance;

            // 완료·취소가 컴포넌트를 제거하므로(swap-remove) 역순으로 순회한다
            for (int i = pickupPool.Count - 1; i >= 0; i--)
            {
                int npcId = pickupPool.GetEntityId(i);
                NpcPickupComponent pickup = pickupPool.GetByIndex(i);

                if (cm.TryGetComponent<TransformComponent>(npcId, out TransformComponent transform) == false ||
                    cm.HasComponent<DestroyTag>(npcId) == true)
                {
                    AR.s.Npc.CancelNpcPickup(npcId, "NPC 소멸");
                    continue;
                }
                if (cm.HasComponent<DuelComponent>(npcId) == true)
                {
                    AR.s.Npc.CancelNpcPickup(npcId, "결투 시작");
                    continue;
                }
                if (cm.HasComponent<NpcActivityPausedTag>(npcId) == true)
                {
                    AR.s.Npc.CancelNpcPickup(npcId, "원정 대기 전환");
                    continue;
                }
                if (AR.s.Item.IsContextDropSpawned(pickup.TargetItemInstanceId) == false)
                {
                    AR.s.Npc.CancelNpcPickup(npcId, "대상 드랍이 사라짐");
                    continue;
                }

                if ((pickup.TargetPosition - transform.Position).sqrMagnitude <= reachSqr)
                {
                    AR.s.Npc.CompleteNpcPickup(npcId);
                    continue;
                }

                pickup.ElapsedSeconds += inFixedDeltaTime;
                if (pickup.ElapsedSeconds >= NpcManager.PickupTimeLimitSeconds)
                {
                    AR.s.Npc.CancelNpcPickup(npcId, "제한 시간 초과");
                    continue;
                }

                bool pathFailed = cm.TryGetComponent<PathfindingComponent>(npcId, out PathfindingComponent pf) == true &&
                                  pf.Status == PathfindingStatus.Failed;
                pickup.PathFailedSeconds = pathFailed == true ? pickup.PathFailedSeconds + inFixedDeltaTime : 0f;
                if (pickup.PathFailedSeconds >= NpcManager.PickupPathFailSeconds)
                {
                    AR.s.Npc.CancelNpcPickup(npcId, "길찾기 실패");
                    continue;
                }

                cm.SetComponent(npcId, pickup);
                AIStateHelper.MoveToward(npcId, pickup.TargetPosition);
            }
        }
    }
}
