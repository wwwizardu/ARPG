#nullable enable
using ARPG.Component;
using UnityEngine;

namespace ARPG.Systems
{
    /// <summary>
    /// 스킬 시스템 - 스킬의 실행, 상태 전환, 타이밍 처리를 담당
    /// FixedUpdate로 실행하여 일정한 타임스텝 보장
    /// </summary>
    public partial struct System_Skill : IFixedUpdateSystem
    {
        public int Priority => 200; // Move 시스템(100) 이후 실행

        public void OnCreate()
        {
            Debug.Log("System_Skill Created");
        }

        public void OnReset()
        {
            Debug.Log("System_Skill Reset called");
        }

        public readonly void OnFixedUpdate(float inFixedDeltaTime)
        {
            // SkillComponent를 가진 모든 엔티티 순회
            SparseSet<SkillComponent> skillPool = AR.s.Component.GetComponentPool<SkillComponent>();

            for (int i = 0; i < skillPool.Count; i++)
            {
                int entityId = skillPool.GetEntityId(i);
                SkillComponent skill = skillPool.GetByIndex(i);

                // 초기화되지 않은 스킬은 건너뜀
                if (skill.IsInitialized == false)
                    continue;

                // 상태 컴포넌트가 없으면 건너뜀
                if (AR.s.Component.TryGetComponent<SkillStateComponent>(entityId, out var skillState) == false)
                    continue;

                // 타이밍 컴포넌트가 없으면 건너뜀
                if (AR.s.Component.TryGetComponent<SkillTimingComponent>(entityId, out var timing) == false)
                    continue;

                // 1. 커맨드 처리 (다른 시스템에서의 스킬 실행 요청)
                ProcessSkillCommands(entityId);

                // 2. 캐릭터 상태 확인 - 스킬 취소가 필요한 상태인지 체크
                if (AR.s.Component.TryGetComponent<StateComponent>(entityId, out var charState))
                {
                    if (ShouldCancelSkill(charState.Condition))
                    {
                        // 실행 중인 스킬이 있으면 취소
                        if (skillState.IsRunning)
                        {
                            skillState.Reset();
                            AR.s.Component.SetComponent(entityId, skillState);
                            Debug.Log($"[System_Skill] Skill cancelled due to condition: {charState.Condition}");
                        }
                        continue; // 스킬 업데이트 건너뜀
                    }
                }

                // 3. 실행 중인 스킬 업데이트
                if (skillState.IsRunning)
                {
                    UpdateSkillState(entityId, ref skillState, ref timing, inFixedDeltaTime);
                }
            }
        }

        /// <summary>
        /// SkillCommandComponent를 확인하고 처리
        /// </summary>
        private readonly void ProcessSkillCommands(int entityId)
        {
            if (!AR.s.Component.TryGetComponent<SkillCommandComponent>(entityId, out var command))
                return;

            // 이미 처리된 커맨드는 무시
            if (command.IsProcessed || command.CommandType == SkillCommandType.None)
                return;

            // 커맨드 타입에 따라 처리
            bool success = false;
            switch (command.CommandType)
            {
                case SkillCommandType.StartWithEntityTarget:
                    success = StartSkillInternal(entityId, command.TargetType, command.TargetId);
                    break;

                case SkillCommandType.StartWithPositionTarget:
                    success = StartSkillAtPositionInternal(entityId, command.TargetPosition);
                    break;

                case SkillCommandType.StartWithDirectionTarget:
                    success = StartSkillInDirectionInternal(entityId, command.TargetDirection);
                    break;

                case SkillCommandType.Stop:
                    StopSkillInternal(entityId);
                    success = true;
                    break;
            }

            // 커맨드 처리 완료 표시
            command.IsProcessed = true;
            AR.s.Component.SetComponent(entityId, command);

            if (!success)
            {
                Debug.LogWarning($"[System_Skill] Failed to process command: {command.CommandType}");
            }
        }

        /// <summary>
        /// 특정 캐릭터 상태에서 스킬이 취소되어야 하는지 확인
        /// </summary>
        private readonly bool ShouldCancelSkill(Creature.CharacterConditions condition)
        {
            // 스킬을 취소해야 하는 상태들
            // Stunned 이상의 상태는 Input도 영향을 주지 못하므로 스킬도 취소
            return condition >= Creature.CharacterConditions.Stunned;
        }

