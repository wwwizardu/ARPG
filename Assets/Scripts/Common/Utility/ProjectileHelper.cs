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
        public static async void SpawnProjectile(
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
                return;
            }

            // 방향에 따라 회전
            float angle = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg;
            Vector3 spawnPos = new Vector3(spawnPosition.x, spawnPosition.y, 0f);
            Quaternion rotation = Quaternion.Euler(0f, 0f, angle);

            // AddressablePool에서 가져오기
            GameObject? obj = null;
            if (string.IsNullOrEmpty(table.PrefabKey) == false)
            {
                obj = await AddressablePool.Get(table.PrefabKey, spawnPos, rotation);
            }

            if (obj == null)
            {
                // 프리팹이 없으면 빈 오브젝트 생성
                obj = new GameObject($"Projectile_{projectileTableId}");
                obj.transform.position = spawnPos;
                obj.transform.rotation = rotation;
            }
            obj.transform.rotation = Quaternion.Euler(0f, 0f, angle);

            // 엔티티 ID 발급
            int entityId = EntityIdHelper.CreateEntity();

            // ECS 컴포넌트 추가
            AR.s.Component.AddComponent(entityId, new TransformComponent
            {
                Position = spawnPosition,
                Rotation = angle,
                Scale = Vector2.one
            });

            AR.s.Component.AddComponent(entityId, new VelocityComponent
            {
                Direction = direction,
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
                IsPiercing = table.IsPiercing
            });

            AR.s.Component.AddComponent(entityId, new ProjectileTag());

            // [SkillEffect] OnProjectileSpawn 트리거
            if (AR.s.Component.TryGetComponent<SkillComponent>(skillEntityId, out var skill) && skill.Table != null)
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

            // System_Render에 등록 (ECS Position → GameObject transform 동기화)
            var renderSystem = AR.s.System.GetSystem<Systems.System_Render>();
            if (renderSystem != null)
            {
                var entityBase = obj.GetComponent<Base.EntityBase>();
                if (entityBase != null)
                {
                    renderSystem.RegisterEntity(entityId, entityBase);
                }
                else
                {
                    Debug.LogError($"[ProjectileHelper] EntityBase component not found on projectile prefab. Add EntityBase to {obj.name}.");
                }
            }

            Debug.Log($"[ProjectileHelper] Spawned projectile - EntityId: {entityId}, TableId: {projectileTableId}, Owner: {ownerEntityId}, Direction: {direction}");
        }
    }
}
