using ARPG.Component;
using ARPG.Message;
using ARPG.Utility;
using UnityEngine;

namespace ARPG.Systems
{
    /// <summary>
    /// HP 체크 시스템
    /// HpDirtyTag가 있는 엔티티의 CurrentHp를 검사하여 0 이하일 경우 DeathMessage 전송
    ///
    /// 실행 흐름:
    /// 1. HpDirtyTag가 있는 엔티티 순회
    /// 2. CurrentHp가 0 이하인 엔티티 탐지
    /// 3. DeathMessage 전송
    /// 4. HpDirtyTag 제거
    /// </summary>
    public struct System_HpCheck : IFixedUpdateSystem
    {
        /// <summary>
        /// Priority 250: 스킬 시스템(200) 이후, 데미지 처리 이후 실행
        /// </summary>
        public int Priority => 250;

        public void OnCreate()
        {
            Debug.Log("[System_HpCheck] Created");
        }

        public void OnReset()
        {
            Debug.Log("[System_HpCheck] Reset");
        }

        public readonly void OnFixedUpdate(float inFixedDeltaTime)
        {
            ComponentManager cm = AR.s.Component;
            SparseSet<HpDirtyTag> dirtyPool = cm.GetComponentPool<HpDirtyTag>();

            if (dirtyPool == null || dirtyPool.Count == 0)
                return;

            for (int i = dirtyPool.Count - 1; i >= 0; i--)
            {
                int entityId = dirtyPool.GetEntityId(i);

                // StatComponent가 없으면 태그만 제거
                if (cm.TryGetComponent<StatComponent>(entityId, out StatComponent stat) == false)
                {
                    cm.RemoveComponent<HpDirtyTag>(entityId);
                    continue;
                }

                // CurrentHp가 0 이하이면 DeathMessage 전송
                if (stat.CurrentHp <= 0) // 죽음 처리
                {
                    // 상태 변경 (죽음)
                    if (cm.TryGetComponent<StateComponent>(entityId, out StateComponent state))
                    {
                        state.Condition = Creature.CharacterConditions.Dead;
                        cm.SetComponent(entityId, state);
                    }

                    // 스킬 중지 처리
                    StopOwnerSkills(cm, entityId);

                    EntityMessenger.Send(new DeathMessage
                    {
                        TargetEntityId = entityId,
                        KillerEntityId = 0 // TODO: 마지막 데미지를 준 엔티티 추적 필요 시 확장
                    });
                }

                // 태그 제거
                cm.RemoveComponent<HpDirtyTag>(entityId);
            }
        }

        /// <summary>
        /// 해당 엔티티가 소유한 모든 스킬을 중지
        /// </summary>
        private readonly void StopOwnerSkills(ComponentManager cm, int ownerEntityId)
        {
            for (int slotIndex = 0; slotIndex < SkillEntityIdHelper.MAX_SKILL_SLOTS; slotIndex++)
            {
                int skillEntityId = SkillEntityIdHelper.GetSkillEntityId(ownerEntityId, slotIndex);
                if (skillEntityId == -1)
                    continue;

                // 스킬이 존재하지 않으면 더 이상 슬롯이 없는 것으로 판단하고 종료
                if (cm.HasComponent<SkillComponent>(skillEntityId) == false)
                    break;

                if (cm.TryGetComponent<SkillStateComponent>(skillEntityId, out SkillStateComponent skillState))
                {
                    if (skillState.IsRunning)
                    {
                        skillState.Reset();
                        cm.SetComponent(skillEntityId, skillState);
                    }
                }
            }
        }
    }
}
