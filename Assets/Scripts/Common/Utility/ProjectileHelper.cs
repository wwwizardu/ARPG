using ARPG.Component;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.AddressableAssets;

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

            // 프리팹 로드
            GameObject obj = null;
            if (string.IsNullOrEmpty(table.PrefabKey) == false)
            {
                try
                {
                    obj = await Addressables.InstantiateAsync(
                        table.PrefabKey,
                        new Vector3(spawnPosition.x, spawnPosition.y, 0f),
                        Quaternion.identity
                    ).ToUniTask();
                }
                catch (System.Exception e)
                {
                    Debug.LogError($"[ProjectileHelper] Failed to load prefab '{table.PrefabKey}': {e.Message}");
                    return;
                }
            }

            if (obj == null)
            {
                // 프리팹이 없으면 빈 오브젝트 생성
                obj = new GameObject($"Projectile_{projectileTableId}");
                obj.transform.position = new Vector3(spawnPosition.x, spawnPosition.y, 0f);
            }

            // 방향에 따라 회전
            float angle = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg;
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

            // System_Render에 등록 (ECS Position → GameObject transform 동기화)
            var renderSystem = AR.s.System.GetSystem<Systems.System_Render>();
            if (renderSystem != null)
            {
                renderSystem.RegisterGameObject(entityId, obj);
            }

            Debug.Log($"[ProjectileHelper] Spawned projectile - EntityId: {entityId}, TableId: {projectileTableId}, Owner: {ownerEntityId}, Direction: {direction}");
        }
    }
}
