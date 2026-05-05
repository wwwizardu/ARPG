#nullable enable
using ARPG.Component;
using ARPG.Factory;
using ARPG.Skill.Combat;
using ARPG.Utility;
using UnityEngine;
using GE = GlobalEnum;

namespace ARPG.Systems
{
    /// <summary>
    /// 스킬 시스템 - 스킬의 실행, 상태 전환, 타이밍 처리를 담당
    /// FixedUpdate로 실행하여 일정한 타임스텝 보장
    /// </summary>
    public class System_Skill : IFixedUpdateSystem
    {
        public int Priority => 200; // Move 시스템(100) 이후 실행

        // 매 히트마다 List 할당 방지용 캐시. ProcessSkillHit 내부에서만 사용 (재진입 없음)
        private readonly System.Collections.Generic.List<int> _hitEntitiesCache = new(16);

        public void OnCreate()
        {
            Debug.Log("System_Skill Created");
        }

        public void OnReset()
        {
            Debug.Log("System_Skill Reset called");
        }

        public void OnFixedUpdate(float inFixedDeltaTime)
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
                else // 실행중이 아니라면 쿨타임 감소 + 커맨드 처리
                {
                    // 쿨타임 감소
                    if (skillState.CooldownRemaining > 0f)
                    {
                        skillState.CooldownRemaining -= inFixedDeltaTime;
                        AR.s.Component.SetComponent(skillEntityId, skillState);
                    }
                }

