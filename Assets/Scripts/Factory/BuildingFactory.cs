#nullable enable
using System.Threading;
using ARPG.Base;
using ARPG.Component;
using ARPG.Tables;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.AddressableAssets;

namespace ARPG.Factory
{
    /// <summary>
    /// BuildableItemTable.SpawnType == Entity 용 경량 엔티티 팩토리.
    /// AnimationId == 0 → 정적 Sprite 로드, > 0 → SpriteAnimationComponent 등록 (System_Animation 파이프 진입).
    /// EntityFactory와 동일한 Prefabs/Entity를 사용하되, HP바/그림자/AI/Skill은 부착하지 않음.
    /// </summary>
    public static class BuildingFactory
    {
        // EntityFactory와 동일한 범용 프리팹 사용 (SpriteRenderer 1개 구성).
        // 애니메이션은 런타임에 _sr.sprite를 SpriteAnimationData가 직접 교체하므로 SpriteLibrary/Resolver 컴포넌트는 필요 없음.
        private const string BUILDING_PREFAB_KEY = "Prefabs/Entity";

        /// <summary>
        /// 경량 건물 엔티티 생성.
        /// </summary>
        /// <param name="tableId">BuildableItemTable.Id</param>
        /// <param name="worldTileX">월드 타일 좌표 X</param>
        /// <param name="worldTileY">월드 타일 좌표 Y</param>
        /// <param name="villageId">소속 마을 Id. 없으면 -1</param>
        /// <param name="savedEntityId">저장된 EntityId 재사용 시 >=0</param>
        /// <param name="savedHp">저장된 HP. 없으면 -1 (테이블 HP 사용)</param>
        /// <returns>(entityId, entity) 튜플. 실패 시 (-1, null)</returns>
        public static async UniTask<(int entityId, EntityBase? entity)> CreateBuilding(
            int tableId, int worldTileX, int worldTileY,
            int villageId, int savedEntityId = -1, int savedHp = -1)
        {
            BuildableItemTable? table = AR.s.Data.GetBuildableItem(tableId);
            if (table == null)
            {
                Debug.LogError($"[BuildingFactory] BuildableItemTable not found for Id: {tableId}");
                return (-1, null);
            }

            // 프리팹 인스턴스 생성 (타일 중심으로 배치)
            Vector3 spawnPos = new Vector3(worldTileX + 0.5f, worldTileY + 0.5f, -0.01f);
            GameObject obj = await Addressables.InstantiateAsync(BUILDING_PREFAB_KEY, spawnPos, Quaternion.identity).ToUniTask();
            if (obj == null)
            {
                Debug.LogError($"[BuildingFactory] Failed to instantiate building prefab");
                return (-1, null);
            }

            EntityBase? entity = obj.GetComponent<EntityBase>();
            if (entity == null)
            {
                Debug.LogError($"[BuildingFactory] EntityBase not found on Building prefab");
                Object.Destroy(obj);
                return (-1, null);
            }

            // EntityId 할당 + TransformComponent
            if (savedEntityId >= 0)
            {
                entity.SetEntityId(savedEntityId);
            }
            entity.SetupEntityId();
            int entityId = entity.EntityId;

            // 태그 + 메타데이터 컴포넌트
            AR.s.Component.AddComponent(entityId, new BuildingTag());
            AR.s.Component.AddComponent(entityId, new BuildingComponent
            {
                TableId = tableId,
                VillageId = villageId,
                WorldTileX = worldTileX,
                WorldTileY = worldTileY,
                CurrentHp = savedHp >= 0 ? savedHp : table.HP
            });

            // System_Render 등록
            var renderSystem = AR.s.System.GetSystem<Systems.System_Render>();
            if (renderSystem != null)
            {
                renderSystem.RegisterEntity(entityId, entity);
            }

            // 스프라이트 경로 분기
            if (table.AnimationId == 0)
            {
                await LoadStaticSprite(entity, table.ResourceName);
            }
            else
            {
                SetupAnimatedSprite(entityId, entity, table.AnimationId);
            }

            // 자식 프리팹의 IEntityMessageHandler 자동 등록 (미래 확장용)
            entity.AutoRegisterChildHandlers();

            Debug.Log($"[BuildingFactory] Building created - EntityId: {entityId}, TableId: {tableId}, Name: {table.Name}, Pos: ({worldTileX},{worldTileY})");
            return (entityId, entity);
        }

