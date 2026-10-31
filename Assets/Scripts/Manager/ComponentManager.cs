using System;
using System.Collections.Generic;
using ARPG.Utility;
using UnityEngine;

namespace ARPG.Component
{
    public partial class ComponentManager : MonoBehaviour
    {
        private readonly Dictionary<Type, IComponentPool> _componentPools = new();

        /// <summary>
        /// <see cref="_componentPools"/>와 같은 풀들을 for 순회용으로 보관. Reset()에서 전체 풀을 비울 때 사용.
        /// </summary>
        private readonly List<IComponentPool> _poolList = new();

        /// <summary>
        /// Entity별로 등록된 컴포넌트 타입들을 추적
        /// Key: EntityId, Value: 해당 Entity에 등록된 컴포넌트 타입들
        /// </summary>
        private Dictionary<int, HashSet<Type>> _entityComponents = new Dictionary<int, HashSet<Type>>();

        /// <summary>
        /// Reset() 호출 시 모든 PoolCache&lt;T&gt;.Pool을 무효화하기 위한 콜백 목록.
        /// PoolCache&lt;T&gt;의 static ctor에서 한 번 등록됨.
        /// </summary>
        private static readonly List<Action> _cacheInvalidators = new();

        /// <summary>
        /// 제네릭 타입별로 SparseSet&lt;T&gt;를 정적 캐싱.
        /// Dictionary&lt;Type, IComponentPool&gt; 조회를 타입당 1회로 줄여 GetPool 비용을 거의 0에 가깝게 만듦.
        /// ComponentManager가 단일 인스턴스라는 가정에 의존 (GlobalManager.Instance.ComponentManager).
        /// </summary>
        private static class PoolCache<T> where T : struct
        {
            public static SparseSet<T> Pool;

            static PoolCache()
            {
                _cacheInvalidators.Add(() => Pool = null);
            }
        }

