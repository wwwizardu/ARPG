#nullable enable
using System.Threading;
using ARPG.Base;
using ARPG.Component;
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

            // 저장된 장비의 스탯 modifier 복원
            EquipHelper.ApplyAllEquipmentModifiers(entityId, AR.s.Data.Player._inventoryEquip);

            // 플레이어 스킬
            CreateSkill(entityId, 0, 1); // 슬롯 0: Strike
            CreateSkill(entityId, 1, 5); // 슬롯 1: QuickHop (Space 키로 발동)

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
                ExecutionType = SkillExecutionType.MultiHit,
                HitCount = skillTable.HitCount > 0 ? skillTable.HitCount : 1,
                HitInterval = skillTable.HitInterval,
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
