#nullable enable
using ARPG.Component;
using ARPG.Tables;
using ARPG.Utility;
using ARPG.Skill.Combat;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace ARPG.Systems
{
    /// <summary>
    /// 설치된 장판의 대기 → 활성화 → 반복 틱/단발 폭발을 처리한다.
    /// 대기 중에는 피해/버프가 없고, 반복 장판의 Lifetime은 활성화 시점부터 시작한다.
    /// 단발 폭발은 설치 당시 공격 프로필과 귀속을 사용하고 같은 틱에 제거를 예약한다.
    ///
    /// Priority 240: System_Skill(200)·System_Jump(220) 이후, System_HpCheck(250) 이전.
    /// 같은 프레임에 데미지 → HP 0 체크가 처리되어 사망 후 정리가 자연스럽게 이어짐.
    /// </summary>
    public class System_AreaEffect : IFixedUpdateSystem
    {
        public int Priority => 240;
        public float UpdateInterval => 0f;

        public void OnCreate()
        {
            Debug.Log("[System_AreaEffect] Created");
        }

        public void OnReset() { }

        public void OnFixedUpdate(float inFixedDeltaTime)
        {
            ComponentManager cm = AR.s.Component;
            SparseSet<AreaEffectComponent> pool = cm.GetComponentPool<AreaEffectComponent>();

            if (pool == null || pool.Count == 0)
                return;

            for (int i = pool.Count - 1; i >= 0; i--)
            {
                int entityId = pool.GetEntityId(i);

                UpdateArea(cm, entityId, pool.GetByIndex(i), inFixedDeltaTime);
            }
        }

        private void UpdateArea(ComponentManager cm, int entityId, AreaEffectComponent area, float deltaTime)
        {
            if (cm.HasComponent<DestroyTag>(entityId) == true)
                return;

            AreaEffectTable? table = AR.s.Data.GetAreaEffect(area.AreaEffectTableId);
            if (table == null)
            {
                cm.AddComponent(entityId, new DestroyTag());
                return;
            }

            if (area.IsActivated == false)
            {
                if (Time.fixedTime + 0.0001f < area.ActivationFixedTime)
                    return;

                area.IsActivated = true;
                cm.RemoveComponent<AttackTelegraphComponent>(entityId);
                if (area.SingleShot == false)
                {
                    cm.AddComponent(entityId, new LifetimeComponent { Remaining = table.Duration });
                    if (cm.TryGetComponent<TransformComponent>(entityId, out var areaTransform) == true)
                        Factory.EntityFactory.AttachAreaEffectVisual(entityId, table, areaTransform.Position);
                }
            }

            area.NextTickIn -= deltaTime;
            if (area.NextTickIn > 0f)
            {
                cm.SetComponent(entityId, area);
                return;
            }

            area.NextTickIn = area.TickInterval > 0f ? area.TickInterval : deltaTime;
            cm.SetComponent(entityId, area);
            // side effect보다 먼저 완료를 기록해 재진입/연속 FixedUpdate에서도 1회만 발동한다.
            if (area.SingleShot == true)
                cm.AddComponent(entityId, new DestroyTag());

            ProcessTick(cm, entityId, area);
            if (area.SingleShot == true && string.IsNullOrEmpty(table.TickEffectName) == false
                && cm.TryGetComponent<TransformComponent>(entityId, out var transform) == true)
            {
                AR.s.Effect.Spawn(table.TickEffectName, transform.Position, Quaternion.identity, scale: area.Radius).Forget();
            }
        }

        private void ProcessTick(ComponentManager cm, int areaEntityId, AreaEffectComponent area)
        {
            AreaEffectTable? table = AR.s.Data.GetAreaEffect(area.AreaEffectTableId);
            if (table == null)
            {
                Debug.LogWarning($"[System_AreaEffect] AreaEffectTable not found: {area.AreaEffectTableId}");
                return;
            }

            if (cm.TryGetComponent<TransformComponent>(areaEntityId, out var areaTransform) == false)
                return;

            Vector2 center = areaTransform.Position;
            float sqrRadius = area.Radius * area.Radius;

            // 진영 풀 순회 — 장판은 FactionComponent를 가지지 않으므로 자기 자신은 자동 제외.
            // 적/아군 판정은 area.CasterFaction 스냅샷 기반 (caster 사망에도 안전).
            SparseSet<FactionComponent> factionPool = cm.GetComponentPool<FactionComponent>();
            for (int i = 0; i < factionPool.Count; i++)
            {
                int candidateId = factionPool.GetEntityId(i);

                if (cm.HasComponent<DestroyTag>(candidateId) == true
                    || cm.TryGetComponent<StatComponent>(candidateId, out var targetStat) == false
                    || targetStat.CurrentHp <= 0)
                    continue;

                FactionComponent candidateFaction = factionPool.GetByIndex(i);

                // Neutral(NPC·중립)·같은 진영(아군) 제외. 결투 쌍 규칙(NPC_COMBAT_DESIGN §3.3)은 IsEntityHostile이 우선 적용
                if (FactionHelper.IsEntityHostile(area.OwnerEntityId, area.CasterFaction, candidateId, candidateFaction.FactionId) == false)
                    continue;

                if (cm.TryGetComponent<TransformComponent>(candidateId, out var candidateTransform) == false)
                    continue;

                if ((candidateTransform.Position - center).sqrMagnitude > sqrRadius)
                    continue;

                ApplyTickToTarget(cm, areaEntityId, area, candidateId, table, center);
            }
        }

        private void ApplyTickToTarget(ComponentManager cm, int areaEntityId, AreaEffectComponent area,
            int targetEntityId, AreaEffectTable table, Vector2 center)
        {
            if (area.SingleShot == true)
            {
                if (cm.TryGetComponent<SkillAttackProfileComponent>(areaEntityId, out var profile) == false)
                    return;

                bool hasDamage = profile.BaseDamageMul > 0f && profile.SkillDamageMul > 0f
                    && profile.PhysAddedMax + profile.FireAddedMax + profile.IceAddedMax
                        + profile.LightAddedMax + profile.PoisonAddedMax > 0;
                if (hasDamage == true)
                {
                    DamageResult result = DamageCalculator.Calculate(profile, targetEntityId);
                    // 폭발 상태이상은 OnTickBuffId로 명시한다. Ice의 자동 Chill과 Slow를 중첩하지 않는다.
                    DamageCalculator.ApplyDamageResult(areaEntityId, areaEntityId, targetEntityId, result, false, area.Credit);
                    if (result.IsEvaded == true)
                        return;
                }
            }
            // 직접 데미지
            else if (table.Damage > 0)
            {
                if (cm.TryGetComponent<StatComponent>(targetEntityId, out var targetStat))
                {
                    int newHp = Mathf.Max(0, targetStat.CurrentHp - table.Damage);
                    CombatCredit.RecordLethalDamage(targetEntityId, targetStat.CurrentHp, newHp, area.Credit);
                    targetStat.SetCurrentHp(targetEntityId, newHp);
                    cm.SetComponent(targetEntityId, targetStat);

                    Debug.Log($"[System_AreaEffect] Tick damage — Owner({area.OwnerEntityId}) → Target({targetEntityId}) {table.Damage} {table.DamageType}, HP {newHp}/{targetStat.FinalMaxHp}");
                }
            }

            // 매 틱 버프 부여 (Poison/Ignite 등)
            if (table.OnTickBuffId > 0)
            {
                BuffTable? buff = AR.s.Data.GetBuff(table.OnTickBuffId);
                if (buff != null)
                {
                    int buffEntityId = BuffHelper.AddBuff(targetEntityId, table.OnTickBuffId, buff.Duration, areaEntityId);
                    if (cm.TryGetComponent<BuffInstance>(buffEntityId, out var instance) == true)
                    {
                        instance.Credit = area.Credit;
                        cm.SetComponent(buffEntityId, instance);
                    }
                }
            }

            // 넉백·당기기는 장판 중심이 기준점 (SKILL_DESIGN §6.10). 이번 틱 피해로 죽은 대상은 ForcedMoveStarter가 거른다
            if (table.ForcedMoveEffectId > 0)
                ForcedMoveStarter.TryStartFromArea(table.ForcedMoveEffectId, targetEntityId, center);
        }
    }
}
