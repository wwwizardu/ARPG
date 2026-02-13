#nullable enable
using System.Collections.Generic;
using System.Threading;
using ARPG.Base;
using ARPG.Component;
using ARPG.Tables;
using ARPG.Utility;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.U2D.Animation;

namespace ARPG.Factory
{
    /// <summary>
    /// 기존 테이블(MonsterTable, CreatureTable 등) 기반으로
    /// EntityBase에 ECS 컴포넌트를 조합하여 엔티티를 생성하는 팩토리
    /// </summary>
    public static class EntityFactory
    {
        /// <summary>
        /// MonsterTable 기반 몬스터 엔티티 생성
        /// MonsterTable → Stat + State + Velocity + AI + Skill + Activation
        /// </summary>
        /// <param name="monster">out으로 생성된 Monster MonoBehaviour 참조 반환</param>
        public static int CreateMonster(int monsterTableId, GameObject prefab, Vector3 position, Transform? parent, out Creature.Monster? monster)
        {
            monster = null;

            // 1. 프리팹 인스턴스 생성
            GameObject obj = Object.Instantiate(prefab, position, Quaternion.identity, parent);
            monster = obj.GetComponent<Creature.Monster>();
            if (monster == null)
            {
                Debug.LogError($"[EntityFactory] Monster component not found on prefab");
                Object.Destroy(obj);
                return -1;
            }

            // 2. MonoBehaviour 초기화 (스프라이트, 메시지 핸들러)
            monster.Initialize();

            // 3. 테이블 로드
            if (monster.Load(monsterTableId) == false)
            {
                Debug.LogError($"[EntityFactory] Failed to load MonsterTable Id: {monsterTableId}");
                Object.Destroy(obj);
                monster = null;
                return -1;
            }

            // 4. EntityId 발급 + TransformComponent만 (팩토리가 나머지 담당)
            monster.SetupEntityId();
            int entityId = monster.EntityId;

            MonsterTable table = monster.MonsterTable;

            // 5. 팩토리가 ECS 컴포넌트 추가
            AddCreatureComponents(entityId, table);

            if (table.AiTableId > 0)
            {
                AddAIComponents(entityId, table.AiTableId);
            }

            if (table.AiTable != null)
            {
                AddSkillsFromAiTable(entityId, table.AiTable);
            }

            // DropComponent (DropId > 0이면 추가)
            if (table.DropId > 0)
            {
                AR.s.Component.AddComponent(entityId, new DropComponent
                {
                    DropId = table.DropId,
                    DropRateBonus = table.DropRateBonus,
                    DropRarityBonus = table.DropRarityBonus
                });
            }

            RegisterToSystems(entityId, obj, table.AnimationId);

            if (table.AnimationData != null)
            {
                LoadAnimationAsync(entityId, obj, table.AnimationData).Forget();
            }

            Debug.Log($"[EntityFactory] Monster created - EntityId: {entityId}, TableId: {monsterTableId}, Name: {table.Name}");
            return entityId;
        }

        /// <summary>
        /// CreatureTable 기반 플레이어 엔티티 생성
        /// CreatureTable → Stat + State + Velocity + Input + ChunkLoader + Skill
        /// </summary>
        /// <param name="player">out으로 생성된 ArpgPlayer MonoBehaviour 참조 반환</param>
        public static int CreatePlayer(int creatureTableId, GameObject prefab, Vector3 position, out Creature.ArpgPlayer? player)
        {
            player = null;

            // 1. 프리팹 인스턴스 생성
            GameObject obj = Object.Instantiate(prefab, position, Quaternion.identity);
            player = obj.GetComponent<Creature.ArpgPlayer>();
            if (player == null)
            {
                Debug.LogError($"[EntityFactory] ArpgPlayer component not found on prefab");
                Object.Destroy(obj);
                return -1;
            }

            // 2. MonoBehaviour 초기화
            player.Initialize();

            // 3. 테이블 로드 (내부에서 _entityId = PlayerData.PlayerId 설정)
            if (player.Load(creatureTableId) == false)
            {
                Debug.LogError($"[EntityFactory] Failed to load CreatureTable Id: {creatureTableId}");
                Object.Destroy(obj);
                player = null;
                return -1;
            }

            // 4. EntityId 등록 + TransformComponent (Load에서 이미 _entityId 설정됨)
            player.SetupEntityId();
            int entityId = player.EntityId;

            CreatureTable table = player.Table;

            // 5. 팩토리가 ECS 컴포넌트 추가
            AddCreatureComponents(entityId, table);

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

            // 플레이어 스킬 (SkillId 1)
            CreateSkill(entityId, 0, 1);

            RegisterToSystems(entityId, obj, table.AnimationId);

            if (table.AnimationData != null)
            {
                LoadAnimationAsync(entityId, obj, table.AnimationData).Forget();
            }

            Debug.Log($"[EntityFactory] Player created - EntityId: {entityId}, TableId: {creatureTableId}, Name: {table.Name}");
            return entityId;
        }

        #region 공통 컴포넌트 추가 메서드

