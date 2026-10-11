#nullable enable
using System.Collections.Generic;
using System.Threading;
using ARPG.AI;
using ARPG.Base;
using ARPG.Component;
using ARPG.Data;
using ARPG.Manager;
using ARPG.Map;
using ARPG.Npc;
using ARPG.Tables;
using ARPG.Utility;
using ARPG.View;
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
        private const string ENTITY_PREFAB_KEY = "Prefabs/Entity";
        private const string SKILL_AIM_INDICATOR_PREFAB_KEY = "Prefabs/Player/SkillAimIndicator";

        // 충돌 반경 fallback — CreatureTable.MoveRadius/HitRadius/HitOffsetY가 0일 때 사용
        // 1 unit = 1 tile, 캐릭터 휴머노이드 기본값
        private const float DEFAULT_MOVE_RADIUS = 0.30f;

        /// <summary>
        /// MonsterTable 기반 몬스터 엔티티 생성
        /// MonsterTable → Stat + State + Velocity + AI + Skill + Drop + MonsterTag + MonsterArchetype
        /// </summary>
        /// <param name="archetypeOverride">개체 등급. null이면 MonsterTable.Archetype (MONSTER_RARITY_DESIGN.md §5)</param>
        /// <param name="affixes">무리 생성 때 굴린 접사 (MONSTER_AFFIX_DESIGN.md §4). 기본값은 접사 없음</param>
        /// <returns>(entityId, entity) 튜플. 실패 시 (-1, null)</returns>
        public static async UniTask<(int entityId, EntityBase? entity)> CreateMonster(int monsterTableId, Vector3 position, Transform? parent = null, int level = 0,
            GlobalEnum.MonsterArchetype? archetypeOverride = null, MonsterAffixComponent affixes = default)
        {
            using var contextLoad = AR.s.Map.Context.BeginLoad();
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

            if (contextLoad.IsCurrent == false)
            {
                Addressables.ReleaseInstance(obj);
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
            AddFogVisibility(entityId, entity);

            // 4. ECS 컴포넌트 추가
            await AddCreatureComponents(entityId, table, entity);

            // 4-1. Archetype × Level 스탯 스케일링 (HP / 모든 속성 공격력 / 원소 저항)
            if (contextLoad.IsCurrent == false || entity == null || entity.IsCurrentEntity == false)
            {
                ReleaseUnfinishedEntity(entity);
                return (-1, null);
            }
            int effectiveLevel = level > 0 ? level : table.Level;
            GlobalEnum.MonsterArchetype archetype = archetypeOverride ?? table.Archetype;
            MonsterArchetypeTable? archetypeTable = AR.s.Data.GetMonsterArchetype(archetype);
            if (archetypeTable == null)
            {
                Debug.LogWarning($"[EntityFactory] MonsterArchetypeTable not found for {archetype} (MonsterId: {table.Id})");
            }
            else
            {
                ApplyMonsterArchetypeScaling(entityId, archetypeTable, effectiveLevel);
            }
            // 접사는 등급 스케일링 다음에 적용한다 (MONSTER_AFFIX_DESIGN.md §6.1).
            int affixCount = ApplyMonsterAffixes(entityId, affixes);
            AR.s.Component.AddComponent(entityId, new MonsterArchetypeComponent { Archetype = archetype });

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
                // 등급 보상: 판정 횟수와 높은 Tier 보너스 (MONSTER_RARITY_DESIGN.md §3.2)
                // 판정 횟수 = 등급 기본 판정 + 접사당 판정 × 접사 수 (MONSTER_AFFIX_DESIGN.md §8)
                AR.s.Component.AddComponent(entityId, new DropComponent
                {
                    DropId = table.DropId,
                    DropRateBonus = table.DropRateBonus,
                    DropRarityBonus = table.DropRarityBonus + (archetypeTable != null ? archetypeTable.DropRarityBonus : 0),
                    MonsterLevel = effectiveLevel,
                    DropRolls = archetypeTable != null
                        ? Mathf.Max(1, archetypeTable.DropRolls + archetypeTable.DropRollsPerAffix * affixCount)
                        : 1,
                    ModTier11ChanceBp = archetypeTable != null ? archetypeTable.ModTier11ChanceBp : 0,
                    ModTier12ChanceBp = archetypeTable != null ? archetypeTable.ModTier12ChanceBp : 0,
                });
            }

            RegisterToSystems(entityId, obj, table.AnimationId);

            if (table.AnimationData != null)
            {
                LoadAnimationAsync(entityId, obj, table.AnimationData).Forget();
            }

            // 자식 프리팹의 IEntityMessageHandler 자동 등록
            entity.AutoRegisterChildHandlers();

            Debug.Log($"[EntityFactory] Monster created - EntityId: {entityId}, TableId: {monsterTableId}, Name: {table.Name}, Archetype: {archetype}, Affixes: {affixCount}");
            return (entityId, entity);
        }

        /// <summary>
        /// NpcTable 기반 NPC 엔티티 생성
        /// NpcTable → Stat + State + Velocity + AI
        /// saveData가 주어지면 InventoryEquip의 장비 modifier를 ECS에 복원한다 (PlayerData와 동일 패턴).
        /// </summary>
        /// <returns>(entityId, entity) 튜플. 실패 시 (-1, null)</returns>
        public static async UniTask<(int entityId, EntityBase? entity)> CreateNpc(int npcTableId, Vector3 position, Transform? parent = null, int savedEntityId = -1, NpcSaveData? saveData = null)
        {
            using var contextLoad = AR.s.Map.Context.BeginLoad();
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

            if (contextLoad.IsCurrent == false)
            {
                Addressables.ReleaseInstance(obj);
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
            AddFogVisibility(entityId, entity);

            // 4. ECS 컴포넌트 추가
            await AddCreatureComponents(entityId, table, entity);

            // NpcTag는 AI 컴포넌트 부착보다 먼저 등록해야 AddAIComponents의 isNpc 판정이 정상 동작
            if (contextLoad.IsCurrent == false || entity == null || entity.IsCurrentEntity == false)
            {
                ReleaseUnfinishedEntity(entity);
                return (-1, null);
            }
            // (Patrol/PatrolRanged BehaviorType 강제 + Patrol 초기 상태)
            AR.s.Component.AddComponent(entityId, new NpcTag());

            // 진영 명시 — "컴포넌트 없음 = 중립" 암묵 규칙에 기대지 않는다 (NPC_COMBAT_DESIGN §3.2)
            AR.s.Component.AddComponent(entityId, new FactionComponent { FactionId = Faction.Neutral });

            // NPC 고유 성향 스탯 (saveData에 영속화됨, 신규 NPC는 생성자에서 랜덤 부여됨)
            AR.s.Component.AddComponent(entityId, saveData != null ? saveData.NpcStat : new NpcStatComponent());

            // 마을 NPC 컴포넌트
            AR.s.Component.AddComponent(entityId, new NpcVillageComponent());

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

            // 장비 modifier 적용 (StatComponent에 합산). saveData.InventoryEquip의 ItemData는
            // NpcManager가 신규 시드(SeedInitialEquipment) 또는 로드(OnLoadCompleted) 단계에서 이미 준비해 둔다.
            if (saveData != null)
            {
                EquipHelper.ApplyAllEquipmentModifiers(entityId, saveData.InventoryEquip);
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

            // 3. 플레이어 전용 조준 화살표 로드. 실패해도 전투 로직은 계속 초기화한다.
            await AttachPlayerSkillAimIndicatorAsync(entity);

            // 4. 저장된 PlayerId로 EntityId 설정
            int savedPlayerId = AR.s.Player.GetSavedPlayerId();
            if (savedPlayerId >= 0)
            {
                entity.SetEntityId(savedPlayerId);
            }

            entity.SetupEntityId();
            int entityId = entity.EntityId;

            // 5. PlayerData/인벤토리 초기화
            AR.s.Player.InitializePlayerData();

            // 6. ECS 컴포넌트 추가
            await AddCreatureComponents(entityId, table, entity);

            AR.s.Component.AddComponent(entityId, new InputComponent
            {
                MoveDirection = Vector2.zero,
                MousePosition = Vector2.zero,
                MouseWorldPosition = position,
                IsAttacking = false,
                IsInteracting = false,
                IsSprinting = false
            });

            AR.s.Component.AddComponent(entityId, new SkillAimIndicatorComponent
            {
                ActiveSkillEntityId = -1,
                IsVisible = false,
                LastAppliedAngle = 0f,
            });

            AR.s.Component.AddComponent(entityId, new MapChunkLoaderComponent
            {
                CurrentChunk = new Vector2Int(-100000, -100000),
                LoadRadius = 2,
                IsInitialized = false
            });

            // 진영 (플레이어) - 스킬·발사체·AI 타겟 필터에 사용. 토템·지뢰는 이 값을 그대로 복사
            AR.s.Component.AddComponent(entityId, new FactionComponent { FactionId = Faction.Player });

            // 시야 (FOG_OF_WAR_DESIGN §4) - 지금은 고정 반경. 장비 옵션으로 늘리면 이 값을 스탯에서 채운다
            AR.s.Component.AddComponent(entityId, new VisionComponent { Radius = FogOfWarManager.DEFAULT_VISION_RADIUS });

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

        private static void ReleaseUnfinishedEntity(EntityBase? entity)
        {
            if (entity == null) return;
            if (entity.IsCurrentEntity)
            {
                entity.OnEntityDestroy();
                AR.s.Entity.DestroyEntity(entity.EntityId);
            }
            Addressables.ReleaseInstance(entity.gameObject);
        }

        /// <summary>
        /// 몬스터에 Archetype × Level 스케일링 적용. HP와 전체 스킬 데미지 배율에 곱연산, 원소 저항에 가산.
        /// 개체 등급 행 + effectiveLevel + BalanceConstants 곡선 사용.
        /// finalHp  = BaseHp  × HpPerLevel^(Level-1)  × Archetype.HpMul
        /// finalDmg = (StatDamage + SkillTableDamage) × DmgPerLevel^(Level-1) × Archetype.DmgMul
        /// resist   = BaseResist + Archetype.ResistAdd (상한은 DamageCalculator가 최대 저항으로 자름)
        /// </summary>
        private static void ApplyMonsterArchetypeScaling(int entityId, MonsterArchetypeTable archetype, int effectiveLevel)
        {
            if (effectiveLevel <= 0)
                return;

            if (AR.s.Component.TryGetComponent<StatComponent>(entityId, out var stat) == false)
                return;

            float hpMul  = Mathf.Pow(BalanceConstants.HpPerLevel,  effectiveLevel - 1) * archetype.HpMul;
            float dmgMul = Mathf.Pow(BalanceConstants.DmgPerLevel, effectiveLevel - 1) * archetype.DmgMul;

            stat.BaseMaxHp = Mathf.Max(1, Mathf.RoundToInt(stat.BaseMaxHp * hpMul));

            // SkillDamage는 System_SkillAttackProfileCalculation에서 전체 스킬 데미지에 곱해진다.
            // 0은 기존 코드에서 100% fallback 의미로 쓰이므로 스케일링 전 기준값도 100으로 취급.
            int baseSkillDamage = stat.BaseSkillDamage > 0 ? stat.BaseSkillDamage : 100;
            stat.BaseSkillDamage = Mathf.Max(1, Mathf.RoundToInt(baseSkillDamage * dmgMul));

            stat.BaseFireResist      += archetype.ResistAdd;
            stat.BaseIceResist       += archetype.ResistAdd;
            stat.BaseLightningResist += archetype.ResistAdd;
            stat.BasePoisonResist    += archetype.ResistAdd;

            stat.CopyBaseToFinal();
            stat.SetCurrentHpDirect(stat.FinalMaxHp);
            AR.s.Component.SetComponent(entityId, stat);

            // AddCreatureComponents가 HP바를 기다리는 동안 첫 재계산이 끝났을 수 있다.
            // 바뀐 저항이 피격 감소 캐시(DamageDefenseCacheComponent)에 반영되도록 다시 계산을 요청한다.
            AR.s.Component.AddComponent(entityId, new StatDirtyTag());
        }

        /// <summary>
        /// 굴린 접사를 몬스터에 적용한다 (MONSTER_AFFIX_DESIGN.md §6.1).
        /// 스탯형은 StatModifier(출처 MonsterAffix, SourceId = 접사 Id)로 등록하고, 오라형은 개수만 세어 System_MonsterAura가 처리한다.
        /// 접사가 1개 이상이면 MonsterAffixComponent를 붙이고, 다음 재계산에서 HP를 가득 채우도록 SpawnHpRefillTag를 붙인다.
        /// </summary>
        /// <returns>실제로 적용된 접사 수 (시트에 없는 Id는 건너뛴다)</returns>
        private static int ApplyMonsterAffixes(int entityId, MonsterAffixComponent rolled)
        {
            if (rolled.Count <= 0)
                return 0;

            ComponentManager cm = AR.s.Component;
            DataManager data = AR.s.Data;
            MonsterAffixComponent applied = default;
            for (int i = 0; i < rolled.Count; i++)
            {
                int affixId = rolled.GetByIndex(i);
                MonsterAffixTable? affix = data.GetMonsterAffix(affixId);
                if (affix == null)
                {
                    Debug.LogWarning($"[EntityFactory] MonsterAffixTable not found: {affixId} (EntityId: {entityId})");
                    continue;
                }

                if (affix.Kind == GlobalEnum.MonsterAffixKind.Stat)
                {
                    AddMonsterAffixStatModifier(data, entityId, affixId, affix.Stat1, affix.ModType1, affix.Value1);
                    AddMonsterAffixStatModifier(data, entityId, affixId, affix.Stat2, affix.ModType2, affix.Value2);
                    AddMonsterAffixStatModifier(data, entityId, affixId, affix.Stat3, affix.ModType3, affix.Value3);
                }
                else if (affix.Kind == GlobalEnum.MonsterAffixKind.Aura)
                {
                    applied.AuraCount++;
                }

                applied.Add(affixId);
            }

            if (applied.Count == 0)
                return 0;

            cm.AddComponent(entityId, applied);
            cm.AddComponent(entityId, new SpawnHpRefillTag());
            cm.AddComponent(entityId, new StatDirtyTag());
            return applied.Count;
        }

        private static void AddMonsterAffixStatModifier(DataManager data, int entityId, int affixId,
            GlobalEnum.Stat stat, StatModifierType modifierType, int value)
        {
            if (value == 0)
                return;

            data.AddStatModifier(entityId, StatModifierSource.MonsterAffix, affixId, stat, modifierType, value);
        }

        #region 공통 컴포넌트 추가 메서드

        private const string UI_CANVAS_PREFAB_KEY = "Prefabs/UICanvas";
        private const string SHADOW_PREFAB_KEY = "Prefabs/Shadow";

        /// <summary>
        /// 시야 밖에서 숨길 이동 엔티티로 등록한다 (FOG_OF_WAR_DESIGN §5). 안개가 켜져 있으면 숨김 상태로 시작해
        /// HP바·그림자·애니메이션을 비동기로 불러오는 동안이나 첫 판정 전에 한 프레임도 보이지 않게 한다.
        /// 숨긴 Visual 아래의 핸들러도 AutoRegisterChildHandlers가 등록한다. 보임 전환은 System_FogEntityVisibility가 한다.
        /// </summary>
        private static void AddFogVisibility(int entityId, EntityBase entity)
        {
            bool visible = AR.s.Fog.IsActive == false;
            AR.s.Component.AddComponent(entityId, new FogVisibilityComponent { IsVisible = visible });
            if (entity.Visual != null && entity.Visual.activeSelf != visible)
                entity.Visual.SetActive(visible);
        }

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

            // ColliderComponent — 시트 미입력 시 fallback:
            //   MoveRadius == 0  → DEFAULT_MOVE_RADIUS
            //   HitRadius  == 0  → MoveRadius (피격/이동 동일 = 분리 안 한 것과 동일 동작)
            //   HitOffsetY == 0  → 발 좌표 (기존 동작)
            float moveRadius = table.MoveRadius > 0f ? table.MoveRadius : DEFAULT_MOVE_RADIUS;
            float hitRadius = table.HitRadius > 0f ? table.HitRadius : moveRadius;
            AR.s.Component.AddComponent(entityId, new ColliderComponent
            {
                MoveRadius = moveRadius,
                HitRadius = hitRadius,
                HitOffset = new Vector2(0f, table.HitOffsetY)
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
            if (entity == null || entity.IsCurrentEntity == false) return;
            try
            {
                GameObject shadowObj = await Addressables.InstantiateAsync(SHADOW_PREFAB_KEY, entity.Visual.transform).ToUniTask();
                if (shadowObj != null && entity != null)
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

            // KeepDistance는 AiTable에서 직접 읽음 (시트 미설정 시 Ranged 7, Melee 0 fallback)
            float keepDistance = aiTable != null
                ? aiTable.KeepDistance
                : (behaviorType == AIBehaviorType.Ranged ? 7f : 0f);

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

            AIStateHelper.EnterInitialState(entityId);
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

            AIStateHelper.EnterInitialState(entityId);
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
        /// 상호작용 전용 정적 엔티티(던전 상자·입구) 생성. 범용 프리팹에 스프라이트만 붙이고
        /// HP·진영·AI·그림자는 붙이지 않으므로 전투 대상 조회에 잡히지 않는다.
        /// 도메인 컴포넌트와 표시 자식은 소유 매니저가 붙인다. 생성 중 맵 컨텍스트가 바뀌면 (-1, null).
        /// </summary>
        /// <param name="map">엔티티가 속할 맵. 이 맵의 컨텍스트가 바뀌면 생성을 버린다</param>
        /// <param name="position">루트 위치 (상호작용 판정 기준)</param>
        /// <param name="visualOffset">스프라이트를 루트에서 띄울 로컬 오프셋</param>
        public static async UniTask<(int entityId, EntityBase? entity)> CreateStaticEntity(MapManager map,
            Vector3 position, Sprite? sprite, Vector2 visualOffset, int sortingOrder, Transform? parent = null)
        {
            using var contextLoad = map.Context.BeginLoad();
            GameObject obj = await Addressables.InstantiateAsync(ENTITY_PREFAB_KEY, position, Quaternion.identity, parent).ToUniTask();
            if (obj == null)
            {
                Debug.LogError("[EntityFactory] Failed to instantiate static entity prefab");
                return (-1, null);
            }
            if (contextLoad.IsCurrent == false)
            {
                Addressables.ReleaseInstance(obj);
                return (-1, null);
            }

            EntityBase? entity = obj.GetComponent<EntityBase>();
            if (entity == null)
            {
                Debug.LogError("[EntityFactory] EntityBase not found on static entity prefab");
                Addressables.ReleaseInstance(obj);
                return (-1, null);
            }

            entity.SetupEntityId();
            if (entity.SpriteRenderer != null)
            {
                entity.SpriteRenderer.sprite = sprite;
                entity.SpriteRenderer.sortingOrder = sortingOrder;
            }
            if (entity.Visual != null)
                entity.Visual.transform.localPosition = visualOffset;
            return (entity.EntityId, entity);
        }

        /// <summary>
        /// 생성 직후 소유자가 더 이상 필요로 하지 않게 된 정적 엔티티를 컴포넌트 부착 전에 반환한다.
        /// </summary>
        public static void ReleaseStaticEntity(EntityBase entity)
        {
            if (entity.IsCurrentEntity == true)
            {
                entity.OnEntityDestroy();
                AR.s.Entity.DestroyEntity(entity.EntityId);
            }
            Addressables.ReleaseInstance(entity.gameObject);
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

            int totemId = AR.s.Entity.CreateEntity();

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
            // 토템은 정적이라 피격 반경/오프셋만 의미가 있으나, 통일성을 위해 캐릭터 기본값 사용
            cm.AddComponent(totemId, new ColliderComponent
            {
                MoveRadius = DEFAULT_MOVE_RADIUS,
                HitRadius = DEFAULT_MOVE_RADIUS,
                HitOffset = Vector2.zero
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

            AIStateHelper.EnterInitialState(totemId);

            Debug.Log($"[EntityFactory] CreateTotem - TotemId: {totemId}, Caster: {casterEntityId}, SkillId: {skillId}, Position: {position}, Duration: {duration}");
            return totemId;
        }

        /// <summary>
        /// 장판(AreaEffect) 엔티티 생성. caster의 진영을 그대로 따르며 AreaEffectTable을 참조해
        /// System_AreaEffect가 매 틱 범위 내 적에게 데미지/버프를 적용한다.
        /// 만료(LifetimeComponent)는 System_Lifetime이 DestroyTag를 부착해 정리.
        /// PrefabKey가 지정되면 AddressablePool에서 GameObject를 로드하고 System_Render에 등록.
        /// 컴포넌트는 즉시 부착된다. 반복 장판의 GameObject는 활성화 시점에 비동기 로드한다.
        /// </summary>
        /// <param name="fallbackCasterFaction">caster가 이미 사라져 진영을 읽을 수 없을 때 쓸 진영 (포물선 발사체 비행 중 사망 등)</param>
        public static void CreateAreaEffect(int casterEntityId, int areaEffectTableId, Vector2 position, int skillId = 0,
            Faction fallbackCasterFaction = Faction.Neutral, int sourceSkillEntityId = 0)
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
                faction = cm.TryGetComponent<FactionComponent>(casterEntityId, out var casterFaction)
                    ? casterFaction.FactionId
                    : fallbackCasterFaction;
            }

            int entityId = AR.s.Entity.CreateEntity();

            cm.AddComponent(entityId, new TransformComponent
            {
                Position = position,
                Rotation = 0f,
                Scale = Vector2.one
            });
            // FactionComponent는 의도적으로 부착하지 않음 — AI 적 탐색이 장판을 공격 대상으로 인식하지 않게.
            // 적/아군 판정은 AreaEffectComponent.CasterFaction 스냅샷으로 수행 (System_AreaEffect 참조).
            cm.AddComponent(entityId, new CasterLinkComponent { CasterEntityId = casterEntityId });
            // Lifetime은 활성화 이후부터 시작한다. 대기 시간 만료가 폭발보다 먼저 엔티티를 없애면 안 된다.
            float activationDelay = Mathf.Max(0f, table.ActivationDelay);
            float step = Mathf.Max(0.0001f, Time.fixedDeltaTime);
            float activationTime = Time.fixedTime + Mathf.Ceil(activationDelay / step) * step;
            cm.AddComponent(entityId, new AreaEffectComponent
            {
                OwnerEntityId = casterEntityId,
                AreaEffectTableId = areaEffectTableId,
                SkillId = skillId,
                CasterFaction = faction,
                Radius = table.Radius,
                TickInterval = table.TickInterval,
                NextTickIn = 0f,   // 첫 프레임에 첫 틱 발동
                ActivationFixedTime = activationTime,
                SingleShot = table.SingleShot,
                Credit = ARPG.Skill.Combat.CombatCredit.Resolve(casterEntityId),
            });
            cm.AddComponent(entityId, new AreaEffectTag());

            if (table.SingleShot == true)
                SnapshotAreaAttack(cm, entityId, casterEntityId, table, sourceSkillEntityId, skillId);

            // 설치된 범위의 예고는 시전자 사망/시전 종료와 독립적으로 유지한다.
            if (activationDelay > 0f && faction == Faction.Hostile)
            {
                cm.AddComponent(entityId, new AttackTelegraphComponent
                {
                    OwnerEntityId = casterEntityId,
                    Shape = ARPG.Skill.Telegraph.AttackTelegraphShape.TargetCircle,
                    OriginMode = ARPG.Skill.Telegraph.AttackTelegraphOriginMode.TargetPosition,
                    TargetPosition = position,
                    Direction = Vector2.right,
                    Range = table.Radius,
                    HalfAngleDeg = 180f,
                    ProjectileCount = 1,
                    WarningStartFixedTime = Time.fixedTime,
                    NextCommitFixedTime = activationTime,
                    CommitCount = 1,
                });
            }

            Debug.Log($"[EntityFactory] CreateAreaEffect - EntityId: {entityId}, Caster: {casterEntityId}, TableId: {areaEffectTableId}, Position: {position}, Radius: {table.Radius}, Duration: {table.Duration}");
        }

        /// <summary>반복 장판의 활성화 순간 시각을 로드한다. 준비 중에는 활성 장판 비주얼을 표시하지 않는다.</summary>
        public static async void AttachAreaEffectVisual(int entityId, AreaEffectTable table, Vector2 position)
        {
            if (string.IsNullOrEmpty(table.PrefabKey))
                return;

            using var contextLoad = AR.s.Map.Context.BeginLoad();
            ComponentManager cm = AR.s.Component;

            // 엔티티가 이미 만료되어 사라진 경우(짧은 Duration + 늦은 로드) 로드 결과를 버림
            Vector3 spawnPos = new Vector3(position.x, position.y, 0f);
            GameObject? obj = await AddressablePool.Get(table.PrefabKey, spawnPos, Quaternion.identity);
            if (obj == null)
            {
                Debug.LogWarning($"[EntityFactory] CreateAreaEffect - AddressablePool.Get returned null for key: {table.PrefabKey}");
                return;
            }

            if (contextLoad.IsCurrent == false || cm.HasComponent<DestroyTag>(entityId) || cm.HasComponent<AreaEffectComponent>(entityId) == false)
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
            int skillEntityId = AR.s.Entity.CreateSkillEntity(ownerEntityId, slotIndex);
            if (skillEntityId == -1)
            {
                Debug.LogError($"[EntityFactory] Failed to create skill entity. Owner: {ownerEntityId}, Slot: {slotIndex}, SkillId: {skillId}");
                return;
            }

            var skillTable = AR.s.Data.GetSkill(skillId);
            if (skillTable == null)
            {
                Debug.LogError($"[EntityFactory] Skill table not found for SkillId: {skillId}");
                return;
            }

            AR.s.Component.GetComponentPool<SkillCommandComponent>();

            List<int>? allEffectIds = BuildEffectiveSkillEffectIds(ownerEntityId, slotIndex, skillTable);

            // StatBonus 행을 트리거별 SkillStatBonus*Component로 흡수
            SkillStatBonusHelper.Rebuild(skillEntityId, allEffectIds);

            // 흡수된 StatBonus 행은 런타임 트리거 리스트에서 제거 → EffectAction만 인라인 배열로 보관
            SkillEffectIds actionEffectIds = SkillStatBonusHelper.FilterToActionEffects(allEffectIds);

            AR.s.Component.AddComponent(skillEntityId, new SkillComponent
            {
                SkillId = skillId,
                OwnerEntityId = ownerEntityId,
                SlotIndex = slotIndex,
                Table = skillTable,
                EffectiveSkillEffectIds = actionEffectIds,
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

            // 데미지 계산용 공격 프로파일 캐시 최초 빌드 요청 (다음 Update에서 System_SkillAttackProfileCalculation이 처리)
            AR.s.Component.AddComponent(skillEntityId, new SkillAttackProfileDirtyTag());
        }

        private static List<int>? BuildEffectiveSkillEffectIds(int ownerEntityId, int slotIndex, SkillTable skillTable)
        {
            List<int>? result = null;
            HashSet<int>? seen = null;

            void AddEffectId(int effectId)
            {
                if (effectId <= 0)
                    return;

                seen ??= new HashSet<int>();
                if (seen.Add(effectId) == false)
                    return;

                result ??= new List<int>();
                result.Add(effectId);
            }

            if (skillTable.SkillEffectIds != null)
            {
                for (int i = 0; i < skillTable.SkillEffectIds.Count; i++)
                {
                    AddEffectId(skillTable.SkillEffectIds[i]);
                }
            }

            ItemData? book = GetPlayerSkillBookForSlot(ownerEntityId, slotIndex);
            if (book?.SkillBook?.SocketedPages != null)
            {
                List<ItemData> socketedPages = book.SkillBook.SocketedPages;
                for (int i = 0; i < socketedPages.Count; i++)
                {
                    SkillPageData? page = socketedPages[i].SkillPage;
                    if (page != null) AddEffectId(page.SkillEffectId);
                }
            }

            return result;
        }

        private static ItemData? GetPlayerSkillBookForSlot(int ownerEntityId, int slotIndex)
        {
            if (AR.s?.Data == null || ownerEntityId != AR.s.Data.CurrentPlayerEntityId)
                return null;

            ItemData?[]? skillBookSlots = AR.s.Data.Player?._skillBookSlots;
            if (skillBookSlots == null || slotIndex < 0 || slotIndex >= skillBookSlots.Length)
                return null;

            return skillBookSlots[slotIndex];
        }

        /// <summary>
        /// 지정 슬롯의 스킬 엔티티 제거 (SKILLBOOK_DESIGN.md §2.6).
        /// 결정적 ID로 스킬 엔티티를 찾아 ECS 컴포넌트 일괄 제거 + 슬롯 해제.
        /// 슬롯이 비어 있으면 (등록되지 않은 ID) 조용히 무시.
        /// </summary>
        public static void RemoveSkill(int ownerEntityId, int slotIndex)
        {
            int skillEntityId = EntityManager.GetDeterministicId(ownerEntityId, EntityIdCategory.Skill, slotIndex);
            if (skillEntityId == -1)
            {
                Debug.LogWarning($"[EntityFactory] RemoveSkill - Invalid skill entity ID. Owner: {ownerEntityId}, Slot: {slotIndex}");
                return;
            }

            // 등록되지 않은 슬롯(스킬 미장착)은 정상 케이스 — 경고 없이 무시
            if (AR.s.Entity.IsEntityRegistered(skillEntityId) == false)
            {
                return;
            }

            AR.s.Entity.DestroySkillEntity(skillEntityId);
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

        private static async UniTask AttachPlayerSkillAimIndicatorAsync(EntityBase entity)
        {
            AsyncOperationHandle<GameObject> handle = Addressables.InstantiateAsync(
                SKILL_AIM_INDICATOR_PREFAB_KEY,
                entity.transform,
                false);

            try
            {
                GameObject indicatorObject = await handle.ToUniTask();
                if (indicatorObject == null || entity == null)
                {
                    if (handle.IsValid() == true)
                    {
                        Addressables.ReleaseInstance(handle);
                    }
                    return;
                }

                SkillAimIndicatorView? view = indicatorObject.GetComponent<SkillAimIndicatorView>();
                if (view == null)
                {
                    Debug.LogWarning(
                        $"[EntityFactory] SkillAimIndicatorView not found on '{SKILL_AIM_INDICATOR_PREFAB_KEY}'.");
                    Addressables.ReleaseInstance(handle);
                    return;
                }

                indicatorObject.transform.localPosition = Vector3.zero;
                indicatorObject.transform.localRotation = Quaternion.identity;
                indicatorObject.transform.localScale = Vector3.one;
                entity.AttachSkillAimIndicator(view, handle);
            }
            catch (System.Exception e)
            {
                if (handle.IsValid() == true)
                {
                    if (handle.Status == AsyncOperationStatus.Succeeded)
                    {
                        Addressables.ReleaseInstance(handle);
                    }
                    else
                    {
                        Addressables.Release(handle);
                    }
                }

                Debug.LogWarning(
                    $"[EntityFactory] Failed to load player skill aim indicator: {e.Message}");
            }
        }

        private static async UniTaskVoid LoadAnimationAsync(int entityId, GameObject obj, AnimationTable animData)
        {
            using var contextLoad = AR.s.Map.Context.BeginLoad();
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

                if (contextLoad.IsCurrent == false || obj == null ||
                    obj.GetComponent<EntityBase>()?.IsCurrentEntity != true) return;

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
                if (contextLoad.IsCurrent && obj != null && obj.GetComponent<EntityBase>()?.IsCurrentEntity == true)
                    UpdateAnimationLoadState(entityId, AnimationLoadState.Failed);
                Debug.LogError($"[EntityFactory] Failed to load animation for Entity {entityId}: {e.Message}");
            }
            finally
            {
                cts.Dispose();
            }
        }

        private static void SnapshotAreaAttack(ComponentManager cm, int areaEntityId, int casterEntityId,
            AreaEffectTable table, int sourceSkillEntityId, int skillId)
        {
            if (sourceSkillEntityId <= 0 && skillId > 0)
            {
                SparseSet<SkillComponent> skills = cm.GetComponentPool<SkillComponent>();
                for (int i = 0; i < skills.Count; i++)
                {
                    SkillComponent skill = skills.GetByIndex(i);
                    if (skill.OwnerEntityId == casterEntityId && skill.SkillId == skillId)
                    {
                        sourceSkillEntityId = skills.GetEntityId(i);
                        break;
                    }
                }
            }

            if (sourceSkillEntityId > 0)
            {
                if (cm.HasComponent<SkillAttackProfileComponent>(sourceSkillEntityId) == false)
                    Systems.System_SkillAttackProfileCalculation.BuildProfile(sourceSkillEntityId);
                if (cm.TryGetComponent<SkillAttackProfileComponent>(sourceSkillEntityId, out var profile) == true)
                {
                    // 장판은 생명체가 아니므로 흡혈/반사 자기 유지 효과를 갖지 않는다.
                    profile.LifeStealRatio = 0f;
                    cm.AddComponent(areaEntityId, profile);
                    return;
                }
            }

            // 독립 생성(연결 스킬 없음)은 장판 Damage를 기본 피해로 사용한다.
            cm.TryGetComponent<StatComponent>(casterEntityId, out var casterStat);
            SkillAttackProfileComponent fallback = new()
            {
                DamageType = table.DamageType,
                BaseDamageMul = 1f,
                SkillDamageMul = casterStat.FinalSkillDamage > 0 ? casterStat.FinalSkillDamage / 100f : 1f,
                CritMultiplier = 1.5f,
            };
            int damage = Mathf.Max(0, table.Damage);
            switch (table.DamageType)
            {
                case GlobalEnum.DamageType.Physics: fallback.PhysAddedMin = fallback.PhysAddedMax = damage; break;
                case GlobalEnum.DamageType.Fire: fallback.FireAddedMin = fallback.FireAddedMax = damage; break;
                case GlobalEnum.DamageType.Ice: fallback.IceAddedMin = fallback.IceAddedMax = damage; break;
                case GlobalEnum.DamageType.Lightning: fallback.LightAddedMin = fallback.LightAddedMax = damage; break;
                case GlobalEnum.DamageType.Poison: fallback.PoisonAddedMin = fallback.PoisonAddedMax = damage; break;
            }
            cm.AddComponent(areaEntityId, fallback);
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
