using ARPG.Component;
using ARPG.Utility;
using UnityEngine;

namespace ARPG.Systems
{
    /// <summary>
    /// 버프 업데이트 시스템
    /// 모든 버프의 남은 시간을 감소시키고, 만료된 버프를 제거합니다.
    ///
    /// 실행 흐름:
    /// 1. BuffInstance 컴포넌트를 가진 모든 Entity 순회
    /// 2. RemainTime 감소
    /// 3. RemainTime이 0 이하가 되면 버프 제거
    /// </summary>
    public partial struct System_BuffUpdate : IUpdateSystem
    {
        public int Priority => 40;  // 스킬(200) 이전, 입력(0) 이후 실행

        public void OnCreate()
        {
            Debug.Log("[System_BuffUpdate] Created");
        }

        public void OnReset()
        {
            Debug.Log("[System_BuffUpdate] Reset called");
        }

        public readonly void OnUpdate(float inDeltaTime)
        {
            // BuffInstance 컴포넌트 풀 가져오기
            SparseSet<BuffInstance> buffPool = AR.s.Component.GetComponentPool<BuffInstance>();
            if (buffPool == null || buffPool.Count == 0)
                return;

            // 역순으로 순회 (제거 중에도 안전)
            for (int i = buffPool.Count - 1; i >= 0; i--)
            {
                int buffEntityId = buffPool.GetEntityId(i);
                BuffInstance buff = buffPool.GetByIndex(i);

                // 남은 시간 감소
                buff.RemainTime -= inDeltaTime;

                // 틱 데미지 처리 (TickInterval > 0일 때만)
                if (buff.TickInterval > 0f)
                {
                    ProcessBuffTick(buffEntityId, ref buff, inDeltaTime);
                }

                // 버프 만료 확인
                if (buff.RemainTime <= 0f)
                {
                    // 버프 제거 (BuffHelper가 타겟의 버프 리스트도 정리해줌)
                    BuffHelper.RemoveBuff(buffEntityId);
                }
                else
                {
                    // 시간만 업데이트
                    AR.s.Component.SetComponent(buffEntityId, buff);
                }
            }
        }

        /// <summary>
        /// 버프 틱 처리 - 일정 간격마다 데미지/힐 적용
        /// </summary>
        private readonly void ProcessBuffTick(int buffEntityId, ref BuffInstance buff, float deltaTime)
        {
            // 마지막 틱 이후 경과 시간 계산 (RemainTime이 감소하므로 역방향 계산)
            // LastTickTime은 RemainTime 기준이므로, LastTickTime - RemainTime = 경과 시간
            float timeSinceLastTick = buff.LastTickTime - buff.RemainTime;

            // 틱 간격만큼 시간이 지났는지 확인
            if (timeSinceLastTick >= buff.TickInterval)
            {
                // 틱 횟수 계산 (여러 틱을 한 번에 처리할 수 있음)
                int tickCount = (int)(timeSinceLastTick / buff.TickInterval);

                for (int t = 0; t < tickCount; t++)
                {
                    ApplyTickDamage(buffEntityId, ref buff);
                }

                // 마지막 틱 시간 업데이트
                buff.LastTickTime = buff.RemainTime + (timeSinceLastTick % buff.TickInterval);
            }
        }

        /// <summary>
        /// 틱 데미지 적용 - 스택 수에 따라 데미지 증가
        /// </summary>
        private readonly void ApplyTickDamage(int buffEntityId, ref BuffInstance buff)
        {
            // 타겟 엔티티의 StatComponent 가져오기
            if (AR.s.Component.TryGetComponent<StatComponent>(buff.TargetEntityId, out var targetStat) == false)
            {
                Debug.LogWarning($"[System_BuffUpdate] Target entity has no StatComponent - TargetEntityId: {buff.TargetEntityId}, BuffEntityId: {buffEntityId}");
                return;
            }

            // 스택에 따른 총 데미지 계산
            int totalDamage = buff.TickDamage * buff.StackCount;

            // TODO: 속성 저항 적용 (DamageType에 따라 저항 계산)
            // int finalDamage = CalculateDamageWithResistance(totalDamage, buff.DamageType, targetStat);

            // 현재는 단순 데미지 적용
            int finalDamage = totalDamage;

            // 데미지 적용 (양수면 데미지, 음수면 힐)
            if (finalDamage > 0)
            {
                // 데미지 처리
                targetStat.CurrentHp = Mathf.Max(0, targetStat.CurrentHp - finalDamage);
                Debug.Log($"[System_BuffUpdate] Tick Damage - BuffEntityId: {buffEntityId}, Target: {buff.TargetEntityId}, Damage: {finalDamage}, RemainingHP: {targetStat.CurrentHp}, Stack: {buff.StackCount}");
            }
            else if (finalDamage < 0)
            {
                // 힐 처리
                int healAmount = -finalDamage;
                targetStat.CurrentHp = Mathf.Min(targetStat.FinalMaxHp, targetStat.CurrentHp + healAmount);
                Debug.Log($"[System_BuffUpdate] Tick Heal - BuffEntityId: {buffEntityId}, Target: {buff.TargetEntityId}, Heal: {healAmount}, CurrentHP: {targetStat.CurrentHp}, Stack: {buff.StackCount}");
            }

            // 변경된 StatComponent 저장
            AR.s.Component.SetComponent(buff.TargetEntityId, targetStat);
        }
    }
}
