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

        // 각 시스템의 UpdateInterval 타이머 (리스트 인덱스로 시스템과 1:1 매칭)
        // Dictionary 대신 List를 사용하여 struct System의 GetHashCode() 변경에 영향받지 않음
        private readonly List<float> _updateSystemTimers = new();
        private readonly List<float> _fixedUpdateSystemTimers = new();
        private readonly List<float> _lateUpdateSystemTimers = new();

        public void Initialize()
        {
            Debug.Log("SystemManager Initialized");

            // Systems 등록 (Priority 순서대로 주석)

            // Priority 5: Time System (Update) - 게임 시간 진행
            System_Time timeSystem = new();
            RegisterSystems(timeSystem);

            // Priority 0: Input System (Update) - 입력 수집
            System_Input inputSystem = new();
            RegisterSystems(inputSystem);

            // Priority 30: AI Perception System (FixedUpdate) - AI 타겟 감지
            System_AI_Perception aiPerceptionSystem = new();
            RegisterSystems(aiPerceptionSystem);

            // Priority 40: Buff Update System (Update) - 버프 시간 감소 및 만료
            System_BuffUpdate buffUpdateSystem = new();
            RegisterSystems(buffUpdateSystem);

            // Priority 50: Stat Calculation System (Update) - StatDirtyTag 기반 스탯 재계산
            System_StatCalculation statCalculationSystem = new();
            RegisterSystems(statCalculationSystem);

            // Priority 50: AI Behavior System (FixedUpdate) - AI 상태 머신 및 행동 로직
            System_AI_Behavior aiBehaviorSystem = new();
            RegisterSystems(aiBehaviorSystem);

            // Priority 57: Village Passive Production (FixedUpdate, 5.0s) - 정수 기반 자원 생산/소비
            System_VillagePassiveProduction villagePassiveProductionSystem = new();
            RegisterSystems(villagePassiveProductionSystem);

            // Priority 58: Village First Build (FixedUpdate, 5.0s) - Phase A MVP: 첫 Campfire 제작 루프
            System_VillageFirstBuild villageFirstBuildSystem = new();
            RegisterSystems(villageFirstBuildSystem);

            // Priority 58.5: Relationship System (FixedUpdate, 3.0s) - 관계 패시브 변동
            System_Relationship relationshipSystem = new();
            RegisterSystems(relationshipSystem);

            // Priority 59: Village Respawn System (FixedUpdate, 5.0s) - 마을 기본 NPC 스폰 및 쿨타임 재스폰
            System_VillageRespawn villageRespawnSystem = new();
            RegisterSystems(villageRespawnSystem);

            // Priority 100: Movement System (FixedUpdate) - 이동 로직
            System_Move moveSystem = new();
            RegisterSystems(moveSystem);

            // Priority 120: Map Chunk Loader System (FixedUpdate) - 플레이어 위치 기반 청크 로딩/언로딩
            System_MapChunkLoader mapChunkLoaderSystem = new();
            RegisterSystems(mapChunkLoaderSystem);

            // Priority 130: Monster Spawn System (FixedUpdate) - 몬스터 최초 스폰 + 리스폰 (0.5초 간격)
            System_MonsterSpawn monsterSpawnSystem = new();
            RegisterSystems(monsterSpawnSystem);

            // Priority 150: Projectile System (FixedUpdate) - 발사체 이동 및 충돌
            System_Projectile projectileSystem = new();
            RegisterSystems(projectileSystem);

            // Priority 200: Skill System (FixedUpdate) - 스킬 로직
            System_Skill skillSystem = new();
            RegisterSystems(skillSystem);

            // Priority 220: Jump System (FixedUpdate) - 점프 궤적 갱신 및 착지 처리
            System_Jump jumpSystem = new();
            RegisterSystems(jumpSystem);

            // Priority 250: HP Check System (FixedUpdate) - HP 0 체크 및 DeathMessage 전송
            System_HpCheck hpCheckSystem = new();
            RegisterSystems(hpCheckSystem);

            // Priority 500: Entity Activation System (FixedUpdate) - 거리 기반 엔티티 활성화/비활성화 (0.5초 간격)
            System_EntityActivation entityActivationSystem = new();
            RegisterSystems(entityActivationSystem);

            // Priority 500: Animation System (Update) - 애니메이션 제어
            System_Animation animationSystem = new();
            RegisterSystems(animationSystem);

            // Priority 1000: Render System (Update) - GameObject 동기화
            System_Render renderSystem = new();
            RegisterSystems(renderSystem);

            // Priority 900: Entity Message System (LateUpdate) - 메시지 일괄 처리
            System_EntityMessage entityMessageSystem = new();
            RegisterSystems(entityMessageSystem);

            // Priority 950: Entity Destroy System (LateUpdate) - DestroyTag 엔티티 제거
            System_EntityDestroy entityDestroySystem = new();
            RegisterSystems(entityDestroySystem);

            // Priority 값이 작은 순서대로 정렬 (타이머 리스트도 함께 정렬)
            SortSystemsByPriority(_updateSystems, _updateSystemTimers);
            SortSystemsByPriority(_fixedUpdateSystems, _fixedUpdateSystemTimers);
            SortSystemsByPriority(_lateUpdateSystems, _lateUpdateSystemTimers);
            _systems.Sort((a, b) => a.Priority.CompareTo(b.Priority));
        }

        /// <summary>
        /// 시스템 리스트를 Priority 기준으로 정렬하면서 타이머 리스트도 동기화
        /// </summary>
        private void SortSystemsByPriority<T>(List<T> systems, List<float> timers) where T : ISystem
        {
            // 인덱스 배열을 만들어 정렬 후 타이머도 같은 순서로 재배치
            int count = systems.Count;
            int[] indices = new int[count];
            for (int i = 0; i < count; i++)
            {
                indices[i] = i;
            }

            // Priority 기준으로 인덱스 정렬
            System.Array.Sort(indices, (a, b) => systems[a].Priority.CompareTo(systems[b].Priority));

            // 정렬된 순서로 새 리스트 구성
            List<T> sortedSystems = new(count);
            List<float> sortedTimers = new(count);
            for (int i = 0; i < count; i++)
            {
                sortedSystems.Add(systems[indices[i]]);
                sortedTimers.Add(timers[indices[i]]);
            }

            // 원본에 반영
            systems.Clear();
            systems.AddRange(sortedSystems);
            timers.Clear();
            timers.AddRange(sortedTimers);
        }

        public void Reset()
        {
            for (int i = 0; i < _systems.Count; i++)
            {
                _systems[i].OnReset();
            }

            _systems.Clear();
            _updateSystems.Clear();
            _fixedUpdateSystems.Clear();
            _lateUpdateSystems.Clear();
            _updateSystemTimers.Clear();
            _fixedUpdateSystemTimers.Clear();
            _lateUpdateSystemTimers.Clear();

            Debug.Log("SystemManager Reset - All systems disposed");
        }

        public void RegisterSystems(ISystem inSystem)
        {
            _systems.Add(inSystem);

            // Update System 분류 (시스템 리스트와 타이머 리스트를 같은 인덱스로 관리)
            if (inSystem is IUpdateSystem updateSystem)
            {
                _updateSystems.Add(updateSystem);
                _updateSystemTimers.Add(0f);
            }

            // FixedUpdate System 분류
            if (inSystem is IFixedUpdateSystem fixedUpdateSystem)
            {
                _fixedUpdateSystems.Add(fixedUpdateSystem);
                _fixedUpdateSystemTimers.Add(0f);
            }

            // LateUpdate System 분류
            if (inSystem is ILateUpdateSystem lateUpdateSystem)
            {
                _lateUpdateSystems.Add(lateUpdateSystem);
                _lateUpdateSystemTimers.Add(0f);
            }

            inSystem.OnCreate();
        }

        public void UnRegisterSystems(ISystem inSystem)
        {
            _systems.Remove(inSystem);

            if (inSystem is IUpdateSystem updateSystem)
            {
                int index = _updateSystems.IndexOf(updateSystem);
                if (index >= 0)
                {
                    _updateSystems.RemoveAt(index);
                    _updateSystemTimers.RemoveAt(index);
                }
            }

            if (inSystem is IFixedUpdateSystem fixedUpdateSystem)
            {
                int index = _fixedUpdateSystems.IndexOf(fixedUpdateSystem);
                if (index >= 0)
                {
                    _fixedUpdateSystems.RemoveAt(index);
                    _fixedUpdateSystemTimers.RemoveAt(index);
                }
            }

            if (inSystem is ILateUpdateSystem lateUpdateSystem)
            {
                int index = _lateUpdateSystems.IndexOf(lateUpdateSystem);
                if (index >= 0)
                {
                    _lateUpdateSystems.RemoveAt(index);
                    _lateUpdateSystemTimers.RemoveAt(index);
                }
            }
        }

        public T? GetSystem<T>() where T : class, ISystem
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

        public bool HasSystem<T>() where T : class, ISystem
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

                if (updateInterval <= 0f)
                {
                    system.OnUpdate(deltaTime);
                }
                else
                {
                    _updateSystemTimers[i] += deltaTime;

                    if (_updateSystemTimers[i] >= updateInterval)
                    {
                        system.OnUpdate(_updateSystemTimers[i]);
                        _updateSystemTimers[i] = 0f;
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

                if (updateInterval <= 0f)
                {
                    system.OnFixedUpdate(fixedDeltaTime);
                }
                else
                {
                    _fixedUpdateSystemTimers[i] += fixedDeltaTime;

                    if (_fixedUpdateSystemTimers[i] >= updateInterval)
                    {
                        system.OnFixedUpdate(_fixedUpdateSystemTimers[i]);
                        _fixedUpdateSystemTimers[i] = 0f;
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

                if (updateInterval <= 0f)
                {
                    system.OnLateUpdate(deltaTime);
                }
                else
                {
                    _lateUpdateSystemTimers[i] += deltaTime;

                    if (_lateUpdateSystemTimers[i] >= updateInterval)
                    {
                        system.OnLateUpdate(_lateUpdateSystemTimers[i]);
                        _lateUpdateSystemTimers[i] = 0f;
                    }
                }
            }
        }
    }
}
