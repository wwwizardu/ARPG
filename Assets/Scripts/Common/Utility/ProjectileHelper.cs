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

            // Release FixedUpdate에서 논리 엔티티를 먼저 만든다. Addressable 로딩은 게임플레이를 지연하지 않는다.
            int entityId = AR.s.Entity.CreateEntity();
            long visualRequestToken = System.Diagnostics.Stopwatch.GetTimestamp() ^ ((long)entityId << 32);

            // ECS 컴포넌트 추가
            AR.s.Component.AddComponent(entityId, new TransformComponent
            {
                Position = spawnPosition,
                Rotation = releaseAngle,
                Scale = Vector2.one
            });

            AR.s.Component.AddComponent(entityId, new VelocityComponent
            {
                Direction = normalizedDirection,
                Speed = table.Speed
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
                normalizedDirection).Forget();

            Debug.Log($"[ProjectileHelper] Released projectile - EntityId: {entityId}, TableId: {projectileTableId}, Owner: {ownerEntityId}, Direction: {normalizedDirection}");
            return entityId;
        }

        private static async UniTaskVoid AttachProjectileViewAsync(
            int entityId,
            int ownerEntityId,
            int projectileTableId,
            long visualRequestToken,
            string prefabKey,
            Vector2 spawnPosition,
            Vector2 releaseDirection)
        {
            if (string.IsNullOrEmpty(prefabKey) == true)
                return;

            float releaseAngle = Mathf.Atan2(releaseDirection.y, releaseDirection.x) * Mathf.Rad2Deg;
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

            float currentAngle = releaseAngle;
            if (cm.TryGetComponent<TransformComponent>(entityId, out TransformComponent currentTransform) == true)
            {
                obj.transform.position = new Vector3(currentTransform.Position.x, currentTransform.Position.y, 0f);
            }
            if (cm.TryGetComponent<VelocityComponent>(entityId, out VelocityComponent velocity) == true)
            {
                currentAngle = Mathf.Atan2(velocity.Direction.y, velocity.Direction.x) * Mathf.Rad2Deg;
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
