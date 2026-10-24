using ARPG.Component;
using ARPG.Manager;
using ARPG.Message;
using ARPG.Utility;
using UnityEngine;

namespace ARPG.Systems
{
    /// <summary>
    /// HP 체크 + UI 동기화 시스템
    /// HpDirtyTag가 있는 엔티티의 CurrentHp를 검사하여 사망 처리하고, HP바 UI를 갱신한다.
    ///
    /// 실행 흐름:
    /// 1. HpDirtyTag가 있는 엔티티 순회
    /// 2. CurrentHp가 0 이하면 사망 처리 (상태/스킬/드랍/DestroyTag/DeathMessage)
    /// 3. UI 동기화용 DamageMessage(DamageAmount=0) 발송 → HpBarView fillAmount 갱신
    /// 4. HpDirtyTag 제거
    ///
    /// → SetCurrentHp() 호출만으로 UI 갱신이 보장됨. 호출자는 floating text가 필요할 때만
    ///   별도 DamageMessage(상세 정보 포함)를 보내면 된다.
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

                // 결투 참가자는 원인과 관계없이 사망 대신 HP 1로 두고 결투를 끝낸다 (NPC_COMBAT_DESIGN §7.2 D22·Q11).
                // 사망·파괴 파이프라인(던전 플레이어 사망 가로채기 포함)보다 먼저 가로챈다.
                if (stat.CurrentHp <= 0 && AR.s.Npc.TryInterceptDuelDefeat(entityId) == true)
                {
                    cm.TryGetComponent<StatComponent>(entityId, out stat);
                    cm.RemoveComponent<KillCreditComponent>(entityId);
                }

                // CurrentHp가 0 이하이면 사망 처리
                if (stat.CurrentHp <= 0)
                {
                    KillCreditComponent credit = cm.TryGetComponent<KillCreditComponent>(entityId, out var lethal)
                        ? lethal : new KillCreditComponent { SourceEntityId = -1 };
                    if (AR.s.Dungeon.HandleDeath(entityId, credit))
                    {
                        cm.RemoveComponent<HpDirtyTag>(entityId);
                        continue;
                    }
                    // 네임드 위협 처치 (NAMED_THREAT_DESIGN §9.1): 드랍 처리 전에 기록을 끝내고 처치 묶음을 확정한다.
                    // 네임드가 아니면 아무것도 하지 않는다. 사망·드랍·파괴는 아래 원래 경로가 그대로 처리한다.
                    AR.s.NamedThreat.HandleDeath(entityId, credit);
                    // 상태 변경 (죽음)
                    if (cm.TryGetComponent<StateComponent>(entityId, out StateComponent state))
                    {
                        state.Condition = Creature.CharacterConditions.Dead;
                        cm.SetComponent(entityId, state);
                    }

                    // 스킬 중지 처리
                    StopOwnerSkills(cm, entityId);

                    // 처치 경험치 (PLAYER_LEVEL_CHARM_DESIGN §3.2): 플레이어 근처에서 죽은 몬스터는 누가 잡았든 준다.
                    // 한 번만 주도록 읽은 뒤 바로 뗀다.
                    if (cm.TryGetComponent<ExpRewardComponent>(entityId, out ExpRewardComponent expReward) == true)
                    {
                        cm.RemoveComponent<ExpRewardComponent>(entityId);
                        if (cm.TryGetComponent<TransformComponent>(entityId, out TransformComponent deathTransform) == true)
                            AR.s.Player.GrantMonsterKillExp(deathTransform.Position, expReward.MonsterLevel, expReward.ExpMul);
                    }

                    // DropComponent가 있으면 아이템 드랍 처리
                    if (cm.TryGetComponent<DropComponent>(entityId, out DropComponent drop))
                    {
                        if (cm.TryGetComponent<TransformComponent>(entityId, out TransformComponent transform))
                        {
                            Vector3 dropPosition = new Vector3(transform.Position.x, transform.Position.y, -0.01f);
                            DropHelper.ProcessDrops(entityId, drop, dropPosition);
                        }
                    }

                    // 엔티티 제거 예약
                    cm.AddComponent(entityId, new DestroyTag());

                    // 비주얼 이펙트용 DeathMessage 전송
                    AR.s.Message.SendToEntity(new DeathMessage
                    {
                        TargetEntityId = entityId,
                        KillerEntityId = credit.SourceEntityId
                    });
                }

                // HP 동기화 메시지 — HpBarView 등 UI 갱신용
                // DamageAmount=0 → fillAmount만 갱신, 텍스트/이펙트 미표시 (HpBarView.cs:44-46)
                // 호출자가 별도 DamageMessage(상세 정보 포함)를 보낸 경우와 중복돼도 같은 값으로 set되므로 무해
                AR.s.Message.SendToEntity(new DamageMessage
                {
                    TargetEntityId = entityId,
                    DamageAmount = 0,
                    AttackerEntityId = -1,
                    DamageType = GlobalEnum.DamageType.Physics,
                    IsCritical = false,
                    IsEvaded = false,
                    IsBlocked = false,
                    CurrentHp = stat.CurrentHp,
                    MaxHp = stat.FinalMaxHp,
                });

                // 태그 제거
                cm.RemoveComponent<HpDirtyTag>(entityId);
            }
        }

        /// <summary>
        /// 해당 엔티티가 소유한 모든 스킬을 중지
        /// </summary>
        private void StopOwnerSkills(ComponentManager cm, int ownerEntityId)
        {
            for (int slotIndex = 0; slotIndex < EntityManager.GetMaxIndex(EntityIdCategory.Skill); slotIndex++)
            {
                int skillEntityId = EntityManager.GetDeterministicId(ownerEntityId, EntityIdCategory.Skill, slotIndex);
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
