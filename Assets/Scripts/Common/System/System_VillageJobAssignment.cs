#nullable enable
using System.Collections.Generic;
using ARPG.Component;
using ARPG.Village;
using UnityEngine;

namespace ARPG.Systems
{
    /// <summary>
    /// Phase D: NPC 직업 × 작업 오브젝트 매칭 시스템.
    /// 게임시간 1h마다 마을의 idle NPC를 PlacedObject(AssociatedJobType 매칭)와 1:1 결합.
    /// 결과는 NpcAssignmentComponent에 부착 — System_VillagePassiveProduction이 이 컴포넌트를 보고 JobBonusTable 가산.
    ///
    /// Phase D MVP: 매칭만. NPC 실제 이동/일과는 Phase E (System_AbstractVillageSimulation).
    ///
    /// 매칭 룰: NpcJobComponent.JobType == BuildableItemTable.AssociatedJobType (둘 다 0 아님).
    /// 1 NPC : 1 작업 오브젝트, 1 작업 오브젝트 : 1 NPC.
    /// </summary>
    public class System_VillageJobAssignment : IFixedUpdateSystem
    {
        // 도메인 대역 (CLAUDE.md): 65-69 Construction
        public int Priority => 68;
        public float UpdateInterval => 5.0f;

        private const float CHECK_INTERVAL_HOURS = 1f;
        private float _lastCheckGameTime = -1f;

        // 점유 상태 추적용 (마을 단위 사용 후 매 cycle 클리어)
        private static readonly HashSet<int> _busyEntityIds = new();

        public void OnCreate()
        {
            _lastCheckGameTime = AR.s.Time.CurrentGameTime;
        }

        public void OnFixedUpdate(float inFixedDeltaTime)
        {
            float now = AR.s.Time.CurrentGameTime;
            if (now - _lastCheckGameTime < CHECK_INTERVAL_HOURS) return;
            _lastCheckGameTime = now;

            foreach (VillageData v in AR.s.Village.GetAllVillages())
            {
                if (v.EntityId < 0) continue;
                if (v.NpcEntityIds.Count == 0) continue;

                ProcessVillage(v);
            }
        }

        public void OnReset()
        {
            _lastCheckGameTime = -1f;
            _busyEntityIds.Clear();
        }

        // ========== 마을 단위 매칭 ==========

        private static void ProcessVillage(VillageData v)
        {
            // 1. 마을 PlacedObject 중 AssociatedJobType > 0인 것만 작업 오브젝트 후보
            var allEntities = PlacedObjectRegistry.GetAllEntitiesInVillage(v.VillageId);
            List<(int entityId, int tableId, GlobalEnum.JobType job)> workplaces = new();
            for (int i = 0; i < allEntities.Count; i++)
            {
                int entityId = allEntities[i];
                if (AR.s.Component.TryGetComponent<PlacedObjectComponent>(entityId, out var po) == false) continue;
                Tables.BuildableItemTable? t = AR.s.Data.GetBuildableItem(po.TableId);
                if (t == null || t.AssociatedJobType == 0) continue;
                workplaces.Add((entityId, po.TableId, (GlobalEnum.JobType)t.AssociatedJobType));
            }

            if (workplaces.Count == 0)
            {
                // 작업 오브젝트 없음 — 기존 할당 모두 해제
                ClearAllAssignmentsInVillage(v);
                return;
            }

            // 2. 점유 상태 갱신 — 기존 할당 중 작업 오브젝트가 사라졌거나 NPC가 사라진 케이스 정리
            _busyEntityIds.Clear();
            for (int i = 0; i < v.NpcEntityIds.Count; i++)
            {
                int npcId = v.NpcEntityIds[i];
                if (AR.s.Component.TryGetComponent<NpcAssignmentComponent>(npcId, out var existing))
                {
                    if (existing.AssignedObjectEntityId >= 0
                        && IsWorkplaceValid(workplaces, existing.AssignedObjectEntityId))
                    {
                        _busyEntityIds.Add(existing.AssignedObjectEntityId);
                    }
                    else
                    {
                        // 작업 오브젝트 무효화 → 할당 해제
                        AR.s.Component.RemoveComponent<NpcAssignmentComponent>(npcId);
                    }
                }
            }

            // 3. 미할당 NPC 순회 → 매칭 시도
            for (int i = 0; i < v.NpcEntityIds.Count; i++)
            {
                int npcId = v.NpcEntityIds[i];
                if (AR.s.Component.HasComponent<NpcAssignmentComponent>(npcId)) continue;

                // NPC의 직업
                if (AR.s.Component.TryGetComponent<NpcJobComponent>(npcId, out var jobComp) == false) continue;
                if (jobComp.JobType == GlobalEnum.JobType.None) continue;

                // 매칭되는 작업장 찾기 (점유 X)
                for (int w = 0; w < workplaces.Count; w++)
                {
                    var wp = workplaces[w];
                    if (wp.job != jobComp.JobType) continue;
                    if (_busyEntityIds.Contains(wp.entityId)) continue;

                    // 매칭 — NpcAssignmentComponent 부착
                    AR.s.Component.AddComponent(npcId, new NpcAssignmentComponent
                    {
                        VillageId = v.VillageId,
                        AssignedObjectEntityId = wp.entityId,
                        AssignedTableId = wp.tableId,
                        JobType = jobComp.JobType,
                    });
                    _busyEntityIds.Add(wp.entityId);

                    Tables.BuildableItemTable? table = AR.s.Data.GetBuildableItem(wp.tableId);
                    string tableName = table != null ? table.Name : $"Id{wp.tableId}";
                    Debug.Log($"[JobAssign] v{v.VillageId} npc{npcId}({jobComp.JobType}) → {tableName}({wp.entityId})");
                    break;
                }
            }
        }

        private static bool IsWorkplaceValid(List<(int entityId, int tableId, GlobalEnum.JobType job)> workplaces, int entityId)
        {
            for (int i = 0; i < workplaces.Count; i++)
                if (workplaces[i].entityId == entityId) return true;
            return false;
        }

        private static void ClearAllAssignmentsInVillage(VillageData v)
        {
            for (int i = 0; i < v.NpcEntityIds.Count; i++)
            {
                int npcId = v.NpcEntityIds[i];
                if (AR.s.Component.HasComponent<NpcAssignmentComponent>(npcId))
                    AR.s.Component.RemoveComponent<NpcAssignmentComponent>(npcId);
            }
        }
    }
}
