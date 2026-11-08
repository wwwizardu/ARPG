using ARPG.Component;
using ARPG.Utility;
using UnityEngine;

namespace ARPG.Systems
{
    /// <summary>
    /// 비행 중 방향을 바꾸는 발사체(전기 불꽃 등)의 속도 방향을 정한다 (SKILL_DESIGN §6.4).
    /// Priority 148 (FixedUpdate): 소환 정리(145) 뒤, 발사체 충돌(150) 앞.
    /// 위치는 바꾸지 않는다. 바꾼 방향으로 System_Render가 옮기고, System_Projectile이 지나온 선분으로 충돌을 검사한다.
    /// </summary>
    public class System_ProjectileSteering : IFixedUpdateSystem
    {
        public int Priority => 148;
        public float UpdateInterval => 0f;

        // 벽 확인 거리 = 이번 틱 이동 거리 × 이 배수. 다음 검사 전에 Render가 옮길 거리보다 넉넉히 앞을 본다
        private const float WallLookAheadSteps = 2f;

        public void OnCreate()
        {
            Debug.Log("[System_ProjectileSteering] Created");
        }

        public void OnReset()
        {
        }

        public void OnFixedUpdate(float inFixedDeltaTime)
        {
            ComponentManager cm = AR.s.Component;
            SparseSet<ProjectileSteeringComponent> steeringPool = cm.GetComponentPool<ProjectileSteeringComponent>();
            if (steeringPool == null || steeringPool.Count == 0)
                return;

            SparseSet<VelocityComponent> velocityPool = cm.GetComponentPool<VelocityComponent>();
            SparseSet<TransformComponent> transformPool = cm.GetComponentPool<TransformComponent>();
            SparseSet<ProjectileComponent> projectilePool = cm.GetComponentPool<ProjectileComponent>();

            for (int i = 0; i < steeringPool.Count; i++)
            {
                int entityId = steeringPool.GetEntityId(i);
                if (cm.HasComponent<DestroyTag>(entityId) == true)
                    continue;
                if (velocityPool.TryGet(entityId, out VelocityComponent velocity) == false || velocity.Speed <= 0f)
                    continue;
                if (transformPool.TryGet(entityId, out TransformComponent transform) == false)
                    continue;

                ProjectileSteeringComponent steering = steeringPool.GetByIndex(i);
                Vector2 direction = velocity.Direction;

                // 지그재그: 간격마다 기준 방향 ± WanderAngle 안에서 새 방향을 고른다.
                // WanderDrift가 있으면 기준 방향도 그만큼씩 무작위로 돌아가 경로가 발마다 다른 쪽으로 휜다. 0이면 기준 방향이 고정돼 곧게 나아간다
                if (steering.WanderInterval > 0f && steering.WanderAngle > 0f)
                {
                    steering.WanderTimer -= inFixedDeltaTime;
                    if (steering.WanderTimer <= 0f)
                    {
                        steering.WanderTimer += steering.WanderInterval;
                        if (steering.WanderTimer <= 0f)
                            steering.WanderTimer = steering.WanderInterval;

                        if (steering.WanderDrift > 0f)
                            steering.Heading = Rotate(steering.Heading, Random.Range(-steering.WanderDrift, steering.WanderDrift));

                        direction = Rotate(steering.Heading, Random.Range(-steering.WanderAngle, steering.WanderAngle));
                    }
                }

                // 벽 튕김: 새 방향까지 정한 뒤 앞이 막혔는지 본다. 막힌 축의 성분을 방향·기준 방향 모두 뒤집는다
                if (steering.BounceOnWall == true)
                {
                    float radius = projectilePool.TryGet(entityId, out ProjectileComponent projectile) == true ? projectile.HitRadius : 0f;
                    Vector2 position = transform.Position;
                    if (CollisionUtil.IsBlockedAt(position, radius) == true)
                    {
                        // 프레임이 끊겨 앞을 보기 전에 벽 안으로 들어왔으면, 들어온 축을 뒤집어 온 길로 돌려보낸다.
                        // 지난 틱에도 벽 안이었으면(벽에 붙어 생성 등) 뒤집지 않는다. 뒤집으면 벽 안에서 왕복하며 갇힌다
                        if (CollisionUtil.IsBlockedAt(steering.LastPosition, radius) == false)
                            ReflectEnteredAxis(steering.LastPosition, position, radius, ref direction, ref steering.Heading);
                    }
                    else
                    {
                        float lookAhead = velocity.Speed * inFixedDeltaTime * WallLookAheadSteps;
                        ReflectOffWalls(position, radius, lookAhead, ref direction, ref steering.Heading);
                    }
                }

                steering.LastPosition = transform.Position;
                steeringPool.SetByIndex(i, steering);

                if (direction == velocity.Direction)
                    continue;

                velocity.Direction = direction;
                velocityPool.Set(entityId, velocity);

                // 회전은 그림 방향만 맞춘다 (위치는 System_Render가 속도로 옮긴다)
                transform.Rotation = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg;
                transformPool.Set(entityId, transform);
            }
        }

