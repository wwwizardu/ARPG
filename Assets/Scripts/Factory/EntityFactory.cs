#nullable enable
using System.Threading;
using ARPG.Base;
using ARPG.Component;
using ARPG.Data;
using ARPG.Tables;
using ARPG.Utility;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.U2D.Animation;

namespace ARPG.Factory
{
    /// <summary>
    /// 기존 테이블(MonsterTable, CreatureTable 등) 기반으로
    /// EntityBase에 ECS 컴포넌트를 조합하여 엔티티를 생성하는 팩토리
    /// </summary>
    public static class EntityFactory
    {
        private const string ENTITY_PREFAB_KEY = "Prefabs/Entity";

        /// <summary>
        /// MonsterTable 기반 몬스터 엔티티 생성
        /// MonsterTable → Stat + State + Velocity + AI + Skill + Drop + MonsterTag
        /// </summary>
        /// <returns>(entityId, entity) 튜플. 실패 시 (-1, null)</returns>
        public static async UniTask<(int entityId, EntityBase? entity)> CreateMonster(int monsterTableId, Vector3 position, Transform? parent = null, int level = 0)
        {
            // 1. 테이블 로드
            MonsterTable? table = AR.s.Data.GetMonster(monsterTableId);
            if (table == null)
            {
                Debug.LogError($"[EntityFactory] MonsterTable not found for Id: {monsterTableId}");
                return (-1, null);
            }

            // 2. Addressable로 프리팹 인스턴스 생성
            Vector3 spawnPos = new Vector3(position.x, position.y, -0.01f);
            GameObject obj = await Addressables.InstantiateAsync(ENTITY_PREFAB_KEY, spawnPos, Quaternion.identity, parent).ToUniTask();
            if (obj == null)
            {
                Debug.LogError($"[EntityFactory] Failed to instantiate entity prefab");
                return (-1, null);
            }

            EntityBase? entity = obj.GetComponent<EntityBase>();
            if (entity == null)
            {
                Debug.LogError($"[EntityFactory] EntityBase not found on prefab");
                Object.Destroy(obj);
                return (-1, null);
            }

            // 3. EntityId 발급 + TransformComponent
            entity.SetupEntityId();
            int entityId = entity.EntityId;

            // 4. ECS 컴포넌트 추가
            await AddCreatureComponents(entityId, table, entity);

            // MonsterTag (System_EntityDestroy에서 MonsterManager 연동에 사용)
            AR.s.Component.AddComponent(entityId, new MonsterTag());

            // 진영 (적대) - 스킬·발사체·AI 타겟 필터에 사용
            AR.s.Component.AddComponent(entityId, new FactionComponent { FactionId = Faction.Hostile });

            if (table.AiTableId > 0)
            {
                AddAIComponents(entityId, table.AiTableId);
            }

            if (table.AiTable != null)
            {
                AddSkillsFromAiTable(entityId, table.AiTable);
            }

            if (table.DropId > 0)
            {
                AR.s.Component.AddComponent(entityId, new DropComponent
                {
                    DropId = table.DropId,
                    DropRateBonus = table.DropRateBonus,
                    DropRarityBonus = table.DropRarityBonus,
                    MonsterLevel = level > 0 ? level : table.Level
                });
            }

            RegisterToSystems(entityId, obj, table.AnimationId);

            if (table.AnimationData != null)
            {
                LoadAnimationAsync(entityId, obj, table.AnimationData).Forget();
            }

            // 자식 프리팹의 IEntityMessageHandler 자동 등록
            entity.AutoRegisterChildHandlers();

            Debug.Log($"[EntityFactory] Monster created - EntityId: {entityId}, TableId: {monsterTableId}, Name: {table.Name}");
            return (entityId, entity);
        }