        /// <summary>
        /// 스킬 상태를 업데이트하고 상태 전환을 처리
        /// </summary>
        private readonly void UpdateSkillState(
            int entityId,
            ref SkillStateComponent state,
            ref SkillTimingComponent timing,
            float deltaTime)
        {
            // 경과 시간 증가
            state.ElapsedTime += deltaTime;

            // 현재 상태에 따라 처리
            switch (state.State)
            {
                case SkillState.Start:
                    ProcessStartState(entityId, ref state, ref timing);
                    break;

                case SkillState.Process:
                    ProcessProcessState(entityId, ref state, ref timing);
                    break;

                case SkillState.End:
                    ProcessEndState(entityId, ref state, ref timing);
                    break;
            }

            // 변경된 상태 저장
            AR.s.Component.SetComponent(entityId, state);
        }

        /// <summary>
        /// Start 상태 처리 - 준비 모션
        /// </summary>
        private readonly void ProcessStartState(
            int entityId,
            ref SkillStateComponent state,
            ref SkillTimingComponent timing)
        {
            if (state.ElapsedTime >= timing.StartDuration)
            {
                // Process 상태로 전환
                state.ChangeState(SkillState.Process);
                OnEnterProcessState(entityId);
            }
        }

        /// <summary>
        /// Process 상태 처리 - 주요 효과 발생 (데미지, 버프 등)
        /// </summary>
        private readonly void ProcessProcessState(
            int entityId,
            ref SkillStateComponent state,
            ref SkillTimingComponent timing)
        {
            if (state.ElapsedTime >= timing.ProcessDuration)
            {
                // End 상태로 전환
                state.ChangeState(SkillState.End);
                OnEnterEndState(entityId);
            }
        }

        /// <summary>
        /// End 상태 처리 - 후딜레이
        /// </summary>
        private readonly void ProcessEndState(
            int entityId,
            ref SkillStateComponent state,
            ref SkillTimingComponent timing)
        {
            if (state.ElapsedTime >= timing.EndDuration)
            {
                // 스킬 종료
                state.ChangeState(SkillState.None);
                OnSkillComplete(entityId);
            }
        }

        /// <summary>
        /// Process 상태 진입 시 호출 - 실제 스킬 효과 발생
        /// </summary>
        private readonly void OnEnterProcessState(int entityId)
        {
            // SkillTarget 컴포넌트에서 타겟 정보 가져오기
            if (!AR.s.Component.TryGetComponent<SkillTargetComponent>(entityId, out var target))
                return;

            // TODO: 여기서 실제 스킬 효과 처리
            // - 데미지 계산 및 적용
            // - 이펙트 생성
            // - 사운드 재생
            // - 버프/디버프 적용 등

            Debug.Log($"[System_Skill] Skill Process - EntityId: {entityId}, Target: {target.TargetId}");
        }

        /// <summary>
        /// End 상태 진입 시 호출
        /// </summary>
        private readonly void OnEnterEndState(int entityId)
        {
            // TODO: 종료 이펙트, 사운드 등
            Debug.Log($"[System_Skill] Skill End - EntityId: {entityId}");
        }

        /// <summary>
        /// 스킬 완료 시 호출
        /// </summary>
        private readonly void OnSkillComplete(int entityId)
        {
            // TODO: 스킬 완료 콜백 처리
            // - 쿨다운 시작
            // - 스킬 슬롯 해제
            // - UI 업데이트 등

            Debug.Log($"[System_Skill] Skill Complete - EntityId: {entityId}");
        }

        #region Internal Helper Methods