        /// <summary>
        /// 진행 방향의 축별로 앞이 막혔고 뒤는 비어 있으면 그 축을 뒤집는다. 맵 벽은 축 정렬 타일이라 축별 반사로 충분하다.
        /// 앞뒤가 모두 막힌 좁은 틈에서는 뒤집지 않는다. 뒤집어도 반대쪽 벽에서 곧바로 다시 뒤집혀 제자리에서 떨게 된다.
        /// </summary>
        private static void ReflectOffWalls(Vector2 position, float radius, float lookAhead, ref Vector2 direction, ref Vector2 heading)
        {
            Vector2 stepX = new(direction.x * lookAhead, 0f);
            Vector2 stepY = new(0f, direction.y * lookAhead);
            bool flipX = direction.x != 0f && IsWallAhead(position, stepX, radius);
            bool flipY = direction.y != 0f && IsWallAhead(position, stepY, radius);

            // 축별로는 비었지만 대각선 앞만 막힌 모서리: 두 축 모두 뒤집어 온 방향으로 돌려보낸다
            if (flipX == false && flipY == false)
            {
                Vector2 step = direction * lookAhead;
                if (IsWallAhead(position, step, radius) == false)
                    return;

                flipX = true;
                flipY = true;
            }

            if (flipX == true)
            {
                direction.x = -direction.x;
                heading.x = -heading.x;
            }

            if (flipY == true)
            {
                direction.y = -direction.y;
                heading.y = -heading.y;
            }
        }

        /// <summary>
        /// 지난 틱(벽 밖) → 지금(벽 안)으로 들어온 축을 찾아 뒤집는다. 한 축만 옮겼을 때 막히는 쪽이 들어온 축이고,
        /// 둘 다 아니면 모서리로 들어온 것이라 두 축 모두 뒤집는다. 이미 그 축으로 빠져나가는 중이면 뒤집지 않는다.
        /// </summary>
        private static void ReflectEnteredAxis(Vector2 previous, Vector2 current, float radius, ref Vector2 direction, ref Vector2 heading)
        {
            bool enteredX = CollisionUtil.IsBlockedAt(new Vector2(current.x, previous.y), radius);
            bool enteredY = CollisionUtil.IsBlockedAt(new Vector2(previous.x, current.y), radius);
            if (enteredX == false && enteredY == false)
            {
                enteredX = true;
                enteredY = true;
            }

            if (enteredX == true && direction.x * (current.x - previous.x) > 0f)
            {
                direction.x = -direction.x;
                heading.x = -heading.x;
            }

            if (enteredY == true && direction.y * (current.y - previous.y) > 0f)
            {
                direction.y = -direction.y;
                heading.y = -heading.y;
            }
        }

        private static bool IsWallAhead(Vector2 position, Vector2 step, float radius)
        {
            return CollisionUtil.IsBlockedAt(position + step, radius) == true
                && CollisionUtil.IsBlockedAt(position - step, radius) == false;
        }

        private static Vector2 Rotate(Vector2 vector, float degrees)
        {
            float radians = degrees * Mathf.Deg2Rad;
            float cos = Mathf.Cos(radians);
            float sin = Mathf.Sin(radians);
            return new Vector2(vector.x * cos - vector.y * sin, vector.x * sin + vector.y * cos);
        }
    }
}