                // 스킬이 실행중이 아닐 때 커맨드 처리(스킬 실행 체크)
                if (skillState.IsRunning == false)
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
        private void ProcessSkillCommands(int skillEntityId, ref SkillComponent inSkill, ref SkillCommandComponent inCommand)
        {
            if(AR.s.Component.TryGetComponent<StateComponent>(inSkill.OwnerEntityId, out var charState) == false)
            {
                Debug.LogError($"[System_Skill] Character StateComponent not found (OwnerEntityId: {inSkill.OwnerEntityId})");
                return;
            }

            // 스킬 사용 가능 여부 확인 (Stunned 등 - SkillCommand는 유지하여 상태 회복 후 자동 시전)
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

            // [SkillEffect] OnSkillCommand 트리거 - 모든 전제조건 통과 후 1회만 발화
            // 위치: CheckEnableSkill / 컴포넌트 검증 이후, 실제 시전 처리 이전
            //   - Stunned 상태에서 효과(예: DelegateToTotem) 누수 방지
            //   - SkillCommand가 매 프레임 들어와도 효과 중복 발동 방지(상태 차단 시 여기 도달 X)
            SkillEffectContext cmdCtx = new()
            {
                SkillEntityId = skillEntityId,
                SkillId = inSkill.SkillId,
                OwnerEntityId = inSkill.OwnerEntityId,
                TargetPosition = inCommand.TargetPosition,
            };
            SkillEffectExecutor.Trigger(GE.SkillTrigger.OnSkillCommand, ref cmdCtx, inSkill.Table?.SkillEffectIds);
            if (cmdCtx.CancelOriginalCast)
            {
                AR.s.Component.RemoveComponent<SkillCommandComponent>(inSkill.OwnerEntityId);
                return;
            }

            // 커맨드 타입에 따라 처리
            switch (inCommand.TargetType)
            {
                case GlobalEnum.SkillTargetType.SingleEntity:
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

                case GlobalEnum.SkillTargetType.Direction:
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

                case GlobalEnum.SkillTargetType.Position:
                    // 지점 지정 - 마우스 위치를 타겟으로 (점프 스킬의 Leap Slam 등)
                    if (AR.s.Component.TryGetComponent<TransformComponent>(inSkill.OwnerEntityId, out var ownerTransformPos))
                    {
                        Vector2 dirToTarget = (inCommand.TargetPosition - ownerTransformPos.Position).normalized;
                        target.SetPositionTarget(inCommand.TargetPosition);
                        target.SetDirectionTarget(dirToTarget);
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

            // 속도 배율 적용 (Attack → 무기 공속 기반 + 공격 속도%, Spell → 시전 속도%)
            if (AR.s.Component.TryGetComponent<SkillTimingComponent>(skillEntityId, out var timing))
            {
                GlobalEnum.SkillTag tags = inSkill.Table != null ? inSkill.Table.Tags : GlobalEnum.SkillTag.None;
                bool isAttack = (tags & GlobalEnum.SkillTag.Attack) != 0;

                // Attack 태그: 무기 공속으로 Base Duration 균등 스케일
                // StartTime/ProcessTime/EndTime을 무기 공속 배율로 모두 스케일 (DamageTime은 비율이라 불변)
                if (isAttack && inSkill.Table != null)
                {
                    float weaponAttackSpeed = GetWeaponAttackSpeed(inSkill.OwnerEntityId);
                    // 스킬 고유 공속 배율 (예: Cleave 80%)을 무기 공속에 먼저 곱함
                    weaponAttackSpeed *= inSkill.Table.BaseAttackSpeedMul / 100f;
                    if (weaponAttackSpeed > 0f)
                    {
                        float weaponMultiplier = 1f / weaponAttackSpeed;
                        timing.BaseStartDuration = inSkill.Table.StartTime * weaponMultiplier;
                        timing.BaseProcessDuration = inSkill.Table.ProcessTime * weaponMultiplier;
                        timing.BaseEndDuration = inSkill.Table.EndTime * weaponMultiplier;
                    }
                }

                float speedMultiplier = GetSkillSpeedMultiplier(inSkill);
                timing.ApplySpeedMultiplier(speedMultiplier);
                AR.s.Component.SetComponent(skillEntityId, timing);

                Debug.Log($"<color=yellow>[System_Skill] Speed applied - SkillId: {inSkill.SkillId}, Tags: {tags}, IsAttack: {isAttack}, SpeedMul: {speedMultiplier:F2}, Duration: {timing.StartDuration:F3}/{timing.ProcessDuration:F3}/{timing.EndDuration:F3} (Total: {timing.TotalDuration:F3}s)</color>");
            }

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
        private bool ShouldCancelSkill(SkillComponent inSkill)
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
        private void UpdateSkillState(int skillEntityId, ref SkillStateComponent state, ref SkillTimingComponent timing, float deltaTime)
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
        private void ProcessStartState(int skillEntityId, ref SkillStateComponent state, ref SkillTimingComponent timing)
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
        private void ProcessProcessState(int skillEntityId, ref SkillStateComponent state, ref SkillTimingComponent timing)
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
            // 단, 플레이어 채널링은 입력 유지가 종료를 결정하므로 ProcessDuration 자동 종료를 스킵
            // (AI 채널링은 InputComponent가 없으므로 ProcessDuration으로 종료됨 → 채널 지속시간 데이터로 활용)
            // ProcessChannelingSkill에서 이미 End로 전이된 경우는 state.State가 더 이상 Process가 아니므로 중복 전이 방지
            bool hasInput = AR.s.Component.HasComponent<InputComponent>(skill.OwnerEntityId);
            bool isPlayerChanneling = (skill.ExecutionType == SkillExecutionType.Channeling) && hasInput;
            if (state.State == SkillState.Process && isPlayerChanneling == false && state.ElapsedTime >= timing.ProcessDuration)
            {
                OnChangeState(skillEntityId, ref state, ref skill, SkillState.End);
            }
        }

        /// <summary>
        /// End 상태 처리 - 후딜레이
        /// </summary>
        private void ProcessEndState(int skillEntityId, ref SkillStateComponent state, ref SkillTimingComponent timing)
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
        private void OnChangeState(int skillEntityId, ref SkillStateComponent state, ref SkillComponent skill, SkillState newState)
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
        /// Start 상태 진입 시 호출 - 스킬 준비 함수 호출
        /// 현재는 애니메이션 없이 준비 로직만 실행 (StartTime이 0이면 즉시 Process로 전환됨)
        /// </summary>
        private void OnEnterStartState(int skillEntityId, ref SkillComponent inSkill)
        {
            // 점프 스킬이면 JumpComponent 생성
            if (inSkill.Table != null && inSkill.Table.SkillType == GlobalEnum.SkillType.Jump && inSkill.Table.ArcHeight > 0f)
            {
                StartJump(skillEntityId, ref inSkill);
            }

            // TODO: 나중에 준비 애니메이션이 생기면 여기서 재생 (StartDuration 기준)
            // TODO: 시작 이펙트, 사운드 등
        }

        /// <summary>
        /// 점프 스킬 시작 - JumpComponent 생성 및 파라미터 세팅
        /// 체공 시간은 SkillTiming의 TotalDuration, 착지 위치는 SkillTargetType에 따라 결정
        /// </summary>
        private void StartJump(int skillEntityId, ref SkillComponent inSkill)
        {
            ComponentManager cm = AR.s.Component;

            if (cm.TryGetComponent<TransformComponent>(inSkill.OwnerEntityId, out var ownerTransform) == false)
            {
                Debug.LogError($"[System_Skill] StartJump - Owner TransformComponent not found, OwnerEntityId: {inSkill.OwnerEntityId}");
                return;
            }

            if (cm.TryGetComponent<SkillTimingComponent>(skillEntityId, out var timing) == false)
            {
                Debug.LogError($"[System_Skill] StartJump - SkillTimingComponent not found, SkillEntityId: {skillEntityId}");
                return;
            }

            if (inSkill.Table == null)
                return;

            Vector2 startPos = ownerTransform.Position;
            Vector2 endPos = startPos;

            // 착지 위치는 SkillTargetType에 따라 결정
            if (cm.TryGetComponent<SkillTargetComponent>(skillEntityId, out var target) == true)
            {
                float maxDistance = inSkill.Table.SkillRangeMax;

                switch (inSkill.Table.SkillTargetType)
                {
                    case GlobalEnum.SkillTargetType.Direction:
                        // 방향으로 최대 거리만큼 이동
                        if (maxDistance > 0f)
                        {
                            endPos = startPos + target.TargetDirection * maxDistance;
                        }
                        break;

                    case GlobalEnum.SkillTargetType.Position:
                        // 지점으로 이동 (최대 거리 제한)
                        Vector2 toTarget = target.TargetPosition - startPos;
                        float distSqr = toTarget.sqrMagnitude;
                        if (maxDistance > 0f && distSqr > maxDistance * maxDistance)
                        {
                            endPos = startPos + toTarget.normalized * maxDistance;
                        }
                        else
                        {
                            endPos = target.TargetPosition;
                        }
                        break;

                    case GlobalEnum.SkillTargetType.SingleEntity:
                        // 제자리 점프
                        endPos = startPos;
                        break;
                }
            }

            // JumpComponent 생성
            JumpComponent jump = new JumpComponent
            {
                Height = 0f,
                Elapsed = 0f,
                Duration = timing.TotalDuration,
                MaxHeight = inSkill.Table.ArcHeight,
                StartPosition = startPos,
                EndPosition = endPos,
            };
            cm.AddComponent(inSkill.OwnerEntityId, jump);

            // 이동 상태 변경
            if (cm.TryGetComponent<StateComponent>(inSkill.OwnerEntityId, out var state) == true)
            {
                state.MovementStatePrev = state.MoveState;
                state.MoveState = Creature.MovementStates.Jumping;
                cm.SetComponent(inSkill.OwnerEntityId, state);
            }

            Debug.Log($"[System_Skill] StartJump - EntityId: {inSkill.OwnerEntityId}, Start: {startPos}, End: {endPos}, MaxHeight: {jump.MaxHeight}, Duration: {jump.Duration:F2}s");
        }

        /// <summary>
        /// Process 상태 진입 시 호출 - 실제 스킬 효과 발생 + 애니메이션 재생
        /// </summary>
        private void OnEnterProcessState(int skillEntityId, ref SkillComponent inSkill)
        {
            // [SkillEffect] OnSkillStart 트리거
            SkillEffectContext startCtx = new()
            {
                SkillEntityId = skillEntityId,
                SkillId = inSkill.SkillId,
                OwnerEntityId = inSkill.OwnerEntityId,
            };
            SkillEffectExecutor.Trigger(GE.SkillTrigger.OnSkillStart, ref startCtx, inSkill.Table?.SkillEffectIds);

            // SkillTarget 컴포넌트에서 타겟 정보 가져오기
            if (!AR.s.Component.TryGetComponent<SkillTargetComponent>(skillEntityId, out var target))
                return;

            // [AreaEffect] SkillTable.AreaEffectId > 0이면 장판 스폰
            // Position 타겟은 마우스/지정 위치, 그 외는 caster 위치 기준.
            if (inSkill.Table != null && inSkill.Table.AreaEffectId > 0)
            {
                Vector2 spawnPos;
                if (inSkill.Table.SkillTargetType == GE.SkillTargetType.Position)
                {
                    spawnPos = target.TargetPosition;
                }
                else if (AR.s.Component.TryGetComponent<TransformComponent>(inSkill.OwnerEntityId, out var ownerTr))
                {
                    spawnPos = ownerTr.Position;
                }
                else
                {
                    spawnPos = Vector2.zero;
                }
                EntityFactory.CreateAreaEffect(inSkill.OwnerEntityId, inSkill.Table.AreaEffectId, spawnPos, inSkill.SkillId);
            }

            // Process 기간 동안 애니메이션 재생 요청
            // DamageTime(비율) * ProcessDuration 지점에서 타격 프레임이 나오도록 에셋을 구성
            if (AR.s.Component.TryGetComponent<AnimatorComponent>(inSkill.OwnerEntityId, out var animatorComp))
            {
                if (inSkill.Table != null && string.IsNullOrEmpty(inSkill.Table.AnimationName) == false)
                {
                    if (System.Enum.TryParse<GlobalEnum.AnimCategory>(inSkill.Table.AnimationName, out var category))
                    {
                        float duration = 0f;
                        if (AR.s.Component.TryGetComponent<SkillTimingComponent>(skillEntityId, out var timing))
                        {
                            duration = timing.ProcessDuration;
                        }

                        animatorComp.RequestAnimation(category, true, duration);
                        AR.s.Component.SetComponent(inSkill.OwnerEntityId, animatorComp);

                        Debug.Log($"[System_Skill] Requested animation '{category}' (ProcessDuration: {duration:F3}) - SkillEntityId: {skillEntityId}, OwnerEntityId: {inSkill.OwnerEntityId}");
                    }
                    else
                    {
                        Debug.LogWarning($"[System_Skill] Invalid AnimCategory name: '{inSkill.Table.AnimationName}' for SkillId: {inSkill.SkillId}");
                    }
                }
            }
        }

        /// <summary>
        /// End 상태 진입 시 호출
        /// </summary>
        private void OnEnterEndState(int skillEntityId, ref SkillComponent inSkill)
        {
            // [SkillEffect] OnSkillEnd 트리거
            SkillEffectContext endCtx = new()
            {
                SkillEntityId = skillEntityId,
                SkillId = inSkill.SkillId,
                OwnerEntityId = inSkill.OwnerEntityId,
            };
            SkillEffectExecutor.Trigger(GE.SkillTrigger.OnSkillEnd, ref endCtx, inSkill.Table?.SkillEffectIds);

            // TODO: 종료 이펙트, 사운드 등
            // Debug.Log($"[System_Skill] Skill End - SkillEntityId: {skillEntityId}");
        }

        /// <summary>
        /// 스킬 완료 시 호출
        /// </summary>
        private void OnSkillComplete(int skillEntityId, ref SkillComponent inSkill, ref SkillStateComponent inSkillState)
        {
            // 스킬 런타임 데이터 초기화
            inSkill.ResetRuntimeData();
            AR.s.Component.SetComponent(skillEntityId, inSkill);

            // 스킬 상태 초기화
            inSkillState.Reset();

            // 쿨타임 세팅 (CooldownReduction 적용)
            if (inSkill.Table != null && inSkill.Table.Cooltime > 0f)
            {
                float cooldown = inSkill.Table.Cooltime;
                if (AR.s.Component.TryGetComponent<StatComponent>(inSkill.OwnerEntityId, out var ownerStat))
                {
                    float cdr = Mathf.Clamp(ownerStat.FinalCooldownReduction, 0, 90) / 100f;
                    cooldown *= (1f - cdr);
                }
                inSkillState.CooldownRemaining = cooldown;
            }

            AR.s.Component.SetComponent(skillEntityId, inSkillState);

            // 캐릭터 상태 초기화
            if(AR.s.Component.TryGetComponent<StateComponent>(inSkill.OwnerEntityId, out var charState) == false)
            {
                Debug.LogError($"[System_Skill] StopSkillInternal - StateComponent not found, EntityId({inSkill.OwnerEntityId})");
                return;
            }

            charState.Condition = Creature.CharacterConditions.Normal;
            AR.s.Component.SetComponent(inSkill.OwnerEntityId, charState);

            Debug.Log($"[System_Skill] Skill Complete - SkillEntityId: {skillEntityId}");
        }

        /// <summary>
        /// 엔티티의 장착 무기 공격 속도 (초당 공격 횟수) 반환
        /// WeaponHelper가 캐시된 Local 파이프라인 결과를 반환
        /// 무기 없으면 1.0 (맨손 기본값)
        /// </summary>
        private float GetWeaponAttackSpeed(int ownerEntityId)
        {
            float weaponAS = Utility.WeaponHelper.GetWeaponAttackSpeed(ownerEntityId);
            return weaponAS > 0f ? weaponAS : 1f;
        }

        /// <summary>
        /// 스킬 태그에 따라 속도 배율 결정
        /// Attack → (100 + FinalAttackSpeed) / 100, Spell → (100 + FinalCastSpeed) / 100
        /// </summary>
        /// <summary>
        /// 스킬 태그에 따라 속도 배율 결정
        /// Attack → (100 + FinalAttackSpeed) / 100, Spell → (100 + FinalCastSpeed) / 100
        /// FinalAttackSpeed 0 = 기본 1배속, 50 = 1.5배속, 100 = 2배속, -20 = 0.8배속
        /// </summary>
        private float GetSkillSpeedMultiplier(SkillComponent skill)
        {
            if (skill.Table == null)
                return 1f;

            if (AR.s.Component.TryGetComponent<StatComponent>(skill.OwnerEntityId, out var stat) == false)
                return 1f;

            GlobalEnum.SkillTag tags = skill.Table.Tags;
            bool isAttack = (tags & GlobalEnum.SkillTag.Attack) != 0;
            bool isSpell = (tags & GlobalEnum.SkillTag.Spell) != 0;

            float multiplier = 1f;

            if (isAttack && isSpell == false)
            {
                multiplier = (100 + stat.FinalAttackSpeed) / 100f;
            }
            else if (isSpell && isAttack == false)
            {
                multiplier = (100 + stat.FinalCastSpeed) / 100f;
            }

            return Mathf.Clamp(multiplier, 0.1f, 5f);
        }

        #region Skill Type Processing Methods

        /// <summary>
        /// MultiHit 타입 스킬 처리 - DamageTime(Process 내부 비율) 지점에서 첫 히트, HitInterval 간격으로 반복
        /// Single 타입은 HitCount=1, HitInterval=0으로 동작
        /// </summary>
        private void ProcessMultiHitSkill(int skillEntityId, ref SkillStateComponent state, ref SkillComponent skill)
        {
            // 모든 히트가 완료되었으면 스킵
            if (skill.CurrentHitIndex >= skill.HitCount)
                return;

            // Process 기간 가져오기
            float processDuration = 0f;
            if (AR.s.Component.TryGetComponent<SkillTimingComponent>(skillEntityId, out var timing))
            {
                processDuration = timing.ProcessDuration;
            }

            // DamageTime(비율) * ProcessDuration = 첫 히트 오프셋
            float damageRatio = skill.Table != null ? Mathf.Clamp01(skill.Table.DamageTime) : 0f;
            float firstHitOffset = damageRatio * processDuration;
            float nextHitTime = firstHitOffset + skill.CurrentHitIndex * skill.HitInterval;

            // 다음 히트 시간이 되었으면 효과 적용
            if (state.ElapsedTime >= nextHitTime)
            {
                ProcessSkillHit(skillEntityId, skill);
                skill.CurrentHitIndex++;

                Debug.Log($"[System_Skill] MultiHit {skill.CurrentHitIndex}/{skill.HitCount} at {state.ElapsedTime:F3}s - SkillEntityId: {skillEntityId}");
            }
        }

        /// <summary>
        /// Channeling 타입 스킬 처리
        /// - 플레이어(InputComponent 보유): 슬롯 키를 유지하는 동안 tick. 입력을 떼면 End 상태로 전이하여 후딜레이/쿨타임 정상 처리
        /// - AI(InputComponent 없음): 항상 held 취급. 종료는 ProcessProcessState의 ProcessDuration 자동 종료 경로에서 처리
        /// </summary>
        private void ProcessChannelingSkill(int skillEntityId, ref SkillStateComponent state, ref SkillComponent skill)
        {
            bool isHeld;
            if (AR.s.Component.TryGetComponent<InputComponent>(skill.OwnerEntityId, out var input))
            {
                int slotBit = 1 << skill.SlotIndex;
                isHeld = (input.SkillSlotHeldMask & slotBit) != 0;
            }
            else
            {
                // AI 채널링: ProcessDuration이 종료를 결정
                isHeld = true;
            }

            if (isHeld == false)
            {
                // 입력을 뗀 경우 End 상태 경유 (후딜레이 + 쿨타임 적용)
                OnChangeState(skillEntityId, ref state, ref skill, SkillState.End);
                return;
            }

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
        private void ProcessChargeSkill(int skillEntityId, ref SkillStateComponent state, ref SkillComponent skill)
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
        private void ProcessToggleSkill(int skillEntityId, ref SkillStateComponent state, ref SkillComponent skill)
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
        private void ProcessSkillHit(int skillEntityId, SkillComponent skill)
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

            // 발사체 스킬인 경우 발사체 생성 (BaseProjectileCount + Stat.ProjectileCountAdd만큼 부채꼴 발사)
            if (skill.Table.ProjectileId > 0)
            {
                if (AR.s.Component.TryGetComponent<TransformComponent>(skill.OwnerEntityId, out var ownerTransform))
                {
                    const float SPREAD_ANGLE_PER_SHOT = 15f;  // 발사체 간 각도(도). 추후 Stat 합산 도입 시 교체

                    // 발사 시작점 = owner의 몸통 중심 (HitOffset 적용). 타겟도 몸통 기준이라 방향이 직관적
                    Vector2 spawnOrigin = ownerTransform.Position;
                    if (AR.s.Component.TryGetComponent<ColliderComponent>(skill.OwnerEntityId, out var ownerCollider))
                    {
                        spawnOrigin += ownerCollider.HitOffset;
                    }

                    Vector2 baseDir = target.TargetPosition - spawnOrigin;
                    if (baseDir.sqrMagnitude < 0.0001f)
                        baseDir = Vector2.right;
                    else
                        baseDir.Normalize();
                    float baseAngle = Mathf.Atan2(baseDir.y, baseDir.x) * Mathf.Rad2Deg;

                    int baseCount = Mathf.Max(1, skill.Table.BaseProjectileCount);
                    int extraCount = 0;
                    if (AR.s.Component.TryGetComponent<StatComponent>(skill.OwnerEntityId, out var ownerStat))
                        extraCount = ownerStat.FinalProjectileCountAdd;
                    int finalCount = Mathf.Max(1, baseCount + extraCount);

                    float totalSpread = (finalCount - 1) * SPREAD_ANGLE_PER_SHOT;
                    float startOffset = -totalSpread * 0.5f;

                    for (int i = 0; i < finalCount; i++)
                    {
                        float angle = baseAngle + startOffset + SPREAD_ANGLE_PER_SHOT * i;
                        float rad = angle * Mathf.Deg2Rad;
                        Vector2 dir = new Vector2(Mathf.Cos(rad), Mathf.Sin(rad));
                        Utility.ProjectileHelper.SpawnProjectile(
                            skill.OwnerEntityId,
                            skillEntityId,
                            skill.Table.ProjectileId,
                            spawnOrigin,
                            dir
                        );
                    }
                }

                return;
            }

            // 즉발 스킬: 기존 로직
            System.Collections.Generic.List<int> hitEntities = GetEntitiesInSkillRange(skill, target);

            for (int i = 0; i < hitEntities.Count; i++)
            {
                int hitEntityId = hitEntities[i];
                ApplySkillEffectToEntity(skillEntityId, skill, hitEntityId);
            }

            Debug.Log($"[System_Skill] ProcessSkillHit - SkillEntityId: {skillEntityId}, OwnerEntityId: {skill.OwnerEntityId}, HitCount: {hitEntities.Count}");
        }

        /// <summary>
        /// 특정 위치에서 가장 가까운 적 엔티티를 찾습니다 (진영 필터 적용).
        /// 타겟의 ColliderComponent.HitOffset을 적용한 몸통 중심 기준으로 비교 — 마우스 hover와 시각이 일치
        /// </summary>
        /// <param name="position">기준 위치(마우스 월드 좌표 등)</param>
        /// <param name="casterEntityId">시전자 엔티티 ID (자기 자신 + 같은 진영 제외)</param>
        /// <returns>가장 가까운 적 엔티티 ID (없으면 0)</returns>
        private int FindClosestEntity(Vector2 position, int casterEntityId)
        {
            int closestEntityId = 0;
            float closestSqrDistance = float.MaxValue;

            ComponentManager cm = AR.s.Component;

            // 모든 TransformComponent를 가진 엔티티 순회
            SparseSet<TransformComponent> transformPool = cm.GetComponentPool<TransformComponent>();

            for (int i = 0; i < transformPool.Count; i++)
            {
                int entityId = transformPool.GetEntityId(i);

                // 자기 자신 제외
                if (entityId == casterEntityId)
                    continue;

                // 진영 필터 (caster가 진영 없으면 기존 동작 유지)
                if (FactionHelper.IsHostileTo(casterEntityId, entityId) == false)
                    continue;

                // 몸통 중심 기준 거리 비교 (HitOffset 없으면 발 좌표 그대로)
                TransformComponent entityTransform = transformPool.GetByIndex(i);
                Vector2 entityCenter = entityTransform.Position;
                if (cm.TryGetComponent<ColliderComponent>(entityId, out var entityCollider))
                {
                    entityCenter += entityCollider.HitOffset;
                }

                float sqrDistance = HitboxMath.SqrDistance(entityCenter, position);

                if (sqrDistance < closestSqrDistance)
                {
                    closestSqrDistance = sqrDistance;
                    closestEntityId = entityId;
                }
            }

            return closestEntityId;
        }


        /// <summary>
        /// 스킬 범위 내에 있는 엔티티들을 가져옵니다.
        /// 반환 List는 _hitEntitiesCache를 재사용하므로, 다음 호출 전에 소비해야 함.
        /// </summary>
        private System.Collections.Generic.List<int> GetEntitiesInSkillRange(SkillComponent skill, SkillTargetComponent target)
        {
            _hitEntitiesCache.Clear();

            if (skill.Table == null)
                return _hitEntitiesCache;

            // 스킬 타겟 타입에 따라 분기
            switch (skill.Table.SkillTargetType)
            {
                case GlobalEnum.SkillTargetType.SingleEntity:
                    // 단일 타겟 - TargetId만 체크
                    if (target.TargetId != 0)
                    {
                        _hitEntitiesCache.Add((int)target.TargetId);
                    }
                    break;

                case GlobalEnum.SkillTargetType.Direction:
                    // 범위 원형 - TargetPosition 중심으로 범위 내 엔티티 체크
                    CheckCircleRangeEntities(skill, target, _hitEntitiesCache);
                    break;
            }

            return _hitEntitiesCache;
        }

        /// <summary>
        /// 원형 범위 내 엔티티 체크
        /// Range1: 거리, Range2: 각도 (360도면 전방향, 그 외는 부채꼴 한쪽 각도)
        /// 시전자 발 좌표를 기준점으로 사용하고, 타겟은 ColliderComponent.HitOffset/HitRadius로 보정
        /// </summary>
        private void CheckCircleRangeEntities(SkillComponent skill, SkillTargetComponent target, System.Collections.Generic.List<int> outHitEntities)
        {
            if (skill.Table == null)
                return;

            ComponentManager cm = AR.s.Component;

            if(cm.TryGetComponent<TransformComponent>(skill.OwnerEntityId, out var ownerTransform) == false)
            {
                Debug.LogError($"[System_Skill] Owner TransformComponent not found - OwnerEntityId: {skill.OwnerEntityId}");
                return;
            }

            // 시전자 시점 기준은 발 좌표 그대로 (스킬 사거리 기획 의미상 발 기준)
            Vector2 originPosition = ownerTransform.Position;

            float range = skill.Table.SkillTargetRange1;       // 거리
            float halfAngleDeg = skill.Table.SkillTargetRange2; // 한쪽 각도 (360이면 전방향)
            Vector2 forward = target.TargetDirection;

            // 모든 TransformComponent를 가진 엔티티 순회
            SparseSet<TransformComponent> transformPool = cm.GetComponentPool<TransformComponent>();

            for (int i = 0; i < transformPool.Count; i++)
            {
                int entityId = transformPool.GetEntityId(i);

                // 자기 자신은 제외
                if (entityId == skill.OwnerEntityId)
                    continue;

                // 진영 필터 (적대 관계만 타격)
                if (FactionHelper.IsHostileTo(skill.OwnerEntityId, entityId) == false)
                    continue;

                // 타겟 충돌 중심 = 발 좌표 + HitOffset, 타겟 반경 = HitRadius
                TransformComponent entityTransform = transformPool.GetByIndex(i);
                Vector2 entityCenter;
                float entityRadius;
                if (cm.TryGetComponent<ColliderComponent>(entityId, out var entityCollider))
                {
                    entityCenter = entityTransform.Position + entityCollider.HitOffset;
                    entityRadius = entityCollider.HitRadius;
                }
                else
                {
                    entityCenter = entityTransform.Position;
                    entityRadius = 0f;
                }

                // CircleVsSector: 부채꼴 거리 검사에 타겟 반경을 더해 가장자리 명중까지 커버
                if (HitboxMath.CircleVsSector(entityCenter, entityRadius, originPosition, forward, range, halfAngleDeg))
                {
                    outHitEntities.Add(entityId);
                }
            }
        }

        /// <summary>
        /// 특정 엔티티에게 스킬 효과를 적용합니다
        /// </summary>
        private void ApplySkillEffectToEntity(int skillEntityId, SkillComponent skill, int targetEntityId)
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

            // ========== DamageCalculator를 사용한 데미지 계산 ==========
            DamageResult damageResult = DamageCalculator.Calculate(skill.OwnerEntityId, targetEntityId, skill.Table);

            // ========== 데미지 적용 (HP 감소, 흡혈, 반사, 메시지 전송, 상태이상) ==========
            DamageCalculator.ApplyDamageResult(skill.OwnerEntityId, targetEntityId, damageResult);

            // 타겟 StatComponent 다시 가져오기 (ApplyDamageResult에서 HP가 변경됨)
            if (AR.s.Component.TryGetComponent<StatComponent>(targetEntityId, out targetStat) == false)
            {
                Debug.LogWarning($"[System_Skill] Target StatComponent lost after damage - TargetEntityId: {targetEntityId}");
                return;
            }

            // [SkillEffect] OnHit / OnCrit / OnKill 트리거
            SkillEffectContext hitCtx = new()
            {
                SkillEntityId = skillEntityId,
                SkillId = skill.SkillId,
                OwnerEntityId = skill.OwnerEntityId,
                TargetEntityId = targetEntityId,
                DamageResult = damageResult,
            };
            SkillEffectExecutor.Trigger(GE.SkillTrigger.OnHit, ref hitCtx, skill.Table.SkillEffectIds);
            if (damageResult.IsCritical)
            {
                SkillEffectExecutor.Trigger(GE.SkillTrigger.OnCrit, ref hitCtx, skill.Table.SkillEffectIds);
            }
            if (targetStat.CurrentHp <= 0f)
            {
                SkillEffectExecutor.Trigger(GE.SkillTrigger.OnKill, ref hitCtx, skill.Table.SkillEffectIds);
            }

            Debug.Log($"[System_Skill] ApplySkillEffectToEntity - SkillEntityId: {skillEntityId}, SkillId: {skill.SkillId}, TargetEntityId: {targetEntityId}, Damage: {Mathf.RoundToInt(damageResult.FinalDamage)}, Critical: {damageResult.IsCritical}, Evaded: {damageResult.IsEvaded}, Blocked: {damageResult.IsBlocked}, RemainingHP: {targetStat.CurrentHp}/{targetStat.FinalMaxHp}");

            // TODO: 추가 구현 필요
            // - 버프/디버프 적용
            // - 넉백, CC 효과 등
            // - 데미지 이펙트, 사운드 재생
        }

        /// <summary>
        /// 스킬 효과를 제거합니다 (주로 Toggle 스킬용)
        /// </summary>
        private void RemoveSkillEffect(int skillEntityId, SkillComponent skill)
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
        private bool StartSkillInternal(int skillEntityId, SkillTargetComponent inTargetComponent)
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
        private bool StartSkillAtPositionInternal(int skillEntityId, Vector2 position)
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
        private bool StartSkillInDirectionInternal(int skillEntityId, Vector2 direction)
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
        private void StopSkillInternal(int skillEntityId, ref SkillComponent inSkill, ref SkillStateComponent inState)
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

        private bool CheckEnableSkill(StateComponent inCharState)
        {
            // 비정상 상태에서는 스킬 사용 불가    
            if(inCharState.Condition == Creature.CharacterConditions.Normal)
                return true;
        
            return false;
        }

        #endregion
    }
}