        /// <summary>
        /// NpcTable 기반 NPC 엔티티 생성
        /// NpcTable → Stat + State + Velocity + AI
        /// </summary>
        /// <returns>(entityId, entity) 튜플. 실패 시 (-1, null)</returns>
        public static async UniTask<(int entityId, EntityBase? entity)> CreateNpc(int npcTableId, Vector3 position, Transform? parent = null, int savedEntityId = -1)
        {
            // 1. 테이블 로드
            NpcTable? table = AR.s.Data.GetNpc(npcTableId);
            if (table == null)
            {
                Debug.LogError($"[EntityFactory] NpcTable not found for Id: {npcTableId}");
                return (-1, null);
            }

            // 2. Addressable로 프리팹 인스턴스 생성
            Vector3 spawnPos = new Vector3(position.x, position.y, -0.01f);
            GameObject obj = await Addressables.InstantiateAsync(ENTITY_PREFAB_KEY, spawnPos, Quaternion.identity, parent).ToUniTask();
            if (obj == null)
            {
                Debug.LogError($"[EntityFactory] Failed to instantiate entity prefab for NPC");
                return (-1, null);
            }

            EntityBase? entity = obj.GetComponent<EntityBase>();
            if (entity == null)
            {
                Debug.LogError($"[EntityFactory] EntityBase not found on prefab for NPC");
                Object.Destroy(obj);
                return (-1, null);
            }

            // 3. EntityId 설정 + TransformComponent
            if (savedEntityId >= 0)
            {
                entity.SetEntityId(savedEntityId);
            }
            entity.SetupEntityId();
            int entityId = entity.EntityId;

            // 4. ECS 컴포넌트 추가
            await AddCreatureComponents(entityId, table, entity);

            // NpcTag는 AI 컴포넌트 부착보다 먼저 등록해야 AddAIComponents의 isNpc 판정이 정상 동작
            // (Patrol/PatrolRanged BehaviorType 강제 + Patrol 초기 상태)
            AR.s.Component.AddComponent(entityId, new NpcTag());

            // NPC 고유 성향 스탯 (랜덤 생성)
            AR.s.Component.AddComponent(entityId, new NpcStatComponent
            {
                Friendliness = Random.Range(0, 101),
                Honesty = Random.Range(0, 101),
                Greed = Random.Range(0, 101),
                Loyalty = Random.Range(0, 101),
                Courage = Random.Range(0, 101),
                Curiosity = Random.Range(0, 101),
                Pride = Random.Range(0, 101),
                Patience = Random.Range(0, 101)
            });

            // 마을 NPC 컴포넌트
            AR.s.Component.AddComponent(entityId, new NpcVillageComponent());
            AR.s.Component.AddComponent(entityId, new NpcJobComponent());

            if (table.AiTableId > 0)
            {
                AddAIComponents(entityId, table.AiTableId);
            }
            else
            {
                // AI 테이블이 없는 NPC: 기본 Patrol 행동 세팅
                AddDefaultNpcAIComponents(entityId, position);
            }

            if (table.AiTable != null)
            {
                AddSkillsFromAiTable(entityId, table.AiTable);
            }

            RegisterToSystems(entityId, obj, table.AnimationId);

            if (table.AnimationData != null)
            {
                LoadAnimationAsync(entityId, obj, table.AnimationData).Forget();
            }

            // 자식 프리팹의 IEntityMessageHandler 자동 등록
            entity.AutoRegisterChildHandlers();

            Debug.Log($"[EntityFactory] NPC created - EntityId: {entityId}, TableId: {npcTableId}, Name: {table.Name}");
            return (entityId, entity);
        }

