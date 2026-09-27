using ARPG.Component;
using ARPG.Message;
using ARPG.Utility;
using UnityEngine;

namespace ARPG.Systems
{
    /// <summary>
    /// 엔티티 제거 시스템
    /// DestroyTag가 있는 엔티티를 순회하여 정리 및 GameObject 파괴
    ///
    /// 실행 흐름:
    /// 1. DestroyTag가 있는 엔티티 순회 (역순)
    /// 2. EntityRegistry에서 EntityBase 조회
    /// 3. EntityBase.OnEntityDestroy() 호출 (타입별 정리)
    /// 4. AR.s.Entity.DestroyEntity() 호출 (ECS 컴포넌트 정리)
    /// 5. GameObject 파괴
    /// </summary>
    public class System_EntityDestroy : ILateUpdateSystem
    {
        /// <summary>
        /// Priority 950: System_EntityMessage(900) 이후 실행
        /// DeathMessage 처리 완료 후 제거 수행
        /// </summary>
        public int Priority => 950;

        public void OnCreate()
        {
            Debug.Log("[System_EntityDestroy] Created");
        }

        public void OnReset()
        {
            Debug.Log("[System_EntityDestroy] Reset");
        }

        public void OnLateUpdate(float inDeltaTime)
        {
            ComponentManager cm = AR.s.Component;
            SparseSet<DestroyTag> pool = cm.GetComponentPool<DestroyTag>();

            if (pool == null || pool.Count == 0)
                return;

            for (int i = pool.Count - 1; i >= 0; i--)
            {
                int entityId = pool.GetEntityId(i);

                // 외부 제거(만료, 씬 정리 등)가 정상 전파 완료 경로보다 먼저 도달하면
                // 남은 작업 수를 한 번만 해제한다. 완료 경로는 marker를 남겨 이 블록을 건너뛴다.
                if (cm.HasComponent<SkillPropagationWorkCompletedTag>(entityId) == false)
                {
                    if (cm.TryGetComponent<SkillPropagationRequestComponent>(entityId, out SkillPropagationRequestComponent propagationRequest) == true)
                    {
                        Skill.Propagation.SkillPropagationRuntime.CompleteOutstandingDelivery(propagationRequest.RootEntityId);
                    }
                    else if (cm.TryGetComponent<InstantSkillDeliveryComponent>(entityId, out InstantSkillDeliveryComponent instantDelivery) == true)
                    {
                        Skill.Propagation.SkillPropagationRuntime.CompleteOutstandingDelivery(instantDelivery.RootEntityId);
                    }
                    else if (cm.TryGetComponent<SkillPropagationBranchComponent>(entityId, out SkillPropagationBranchComponent propagationBranch) == true)
                    {
                        Skill.Propagation.SkillPropagationRuntime.CompleteOutstandingDelivery(propagationBranch.RootEntityId);
                    }
                }

                // MonsterTag가 있으면 MonsterManager에서 제거
                if (cm.HasComponent<MonsterTag>(entityId))
                {
                    AR.s.Monster.UnregisterMonsterByEntityId(entityId);
                }

                // NpcTag가 있으면 NpcManager에서 제거
                if (cm.HasComponent<NpcTag>(entityId))
                {
                    // 전투 사망(System_HpCheck가 Condition=Dead 설정)만 영구 사망으로 기록한다.
                    // MarkNpcDead 발 파괴는 내부에서 이미 Dead라 no-op.
                    // Unregister가 saveData.Condition을 Dead로 덮으면 MarkNpcDead가 조기 반환하므로 반드시 먼저 호출.
                    bool combatDeath = cm.TryGetComponent<StateComponent>(entityId, out StateComponent npcState) == true &&
                                       npcState.Condition == Creature.CharacterConditions.Dead;
                    if (combatDeath == true)
                    {
                        AR.s.Npc.MarkNpcDead(entityId, AR.s.Time.CurrentGameTime);
                    }
                    AR.s.Npc.UnregisterNpcByEntityId(entityId);
                    // 동행 중 전투 사망이면 동행을 즉시 끝낸다 (NPC_COMBAT_DESIGN §4.5·§8)
                    if (combatDeath == true)
                    {
                        AR.s.Npc.EndCompanionOnDeath(entityId);
                    }
                }

                // BuildingTag가 있으면 BuildingManager에서 제거
                if (cm.HasComponent<BuildingTag>(entityId))
                {
                    AR.s.Building.UnregisterBuildingByEntityId(entityId);
                }

                // System_Animation에서 애니메이션 데이터 해제
                var animSystem = AR.s.System.GetSystem<System_Animation>();
                if (animSystem != null)
                {
                    animSystem.UnregisterSpriteAnimation(entityId);
                }

                if (AR.s.Message.TryGetEntity(entityId, out var entity))
                {
                    entity.OnEntityDestroy();
                    AR.s.Entity.DestroyEntity(entityId);

                    // System_Render에서도 등록 해제 (딕셔너리 정리)
                    var renderSystem = AR.s.System.GetSystem<System_Render>();
                    if (renderSystem != null)
                    {
                        renderSystem.UnregisterEntity(entityId);
                    }

                    Object.Destroy(entity.gameObject);
                }
                else
                {
                    // MessageManager에 등록되지 않은 엔티티 (발사체 등)
                    var renderSystem = AR.s.System.GetSystem<System_Render>();
                    if (renderSystem != null)
                    {
                        GameObject go = renderSystem.GetGameObject(entityId);
                        if (go != null)
                        {
                            // 발사체는 AddressablePool에 반환
                            if (cm.TryGetComponent<ProjectileComponent>(entityId, out var proj))
                            {
                                var projTable = AR.s.Data.GetProjectile(proj.ProjectileTableId);
                                if (projTable != null && string.IsNullOrEmpty(projTable.PrefabKey) == false)
                                {
                                    AddressablePool.Return(projTable.PrefabKey, go);
                                }
                                else
                                {
                                    Object.Destroy(go);
                                }
                            }
                            // 장판도 AddressablePool에 반환
                            else if (cm.TryGetComponent<AreaEffectComponent>(entityId, out var area))
                            {
                                var areaTable = AR.s.Data.GetAreaEffect(area.AreaEffectTableId);
                                if (areaTable != null && string.IsNullOrEmpty(areaTable.PrefabKey) == false)
                                {
                                    AddressablePool.Return(areaTable.PrefabKey, go);
                                }
                                else
                                {
                                    Object.Destroy(go);
                                }
                            }
                            else
                            {
                                Object.Destroy(go);
                            }
                        }
                        renderSystem.UnregisterEntity(entityId);
                    }

                    AR.s.Entity.DestroyEntity(entityId);
                }
            }
        }
    }
}
