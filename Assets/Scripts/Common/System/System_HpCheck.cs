using ARPG.Component;
using ARPG.Message;
using ARPG.Utility;
using UnityEngine;

namespace ARPG.Systems
{
    /// <summary>
    /// HP 체크 시스템
    /// HpDirtyTag가 있는 엔티티의 CurrentHp를 검사하여 0 이하일 경우 사망 처리
    ///
    /// 실행 흐름:
    /// 1. HpDirtyTag가 있는 엔티티 순회
    /// 2. CurrentHp가 0 이하인 엔티티 탐지
    /// 3. 상태 변경 (Dead) + 스킬 중지
    /// 4. DropComponent가 있으면 아이템 드랍 처리
    /// 5. DestroyTag 추가 (엔티티 제거 예약)
    /// 6. DeathMessage 전송 (비주얼 이펙트용)
    /// 7. HpDirtyTag 제거
    /// </summary>
    public class System_HpCheck : IFixedUpdateSystem
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

        public void OnFixedUpdate(float inFixedDeltaTime)
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

                // CurrentHp가 0 이하이면 사망 처리
                if (stat.CurrentHp <= 0)
                {
                    // 상태 변경 (죽음)
                    if (cm.TryGetComponent<StateComponent>(entityId, out StateComponent state))
                    {
                        state.Condition = Creature.CharacterConditions.Dead;
                        cm.SetComponent(entityId, state);
                    }

                    // 스킬 중지 처리
                    StopOwnerSkills(cm, entityId);

                    // DropComponent가 있으면 아이템 드랍 처리
                    if (cm.TryGetComponent<DropComponent>(entityId, out DropComponent drop))
                    {
                        if (cm.TryGetComponent<TransformComponent>(entityId, out TransformComponent transform))
                        {
                            Vector3 dropPosition = new Vector3(transform.Position.x, transform.Position.y, -0.01f);
                            DropHelper.ProcessDrop(drop.DropId, dropPosition);
                        }
                    }

                    // 엔티티 제거 예약
                    cm.AddComponent(entityId, new DestroyTag());

                    // 비주얼 이펙트용 DeathMessage 전송
                    AR.s.Message.SendToEntity(new DeathMessage
                    {
                        TargetEntityId = entityId,
                        KillerEntityId = 0
                    });
                }

                // 태그 제거
                cm.RemoveComponent<HpDirtyTag>(entityId);
            }
        }

        /// <summary>
        /// 해당 엔티티가 소유한 모든 스킬을 중지
        /// </summary>
        private void StopOwnerSkills(ComponentManager cm, int ownerEntityId)
        {
            for (int slotIndex = 0; slotIndex < EntityIdHelper.GetMaxIndex(EntityIdCategory.Skill); slotIndex++)
            {
                int skillEntityId = EntityIdHelper.GetDeterministicId(ownerEntityId, EntityIdCategory.Skill, slotIndex);
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
