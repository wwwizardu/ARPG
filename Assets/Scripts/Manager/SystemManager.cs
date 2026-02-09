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

        // 각 시스템의 마지막 업데이트 시간 추적 (UpdateInterval 사용 시)
        private readonly Dictionary<IUpdateSystem, float> _updateSystemTimers = new();
        private readonly Dictionary<IFixedUpdateSystem, float> _fixedUpdateSystemTimers = new();
        private readonly Dictionary<ILateUpdateSystem, float> _lateUpdateSystemTimers = new();

        public void Initialize()
        {
            Debug.Log("SystemManager Initialized");

            // Systems 등록 (Priority 순서대로 주석)

            // Priority 0: Input System (Update) - 입력 수집
            System_Input inputSystem = new();
            RegisterSystems(inputSystem);

            // Priority 30: AI Perception System (FixedUpdate) - AI 타겟 감지
            System_AI_Perception aiPerceptionSystem = new();
            RegisterSystems(aiPerceptionSystem);

            // Priority 40: Buff Update System (Update) - 버프 시간 감소 및 만료
            System_BuffUpdate buffUpdateSystem = new();
            RegisterSystems(buffUpdateSystem);

            // Priority 50: AI Behavior System (FixedUpdate) - AI 상태 머신 및 행동 로직
            System_AI_Behavior aiBehaviorSystem = new();
            RegisterSystems(aiBehaviorSystem);

            // Priority 100: Movement System (FixedUpdate) - 이동 로직
            System_Move moveSystem = new();
            RegisterSystems(moveSystem);

            // Priority 120: Map Chunk Loader System (FixedUpdate) - 플레이어 위치 기반 청크 로딩/언로딩
            System_MapChunkLoader mapChunkLoaderSystem = new();
            RegisterSystems(mapChunkLoaderSystem);

            // Priority 130: Monster Respawn System (FixedUpdate) - 몬스터 리스폰 (5초 간격)
            System_MonsterRespawn monsterRespawnSystem = new();
            RegisterSystems(monsterRespawnSystem);

            // Priority 200: Skill System (FixedUpdate) - 스킬 로직
            System_Skill skillSystem = new();
            RegisterSystems(skillSystem);

            // Priority 250: HP Check System (FixedUpdate) - HP 0 체크 및 DeathMessage 전송
            System_HpCheck hpCheckSystem = new();
            RegisterSystems(hpCheckSystem);

            // Priority 500: Animation System (Update) - 애니메이션 제어
            System_Animation animationSystem = new();
            RegisterSystems(animationSystem);

            // Priority 1000: Render System (Update) - GameObject 동기화
            System_Render renderSystem = new();
            RegisterSystems(renderSystem);

            // Priority 900: Entity Message System (LateUpdate) - 메시지 일괄 처리
            System_EntityMessage entityMessageSystem = new();
            RegisterSystems(entityMessageSystem);

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
                _updateSystemTimers[updateSystem] = 0f;
            }

            // FixedUpdate System 분류
            if (inSystem is IFixedUpdateSystem fixedUpdateSystem)
            {
                _fixedUpdateSystems.Add(fixedUpdateSystem);
                _fixedUpdateSystemTimers[fixedUpdateSystem] = 0f;
            }

            // LateUpdate System 분류
            if (inSystem is ILateUpdateSystem lateUpdateSystem)
            {
                _lateUpdateSystems.Add(lateUpdateSystem);
                _lateUpdateSystemTimers[lateUpdateSystem] = 0f;
            }

            inSystem.OnCreate();
        }

        public void UnRegisterSystems(ISystem inSystem)
        {
            _systems.Remove(inSystem);

            if (inSystem is IUpdateSystem updateSystem)
            {
                _updateSystems.Remove(updateSystem);
                _updateSystemTimers.Remove(updateSystem);
            }

            if (inSystem is IFixedUpdateSystem fixedUpdateSystem)
            {
                _fixedUpdateSystems.Remove(fixedUpdateSystem);
                _fixedUpdateSystemTimers.Remove(fixedUpdateSystem);
            }

            if (inSystem is ILateUpdateSystem lateUpdateSystem)
            {
                _lateUpdateSystems.Remove(lateUpdateSystem);
                _lateUpdateSystemTimers.Remove(lateUpdateSystem);
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
                IUpdateSystem system = _updateSystems[i];
                float updateInterval = system.UpdateInterval;

                // UpdateInterval이 0이면 매 프레임 실행
                if (updateInterval <= 0f)
                {
                    system.OnUpdate(deltaTime);
                }
                else
                {
                    // 타이머 누적
                    _updateSystemTimers[system] += deltaTime;

                    // 간격이 지났으면 실행
                    if (_updateSystemTimers[system] >= updateInterval)
                    {
                        system.OnUpdate(_updateSystemTimers[system]);
                        _updateSystemTimers[system] = 0f;
                    }
                }
            }
        }

        // FixedUpdate: 고정 타임스텝으로 실행 (물리, 게임플레이 로직)
        private void FixedUpdate()
        {
            float fixedDeltaTime = Time.fixedDeltaTime;

            for (int i = 0; i < _fixedUpdateSystems.Count; i++)
            {
                IFixedUpdateSystem system = _fixedUpdateSystems[i];
                float updateInterval = system.UpdateInterval;

                // UpdateInterval이 0이면 매 프레임 실행
                if (updateInterval <= 0f)
                {
                    system.OnFixedUpdate(fixedDeltaTime);
                }
                else
                {
                    // 타이머 누적
                    _fixedUpdateSystemTimers[system] += fixedDeltaTime;

                    // 간격이 지났으면 실행
                    if (_fixedUpdateSystemTimers[system] >= updateInterval)
                    {
                        system.OnFixedUpdate(_fixedUpdateSystemTimers[system]);
                        _fixedUpdateSystemTimers[system] = 0f;
                    }
                }
            }
        }

        // LateUpdate: Update와 FixedUpdate 이후 실행 (카메라, 렌더링 동기화)
        private void LateUpdate()
        {
            float deltaTime = Time.deltaTime;

            for (int i = 0; i < _lateUpdateSystems.Count; i++)
            {
                ILateUpdateSystem system = _lateUpdateSystems[i];
                float updateInterval = system.UpdateInterval;

                // UpdateInterval이 0이면 매 프레임 실행
                if (updateInterval <= 0f)
                {
                    system.OnLateUpdate(deltaTime);
                }
                else
                {
                    // 타이머 누적
                    _lateUpdateSystemTimers[system] += deltaTime;

                    // 간격이 지났으면 실행
                    if (_lateUpdateSystemTimers[system] >= updateInterval)
                    {
                        system.OnLateUpdate(_lateUpdateSystemTimers[system]);
                        _lateUpdateSystemTimers[system] = 0f;
                    }
                }
            }
        }
    }
}


