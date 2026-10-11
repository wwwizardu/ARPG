#nullable enable
using ARPG.Component;
using ARPG.Skill.Combat;
using Cysharp.Threading.Tasks;
using UnityEngine;
using GE = GlobalEnum;

namespace ARPG.Utility
{
    public static class ProjectileHelper
    {
        public static int SpawnProjectile(
            int ownerEntityId,
            int skillEntityId,
            int projectileTableId,
            Vector2 spawnPosition,
            Vector2 direction)
        {
            Tables.ProjectileTable? table = AR.s.Data.GetProjectile(projectileTableId);
            if (table == null)
            {
                Debug.LogError($"[ProjectileHelper] ProjectileTable not found - Id: {projectileTableId}");
                return -1;
            }

            Vector2 normalizedDirection = direction.sqrMagnitude > 0.0001f
                ? direction.normalized
                : Vector2.right;
            float releaseAngle = Mathf.Atan2(normalizedDirection.y, normalizedDirection.x) * Mathf.Rad2Deg;

            int entityId = CreateProjectileEntity(
                ownerEntityId,
                skillEntityId,
                projectileTableId,
                table,
                spawnPosition,
                normalizedDirection,
                table.Speed,
                releaseAngle,
                out long visualRequestToken);
            FinishSpawn(entityId, ownerEntityId, skillEntityId, projectileTableId, table, spawnPosition, releaseAngle, visualRequestToken);

            Debug.Log($"[ProjectileHelper] Released projectile - EntityId: {entityId}, TableId: {projectileTableId}, Owner: {ownerEntityId}, Direction: {normalizedDirection}");
            return entityId;
        }

        /// <summary>
        /// 목표 지점까지 포물선으로 날아가 내려앉으면 장판을 까는 발사체를 만든다 (SkillTable.DeliversAreaByProjectile).
        /// 비행 시간은 거리 / ProjectileTable.Speed이며 LifeTime은 쓰지 않는다. 비행 중에는 아무도 맞히지 않는다.
        /// </summary>
        /// <param name="startHeight">출발 높이 (시전자 몸통 중심). 착지할 때까지 0으로 줄어든다</param>
        public static int SpawnArcProjectile(
            int ownerEntityId,
            int skillEntityId,
            int projectileTableId,
            Vector2 startPosition,
            float startHeight,
            Vector2 landingPosition,
            float arcHeight,
            int landingAreaEffectId,
            int skillId)
        {
            Tables.ProjectileTable? table = AR.s.Data.GetProjectile(projectileTableId);
            if (table == null)
            {
                Debug.LogError($"[ProjectileHelper] ProjectileTable not found - Id: {projectileTableId}");
                return -1;
            }

            Vector2 toLanding = landingPosition - startPosition;
            float distance = toLanding.magnitude;
            Vector2 direction = distance > 0.0001f ? toLanding / distance : Vector2.right;
            float flightDuration = table.Speed > 0f ? distance / table.Speed : 0f;

            // 발사 시점 진영을 남겨 둔다. 비행 중 시전자가 죽어도 장판이 같은 편을 노린다.
            Faction casterFaction = Faction.Neutral;
            if (AR.s.Component.TryGetComponent<FactionComponent>(ownerEntityId, out FactionComponent ownerFaction) == true)
                casterFaction = ownerFaction.FactionId;

            // 위치는 System_Projectile이 경로로 직접 계산하므로 속도는 0으로 둔다 (System_Render의 속도 적분 제외).
            // 회전도 0으로 둔다. 높이는 Visual의 로컬 Y로 띄우므로 루트가 돌아가 있으면 화면 위가 아닌 옆으로 뜬다.
            int entityId = CreateProjectileEntity(
                ownerEntityId,
                skillEntityId,
                projectileTableId,
                table,
                startPosition,
                direction,
                0f,
                0f,
                out long visualRequestToken);
            AR.s.Component.AddComponent(entityId, new ProjectileArcComponent
            {
                StartPosition = startPosition,
                EndPosition = landingPosition,
                FlightDuration = flightDuration,
                Elapsed = 0f,
                StartHeight = Mathf.Max(0f, startHeight),
                ArcHeight = Mathf.Max(0f, arcHeight),
                Height = Mathf.Max(0f, startHeight),
                LandingAreaEffectId = landingAreaEffectId,
                SkillId = skillId,
                CasterFaction = casterFaction,
            });
            FinishSpawn(entityId, ownerEntityId, skillEntityId, projectileTableId, table, startPosition, 0f, visualRequestToken);

            Debug.Log($"[ProjectileHelper] Released arc projectile - EntityId: {entityId}, TableId: {projectileTableId}, Owner: {ownerEntityId}, Landing: {landingPosition}, Flight: {flightDuration:F2}s, AreaEffect: {landingAreaEffectId}");
            return entityId;
        }