        /// <summary>
        /// CreatureTable 기반 공통 컴포넌트 추가 (Stat + State + Velocity)
        /// </summary>
        private static void AddCreatureComponents(int entityId, CreatureTable table)
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
        }

        /// <summary>
        /// AI 컴포넌트 4종 추가
        /// </summary>
        private static void AddAIComponents(int entityId, int aiTableId)
        {
            AR.s.Component.AddComponent(entityId, new AIComponent
            {
                AITableID = aiTableId,
                TargetEntityId = 0,
                LastKnownTargetPos = Vector2.zero
            });

            AR.s.Component.AddComponent(entityId, new AIPerceptionComponent
            {
                DetectionRange = 5f,
                AttackRange = 0.8f,
                LoseTargetRange = 10f,
                FieldOfView = 360f,
                LastDetectionTime = 0f
            });

            AR.s.Component.AddComponent(entityId, new AIBehaviorTypeComponent
            {
                BehaviorType = AIBehaviorType.Melee,
                AggroRange = 10f,
                AttackRange = 1f
            });

            AR.s.Component.AddComponent(entityId, new AIStateComponent
            {
                CurrentState = AIState.Idle,
                SpawnPosition = Vector2.zero
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
                HitCount = 1,
                HitInterval = 0,
            });

            AR.s.Component.AddComponent(skillEntityId, new SkillStateComponent
            {
                State = SkillState.None,
                ElapsedTime = 0f
            });

            AR.s.Component.AddComponent(skillEntityId, new SkillTimingComponent
            {
                StartDuration = skillTable.DamageTime,
                ProcessDuration = 0.1f,
                EndDuration = skillTable.Duration - skillTable.DamageTime,
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
            // System_Render에 등록
            var renderSystem = AR.s.System.GetSystem<Systems.System_Render>();
            if (renderSystem != null)
            {
                renderSystem.RegisterGameObject(entityId, obj);
            }

            // 애니메이션이 있는 경우 SpriteAnimationComponent 추가
            if (animationId > 0)
            {
                AR.s.Component.AddComponent(entityId, new SpriteAnimationComponent
                {
                    AnimationTableId = animationId,
                    LoadState = AnimationLoadState.None,
                    PlaybackSpeed = 1f
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
                if (string.IsNullOrEmpty(animData.SpriteLibraryPath) == false)
                {
                    var spriteLibrary = obj.GetComponent<SpriteLibrary>();
                    if (spriteLibrary == null)
                    {
                        spriteLibrary = obj.AddComponent<SpriteLibrary>();
                    }

                    var slAsset = await Addressables.LoadAssetAsync<SpriteLibraryAsset>(
                        animData.SpriteLibraryPath).ToUniTask(cancellationToken: cts.Token);

                    if (slAsset != null)
                    {
                        spriteLibrary.spriteLibraryAsset = slAsset;
                    }
                }

                // 2. AnimationClip 로드
                string[] clipNames = animData.ClipNameArray;
                string clipPath = animData.AnimClipPath;

                if (clipNames == null || clipNames.Length == 0)
                {
                    Debug.LogWarning($"[EntityFactory] No animation clip names for AnimationTable Id: {animData.Id}");
                    UpdateAnimationLoadState(entityId, AnimationLoadState.Failed);
                    return;
                }

                List<AnimationClip> clips = new();

                for (int i = 0; i < clipNames.Length; i++)
                {
                    cts.Token.ThrowIfCancellationRequested();

                    string path = $"{clipPath}/{clipNames[i]}";
                    var handle = Addressables.LoadAssetAsync<AnimationClip>(path);

                    try
                    {
                        AnimationClip clip = await handle.ToUniTask(cancellationToken: cts.Token);
                        if (handle.Status == AsyncOperationStatus.Succeeded && clip != null)
                        {
                            clips.Add(clip);
                        }
                        else
                        {
                            Debug.LogWarning($"[EntityFactory] Failed to load animation clip: {path}");
                        }
                    }
                    catch (System.OperationCanceledException)
                    {
                        Addressables.Release(handle);
                        throw;
                    }
                }

                // 3. 오브젝트 파괴 확인
                if (obj == null)
                {
                    ReleaseClips(clips);
                    return;
                }

                // 4. PlayableAnimator 초기화
                PlayableAnimator playableAnimator = obj.GetComponent<PlayableAnimator>();
                if (playableAnimator == null)
                {
                    playableAnimator = obj.AddComponent<PlayableAnimator>();
                }

                playableAnimator.Initialize(clips.ToArray());

                // System_Animation에 등록
                var animSystem = AR.s.System.GetSystem<Systems.System_Animation>();
                if (animSystem != null)
                {
                    animSystem.RegisterPlayableAnimator(entityId, playableAnimator);
                }

                UpdateAnimationLoadState(entityId, AnimationLoadState.Loaded);
                Debug.Log($"[EntityFactory] Animation loaded for Entity {entityId} with {clips.Count} clips");
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

        private static void ReleaseClips(List<AnimationClip> clips)
        {
            for (int i = 0; i < clips.Count; i++)
            {
                if (clips[i] != null)
                {
                    Addressables.Release(clips[i]);
                }
            }
        }

        #endregion
    }
}
