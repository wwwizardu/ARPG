using ARPG.AI;
using ARPG.Component;
using ARPG.Npc;
using UnityEngine;

namespace ARPG.Systems
{
    /// <summary>
    /// 마을 안 걷기 진행 시스템 (NPC_SIMULATION_DESIGN §3.1 D13·D14). System_NpcPickup을 본보기로 한다.
    /// NpcVillageWalkComponent를 가진 NPC를 목표 지점으로 걷게 하고, 판단은 NpcManager에 넘긴다 — 시스템은 상태를 갖지 않는다.
    /// - 출발·여관 가기: 플레이어 화면(카메라 영역 + 여유)을 벗어나는 순간 강등한다.
    /// - 지점에 닿으면 NpcManager.OnVillageWalkReached (귀환 = 기본 AI, 여관 문 = 강등, 아직 화면 안 = 다음 지점).
    /// - 구간 제한 시간 초과·길찾기 연속 실패·활성 청크 밖으로 나감 → NpcManager.OnVillageWalkFailed.
    /// - 결투가 시작되면 걷기를 멈춘다(StartPlayerDuel이 먼저 떼고, 여기서도 확인한다).
    /// </summary>
    public class System_NpcVillageWalk : IFixedUpdateSystem, IWorldMapSystem
    {
        /// <summary>
        /// Priority 76: 드랍 줍기(75)와 같은 자리 — AI Behavior(50) 이후(걷는 NPC는 AI 핸들러를 건너뛰므로 이동을 여기서 소유),
        /// Pathfinding(80) 이전(같은 틱에 Goal이 A*에 반영되도록).
        /// </summary>
        public int Priority => 76;

        public void OnCreate()
        {
            Debug.Log("[System_NpcVillageWalk] Created");
        }

        public void OnReset()
        {
            Debug.Log("[System_NpcVillageWalk] Reset");
        }

        public void OnFixedUpdate(float inFixedDeltaTime)
        {
            ComponentManager cm = AR.s.Component;
            SparseSet<NpcVillageWalkComponent> walkPool = cm.GetComponentPool<NpcVillageWalkComponent>();
            if (walkPool == null || walkPool.Count == 0)
                return;

            bool hasView = AR.s.Npc.TryGetPlayerViewRect(out Rect view);
            float reachSqr = NpcManager.VillageWalkReachDistance * NpcManager.VillageWalkReachDistance;

            // 도착·강등·실패 처리가 컴포넌트를 제거하므로(swap-remove) 역순으로 순회한다
            for (int i = walkPool.Count - 1; i >= 0; i--)
            {
                int npcId = walkPool.GetEntityId(i);
                NpcVillageWalkComponent walk = walkPool.GetByIndex(i);

                if (cm.TryGetComponent<TransformComponent>(npcId, out TransformComponent transform) == false ||
                    cm.HasComponent<DestroyTag>(npcId) == true)
                {
                    cm.RemoveComponent<NpcVillageWalkComponent>(npcId);
                    continue;
                }
                if (cm.HasComponent<DuelComponent>(npcId) == true)
                {
                    AR.s.Npc.CancelVillageWalk(npcId);
                    continue;
                }

                Vector2 position = transform.Position;
                if (AR.s.Map.IsChunkActiveAtWorld(position) == false)
                {
                    AR.s.Npc.OnVillageWalkFailed(npcId, walk, true);
                    continue;
                }
                // 떠나는 걷기는 화면을 벗어나는 순간 강등한다 — 귀환은 화면 바로 밖에서 시작하므로 제외
                if (walk.Kind != NpcVillageWalkKind.Return && hasView == true && view.Contains(position) == false)
                {
                    AR.s.Npc.DemoteMaterializedNpc(npcId);
                    continue;
                }
                if ((walk.Target - position).sqrMagnitude <= reachSqr)
                {
                    AR.s.Npc.OnVillageWalkReached(npcId, walk);
                    continue;
                }

                walk.ElapsedSeconds += inFixedDeltaTime;
                bool pathFailed = cm.TryGetComponent<PathfindingComponent>(npcId, out PathfindingComponent pf) == true &&
                                  pf.Status == PathfindingStatus.Failed;
                walk.PathFailedSeconds = pathFailed == true ? walk.PathFailedSeconds + inFixedDeltaTime : 0f;
                if (walk.ElapsedSeconds >= NpcManager.VillageWalkSegmentSeconds ||
                    walk.PathFailedSeconds >= NpcManager.VillageWalkPathFailSeconds)
                {
                    AR.s.Npc.OnVillageWalkFailed(npcId, walk, false);
                    continue;
                }

                cm.SetComponent(npcId, walk);
                AIStateHelper.MoveToward(npcId, walk.Target);
            }
        }
    }
}