        /// <summary>
        /// 컴포넌트 타입별 SparseSet 초기 용량 설정
        /// </summary>
        private readonly Dictionary<Type, int> _poolSizes = new Dictionary<Type, int>
        {
            // 많이 사용되는 컴포넌트 (캐릭터/엔티티마다 1개씩)
            { typeof(TransformComponent), 1000 },
            { typeof(VelocityComponent), 500 },
            { typeof(StateComponent), 300 },
            { typeof(StatComponent), 300 },
            { typeof(DamageDefenseCacheComponent), 300 }, // StatComponent와 1:1 — 재계산 시 항상 갱신
            { typeof(ColliderComponent), 500 },

            // HP/MP 재생 누적기 (FinalHpGeneration > 0 또는 FinalMpGeneration > 0인 엔티티만)
            { typeof(RegenComponent), 50 },

            // 점프 컴포넌트 (점프 중인 엔티티만)
            { typeof(JumpComponent), 50 },

            // 넉백·당기기 중인 엔티티 (장판 하나가 여러 대상을 한 번에 밀 수 있음)
            { typeof(ForcedMoveComponent), 128 },

            // 돌진 중인 엔티티와 넉백 저항 스킬 실행 중인 시전자 (SKILL_DESIGN §6.10·§6.11)
            { typeof(DashComponent), 32 },
            { typeof(ForcedMoveImmuneTag), 32 },

            // 보통 사용되는 컴포넌트
            { typeof(InputComponent), 100 },
            { typeof(QuickItemCooldownComponent), 8 },
            { typeof(SkillAimIndicatorComponent), 8 },
            { typeof(SkillMovementPenaltyComponent), 20 },
            { typeof(SkillChannelEffectComponent), 32 },
            { typeof(TotemTag), 32 },
            { typeof(MoveSlowComponent), 64 },        // 둔화 중인 엔티티 (장판 위 몬스터·플레이어)
            { typeof(MoveSlowBuffComponent), 64 },    // 둔화(Slow) 버프 엔티티
            { typeof(AttackTelegraphComponent), 128 },

            // AI 컴포넌트 (NPC/몬스터용)
            { typeof(AIComponent), 500 },
            { typeof(AIPerceptionComponent), 500 },
            { typeof(AIBehaviorTypeComponent), 500 },
            { typeof(AIStateComponent), 500 },
            { typeof(AICanSeeTargetTag), 500 },
            { typeof(PathfindingComponent), 500 },
            { typeof(MonsterArchetypeComponent), 256 }, // 모든 몬스터에 1개 (필드 최대 ~150 + 던전)
            { typeof(ExpRewardComponent), 256 },        // 모든 몬스터에 1개 (PLAYER_LEVEL_CHARM_DESIGN §3.2)
            { typeof(NamedThreatComponent), 4 },        // 네임드 위협 엔티티 (마을당 1마리, 플레이어 근처만 실체화 — NAMED_THREAT §12.1)
            { typeof(MonsterAffixComponent), 256 },     // 접사가 있는 몬스터 (필드 몬스터의 약 27%, 등급 컴포넌트와 같은 상한)
            { typeof(SpawnHpRefillTag), 64 },           // 접사 몬스터 스폰 직후 첫 재계산까지만 존재
            { typeof(SummonedMonsterComponent), 32 },   // 소환된 몬스터 (주술사 1마리당 최대 2, 12초 일시 존재 — SKILL_DESIGN §6.12)

            // 적게 사용되는 컴포넌트 (스킬 엔티티용)
            { typeof(SkillComponent), 50 },
            { typeof(SkillStateComponent), 50 },
            { typeof(SkillTimingComponent), 50 },
            { typeof(SkillTargetComponent), 50 },
            { typeof(SkillAttackProfileComponent), 128 }, // 스킬 캐시 + 설치된 단발 장판의 공격 스냅샷
            { typeof(SkillAttackProfileDirtyTag), 50 },  // 재계산 요청 태그 (처리 후 즉시 제거)
            { typeof(SkillLandingCommitComponent), 32 }, // 점프 착지 후 다음 FixedUpdate까지 유지되는 일회성 신호
            { typeof(SkillDashHitComponent), 32 },       // 돌진 중인 스킬 엔티티의 경로 판정 상태
            { typeof(SkillPropagationComponent), 32 },
            { typeof(SkillPropagationBranchComponent), 256 },
            { typeof(SkillPropagationRequestComponent), 128 },
            { typeof(InstantSkillDeliveryComponent), 128 },
            { typeof(SkillPropagationWorkCompletedTag), 384 },

            // 매우 적게 사용되는 컴포넌트 (커맨드용, 일시적)
            { typeof(SkillCommandComponent), 20 },

            // 월드 아이템 컴포넌트
            { typeof(WorldItemComponent), 500 },              // 월드에 떨어진 아이템

            // 애니메이션 컴포넌트
            { typeof(SpriteAnimationComponent), 300 },        // 스프라이트 애니메이션 설정 및 로딩 상태
            { typeof(AnimatorComponent), 300 },               // 애니메이션 재생 요청

            // 맵 컴포넌트
            { typeof(MapChunkLoaderComponent), 4 },           // 청크 로더 (플레이어 전용)
            { typeof(DungeonObjectComponent), 64 },
            { typeof(DungeonChestTag), 32 },                  // 활성 던전 청크의 미개봉 상자
            { typeof(DungeonEntranceComponent), 32 },         // 활성 월드 청크의 던전 입구 (마을당 2~3개)
            { typeof(DungeonEntranceSignComponent), 32 },     // 소문 징후 자막이 있는 입구만
            { typeof(OreVeinComponent), 32 },                 // 활성 월드 청크의 광맥 (청크당 최대 1개, 활성 5×5 — WORLD_ORE_VEIN_PLAN §8.2)
            { typeof(MiningComponent), 4 },                   // 채광 중인 플레이어 (§7.1)
            { typeof(KillCreditComponent), 128 },
            // 시야·안개 (FOG_OF_WAR_DESIGN §5)
            { typeof(VisionComponent), 4 },                   // 시야를 주는 엔티티 (플레이어 전용)
            { typeof(FogVisibilityComponent), 512 },          // 시야 밖에서 숨길 이동 엔티티: 몬스터(최대 ~150) + NPC + 적 투사체

            // 엔티티 라이프사이클 컴포넌트
            { typeof(DestroyTag), 100 },                       // 제거 예약 태그
            { typeof(ActivationDistanceComponent), 500 },      // 거리 기반 활성화

            // 마을 시스템 컴포넌트
            { typeof(NpcVillageComponent), 200 },
            { typeof(NpcActivityPausedTag), 200 }, // 이동 중 materialize된 NPC의 조우 대기 태그
            { typeof(NpcCombatEnabledTag), 8 },    // 전투 허용 NPC (동행·결투 중, 동시에 소수)
            { typeof(NpcCompanionComponent), 8 },  // 동행 추종 NPC (v1은 1명)
            { typeof(DuelComponent), 4 },          // 결투 참가자 (플레이어↔NPC 1건 = 2개)
            { typeof(NpcPickupComponent), 8 },     // 드랍 줍기 중인 NPC (디버그·C1 배신 규칙 호출, 동시에 소수)
            { typeof(NpcVillageWalkComponent), 64 }, // 마을 안을 걷는 NPC (원정 출발·귀환·여관 가기, 화면 근처 마을 NPC 수 이하)
            { typeof(VillageStorageComponent), 32 },   // 마을 1개당 1개, 현재 <10, 여유 32
            { typeof(VillageComponent), 32 },          // 마을 1개당 1개 (Phase C: Stage/Bounds/Threat)
            { typeof(WallPlanRequestTag), 32 },        // Stage 3 도달 시 부착, 처리 후 제거
            { typeof(ObjectPlacementTaskComponent), 32 }, // 마을 1개당 0~1개 (Phase B 배치 큐)

            // Phase D 마을 시스템 컴포넌트
            { typeof(PlacedObjectComponent), 1024 },   // 마을당 수십~수백 오브젝트 × 5~10마을
            { typeof(PlayerNearbyServicesComponent), 8 }, // 플레이어 1명당 1개 (멀티 대비 8)

            // 건물 시스템 컴포넌트 (BuildingManager 관리)
            { typeof(BuildingTag), 200 },
            { typeof(BuildingComponent), 200 },

            // 발사체 컴포넌트
            { typeof(ProjectileComponent), 100 },
            { typeof(ProjectileTag), 100 },
            { typeof(ProjectileArcComponent), 32 },   // 목표 지점에 내려앉아 장판을 까는 포물선 발사체

            // 장판(지속 영역 효과) 컴포넌트
            { typeof(AreaEffectComponent), 50 },
            { typeof(AreaEffectTag), 50 },

            // 장비 Mod 풀 컴포넌트
            { typeof(ModPoolComponent), 10 },

            // 유적 단편 (Lore Fragment) 시스템
            { typeof(LoreFragmentComponent), 512 },          // region당 0~3 fragment × 활성 region 수

            // Live Backbone(Phase 7) 예약 — 현재 미사용. 도입 시 부활.
            { typeof(HistoryActorComponent), 64 },           // 종족 + 신 + 괴물 카탈로그 인스턴스
            { typeof(HistoryActorTagsComponent), 64 },       // HistoryActorComponent와 1:1
            { typeof(HistoricalEventComponent), 256 },       // Live 사건 append
            { typeof(HistoryMemoryComponent), 500 },         // NPC와 1:1 (최근 사건 캐시)
        };

