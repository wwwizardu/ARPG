#nullable enable
using ARPG.Component;
using ARPG.Map;
using ARPG.Utility;
using ARPG.Village;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace ARPG.Systems
{
    /// <summary>
    /// 마을의 범용 오브젝트 배치 큐 — 자가 건설 (NPC_SIMPLIFICATION_PLAN D4).
    /// Step A (BUILD_PRIORITY_DESIGN.md): ObjectPlacementTaskComponent를 별도 task entity에 부착.
    ///  - 동시 task 한도는 마을당 Population (§0 추상 인구 정량화 값).
    ///  - VillageNeedsEvaluator가 점수 정렬한 후보 리스트를 받아 affordable + placeable한 첫 후보 채택.
    ///  - 자원 차감 + 새 task entity 발급 + 게임시간 누적 → 완료 시 PlaceObject + entity 폐기.
    ///  - NPC 배정 없음: AccumulatedHours는 게임시간 경과만큼 무조건 누적된다.
    ///    단 건설중 빌딩이 데미지를 받으면 진행도가 후퇴한다 (HP↔진행도 양방향 동기화 유지).
    /// 우선순위 정책은 BUILD_PRIORITY_DESIGN.md §2 (4 Layer 점수) 참조.
    /// </summary>
    public class System_VillageBuildQueue : IFixedUpdateSystem
    {
        private const int DEFAULT_MAX_RADIUS = 3;

        // 도메인 대역 (CLAUDE.md): 65-69 Construction (Phase C에서 58→66 재할당)
        public int Priority => 66;
        public float UpdateInterval => 5.0f;

        public void OnCreate() { }

        public void OnFixedUpdate(float inFixedDeltaTime)
        {
            float now = AR.s.Time.CurrentGameTime;

            // Phase 1a: 진행시간 누적 (자가 건설 — 게임시간 차분만큼 무조건 진행).
            // UpdateInterval(5초)마다 한 번 호출되므로, 게임시간 기준 dt를 직접 계산해 누적.
            //   AR.s.Time.CurrentGameTime은 게임시간(h)이므로 마지막 호출 이후 경과를 추적.
            //   AccumulatedHours는 게임시간 단위로 직접 다룬다 (StartedAt과 같은 단위).
            UpdateAccumulatedHours(now);

            // Phase 1b: 진행 중 task entity 풀 순회 → 완료된 task 처리
            // 뒤에서부터 순회 (Finish가 풀에서 제거하므로 인덱스 보존)
            SparseSet<ObjectPlacementTaskComponent> taskPool = AR.s.Component.GetComponentPool<ObjectPlacementTaskComponent>();
            for (int i = taskPool.Count - 1; i >= 0; i--)
            {
                int taskEntityId = taskPool.GetEntityId(i);
                ObjectPlacementTaskComponent task = taskPool.GetByIndex(i);
                if (task.AccumulatedHours < task.BuildDurationHours) continue;
                TryFinishAsync(task, taskEntityId).Forget();
            }

            // Phase 2: 마을마다 한 번에 건물 하나씩만 건설 (선택 A 간이 밸런스).
            // 과거엔 동시 한도 = Population(1~25)이라 실체화 시 한 틱에 우르르 착공됐다.
            // v.Population이 물리 NPC 수와 분리되어 커지므로, 착공은 1개로 고정한다.
            var villages = AR.s.Village.GetAllVillages();
            foreach (VillageData v in villages)
            {
                if (v.Population < 1) continue;
                if (v.EntityId < 0) continue;

                int activeCount = CountActiveTasksForVillage(v.VillageId);
                const int maxConcurrent = 1;

                if (activeCount < maxConcurrent)
                    TryStartNextTask(v, now);
            }
        }

        // 마지막 누적 처리 시점 (게임시간 h). 0이면 아직 미초기화.
        private float _lastAccumGameTime = -1f;

        /// <summary>
        /// 모든 진행 중 task에 대해 진행도를 갱신하고 HP/AccumulatedHours를 양방향 동기화한다.
        /// 1) Pull: 빌딩 엔티티가 있고 외부 데미지로 CurrentHp가 떨어져 진행도보다 낮으면 AccumulatedHours를 끌어내림
        /// 2) Push (자가 건설): dt만큼 AccumulatedHours += → CurrentHp = (Acc/Duration)*MaxHp 로 빌딩에 반영
        /// 3) HpBarView 갱신: DamageMessage 발송으로 fillAmount 자동 갱신
        /// </summary>
        private void UpdateAccumulatedHours(float now)
        {
            if (_lastAccumGameTime < 0f)
            {
                _lastAccumGameTime = now;
                return;
            }

            float dtHours = now - _lastAccumGameTime;
            _lastAccumGameTime = now;
            if (dtHours < 0f) dtHours = 0f;

            ComponentManager cm = AR.s.Component;
            SparseSet<ObjectPlacementTaskComponent> taskPool = cm.GetComponentPool<ObjectPlacementTaskComponent>();
            for (int i = 0; i < taskPool.Count; i++)
            {
                ObjectPlacementTaskComponent task = taskPool.GetByIndex(i);
                int taskEntityId = taskPool.GetEntityId(i);
                bool dirty = false;

                // 빌딩 상태 조회 — out 변수가 분기 밖에서 사용 가능하도록 명시 분리
                int buildingId = task.BuildingEntityId;
                bool hasBuildingComp = false;
                bool hasStatComp = false;
                BuildingComponent bc = default;
                StatComponent stat = default;
                if (buildingId >= 0)
                {
                    hasBuildingComp = cm.TryGetComponent<BuildingComponent>(buildingId, out bc);
                    hasStatComp = cm.TryGetComponent<StatComponent>(buildingId, out stat);
                }
                bool hasBuilding = hasBuildingComp && hasStatComp;

                Tables.BuildableItemTable? table = AR.s.Data.GetBuildableItem(task.TargetTableId);
                int maxHp = (table != null && table.HP > 0) ? table.HP : 1;
                float duration = Mathf.Max(0.0001f, task.BuildDurationHours);

                // 1) Pull — 외부 데미지로 CurrentHp가 떨어졌으면 AccumulatedHours를 끌어내림
                if (hasBuilding)
                {
                    int curHp = stat.CurrentHp;
                    float expectedHp = task.AccumulatedHours / duration * maxHp;
                    if (curHp < expectedHp - 0.5f)
                    {
                        float newAcc = (float)curHp / maxHp * task.BuildDurationHours;
                        if (newAcc < 0f) newAcc = 0f;
                        task.AccumulatedHours = newAcc;
                        dirty = true;
                    }
                }

                // 2) Push — 자가 건설: 게임시간 경과분만큼 무조건 누적 (D4, NPC 배정 없음)
                if (dtHours > 0f)
                {
                    task.AccumulatedHours += dtHours;
                    if (task.AccumulatedHours > task.BuildDurationHours)
                        task.AccumulatedHours = task.BuildDurationHours;
                    dirty = true;
                }

                // 3) HP 반영 + UI 갱신
                if (hasBuilding)
                {
                    int newHp = Mathf.RoundToInt(task.AccumulatedHours / duration * maxHp);
                    if (newHp < 0) newHp = 0;
                    if (newHp > maxHp) newHp = maxHp;

                    if (newHp != stat.CurrentHp)
                    {
                        stat.SetCurrentHpDirect(newHp);
                        cm.SetComponent(buildingId, stat);

                        // BuildingComponent의 CurrentHp도 동기화 (세이브/외부 참조용)
                        bc.CurrentHp = newHp;
                        cm.SetComponent(buildingId, bc);

                        // HpBarView가 fillAmount를 갱신하도록 0 데미지 메시지 발송
                        AR.s.Message.SendToEntity(new ARPG.Message.DamageMessage
                        {
                            TargetEntityId = buildingId,
                            DamageAmount = 0,
                            AttackerEntityId = -1,
                            DamageType = GlobalEnum.DamageType.Physics,
                            CurrentHp = newHp,
                            MaxHp = maxHp,
                        });
                    }
                }

                if (dirty)
                    cm.SetComponent(taskEntityId, task);
            }
        }

        /// <summary>
        /// 해당 마을 소속 진행 중 task 수 카운트. Step B: Pop과 비교해 동시성 제한에 사용.
        /// </summary>
        private static int CountActiveTasksForVillage(int villageId)
        {
            SparseSet<ObjectPlacementTaskComponent> pool = AR.s.Component.GetComponentPool<ObjectPlacementTaskComponent>();
            int count = 0;
            for (int i = 0; i < pool.Count; i++)
            {
                if (pool.GetByIndex(i).VillageId == villageId) count++;
            }
            return count;
        }

        /// <summary>
        /// 마을의 다음 task 1개를 시도한다. true=시작 성공, false=시작 안 됨(자원 부족·후보 없음·자리 없음 등).
        /// 호출자(OnFixedUpdate)는 이 반환값으로 while 루프 종료 여부를 결정한다.
        /// </summary>
        private bool TryStartNextTask(VillageData v, float now)
        {
            // 다음 미완성 벽 세그먼트 조회 — Gate면 절대 우선, 일반 Palisade는 fallback
            WallSegmentSaveData? wallSeg = WallSegmentRegistry.GetNextUnbuiltSegment(v);
            bool wallIsGate = wallSeg != null && wallSeg.Orient == (int)WallOrientation.Gate;
            if (wallSeg != null && wallIsGate)
                return TryStartWallTask(v, wallSeg, now);

            // BUILD_PRIORITY_DESIGN.md §3 — 점수 우선순위 절대 존중.
            //  • 1위가 자원 부족 → 다른 후보로 우회하지 않고 자원이 모일 때까지 대기
            //  • 1위가 자리 없음(영구 블로커) → 2위, 3위 순으로 시도
            //  • 모든 후보 자리 없음 → 벽 fallback
            var ranked = VillageNeedsEvaluator.GetRankedCandidates(v);
            for (int i = 0; i < ranked.Count; i++)
            {
                BuildAttemptResult result = TryStartGeneralBuild(v, ranked[i], now);
                if (result == BuildAttemptResult.Started) return true;
                if (result == BuildAttemptResult.WaitForResources) return false;
                // BuildAttemptResult.NoTileOrTableMissing → 다음 후보 시도
            }

            // 모든 일반 후보 자리 없음 / 테이블 누락 → 벽 천천히 진행
            if (wallSeg != null)
                return TryStartWallTask(v, wallSeg, now);
            return false;
        }

        private enum BuildAttemptResult
        {
            Started,                  // 빌드 시작 성공
            WaitForResources,         // 자원 부족 — 시간이 지나면 해소되므로 대기
            NoTileOrTableMissing,     // 자리 없음 / 테이블 누락 — 영구 블로커, 다음 후보 시도
        }

        /// <summary>
        /// 일반 건물 1개 빌드 시도.
        /// BuildHours는 BuildableItemTable.BuildHours에서 직접 읽음.
        /// </summary>
        private BuildAttemptResult TryStartGeneralBuild(VillageData v, int targetTableId, float now)
        {
            Tables.BuildableItemTable? table = AR.s.Data.GetBuildableItem(targetTableId);
            if (table == null) return BuildAttemptResult.NoTileOrTableMissing;

            int wood = AR.s.Village.GetResourceAmount(v.VillageId, GlobalEnum.ItemType.Wood);
            int stone = AR.s.Village.GetResourceAmount(v.VillageId, GlobalEnum.ItemType.Stone);
            if (wood < table.Cost_Wood || stone < table.Cost_Stone)
                return BuildAttemptResult.WaitForResources;

            // 빈 타일 탐색 — 광장/큰길 예약 + 카테고리 minSep + 거리 차등 클러스터
            Vector2Int center = new Vector2Int(
                Mathf.FloorToInt(v.PositionX),
                Mathf.FloorToInt(v.PositionY)
            );
            BuildableCategory category = table.Category;
            int boundsRadius = VillageManager.GetBoundsRadius(v.Stage);
            int maxRadius = boundsRadius > 0
                ? Mathf.Max(DEFAULT_MAX_RADIUS, boundsRadius - VillageTileFinder.OUTSKIRT_MARGIN_TILES)
                : DEFAULT_MAX_RADIUS;

            (int roadRadius, int roadHalfWidth) = GetRoadReserve(v.Stage);
            VillageTileFinder.SetRoadReserve(roadRadius, roadHalfWidth);
            VillageTileFinder.SetPlazaRadius(GetPlazaRadius(v.Stage));

            Vector2Int? tile = VillageTileFinder.FindEmptyTileNearest(
                center, maxRadius, v.VillageId, category, boundsRadius, table.MinSeparation);
            if (tile.HasValue == false) return BuildAttemptResult.NoTileOrTableMissing;  // 자리 없음 → 다음 후보 시도

            // 자원 차감 (Wood/Stone 둘 중 하나라도 실패 시 환불). 동시성 race 시에만 발생.
            if (table.Cost_Wood > 0
                && AR.s.Village.ConsumeResource(v.VillageId, GlobalEnum.ItemType.Wood, table.Cost_Wood) == false)
                return BuildAttemptResult.WaitForResources;
            if (table.Cost_Stone > 0
                && AR.s.Village.ConsumeResource(v.VillageId, GlobalEnum.ItemType.Stone, table.Cost_Stone) == false)
            {
                if (table.Cost_Wood > 0)
                    AR.s.Village.ProduceResource(v.VillageId, GlobalEnum.ItemType.Wood, table.Cost_Wood);
                return BuildAttemptResult.WaitForResources;
            }

            float buildHours = table.BuildHours > 0f ? table.BuildHours : 2f;

            // 즉시 건설중 빌딩 엔티티 생성 (SpawnType=Entity 한정).
            //  - 진행도 = CurrentHp(0 → table.HP), 데미지 받으면 진행 후퇴.
            //  - 벽(SpawnType=Tile)은 placeholder 엔티티 만들지 않고 완료 시 한 번에 타일 그리는 기존 흐름 유지.
            int buildingEntityId = -1;
            if (table.SpawnType == GlobalEnum.BuildableSpawnType.Entity)
            {
                if (AR.s.Map.PlaceObject(tile.Value.x, tile.Value.y, targetTableId,
                                         isUnderConstruction: true, out buildingEntityId) == false)
                {
                    // 배치 실패 → 자원 환불 후 다른 후보로 넘어가도록 표시
                    if (table.Cost_Wood > 0)
                        AR.s.Village.ProduceResource(v.VillageId, GlobalEnum.ItemType.Wood, table.Cost_Wood);
                    if (table.Cost_Stone > 0)
                        AR.s.Village.ProduceResource(v.VillageId, GlobalEnum.ItemType.Stone, table.Cost_Stone);
                    return BuildAttemptResult.NoTileOrTableMissing;
                }
            }

            // Step A: task를 별도 entity에 부착 — N개 동시 task 확장의 발판.
            // 자가 건설(D4): NPC 배정 없이 타이머로만 진행.
            int taskEntityId = AR.s.Entity.CreateEntity();
            ObjectPlacementTaskComponent task = new ObjectPlacementTaskComponent
            {
                VillageId = v.VillageId,
                TargetTableId = targetTableId,
                TileX = tile.Value.x,
                TileY = tile.Value.y,
                StartedAt = now,
                AccumulatedHours = 0f,
                BuildDurationHours = buildHours,
                ReservedWoodCost = table.Cost_Wood,
                ReservedStoneCost = table.Cost_Stone,
                BuildingEntityId = buildingEntityId,
            };
            AR.s.Component.AddComponent(taskEntityId, task);

            string buildingNote = buildingEntityId >= 0 ? $", building={buildingEntityId}" : "";
            Debug.Log($"[BuildQueue] v{v.VillageId} 착수 '{table.Name}' (taskEntity={taskEntityId}{buildingNote}): Wood -{table.Cost_Wood}, Stone -{table.Cost_Stone}, tile=({tile.Value.x},{tile.Value.y}), 필요={buildHours:F1}h");
            return BuildAttemptResult.Started;
        }

        private async UniTask TryFinishAsync(ObjectPlacementTaskComponent task, int taskEntityId)
        {
            // 중복 방어: 즉시 컴포넌트 제거 + entity 폐기 (재활용 풀로)
            AR.s.Component.RemoveComponent<ObjectPlacementTaskComponent>(taskEntityId);
            AR.s.Entity.DestroyEntity(taskEntityId);

            // task에는 VillageId만 있으므로 VillageData를 다시 조회
            VillageData? v = AR.s.Village.GetVillage(task.VillageId);
            if (v == null) return;

            Tables.BuildableItemTable? table = AR.s.Data.GetBuildableItem(task.TargetTableId);
            if (table == null)
            {
                RefundReserved(v, task);
                return;
            }

            // 벽(SpawnType=Tile)은 placeholder 엔티티 없이 완료 시 한 번에 타일 그림 — 기존 흐름 유지
            if (table.SpawnType == GlobalEnum.BuildableSpawnType.Tile)
            {
                await BuildableTileRegistry.EnsureLoadedAsync(task.TargetTableId);
                bool placed = AR.s.Map.PlaceObject(task.TileX, task.TileY, task.TargetTableId);
                if (placed == false)
                {
                    RefundReserved(v, task);
                    Debug.LogWarning($"[BuildQueue] v{v.VillageId} '{table.Name}' 타일 배치 실패, Wood +{task.ReservedWoodCost} Stone +{task.ReservedStoneCost} 환불");
                    return;
                }

                if (IsWallTask(task.TargetTableId))
                {
                    OnWallSegmentCompleted(v, task);
                    Debug.Log($"[BuildQueue] v{v.VillageId} 벽 '{table.Name}' 완성 at ({task.TileX},{task.TileY})");
                    return;
                }

                v.PlacedObjectTypeIds.Add(task.TargetTableId);
                AR.s.Village.OnObjectPlaced(v.VillageId, task.TargetTableId, task.TileX, task.TileY);
                Debug.Log($"[BuildQueue] v{v.VillageId} '{table.Name}' 완성 at ({task.TileX},{task.TileY}) [tile]");
                return;
            }

            // Entity 빌딩: 시작 시 만든 건설중 엔티티를 완성 상태로 전환 (스프라이트/HP/IsUnderConstruction).
            int buildingId = task.BuildingEntityId;
            if (buildingId < 0)
            {
                // 안전망 — BuildingEntityId 없는 비정상 상태. 새로 PlaceObject로 만들어줌.
                Debug.LogWarning($"[BuildQueue] v{v.VillageId} '{table.Name}' BuildingEntityId 없음, fallback 으로 새 빌딩 생성");
                bool placed = AR.s.Map.PlaceObject(task.TileX, task.TileY, task.TargetTableId);
                if (placed == false)
                {
                    RefundReserved(v, task);
                    return;
                }
            }
            else
            {
                FinalizeUnderConstructionBuilding(buildingId, table);
            }

            v.PlacedObjectTypeIds.Add(task.TargetTableId);
            AR.s.Village.OnObjectPlaced(v.VillageId, task.TargetTableId, task.TileX, task.TileY);

            // Phase A 호환 플래그 (UI/타 시스템이 아직 참조할 수 있어 유지)
            if (task.TargetTableId == VillageNeedsEvaluator.CAMPFIRE_TABLE_ID)
                v.HasCampfire = true;

            Debug.Log($"[BuildQueue] v{v.VillageId} '{table.Name}' 완성 at ({task.TileX},{task.TileY}) [entity={buildingId}]");
        }

        /// <summary>
        /// 건설중 빌딩 엔티티를 완성 상태로 전환.
        /// 스프라이트 교체 + IsUnderConstruction=false + HP=MaxHp + HpBarView 갱신.
        /// (StatComponent는 그대로 유지 — 이후 데미지 받으면 HP바가 다시 표시되도록)
        /// </summary>
        private static void FinalizeUnderConstructionBuilding(int buildingId, Tables.BuildableItemTable table)
        {
            ComponentManager cm = AR.s.Component;

            // BuildingComponent: IsUnderConstruction=false, CurrentHp=table.HP
            if (cm.TryGetComponent<BuildingComponent>(buildingId, out var bc))
            {
                bc.IsUnderConstruction = false;
                bc.CurrentHp = table.HP;
                cm.SetComponent(buildingId, bc);
            }

            // StatComponent: CurrentHp = MaxHp (만피 → HP바 자동 숨김)
            if (cm.TryGetComponent<StatComponent>(buildingId, out var stat))
            {
                stat.SetCurrentHpDirect(table.HP);
                cm.SetComponent(buildingId, stat);
            }

            // BuildingSaveData도 동기화 (청크 비활성화 시 제대로 저장되도록)
            if (AR.s.Building != null)
            {
                BuildingSaveData? saveData = AR.s.Building.GetSaveData(buildingId);
                if (saveData != null)
                {
                    saveData.IsUnderConstruction = false;
                    saveData.CurrentHp = table.HP;
                }
            }

            // 스프라이트 교체 — placeholder → 최종
            if (AR.s.Message.TryGetEntity(buildingId, out var entity) && entity != null)
            {
                if (table.AnimationId == 0)
                {
                    SwapToFinalSprite(entity, table.ResourceName).Forget();
                }
                else
                {
                    SwapToFinalAnimation(buildingId, entity, table.AnimationId);
                }
            }

            // HpBarView 갱신 → fillAmount=1.0 → 만피 숨김 로직 적용
            AR.s.Message.SendToEntity(new ARPG.Message.DamageMessage
            {
                TargetEntityId = buildingId,
                DamageAmount = 0,
                AttackerEntityId = -1,
                DamageType = GlobalEnum.DamageType.Physics,
                CurrentHp = table.HP,
                MaxHp = table.HP,
            });
        }

        private static async UniTask SwapToFinalSprite(ARPG.Base.EntityBase entity, string resourceName)
        {
            if (string.IsNullOrEmpty(resourceName)) return;
            try
            {
                Sprite sprite = await UnityEngine.AddressableAssets.Addressables
                    .LoadAssetAsync<Sprite>(resourceName).ToUniTask();
                if (sprite != null && entity != null && entity.SpriteRenderer != null)
                    entity.SpriteRenderer.sprite = sprite;
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[BuildQueue] 최종 스프라이트 로드 실패 '{resourceName}': {e.Message}");
            }
        }

        private static void SwapToFinalAnimation(int buildingId, ARPG.Base.EntityBase entity, int animationId)
        {
            // BuildingFactory의 SetupAnimatedSprite와 동등 — 진행중에는 정적 스프라이트만 썼으므로 새로 부착.
            // 동일 함수가 internal이라 호출 불가 → SpriteAnimationComponent 추가만 직접 수행, System_Animation이 처리.
            ARPG.Tables.AnimationTable? animTable = AR.s.Data.GetAnimation(animationId);
            if (animTable == null) return;

            AR.s.Component.AddComponent(buildingId, new SpriteAnimationComponent
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
            AR.s.Component.AddComponent(buildingId, new AnimatorComponent());
            // SpriteLibrary 로드는 System_Animation이 LoadState=None 보고 처리하길 기대.
            // 만약 그렇지 않다면 BuildingFactory의 비공개 헬퍼를 internal로 노출하거나 동일 로직을 여기 복제 필요.
        }

        private static void RefundReserved(VillageData v, ObjectPlacementTaskComponent task)
        {
            if (task.ReservedWoodCost > 0)
                AR.s.Village.ProduceResource(v.VillageId, GlobalEnum.ItemType.Wood, task.ReservedWoodCost);
            if (task.ReservedStoneCost > 0)
                AR.s.Village.ProduceResource(v.VillageId, GlobalEnum.ItemType.Stone, task.ReservedStoneCost);
        }

        // ========== Phase C: 벽 세그먼트 처리 ==========

        // VILLAGE_PHASES.md (Phase C 벽 인프라): 벽 1칸 건설 시간 0.5h (실시간 ~15초)
        private const float PALISADE_BUILD_HOURS = 0.5f;
        private const int PALISADE_TABLE_ID = 180;
        private const int PALISADE_GATE_TABLE_ID = 181;

        /// <summary>
        /// 미완성 벽 세그먼트를 ObjectPlacementTaskComponent로 큐잉.
        /// 자원/타일 체크 — Cost_Wood만 사용 (Palisade=8, Gate=40).
        /// 반환: true=task entity 생성됨, false=실패(자원/타일/테이블).
        /// </summary>
        private bool TryStartWallTask(VillageData v, WallSegmentSaveData seg, float now)
        {
            int tableId = (seg.Orient == (int)WallOrientation.Gate) ? PALISADE_GATE_TABLE_ID : PALISADE_TABLE_ID;
            Tables.BuildableItemTable? table = AR.s.Data.GetBuildableItem(tableId);
            if (table == null) return false;

            int wood = AR.s.Village.GetResourceAmount(v.VillageId, GlobalEnum.ItemType.Wood);
            if (wood < table.Cost_Wood) return false;

            // 타일이 비어있는지 확인 (다른 오브젝트가 점유했으면 스킵)
            if (AR.s.Map.GetObjectIdAt(seg.TileX, seg.TileY) != 0)
            {
                // 이미 무언가 있음 — 벽 못 세움. SegmentId가 있으니 재시도 안 함, IsBuilt=true로 마킹
                Debug.LogWarning($"[WallPlanner] v{v.VillageId} seg{seg.SegmentId} 위치 ({seg.TileX},{seg.TileY}) 점유됨, 스킵");
                WallSegmentRegistry.MarkSegmentBuilt(v, seg.SegmentId);
                return false;
            }

            // 자원 차감
            if (table.Cost_Wood > 0
                && AR.s.Village.ConsumeResource(v.VillageId, GlobalEnum.ItemType.Wood, table.Cost_Wood) == false)
                return false;

            // 자가 건설(D4): NPC 배정 없이 타이머로만 진행
            int taskEntityId = AR.s.Entity.CreateEntity();
            ObjectPlacementTaskComponent task = new ObjectPlacementTaskComponent
            {
                VillageId = v.VillageId,
                TargetTableId = tableId,
                TileX = seg.TileX,
                TileY = seg.TileY,
                StartedAt = now,
                AccumulatedHours = 0f,
                BuildDurationHours = PALISADE_BUILD_HOURS,
                ReservedWoodCost = table.Cost_Wood,
                ReservedStoneCost = 0,
                BuildingEntityId = -1,
            };
            AR.s.Component.AddComponent(taskEntityId, task);

            Debug.Log($"[BuildQueue] v{v.VillageId} 벽 착수 seg{seg.SegmentId} '{table.Name}' (taskEntity={taskEntityId}) tile=({seg.TileX},{seg.TileY})");
            return true;
        }

        /// <summary>현재 task가 벽 세그먼트인지 판정 — TableId로 분기.</summary>
        private static bool IsWallTask(int tableId)
        {
            return tableId == PALISADE_TABLE_ID || tableId == PALISADE_GATE_TABLE_ID;
        }

        /// <summary>벽 완성 시 처리 — PlacedObjectTypeIds에 추가하지 않고 WallSegments 마킹.</summary>
        private static void OnWallSegmentCompleted(VillageData v, ObjectPlacementTaskComponent task)
        {
            // 좌표로 세그먼트 찾기 (SegmentId를 task에 안 박았으므로 좌표 매칭)
            if (v.WallSegments == null) return;
            for (int i = 0; i < v.WallSegments.Count; i++)
            {
                var seg = v.WallSegments[i];
                if (seg.IsBuilt == false && seg.TileX == task.TileX && seg.TileY == task.TileY)
                {
                    WallSegmentRegistry.MarkSegmentBuilt(v, seg.SegmentId);
                    UpdateVillageWallStats(v);
                    return;
                }
            }
        }

        private static void UpdateVillageWallStats(VillageData v)
        {
            if (AR.s.Component.TryGetComponent<VillageComponent>(v.EntityId, out var vc))
            {
                vc.CompletedWallSegments = v.CompletedWallSegments;
                AR.s.Component.SetComponent(v.EntityId, vc);
            }

            // 10% 단위 진행률 로그
            int total = v.WallSegmentCount;
            int built = v.CompletedWallSegments;
            if (total <= 0) return;
            int pct = built * 100 / total;
            int prevPct = (built - 1) * 100 / total;
            if (pct / 10 != prevPct / 10)
                Debug.Log($"[WallPlanner] v{v.VillageId} 벽 {built}/{total} ({pct}%)");
        }

        /// <summary>
        /// Stage별 큰길 예약 (radius, halfWidth) — VillageStageTable에서 조회.
        /// Settlement는 비활성(0,0). Hamlet+은 폭 3타일(±1)로 통로 시각화.
        /// </summary>
        private static (int radius, int halfWidth) GetRoadReserve(VillageStage stage)
        {
            Tables.VillageStageTable? t = AR.s.Data.GetVillageStage(stage);
            if (t == null) return (0, 0);
            return (t.RoadReserveRadius, t.RoadReserveHalfWidth);
        }

        /// <summary>
        /// Stage별 마을 중심 광장 반경 — VillageStageTable에서 조회.
        /// 0=없음, 1=3×3, 2=5×5. 큰길 예약과 겹치지만 시각/판정상 무해.
        /// </summary>
        private static int GetPlazaRadius(VillageStage stage)
        {
            Tables.VillageStageTable? t = AR.s.Data.GetVillageStage(stage);
            return t != null ? t.PlazaRadius : 0;
        }

        public void OnReset()
        {
            _lastAccumGameTime = -1f;
        }
    }
}
