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
        private const string UI_CANVAS_PREFAB_KEY = "Prefabs/UICanvas";

        // 건설 중 공통 placeholder 스프라이트 (모든 빌딩이 건설 중에 공통 사용).
        // ComfyUI로 생성 후 ImportGeneratedSprites가 "Sprites/Tiles/UnderConstruction" 키로 등록.
        // 추후 BuildableItemTable.ConstructionSpriteResourceName 컬럼이 추가되면 우선 적용 예정.
        private const string CONSTRUCTION_SPRITE_KEY = "Sprites/Tiles/UnderConstruction";

        /// <summary>
        /// 경량 건물 엔티티 생성.
        /// </summary>
        /// <param name="tableId">BuildableItemTable.Id</param>
        /// <param name="worldTileX">월드 타일 좌표 X</param>
        /// <param name="worldTileY">월드 타일 좌표 Y</param>
        /// <param name="villageId">소속 마을 Id. 없으면 -1</param>
        /// <param name="savedEntityId">저장된 EntityId 재사용 시 >=0</param>
        /// <param name="savedHp">저장된 HP. 없으면 -1 (테이블 HP 사용). 건설중이면 0~table.HP 진행도.</param>
        /// <param name="isUnderConstruction">true면 건설중 스프라이트 + HP바(진행도)로 표시</param>
        /// <returns>(entityId, entity) 튜플. 실패 시 (-1, null)</returns>
        public static async UniTask<(int entityId, EntityBase? entity)> CreateBuilding(
            int tableId, int worldTileX, int worldTileY,
            int villageId, int savedEntityId = -1, int savedHp = -1,
            bool isUnderConstruction = false)
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
            // 건설중일 땐 CurrentHp가 진행도 역할 (0 → table.HP). 저장 HP가 없으면 0부터 시작.
            int initialHp = isUnderConstruction
                ? (savedHp >= 0 ? savedHp : 0)
                : (savedHp >= 0 ? savedHp : table.HP);
            AR.s.Component.AddComponent(entityId, new BuildingComponent
            {
                TableId = tableId,
                VillageId = villageId,
                WorldTileX = worldTileX,
                WorldTileY = worldTileY,
                CurrentHp = initialHp,
                IsUnderConstruction = isUnderConstruction,
            });

            // System_Render 등록
            var renderSystem = AR.s.System.GetSystem<Systems.System_Render>();
            if (renderSystem != null)
            {
                renderSystem.RegisterEntity(entityId, entity);
            }

            // 스프라이트 경로 분기
            //  - 건설중: 공통 placeholder 스프라이트 사용 (애니메이션 무시)
            //  - 완성: 테이블의 ResourceName 또는 AnimationId
            if (isUnderConstruction)
            {
                await LoadStaticSprite(entity, CONSTRUCTION_SPRITE_KEY);
            }
            else if (table.AnimationId == 0)
            {
                await LoadStaticSprite(entity, table.ResourceName);
            }
            else
            {
                SetupAnimatedSprite(entityId, entity, table.AnimationId);
            }

            // HP바 부착 (완성 빌딩도 데미지 받으면 표시되도록 항상 부착).
            // table.HP <= 0 인 빌딩은 HP 개념이 없으므로 스킵.
            // await 필수 — 다음의 AutoRegisterChildHandlers가 HpBarView를 등록할 때 GameObject가 살아있어야 함.
            if (table.HP > 0)
            {
                await AttachStatAndHpBar(entityId, entity, initialHp, table.HP);
            }

            // 자식 프리팹의 IEntityMessageHandler 자동 등록 (HpBarView도 여기서 등록됨)
            entity.AutoRegisterChildHandlers();

            // 초기 HP바 갱신 — DamageMessage로 fillAmount 즉시 동기화
            if (table.HP > 0)
            {
                AR.s.Message.SendToEntity(new ARPG.Message.DamageMessage
                {
                    TargetEntityId = entityId,
                    DamageAmount = 0,
                    AttackerEntityId = -1,
                    DamageType = GlobalEnum.DamageType.Physics,
                    CurrentHp = initialHp,
                    MaxHp = table.HP,
                });
            }

            string mode = isUnderConstruction ? "건설중" : "완성";
            Debug.Log($"[BuildingFactory] Building created [{mode}] - EntityId: {entityId}, TableId: {tableId}, Name: {table.Name}, Pos: ({worldTileX},{worldTileY}), HP: {initialHp}/{table.HP}");
            return (entityId, entity);
        }

        /// <summary>
        /// 빌딩에 StatComponent + HP바 프리팹 부착.
        /// 진행도/HP를 단일 표현으로 통합 — fillAmount = CurrentHp / MaxHp.
        /// </summary>
        private static async UniTask AttachStatAndHpBar(int entityId, EntityBase entity, int currentHp, int maxHp)
        {
            // StatComponent — MaxHp만 의미있게 사용. 다른 스탯은 0으로 두어도 무방 (전투 시스템이 빌딩을 별도 분기 처리하지 않으면 데미지 계산 시 적용됨).
            StatComponent stat = new();
            stat.BaseMaxHp = maxHp;
            stat.FinalMaxHp = maxHp;
            stat.SetCurrentHpDirect(currentHp);
            AR.s.Component.AddComponent(entityId, stat);

            // HP바 프리팹 로드 → _visual 아래 자식으로 추가 (NPC/몬스터와 동일 패턴)
            try
            {
                GameObject hpBarObj = await Addressables.InstantiateAsync(UI_CANVAS_PREFAB_KEY, entity.Visual.transform).ToUniTask();
                if (hpBarObj != null)
                    hpBarObj.transform.localPosition = Vector3.zero;
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"[BuildingFactory] Failed to load HP bar prefab: {e.Message}");
            }
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
