#nullable enable
using ARPG.Component;
using ARPG.Manager;
using ARPG.Tables;
using ARPG.Utility;
using UnityEngine;

namespace ARPG.Systems
{
    /// <summary>
    /// 몬스터 오라 접사 처리 (MONSTER_AFFIX_DESIGN.md §6.3).
    /// 0.5초마다 오라 접사를 가진 몬스터 주변의 같은 진영 엔티티(자기 자신 포함)에 오라 버프를 건다.
    /// 버프 지속(시트 1초)이 검사 주기보다 길어 범위 안에 있는 동안 끊기지 않고, 주인이 죽거나 범위를 벗어나면 곧 만료된다.
    /// 이미 걸린 버프는 남은 시간만 되돌린다 — BuffHelper.AddBuff의 갱신 경로는 매번 스탯 재계산을 요청하기 때문이다.
    /// 같은 오라는 대상당 버프 하나(버프 Id 결정적 엔티티)라 여러 마리여도 겹치지 않는다.
    ///
    /// Priority 140: 게임플레이 구간. 버프 만료(System_BuffUpdate, 40)와의 순서는 지속이 주기보다 길어 상관없다.
    /// </summary>
    public class System_MonsterAura : IUpdateSystem
    {
        public int Priority => 140;
        public float UpdateInterval => 0.5f;

        public void OnCreate()
        {
            Debug.Log("[System_MonsterAura] Created");
        }

        public void OnReset() { }

        public void OnUpdate(float inDeltaTime)
        {
            ComponentManager cm = AR.s.Component;
            SparseSet<MonsterAffixComponent> affixPool = cm.GetComponentPool<MonsterAffixComponent>();
            if (affixPool == null || affixPool.Count == 0)
                return;

            SparseSet<FactionComponent> factionPool = cm.GetComponentPool<FactionComponent>();
            if (factionPool == null || factionPool.Count == 0)
                return;

            for (int i = 0; i < affixPool.Count; i++)
            {
                MonsterAffixComponent affixes = affixPool.GetByIndex(i);
                if (affixes.AuraCount <= 0)
                    continue;

                int sourceId = affixPool.GetEntityId(i);
                if (IsAuraParticipant(cm, sourceId) == false)
                    continue;
                if (cm.TryGetComponent<TransformComponent>(sourceId, out TransformComponent sourceTransform) == false ||
                    cm.TryGetComponent<FactionComponent>(sourceId, out FactionComponent sourceFaction) == false)
                {
                    continue;
                }

                for (int k = 0; k < affixes.Count; k++)
                {
                    MonsterAffixTable? affix = AR.s.Data.GetMonsterAffix(affixes.GetByIndex(k));
                    if (affix == null || affix.Kind != GlobalEnum.MonsterAffixKind.Aura)
                        continue;

                    BuffTable? buffTable = AR.s.Data.GetBuff(affix.AuraBuffId);
                    if (buffTable == null)
                        continue;

                    ApplyAura(cm, factionPool, sourceId, sourceTransform.Position, sourceFaction.FactionId,
                        affix.AuraRadius, buffTable);
                }
            }
        }

        private static void ApplyAura(ComponentManager cm, SparseSet<FactionComponent> factionPool, int sourceId,
            Vector2 center, Faction sourceFaction, float radius, BuffTable buffTable)
        {
            float sqrRadius = radius * radius;
            for (int i = 0; i < factionPool.Count; i++)
            {
                if (factionPool.GetByIndex(i).FactionId != sourceFaction)
                    continue;

                int targetId = factionPool.GetEntityId(i);
                if (IsAuraParticipant(cm, targetId) == false)
                    continue;
                if (cm.TryGetComponent<TransformComponent>(targetId, out TransformComponent targetTransform) == false)
                    continue;
                if ((targetTransform.Position - center).sqrMagnitude > sqrRadius)
                    continue;

                ApplyOrRefreshBuff(cm, targetId, buffTable, sourceId);
            }
        }

        private static void ApplyOrRefreshBuff(ComponentManager cm, int targetId, BuffTable buffTable, int sourceId)
        {
            int buffEntityId = EntityManager.GetDeterministicId(targetId, EntityIdCategory.Buff, buffTable.Id);
            if (buffEntityId >= 0 &&
                AR.s.Entity.IsValidBuffEntity(buffEntityId) == true &&
                cm.TryGetComponent<BuffInstance>(buffEntityId, out BuffInstance buff) == true)
            {
                buff.RemainTime = buff.Duration;
                cm.SetComponent(buffEntityId, buff);
                return;
            }

            BuffHelper.AddBuff(targetId, buffTable.Id, buffTable.Duration, sourceId);
        }

        /// <summary>
        /// 살아 있고, 제거 예약이 없고, 거리 비활성화(Warm 청크 등)되지 않은 스탯 보유 엔티티만 오라를 주고받는다.
        /// </summary>
        private static bool IsAuraParticipant(ComponentManager cm, int entityId)
        {
            if (cm.HasComponent<DestroyTag>(entityId) == true)
                return false;
            if (cm.TryGetComponent<StatComponent>(entityId, out StatComponent stat) == false || stat.CurrentHp <= 0)
                return false;
            if (cm.TryGetComponent<ActivationDistanceComponent>(entityId, out ActivationDistanceComponent activation) == true &&
                activation.IsActivated == false)
            {
                return false;
            }

            return true;
        }
    }
}