        /// <summary>
        /// 엔티티 타겟으로 스킬 시작 (내부 사용)
        /// 외부에서는 SkillCommandComponent를 사용할 것
        /// </summary>
        private readonly bool StartSkillInternal(int entityId, byte targetType, long targetId)
        {
            // 스킬 컴포넌트 확인
            if (!AR.s.Component.TryGetComponent<SkillComponent>(entityId, out var skill))
            {
                Debug.LogWarning($"[System_Skill] Entity {entityId} has no SkillComponent");
                return false;
            }

            // 초기화 확인
            if (!skill.IsInitialized)
            {
                Debug.LogWarning($"[System_Skill] Skill not initialized for entity {entityId}");
                return false;
            }

            // 상태 컴포넌트 확인
            if (!AR.s.Component.TryGetComponent<SkillStateComponent>(entityId, out var state))
            {
                Debug.LogWarning($"[System_Skill] Entity {entityId} has no SkillStateComponent");
                return false;
            }

            // 이미 실행 중인지 확인
            if (state.IsRunning)
            {
                Debug.LogWarning($"[System_Skill] Skill already running for entity {entityId}");
                return false;
            }

            // 타겟 설정
            if (AR.s.Component.TryGetComponent<SkillTargetComponent>(entityId, out var target))
            {
                target.SetEntityTarget(targetType, targetId);
                AR.s.Component.SetComponent(entityId, target);
            }

            // 스킬 시작
            state.ChangeState(SkillState.Start);
            AR.s.Component.SetComponent(entityId, state);

            Debug.Log($"[System_Skill] Skill started - EntityId: {entityId}, TargetId: {targetId}");
            return true;
        }

        /// <summary>
        /// 위치 타겟으로 스킬 시작 (내부 사용)
        /// </summary>
        private readonly bool StartSkillAtPositionInternal(int entityId, Vector2 position)
        {
            // 스킬 컴포넌트 확인
            if (!AR.s.Component.TryGetComponent<SkillComponent>(entityId, out var skill))
                return false;

            if (!skill.IsInitialized)
                return false;

            // 상태 확인
            if (!AR.s.Component.TryGetComponent<SkillStateComponent>(entityId, out var state))
                return false;

            if (state.IsRunning)
                return false;

            // 타겟 설정
            if (AR.s.Component.TryGetComponent<SkillTargetComponent>(entityId, out var target))
            {
                target.SetPositionTarget(position);
                AR.s.Component.SetComponent(entityId, target);
            }

            // 스킬 시작
            state.ChangeState(SkillState.Start);
            AR.s.Component.SetComponent(entityId, state);

            Debug.Log($"[System_Skill] Skill started at position - EntityId: {entityId}, Position: {position}");
            return true;
        }

        /// <summary>
        /// 방향 타겟으로 스킬 시작 (내부 사용)
        /// </summary>
        private readonly bool StartSkillInDirectionInternal(int entityId, Vector2 direction)
        {
            // 스킬 컴포넌트 확인
            if (!AR.s.Component.TryGetComponent<SkillComponent>(entityId, out var skill))
                return false;

            if (!skill.IsInitialized)
                return false;

            // 상태 확인
            if (!AR.s.Component.TryGetComponent<SkillStateComponent>(entityId, out var state))
                return false;

            if (state.IsRunning)
                return false;

            // 타겟 설정
            if (AR.s.Component.TryGetComponent<SkillTargetComponent>(entityId, out var target))
            {
                target.SetDirectionTarget(direction);
                AR.s.Component.SetComponent(entityId, target);
            }

            // 스킬 시작
            state.ChangeState(SkillState.Start);
            AR.s.Component.SetComponent(entityId, state);

            Debug.Log($"[System_Skill] Skill started in direction - EntityId: {entityId}, Direction: {direction}");
            return true;
        }

        /// <summary>
        /// 스킬 강제 중단 (내부 사용)
        /// </summary>
        private readonly void StopSkillInternal(int entityId)
        {
            if (AR.s.Component.TryGetComponent<SkillStateComponent>(entityId, out var state))
            {
                state.Reset();
                AR.s.Component.SetComponent(entityId, state);

                Debug.Log($"[System_Skill] Skill stopped - EntityId: {entityId}");
            }
        }

        #endregion

        #region Public Helper Methods (Component 기반으로 사용 권장)

        /// <summary>
        /// 스킬이 실행 중인지 확인
        /// 이 메서드는 조회만 하므로 public static으로 제공
        /// </summary>
        public static bool IsSkillRunning(int entityId)
        {
            if (AR.s.Component.TryGetComponent<SkillStateComponent>(entityId, out var state))
            {
                return state.IsRunning;
            }
            return false;
        }

        #endregion
    }
}
