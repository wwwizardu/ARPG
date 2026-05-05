using ARPG.Component;
using ARPG.Tables;
using ARPG.Utility;
using UnityEngine;

namespace ARPG.Systems
{
    /// <summary>
    /// 장판(지속 영역 효과) 처리 시스템.
    /// 매 FixedUpdate마다 AreaEffectComponent를 가진 엔티티의 NextTickIn을 차감하고,
    /// 0 이하가 되면 1 틱 발동 — 반경 내 적대 엔티티에 데미지/버프 부여.
    /// 만료(생존시간 종료)는 LifetimeComponent + System_Lifetime이 담당.
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

                // Lifetime이 만료시킨 엔티티는 이번 프레임 LateUpdate에 정리 — 틱 스킵
                if (cm.HasComponent<DestroyTag>(entityId))
                    continue;

                AreaEffectComponent area = pool.GetByIndex(i);

                area.NextTickIn -= inFixedDeltaTime;
                if (area.NextTickIn > 0f)
                {
                    cm.SetComponent(entityId, area);
                    continue;
                }

                // 다음 틱 예약 (TickInterval이 0이면 매 프레임 발동 방지를 위해 1프레임 간격)
                float interval = area.TickInterval > 0f ? area.TickInterval : inFixedDeltaTime;
                area.NextTickIn = interval;
                cm.SetComponent(entityId, area);

                ProcessTick(cm, entityId, area);
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

                FactionComponent candidateFaction = factionPool.GetByIndex(i);

                // Neutral은 NPC·중립 — 타격 대상 아님
                if (candidateFaction.FactionId == Faction.Neutral)
                    continue;

                // 같은 진영(아군) 제외
                if (candidateFaction.FactionId == area.CasterFaction)
                    continue;

                if (cm.TryGetComponent<TransformComponent>(candidateId, out var candidateTransform) == false)
                    continue;

                if ((candidateTransform.Position - center).sqrMagnitude > sqrRadius)
                    continue;

                ApplyTickToTarget(cm, area.OwnerEntityId, candidateId, table);
            }
        }

        private void ApplyTickToTarget(ComponentManager cm, int ownerEntityId, int targetEntityId, AreaEffectTable table)
        {
            // 직접 데미지
            if (table.Damage > 0)
            {
                if (cm.TryGetComponent<StatComponent>(targetEntityId, out var targetStat))
                {
                    int newHp = Mathf.Max(0, targetStat.CurrentHp - table.Damage);
                    targetStat.SetCurrentHp(targetEntityId, newHp);
                    cm.SetComponent(targetEntityId, targetStat);

                    Debug.Log($"[System_AreaEffect] Tick damage — Owner({ownerEntityId}) → Target({targetEntityId}) {table.Damage} {table.DamageType}, HP {newHp}/{targetStat.FinalMaxHp}");
                }
            }

            // 매 틱 버프 부여 (Poison/Ignite 등)
            if (table.OnTickBuffId > 0)
            {
                BuffTable? buff = AR.s.Data.GetBuff(table.OnTickBuffId);
                if (buff != null)
                {
                    BuffHelper.AddBuff(targetEntityId, table.OnTickBuffId, buff.Duration);
                }
            }
        }
    }
}