        /// <summary>
        /// 발사체 논리 엔티티와 공통 컴포넌트를 만든다.
        /// Release FixedUpdate에서 논리 엔티티를 먼저 만든다. Addressable 로딩은 게임플레이를 지연하지 않는다.
        /// </summary>
        private static int CreateProjectileEntity(
            int ownerEntityId,
            int skillEntityId,
            int projectileTableId,
            Tables.ProjectileTable table,
            Vector2 spawnPosition,
            Vector2 direction,
            float speed,
            float rotation,
            out long visualRequestToken)
        {
            int entityId = AR.s.Entity.CreateEntity();
            visualRequestToken = System.Diagnostics.Stopwatch.GetTimestamp() ^ ((long)entityId << 32);

            // ECS 컴포넌트 추가
            AR.s.Component.AddComponent(entityId, new TransformComponent
            {
                Position = spawnPosition,
                Rotation = rotation,
                Scale = Vector2.one
            });

            AR.s.Component.AddComponent(entityId, new VelocityComponent
            {
                Direction = direction,
                Speed = speed
            });

            AR.s.Component.AddComponent(entityId, new ProjectileComponent
            {
                OwnerEntityId = ownerEntityId,
                SkillEntityId = skillEntityId,
                ProjectileTableId = projectileTableId,
                LifeTime = table.LifeTime,
                CurrentLifeTime = 0f,
                HitRadius = table.HitRadius,
                IsPiercing = table.IsPiercing,
                VisualRequestToken = visualRequestToken,
            });

            AR.s.Component.AddComponent(entityId, new ProjectileTag());

            // 플레이어 편이 아닌 투사체는 시야 밖에서 숨긴다 (FOG_OF_WAR_DESIGN §5). 숨김 상태로 시작하고 표시는 프리팹 로드 뒤 적용한다.
            if (AR.s.Component.TryGetComponent<FactionComponent>(ownerEntityId, out FactionComponent ownerFaction) == false ||
                ownerFaction.FactionId != Faction.Player)
            {
                AR.s.Component.AddComponent(entityId, new FogVisibilityComponent { IsVisible = AR.s.Fog.IsActive == false });
            }

            return entityId;
        }

        /// <summary>
        /// 모든 컴포넌트가 붙은 뒤 OnProjectileSpawn 효과를 발화하고 View 로드를 시작한다.
        /// </summary>
        private static void FinishSpawn(
            int entityId,
            int ownerEntityId,
            int skillEntityId,
            int projectileTableId,
            Tables.ProjectileTable table,
            Vector2 spawnPosition,
            float releaseAngle,
            long visualRequestToken)
        {
            // [SkillEffect] OnProjectileSpawn 트리거
            if (AR.s.Component.TryGetComponent<SkillComponent>(skillEntityId, out var skill) == true
                && skill.Table != null)
            {
                SkillEffectContext spawnCtx = new()
                {
                    SkillEntityId = skillEntityId,
                    SkillId = skill.SkillId,
                    OwnerEntityId = ownerEntityId,
                    ProjectileEntityId = entityId,
                };
                SkillEffectExecutor.Trigger(GE.SkillTrigger.OnProjectileSpawn, ref spawnCtx, skill.EffectiveSkillEffectIds);
            }

            AttachProjectileViewAsync(
                entityId,
                ownerEntityId,
                projectileTableId,
                visualRequestToken,
                table.PrefabKey,
                spawnPosition,
                releaseAngle).Forget();
        }

        private static async UniTaskVoid AttachProjectileViewAsync(
            int entityId,
            int ownerEntityId,
            int projectileTableId,
            long visualRequestToken,
            string prefabKey,
            Vector2 spawnPosition,
            float releaseAngle)
        {
            if (string.IsNullOrEmpty(prefabKey) == true)
                return;

            Vector3 requestedPosition = new(spawnPosition.x, spawnPosition.y, 0f);
            Quaternion requestedRotation = Quaternion.Euler(0f, 0f, releaseAngle);
            GameObject? obj = await AddressablePool.Get(prefabKey, requestedPosition, requestedRotation);
            if (obj == null)
                return;

            if (AR.s == null)
            {
                AddressablePool.Return(prefabKey, obj);
                return;
            }

            ComponentManager cm = AR.s.Component;
            if (cm.HasComponent<DestroyTag>(entityId) == true
                || cm.TryGetComponent<ProjectileComponent>(entityId, out ProjectileComponent projectile) == false
                || projectile.OwnerEntityId != ownerEntityId
                || projectile.ProjectileTableId != projectileTableId
                || projectile.VisualRequestToken != visualRequestToken)
            {
                AddressablePool.Return(prefabKey, obj);
                return;
            }

            // 회전은 System_Render가 매 프레임 적용하는 Transform.Rotation과 맞춘다 (포물선 발사체는 0으로 고정).
            float currentAngle = releaseAngle;
            if (cm.TryGetComponent<TransformComponent>(entityId, out TransformComponent currentTransform) == true)
            {
                obj.transform.position = new Vector3(currentTransform.Position.x, currentTransform.Position.y, 0f);
                currentAngle = currentTransform.Rotation;
            }
            obj.transform.rotation = Quaternion.Euler(0f, 0f, currentAngle);

            Systems.System_Render? renderSystem = AR.s.System.GetSystem<Systems.System_Render>();
            Base.EntityBase? entityBase = obj.GetComponent<Base.EntityBase>();
            if (renderSystem == null || entityBase == null)
            {
                Debug.LogError($"[ProjectileHelper] EntityBase/System_Render missing for projectile prefab '{prefabKey}'.");
                AddressablePool.Return(prefabKey, obj);
                return;
            }

            // 풀에서 꺼낸 프리팹에는 이전 투사체의 숨김 상태가 남아 있을 수 있다. 지금 판정 상태로 맞춘다 (FOG_OF_WAR_DESIGN §5).
            bool visible = cm.TryGetComponent<FogVisibilityComponent>(entityId, out FogVisibilityComponent fog) == false || fog.IsVisible;
            if (entityBase.Visual != null && entityBase.Visual.activeSelf != visible)
                entityBase.Visual.SetActive(visible);

            renderSystem.RegisterEntity(entityId, entityBase);
        }
    }
}
