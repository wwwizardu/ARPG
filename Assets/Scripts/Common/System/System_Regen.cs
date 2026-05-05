using ARPG.Component;
using UnityEngine;

namespace ARPG.Systems
{
    /// <summary>
    /// HP/MP 재생 시스템.
    /// RegenComponent가 부착된 엔티티의 FinalHpGeneration / FinalMpGeneration을 매 FixedUpdate
    /// 누적기에 더하고, 정수로 떨어지는 만큼 CurrentHp/CurrentMp에 반영한다.
    ///
    /// Priority 245: AreaEffect(240) 이후, HpCheck(250) 이전.
    /// SetCurrentHp 호출이 HpDirtyTag를 부착하여 같은 틱에 HpCheck가 UI 동기화를 처리한다.
    ///
    /// 음수 재생(0 미만)은 처리하지 않는다 — DoT는 별도 버프 틱(System_BuffUpdate) 영역.
    /// 만피/만마나 도달 시 누적기를 0으로 리셋하여 데미지 후 큰 값이 일시 흡수되는 현상을 방지한다.
    /// </summary>
    public class System_Regen : IFixedUpdateSystem
    {
        public int Priority => 245;
        public float UpdateInterval => 0f;

        public void OnCreate()
        {
            Debug.Log("[System_Regen] Created");
        }

        public void OnReset()
        {
            Debug.Log("[System_Regen] Reset");
        }

        public void OnFixedUpdate(float inFixedDeltaTime)
        {
            ComponentManager cm = AR.s.Component;
            SparseSet<RegenComponent> pool = cm.GetComponentPool<RegenComponent>();

            if (pool == null || pool.Count == 0)
                return;

            for (int i = 0; i < pool.Count; i++)
            {
                int entityId = pool.GetEntityId(i);
                RegenComponent regen = pool.GetByIndex(i);

                if (cm.TryGetComponent<StatComponent>(entityId, out StatComponent stat) == false)
                    continue;

                if (cm.HasComponent<DestroyTag>(entityId))
                    continue;

                if (cm.TryGetComponent<StateComponent>(entityId, out StateComponent state) &&
                    state.Condition == Creature.CharacterConditions.Dead)
                    continue;

                bool statChanged = false;
                bool regenChanged = false;

                // === HP 재생 ===
                if (stat.FinalHpGeneration > 0 && stat.CurrentHp < stat.FinalMaxHp)
                {
                    regen.HpAccumulator += stat.FinalHpGeneration * inFixedDeltaTime;
                    int whole = (int)regen.HpAccumulator;
                    if (whole > 0)
                    {
                        int newHp = Mathf.Min(stat.FinalMaxHp, stat.CurrentHp + whole);
                        stat.SetCurrentHp(entityId, newHp);
                        regen.HpAccumulator -= whole;
                        statChanged = true;
                        if (newHp >= stat.FinalMaxHp)
                            regen.HpAccumulator = 0f;
                    }
                    regenChanged = true;
                }
                else if (regen.HpAccumulator != 0f)
                {
                    regen.HpAccumulator = 0f;
                    regenChanged = true;
                }

                // === MP 재생 ===
                if (stat.FinalMpGeneration > 0 && stat.CurrentMp < stat.FinalMaxMp)
                {
                    regen.MpAccumulator += stat.FinalMpGeneration * inFixedDeltaTime;
                    int whole = (int)regen.MpAccumulator;
                    if (whole > 0)
                    {
                        stat.CurrentMp = Mathf.Min(stat.FinalMaxMp, stat.CurrentMp + whole);
                        regen.MpAccumulator -= whole;
                        statChanged = true;
                        if (stat.CurrentMp >= stat.FinalMaxMp)
                            regen.MpAccumulator = 0f;
                    }
                    regenChanged = true;
                }
                else if (regen.MpAccumulator != 0f)
                {
                    regen.MpAccumulator = 0f;
                    regenChanged = true;
                }

                if (statChanged)
                    cm.SetComponent(entityId, stat);
                if (regenChanged)
                    cm.SetComponent(entityId, regen);
            }
        }
    }
}
