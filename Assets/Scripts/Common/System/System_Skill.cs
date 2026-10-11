#nullable enable
using ARPG.Component;
using ARPG.Factory;
using ARPG.Skill;
using ARPG.Skill.Aim;
using ARPG.Skill.Combat;
using ARPG.Skill.Telegraph;
using ARPG.Utility;
using Cysharp.Threading.Tasks;
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

        private readonly struct SkillRuntimePools
        {
            public readonly SparseSet<SkillComponent> Skill;
            public readonly SparseSet<SkillStateComponent> SkillState;
            public readonly SparseSet<SkillTimingComponent> SkillTiming;
            public readonly SparseSet<SkillCommandComponent> SkillCommand;
            public readonly SparseSet<SkillTargetComponent> SkillTarget;
            public readonly SparseSet<StateComponent> State;
            public readonly SparseSet<TransformComponent> Transform;
            public readonly SparseSet<InputComponent> Input;
            public readonly SparseSet<StatComponent> Stat;
            public readonly SparseSet<ColliderComponent> Collider;
            public readonly SparseSet<FactionComponent> Faction;
            public readonly SparseSet<SkillStatBonusOnSkillStartComponent> SkillBonus;
            public readonly SparseSet<AnimatorComponent> Animator;
            public readonly SparseSet<SkillAimIndicatorComponent> SkillAimIndicator;
            public readonly SparseSet<AttackTelegraphComponent> AttackTelegraph;
            public readonly SparseSet<JumpComponent> Jump;
            public readonly SparseSet<SkillLandingCommitComponent> LandingCommit;

            public SkillRuntimePools(ComponentManager cm)
            {
                Skill = cm.GetComponentPool<SkillComponent>();
                SkillState = cm.GetComponentPool<SkillStateComponent>();
                SkillTiming = cm.GetComponentPool<SkillTimingComponent>();
                SkillCommand = cm.GetComponentPool<SkillCommandComponent>();
                SkillTarget = cm.GetComponentPool<SkillTargetComponent>();
                State = cm.GetComponentPool<StateComponent>();
                Transform = cm.GetComponentPool<TransformComponent>();
                Input = cm.GetComponentPool<InputComponent>();
                Stat = cm.GetComponentPool<StatComponent>();
                Collider = cm.GetComponentPool<ColliderComponent>();
                Faction = cm.GetComponentPool<FactionComponent>();
                SkillBonus = cm.GetComponentPool<SkillStatBonusOnSkillStartComponent>();
                Animator = cm.GetComponentPool<AnimatorComponent>();
                SkillAimIndicator = cm.GetComponentPool<SkillAimIndicatorComponent>();
                AttackTelegraph = cm.GetComponentPool<AttackTelegraphComponent>();
                Jump = cm.GetComponentPool<JumpComponent>();
                LandingCommit = cm.GetComponentPool<SkillLandingCommitComponent>();
            }
        }

        public void OnCreate()
        {
            Debug.Log("System_Skill Created");
        }

        public void OnReset()
        {
            ComponentManager cm = AR.s.Component;
            SparseSet<SkillComponent> skillPool = cm.GetComponentPool<SkillComponent>();
            SparseSet<SkillStateComponent> skillStatePool = cm.GetComponentPool<SkillStateComponent>();
            SparseSet<SkillTargetComponent> skillTargetPool = cm.GetComponentPool<SkillTargetComponent>();

            for (int i = 0; i < skillPool.Count; i++)
            {
                int skillEntityId = skillPool.GetEntityId(i);
                SkillComponent skill = skillPool.GetByIndex(i);
                skill.ResetAimRuntimeData();
                cm.SetComponent(skillEntityId, skill);

                if (skillStatePool.TryGet(skillEntityId, out SkillStateComponent skillState) == true)
                {
                    skillState.Reset();
                    cm.SetComponent(skillEntityId, skillState);
                }

                if (skillTargetPool.TryGet(skillEntityId, out SkillTargetComponent skillTarget) == true)
                {
                    skillTarget.Reset();
                    cm.SetComponent(skillEntityId, skillTarget);
                }

                cm.RemoveComponent<AttackTelegraphComponent>(skillEntityId);
                cm.RemoveComponent<SkillLandingCommitComponent>(skillEntityId);
            }

            Debug.Log("System_Skill Reset called");
        }

        public void OnFixedUpdate(float inFixedDeltaTime)
        {
            // SkillComponent를 가진 모든 스킬 엔티티 순회
            ComponentManager cm = AR.s.Component;
            SkillRuntimePools pools = new SkillRuntimePools(cm);
            SparseSet<SkillComponent> skillPool = pools.Skill;

            for (int i = 0; i < skillPool.Count; i++)
            {
                int skillEntityId = skillPool.GetEntityId(i);
                SkillComponent skill = skillPool.GetByIndex(i);

                // 초기화되지 않은 스킬은 건너뜀
                if (skill.IsInitialized == false)
                    continue;

                // 상태 컴포넌트가 없으면 건너뜀
                if (pools.SkillState.TryGet(skillEntityId, out var skillState) == false)
                    continue;

                if (skillState.IsRunning == true) // 스킬이 실행 중이면 스킬 업데이트
                {
                    // 타이밍 컴포넌트가 없으면 건너뜀
                    if (pools.SkillTiming.TryGet(skillEntityId, out var timing) == false)
                        continue;

                    if (ShouldCancelSkill(skill, in pools) == true)
                    {
                        // 실행 중인 스킬이 있으면 취소
                        StopSkillInternal(skillEntityId, ref skill, ref skillState, in pools);
                        continue; // 스킬 업데이트 건너뜀
                    }

                    // 3. 실행 중인 스킬 업데이트
                    UpdateSkillState(skillEntityId, ref skill, ref skillState, ref timing, inFixedDeltaTime, in pools);
                }
                else // 실행중이 아니라면 쿨타임 감소 + 커맨드 처리
                {
                    // 쿨타임 감소
                    if (skillState.CooldownRemaining > 0f)
                    {
                        skillState.CooldownRemaining -= inFixedDeltaTime;
                        cm.SetComponent(skillEntityId, skillState);
                    }
                }

                // 스킬이 실행중이 아닐 때 커맨드 처리(스킬 실행 체크)
                if (skillState.IsRunning == false)
                {
                    // 캐릭터 엔티티에서 커맨드 확인
                    if (pools.SkillCommand.TryGet(skill.OwnerEntityId, out var command) == false)
                        continue;

                    // 커맨드 스킬 엔티티가 아니라면 다음꺼
                    if(skillEntityId != command.SkillEntityId)
                        continue;

                    ProcessSkillCommands(skillEntityId, ref skill, ref command, in pools);
                }
            }
        }

        /// <summary>
        /// SkillCommandComponent를 확인하고 처리
        /// </summary>
        /// <param name="skillEntityId">스킬 엔티티 ID</param>
        /// <param name="skill">스킬 컴포넌트</param>
        private void ProcessSkillCommands(int skillEntityId, ref SkillComponent inSkill, ref SkillCommandComponent inCommand, in SkillRuntimePools pools)
        {
            if(pools.State.TryGet(inSkill.OwnerEntityId, out var charState) == false)
            {
                Debug.LogError($"[System_Skill] Character StateComponent not found (OwnerEntityId: {inSkill.OwnerEntityId})");
                return;
            }

            // 스킬 사용 가능 여부 확인 (Stunned 등 - SkillCommand는 유지하여 상태 회복 후 자동 시전)
            if(CheckEnableSkill(charState) == false)
                return;

            // 상태 컴포넌트가 없으면 건너뜀
            if (pools.SkillState.TryGet(skillEntityId, out var skillState) == false)
            {
                Debug.LogError($"[System_Skill] SkillStateComponent not found for SkillEntityId: {skillEntityId}");
                AR.s.Component.RemoveComponent<SkillCommandComponent>(inSkill.OwnerEntityId);
                return;
            }

            if(pools.SkillTarget.TryGet(skillEntityId, out var target) == false)
            {
                Debug.LogError($"[System_Skill] SkillTargetComponent not found for SkillEntityId: {skillEntityId}");
                AR.s.Component.RemoveComponent<SkillCommandComponent>(inSkill.OwnerEntityId);
                return;
            }

            target.Reset();
            inSkill.ResetAimRuntimeData();
            inSkill.IsAreaEffectCreated = false;
            AR.s.Component.RemoveComponent<SkillLandingCommitComponent>(skillEntityId);

            // 마나 가드 — 플레이어 전용. OnSkillCommand 효과 발화 전에 차단하여 효과 누수 방지
            int manaCost = inSkill.Table != null ? inSkill.Table.Mana : 0;
            bool isPlayerCaster = AR.s.Data != null && inSkill.OwnerEntityId == AR.s.Data.CurrentPlayerEntityId;
            if (isPlayerCaster && manaCost > 0)
            {
                if (pools.Stat.TryGet(inSkill.OwnerEntityId, out var manaStat) == false || manaStat.CurrentMp < manaCost)
                {
                    AR.s.Component.RemoveComponent<SkillCommandComponent>(inSkill.OwnerEntityId);
                    return;
                }
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
            SkillEffectExecutor.Trigger(GE.SkillTrigger.OnSkillCommand, ref cmdCtx, inSkill.EffectiveSkillEffectIds);
            if (cmdCtx.CancelOriginalCast)
            {
                AR.s.Component.RemoveComponent<SkillCommandComponent>(inSkill.OwnerEntityId);
                return;
            }

            // 커맨드 타입에 따라 처리
            switch (inCommand.TargetType)
            {
                case GlobalEnum.SkillTargetType.Direction:
                    // 오너 엔티티의 위치와 마우스 위치로 방향 계산
                    if (pools.Transform.TryGet(inSkill.OwnerEntityId, out var ownerTransform))
                    {
                        Vector2 direction = SkillAimResolver.ResolveDirection(
                            ownerTransform.Position,
                            inCommand.TargetPosition,
                            Vector2.right);
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
                    if (pools.Transform.TryGet(inSkill.OwnerEntityId, out var ownerTransformPos))
                    {
                        Vector2 dirToTarget = SkillAimResolver.ResolveDirection(
                            ownerTransformPos.Position,
                            inCommand.TargetPosition,
                            Vector2.right);
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

            // 속도 배율 적용 (Attack → 공격 프로필 최종 APS, Spell → 기존 시전 속도%)
            if (pools.SkillTiming.TryGet(skillEntityId, out var timing))
            {
                GlobalEnum.SkillTag tags = inSkill.Table != null ? inSkill.Table.Tags : GlobalEnum.SkillTag.None;
                bool isAttack = (tags & GlobalEnum.SkillTag.Attack) != 0;
                float speedMultiplier = 1f;

                // Attack 태그: 프로필의 최종 초당 공격 횟수로 원본 Duration을 균등 스케일한다.
                // 프로필이 Weapon Base/강화, Skill 배율, 글로벌 증가율을 이미 모두 포함하므로 추가 배율을 곱하지 않는다.
                if (isAttack == true && inSkill.Table != null)
                {
                    float attacksPerSecond = DamageCalculator.EstimateAttackSpeed(skillEntityId);
                    float durationMultiplier = 1f / Mathf.Max(0.1f, attacksPerSecond);
                    timing.BaseStartDuration = inSkill.Table.StartTime * durationMultiplier;
                    timing.BaseProcessDuration = inSkill.Table.ProcessTime * durationMultiplier;
                    timing.BaseEndDuration = inSkill.Table.EndTime * durationMultiplier;
                }
                else if (inSkill.Table != null)
                {
                    // 주문은 무기 공격속도를 사용하지 않는다. 테이블 원본 시간에 시전 속도만 적용한다.
                    timing.BaseStartDuration = inSkill.Table.StartTime;
                    timing.BaseProcessDuration = inSkill.Table.ProcessTime;
                    timing.BaseEndDuration = inSkill.Table.EndTime;
                    speedMultiplier = GetSkillSpeedMultiplier(inSkill, pools.Stat);
                }

                timing.ApplySpeedMultiplier(speedMultiplier);
                AR.s.Component.SetComponent(skillEntityId, timing);

                Debug.Log($"<color=yellow>[System_Skill] Speed applied - SkillId: {inSkill.SkillId}, Tags: {tags}, IsAttack: {isAttack}, SpeedMul: {speedMultiplier:F2}, Duration: {timing.StartDuration:F3}/{timing.ProcessDuration:F3}/{timing.EndDuration:F3} (Total: {timing.TotalDuration:F3}s)</color>");
            }

            // 스킬 시작
            OnChangeState(skillEntityId, ref skillState, ref inSkill, SkillState.Start, in pools);
            AR.s.Component.SetComponent(skillEntityId, skillState);
            AR.s.Component.SetComponent(skillEntityId, inSkill);
            ActivateSkillRuntime(skillEntityId, inSkill, in pools);
            if (pools.SkillTiming.TryGet(skillEntityId, out SkillTimingComponent attackTiming) == true)
            {
                CreateAttackTelegraph(skillEntityId, inSkill, target, attackTiming, in pools);
            }
            TraceAim("Command", skillEntityId, inSkill.OwnerEntityId, target.TargetDirection, target.TargetPosition);

            // 마나 차감 — 플레이어 전용, 시전 확정 후 1회. 차감 후 UI 갱신용 메시지 발화
            if (isPlayerCaster && manaCost > 0 && pools.Stat.TryGet(inSkill.OwnerEntityId, out var spendStat))
            {
                spendStat.CurrentMp = Mathf.Max(0, spendStat.CurrentMp - manaCost);
                AR.s.Component.SetComponent(inSkill.OwnerEntityId, spendStat);

                AR.s.Message.SendToEntity(new ARPG.Message.ManaChangedMessage
                {
                    TargetEntityId = inSkill.OwnerEntityId,
                    CurrentMp = spendStat.CurrentMp,
                    MaxMp = spendStat.FinalMaxMp,
                });
            }

            // 커맨드 처리 완료 - 컴포넌트 제거
            AR.s.Component.RemoveComponent<SkillCommandComponent>(inSkill.OwnerEntityId);

            // 캐릭터 상태 변경
            charState.Condition = Creature.CharacterConditions.UseSkill;
            AR.s.Component.SetComponent(inSkill.OwnerEntityId , charState);
        }

        /// <summary>
        /// 특정 캐릭터 상태에서 스킬이 취소되어야 하는지 확인
        /// </summary>
        private bool ShouldCancelSkill(SkillComponent inSkill, in SkillRuntimePools pools)
        {
            if (AR.s.Component.HasComponent<DestroyTag>(inSkill.OwnerEntityId) == true)
                return true;

            // 캐릭터 필수 컴포넌트 — 없으면 데이터 문제이므로 보고만 하고 취소하지 않음
            if (pools.State.Contains(inSkill.OwnerEntityId) == false)
            {
                Debug.LogError($"[System_Skill] StateComponent not found for OwnerEntityId: {inSkill.OwnerEntityId}");
                return false;
            }

            // 중단 규칙 정본은 SkillInterruptRule — System_AttackTelegraph의 예고 제거와 같은 조건을 공유한다.
            SkillInterruptReason reason = SkillInterruptRule.Evaluate(AR.s.Component, inSkill.OwnerEntityId);
            if (reason == SkillInterruptReason.None)
                return false;

            Debug.Log($"[System_Skill] Skill cancelled - Reason: {reason} (OwnerEntityId: {inSkill.OwnerEntityId})");
            return true;
        }

        /// <summary>
        /// 스킬 상태를 업데이트하고 상태 전환을 처리
        /// </summary>
        private void UpdateSkillState(int skillEntityId, ref SkillComponent skill, ref SkillStateComponent state, ref SkillTimingComponent timing, float deltaTime, in SkillRuntimePools pools)
        {
            // 경과 시간 증가
            state.ElapsedTime += deltaTime;

            // 현재 상태에 따라 처리
            switch (state.State)
            {
                case SkillState.Start:
                    ProcessStartState(skillEntityId, ref skill, ref state, ref timing, in pools);
                    break;

                case SkillState.Process:
                    ProcessProcessState(skillEntityId, ref skill, ref state, ref timing, in pools);
                    break;

                case SkillState.End:
                    ProcessEndState(skillEntityId, ref skill, ref state, ref timing, in pools);
                    break;
            }

            // 변경된 상태 저장
            AR.s.Component.SetComponent(skillEntityId, state);
        }

        /// <summary>
        /// Start 상태 처리 - 준비 모션
        /// </summary>
        private void ProcessStartState(int skillEntityId, ref SkillComponent skill, ref SkillStateComponent state, ref SkillTimingComponent timing, in SkillRuntimePools pools)
        {
            if (state.ElapsedTime >= timing.StartDuration)
            {
                // Process 상태로 전환
                OnChangeState(skillEntityId, ref state, ref skill, SkillState.Process, in pools);
            }
        }

        /// <summary>
        /// Process 상태 처리 - 주요 효과 발생 (데미지, 버프 등)
        /// </summary>
        private void ProcessProcessState(int skillEntityId, ref SkillComponent skill, ref SkillStateComponent state, ref SkillTimingComponent timing, in SkillRuntimePools pools)
        {
            // 스킬 실행 타입에 따라 분기 처리
            switch (skill.ExecutionType)
            {
                case SkillExecutionType.Single:
                case SkillExecutionType.MultiHit:
                    ProcessMultiHitSkill(skillEntityId, ref state, ref skill, in timing, in pools);
                    break;

                case SkillExecutionType.Channeling:
                    ProcessChannelingSkill(skillEntityId, ref state, ref skill, in pools);
                    break;

                case SkillExecutionType.Charge:
                    ProcessChargeSkill(skillEntityId, ref state, ref skill);
                    break;

                case SkillExecutionType.Toggle:
                    ProcessToggleSkill(skillEntityId, ref state, ref skill, in pools);
                    break;
            }

            // 스킬 컴포넌트 변경사항 저장
            AR.s.Component.SetComponent(skillEntityId, skill);

            // Process 상태 시간이 끝나면 End 상태로 전환
            // 단, 플레이어 채널링은 입력 유지가 종료를 결정하므로 ProcessDuration 자동 종료를 스킵
            // (AI 채널링은 InputComponent가 없으므로 ProcessDuration으로 종료됨 → 채널 지속시간 데이터로 활용)
            // ProcessChannelingSkill에서 이미 End로 전이된 경우는 state.State가 더 이상 Process가 아니므로 중복 전이 방지
            bool hasInput = pools.Input.Contains(skill.OwnerEntityId);
            bool isPlayerChanneling = (skill.ExecutionType == SkillExecutionType.Channeling) && hasInput;
            SkillImpactSchedule processSchedule = ResolveImpactSchedule(skill, timing);
            // 오너의 아무 점프가 아니라 이 스킬이 띄운 점프만 기다린다.
            // 넉백·대시 같은 다른 점프 소스가 추가되면 오너 단위 검사는 엉뚱한 점프를 기다린다.
            bool isOwnJumpAirborne = pools.Jump.TryGet(skill.OwnerEntityId, out JumpComponent ownerJump) == true
                && ownerJump.SkillEntityId == skillEntityId;
            bool waitsForLanding = processSchedule.Kind == SkillImpactKind.LandingImpact
                && (isOwnJumpAirborne == true
                    || pools.LandingCommit.Contains(skillEntityId) == true);
            if (state.State == SkillState.Process
                && isPlayerChanneling == false
                && waitsForLanding == false
                && state.ElapsedTime >= timing.ProcessDuration)
            {
                OnChangeState(skillEntityId, ref state, ref skill, SkillState.End, in pools);
            }
        }

        /// <summary>
        /// End 상태 처리 - 후딜레이
        /// </summary>
        private void ProcessEndState(int skillEntityId, ref SkillComponent skill, ref SkillStateComponent state, ref SkillTimingComponent timing, in SkillRuntimePools pools)
        {
            if (state.ElapsedTime >= timing.EndDuration)
            {
                // 스킬 종료
                OnChangeState(skillEntityId, ref state, ref skill, SkillState.None, in pools);
            }
        }

        /// <summary>
        /// 스킬 상태 변경 및 상태 진입 처리
        /// </summary>
        private void OnChangeState(int skillEntityId, ref SkillStateComponent state, ref SkillComponent skill, SkillState newState)
        {
            ComponentManager cm = AR.s.Component;
            SkillRuntimePools pools = new SkillRuntimePools(cm);
            OnChangeState(skillEntityId, ref state, ref skill, newState, in pools);
        }

        private void OnChangeState(int skillEntityId, ref SkillStateComponent state, ref SkillComponent skill, SkillState newState, in SkillRuntimePools pools)
        {
            state.ChangeState(newState);

            // 새로운 상태 진입 처리
            switch (newState)
            {
                case SkillState.Start:
                    OnEnterStartState(skillEntityId, ref skill, in pools);
                    break;

                case SkillState.Process:
                    OnEnterProcessState(skillEntityId, ref skill, in pools);
                    break;

                case SkillState.End:
                    OnEnterEndState(skillEntityId, ref skill);
                    break;

                case SkillState.None:
                    OnSkillComplete(skillEntityId, ref skill, ref state, false, in pools);
                    break;
            }
        }


        /// <summary>
        /// Start 상태 진입 시 호출 - 스킬 준비 함수 호출
        /// 현재는 애니메이션 없이 준비 로직만 실행 (StartTime이 0이면 즉시 Process로 전환됨)
        /// </summary>
        private void OnEnterStartState(int skillEntityId, ref SkillComponent inSkill, in SkillRuntimePools pools)
        {
            // TODO: 나중에 준비 애니메이션이 생기면 여기서 재생 (StartDuration 기준)
            // TODO: 시작 이펙트, 사운드 등
        }

        /// <summary>
        /// 점프 스킬 시작 - JumpComponent 생성 및 파라미터 세팅
        /// 체공 시간은 SkillTiming의 ProcessDuration, 착지 위치는 SkillTargetType에 따라 결정
        /// </summary>
        private void StartJump(int skillEntityId, ref SkillComponent inSkill, in SkillRuntimePools pools)
        {
            ComponentManager cm = AR.s.Component;

            if (pools.Transform.TryGet(inSkill.OwnerEntityId, out var ownerTransform) == false)
            {
                Debug.LogError($"[System_Skill] StartJump - Owner TransformComponent not found, OwnerEntityId: {inSkill.OwnerEntityId}");
                return;
            }

            if (pools.SkillTiming.TryGet(skillEntityId, out var timing) == false)
            {
                Debug.LogError($"[System_Skill] StartJump - SkillTimingComponent not found, SkillEntityId: {skillEntityId}");
                return;
            }

            if (inSkill.Table == null)
                return;

            Vector2 startPos = ownerTransform.Position;
            Vector2 endPos = startPos;

            // 착지 위치는 SkillTargetType에 따라 결정
            if (pools.SkillTarget.TryGet(skillEntityId, out var target) == true)
            {
                endPos = ResolveJumpEndPosition(inSkill.Table, target, startPos);
            }

            // 체공 시간 = Process 단계 시간. 점프 이동 구간은 Process에서만 실행되기 때문.
            // 0이면 System_Jump의 t = Elapsed / Duration이 무한대가 되므로 하한을 두되,
            // 조용히 넘어가면 "포물선 없이 순간이동"으로만 드러나 원인 추적이 어려우므로 반드시 기록한다.
            const float MIN_JUMP_DURATION = 0.01f;
            float jumpDuration = timing.ProcessDuration;
            if (jumpDuration <= 0f)
            {
                Debug.LogError($"[System_Skill] StartJump - SkillId({inSkill.SkillId}) ProcessTime이 0입니다. " +
                               $"점프 이동은 Process 단계에서 실행되므로 ProcessTime > 0 이어야 합니다. " +
                               $"체공 시간을 {MIN_JUMP_DURATION}초로 대체하며, 포물선 없이 착지 지점으로 즉시 이동합니다. " +
                               $"(Start/Process/End = {timing.StartDuration:F3}/{timing.ProcessDuration:F3}/{timing.EndDuration:F3})");
                jumpDuration = MIN_JUMP_DURATION;
            }

            // JumpComponent 생성
            JumpComponent jump = new JumpComponent
            {
                SkillEntityId = skillEntityId,
                Height = 0f,
                Elapsed = 0f,
                Duration = jumpDuration,
                MaxHeight = inSkill.Table.ArcHeight,
                StartPosition = startPos,
                EndPosition = endPos,
            };
            cm.AddComponent(inSkill.OwnerEntityId, jump);

            // 이동 상태 변경
            if (pools.State.TryGet(inSkill.OwnerEntityId, out var state) == true)
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
        private void OnEnterProcessState(int skillEntityId, ref SkillComponent inSkill, in SkillRuntimePools pools)
        {
            // [SkillEffect] OnSkillStart 트리거
            SkillEffectContext startCtx = new()
            {
                SkillEntityId = skillEntityId,
                SkillId = inSkill.SkillId,
                OwnerEntityId = inSkill.OwnerEntityId,
            };
            SkillEffectExecutor.Trigger(GE.SkillTrigger.OnSkillStart, ref startCtx, inSkill.EffectiveSkillEffectIds);

            // SkillTarget 컴포넌트에서 타겟 정보 가져오기
            if (pools.SkillTarget.TryGet(skillEntityId, out var target) == false)
                return;

            bool isJumpLaunch = inSkill.Table != null
                && inSkill.Table.SkillType == GlobalEnum.SkillType.Jump
                && inSkill.Table.ArcHeight > 0f;
            if (isJumpLaunch == true)
            {
                target = CommitAim(skillEntityId, ref inSkill, target, "LaunchCommit", in pools);
            }

            // 점프·이동 스킬은 선딜이 끝난 LaunchCommit에서 경로를 확정한 뒤 이동을 시작합니다.
            if (isJumpLaunch == true)
            {
                StartJump(skillEntityId, ref inSkill, in pools);
            }

            // Process 기간 동안 애니메이션 재생 요청
            // DamageTime(비율) * ProcessDuration 지점에서 타격 프레임이 나오도록 에셋을 구성
            if (pools.Animator.TryGet(inSkill.OwnerEntityId, out var animatorComp))
            {
                if (inSkill.Table != null && string.IsNullOrEmpty(inSkill.Table.AnimationName) == false)
                {
                    if (System.Enum.TryParse<GlobalEnum.AnimCategory>(inSkill.Table.AnimationName, out var category))
                    {
                        float duration = 0f;
                        if (pools.SkillTiming.TryGet(skillEntityId, out var timing))
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
            ComponentManager cm = AR.s.Component;
            // 채널 종료 등 남은 커밋이 실행되지 않는 경우에도 후딜에는 예고를 남기지 않는다.
            cm.RemoveComponent<AttackTelegraphComponent>(skillEntityId);
            cm.RemoveComponent<SkillLandingCommitComponent>(skillEntityId);

            // [SkillEffect] OnSkillEnd 트리거
            SkillEffectContext endCtx = new()
            {
                SkillEntityId = skillEntityId,
                SkillId = inSkill.SkillId,
                OwnerEntityId = inSkill.OwnerEntityId,
            };
            SkillEffectExecutor.Trigger(GE.SkillTrigger.OnSkillEnd, ref endCtx, inSkill.EffectiveSkillEffectIds);

            // TODO: 종료 이펙트, 사운드 등
            // Debug.Log($"[System_Skill] Skill End - SkillEntityId: {skillEntityId}");
        }

        /// <summary>
        /// 스킬 완료 시 호출
        /// </summary>
        private void OnSkillComplete(
            int skillEntityId,
            ref SkillComponent inSkill,
            ref SkillStateComponent inSkillState,
            bool wasCancelled,
            in SkillRuntimePools pools)
        {
            // 스킬 런타임 데이터 초기화
            inSkill.ResetRuntimeData();
            AR.s.Component.SetComponent(skillEntityId, inSkill);

            if (pools.SkillTarget.TryGet(skillEntityId, out SkillTargetComponent target) == true)
            {
                target.Reset();
                AR.s.Component.SetComponent(skillEntityId, target);
            }

            AR.s.Component.RemoveComponent<SkillMovementPenaltyComponent>(inSkill.OwnerEntityId);
            AR.s.Component.RemoveComponent<AttackTelegraphComponent>(skillEntityId);
            AR.s.Component.RemoveComponent<SkillLandingCommitComponent>(skillEntityId);
            if (pools.SkillAimIndicator.TryGet(inSkill.OwnerEntityId, out SkillAimIndicatorComponent indicator) == true)
            {
                indicator.Reset();
                AR.s.Component.SetComponent(inSkill.OwnerEntityId, indicator);
            }

            if (wasCancelled == true
                && inSkill.Table != null
                && inSkill.Table.SkillType == GlobalEnum.SkillType.Jump)
            {
                AR.s.Component.RemoveComponent<JumpComponent>(inSkill.OwnerEntityId);
            }

            // 스킬 상태 초기화
            inSkillState.Reset();

            // 쿨타임 세팅 (CooldownReduction 적용)
            if (inSkill.Table != null && inSkill.Table.Cooltime > 0f)
            {
                float cooldown = inSkill.Table.Cooltime;
                if (pools.Stat.TryGet(inSkill.OwnerEntityId, out var ownerStat))
                {
                    float cdr = Mathf.Clamp(ownerStat.FinalCooldownReduction, 0, 90) / 100f;
                    cooldown *= (1f - cdr);
                }
                inSkillState.CooldownDuration = cooldown;
                inSkillState.CooldownRemaining = cooldown;
            }

            AR.s.Component.SetComponent(skillEntityId, inSkillState);

            // 캐릭터 상태 초기화
            if(pools.State.TryGet(inSkill.OwnerEntityId, out var charState) == false)
            {
                Debug.LogError($"[System_Skill] StopSkillInternal - StateComponent not found, EntityId({inSkill.OwnerEntityId})");
                return;
            }

            if (wasCancelled == false || charState.Condition == Creature.CharacterConditions.UseSkill)
            {
                charState.Condition = Creature.CharacterConditions.Normal;
            }
            if (wasCancelled == true)
            {
                charState.TryEndJumpMovement();
            }
            AR.s.Component.SetComponent(inSkill.OwnerEntityId, charState);

            Debug.Log($"[System_Skill] Skill Complete - SkillEntityId: {skillEntityId}");
        }

        private void ActivateSkillRuntime(int skillEntityId, SkillComponent skill, in SkillRuntimePools pools)
        {
            if (skill.Table == null)
                return;

            bool hasPlayerInput = pools.Input.Contains(skill.OwnerEntityId);
            GlobalEnum.SkillMovementMode movementMode = SkillUsePolicyResolver.ResolveMovementMode(skill, hasPlayerInput);
            float baseMultiplier = SkillUsePolicyResolver.ResolveMovementMultiplier(skill.Table, movementMode);
            AR.s.Component.SetComponent(skill.OwnerEntityId, new SkillMovementPenaltyComponent
            {
                SkillEntityId = skillEntityId,
                MovementMode = movementMode,
                BaseMultiplier = baseMultiplier,
                CurrentMultiplier = 1f,
                EndRecoveryProgress = 0f,
            });

            if (pools.SkillAimIndicator.TryGet(skill.OwnerEntityId, out SkillAimIndicatorComponent indicator) == true)
            {
                indicator.ActiveSkillEntityId = skillEntityId;
                indicator.IsVisible = false;
                indicator.LastAppliedAngle = 0f;
                AR.s.Component.SetComponent(skill.OwnerEntityId, indicator);
            }
        }

        private SkillTargetComponent CommitAim(
            int skillEntityId,
            ref SkillComponent skill,
            SkillTargetComponent target,
            string tracePhase,
            in SkillRuntimePools pools)
        {
            if (skill.Table == null)
            {
                return target;
            }

            bool hasPlayerInput = pools.Input.Contains(skill.OwnerEntityId);
            GlobalEnum.SkillAimTrackingMode trackingMode = SkillUsePolicyResolver.ResolveAimTrackingMode(skill, hasPlayerInput);
            bool keepsSingleCommit = trackingMode == GlobalEnum.SkillAimTrackingMode.SnapshotOnCommand
                || trackingMode == GlobalEnum.SkillAimTrackingMode.TrackUntilCommit;
            if (keepsSingleCommit == true && skill.IsAimCommitted == true)
            {
                return target;
            }

            target.TargetDirection = SkillAimResolver.NormalizeOrFallback(target.TargetDirection, Vector2.right);
            if (skill.Table != null
                && skill.Table.SkillTargetType == GlobalEnum.SkillTargetType.Position
                && pools.Transform.TryGet(skill.OwnerEntityId, out TransformComponent ownerTransform) == true
                && pools.Input.TryGet(skill.OwnerEntityId, out InputComponent input) == true)
            {
                target.TargetPosition = SkillAimResolver.ResolveTargetPosition(
                    ownerTransform.Position,
                    input.MouseWorldPosition,
                    target.TargetDirection,
                    skill.Table.SkillRangeMin,
                    skill.Table.SkillRangeMax);
            }

            skill.IsAimCommitted = keepsSingleCommit;
            AR.s.Component.SetComponent(skillEntityId, target);
            AR.s.Component.SetComponent(skillEntityId, skill);
            TraceAim(tracePhase, skillEntityId, skill.OwnerEntityId, target.TargetDirection, target.TargetPosition);
            return target;
        }

        private static void TraceAim(string phase, int skillEntityId, int ownerEntityId, Vector2 direction, Vector2 position)
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            Debug.Log($"[SkillAim] {phase} SkillEntityId={skillEntityId}, OwnerEntityId={ownerEntityId}, Direction={direction}, Position={position}");
#endif
        }

        /// <summary>
        /// 주문의 시전 속도 배율을 반환한다. 공격은 SkillAttackProfileComponent의 최종 APS를 사용한다.
        /// </summary>
        private float GetSkillSpeedMultiplier(SkillComponent skill, SparseSet<StatComponent> statPool)
        {
            if (skill.Table == null)
                return 1f;

            if (statPool.TryGet(skill.OwnerEntityId, out var stat) == false)
                return 1f;

            GlobalEnum.SkillTag tags = skill.Table.Tags;
            bool isSpell = (tags & GlobalEnum.SkillTag.Spell) != 0;

            float multiplier = 1f;

            if (isSpell == true)
            {
                multiplier = (100 + stat.FinalCastSpeed) / 100f;
            }

            // 상한 없음 — 실제 하한은 상태 머신 틱 수가 만든다 (Start/Process/End 각 1틱 이상 = FixedUpdate 3틱).
            // 하한 0.1은 유지: FinalCastSpeed <= -100이면 multiplier가 0/음수가 되어
            // duration이 무한대 또는 음수(= 첫 틱에 즉시 종료)가 된다.
            return Mathf.Max(multiplier, 0.1f);
        }

        private static SkillImpactSchedule ResolveImpactSchedule(
            SkillComponent skill,
            SkillTimingComponent timing)
        {
            if (skill.Table == null)
                return default;

            SkillImpactExecutionMode executionMode;
            switch (skill.ExecutionType)
            {
                case SkillExecutionType.Channeling:
                    executionMode = SkillImpactExecutionMode.Channeling;
                    break;
                case SkillExecutionType.MultiHit:
                    executionMode = SkillImpactExecutionMode.MultiHit;
                    break;
                case SkillExecutionType.Single:
                    executionMode = SkillImpactExecutionMode.Single;
                    break;
                default:
                    return default;
            }

            SkillImpactTimingInput input = new(
                executionMode,
                timing.StartDuration,
                timing.ProcessDuration,
                skill.Table.DamageTime,
                skill.HitInterval,
                skill.ChannelingInterval,
                skill.HitCount,
                skill.Table.HasDirectImpact,
                skill.Table.ProjectileId > 0,
                skill.Table.AreaEffectId > 0,
                skill.Table.UsesLandingCommit);
            return SkillImpactTimingResolver.Resolve(input);
        }

        private void CreateAttackTelegraph(
            int skillEntityId,
            SkillComponent skill,
            SkillTargetComponent target,
            SkillTimingComponent timing,
            in SkillRuntimePools pools)
        {
            if (skill.Table == null || AR.s.Component.HasComponent<MonsterTag>(skill.OwnerEntityId) == false)
                return;

            SkillImpactSchedule schedule = ResolveImpactSchedule(skill, timing);
            if (schedule.HasCommit == false)
                return;

            AttackTelegraphGeometry geometry = AttackTelegraphRuntimeGeometryResolver.Resolve(
                AR.s.Component,
                skillEntityId,
                skill,
                AttackTelegraphRuntimeGeometryResolver.ResolvePlayerHitRadius(AR.s.Component));
            if (geometry.Shape == AttackTelegraphShape.None)
                return;

            Vector2 snapshotOrigin = Vector2.zero;
            if (pools.Transform.TryGet(skill.OwnerEntityId, out TransformComponent ownerTransform) == true)
            {
                snapshotOrigin = AttackTelegraphRuntimeGeometryResolver.ResolveOrigin(
                    AR.s.Component,
                    skill.OwnerEntityId,
                    geometry.Shape,
                    ownerTransform.Position);
            }

            Vector2 targetPosition = target.TargetPosition;
            if (geometry.OriginMode == AttackTelegraphOriginMode.LandingPosition)
            {
                targetPosition = ResolveJumpEndPosition(skill.Table, target, snapshotOrigin);
            }

            float warningStart = Time.fixedTime;
            float nextCommit = warningStart + SkillImpactTimingResolver.GetQuantizedCommitOffset(
                schedule,
                0,
                Time.fixedDeltaTime);
            AttackTelegraphComponent telegraph = new()
            {
                OwnerEntityId = skill.OwnerEntityId,
                Shape = geometry.Shape,
                OriginMode = geometry.OriginMode,
                SnapshotOrigin = snapshotOrigin,
                TargetPosition = targetPosition,
                Direction = AttackTelegraphGeometryResolver.NormalizeOrFallback(target.TargetDirection, Vector2.right),
                Range = geometry.Range,
                HalfAngleDeg = geometry.HalfAngleDeg,
                Width = geometry.Width,
                ProjectileCount = geometry.ProjectileCount,
                ProjectileSpreadAngleDeg = geometry.ProjectileSpreadAngleDeg,
                WarningStartFixedTime = warningStart,
                NextCommitFixedTime = nextCommit,
                CommitIndex = 0,
                CommitCount = schedule.CommitCount,
            };
            AR.s.Component.SetComponent(skillEntityId, telegraph);
        }


        private void AdvanceAttackTelegraph(
            int skillEntityId,
            SkillComponent skill,
            float processElapsed,
            SkillImpactSchedule schedule,
            in SkillRuntimePools pools)
        {
            ComponentManager cm = AR.s.Component;
            int nextCommitIndex = skill.CurrentHitIndex;
            if (schedule.HasCommitAt(nextCommitIndex) == false)
            {
                // 마지막 타격·발사·착지 직후 제거하고, 다음 커밋이 있을 때만 예고를 유지한다.
                cm.RemoveComponent<AttackTelegraphComponent>(skillEntityId);
                return;
            }

            if (pools.AttackTelegraph.TryGet(skillEntityId, out AttackTelegraphComponent telegraph) == false)
                return;

            if (pools.SkillTarget.TryGet(skillEntityId, out SkillTargetComponent target) == true)
            {
                telegraph.Direction = AttackTelegraphGeometryResolver.NormalizeOrFallback(
                    target.TargetDirection,
                    telegraph.Direction);
                telegraph.TargetPosition = target.TargetPosition;
            }

            telegraph.WarningStartFixedTime = Time.fixedTime;
            telegraph.NextCommitFixedTime = Time.fixedTime
                + SkillImpactTimingResolver.GetQuantizedRemainingDelay(
                    schedule,
                    nextCommitIndex,
                    processElapsed,
                    Time.fixedDeltaTime);
            telegraph.CommitIndex = nextCommitIndex;
            telegraph.CommitCount = schedule.CommitCount;
            cm.SetComponent(skillEntityId, telegraph);
        }

        private static Vector2 ResolveJumpEndPosition(
            ARPG.Tables.SkillTable table,
            SkillTargetComponent target,
            Vector2 startPosition)
        {
            float maxDistance = table.SkillRangeMax;
            if (table.SkillTargetType == GlobalEnum.SkillTargetType.Direction)
            {
                if (maxDistance > 0f)
                {
                    Vector2 direction = AttackTelegraphGeometryResolver.NormalizeOrFallback(
                        target.TargetDirection,
                        Vector2.right);
                    return startPosition + direction * maxDistance;
                }
                return startPosition;
            }

            if (table.SkillTargetType == GlobalEnum.SkillTargetType.Position)
            {
                Vector2 toTarget = target.TargetPosition - startPosition;
                float distanceSqr = toTarget.sqrMagnitude;
                if (maxDistance > 0f && distanceSqr > maxDistance * maxDistance)
                {
                    Vector2 direction = AttackTelegraphGeometryResolver.NormalizeOrFallback(
                        toTarget,
                        target.TargetDirection);
                    return startPosition + direction * maxDistance;
                }
                return target.TargetPosition;
            }

            return startPosition;
        }

        #region Skill Type Processing Methods

        /// <summary>
        /// MultiHit 타입 스킬 처리 - DamageTime(Process 내부 비율) 지점에서 첫 히트, HitInterval 간격으로 반복
        /// Single 타입은 HitCount=1, HitInterval=0으로 동작
        /// </summary>
        private void ProcessMultiHitSkill(int skillEntityId, ref SkillStateComponent state, ref SkillComponent skill, in SkillTimingComponent timing, in SkillRuntimePools pools)
        {
            SkillImpactSchedule schedule = ResolveImpactSchedule(skill, timing);
            if (schedule.HasCommit == false || schedule.HasCommitAt(skill.CurrentHitIndex) == false)
                return;

            if (schedule.Kind == SkillImpactKind.LandingImpact)
            {
                if (pools.LandingCommit.TryGet(skillEntityId, out SkillLandingCommitComponent landingCommit) == false)
                    return;

                if (pools.SkillTarget.TryGet(skillEntityId, out SkillTargetComponent landingTarget) == true)
                {
                    landingTarget.TargetPosition = landingCommit.LandingPosition;
                    AR.s.Component.SetComponent(skillEntityId, landingTarget);
                }

                AR.s.Component.RemoveComponent<SkillLandingCommitComponent>(skillEntityId);
                ProcessSkillHit(skillEntityId, ref skill, in pools);
                skill.CurrentHitIndex++;
                AdvanceAttackTelegraph(skillEntityId, skill, state.ElapsedTime, schedule, in pools);
                return;
            }

            float nextCommitTime = schedule.GetProcessCommitOffset(skill.CurrentHitIndex);
            if (state.ElapsedTime >= nextCommitTime)
            {
                ProcessSkillHit(skillEntityId, ref skill, in pools);
                skill.CurrentHitIndex++;
                AdvanceAttackTelegraph(skillEntityId, skill, state.ElapsedTime, schedule, in pools);

                Debug.Log($"[System_Skill] Commit {skill.CurrentHitIndex}/{schedule.CommitCount} ({schedule.Kind}) at {state.ElapsedTime:F3}s - SkillEntityId: {skillEntityId}");
            }
        }

        /// <summary>
        /// Channeling 타입 스킬 처리
        /// - 플레이어(InputComponent 보유): 슬롯 키를 유지하는 동안 tick. 입력을 떼면 End 상태로 전이하여 후딜레이/쿨타임 정상 처리
        /// - AI(InputComponent 없음): 항상 held 취급. 종료는 ProcessProcessState의 ProcessDuration 자동 종료 경로에서 처리
        /// </summary>
        private void ProcessChannelingSkill(int skillEntityId, ref SkillStateComponent state, ref SkillComponent skill, in SkillRuntimePools pools)
        {
            bool isHeld;
            if (pools.Input.TryGet(skill.OwnerEntityId, out var input))
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
                OnChangeState(skillEntityId, ref state, ref skill, SkillState.End, in pools);
                return;
            }

            if (pools.SkillTiming.TryGet(skillEntityId, out SkillTimingComponent timing) == false)
                return;

            SkillImpactSchedule schedule = ResolveImpactSchedule(skill, timing);
            if (schedule.HasCommit == false)
                return;

            // 첫 틱은 DamageTime × ProcessDuration, 이후 틱은 ChannelingInterval을 사용한다.
            float nextCommitTime = schedule.GetProcessCommitOffset(skill.CurrentHitIndex);
            if (state.ElapsedTime >= nextCommitTime)
            {
                ProcessSkillHit(skillEntityId, ref skill, in pools);
                skill.CurrentHitIndex++;
                skill.LastChannelingTime = state.ElapsedTime;
                AdvanceAttackTelegraph(skillEntityId, skill, state.ElapsedTime, schedule, in pools);

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
        private void ProcessToggleSkill(int skillEntityId, ref SkillStateComponent state, ref SkillComponent skill, in SkillRuntimePools pools)
        {
            // Toggle 스킬은 Process 상태에서 ON/OFF 전환만 처리
            if (!state.IsEffectApplied)
            {
                skill.IsToggleActive = !skill.IsToggleActive;

                if (skill.IsToggleActive)
                {
                    // 토글 ON - 버프 적용 등
                    ProcessSkillHit(skillEntityId, ref skill, in pools);
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
        private void ProcessSkillHit(int skillEntityId, ref SkillComponent skill, in SkillRuntimePools pools)
        {
            // SkillTarget 컴포넌트에서 타겟 정보 가져오기
            if (pools.SkillTarget.TryGet(skillEntityId, out var target) == false)
            {
                Debug.LogWarning($"[System_Skill] SkillTargetComponent not found - SkillEntityId: {skillEntityId}");
                return;
            }

            if(skill.Table == null)
            {
                Debug.LogError($"[System_Skill] SkillTable is null - SkillId({skill.SkillId}) SkillEntityId({skillEntityId})");
                return;
            }
            ARPG.Tables.SkillTable skillTable = skill.Table;

            string commitPhase = skillTable.AreaEffectId > 0
                ? "AreaCommit"
                : skillTable.ProjectileId > 0 ? "ProjectileCommit" : "HitCommit";
            target = CommitAim(skillEntityId, ref skill, target, commitPhase, in pools);

            Vector2 commitPosition;
            if (skillTable.SkillTargetType == GE.SkillTargetType.Position)
            {
                commitPosition = target.TargetPosition;
            }
            else if (pools.Transform.TryGet(skill.OwnerEntityId, out TransformComponent casterTransform) == true)
            {
                commitPosition = casterTransform.Position;
            }
            else
            {
                commitPosition = Vector2.zero;
            }

            // 기존 테이블의 "Hit" 같은 값은 Addressable 이펙트 키가 아니다.
            // Effects/ 주소가 명시된 경우에만 첫 커밋 연출을 한 번 재생한다.
            if (skill.CurrentHitIndex == 0
                && string.IsNullOrEmpty(skillTable.ActivateName) == false
                && skillTable.ActivateName.StartsWith("Effects/", System.StringComparison.Ordinal) == true)
            {
                Vector3 effectPosition = new Vector3(commitPosition.x, commitPosition.y, 0f);
                AR.s.Effect.Spawn(skillTable.ActivateName, effectPosition, Quaternion.identity).Forget();
            }

            SkillEffectContext commitContext = new()
            {
                SkillEntityId = skillEntityId,
                SkillId = skill.SkillId,
                OwnerEntityId = skill.OwnerEntityId,
                TargetPosition = commitPosition,
            };
            SkillEffectExecutor.Trigger(GE.SkillTrigger.OnSkillCommit, ref commitContext, skill.EffectiveSkillEffectIds);
            if (commitContext.SuppressDefaultImpact == true)
                return;

            // 장판은 공통 스케줄의 첫 커밋에서 논리 엔티티를 즉시 생성한다.
            // AreaEffect 시스템(priority 240)이 같은 FixedUpdate의 첫 틱을 처리한다.
            // 생성은 IsAreaEffectCreated로 한 실행에 1회만 보장하고, 직접 타격/발사체는 그대로 진행한다.
            // (장판과 직접 타격은 공존한다 — 예: 내려찍어 3연타 + 바닥에 불장판 1개)
            // 발사체가 장판을 운반하는 스킬은 여기서 만들지 않고 아래에서 던진 발사체가 착지할 때 깐다.
            if (skillTable.AreaEffectId > 0 && skillTable.DeliversAreaByProjectile == false)
            {
                if (skill.IsAreaEffectCreated == false)
                {
                    EntityFactory.CreateAreaEffect(
                        skill.OwnerEntityId,
                        skillTable.AreaEffectId,
                        commitPosition,
                        skill.SkillId);
                    skill.IsAreaEffectCreated = true;
                    AR.s.Component.SetComponent(skillEntityId, skill);
                }
            }

            // 발사체 스킬인 경우 발사체 생성 (BaseProjectileCount + Stat.ProjectileCountAdd만큼 부채꼴 발사)
            if (skillTable.ProjectileId > 0)
            {
                if (skillTable.DeliversAreaByProjectile == true)
                {
                    LaunchAreaDeliveryProjectile(skillEntityId, ref skill, commitPosition, in pools);
                    return;
                }

                if (pools.Transform.TryGet(skill.OwnerEntityId, out var ownerTransform))
                {
                    const float SPREAD_ANGLE_PER_SHOT = AttackTelegraphGeometryResolver.DefaultProjectileSpreadAngleDeg;

                    // 발사 시작점 = owner의 몸통 중심 (HitOffset 적용). 타겟도 몸통 기준이라 방향이 직관적
                    Vector2 spawnOrigin = ownerTransform.Position;
                    if (pools.Collider.TryGet(skill.OwnerEntityId, out var ownerCollider))
                    {
                        spawnOrigin += ownerCollider.HitOffset;
                    }

                    Vector2 baseDir = SkillAimResolver.NormalizeOrFallback(target.TargetDirection, Vector2.right);
                    float baseAngle = Mathf.Atan2(baseDir.y, baseDir.x) * Mathf.Rad2Deg;

                    int extraCount = 0;
                    if (pools.Stat.TryGet(skill.OwnerEntityId, out var ownerStat))
                        extraCount = ownerStat.FinalProjectileCountAdd;
                    int skillExtraCount = 0;
                    if (pools.SkillBonus.TryGet(skillEntityId, out var skillBonus))
                        skillExtraCount = skillBonus.ProjectileCountAddBonus;
                    int finalCount = AttackTelegraphGeometryResolver.ResolveProjectileCount(
                        skillTable.BaseProjectileCount,
                        extraCount,
                        skillExtraCount);

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
                            skillTable.ProjectileId,
                            spawnOrigin,
                            dir
                        );
                    }
                }

                return;
            }

            // 직접 타격이 없는 스킬(순수 장판 등)은 범위 판정을 돌리지 않는다.
            // 돌리면 데미지 0인 헛 타격이 OnHit 효과와 HitEffect를 발화시킨다.
            if (skillTable.HasDirectImpact == false)
                return;

            // 즉발 스킬: 기존 로직
            System.Collections.Generic.List<int> hitEntities = GetEntitiesInSkillRange(skill, target, in pools);

            for (int i = 0; i < hitEntities.Count; i++)
            {
                int hitEntityId = hitEntities[i];
                SkillHitProcessor.Process(new SkillHitRequest(skillEntityId, skill.OwnerEntityId, hitEntityId, 1f)
                {
                    IsProjectileDelivery = false,
                }, skill);
            }

            Debug.Log($"[System_Skill] ProcessSkillHit - SkillEntityId: {skillEntityId}, OwnerEntityId: {skill.OwnerEntityId}, HitCount: {hitEntities.Count}");
        }

        /// <summary>
        /// 발사체가 장판을 운반하는 스킬(SkillTable.DeliversAreaByProjectile)의 발사체를 목표 지점으로 던진다.
        /// 장판은 한 실행에 1개라는 규칙을 따라 발사체도 1발만 던진다 (추가 발사체 수는 적용하지 않음).
        /// </summary>
        private void LaunchAreaDeliveryProjectile(int skillEntityId, ref SkillComponent skill, Vector2 landingPosition, in SkillRuntimePools pools)
        {
            if (skill.IsAreaEffectCreated == true || skill.Table == null)
                return;

            if (pools.Transform.TryGet(skill.OwnerEntityId, out TransformComponent ownerTransform) == false)
                return;

            // 지면 경로는 발 위치에서 시작하고, 몸통 중심 높이(HitOffset.y)에서 출발해 착지 지점 바닥으로 내려앉는다.
            float startHeight = 0f;
            if (pools.Collider.TryGet(skill.OwnerEntityId, out ColliderComponent ownerCollider) == true)
                startHeight = ownerCollider.HitOffset.y;

            ProjectileHelper.SpawnArcProjectile(
                skill.OwnerEntityId,
                skillEntityId,
                skill.Table.ProjectileId,
                ownerTransform.Position,
                startHeight,
                landingPosition,
                skill.Table.ArcHeight,
                skill.Table.AreaEffectId,
                skill.SkillId);
            skill.IsAreaEffectCreated = true;
            AR.s.Component.SetComponent(skillEntityId, skill);
        }

        /// <summary>
        /// 스킬 범위 내에 있는 엔티티들을 가져옵니다.
        /// 반환 List는 _hitEntitiesCache를 재사용하므로, 다음 호출 전에 소비해야 함.
        /// </summary>
        private System.Collections.Generic.List<int> GetEntitiesInSkillRange(SkillComponent skill, SkillTargetComponent target, in SkillRuntimePools pools)
        {
            _hitEntitiesCache.Clear();

            if (skill.Table == null)
                return _hitEntitiesCache;

            // 스킬 타겟 타입에 따라 분기
            switch (skill.Table.SkillTargetType)
            {
                case GlobalEnum.SkillTargetType.Direction:
                case GlobalEnum.SkillTargetType.Position:
                    CheckCircleRangeEntities(skill, target, _hitEntitiesCache, in pools);
                    break;
            }

            return _hitEntitiesCache;
        }

        /// <summary>
        /// 원형 범위 내 엔티티 체크
        /// Range1: 거리, Range2: 각도 (360도면 전방향, 그 외는 부채꼴 한쪽 각도)
        /// Direction은 시전자 발 좌표, Position은 커밋된 지정 위치를 중심으로 사용한다.
        /// 타겟은 ColliderComponent.HitOffset/HitRadius로 보정한다.
        /// caster가 faction을 가지면 적 faction 인덱스만 순회, 없으면 transform 풀 폴백.
        /// </summary>
        private void CheckCircleRangeEntities(SkillComponent skill, SkillTargetComponent target, System.Collections.Generic.List<int> outHitEntities, in SkillRuntimePools pools)
        {
            if (skill.Table == null)
                return;

            SparseSet<TransformComponent> transformPool = pools.Transform;
            SparseSet<ColliderComponent> colliderPool = pools.Collider;
            SparseSet<FactionComponent> factionPool = pools.Faction;

            if (transformPool.TryGet(skill.OwnerEntityId, out var ownerTransform) == false)
            {
                Debug.LogError($"[System_Skill] Owner TransformComponent not found - OwnerEntityId: {skill.OwnerEntityId}");
                return;
            }

            bool isPositionTarget = skill.Table.SkillTargetType == GlobalEnum.SkillTargetType.Position;
            Vector2 originPosition = isPositionTarget == true ? target.TargetPosition : ownerTransform.Position;

            // Direction은 몸 가장자리 밖 추가 거리, Position은 지정 지점의 고리 반경이다.
            float ownerHitRadius = 0f;
            if (isPositionTarget == false && colliderPool.TryGet(skill.OwnerEntityId, out var ownerCollider))
                ownerHitRadius = ownerCollider.HitRadius;

            float range = ownerHitRadius + skill.Table.SkillTargetRange1;
            float halfAngleDeg = skill.Table.SkillTargetRange2; // 한쪽 각도 (360이면 전방향)
            Vector2 forward = target.TargetDirection;

            // 역방향 인덱스 없이 transform 풀을 순회하며 진영으로 적만 거른다.
            // owner에 faction이 있으면 적대 대상만, 없으면 전부(마이그레이션 안전 동작 보존).
            bool ownerHasFaction = factionPool.TryGet(skill.OwnerEntityId, out var ownerFaction);
            for (int i = 0; i < transformPool.Count; i++)
            {
                int entityId = transformPool.GetEntityId(i);
                if (entityId == skill.OwnerEntityId)
                    continue;

                if (ownerHasFaction)
                {
                    if (factionPool.TryGet(entityId, out var targetFaction) == false)
                        continue;
                    if (FactionHelper.IsEntityHostile(skill.OwnerEntityId, ownerFaction.FactionId, entityId, targetFaction.FactionId) == false)
                        continue;
                }

                TransformComponent entityTransform = transformPool.GetByIndex(i);
                Vector2 entityCenter;
                float entityRadius;
                if (colliderPool.TryGet(entityId, out var entityCollider))
                {
                    entityCenter = entityTransform.Position + entityCollider.HitOffset;
                    entityRadius = entityCollider.HitRadius;
                }
                else
                {
                    entityCenter = entityTransform.Position;
                    entityRadius = 0f;
                }

                if (HitboxMath.CircleVsSector(entityCenter, entityRadius, originPosition, forward, range, halfAngleDeg))
                {
                    outHitEntities.Add(entityId);
                }
            }
        }

        /// <summary>
        /// 특정 엔티티에게 스킬 효과를 적용합니다
        /// </summary>
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
        /// 스킬 강제 중단 (내부 사용)
        /// </summary>
        private void StopSkillInternal(int skillEntityId, ref SkillComponent inSkill, ref SkillStateComponent inState)
        {
            ComponentManager cm = AR.s.Component;
            SkillRuntimePools pools = new SkillRuntimePools(cm);
            StopSkillInternal(skillEntityId, ref inSkill, ref inState, in pools);
        }

        private void StopSkillInternal(int skillEntityId, ref SkillComponent inSkill, ref SkillStateComponent inState, in SkillRuntimePools pools)
        {
            OnSkillComplete(skillEntityId, ref inSkill, ref inState, true, in pools);
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
