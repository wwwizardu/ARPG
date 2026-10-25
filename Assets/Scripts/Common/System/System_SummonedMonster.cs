using ARPG.Component;
using UnityEngine;

namespace ARPG.Systems
{
    /// <summary>
    /// 소환 몬스터를 주인과 함께 정리한다 (SKILL_DESIGN §6.12).
    /// 주인이 죽었거나 사라졌거나 비활성(거리·청크)되면 DestroyTag를 붙인다. 소환 몬스터는 저장하지 않는 일시 존재라
    /// 주인이 화면 밖으로 나가면 숨기지 않고 지운다. 생존 시간 만료는 System_Lifetime이 맡는다.
    /// </summary>
    public class System_SummonedMonster : IFixedUpdateSystem
    {
        public int Priority => 145;  // Monster Aura(140) 이후, Projectile(150) 이전
        public float UpdateInterval => 0f;  // 소환 몬스터는 주술사 1마리당 최대 2마리라 매 틱 확인해도 가볍다

        public void OnCreate()
        {
            Debug.Log("[System_SummonedMonster] Created");
        }

        public void OnReset()
        {
        }

        public void OnFixedUpdate(float inFixedDeltaTime)
        {
            ComponentManager cm = AR.s.Component;
            SparseSet<SummonedMonsterComponent> pool = cm.GetComponentPool<SummonedMonsterComponent>();
            if (pool == null || pool.Count == 0)
                return;

            for (int i = pool.Count - 1; i >= 0; i--)
            {
                int entityId = pool.GetEntityId(i);
                if (cm.HasComponent<DestroyTag>(entityId) == true)
                    continue;

                SummonedMonsterComponent summoned = pool.GetByIndex(i);
                if (IsOwnerPresent(cm, summoned.OwnerEntityId) == true)
                    continue;

                cm.AddComponent(entityId, new DestroyTag());
            }
        }

        private static bool IsOwnerPresent(ComponentManager cm, int ownerEntityId)
        {
            if (AR.s.Entity.IsEntityRegistered(ownerEntityId) == false ||
                cm.HasComponent<DestroyTag>(ownerEntityId) == true)
                return false;

            if (cm.TryGetComponent<StatComponent>(ownerEntityId, out StatComponent ownerStat) == false ||
                ownerStat.CurrentHp <= 0f)
                return false;

            if (cm.TryGetComponent<ActivationDistanceComponent>(ownerEntityId, out ActivationDistanceComponent activation) == true &&
                activation.IsActivated == false)
                return false;

            return AR.s.Monster == null || AR.s.Monster.IsMonsterSourceChunkActive(ownerEntityId) == true;
        }
    }
}