        /// <summary>
        /// 기본 풀 크기 (설정되지 않은 컴포넌트 타입)
        /// </summary>
        private const int DEFAULT_POOL_SIZE = 300;

        public void Initialize()
        {
            Debug.Log("ComponentManager Initialized");
        }

        public void Reset()
        {
            // 풀 인스턴스는 유지하고 내용만 비운다. 시스템이 OnCreate에서 캐시한 SparseSet 참조가
            // Reset 이후에도 유효해야 하기 때문 — 풀을 교체하면 캐시한 시스템들이 조용히 죽은 풀을 읽는다.
            // 확보된 용량(_poolSizes 기반)도 함께 보존된다.
            for (int i = 0; i < _poolList.Count; i++)
            {
                _poolList[i].Clear();
            }

            _entityComponents.Clear();

            // PoolCache<T>.Pool 무효화. 풀 인스턴스가 유지되므로 필수는 아니지만,
            // static 캐시를 인스턴스 상태와 어긋나지 않게 유지하는 방어 단계로 남긴다.
            for (int i = 0; i < _cacheInvalidators.Count; i++)
            {
                _cacheInvalidators[i].Invoke();
            }

            Debug.Log($"ComponentManager Reset - {_poolList.Count} component pools cleared");
        }

        // 컴포넌트 추가/업데이트 (존재하면 업데이트, 없으면 추가)
        public void AddComponent<T>(int entityId, T component) where T : struct
        {
            SparseSet<T> pool = GetOrCreatePool<T>();
            pool.Add(entityId, component);

            // Entity에 등록된 컴포넌트 타입 추적
            TrackComponentType<T>(entityId);
        }

        // 컴포넌트 설정 (존재하면 업데이트, 없으면 추가)
        public void SetComponent<T>(int entityId, T component) where T : struct
        {
            SparseSet<T> pool = GetOrCreatePool<T>();
            pool.Set(entityId, component);

            // Entity에 등록된 컴포넌트 타입 추적
            TrackComponentType<T>(entityId);
        }

        // 컴포넌트 조회 시도 (Unity 패턴, 가장 효율적)
        public bool TryGetComponent<T>(int entityId, out T component) where T : struct
        {
            SparseSet<T> pool = PoolCache<T>.Pool;
            if (pool == null)
            {
                pool = GetPool<T>();
                if (pool == null)
                {
                    component = default;
                    return false;
                }
            }

            // SparseSet.TryGet: Contains + Get을 통합하여 sparse Dictionary 조회를 1회로 줄임
            return pool.TryGet(entityId, out component);
        }

