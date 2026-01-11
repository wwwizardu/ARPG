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
        private readonly List<ILateUpdateSystem> _lateUpdateSystems = new();

        public void Initialize()
        {
            Debug.Log("SystemManager Initialized");

            // Systems 등록 (Priority 순서대로 주석)

            // Priority 0: Input System (Update) - 입력 수집
            System_Input inputSystem = new();
            RegisterSystems(inputSystem);

            // Priority 40: Buff Update System (Update) - 버프 시간 감소 및 만료
            System_BuffUpdate buffUpdateSystem = new();
            RegisterSystems(buffUpdateSystem);

            // Priority 100: Movement System (FixedUpdate) - 이동 로직
            System_Move moveSystem = new();
            RegisterSystems(moveSystem);

            // Priority 200: Skill System (FixedUpdate) - 스킬 로직
            System_Skill skillSystem = new(); 
            RegisterSystems(skillSystem);

            // Priority 500: Animation System (Update) - 애니메이션 제어
            System_Animation animationSystem = new();
            RegisterSystems(animationSystem);

            // Priority 1000: Render System (Update) - GameObject 동기화
            System_Render renderSystem = new();
            RegisterSystems(renderSystem);

            // Priority 값이 작은 순서대로 정렬
            _systems.Sort((a, b) => a.Priority.CompareTo(b.Priority));
            _updateSystems.Sort((a, b) => a.Priority.CompareTo(b.Priority));
            _fixedUpdateSystems.Sort((a, b) => a.Priority.CompareTo(b.Priority));
            _lateUpdateSystems.Sort((a, b) => a.Priority.CompareTo(b.Priority));
        }

        public void Reset()
        {
            // 모든 시스템 정리
            for (int i = 0; i < _systems.Count; i++)
            {
                _systems[i].OnReset();
            }

            _systems.Clear();
            _updateSystems.Clear();
            _fixedUpdateSystems.Clear();
            _lateUpdateSystems.Clear();

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

            // LateUpdate System 분류
            if (inSystem is ILateUpdateSystem lateUpdateSystem)
            {
                _lateUpdateSystems.Add(lateUpdateSystem);
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

            if (inSystem is ILateUpdateSystem lateUpdateSystem)
            {
                _lateUpdateSystems.Remove(lateUpdateSystem);
            }
        }

        public T? GetSystem<T>() where T : struct, ISystem
        {
            for (int i = 0; i < _systems.Count; i++)
            {
                if (_systems[i] is T system)
                {
                    return system;
                }
            }

            return null;
        }

        // System 존재 확인
        public bool HasSystem<T>() where T : struct, ISystem
        {
            for (int i = 0; i < _systems.Count; i++)
            {
                if (_systems[i] is T)
                {
                    return true;
                }
            }

            return false;
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

        // LateUpdate: Update와 FixedUpdate 이후 실행 (카메라, 렌더링 동기화)
        private void LateUpdate()
        {
            float deltaTime = Time.deltaTime;

            for (int i = 0; i < _lateUpdateSystems.Count; i++)
            {
                _lateUpdateSystems[i].OnLateUpdate(deltaTime);
            }
        }
    }
}


