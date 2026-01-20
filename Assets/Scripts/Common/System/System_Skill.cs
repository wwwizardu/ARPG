#nullable enable
using ARPG.Component;
using Mono.Cecil.Cil;
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
            // SkillComponent를 가진 모든 스킬 엔티티 순회
            SparseSet<SkillComponent> skillPool = AR.s.Component.GetComponentPool<SkillComponent>();

            for (int i = 0; i < skillPool.Count; i++)
            {
                int skillEntityId = skillPool.GetEntityId(i);
                SkillComponent skill = skillPool.GetByIndex(i);

                // 초기화되지 않은 스킬은 건너뜀
                if (skill.IsInitialized == false)
                    continue;

                // 상태 컴포넌트가 없으면 건너뜀
                if (AR.s.Component.TryGetComponent<SkillStateComponent>(skillEntityId, out var skillState) == false)
                    continue;

                if (skillState.IsRunning == true) // 스킬이 실행 중이면 스킬 업데이트
                {
                    // 타이밍 컴포넌트가 없으면 건너뜀
                    if (AR.s.Component.TryGetComponent<SkillTimingComponent>(skillEntityId, out var timing) == false)
                        continue;

                    if (ShouldCancelSkill(skill) == true)
                    {
                        // 실행 중인 스킬이 있으면 취소
                        StopSkillInternal(skillEntityId, ref skill, ref skillState);
                        continue; // 스킬 업데이트 건너뜀
                    }

                    // 3. 실행 중인 스킬 업데이트
                    UpdateSkillState(skillEntityId, ref skillState, ref timing, inFixedDeltaTime);
                }
                else // 실행중이 아니라면 커맨드 처리(스킬 실행 체크)
                {
                    // 캐릭터 엔티티에서 커맨드 확인
                    if (AR.s.Component.TryGetComponent<SkillCommandComponent>(skill.OwnerEntityId, out var command) == false)
                        continue;

                    // 커맨드 스킬 엔티티가 아니라면 다음꺼
                    if(skillEntityId != command.SkillEntityId)
                        continue;

                    ProcessSkillCommands(skillEntityId, ref skill, ref command);
                }
            }
        }

        /// <summary>
        /// SkillCommandComponent를 확인하고 처리
        /// </summary>
        /// <param name="skillEntityId">스킬 엔티티 ID</param>
        /// <param name="skill">스킬 컴포넌트</param>
        private readonly void ProcessSkillCommands(int skillEntityId, ref SkillComponent inSkill, ref SkillCommandComponent inCommand)
        {
            if(AR.s.Component.TryGetComponent<StateComponent>(inSkill.OwnerEntityId, out var charState) == false)
            {
                Debug.LogError($"[System_Skill] Character StateComponent not found (OwnerEntityId: {inSkill.OwnerEntityId})");
                return;
            }

            // 스킬 사용 가능 여부 확인
            if(CheckEnableSkill(charState) == false)
                return;

            // 상태 컴포넌트가 없으면 건너뜀
            if (AR.s.Component.TryGetComponent<SkillStateComponent>(skillEntityId, out var skillState) == false)
            {
                Debug.LogError($"[System_Skill] SkillStateComponent not found for SkillEntityId: {skillEntityId}");
                AR.s.Component.RemoveComponent<SkillCommandComponent>(inSkill.OwnerEntityId);
                return;
            }

            if(AR.s.Component.TryGetComponent<SkillTargetComponent>(skillEntityId, out var target) == false)
            {
                Debug.LogError($"[System_Skill] SkillTargetComponent not found for SkillEntityId: {skillEntityId}");
                AR.s.Component.RemoveComponent<SkillCommandComponent>(inSkill.OwnerEntityId);
                return;
            }

            // 커맨드 타입에 따라 처리
            switch (inCommand.TargetType)
            {
                case GlobalEnum.SkillTargetType.Target:
                    // 마우스 포지션과 가장 가까운 엔티티를 찾아 타겟으로 설정
                    int closestEntityId = FindClosestEntity(inCommand.TargetPosition, inSkill.OwnerEntityId);
                    if (closestEntityId != 0)
                    {
                        target.SetEntityTarget(0, closestEntityId); // targetType은 나중에 확장 가능
                        target.TargetPosition = inCommand.TargetPosition;
                    }
                    else
                    {
                        Debug.LogWarning($"[System_Skill] No target entity found near position: {inCommand.TargetPosition}");
                        AR.s.Component.RemoveComponent<SkillCommandComponent>(inSkill.OwnerEntityId);
                        return;
                    }
                    break;

                case GlobalEnum.SkillTargetType.Range_Circle:
                    // 오너 엔티티의 위치와 마우스 위치로 방향 계산
                    if (AR.s.Component.TryGetComponent<TransformComponent>(inSkill.OwnerEntityId, out var ownerTransform))
                    {
                        Vector2 direction = (inCommand.TargetPosition - ownerTransform.Position).normalized;
                        target.SetPositionTarget(inCommand.TargetPosition);
                        target.SetDirectionTarget(direction);
                    }
                    else
                    {
                        Debug.LogError($"[System_Skill] Owner TransformComponent not found - OwnerEntityId: {inSkill.OwnerEntityId}");
                        AR.s.Component.RemoveComponent<SkillCommandComponent>(inSkill.OwnerEntityId);
                        return;
                    }
                    break;
            }

            // 타겟 설정
            AR.s.Component.SetComponent(skillEntityId, target);

            // 스킬 시작
            OnChangeState(skillEntityId, ref skillState, ref inSkill, SkillState.Start);
            AR.s.Component.SetComponent(skillEntityId, skillState);

            // 커맨드 처리 완료 - 컴포넌트 제거
            AR.s.Component.RemoveComponent<SkillCommandComponent>(inSkill.OwnerEntityId);

            // 캐릭터 상태 변경
            charState.Condition = Creature.CharacterConditions.UseSkill;
            AR.s.Component.SetComponent(inSkill.OwnerEntityId , charState);
        }

        /// <summary>
        /// 특정 캐릭터 상태에서 스킬이 취소되어야 하는지 확인
        /// </summary>
        private readonly bool ShouldCancelSkill(SkillComponent inSkill)
        {
            // 2. 캐릭터 상태 확인 - 스킬 취소가 필요한 상태인지 체크
            if (AR.s.Component.TryGetComponent<StateComponent>(inSkill.OwnerEntityId, out var charState) == false)
            {
                Debug.LogError($"[System_Skill] StateComponent not found for OwnerEntityId: {inSkill.OwnerEntityId}");
                return false; // 상태 컴포넌트가 없으면 취소하지 않음
            }

            // 스킬을 취소해야 하는 상태들
            // Stunned 이상의 상태는 Input도 영향을 주지 못하므로 스킬도 취소
            if(Creature.CharacterConditions.Stunned <= charState.Condition)
            {
                Debug.Log($"[System_Skill] Skill cancelled due to condition: {charState.Condition} (SkillEntityId: {inSkill.OwnerEntityId})");
                return true;
            }

            return false;
        }

        /// <summary>
        /// 스킬 상태를 업데이트하고 상태 전환을 처리
        /// </summary>
        private readonly void UpdateSkillState(int skillEntityId, ref SkillStateComponent state, ref SkillTimingComponent timing, float deltaTime)
        {
            // 경과 시간 증가
            state.ElapsedTime += deltaTime;

            // 현재 상태에 따라 처리
            switch (state.State)
            {
                case SkillState.Start:
                    ProcessStartState(skillEntityId, ref state, ref timing);
                    break;

                case SkillState.Process:
                    ProcessProcessState(skillEntityId, ref state, ref timing);
                    break;

                case SkillState.End:
                    ProcessEndState(skillEntityId, ref state, ref timing);
                    break;
            }

            // 변경된 상태 저장
            AR.s.Component.SetComponent(skillEntityId, state);
        }

        /// <summary>
        /// Start 상태 처리 - 준비 모션
        /// </summary>
        private readonly void ProcessStartState(int skillEntityId, ref SkillStateComponent state, ref SkillTimingComponent timing)
        {
            if (state.ElapsedTime >= timing.StartDuration)
            {
                // Process 상태로 전환
                if (AR.s.Component.TryGetComponent<SkillComponent>(skillEntityId, out var skill))
                {
                    OnChangeState(skillEntityId, ref state, ref skill, SkillState.Process);
                }
            }
        }

        /// <summary>
        /// Process 상태 처리 - 주요 효과 발생 (데미지, 버프 등)
        /// </summary>
        private readonly void ProcessProcessState(int skillEntityId, ref SkillStateComponent state, ref SkillTimingComponent timing)
        {
            // 스킬 컴포넌트 가져오기
            if (!AR.s.Component.TryGetComponent<SkillComponent>(skillEntityId, out var skill))
                return;

            // 스킬 실행 타입에 따라 분기 처리
            switch (skill.ExecutionType)
            {
                case SkillExecutionType.Single:
                case SkillExecutionType.MultiHit:
                    ProcessMultiHitSkill(skillEntityId, ref state, ref skill);
                    break;

                case SkillExecutionType.Channeling:
                    ProcessChannelingSkill(skillEntityId, ref state, ref skill);
                    break;

                case SkillExecutionType.Charge:
                    ProcessChargeSkill(skillEntityId, ref state, ref skill);
                    break;

                case SkillExecutionType.Toggle:
                    ProcessToggleSkill(skillEntityId, ref state, ref skill);
                    break;
            }

            // 스킬 컴포넌트 변경사항 저장
            AR.s.Component.SetComponent(skillEntityId, skill);

            // Process 상태 시간이 끝나면 End 상태로 전환
            if (state.ElapsedTime >= timing.ProcessDuration)
            {
                // End 상태로 전환
                OnChangeState(skillEntityId, ref state, ref skill, SkillState.End);
            }
        }

        /// <summary>
        /// End 상태 처리 - 후딜레이
        /// </summary>
        private readonly void ProcessEndState(int skillEntityId, ref SkillStateComponent state, ref SkillTimingComponent timing)
        {
            if (state.ElapsedTime >= timing.EndDuration)
            {
                // 스킬 종료
                if (AR.s.Component.TryGetComponent<SkillComponent>(skillEntityId, out var skill))
                {
                    OnChangeState(skillEntityId, ref state, ref skill, SkillState.None);
                }
            }
        }

        /// <summary>
        /// 스킬 상태 변경 및 상태 진입 처리
        /// </summary>
        private readonly void OnChangeState(int skillEntityId, ref SkillStateComponent state, ref SkillComponent skill, SkillState newState)
        {
            state.ChangeState(newState);

            // 새로운 상태 진입 처리
            switch (newState)
            {
                case SkillState.Start:
                    OnEnterStartState(skillEntityId, ref skill);
                    break;

                case SkillState.Process:
                    OnEnterProcessState(skillEntityId, ref skill);
                    break;

                case SkillState.End:
                    OnEnterEndState(skillEntityId, ref skill);
                    break;

                case SkillState.None:
                    OnSkillComplete(skillEntityId, ref skill, ref state);
                    break;
            }
        }


        /// <summary>
        /// Start 상태 진입 시 호출 - 스킬 준비 모션 시작
        /// </summary>
        private readonly void OnEnterStartState(int skillEntityId, ref SkillComponent inSkill)
        {
            // AnimatorComponent를 통해 애니메이션 재생 요청
            if (AR.s.Component.TryGetComponent<AnimatorComponent>(inSkill.OwnerEntityId, out var animatorComp))
            {
                if (inSkill.Table != null && string.IsNullOrEmpty(inSkill.Table.AnimationName) == false)
                {
                    int animHash = UnityEngine.Animator.StringToHash(inSkill.Table.AnimationName);
                    animatorComp.RequestAnimation(animHash);
                    AR.s.Component.SetComponent(inSkill.OwnerEntityId, animatorComp);

                    Debug.Log($"[System_Skill] Requested animation '{inSkill.Table.AnimationName}' (Hash: {animHash}) - SkillEntityId: {skillEntityId}, OwnerEntityId: {inSkill.OwnerEntityId}");
                }
                else
                {
                    Debug.LogWarning($"[System_Skill] No animation name set for SkillId: {inSkill.SkillId}");
                }
            }
            else
            {
                Debug.LogWarning($"[System_Skill] AnimatorComponent not found for OwnerEntityId: {inSkill.OwnerEntityId}");
            }

            // TODO: 시작 이펙트, 사운드 등
        }

        /// <summary>
        /// Process 상태 진입 시 호출 - 실제 스킬 효과 발생
        /// </summary>
        private readonly void OnEnterProcessState(int skillEntityId, ref SkillComponent inSkill)
        {
            // SkillTarget 컴포넌트에서 타겟 정보 가져오기
            if (!AR.s.Component.TryGetComponent<SkillTargetComponent>(skillEntityId, out var target))
                return;
        

            // Debug.Log($"[System_Skill] Skill Process - SkillEntityId: {skillEntityId}, Target: {target.TargetId}");
        }

        /// <summary>
        /// End 상태 진입 시 호출
        /// </summary>
        private readonly void OnEnterEndState(int skillEntityId, ref SkillComponent inSkill)
        {
            // TODO: 종료 이펙트, 사운드 등
            // Debug.Log($"[System_Skill] Skill End - SkillEntityId: {skillEntityId}");
        }

        /// <summary>
        /// 스킬 완료 시 호출
        /// </summary>
        private readonly void OnSkillComplete(int skillEntityId, ref SkillComponent inSkill, ref SkillStateComponent inSkillState)
        {
            // 스킬 런타임 데이터 초기화
            inSkill.ResetRuntimeData();
            AR.s.Component.SetComponent(skillEntityId, inSkill);

            // 스킬 상태 초기화
            inSkillState.Reset();
            AR.s.Component.SetComponent(skillEntityId, inSkillState);

            // 캐릭터 상태 초기화
            if(AR.s.Component.TryGetComponent<StateComponent>(inSkill.OwnerEntityId, out var charState) == false)
            {
                Debug.LogError($"[System_Skill] StopSkillInternal - StateComponent not found, EntityId({inSkill.OwnerEntityId})");
                return;
            }

            charState.Condition = Creature.CharacterConditions.Normal;
            AR.s.Component.SetComponent(inSkill.OwnerEntityId, charState);

            // TODO: 스킬 완료 콜백 처리
            // - 쿨다운 시작
            // - 스킬 슬롯 해제
            // - UI 업데이트 등

            Debug.Log($"[System_Skill] Skill Complete - SkillEntityId: {skillEntityId}");
        }

        #region Skill Type Processing Methods

        /// <summary>
        /// MultiHit 타입 스킬 처리 - 일정 간격으로 여러 번 타격
        /// Single 타입은 HitCount=1, HitInterval=0으로 동작
        /// </summary>
        private readonly void ProcessMultiHitSkill(int skillEntityId, ref SkillStateComponent state, ref SkillComponent skill)
        {
            // 모든 히트가 완료되었으면 스킵
            if (skill.CurrentHitIndex >= skill.HitCount)
                return;

            // 다음 히트 타이밍 계산
            float nextHitTime = skill.CurrentHitIndex * skill.HitInterval;

            // 다음 히트 시간이 되었으면 효과 적용
            if (state.ElapsedTime >= nextHitTime)
            {
                ProcessSkillHit(skillEntityId, skill);
                skill.CurrentHitIndex++;

                Debug.Log($"[System_Skill] MultiHit {skill.CurrentHitIndex}/{skill.HitCount} - SkillEntityId: {skillEntityId}");
            }
        }

        /// <summary>
        /// Channeling 타입 스킬 처리 - 입력을 누르고 있는 동안 지속적으로 효과 발동
        /// </summary>
        private readonly void ProcessChannelingSkill(int skillEntityId, ref SkillStateComponent state, ref SkillComponent skill)
        {
            // 입력이 끊기면 스킬 중단함
            if(AR.s.Component.TryGetComponent<SkillCommandComponent>(skill.OwnerEntityId, out var command) == false)
            {
                StopSkillInternal(skillEntityId, ref skill, ref state);
                return;
            }

            // if (!IsInputHeld(skill.OwnerEntityId))
            // {
            //     OnChangeState(skillEntityId, ref state, SkillState.End);
            //     return;
            // }

            // 채널링 간격마다 효과 적용
            if (state.ElapsedTime - skill.LastChannelingTime >= skill.ChannelingInterval)
            {
                ProcessSkillHit(skillEntityId, skill);
                skill.LastChannelingTime = state.ElapsedTime;

                Debug.Log($"[System_Skill] Channeling tick at {state.ElapsedTime:F2}s - SkillEntityId: {skillEntityId}");
            }
        }

        /// <summary>
        /// Charge 타입 스킬 처리 - 누르는 시간에 따라 효과 강도 변화
        /// </summary>
        private readonly void ProcessChargeSkill(int skillEntityId, ref SkillStateComponent state, ref SkillComponent skill)
        {
            // 차징 시간 증가
            skill.CurrentChargeTime = Mathf.Min(state.ElapsedTime, skill.MaxChargeTime);

            // TODO: 입력이 끊기면 현재 차지 레벨로 스킬 발동
            // if (!IsInputHeld(skill.OwnerEntityId))
            // {
            //     float chargeRatio = skill.CurrentChargeTime / skill.MaxChargeTime;
            //     chargeRatio = Mathf.Max(chargeRatio, skill.MinChargeRatio);
            //     ApplySkillEffectWithPower(skillEntityId, skill, chargeRatio);
            //     OnChangeState(skillEntityId, ref state, SkillState.End);
            // }

            Debug.Log($"[System_Skill] Charging {skill.CurrentChargeTime:F2}/{skill.MaxChargeTime:F2}s - SkillEntityId: {skillEntityId}");
        }

        /// <summary>
        /// Toggle 타입 스킬 처리 - ON/OFF 전환
        /// </summary>
        private readonly void ProcessToggleSkill(int skillEntityId, ref SkillStateComponent state, ref SkillComponent skill)
        {
            // Toggle 스킬은 Process 상태에서 ON/OFF 전환만 처리
            if (!state.IsEffectApplied)
            {
                skill.IsToggleActive = !skill.IsToggleActive;

                if (skill.IsToggleActive)
                {
                    // 토글 ON - 버프 적용 등
                    ProcessSkillHit(skillEntityId, skill);
                    Debug.Log($"[System_Skill] Toggle ON - SkillEntityId: {skillEntityId}");
                }
                else
                {
                    // 토글 OFF - 버프 제거 등
                    RemoveSkillEffect(skillEntityId, skill);
                    Debug.Log($"[System_Skill] Toggle OFF - SkillEntityId: {skillEntityId}");
                }

                state.IsEffectApplied = true;
                AR.s.Component.SetComponent(skillEntityId, state);
            }
        }

        /// <summary>
        /// 스킬 히트 처리 - 충돌 판정 후 타겟에 스킬 효과 적용
        /// </summary>
        private readonly void ProcessSkillHit(int skillEntityId, SkillComponent skill)
        {
            // SkillTarget 컴포넌트에서 타겟 정보 가져오기
            if (AR.s.Component.TryGetComponent<SkillTargetComponent>(skillEntityId, out var target) == false)
            {
                Debug.LogWarning($"[System_Skill] SkillTargetComponent not found - SkillEntityId: {skillEntityId}");
                return;
            }

            if(skill.Table == null)
            {
                Debug.LogError($"[System_Skill] SkillTable is null - SkillId({skill.SkillId}) SkillEntityId({skillEntityId})");
                return;
            }

            if(skill.OwnerEntityId == 0)
            {
                int di = 0;
            }

            // 스킬 타겟 타입에 따라 충돌 체크
            System.Collections.Generic.List<int> hitEntities = GetEntitiesInSkillRange(skill, target);

            // 충돌한 엔티티들에게 스킬 효과 적용
            for (int i = 0; i < hitEntities.Count; i++)
            {
                int hitEntityId = hitEntities[i];
                ApplySkillEffectToEntity(skillEntityId, skill, hitEntityId);
            }

            // TODO: 실제 스킬 효과 처리 구현
            // - 데미지 계산 및 적용
            // - 이펙트 생성
            // - 사운드 재생
            // - 버프/디버프 적용
            // - 넉백, CC 효과 등

            Debug.Log($"[System_Skill] ProcessSkillHit - SkillEntityId: {skillEntityId}, OwnerEntityId: {skill.OwnerEntityId}, HitCount: {hitEntities.Count}");
        }

        /// <summary>
        /// 특정 위치에서 가장 가까운 엔티티를 찾습니다
        /// </summary>
        /// <param name="position">기준 위치</param>
        /// <param name="excludeEntityId">제외할 엔티티 ID (보통 자기 자신)</param>
        /// <returns>가장 가까운 엔티티 ID (없으면 0)</returns>
        private readonly int FindClosestEntity(Vector2 position, int excludeEntityId)
        {
            int closestEntityId = 0;
            float closestSqrDistance = float.MaxValue;

            // 모든 TransformComponent를 가진 엔티티 순회
            SparseSet<TransformComponent> transformPool = AR.s.Component.GetComponentPool<TransformComponent>();

            for (int i = 0; i < transformPool.Count; i++)
            {
                int entityId = transformPool.GetEntityId(i);

                // 제외할 엔티티는 건너뜀
                if (entityId == excludeEntityId)
                    continue;

                TransformComponent entityTransform = transformPool.GetByIndex(i);

                // 거리 계산 (제곱 거리 비교로 성능 최적화)
                float sqrDistance = (entityTransform.Position - position).sqrMagnitude;

                if (sqrDistance < closestSqrDistance)
                {
                    closestSqrDistance = sqrDistance;
                    closestEntityId = entityId;
                }
            }

            return closestEntityId;
        }

        /// <summary>
        /// 스킬 범위 내에 있는 엔티티들을 가져옵니다
        /// </summary>
        private readonly System.Collections.Generic.List<int> GetEntitiesInSkillRange(SkillComponent skill, SkillTargetComponent target)
        {
            System.Collections.Generic.List<int> hitEntities = new();

            if (skill.Table == null)
                return hitEntities;

            // 스킬 타겟 타입에 따라 분기
            switch (skill.Table.SkillTargetType)
            {
                case GlobalEnum.SkillTargetType.Target:
                    // 단일 타겟 - TargetId만 체크
                    if (target.TargetId != 0)
                    {
                        hitEntities.Add((int)target.TargetId);
                    }
                    break;

                case GlobalEnum.SkillTargetType.Range_Circle:
                    // 범위 원형 - TargetPosition 중심으로 범위 내 엔티티 체크
                    CheckCircleRangeEntities(skill, target, hitEntities);
                    break;
            }

            return hitEntities;
        }

        /// <summary>
        /// 원형 범위 내 엔티티 체크
        /// Range1: 거리, Range2: 각도 (360도면 전방향, 그 외는 부채꼴)
        /// </summary>
        private readonly void CheckCircleRangeEntities(SkillComponent skill, SkillTargetComponent target, System.Collections.Generic.List<int> outHitEntities)
        {
            if (skill.Table == null)
                return;

            if(AR.s.Component.TryGetComponent<TransformComponent>(skill.OwnerEntityId, out var ownerTransform) == false)
            {
                Debug.LogError($"[System_Skill] Owner TransformComponent not found - OwnerEntityId: {skill.OwnerEntityId}");
                return;
            }              

            Vector2 ownerPosition = ownerTransform.Position;  

            float range = skill.Table.SkillTargetRange1; // 거리
            float angle = skill.Table.SkillTargetRange2; // 각도 (360도면 전방향, 그 외는 부채꼴)

            // 모든 TransformComponent를 가진 엔티티 순회
            SparseSet<TransformComponent> transformPool = AR.s.Component.GetComponentPool<TransformComponent>();

            for (int i = 0; i < transformPool.Count; i++)
            {
                int entityId = transformPool.GetEntityId(i);

                // 자기 자신은 제외
                if (entityId == skill.OwnerEntityId)
                    continue;

                TransformComponent entityTransform = transformPool.GetByIndex(i);

                // 1. 거리 체크 (성능을 위해 제곱 거리 비교)
                float sqrDistance = (entityTransform.Position - ownerPosition).sqrMagnitude;
                float sqrRange = range * range;

                if (sqrDistance > sqrRange)
                    continue;

                // 2. 각도 체크 (360도가 아닐 경우에만)
                if (angle < 360f)
                {
                    // 타겟 방향을 기준으로 각도 체크
                    Vector2 directionToEntity = (entityTransform.Position - ownerPosition).normalized;
                    Vector2 targetDirection = target.TargetDirection.normalized;

                    // 두 벡터 사이의 각도 계산
                    float dotProduct = Vector2.Dot(targetDirection, directionToEntity);
                    float angleToEntity = Mathf.Acos(dotProduct) * Mathf.Rad2Deg;

                    // angle은 한쪽 각도를 의미하므로, 양쪽 각도 체크
                    if (angleToEntity > angle)
                        continue;
                }

                // 거리와 각도 조건을 모두 만족하면 리스트에 추가
                outHitEntities.Add(entityId);
            }
        }

        /// <summary>
        /// 특정 엔티티에게 스킬 효과를 적용합니다
        /// </summary>
        private readonly void ApplySkillEffectToEntity(int skillEntityId, SkillComponent skill, int targetEntityId)
        {
            // 타겟 엔티티의 StatComponent 가져오기
            if (AR.s.Component.TryGetComponent<StatComponent>(targetEntityId, out var targetStat) == false)
            {
                Debug.LogWarning($"[System_Skill] Target entity has no StatComponent - TargetEntityId: {targetEntityId}");
                return;
            }

            // 스킬 테이블 확인
            if (skill.Table == null)
            {
                Debug.LogError($"[System_Skill] SkillTable is null - SkillId: {skill.SkillId}");
                return;
            }

            // 데미지 계산 (DamageMin ~ DamageMax 사이의 랜덤 값)
            int damage = UnityEngine.Random.Range(skill.Table.DamageMin, skill.Table.DamageMax + 1);

            // HP 감소 (0 이하로 떨어지지 않도록 처리)
            int newHp = Mathf.Max(0, targetStat.CurrentHp - damage);
            targetStat.SetCurrentHp(targetEntityId, newHp);

            // 변경된 StatComponent 저장
            AR.s.Component.SetComponent(targetEntityId, targetStat);

            if(skill.Table.DamageType == GlobalEnum.DamageType.Physics)
            {
                // 물리 데미지 처리
                if(AR.s.Component.TryGetComponent<StatComponent>(skill.OwnerEntityId, out var attackerStat) == false)
                {
                    Debug.LogError($"[System_Skill] Attacker StatComponent not found - OwnerEntityId: {skill.OwnerEntityId}");
                    return;
                }

                int BloodingRate = attackerStat.FinalBloodingRate + 50;
                if(UnityEngine.Random.Range(0, 100) < BloodingRate)
                {
                    int bloodingDamage = Mathf.FloorToInt(damage * 0.3f);

                    // 출혈 버프 추가 (BuffTableID: 1 = 출혈 버프, duration: 5초)
                    // TODO: BuffTableID는 실제 테이블 데이터에 맞게 수정 필요
                    int bloodingBuffTableId = 1;  // 출혈 버프의 테이블 ID
                    float bloodingDuration = 5f;   // 출혈 지속 시간

                    int buffEntityId = Utility.BuffHelper.AddBuff(targetEntityId, bloodingBuffTableId, bloodingDuration);
                    if(buffEntityId != -1)
                    {
                        Debug.Log($"[System_Skill] Blooding applied - TargetEntityId: {targetEntityId}, BuffEntityId: {buffEntityId}, Damage: {bloodingDamage}");
                    }
                    else
                    {
                        Debug.LogWarning($"[System_Skill] Failed to apply blooding buff - TargetEntityId: {targetEntityId}");
                    }
                }
            }

            // 데미지 메시지 전송(UI 변경 등)
            Message.EntityMessenger.Send(new Message.DamageMessage
            {
                TargetEntityId = targetEntityId,
                DamageAmount = damage,
                AttackerEntityId = skill.OwnerEntityId,
                DamageType = GlobalEnum.DamageType.Physics,
                IsCritical = false,
                CurrentHp = targetStat.CurrentHp,
                MaxHp = targetStat.FinalMaxHp
            });

            Debug.Log($"[System_Skill] ApplySkillEffectToEntity - SkillEntityId: {skillEntityId}, SkillId: {skill.SkillId}, TargetEntityId: {targetEntityId}, Damage: {damage}, RemainingHP: {targetStat.CurrentHp}");

            // TODO: 추가 구현 필요
            // - 버프/디버프 적용
            // - 넉백, CC 효과 등
            // - 데미지 이펙트, 사운드 재생
        }

        /// <summary>
        /// 스킬 효과를 제거합니다 (주로 Toggle 스킬용)
        /// </summary>
        private readonly void RemoveSkillEffect(int skillEntityId, SkillComponent skill)
        {
            // TODO: 실제 스킬 효과 제거 구현
            // - 버프 제거
            // - 지속 효과 중단
            // - 이펙트 제거

            Debug.Log($"[System_Skill] RemoveSkillEffect - SkillEntityId: {skillEntityId}, OwnerEntityId: {skill.OwnerEntityId}");
        }

        #endregion

        #region Internal Helper Methods

        /// <summary>
        /// 엔티티 타겟으로 스킬 시작 (내부 사용)
        /// 외부에서는 SkillCommandComponent를 사용할 것
        /// </summary>
        private readonly bool StartSkillInternal(int skillEntityId, SkillTargetComponent inTargetComponent)
        {
            // 스킬 컴포넌트 확인
            if (AR.s.Component.TryGetComponent<SkillComponent>(skillEntityId, out var skill) == false)
            {
                Debug.LogWarning($"[System_Skill] Skill entity {skillEntityId} has no SkillComponent");
                return false;
            }

            // 초기화 확인
            if (skill.IsInitialized == false)
            {
                Debug.LogWarning($"[System_Skill] Skill not initialized (SkillEntityId: {skillEntityId})");
                return false;
            }

            // 상태 컴포넌트 확인
            if (AR.s.Component.TryGetComponent<SkillStateComponent>(skillEntityId, out var state) == false)
            {
                Debug.LogWarning($"[System_Skill] Skill entity {skillEntityId} has no SkillStateComponent");
                return false;
            }

            // 이미 실행 중인지 확인
            if (state.IsRunning)
            {
                Debug.LogWarning($"[System_Skill] Skill already running (SkillEntityId: {skillEntityId})");
                return false;
            }



            Debug.Log($"[System_Skill] Skill started - SkillEntityId: {skillEntityId}");
            return true;
        }

        /// <summary>
        /// 위치 타겟으로 스킬 시작 (내부 사용)
        /// </summary>
        private readonly bool StartSkillAtPositionInternal(int skillEntityId, Vector2 position)
        {
            // 스킬 컴포넌트 확인
            if (!AR.s.Component.TryGetComponent<SkillComponent>(skillEntityId, out var skill))
                return false;

            if (!skill.IsInitialized)
                return false;

            // 상태 확인
            if (!AR.s.Component.TryGetComponent<SkillStateComponent>(skillEntityId, out var state))
                return false;

            if (state.IsRunning)
                return false;

            // 타겟 설정
            if (AR.s.Component.TryGetComponent<SkillTargetComponent>(skillEntityId, out var target))
            {
                target.SetPositionTarget(position);
                AR.s.Component.SetComponent(skillEntityId, target);
            }

            // 스킬 시작
            OnChangeState(skillEntityId, ref state, ref skill, SkillState.Start);
            AR.s.Component.SetComponent(skillEntityId, state);

            Debug.Log($"[System_Skill] Skill started at position - SkillEntityId: {skillEntityId}, Position: {position}");
            return true;
        }

        /// <summary>
        /// 방향 타겟으로 스킬 시작 (내부 사용)
        /// </summary>
        private readonly bool StartSkillInDirectionInternal(int skillEntityId, Vector2 direction)
        {
            // 스킬 컴포넌트 확인
            if (AR.s.Component.TryGetComponent<SkillComponent>(skillEntityId, out var skill) == false)
                return false;

            if (skill.IsInitialized == false)
                return false;

            // 상태 확인
            if (AR.s.Component.TryGetComponent<SkillStateComponent>(skillEntityId, out var state) == false)
                return false;

            if (state.IsRunning)
                return false;

            // 타겟 설정
            if (AR.s.Component.TryGetComponent<SkillTargetComponent>(skillEntityId, out var target) == true)
            {
                target.SetDirectionTarget(direction);
                AR.s.Component.SetComponent(skillEntityId, target);
            }

            // 스킬 시작
            OnChangeState(skillEntityId, ref state, ref skill, SkillState.Start);
            AR.s.Component.SetComponent(skillEntityId, state);

            Debug.Log($"[System_Skill] Skill started in direction - SkillEntityId: {skillEntityId}, Direction: {direction}");
            return true;
        }

        /// <summary>
        /// 스킬 강제 중단 (내부 사용)
        /// </summary>
        private readonly void StopSkillInternal(int skillEntityId, ref SkillComponent inSkill, ref SkillStateComponent inState)
        {
            OnSkillComplete(skillEntityId, ref inSkill, ref inState);
            Debug.Log($"[System_Skill] Skill stopped - SkillEntityId: {skillEntityId}");
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

        private readonly bool CheckEnableSkill(StateComponent inCharState)
        {
            // 비정상 상태에서는 스킬 사용 불가    
            if(inCharState.Condition == Creature.CharacterConditions.Normal)
                return true;
        
            return false;
        }

        #endregion
    }
}
