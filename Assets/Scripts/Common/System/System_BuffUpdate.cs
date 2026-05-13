using ARPG.Component;
using ARPG.Skill.Combat;
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

        public void OnUpdate(float inDeltaTime)
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
        private void ProcessBuffTick(int buffEntityId, ref BuffInstance buff, float deltaTime)
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
        private void ApplyTickDamage(int buffEntityId, ref BuffInstance buff)
        {
            // 타겟 엔티티의 StatComponent 가져오기
            if (AR.s.Component.TryGetComponent<StatComponent>(buff.TargetEntityId, out var targetStat) == false)
            {
                Debug.LogWarning($"[System_BuffUpdate] Target entity has no StatComponent - TargetEntityId: {buff.TargetEntityId}, BuffEntityId: {buffEntityId}");
                return;
            }

            // 스택에 따른 총 데미지 계산
            int totalDamage = buff.TickDamage * buff.StackCount;

            // 속성 저항 적용 (DamageType에 따라 저항 계산)
            float reduction = GetReductionForDamageType(buff.DamageType, targetStat, totalDamage);
            int finalDamage = Mathf.Max(1, Mathf.RoundToInt(totalDamage * (1f - reduction)));

            // 데미지 적용 (양수면 데미지, 음수면 힐)
            if (finalDamage > 0)
            {
                // 데미지 처리
                int newHp = Mathf.Max(0, targetStat.CurrentHp - finalDamage);
                targetStat.SetCurrentHp(buff.TargetEntityId, newHp);

                // 데미지 메시지 전송(UI 변경 등)
                AR.s.Message.SendToEntity(new Message.DamageMessage
                {
                    TargetEntityId = buff.TargetEntityId,
                    DamageAmount = finalDamage,
                    AttackerEntityId = -1, // 버프로 인한 데미지이므로 공격자 없음
                    DamageType = buff.DamageType,
                    IsCritical = false,
                    CurrentHp = targetStat.CurrentHp,
                    MaxHp = targetStat.FinalMaxHp
                });

#if UNITY_EDITOR
                var buffTable = AR.s.Data.GetBuff(buff.BuffTableID);
                Debug.Log($"[System_BuffUpdate] Tick Damage - Buff: {buffTable?.Name}, BuffEntityId: {buffEntityId}, Target: {buff.TargetEntityId}, Damage: {finalDamage}, Type: {buff.DamageType}, RemainingHP: {targetStat.CurrentHp}, Stack: {buff.StackCount}");
#else
                Debug.Log($"[System_BuffUpdate] Tick Damage - BuffEntityId: {buffEntityId}, Target: {buff.TargetEntityId}, Damage: {finalDamage}, Type: {buff.DamageType}, RemainingHP: {targetStat.CurrentHp}, Stack: {buff.StackCount}");
#endif
            }
            else if (finalDamage < 0)
            {
                // 힐 처리
                int healAmount = -finalDamage;
                int newHp = Mathf.Min(targetStat.FinalMaxHp, targetStat.CurrentHp + healAmount);
                targetStat.SetCurrentHp(buff.TargetEntityId, newHp);
                Debug.Log($"[System_BuffUpdate] Tick Heal - BuffEntityId: {buffEntityId}, Target: {buff.TargetEntityId}, Heal: {healAmount}, CurrentHP: {targetStat.CurrentHp}, Stack: {buff.StackCount}");
            }

            // 변경된 StatComponent 저장
            AR.s.Component.SetComponent(buff.TargetEntityId, targetStat);
        }

        /// <summary>
        /// DamageType에 대응하는 타겟의 저항값 반환
        /// </summary>
        /// <summary>
        /// DoT 데미지에 적용할 감소율 산출.
        /// 물리: PoE 원본 아머 공식 (Armor / (Armor + 10×Damage)) — 들어오는 데미지에 의존
        /// 원소: PoE 원본 단순 % + Max Resist 캡
        /// </summary>
        private float GetReductionForDamageType(GlobalEnum.DamageType damageType, StatComponent targetStat, int incomingDamage)
        {
            switch (damageType)
            {
                case GlobalEnum.DamageType.Physics:
                    return DamageCalculator.GetArmorReduction(targetStat.FinalDefense, incomingDamage);
                case GlobalEnum.DamageType.Fire:
                    return DamageCalculator.GetResistanceReduction(targetStat.FinalFireResist, targetStat.FinalMaxFireResist);
                case GlobalEnum.DamageType.Ice:
                    return DamageCalculator.GetResistanceReduction(targetStat.FinalIceResist, targetStat.FinalMaxIceResist);
                case GlobalEnum.DamageType.Lightning:
                    return DamageCalculator.GetResistanceReduction(targetStat.FinalLightningResist, targetStat.FinalMaxLightningResist);
                case GlobalEnum.DamageType.Poison:
                    return DamageCalculator.GetResistanceReduction(targetStat.FinalPoisonResist, targetStat.FinalMaxPoisonResist);
                default:
                    return 0f;
            }
        }
    }
}
