using ARPG.Component;
using UnityEngine;
using System.Collections.Generic;

namespace ARPG.Systems
{
    // RenderSystem: ECS 데이터를 GameObject에 동기화
    public struct System_Render : IUpdateSystem
    {
        public int Priority => 1000; // 가장 마지막에 실행 (다른 시스템들이 데이터 업데이트 완료 후)

        private ComponentManager _componentManager;
        private Dictionary<int, GameObject> _entityToGameObject; // EntityId -> GameObject 매핑

        public void OnCreate()
        {
            _componentManager = AR.s.Component;
            _entityToGameObject = new Dictionary<int, GameObject>();

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
            _entityToGameObject.Clear();
            Debug.Log("System_Render Reset - Entity to GameObject mapping cleared");
        }

        // GameObject 등록 (Entity 생성 시 호출)
        public void RegisterGameObject(int entityId, GameObject gameObject)
        {
            if (_entityToGameObject == null)
                _entityToGameObject = new Dictionary<int, GameObject>();

            _entityToGameObject[entityId] = gameObject;
            Debug.Log($"GameObject registered for Entity {entityId}: {gameObject.name}");
        }

        // GameObject 해제 (Entity 삭제 시 호출)
        public void UnregisterGameObject(int entityId)
        {
            if (_entityToGameObject == null)
                return;

            _entityToGameObject.Remove(entityId);
            Debug.Log($"GameObject unregistered for Entity {entityId}");
        }

        // Update: Velocity를 이용해 Position 계산 후 GameObject 동기화
        public readonly void OnUpdate(float inDeltaTime)
        {
            if (_componentManager == null || _entityToGameObject == null)
                return;

            SparseSet<TransformComponent> transformPool = _componentManager.GetComponentPool<TransformComponent>();

            // TransformComponent를 가진 모든 엔티티 순회
            for (int i = 0; i < transformPool.Count; i++)
            {
                int entityId = transformPool.GetEntityId(i);
                TransformComponent transformComponent = transformPool.GetByIndex(i);

                // EntityId에 해당하는 GameObject가 있는지 확인
                if (_entityToGameObject.TryGetValue(entityId, out GameObject gameObject) == false)
                    continue;

                if (gameObject == null)
                    continue;

                // Velocity를 이용해 Position 업데이트
                if (_componentManager.TryGetComponent<VelocityComponent>(entityId, out var velocity))
                {
                    transformComponent.Position += velocity.Velocity * inDeltaTime;

                    // 업데이트된 Position 저장
                    _componentManager.AddComponent(entityId, transformComponent);
                }

                // ECS TransformComponent -> GameObject Transform 동기화
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
            }
        }

        public bool RegisterEntity(IEntity inEntity)
        {
            return false;
        }

        public bool UnregisterEntity(IEntity inEntity)
        {
            return false;
        }

        public void Dispose()
        {
            _entityToGameObject?.Clear();
            _componentManager = null;
        }
    }
}
