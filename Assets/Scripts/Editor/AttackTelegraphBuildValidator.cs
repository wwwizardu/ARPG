#nullable enable
using System;
using System.Collections.Generic;
using System.Text;
using ARPG.Component;
using ARPG.Skill.Telegraph;
using ARPG.Tables;
using Newtonsoft.Json;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace ARPG.Editor
{
    /// <summary>
    /// 공격 예고의 정본 계약과 실제 몬스터 스킬 테이블을 빌드 직전에 강제 검증합니다.
    /// PlayMode 진입에는 관여하지 않습니다.
    /// </summary>
    public sealed class AttackTelegraphBuildValidator : IPreprocessBuildWithReport
    {
        private const string SKILL_TABLE_PATH = "Assets/_BinaryData/TableData/SkillTable.bytes";
        private const string PROJECTILE_TABLE_PATH = "Assets/_BinaryData/TableData/ProjectileTable.bytes";
        private const string AREA_EFFECT_TABLE_PATH = "Assets/_BinaryData/TableData/AreaEffectTable.bytes";
        private const string BUFF_TABLE_PATH = "Assets/_BinaryData/TableData/BuffTable.bytes";
        private const string AI_TABLE_PATH = "Assets/_BinaryData/TableData/AiTable.bytes";
        private const string MONSTER_TABLE_PATH = "Assets/_BinaryData/TableData/MonsterTable.bytes";
        private const float EPSILON = 0.0001f;

        public int callbackOrder => -1000;

        public void OnPreprocessBuild(BuildReport report)
        {
            List<string> errors = CollectErrors();
            if (errors.Count > 0)
                throw new BuildFailedException(BuildMessage(errors));

            Debug.Log("[AttackTelegraphBuildValidator] 공격 예고 계약 검증 통과");
        }

        [MenuItem("ARPG/Validation/Attack Telegraph Build Contract")]
        public static void ValidateFromMenu()
        {
            List<string> errors = CollectErrors();
            if (errors.Count > 0)
            {
                Debug.LogError(BuildMessage(errors));
                return;
            }

            Debug.Log("[AttackTelegraphBuildValidator] 공격 예고 계약 검증 통과");
        }

        [MenuItem("ARPG/Validation/Delayed Area Runtime Contract")]
        public static void ValidateDelayedAreaRuntime()
        {
            if (EditorApplication.isPlaying == false || AR.s.Data.GetSkill(208) == null)
            {
                Debug.LogError("[DelayedArea] GameScene 플레이 모드에서 테이블 로드 후 실행하세요.");
                return;
            }

            ComponentManager cm = AR.s.Component;
            List<int> created = new();
            List<string> errors = new();
            UnityEngine.Random.State previousRandom = UnityEngine.Random.state;
            AreaEffectTable repeatTable = AR.s.Data.GetAreaEffect(3)!;
            float previousDelay = repeatTable.ActivationDelay;
            string previousPrefab = repeatTable.PrefabKey;
            try
            {
                Vector2 center = new(1000000f, 1000000f);
                int caster = CreateAreaDiagnosticEntity(cm, created, center, Faction.Hostile, 0);
                int source = AR.s.Entity.CreateEntity();
                created.Add(source);
                cm.AddComponent(source, new SkillComponent { OwnerEntityId = caster, SkillId = 208, Table = AR.s.Data.GetSkill(208) });
                cm.AddComponent(source, new SkillAttackProfileComponent
                {
                    IceAddedMin = 10, IceAddedMax = 10, BaseDamageMul = 1f, SkillDamageMul = 2f,
                    CritMultiplier = 1.5f, DamageType = GlobalEnum.DamageType.Ice,
                });
                int inside = CreateAreaDiagnosticEntity(cm, created, center, Faction.Player, 50);
                int enters = CreateAreaDiagnosticEntity(cm, created, center + Vector2.right * 4f, Faction.Player, 0);
                int leaves = CreateAreaDiagnosticEntity(cm, created, center, Faction.Player, 0);
                int ally = CreateAreaDiagnosticEntity(cm, created, center, Faction.Hostile, 0);
                int outside = CreateAreaDiagnosticEntity(cm, created, center + Vector2.right * 4f, Faction.Player, 0);

                cm.AddComponent(source, new SkillTargetComponent { TargetPosition = center, TargetDirection = Vector2.right });
                var skillSystem = new Systems.System_Skill();
                var poolsType = typeof(Systems.System_Skill).GetNestedType("SkillRuntimePools", System.Reflection.BindingFlags.NonPublic)!;
                object runtimePools = Activator.CreateInstance(poolsType, new object[] { cm })!;
                var commit = typeof(Systems.System_Skill).GetMethod("ProcessSkillHit",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
                object[] commitArguments = { source, ReadAreaDiagnosticComponent<SkillComponent>(cm, source), runtimePools };
                commit.Invoke(skillSystem, commitArguments);
                Require(errors, ReadAreaDiagnosticComponent<StatComponent>(cm, inside).CurrentHp == 100,
                    "설치 커밋에서 직접 타격을 적용하면 안 됩니다.");
                var pool = cm.GetComponentPool<AreaEffectComponent>();
                int areaId = pool.GetEntityId(pool.Count - 1);
                created.Add(areaId);
                int installedCount = pool.Count;
                commit.Invoke(skillSystem, commitArguments);
                Require(errors, pool.Count == installedCount, "한 실행에서 설치 엔티티를 중복 생성하면 안 됩니다.");
                AreaEffectComponent area = ReadAreaDiagnosticComponent<AreaEffectComponent>(cm, areaId);
                Require(errors, cm.HasComponent<LifetimeComponent>(areaId) == false, "대기 수명은 활성 수명을 소비하면 안 됩니다.");
                Require(errors, cm.HasComponent<AttackTelegraphComponent>(areaId) == true, "대기 예고 원이 필요합니다.");
                RequireApproximately(errors, area.ActivationFixedTime - Time.fixedTime,
                    Mathf.Ceil(1.5f / Time.fixedDeltaTime) * Time.fixedDeltaTime, "설치 시각부터 지연해야 합니다.");

                var system = new Systems.System_AreaEffect();
                var update = typeof(Systems.System_AreaEffect).GetMethod("UpdateArea",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
                update.Invoke(system, new object[] { cm, areaId, area, Time.fixedDeltaTime });
                Require(errors, ReadAreaDiagnosticComponent<StatComponent>(cm, inside).CurrentHp == 100
                    && Utility.BuffHelper.HasBuff(inside, 6) == false, "대기 중 피해/슬로우가 없어야 합니다.");

                cm.SetComponent(enters, new TransformComponent { Position = center });
                cm.SetComponent(leaves, new TransformComponent { Position = center + Vector2.right * 4f });
                cm.SetComponent(source, new SkillAttackProfileComponent { IceAddedMin = 999, IceAddedMax = 999 });
                AR.s.Entity.DestroyEntity(caster);
                AR.s.Entity.DestroyEntity(source);
                Require(errors, cm.HasComponent<AttackTelegraphComponent>(areaId) == true, "시전자 제거 후에도 예고를 유지해야 합니다.");
                area.ActivationFixedTime = Time.fixedTime;
                cm.SetComponent(areaId, area);
                update.Invoke(system, new object[] { cm, areaId, area, Time.fixedDeltaTime });
                Require(errors, ReadAreaDiagnosticComponent<StatComponent>(cm, inside).CurrentHp == 90,
                    "설치 당시 피해 배율과 피격 당시 냉기 저항을 적용해야 합니다.");
                Require(errors, ReadAreaDiagnosticComponent<StatComponent>(cm, enters).CurrentHp == 80,
                    "설치 후 들어온 대상은 폭발 시 맞아야 합니다.");
                Require(errors, ReadAreaDiagnosticComponent<StatComponent>(cm, leaves).CurrentHp == 100
                    && ReadAreaDiagnosticComponent<StatComponent>(cm, ally).CurrentHp == 100
                    && ReadAreaDiagnosticComponent<StatComponent>(cm, outside).CurrentHp == 100,
                    "이탈자/아군/범위 밖 대상은 맞으면 안 됩니다.");
                Require(errors, Utility.BuffHelper.HasBuff(inside, 6) == true
                    && Utility.BuffHelper.HasBuff(inside, 3) == false, "명시적 Slow만 부여하고 자동 Chill은 부여하지 않습니다.");
                int slowId = Manager.EntityManager.GetDeterministicId(inside, Manager.EntityIdCategory.Buff, 6);
                RequireApproximately(errors, ReadAreaDiagnosticComponent<BuffInstance>(cm, slowId).RemainTime, 3f,
                    "냉기 폭발 둔화는 3초 유지해야 합니다.");
                RequireApproximately(errors, ReadAreaDiagnosticComponent<MoveSlowBuffComponent>(cm, slowId).SlowRate, 0.4f,
                    "냉기 폭발은 이동 속도를 40% 감소시켜야 합니다.");
                Require(errors, cm.HasComponent<DestroyTag>(areaId) == true
                    && cm.HasComponent<AttackTelegraphComponent>(areaId) == false, "폭발 후 예고/엔티티를 정리해야 합니다.");
                update.Invoke(system, new object[] { cm, areaId, ReadAreaDiagnosticComponent<AreaEffectComponent>(cm, areaId), Time.fixedDeltaTime });
                Require(errors, ReadAreaDiagnosticComponent<StatComponent>(cm, inside).CurrentHp == 90, "연속 FixedUpdate에서 재폭발하면 안 됩니다.");

                repeatTable.ActivationDelay = 0.3f;
                repeatTable.PrefabKey = string.Empty;
                Factory.EntityFactory.CreateAreaEffect(-1, 3, center, fallbackCasterFaction: Faction.Hostile);
                int repeatId = pool.GetEntityId(pool.Count - 1);
                created.Add(repeatId);
                AreaEffectComponent repeat = ReadAreaDiagnosticComponent<AreaEffectComponent>(cm, repeatId);
                update.Invoke(system, new object[] { cm, repeatId, repeat, Time.fixedDeltaTime });
                Require(errors, cm.HasComponent<LifetimeComponent>(repeatId) == false, "반복 장판도 준비 중에는 활성 수명이 없어야 합니다.");
                repeat.ActivationFixedTime = Time.fixedTime;
                cm.SetComponent(repeatId, repeat);
                update.Invoke(system, new object[] { cm, repeatId, repeat, Time.fixedDeltaTime });
                Require(errors, cm.HasComponent<LifetimeComponent>(repeatId) == true
                    && cm.HasComponent<DestroyTag>(repeatId) == false
                    && Utility.BuffHelper.HasBuff(inside, 5) == true, "반복 장판은 활성화 후 수명과 기존 틱 버프를 유지해야 합니다.");
                RequireApproximately(errors, ReadAreaDiagnosticComponent<LifetimeComponent>(cm, repeatId).Remaining,
                    repeatTable.Duration, "준비 시간은 활성 수명에서 차감하면 안 됩니다.");

                if (errors.Count > 0) throw new System.InvalidOperationException(BuildMessage(errors));
                Debug.Log("[DelayedArea] PASS: 설치 커밋 무피해/중복 방지, 대기 무피해, 진입/이탈, 아군 제외, 피해 스냅샷, 냉기 저항, 시전자 제거 후 발동, 40% Slow 3초, 단발 정리, 반복 장판 수명.");
            }
            finally
            {
                repeatTable.ActivationDelay = previousDelay;
                repeatTable.PrefabKey = previousPrefab;
                UnityEngine.Random.state = previousRandom;
                for (int i = created.Count - 1; i >= 0; i--)
                    if (AR.s.Entity.IsEntityRegistered(created[i]) == true) AR.s.Entity.DestroyEntity(created[i]);
            }
        }

        private static T ReadAreaDiagnosticComponent<T>(ComponentManager cm, int entityId) where T : struct
        {
            if (cm.TryGetComponent<T>(entityId, out var component) == true) return component;
            throw new System.InvalidOperationException($"진단 엔티티 {entityId}에 {typeof(T).Name}이 없습니다.");
        }

        private static int CreateAreaDiagnosticEntity(ComponentManager cm, List<int> created, Vector2 position,
            Faction faction, int iceResistance)
        {
            int entityId = AR.s.Entity.CreateEntity();
            created.Add(entityId);
            StatComponent stat = new() { BaseMaxHp = 100, FinalMaxHp = 100,
                FinalIceResist = iceResistance, FinalMaxIceResist = 75 };
            stat.SetCurrentHpDirect(100);
            cm.AddComponent(entityId, stat);
            cm.AddComponent(entityId, new TransformComponent { Position = position });
            cm.AddComponent(entityId, new FactionComponent { FactionId = faction });
            Systems.System_StatCalculation.BuildDefenseCache(entityId, stat);
            return entityId;
        }

        private static List<string> CollectErrors()
        {
            List<string> errors = new();
            ValidateCanonicalResolverContracts(errors);
            ValidateMonsterSkillTables(errors);
            return errors;
        }

        private static void ValidateCanonicalResolverContracts(List<string> errors)
        {
            SkillImpactSchedule direct = SkillImpactTimingResolver.Resolve(new SkillImpactTimingInput(
                SkillImpactExecutionMode.Single,
                0.2f,
                1f,
                0.5f,
                0f,
                0f,
                1,
                true,
                false,
                false,
                false));
            RequireApproximately(errors, direct.FirstCommitOffset, 0.7f,
                "직접 공격은 StartDuration + DamageTime × ProcessDuration에 커밋해야 합니다.");

            SkillImpactSchedule channel = SkillImpactTimingResolver.Resolve(new SkillImpactTimingInput(
                SkillImpactExecutionMode.Channeling,
                0.2f,
                3f,
                0.1f,
                0f,
                0.15f,
                1,
                true,
                false,
                false,
                false));
            RequireApproximately(errors, channel.GetCommitOffset(0), 0.5f,
                "채널 첫 틱은 DamageTime 기준이어야 합니다.");
            RequireApproximately(errors, channel.GetCommitOffset(1), 0.65f,
                "채널 반복 틱은 ChannelingInterval 기준이어야 합니다.");

            SkillImpactSchedule area = SkillImpactTimingResolver.Resolve(new SkillImpactTimingInput(
                SkillImpactExecutionMode.MultiHit,
                0.1f,
                0.4f,
                0.5f,
                0.1f,
                0f,
                4,
                false,
                false,
                true,
                false));
            Require(errors, area.Kind == SkillImpactKind.AreaActivation && area.CommitCount == 1,
                "AreaEffect는 DamageTime 커밋에서 한 번만 활성화해야 합니다.");

            SkillImpactSchedule projectile = SkillImpactTimingResolver.Resolve(new SkillImpactTimingInput(
                SkillImpactExecutionMode.Single,
                0f,
                0.15f,
                0.9f,
                0f,
                0f,
                1,
                true,
                true,
                false,
                false));
            Require(errors, projectile.Kind == SkillImpactKind.ProjectileRelease,
                "발사체 커밋은 피격이 아니라 Release여야 합니다.");
            RequireApproximately(
                errors,
                SkillImpactTimingResolver.GetQuantizedCommitOffset(projectile, 0, 0.02f),
                0.16f,
                "발사체 Release는 현행 FixedUpdate 상태 경계를 포함해야 합니다.");

            AttackTelegraphGeometry directGeometry = AttackTelegraphGeometryResolver.Resolve(
                new AttackTelegraphGeometryInput(
                    true,
                    false,
                    true,
                    false,
                    false,
                    false,
                    2f,
                    45f,
                    0.4f,
                    0.3f,
                    0f,
                    0f,
                    0f,
                    0f,
                    1,
                    15f));
            Require(errors,
                directGeometry.Shape == AttackTelegraphShape.OwnerSector
                && Mathf.Abs(directGeometry.Range - 2.7f) <= EPSILON
                && Mathf.Abs(directGeometry.HalfAngleDeg - 45f) <= EPSILON,
                "직접 공격 범위는 owner/player Collider 반경과 부채꼴 반각을 정확히 포함해야 합니다.");

            for (int projectileCount = 1; projectileCount <= 3; projectileCount++)
            {
                AttackTelegraphGeometry projectileGeometry = AttackTelegraphGeometryResolver.Resolve(
                    new AttackTelegraphGeometryInput(
                        true,
                        false,
                        true,
                        false,
                        true,
                        false,
                        5f,
                        0f,
                        0.4f,
                        0.3f,
                        0f,
                        8f,
                        1.5f,
                        0.2f,
                        projectileCount,
                        15f));
                Require(errors, projectileGeometry.Shape == AttackTelegraphShape.None,
                    $"발사체 스킬은 발사체 수({projectileCount})와 무관하게 공격 범위를 예고하지 않아야 합니다.");
            }

            AttackTelegraphGeometry areaDeliveryGeometry = AttackTelegraphGeometryResolver.Resolve(
                new AttackTelegraphGeometryInput(
                    false,
                    true,
                    false,
                    true,
                    true,
                    false,
                    0f,
                    360f,
                    0.4f,
                    0.3f,
                    1.5f,
                    6f,
                    3f,
                    0.2f,
                    1,
                    15f));
            Require(errors, areaDeliveryGeometry.Shape == AttackTelegraphShape.None,
                "발사체가 장판을 운반하는 위치 지정 스킬은 예고하지 않아야 합니다 (착지해 깔린 장판이 위험을 알림).");
        }

        private static void ValidateMonsterSkillTables(List<string> errors)
        {
            List<SkillTable> skills = LoadRows<SkillTable>(SKILL_TABLE_PATH, errors);
            List<ProjectileTable> projectiles = LoadRows<ProjectileTable>(PROJECTILE_TABLE_PATH, errors);
            List<AreaEffectTable> areas = LoadRows<AreaEffectTable>(AREA_EFFECT_TABLE_PATH, errors);
            List<BuffTable> buffs = LoadRows<BuffTable>(BUFF_TABLE_PATH, errors);
            List<AiTable> ais = LoadRows<AiTable>(AI_TABLE_PATH, errors);
            List<MonsterTable> monsters = LoadRows<MonsterTable>(MONSTER_TABLE_PATH, errors);
            if (errors.Count > 0)
                return;

            Dictionary<int, SkillTable> skillById = IndexRows(skills, errors, "Skill");
            Dictionary<int, ProjectileTable> projectileById = IndexRows(projectiles, errors, "Projectile");
            Dictionary<int, AreaEffectTable> areaById = IndexRows(areas, errors, "AreaEffect");
            Dictionary<int, BuffTable> buffById = IndexRows(buffs, errors, "Buff");
            Dictionary<int, AiTable> aiById = IndexRows(ais, errors, "AI");
            HashSet<int> validatedSkillIds = new();

            for (int i = 0; i < areas.Count; i++)
            {
                AreaEffectTable area = areas[i];
                if (float.IsNaN(area.ActivationDelay) == true || float.IsInfinity(area.ActivationDelay) == true
                    || area.ActivationDelay < 0f || area.Radius <= 0f || area.TickInterval < 0f
                    || (area.SingleShot == false && area.Duration <= 0f))
                    errors.Add($"AreaEffect {area.Id}({area.Name})의 지연/반경/수명이 유효하지 않습니다.");
                if (area.OnTickBuffId > 0)
                    ValidateAreaTickBuff(area, buffById, errors);
                if (area.SingleShot == true && string.IsNullOrEmpty(area.TickEffectName) == false)
                {
                    var settings = UnityEditor.AddressableAssets.AddressableAssetSettingsDefaultObject.Settings;
                    bool found = false;
                    if (settings != null)
                    {
                        for (int g = 0; g < settings.groups.Count; g++)
                        {
                            var group = settings.groups[g];
                            if (group == null) continue;
                            foreach (var entry in group.entries)
                                if (entry.address == area.TickEffectName) found = true;
                        }
                    }
                    if (found == false)
                        errors.Add($"AreaEffect {area.Id}의 폭발 이펙트 주소 {area.TickEffectName}이 없습니다.");
                }
            }

            // 아직 AI에 연결하지 않은 설치형 콘텐츠도 빌드 전에 검증한다.
            for (int i = 0; i < skills.Count; i++)
            {
                SkillTable skill = skills[i];
                if (skill.AreaEffectId > 0 && areaById.TryGetValue(skill.AreaEffectId, out var area) == true
                    && area.SingleShot == true)
                {
                    ValidateMonsterSkillReference(skill.Id, 0, skillById, projectileById, areaById, buffById,
                        validatedSkillIds, errors);
                    if (skill.ProjectileId == 0 && skill.ExecutionType != SkillExecutionType.Single)
                        errors.Add($"단발 설치 Skill {skill.Id}는 Single 실행 타입이어야 합니다.");
                }
            }

            for (int i = 0; i < monsters.Count; i++)
            {
                MonsterTable monster = monsters[i];
                if (aiById.TryGetValue(monster.AiTableId, out AiTable ai) == false)
                {
                    errors.Add($"Monster {monster.Id}({monster.Name})의 AiTableId {monster.AiTableId}가 없습니다.");
                    continue;
                }

                ValidateMonsterSkillReference(ai.SkillId1, ai.Id, skillById, projectileById, areaById, buffById,
                    validatedSkillIds, errors);
                ValidateMonsterSkillReference(ai.SkillId2, ai.Id, skillById, projectileById, areaById, buffById,
                    validatedSkillIds, errors);
                ValidateMonsterSkillReference(ai.SkillId3, ai.Id, skillById, projectileById, areaById, buffById,
                    validatedSkillIds, errors);
            }
        }

        private static void ValidateMonsterSkillReference(
            int skillId,
            int aiId,
            Dictionary<int, SkillTable> skillById,
            Dictionary<int, ProjectileTable> projectileById,
            Dictionary<int, AreaEffectTable> areaById,
            Dictionary<int, BuffTable> buffById,
            HashSet<int> validatedSkillIds,
            List<string> errors)
        {
            if (skillId <= 0 || validatedSkillIds.Add(skillId) == false)
                return;
            if (skillById.TryGetValue(skillId, out SkillTable skill) == false)
            {
                errors.Add($"AI {aiId}의 SkillId {skillId}가 SkillTable에 없습니다.");
                return;
            }

            bool hasDirectImpact = skill.HasDirectImpact;
            bool hasProjectile = skill.ProjectileId > 0;
            bool hasArea = skill.AreaEffectId > 0;
            if (hasDirectImpact == false && hasProjectile == false && hasArea == false)
                return;

            if (skill.DamageTime < 0f || skill.DamageTime > 1f)
                errors.Add($"Skill {skill.Id}({skill.Name}) DamageTime {skill.DamageTime}은 0~1 범위여야 합니다.");
            if (skill.StartTime < 0f || skill.ProcessTime < 0f || skill.EndTime < 0f)
                errors.Add($"Skill {skill.Id}({skill.Name})의 Start/Process/End 시간은 음수일 수 없습니다.");
            if (skill.ExecutionType == SkillExecutionType.Channeling && skill.ChannelingInterval <= 0f)
                errors.Add($"채널 Skill {skill.Id}({skill.Name})의 ChannelingInterval은 0보다 커야 합니다.");
            if (skill.ExecutionType == SkillExecutionType.MultiHit && skill.HitCount > 1 && skill.HitInterval < 0f)
                errors.Add($"다단 Skill {skill.Id}({skill.Name})의 HitInterval은 음수일 수 없습니다.");

            ProjectileTable? projectile = null;
            if (hasProjectile == true)
            {
                if (projectileById.TryGetValue(skill.ProjectileId, out projectile) == false)
                {
                    errors.Add($"Skill {skill.Id}({skill.Name})의 ProjectileId {skill.ProjectileId}가 없습니다.");
                }
                else if (projectile.Speed <= 0f || projectile.LifeTime <= 0f || projectile.HitRadius < 0f)
                {
                    errors.Add($"Projectile {projectile.Id}({projectile.Name})의 Speed/LifeTime/HitRadius가 유효하지 않습니다.");
                }
            }

            AreaEffectTable? area = null;
            if (hasArea == true)
            {
                if (areaById.TryGetValue(skill.AreaEffectId, out area) == false)
                {
                    errors.Add($"Skill {skill.Id}({skill.Name})의 AreaEffectId {skill.AreaEffectId}가 없습니다.");
                }
                else if (area.Radius <= 0f || (area.SingleShot == false && area.Duration <= 0f) || area.TickInterval < 0f)
                {
                    errors.Add($"AreaEffect {area.Id}({area.Name})의 Radius/Duration/TickInterval이 유효하지 않습니다.");
                }
                else if (area.OnTickBuffId > 0)
                {
                    ValidateAreaTickBuff(area, buffById, errors);
                }
            }

            SkillImpactExecutionMode executionMode;
            switch (skill.ExecutionType)
            {
                case SkillExecutionType.Single:
                    executionMode = SkillImpactExecutionMode.Single;
                    break;
                case SkillExecutionType.MultiHit:
                    executionMode = SkillImpactExecutionMode.MultiHit;
                    break;
                case SkillExecutionType.Channeling:
                    executionMode = SkillImpactExecutionMode.Channeling;
                    break;
                default:
                    errors.Add($"공격 Skill {skill.Id}({skill.Name})의 ExecutionType {skill.ExecutionType}은 예고 스케줄을 지원하지 않습니다.");
                    return;
            }

            bool usesLanding = skill.UsesLandingCommit;
            SkillImpactSchedule schedule = SkillImpactTimingResolver.Resolve(new SkillImpactTimingInput(
                executionMode,
                skill.StartTime,
                skill.ProcessTime,
                skill.DamageTime,
                skill.HitInterval,
                skill.ChannelingInterval,
                skill.HitCount,
                hasDirectImpact,
                hasProjectile,
                hasArea,
                usesLanding));
            if (schedule.HasCommit == false)
                errors.Add($"공격 Skill {skill.Id}({skill.Name})에서 커밋 스케줄을 만들 수 없습니다.");

            AttackTelegraphGeometry geometry = AttackTelegraphGeometryResolver.Resolve(
                new AttackTelegraphGeometryInput(
                    skill.SkillTargetType == GlobalEnum.SkillTargetType.Direction,
                    skill.SkillTargetType == GlobalEnum.SkillTargetType.Position,
                    hasDirectImpact,
                    hasArea && area != null,
                    hasProjectile && projectile != null,
                    usesLanding,
                    skill.SkillTargetRange1,
                    skill.SkillTargetRange2,
                    0f,
                    0f,
                    area != null ? area.Radius : 0f,
                    projectile != null ? projectile.Speed : 0f,
                    projectile != null ? projectile.LifeTime : 0f,
                    projectile != null ? projectile.HitRadius : 0f,
                    Mathf.Max(1, skill.BaseProjectileCount),
                    AttackTelegraphGeometryResolver.DefaultProjectileSpreadAngleDeg));
            // 투사체와 장판 스킬은 자체 비주얼이 위험을 알리므로 예고 도형이 없어도 정상이다.
            // 공격 예고는 직접 범위 타격과 설치된 지연 장판만 담당한다.
            if (geometry.Shape == AttackTelegraphShape.None && hasArea == false && hasProjectile == false)
                errors.Add($"공격 Skill {skill.Id}({skill.Name})에서 예고 도형을 만들 수 없습니다.");
        }

        // 장판이 매 틱 다시 거는 버프는 틱 간격 이상 유지돼야 장판 안에서 효과가 끊기지 않는다.
        // 둔화의 FadeOutTime 회복 구간은 Duration 뒤에 붙으므로 최대 효과 유지 시간인 Duration만 비교한다.
        private static void ValidateAreaTickBuff(AreaEffectTable area, Dictionary<int, BuffTable> buffById, List<string> errors)
        {
            if (buffById.TryGetValue(area.OnTickBuffId, out BuffTable buff) == false)
            {
                errors.Add($"AreaEffect {area.Id}({area.Name})의 OnTickBuffId {area.OnTickBuffId}가 BuffTable에 없습니다.");
                return;
            }

            if (area.SingleShot == false && buff.Duration < area.TickInterval)
                errors.Add($"AreaEffect {area.Id}({area.Name})가 거는 Buff {buff.Id}({buff.Name})의 Duration {buff.Duration}이 장판 TickInterval {area.TickInterval}보다 짧아 장판 안에서 효과가 끊깁니다.");
        }

        private static List<T> LoadRows<T>(string assetPath, List<string> errors)
        {
            TextAsset asset = AssetDatabase.LoadAssetAtPath<TextAsset>(assetPath);
            if (asset == null)
            {
                errors.Add($"테이블 에셋을 찾을 수 없습니다: {assetPath}");
                return new List<T>();
            }

            try
            {
                return JsonConvert.DeserializeObject<List<T>>(asset.text) ?? new List<T>();
            }
            catch (Exception exception)
            {
                errors.Add($"테이블 파싱 실패: {assetPath} - {exception.Message}");
                return new List<T>();
            }
        }

        private static Dictionary<int, T> IndexRows<T>(List<T> rows, List<string> errors, string tableName)
            where T : TableBase
        {
            Dictionary<int, T> result = new(rows.Count);
            for (int i = 0; i < rows.Count; i++)
            {
                T row = rows[i];
                if (result.TryAdd(row.Id, row) == false)
                    errors.Add($"{tableName}Table에 중복 Id {row.Id}가 있습니다.");
            }
            return result;
        }

        private static void Require(List<string> errors, bool condition, string message)
        {
            if (condition == false)
                errors.Add(message);
        }

        private static void RequireApproximately(List<string> errors, float actual, float expected, string message)
        {
            if (Mathf.Abs(actual - expected) > EPSILON)
                errors.Add($"{message} Expected={expected}, Actual={actual}");
        }

        private static string BuildMessage(List<string> errors)
        {
            StringBuilder builder = new();
            builder.AppendLine("[AttackTelegraphBuildValidator] 빌드를 중단했습니다.");
            for (int i = 0; i < errors.Count; i++)
            {
                builder.Append("- ");
                builder.AppendLine(errors[i]);
            }
            return builder.ToString();
        }
    }
}