        /// <summary>
        /// 정적 스프라이트 로드 → SpriteRenderer에 할당.
        /// SpriteLibrary/SpriteResolver는 건드리지 않음.
        /// </summary>
        private static async UniTask LoadStaticSprite(EntityBase entity, string resourceName)
        {
            if (string.IsNullOrEmpty(resourceName))
            {
                Debug.LogWarning($"[BuildingFactory] LoadStaticSprite - ResourceName is empty");
                return;
            }

            try
            {
                Sprite sprite = await Addressables.LoadAssetAsync<Sprite>(resourceName).ToUniTask();
                if (sprite != null && entity != null && entity.SpriteRenderer != null)
                {
                    entity.SpriteRenderer.sprite = sprite;
                }
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[BuildingFactory] Failed to load sprite '{resourceName}': {e.Message}");
            }
        }

        /// <summary>
        /// 애니 스프라이트 세팅.
        /// SpriteAnimationComponent 등록 후 SpriteLibraryAsset 비동기 로드 → System_Animation이 _sr.sprite 직접 교체.
        /// SpriteLibrary/SpriteResolver 컴포넌트 불필요 (SpriteAnimationData가 slAsset을 직접 사용).
        /// </summary>
        private static void SetupAnimatedSprite(int entityId, EntityBase entity, int animationId)
        {
            AnimationTable? animTable = AR.s.Data.GetAnimation(animationId);
            if (animTable == null)
            {
                Debug.LogError($"[BuildingFactory] AnimationTable not found for Id: {animationId}");
                return;
            }

            if (entity.SpriteRenderer == null)
            {
                Debug.LogError($"[BuildingFactory] SpriteRenderer missing on Building entity");
                return;
            }

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

            LoadSpriteLibraryAsync(entityId, entity, animTable).Forget();
        }

        /// <summary>
        /// SpriteLibraryAsset 비동기 로드 후 SpriteAnimationData 생성 → System_Animation에 등록.
        /// SpriteAnimationData가 slAsset을 직접 받아 Sprite를 캐싱하고, _sr.sprite를 매 프레임 교체.
        /// (SpriteLibrary 컴포넌트는 사용하지 않음)
        /// </summary>
        private static async UniTaskVoid LoadSpriteLibraryAsync(int entityId, EntityBase entity, AnimationTable animData)
        {
            UpdateAnimationLoadState(entityId, AnimationLoadState.Loading);

            using CancellationTokenSource cts = new();

            try
            {
                if (string.IsNullOrEmpty(animData.SpriteLibraryPath))
                {
                    Debug.LogWarning($"[BuildingFactory] No SpriteLibraryPath for AnimationTable Id: {animData.Id}");
                    UpdateAnimationLoadState(entityId, AnimationLoadState.Failed);
                    return;
                }

                UnityEngine.U2D.Animation.SpriteLibraryAsset slAsset = await Addressables.LoadAssetAsync<UnityEngine.U2D.Animation.SpriteLibraryAsset>(
                    animData.SpriteLibraryPath).ToUniTask(cancellationToken: cts.Token);

                if (slAsset == null || entity == null)
                {
                    UpdateAnimationLoadState(entityId, AnimationLoadState.Failed);
                    return;
                }

                SpriteRenderer? sr = entity.SpriteRenderer;
                if (sr == null)
                {
                    UpdateAnimationLoadState(entityId, AnimationLoadState.Failed);
                    return;
                }

                SpriteAnimationData animDataCache = new SpriteAnimationData(sr, slAsset);
                float[] defaultFrameDurations = new float[]
                {
                    animData.IdleFrame,
                    animData.MoveFrame,
                    animData.AttackFrame,
                    animData.DeadFrame,
                };

                var animSystem = AR.s.System.GetSystem<Systems.System_Animation>();
                if (animSystem != null)
                {
                    animSystem.RegisterSpriteAnimation(entityId, animDataCache, defaultFrameDurations);
                }

                UpdateAnimationLoadState(entityId, AnimationLoadState.Loaded);
            }
            catch (System.OperationCanceledException)
            {
            }
            catch (System.Exception e)
            {
                UpdateAnimationLoadState(entityId, AnimationLoadState.Failed);
                Debug.LogError($"[BuildingFactory] Failed to load animation for Building {entityId}: {e.Message}");
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
    }
}