        /// <summary>
        /// CreatureTable 기반 플레이어 엔티티 생성
        /// CreatureTable → Stat + State + Velocity + Input + ChunkLoader + Skill
        /// </summary>
        /// <returns>(entityId, entity) 튜플. 실패 시 (-1, null)</returns>
        public static async UniTask<(int entityId, EntityBase? entity)> CreatePlayer(int creatureTableId, Vector3 position)
        {
            // 1. 테이블 로드
            CreatureTable? table = AR.s.Data.GetPlayer(creatureTableId);
            if (table == null)
            {
                Debug.LogError($"[EntityFactory] CreatureTable not found for Id: {creatureTableId}");
                return (-1, null);
            }

            // 2. Addressable로 프리팹 인스턴스 생성
            Vector3 spawnPos = new Vector3(position.x, position.y, -0.01f);
            GameObject obj = await Addressables.InstantiateAsync(ENTITY_PREFAB_KEY, spawnPos, Quaternion.identity).ToUniTask();
            if (obj == null)
            {
                Debug.LogError($"[EntityFactory] Failed to instantiate entity prefab");
                return (-1, null);
            }

            EntityBase? entity = obj.GetComponent<EntityBase>();
            if (entity == null)
            {
                Debug.LogError($"[EntityFactory] EntityBase not found on prefab");
                Object.Destroy(obj);
                return (-1, null);
            }

            // 3. 저장된 PlayerId로 EntityId 설정
            int savedPlayerId = AR.s.Player.GetSavedPlayerId();
            if (savedPlayerId >= 0)
            {
                entity.SetEntityId(savedPlayerId);
            }

            entity.SetupEntityId();
            int entityId = entity.EntityId;

            // 4. PlayerData/인벤토리 초기화
            AR.s.Player.InitializePlayerData();

            // 5. ECS 컴포넌트 추가
            await AddCreatureComponents(entityId, table, entity);

            AR.s.Component.AddComponent(entityId, new InputComponent
            {
                MoveDirection = Vector2.zero,
                MousePosition = Vector2.zero,
                IsAttacking = false,
                IsInteracting = false,
                IsSprinting = false
            });

            AR.s.Component.AddComponent(entityId, new MapChunkLoaderComponent
            {
                CurrentChunk = new Vector2Int(-100000, -100000),
                LoadRadius = 2,
                IsInitialized = false
            });

            // 진영 (플레이어) - 스킬·발사체·AI 타겟 필터에 사용. 토템·지뢰는 이 값을 그대로 복사
            AR.s.Component.AddComponent(entityId, new FactionComponent { FactionId = Faction.Player });

            // 저장된 장비의 스탯 modifier 복원
            EquipHelper.ApplyAllEquipmentModifiers(entityId, AR.s.Data.Player._inventoryEquip);

            // 플레이어 스킬 — _skillBookSlots에 장착된 책의 SkillId로 슬롯별 스킬 엔티티 생성 (SKILLBOOK_DESIGN.md §2.6)
            ItemData?[] skillBookSlots = AR.s.Data.Player._skillBookSlots;
            for (int i = 0; i < skillBookSlots.Length; i++)
            {
                ItemData? book = skillBookSlots[i];
                if (book == null || book.SkillBook == null) continue;
                if (book.SkillBook.SkillId <= 0) continue;
                CreateSkill(entityId, i, book.SkillBook.SkillId);
            }

            RegisterToSystems(entityId, obj, table.AnimationId);

            if (table.AnimationData != null)
            {
                LoadAnimationAsync(entityId, obj, table.AnimationData).Forget();
            }

            // 자식 프리팹의 IEntityMessageHandler 자동 등록
            entity.AutoRegisterChildHandlers();

            Debug.Log($"[EntityFactory] Player created - EntityId: {entityId}, TableId: {creatureTableId}, Name: {table.Name}");
            return (entityId, entity);
        }

        #region 공통 컴포넌트 추가 메서드

        private const string UI_CANVAS_PREFAB_KEY = "Prefabs/UICanvas";
        private const string SHADOW_PREFAB_KEY = "Prefabs/Shadow";

        /// <summary>
        /// CreatureTable 기반 공통 컴포넌트 추가 (Stat + State + Velocity + HP바 프리팹 로드)
        /// </summary>
        private static async UniTask AddCreatureComponents(int entityId, CreatureTable table, EntityBase entity)
        {
            if (table.Stat == null)
                return;

            // StatComponent
            StatComponent statComponent = new();
            statComponent.InitializeFromTable(table.Stat);
            AR.s.Component.AddComponent(entityId, statComponent);

            // 첫 stat 재계산 트리거 — RegenComponent 등 파생 마커 동기화 보장
            AR.s.Component.AddComponent(entityId, new StatDirtyTag());

            // StateComponent
            AR.s.Component.AddComponent(entityId, new StateComponent
            {
                Condition = Creature.CharacterConditions.Normal,
                ConditionPrev = Creature.CharacterConditions.Normal,
                MoveState = Creature.MovementStates.Idle,
                MovementStatePrev = Creature.MovementStates.Idle
            });

            // VelocityComponent
            AR.s.Component.AddComponent(entityId, new VelocityComponent
            {
                Direction = Vector2.zero,
                Speed = 0,
                SprintMultiplier = 2f
            });

            // ColliderComponent (정적 충돌용 — 1 unit = 1 tile, 캐릭터는 0.30 반경)
            AR.s.Component.AddComponent(entityId, new ColliderComponent
            {
                Radius = 0.30f
            });

            // HP바 프리팹 로드 → _visual 아래에 자식으로 추가
            try
            {
                GameObject hpBarObj = await Addressables.InstantiateAsync(UI_CANVAS_PREFAB_KEY, entity.Visual.transform).ToUniTask();
                if (hpBarObj != null)
                {
                    hpBarObj.transform.localPosition = Vector3.zero;
                }
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"[EntityFactory] Failed to load HP bar prefab: {e.Message}");
            }

