using ARPG.Base;
using ARPG.Component;
using ARPG.Utility;
using UnityEngine;
using System.Collections.Generic;

namespace ARPG.Systems
{
    // RenderSystem: ECS 데이터를 GameObject에 동기화
    public class System_Render : IUpdateSystem
    {
        public int Priority => 1000; // 가장 마지막에 실행 (다른 시스템들이 데이터 업데이트 완료 후)

        private ComponentManager _componentManager;
        private Dictionary<int, EntityBase> _entityToBase; // EntityId -> EntityBase 매핑 (gameObject, _sr, _shadow 모두 접근 가능)

        public void OnCreate()
        {
            _componentManager = AR.s.Component;
            _entityToBase = new Dictionary<int, EntityBase>();

            Debug.Log("System_Render Created");
        }

        public void OnReset()
        {
            Debug.Log("System_Render Reset called");
        }

        public void Initialize()
        {

        }

        public void Reset()
        {
            _entityToBase.Clear();
            Debug.Log("System_Render Reset - Entity to EntityBase mapping cleared");
        }

        // EntityBase 등록 (Entity 생성 시 호출)
        public void RegisterEntity(int entityId, EntityBase entityBase)
        {
            if (_entityToBase == null)
                _entityToBase = new Dictionary<int, EntityBase>();

            if (entityBase == null)
            {
                Debug.LogWarning($"[System_Render] RegisterEntity - EntityBase is null for EntityId: {entityId}");
                return;
            }

            _entityToBase[entityId] = entityBase;
            Debug.Log($"Entity registered for {entityId}: {entityBase.gameObject.name}");
        }

        public EntityBase GetEntity(int entityId)
        {
            if (_entityToBase == null)
                return null;

            _entityToBase.TryGetValue(entityId, out EntityBase entityBase);
            return entityBase;
        }

        public GameObject GetGameObject(int entityId)
        {
            EntityBase entityBase = GetEntity(entityId);
            return entityBase != null ? entityBase.gameObject : null;
        }

        // EntityBase 해제 (Entity 삭제 시 호출)
        public void UnregisterEntity(int entityId)
        {
            if (_entityToBase == null)
                return;

            _entityToBase.Remove(entityId);
            Debug.Log($"Entity unregistered for {entityId}");
        }

        // Update: Velocity를 이용해 Position 계산 후 GameObject 동기화
        public void OnUpdate(float inDeltaTime)
        {
            if (_componentManager == null || _entityToBase == null)
                return;

            SparseSet<TransformComponent> transformPool = _componentManager.GetComponentPool<TransformComponent>();

            // TransformComponent를 가진 모든 엔티티 순회
            for (int i = 0; i < transformPool.Count; i++)
            {
                int entityId = transformPool.GetEntityId(i);
                TransformComponent transformComponent = transformPool.GetByIndex(i);

                // EntityId에 해당하는 EntityBase가 있는지 확인
                if (_entityToBase.TryGetValue(entityId, out EntityBase entityBase) == false)
                    continue;

                if (entityBase == null)
                    continue;

                GameObject gameObject = entityBase.gameObject;

                // Velocity를 이용해 Position 업데이트
                if (_componentManager.TryGetComponent<VelocityComponent>(entityId, out var velocity))
                {
                    if (0 < velocity.Speed)
                    {
                        Vector2 intendedDelta = velocity.Velocity * inDeltaTime;

                        // ColliderComponent가 있으면 축 분리 슬라이딩, 없으면 통과
                        if (_componentManager.TryGetComponent<ColliderComponent>(entityId, out var collider))
                        {
                            transformComponent.Position = CollisionUtil.ResolveAxisSeparated(
                                transformComponent.Position, intendedDelta, collider.Radius);
                        }
                        else
                        {
                            transformComponent.Position += intendedDelta;
                        }

                        // 업데이트된 Position 저장
                        _componentManager.AddComponent(entityId, transformComponent);
                    }
                }

                // 루트 GameObject는 지면 위치 (판정 기준 그대로)
                gameObject.transform.position = new Vector3(
                    transformComponent.Position.x,
                    transformComponent.Position.y,
                    gameObject.transform.position.z // Z축은 유지
                );

                gameObject.transform.rotation = Quaternion.Euler(0f, 0f, transformComponent.Rotation);

                gameObject.transform.localScale = new Vector3(
                    transformComponent.Scale.x,
                    transformComponent.Scale.y,
                    1f
                );

                // 점프 중이면 스프라이트만 위로 띄움 (그림자는 지면에 그대로)
                ApplyJumpHeight(entityId, entityBase);
            }
        }

        /// <summary>
        /// 점프 높이를 스프라이트에 적용. 그림자는 지면에 고정되므로 건드리지 않음.
        /// 그림자 스케일은 높이에 따라 축소 (깊이감)
        /// </summary>
        private void ApplyJumpHeight(int entityId, EntityBase entityBase)
        {
            if (entityBase.SpriteRenderer == null)
                return;

            Transform spriteTransform = entityBase.SpriteRenderer.transform;

            if (_componentManager.TryGetComponent<JumpComponent>(entityId, out var jump))
            {
                // 스프라이트만 위로 오프셋
                Vector3 localPos = spriteTransform.localPosition;
                localPos.y = jump.Height;
                spriteTransform.localPosition = localPos;

                // 그림자 스케일 축소 (Height=0 → 1.0배, Height=MaxHeight → 0.7배)
                if (entityBase.Shadow != null && jump.MaxHeight > 0f)
                {
                    float heightRatio = jump.Height / jump.MaxHeight;
                    float shadowScale = 1f - heightRatio * 0.3f;
                    entityBase.Shadow.transform.localScale = new Vector3(shadowScale, shadowScale, 1f);
                }
            }
            else
            {
                // 점프 중이 아니면 스프라이트 Y는 0
                if (spriteTransform.localPosition.y != 0f)
                {
                    Vector3 localPos = spriteTransform.localPosition;
                    localPos.y = 0f;
                    spriteTransform.localPosition = localPos;
                }

                // 그림자 스케일 원복
                if (entityBase.Shadow != null)
                {
                    Vector3 scale = entityBase.Shadow.transform.localScale;
                    if (scale.x != 1f || scale.y != 1f)
                    {
                        entityBase.Shadow.transform.localScale = Vector3.one;
                    }
                }
            }
        }

        public void Dispose()
        {
            _entityToBase?.Clear();
            _componentManager = null;
        }
    }
}