        // 컴포넌트 제거
        public void RemoveComponent<T>(int entityId) where T : struct
        {
            SparseSet<T> pool = GetPool<T>();
            pool?.Remove(entityId);

            // Entity 추적 정보에서 컴포넌트 타입 제거
            Type type = typeof(T);
            if (_entityComponents.TryGetValue(entityId, out HashSet<Type> componentTypes))
            {
                componentTypes.Remove(type);

                // 컴포넌트가 모두 제거되면 Entity 정보도 제거
                if (componentTypes.Count == 0)
                {
                    _entityComponents.Remove(entityId);
                }
            }
        }

        /// <summary>
        /// Entity에 등록된 모든 컴포넌트를 제거합니다.
        /// IComponentPool 인터페이스를 통해 타입 안전하게 제거 (Reflection 불필요)
        /// </summary>
        /// <param name="entityId">제거할 Entity ID</param>
        public void RemoveAllComponents(int entityId)
        {
            if (!_entityComponents.TryGetValue(entityId, out HashSet<Type> componentTypes))
            {
                // 해당 Entity에 등록된 컴포넌트가 없음
                return;
            }

            // 등록된 모든 컴포넌트 타입을 순회하며 제거
            // ToArray()로 복사본 생성 (순회 중 컬렉션 수정 방지)
            Type[] typesToRemove = new Type[componentTypes.Count];
            componentTypes.CopyTo(typesToRemove);

            int removedCount = 0;
            foreach (Type componentType in typesToRemove)
            {
                if (_componentPools.TryGetValue(componentType, out IComponentPool pool))
                {
                    // IComponentPool 인터페이스를 통해 직접 Remove 호출
                    pool.Remove(entityId);
                    removedCount++;
                }
            }

            // Entity 추적 정보 제거
            _entityComponents.Remove(entityId);

            Debug.Log($"[ComponentManager] Removed all components for Entity {entityId} ({removedCount}/{typesToRemove.Length} components)");
        }

        // 컴포넌트 존재 확인
        public bool HasComponent<T>(int entityId) where T : struct
        {
            SparseSet<T> pool = PoolCache<T>.Pool;
            if (pool == null)
            {
                pool = GetPool<T>();
                if (pool == null)
                {
                    return false;
                }
            }
            return pool.Contains(entityId);
        }

        // System이 특정 타입의 모든 컴포넌트에 접근할 때 사용
        public SparseSet<T> GetComponentPool<T>() where T : struct
        {
            SparseSet<T> pool = PoolCache<T>.Pool;
            if (pool == null)
            {
                pool = GetOrCreatePool<T>();
            }
            return pool;
        }

        private SparseSet<T> GetOrCreatePool<T>() where T : struct
        {
            Type type = typeof(T);
            if (_componentPools.TryGetValue(type, out IComponentPool existing))
            {
                SparseSet<T> cached = existing as SparseSet<T>;
                PoolCache<T>.Pool = cached;
                return cached;
            }

            // 타입별로 설정된 용량 사용, 없으면 기본값
            int capacity = _poolSizes.TryGetValue(type, out int size) ? size : DEFAULT_POOL_SIZE;
            SparseSet<T> newPool = new SparseSet<T>(capacity);
            _componentPools[type] = newPool;
            _poolList.Add(newPool);
            PoolCache<T>.Pool = newPool;

            Debug.Log($"[ComponentManager] Created component pool for {type.Name} with capacity {capacity}");
            return newPool;
        }

        private SparseSet<T> GetPool<T>() where T : struct
        {
            // ContainsKey + 인덱서 → TryGetValue 단일 호출로 Dictionary FindEntry 절반 감소
            if (_componentPools.TryGetValue(typeof(T), out IComponentPool pool))
            {
                SparseSet<T> typed = pool as SparseSet<T>;
                PoolCache<T>.Pool = typed; // 첫 접근 시 캐싱
                return typed;
            }
            return null;
        }

        /// <summary>
        /// Entity에 컴포넌트 타입을 추적 정보에 추가
        /// </summary>
        private void TrackComponentType<T>(int entityId) where T : struct
        {
            Type type = typeof(T);

            if (!_entityComponents.TryGetValue(entityId, out HashSet<Type> componentTypes))
            {
                componentTypes = new HashSet<Type>();
                _entityComponents[entityId] = componentTypes;
            }

            componentTypes.Add(type);
        }

    }
}

