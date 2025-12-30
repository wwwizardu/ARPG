#nullable enable
using System.Collections.Generic;
using UnityEngine;

namespace ARPG.Systems
{
    public class SystemManager : MonoBehaviour
    {
        private readonly List<ISystem> _systems = new();
        private readonly List<IUpdateSystem> _updateSystems = new();
        private readonly List<IFixedUpdateSystem> _fixedUpdateSystems = new();

        public void Initialize()
        {
            Debug.Log("SystemManager Initialized");

            // Systems 등록
            System_Input inputSystem = new System_Input();
            RegisterSystems(inputSystem);

            System_Move moveSystem = new System_Move();
            RegisterSystems(moveSystem);

            // Priority 값이 작은 순서대로 정렬
            _systems.Sort((a, b) => a.Priority.CompareTo(b.Priority));
            _updateSystems.Sort((a, b) => a.Priority.CompareTo(b.Priority));
            _fixedUpdateSystems.Sort((a, b) => a.Priority.CompareTo(b.Priority));
        }

        public void Reset()
        {
            // 모든 시스템 정리
            for (int i = 0; i < _systems.Count; i++)
            {
                _systems[i].Dispose();
            }

            _systems.Clear();
            _updateSystems.Clear();
            _fixedUpdateSystems.Clear();

            Debug.Log("SystemManager Reset - All systems disposed");
        }

        public void RegisterSystems(ISystem inSystem)
        {
            _systems.Add(inSystem);

            // Update System 분류
            if (inSystem is IUpdateSystem updateSystem)
            {
                _updateSystems.Add(updateSystem);
            }

            // FixedUpdate System 분류
            if (inSystem is IFixedUpdateSystem fixedUpdateSystem)
            {
                _fixedUpdateSystems.Add(fixedUpdateSystem);
            }

            inSystem.OnCreate();
        }

        public void UnRegisterSystems(ISystem inSystem)
        {
            _systems.Remove(inSystem);

            if (inSystem is IUpdateSystem updateSystem)
            {
                _updateSystems.Remove(updateSystem);
            }

            if (inSystem is IFixedUpdateSystem fixedUpdateSystem)
            {
                _fixedUpdateSystems.Remove(fixedUpdateSystem);
            }

            inSystem.Dispose();
        }

        public ISystem? GetSystem<T>() where T : ISystem
        {
            for (int i = 0; i < _systems.Count; i++)
            {
                if (_systems[i] is T)
                {
                    return _systems[i];
                }
            }

            return default;
        }

        // Update: 매 프레임마다 실행 (입력, 렌더링, UI 등)
        private void Update()
        {
            float deltaTime = Time.deltaTime;

            for (int i = 0; i < _updateSystems.Count; i++)
            {
                _updateSystems[i].OnUpdate(deltaTime);
            }
        }

        // FixedUpdate: 고정 타임스텝으로 실행 (물리, 게임플레이 로직)
        private void FixedUpdate()
        {
            float fixedDeltaTime = Time.fixedDeltaTime;

            for (int i = 0; i < _fixedUpdateSystems.Count; i++)
            {
                _fixedUpdateSystems[i].OnFixedUpdate(fixedDeltaTime);
            }
        }
    }
}