            // 그림자 프리팹 로드 → _visual 아래 자식으로 추가 (지면 고정)
            try
            {
                GameObject shadowObj = await Addressables.InstantiateAsync(SHADOW_PREFAB_KEY, entity.Visual.transform).ToUniTask();
                if (shadowObj != null)
                {
                    shadowObj.transform.localPosition = Vector3.zero;
                    entity.SetShadow(shadowObj);
                }
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"[EntityFactory] Failed to load Shadow prefab: {e.Message}");
            }
        }

        /// <summary>
        /// AI 컴포넌트 4종 추가
        /// </summary>
        private static void AddAIComponents(int entityId, int aiTableId)
        {
            Tables.AiTable? aiTable = AR.s.Data.GetAiTable(aiTableId);

            float detectionRange = aiTable != null ? aiTable.DetectionRange : 5f;
            AIBehaviorType behaviorType = aiTable != null ? aiTable.BehaviorType : AIBehaviorType.Melee;

            // AttackRange는 SkillId1의 SkillRangeMax에서 가져옴
            float attackRange = 0.8f;
            if (aiTable != null && aiTable.SkillId1 > 0)
            {
                Tables.SkillTable? skillTable = AR.s.Data.GetSkill(aiTable.SkillId1);
                if (skillTable != null)
                {
                    attackRange = skillTable.SkillRangeMax;
                }
            }

            AR.s.Component.AddComponent(entityId, new AIComponent
            {
                AITableID = aiTableId,
                TargetEntityId = -1,
                LastKnownTargetPos = Vector2.zero
            });

            AR.s.Component.AddComponent(entityId, new AIPerceptionComponent
            {
                DetectionRange = detectionRange,
                AttackRange = attackRange,
                LoseTargetRange = detectionRange * 2f,
                FieldOfView = 360f,
                LastDetectionTime = 0f
            });

            float keepDistance = (behaviorType == AIBehaviorType.Ranged) ? 7f : 1.5f;

            // NPC는 Patrol/PatrolRanged 프로필, 몬스터는 원래 BehaviorType 사용
            bool isNpc = AR.s.Component.HasComponent<NpcTag>(entityId);
            AIBehaviorType finalBehaviorType = behaviorType;
            if (isNpc)
            {
                finalBehaviorType = (behaviorType == AIBehaviorType.Ranged)
                    ? AIBehaviorType.PatrolRanged
                    : AIBehaviorType.Patrol;
            }
            AIState initialState = isNpc ? AIState.Patrol : AIState.Idle;

            AR.s.Component.AddComponent(entityId, new AIBehaviorTypeComponent
            {
                BehaviorType = finalBehaviorType,
                AggroRange = detectionRange,
                AttackRange = attackRange,
                KeepDistance = keepDistance
            });

            // TransformComponent에서 현재 위치 가져오기
            Vector2 spawnPos = Vector2.zero;
            if (AR.s.Component.TryGetComponent<TransformComponent>(entityId, out var transformComp))
            {
                spawnPos = transformComp.Position;
            }

            AR.s.Component.AddComponent(entityId, new AIStateComponent
            {
                CurrentState = initialState,
                SpawnPosition = spawnPos
            });

            // PathfindingComponent — AI 엔티티는 길찾기 사용
            AR.s.Component.AddComponent(entityId, new PathfindingComponent
            {
                Status = PathfindingStatus.None
            });
        }

        /// <summary>
        /// AI 테이블이 없는 NPC용 기본 AI 컴포넌트 추가 (Patrol 전용)
        /// </summary>
        private static void AddDefaultNpcAIComponents(int entityId, Vector3 position)
        {
            AR.s.Component.AddComponent(entityId, new AIComponent
            {
                AITableID = 0,
                TargetEntityId = -1,
                LastKnownTargetPos = Vector2.zero
            });

            AR.s.Component.AddComponent(entityId, new AIPerceptionComponent
            {
                DetectionRange = 8f,
                AttackRange = 0f,
                LoseTargetRange = 16f,
                FieldOfView = 360f,
                LastDetectionTime = 0f
            });

            AR.s.Component.AddComponent(entityId, new AIBehaviorTypeComponent
            {
                BehaviorType = AIBehaviorType.Patrol,
                AggroRange = 8f,
                AttackRange = 0f,
                KeepDistance = 0f
            });

            AR.s.Component.AddComponent(entityId, new AIStateComponent
            {
                CurrentState = AIState.Patrol,
                SpawnPosition = new Vector2(position.x, position.y)
            });

            // PathfindingComponent — AI 엔티티는 길찾기 사용
            AR.s.Component.AddComponent(entityId, new PathfindingComponent
            {
                Status = PathfindingStatus.None
            });
        }

        /// <summary>
        /// AiTable의 SkillId1~3으로 스킬 생성
        /// </summary>
        private static void AddSkillsFromAiTable(int entityId, AiTable aiTable)
        {
            int[] skillIds = { aiTable.SkillId1, aiTable.SkillId2, aiTable.SkillId3 };

            for (int i = 0; i < skillIds.Length; i++)
            {
                if (skillIds[i] <= 0)
                    continue;

                CreateSkill(entityId, i, skillIds[i]);
            }
        }

        /// <summary>
        /// 토템 엔티티 생성. caster의 StatComponent를 스냅샷 복사하고 caster 진영을 그대로 따름.
        /// 슬롯 0에 지정 스킬을 생성하며, System_Totem이 자율로 사거리 내 적에게 발사한다.
        /// LifetimeComponent로 만료되면 System_Lifetime이 DestroyTag를 부착.
        /// </summary>
        /// <param name="casterEntityId">시전자(플레이어 등) - 스탯 스냅샷 출처</param>
        /// <param name="skillId">토템이 시전할 SkillTable ID</param>
        /// <param name="position">토템 스폰 위치</param>
        /// <param name="duration">토템 생존 시간(초)</param>
        /// <returns>생성된 토템 엔티티 ID, 실패 시 -1</returns>
        public static int CreateTotem(int casterEntityId, int skillId, Vector2 position, float duration)
        {
            ComponentManager cm = AR.s.Component;

            // caster 검증
            if (cm.TryGetComponent<StatComponent>(casterEntityId, out var casterStat) == false)
            {
                Debug.LogError($"[EntityFactory] CreateTotem - caster has no StatComponent: {casterEntityId}");
                return -1;
            }
            if (cm.TryGetComponent<FactionComponent>(casterEntityId, out var casterFaction) == false)
            {
                Debug.LogError($"[EntityFactory] CreateTotem - caster has no FactionComponent: {casterEntityId}");
                return -1;
            }

            int totemId = EntityIdHelper.CreateEntity();

            // 위치/이동/충돌
            cm.AddComponent(totemId, new TransformComponent
            {
                Position = position,
                Rotation = 0f,
                Scale = Vector2.one
            });
            cm.AddComponent(totemId, new VelocityComponent
            {
                Direction = Vector2.zero,
                Speed = 0f,
                SprintMultiplier = 1f
            });
            cm.AddComponent(totemId, new ColliderComponent
            {
                Radius = 0.30f
            });

            // 상태/스탯 (caster 스냅샷)
            cm.AddComponent(totemId, casterStat);
            cm.AddComponent(totemId, new StateComponent
            {
                Condition = Creature.CharacterConditions.Normal,
                ConditionPrev = Creature.CharacterConditions.Normal,
                MoveState = Creature.MovementStates.Idle,
                MovementStatePrev = Creature.MovementStates.Idle
            });

            // 진영/링크/수명/식별
            cm.AddComponent(totemId, new FactionComponent { FactionId = casterFaction.FactionId });
            cm.AddComponent(totemId, new CasterLinkComponent { CasterEntityId = casterEntityId });
            cm.AddComponent(totemId, new LifetimeComponent { Remaining = duration });
            cm.AddComponent(totemId, new TotemTag());

            // 슬롯 0에 스킬 생성 (AI 컴포넌트보다 먼저 - AttackRange 계산이 SkillTable.SkillRangeMax를 참조)
            CreateSkill(totemId, 0, skillId);

            // AI 컴포넌트: Stationary 프로필 - StationaryAttackStateHandler가 사거리 내 적 자율 시전
            // AIComponent / AIPerceptionComponent / PathfindingComponent는 추가 안 함:
            //  - StationaryAttackStateHandler가 FactionHelper로 직접 타겟 탐색 (AI Perception 우회)
            //  - 정지 상태이므로 Pathfinding 불필요
            float skillRange = 0f;
            Tables.SkillTable? skillTable = AR.s.Data.GetSkill(skillId);
            if (skillTable != null)
                skillRange = skillTable.SkillRangeMax;

            cm.AddComponent(totemId, new AIBehaviorTypeComponent
            {
                BehaviorType = AIBehaviorType.Stationary,
                AggroRange = skillRange,
                AttackRange = skillRange,
                KeepDistance = 0f
            });
            cm.AddComponent(totemId, new AIStateComponent
            {
                CurrentState = AIState.Idle,
                SpawnPosition = position
            });

            Debug.Log($"[EntityFactory] CreateTotem - TotemId: {totemId}, Caster: {casterEntityId}, SkillId: {skillId}, Position: {position}, Duration: {duration}");
            return totemId;
        }

        /// <summary>
        /// 장판(AreaEffect) 엔티티 생성. caster의 진영을 그대로 따르며 AreaEffectTable을 참조해
        /// System_AreaEffect가 매 틱 범위 내 적에게 데미지/버프를 적용한다.
        /// 만료(LifetimeComponent)는 System_Lifetime이 DestroyTag를 부착해 정리.
        /// PrefabKey가 지정되면 AddressablePool에서 GameObject를 로드하고 System_Render에 등록.
        /// 호출자는 await할 필요 없음 (fire-and-forget) — 컴포넌트는 즉시 부착되고 GameObject만 비동기 로드됨.
        /// </summary>
        public static async void CreateAreaEffect(int casterEntityId, int areaEffectTableId, Vector2 position, int skillId = 0)
        {
            ComponentManager cm = AR.s.Component;

            AreaEffectTable? table = AR.s.Data.GetAreaEffect(areaEffectTableId);
            if (table == null)
            {
                Debug.LogError($"[EntityFactory] CreateAreaEffect - AreaEffectTable not found: {areaEffectTableId}");
                return;
            }

            Faction faction = table.TargetFaction;
            if (faction == Faction.Neutral)
            {
                if (cm.TryGetComponent<FactionComponent>(casterEntityId, out var casterFaction))
                    faction = casterFaction.FactionId;
            }

            int entityId = EntityIdHelper.CreateEntity();

            cm.AddComponent(entityId, new TransformComponent
            {
                Position = position,
                Rotation = 0f,
                Scale = Vector2.one
            });
            // FactionComponent는 의도적으로 부착하지 않음 — AI 적 탐색이 장판을 공격 대상으로 인식하지 않게.
            // 적/아군 판정은 AreaEffectComponent.CasterFaction 스냅샷으로 수행 (System_AreaEffect 참조).
            cm.AddComponent(entityId, new CasterLinkComponent { CasterEntityId = casterEntityId });
            cm.AddComponent(entityId, new LifetimeComponent { Remaining = table.Duration });
            cm.AddComponent(entityId, new AreaEffectComponent
            {
                OwnerEntityId = casterEntityId,
                AreaEffectTableId = areaEffectTableId,
                SkillId = skillId,
                CasterFaction = faction,
                Radius = table.Radius,
                TickInterval = table.TickInterval,
                NextTickIn = 0f,   // 첫 프레임에 첫 틱 발동
            });
            cm.AddComponent(entityId, new AreaEffectTag());

            Debug.Log($"[EntityFactory] CreateAreaEffect - EntityId: {entityId}, Caster: {casterEntityId}, TableId: {areaEffectTableId}, Position: {position}, Radius: {table.Radius}, Duration: {table.Duration}");

            // GameObject 시각 로드 (PrefabKey가 비어있으면 시각 없이 로직만 — 디버그/투명 장판)
            if (string.IsNullOrEmpty(table.PrefabKey))
                return;

            // 엔티티가 이미 만료되어 사라진 경우(짧은 Duration + 늦은 로드) 로드 결과를 버림
            Vector3 spawnPos = new Vector3(position.x, position.y, 0f);
            GameObject? obj = await AddressablePool.Get(table.PrefabKey, spawnPos, Quaternion.identity);
            if (obj == null)
            {
                Debug.LogWarning($"[EntityFactory] CreateAreaEffect - AddressablePool.Get returned null for key: {table.PrefabKey}");
                return;
            }

            if (cm.HasComponent<DestroyTag>(entityId) || cm.HasComponent<AreaEffectComponent>(entityId) == false)
            {
                AddressablePool.Return(table.PrefabKey, obj);
                return;
            }

            var renderSystem = AR.s.System.GetSystem<Systems.System_Render>();
            if (renderSystem != null)
            {
                var entityBase = obj.GetComponent<Base.EntityBase>();
                if (entityBase != null)
                {
                    // 장판 반경에 맞춰 Visual 스케일 (프리팹은 반경 1.0 기준 제작)
                    // 루트 transform은 System_Render가 매 프레임 덮어쓰므로 자식 Visual을 조정.
                    if (entityBase.Visual != null)
                    {
                        float visualScale = table.Radius > 0f ? table.Radius : 1f;
                        entityBase.Visual.transform.localScale = new Vector3(visualScale, visualScale, 1f);
                    }
                    else
                    {
                        Debug.LogWarning($"[EntityFactory] CreateAreaEffect - EntityBase.Visual is null on prefab '{table.PrefabKey}'. Visual scale not applied.");
                    }

                    renderSystem.RegisterEntity(entityId, entityBase);
                }
                else
                {
                    Debug.LogError($"[EntityFactory] EntityBase component not found on AreaEffect prefab '{table.PrefabKey}'. Add EntityBase to the prefab root.");
                }
            }
        }

        /// <summary>
        /// 지정 슬롯에 스킬 생성
        /// </summary>
        public static void CreateSkill(int ownerEntityId, int slotIndex, int skillId)
        {
            int skillEntityId = EntityIdHelper.CreateSkillEntity(ownerEntityId, slotIndex);

            var skillTable = AR.s.Data.GetSkill(skillId);
            if (skillTable == null)
            {
                Debug.LogError($"[EntityFactory] Skill table not found for SkillId: {skillId}");
                return;
            }

            AR.s.Component.GetComponentPool<SkillCommandComponent>();

            AR.s.Component.AddComponent(skillEntityId, new SkillComponent
            {
                SkillId = skillId,
                OwnerEntityId = ownerEntityId,
                SlotIndex = slotIndex,
                Table = skillTable,
                IsInitialized = true,
                IsEnabled = true,
                ExecutionType = skillTable.ExecutionType,
                HitCount = skillTable.HitCount > 0 ? skillTable.HitCount : 1,
                HitInterval = skillTable.HitInterval,
                ChannelingInterval = skillTable.ChannelingInterval,
                MaxChargeTime = skillTable.MaxChargeTime,
                MinChargeRatio = skillTable.MinChargeRatio,
            });

            AR.s.Component.AddComponent(skillEntityId, new SkillStateComponent
            {
                State = SkillState.None,
                ElapsedTime = 0f
            });

            AR.s.Component.AddComponent(skillEntityId, new SkillTimingComponent
            {
                BaseStartDuration = skillTable.StartTime,
                BaseProcessDuration = skillTable.ProcessTime,
                BaseEndDuration = skillTable.EndTime,
                StartDuration = skillTable.StartTime,
                ProcessDuration = skillTable.ProcessTime,
                EndDuration = skillTable.EndTime,
            });

            AR.s.Component.AddComponent(skillEntityId, new SkillTargetComponent());
        }

        /// <summary>
        /// 지정 슬롯의 스킬 엔티티 제거 (SKILLBOOK_DESIGN.md §2.6).
        /// 결정적 ID로 스킬 엔티티를 찾아 ECS 컴포넌트 일괄 제거 + 슬롯 해제.
        /// 슬롯이 비어 있으면 (등록되지 않은 ID) 조용히 무시.
        /// </summary>
        public static void RemoveSkill(int ownerEntityId, int slotIndex)
        {
            int skillEntityId = EntityIdHelper.GetDeterministicId(ownerEntityId, EntityIdCategory.Skill, slotIndex);
            if (skillEntityId == -1)
            {
                Debug.LogWarning($"[EntityFactory] RemoveSkill - Invalid skill entity ID. Owner: {ownerEntityId}, Slot: {slotIndex}");
                return;
            }

            // 등록되지 않은 슬롯(스킬 미장착)은 정상 케이스 — 경고 없이 무시
            if (EntityIdHelper.IsEntityRegistered(skillEntityId) == false)
            {
                return;
            }

            EntityIdHelper.DestroySkillEntity(skillEntityId);
        }

        /// <summary>
        /// ActivationDistanceComponent 추가
        /// </summary>
        public static void AddActivationComponent(int entityId, float activationDistance, float deactivationDistance)
        {
            float activationSqr = activationDistance * activationDistance;
            float deactivationSqr = deactivationDistance * deactivationDistance;

            AR.s.Component.AddComponent(entityId, new ActivationDistanceComponent
            {
                ActivationDistanceSqr = activationSqr,
                DeactivationDistanceSqr = deactivationSqr,
                IsActivated = false
            });
        }

        #endregion

        #region System 등록 및 애니메이션

        private static void RegisterToSystems(int entityId, GameObject obj, int animationId)
        {
            // System_Render에 등록 (EntityBase 기반)
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
                    Debug.LogError($"[EntityFactory] EntityBase component not found on {obj.name}. Add EntityBase to the prefab.");
                }
            }

            // 애니메이션이 있는 경우 SpriteAnimationComponent 추가
            if (animationId > 0)
            {
                AR.s.Component.AddComponent(entityId, new SpriteAnimationComponent
                {
                    AnimationTableId = animationId,
                    LoadState = AnimationLoadState.None,
                    PlaybackSpeed = 1f,
                    CurrentCategory = GlobalEnum.AnimCategory.Idle,
                    CurrentFrame = 0,
                    FrameTimer = 0f,
                    FrameDuration = 0.1f,
                    IsLooping = true,
                    IsPlaying = true
                });

                AR.s.Component.AddComponent(entityId, new AnimatorComponent());
            }
        }

        private static async UniTaskVoid LoadAnimationAsync(int entityId, GameObject obj, AnimationTable animData)
        {
            if (AR.s.Component.TryGetComponent<SpriteAnimationComponent>(entityId, out var spriteAnim))
            {
                spriteAnim.LoadState = AnimationLoadState.Loading;
                AR.s.Component.SetComponent(entityId, spriteAnim);
            }

            CancellationTokenSource cts = new();

            try
            {
                // 1. SpriteLibraryAsset 로드
                if (string.IsNullOrEmpty(animData.SpriteLibraryPath) == true)
                {
                    Debug.LogWarning($"[EntityFactory] No SpriteLibraryPath for AnimationTable Id: {animData.Id}");
                    UpdateAnimationLoadState(entityId, AnimationLoadState.Failed);
                    return;
                }

                var slAsset = await Addressables.LoadAssetAsync<SpriteLibraryAsset>(
                    animData.SpriteLibraryPath).ToUniTask(cancellationToken: cts.Token);

                if (slAsset == null)
                {
                    Debug.LogWarning($"[EntityFactory] Failed to load SpriteLibraryAsset: {animData.SpriteLibraryPath}");
                    UpdateAnimationLoadState(entityId, AnimationLoadState.Failed);
                    return;
                }

                // 2. EntityBase 조회 (SpriteAnimationData가 slAsset을 직접 사용하므로 컴포넌트 세팅 불필요)
                EntityBase? entity = obj.GetComponent<EntityBase>();

                // 3. 오브젝트 파괴 확인
                if (obj == null)
                {
                    return;
                }

                // 4. SpriteAnimationData 생성 및 System_Animation에 등록
                SpriteRenderer? sr = entity != null ? entity.SpriteRenderer : obj.GetComponentInChildren<SpriteRenderer>();
                if (sr == null)
                {
                    Debug.LogWarning($"[EntityFactory] SpriteRenderer not found for Entity {entityId}");
                    UpdateAnimationLoadState(entityId, AnimationLoadState.Failed);
                    return;
                }

                SpriteAnimationData animDataCache = new SpriteAnimationData(sr, slAsset);

                // AnimationTable에서 기본 FrameDuration 배열 생성
                float[] defaultFrameDurations = new float[]
                {
                    animData.IdleFrame,    // AnimCategory.Idle = 0
                    animData.MoveFrame,    // AnimCategory.Move = 1
                    animData.AttackFrame,  // AnimCategory.Attack = 2
                    animData.DeadFrame,    // AnimCategory.Dead = 3
                };

                var animSystem = AR.s.System.GetSystem<Systems.System_Animation>();
                if (animSystem != null)
                {
                    animSystem.RegisterSpriteAnimation(entityId, animDataCache, defaultFrameDurations);
                }

                UpdateAnimationLoadState(entityId, AnimationLoadState.Loaded);
                Debug.Log($"[EntityFactory] SpriteAnimation loaded for Entity {entityId}");
            }
            catch (System.OperationCanceledException)
            {
                Debug.Log($"[EntityFactory] Animation loading cancelled for Entity {entityId}");
            }
            catch (System.Exception e)
            {
                UpdateAnimationLoadState(entityId, AnimationLoadState.Failed);
                Debug.LogError($"[EntityFactory] Failed to load animation for Entity {entityId}: {e.Message}");
            }
            finally
            {
                cts.Dispose();
            }
        }

        private static void UpdateAnimationLoadState(int entityId, AnimationLoadState state)
        {
            if (AR.s.Component.TryGetComponent<SpriteAnimationComponent>(entityId, out var comp))
            {
                comp.LoadState = state;
                AR.s.Component.SetComponent(entityId, comp);
            }
        }

        #endregion
    }
}
