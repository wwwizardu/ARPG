#nullable enable
using System.Collections.Generic;
using ARPG.Component;
using ARPG.Map;
using ARPG.Utility;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace ARPG.Village
{
    public class VillageManager : MonoBehaviour
    {
        private readonly Dictionary<int, VillageData> _villages = new();
        // Dictionary는 VillageId 조회, List는 시스템의 인덱스 기반 시간분할 순회에 사용한다.
        // 등록/초기화는 AddVillage/ClearVillages에서 함께 처리해 두 컬렉션을 동기화한다.
        private readonly List<VillageData> _villageList = new();
        // PlacedObject 조회 인덱스 — 휘발성(세이브 아님). static 대신 매니저 소유, Reset/Load에서 수명 관리.
        private readonly PlacedObjectRegistry _placedObjects = new();
        private readonly HashSet<string> _placementWarnings = new();
        private readonly HashSet<string> _profileWarnings = new();
        private const int DEFAULT_PLACEMENT_RADIUS = 3;
        public PlacedObjectRegistry PlacedObjects => _placedObjects;

        public void Initialize()
        {
            if (_villageList.Count > 0)
                return;

            List<MapFileData> villageMaps = AR.s.Map.GetMapFileDataByType(MapType.Village);
            for (int i = 0; i < villageMaps.Count; i++)
            {
                MapFileData mapFileData = villageMaps[i];
                Vector2 villageCenter = new Vector2(
                    mapFileData.StartPosition.x + mapFileData.Width * 0.5f,
                    mapFileData.StartPosition.y + mapFileData.Height * 0.5f
                );
                // 마을 인덱스 + 1 을 선호 VillageTableId로 삼되, 존재하지 않으면 유효 행으로 폴백.
                // 마을 수 > 테이블 행 수라서 대부분 마을이 단일 기존 테이블로 라우팅된다.
                int tableId = AR.s.Data.ResolveVillageTableId(i + 1);
                RegisterVillage(i, villageCenter, tableId);

                // R1: 절차 정착지 청사진이면 연대기 SettlementId 연결 (손으로 만든 BaseTown 등은 -1 유지)
                int settlementId = AR.s.Map.GetSettlementIdByOrigin(mapFileData.StartPosition);
                if (settlementId >= 0)
                    LinkSettlement(i, settlementId);
                else
                {
                    VillageData? baseTown = GetVillage(i);
                    if (baseTown != null)
                        EnsureVillageProfile(baseTown, false);
                    MaterializeVillage(i);   // 손제작 마을(연대기 미연결) → 항상 활성 (실체화 유지)
                }

                // 연대기 연결 마을은 추상 상태로 남는다 — 근처가 되면 UpdateMaterialization이 실체화.
                Debug.Log($"[VillageManager] Initial village {i} registered at {villageCenter} (TableId={tableId}, SettlementId={settlementId})");
            }
        }

        public void Reset()
        {
            for (int i = 0; i < _villageList.Count; i++)
            {
                VillageData data = _villageList[i];
                if (data.EntityId >= 0)
                {
                    AR.s.Entity.DestroyEntity(data.EntityId, false);
                    data.EntityId = -1;
                }
            }
            ClearVillages();
            _placedObjects.ClearAll();
            _placementWarnings.Clear();
            _profileWarnings.Clear();
        }

        public void RegisterVillage(int villageId, Vector2 position, int tableId = 0)
        {
            if (_villages.ContainsKey(villageId))
            {
                Debug.LogWarning($"[VillageManager] Village {villageId} already registered");
                return;
            }

            VillageData data = new VillageData(villageId, position)
            {
                TableId = tableId,
                RegisteredAt = AR.s.Time.CurrentGameTime,
            };
            AddVillage(data);

            // 엔티티는 여기서 만들지 않는다 (런타임 LOD). VillageData만 등록하고,
            // 실체화는 근처가 될 때 MaterializeVillage(HistoryManager.UpdateMaterialization 경유)가 담당.
            // 손제작 마을(연대기 미연결)의 항상-활성 처리는 Initialize/Load에서 별도 수행.
        }

        // ── 정착지 연대기(SettlementId) ↔ 마을(VillageId) 매핑 (R1, PROCEDURAL §3.4) ──
        // 정본은 VillageData.SettlementId(세이브됨). 마을 수가 적어 조회는 선형 스캔으로 충분.

        /// <summary>마을 ↔ 정착지(연대기) 링크 설정. 정착지 실체화(materialize) 시 호출.</summary>
        public void LinkSettlement(int villageId, int settlementId)
        {
            if (_villages.TryGetValue(villageId, out VillageData? data))
                data.SettlementId = settlementId;
        }

        /// <summary>정착지 SettlementId로 연결된 마을 조회. 없으면 null.</summary>
        public VillageData? GetVillageBySettlement(int settlementId)
        {
            if (settlementId < 0)
                return null;
            for (int i = 0; i < _villageList.Count; i++)
            {
                VillageData v = _villageList[i];
                if (v.SettlementId == settlementId)
                    return v;
            }
            return null;
        }

        /// <summary>마을의 연대기 SettlementId. 미연결이면 -1.</summary>
        public int GetSettlementId(int villageId)
            => _villages.TryGetValue(villageId, out VillageData? data) ? data.SettlementId : -1;

        public VillageData? GetVillage(int villageId)
        {
            if (_villages.TryGetValue(villageId, out VillageData? data))
            {
                return data;
            }
            return null;
        }

        /// <summary>
        /// History 초기화 뒤 Profile이 비어 있는 신규 정착지만 결정론적으로 확정한다.
        /// 이미 저장된 유효 ProfileId는 Race/Biome/Fate가 달라져도 재계산하지 않는다.
        /// </summary>
        public void EnsureVillageProfiles()
        {
            int assigned = 0;
            for (int i = 0; i < _villageList.Count; i++)
            {
                if (EnsureVillageProfile(_villageList[i], false) == true)
                    assigned++;
            }

            if (assigned > 0)
                Debug.Log($"[VillageProfile] Assigned {assigned} missing village profiles.");
        }

        public bool TryGetVillageProfile(int villageId, out VillageProfile profile)
        {
            profile = null!;
            if (_villages.TryGetValue(villageId, out VillageData? data) == false)
                return false;
            return VillageProfileCatalog.TryGet(data.VillageProfileId, out profile);
        }

        public void ProduceResource(int villageId, GlobalEnum.ItemType type, int amount)
        {
            ProduceResource(villageId, type, amount, true);
        }

        /// <summary>
        /// 자원을 생산한다. 오프스크린 시뮬레이션은 플레이어 알림 없이 같은 저장/스토리지 동기화 경로를 사용한다.
        /// </summary>
        public void ProduceResource(int villageId, GlobalEnum.ItemType type, int amount, bool showNotify)
        {
            if (_villages.TryGetValue(villageId, out VillageData? data) == false)
                return;

            VillageResourceSimulation.Produce(data, type, amount);
            SyncStorageFromData(data);

            if (showNotify)
                AR.s.UI.SetNotify(BuildResourceStatusNotify(data));
        }

        public bool ConsumeResource(int villageId, GlobalEnum.ItemType type, int amount)
        {
            if (_villages.TryGetValue(villageId, out VillageData? data) == false)
                return false;

            if (VillageResourceSimulation.Consume(data, type, amount) == false)
                return false;

            SyncStorageFromData(data);

            AR.s.UI.SetNotify(BuildResourceStatusNotify(data));
            return true;
        }

        private static string BuildResourceStatusNotify(VillageData data)
        {
            int food = VillageResourceSimulation.GetAmount(data, GlobalEnum.ItemType.Food);
            int wood = VillageResourceSimulation.GetAmount(data, GlobalEnum.ItemType.Wood);
            int stone = VillageResourceSimulation.GetAmount(data, GlobalEnum.ItemType.Stone);
            int foodCap = VillageResourceSimulation.GetCap(data, GlobalEnum.ItemType.Food);
            int woodCap = VillageResourceSimulation.GetCap(data, GlobalEnum.ItemType.Wood);
            int stoneCap = VillageResourceSimulation.GetCap(data, GlobalEnum.ItemType.Stone);
            return $"마을 {data.VillageId} [{data.Stage}]: Food {food}/{foodCap}  Wood {wood}/{woodCap}  Stone {stone}/{stoneCap}";
        }

        public int GetResourceAmount(int villageId, GlobalEnum.ItemType type)
        {
            if (_villages.TryGetValue(villageId, out VillageData? data) == false)
                return 0;

            return VillageResourceSimulation.GetAmount(data, type);
        }

        public int GetResourceCap(int villageId, GlobalEnum.ItemType type)
        {
            if (_villages.TryGetValue(villageId, out VillageData? data) == false)
                return 0;

            return VillageResourceSimulation.GetCap(data, type);
        }

        public IReadOnlyList<VillageData> GetAllVillages()
        {
            return _villageList;
        }

        public int GetVillageCount()
        {
            return _villageList.Count;
        }

        /// <summary>
        /// 월드 좌표가 소속된 마을 Id 반환. SpawnRadius 이내에서 가장 가까운 마을 선택.
        /// 해당 범위 내 마을이 없으면 -1.
        /// </summary>
        public int FindVillageContaining(int worldX, int worldY)
        {
            float bestSqrDist = float.MaxValue;
            int bestId = -1;

            for (int i = 0; i < _villageList.Count; i++)
            {
                VillageData data = _villageList[i];
                Tables.VillageTable? table = AR.s.Data.GetVillageTable(data.TableId);
                float radius = table != null ? table.SpawnRadius : 0f;
                if (radius <= 0f)
                    continue;

                float dx = data.PositionX - worldX;
                float dy = data.PositionY - worldY;
                float sqr = dx * dx + dy * dy;

                if (sqr > radius * radius)
                    continue;

                if (sqr < bestSqrDist)
                {
                    bestSqrDist = sqr;
                    bestId = data.VillageId;
                }
            }

            return bestId;
        }

        public void RegisterNpcToVillage(int villageId, int npcEntityId)
        {
            if (_villages.TryGetValue(villageId, out VillageData? data) == false)
                return;

            if (data.NpcEntityIds.Contains(npcEntityId))
                return;

            // NpcEntityIds는 물리 모험가 명단(코호트/UI 전용) — 경제(v.Population)를 구동하지 않는다.
            // v.Population은 §0 추상 인구에서만 소싱 (RefreshPopulationFromAbstract).
            data.NpcEntityIds.Add(npcEntityId);
        }

        /// <summary>
        /// 영구 사망 또는 이주로 물리 모험가 명단에서 제외한다.
        /// 경제 인구(v.Population)는 추상 인구를 소싱하므로 이 호출로 바뀌지 않는다.
        /// </summary>
        public void UnregisterNpcFromVillage(int villageId, int npcEntityId)
        {
            if (_villages.TryGetValue(villageId, out VillageData? data) == false)
                return;

            data.NpcEntityIds.Remove(npcEntityId);
        }

        // ── §0 추상 인구 → 마을 경제 인구(v.Population) 소싱 (VILLAGE_NPC_BOUNDARY_DESIGN §2) ──
        // 정량화(pop/50·cap25)는 기존 NPC 스폰 수 캘리브레이션 재사용 — 생산/Tier 임계값 재튜닝 회피.
        public const int ABSTRACT_POP_DIVISOR = 50;  // 추상 인구 K명당 경제 인구 1
        public const int ABSTRACT_POP_CAP = VillageProfile.MAX_ECONOMIC_POPULATION;

        /// <summary>
        /// 추상 인구(수백~수천)를 마을 경제 단위로 정량화. pop&lt;=0이면 0 (경제 정지).
        /// raw 값을 그대로 쓰면 생산이 폭주하므로 반드시 이 정량화를 거칠 것 (§10 리스크).
        /// </summary>
        public static int QuantizeAbstractPopulation(int abstractPop)
        {
            if (abstractPop <= 0)
                return 0;
            return Mathf.Clamp(Mathf.RoundToInt(abstractPop / (float)ABSTRACT_POP_DIVISOR), 1, ABSTRACT_POP_CAP);
        }

        /// <summary>
        /// 모든 마을의 v.Population을 §0 추상 인구에서 재계산.
        /// System_SettlementSim이 매 tick 호출 (per-village 시스템들보다 먼저, Priority 50).
        /// 로드 직후 세이브된 Population도 이 경로로 덮어써 재계산된다 (§10 세이브 마이그레이션).
        /// </summary>
        public void RefreshAllPopulationsFromAbstract()
        {
            for (int i = 0; i < _villageList.Count; i++)
                RefreshPopulationFromAbstract(_villageList[i]);
        }

        private static void RefreshPopulationFromAbstract(VillageData v)
        {
            if (v.SettlementId >= 0)
            {
                int abstractPop = AR.s.History.GetSettlementPopulation(v.SettlementId);
                v.Population = QuantizeAbstractPopulation(abstractPop);
            }
            else
            {
                // 수제작 BaseTown은 물리 NPC 명단과 무관한 저장된 고정 경제 인구를 사용한다.
                VillageProfile profile = VillageProfileCatalog.BaseTownProfile;
                if (VillageProfileCatalog.TryGet(v.VillageProfileId,
                        out VillageProfile storedProfile) == true)
                {
                    profile = storedProfile;
                }
                v.FixedPopulation = VillageProfilePopulationPolicy
                    .ResolveBaseTownFixedPopulation(v.FixedPopulation, v.Population,
                        profile, out _);
                v.Population = v.FixedPopulation;
            }
        }

        public List<VillageData> Save()
        {
            return new List<VillageData>(_villageList);
        }

        public void Load(List<VillageData> villageDatas)
        {
            ClearVillages();
            _placementWarnings.Clear();
            _profileWarnings.Clear();

            if (villageDatas == null)
                return;

            int profileBackfills = 0;

            for (int i = 0; i < villageDatas.Count; i++)
            {
                VillageData data = villageDatas[i];

                // 하위 호환: TableId 필드 없이 저장된 구 세이브 → VillageId+1 관례로 자동 부여.
                // 이후 항상 유효 행으로 라우팅 — 구 세이브가 저장한 빈 tableId(i+1)도 기존 테이블로 폴백.
                if (data.TableId <= 0)
                    data.TableId = data.VillageId + 1;
                data.TableId = AR.s.Data.ResolveVillageTableId(data.TableId);

                if (data.ResourceCaps == null)
                    data.ResourceCaps = new Dictionary<GlobalEnum.ItemType, int>();
                if (data.ActiveBuildTasks == null)
                    data.ActiveBuildTasks = new List<BuildTaskSnapshot>();
                // V3 마이그레이션: 구 세이브는 마일스톤 원장이 없음 — 빈 목록으로 보정.
                // 마일스톤 backfill(기존 시설 인정·부족 시설 완공 원장 생성)은 V7 범위.
                if (data.AppliedGrowthMilestones == null)
                    data.AppliedGrowthMilestones = new List<string>();
                if (data.RegisteredAt <= 0f)
                    data.RegisteredAt = AR.s.Time.CurrentGameTime;

                bool profileWasMissing =
                    string.IsNullOrWhiteSpace(data.VillageProfileId);
                if (EnsureVillageProfile(data, true) == true
                    && profileWasMissing == true)
                {
                    profileBackfills++;
                }

                // Phase A: FirstBuildStartedAt 기본 -1 보정 (구 세이브는 0으로 역직렬화될 수 있음)
                if (data.HasCampfire == false && data.FirstBuildStartedAt == 0f)
                    data.FirstBuildStartedAt = -1f;

                // Phase B 마이그레이션
                if (data.PlacedObjectTypeIds == null)
                    data.PlacedObjectTypeIds = new List<int>();

                // HasCampfire == true → PlacedObjectTypeIds에 100 추가 (중복 방지)
                if (data.HasCampfire && data.PlacedObjectTypeIds.Contains(100) == false)
                    data.PlacedObjectTypeIds.Add(100);

                // FirstBuild* 진행 중이던 Campfire 태스크 → CurrentBuild* 필드로 승격
                if (data.CurrentBuildTableId == 0 && data.FirstBuildStartedAt >= 0f)
                {
                    data.CurrentBuildTableId = 100;
                    data.CurrentBuildStartedAt = data.FirstBuildStartedAt;
                    data.CurrentBuildTileX = data.FirstBuildTileX;
                    data.CurrentBuildTileY = data.FirstBuildTileY;
                    data.CurrentBuildReservedWood = 3;  // Phase A CAMPFIRE_WOOD_COST
                    data.CurrentBuildReservedStone = 0;
                }

                // Phase C 마이그레이션
                if (data.WallSegments == null)
                    data.WallSegments = new List<WallSegmentSaveData>();
                // Bounds가 0인 구 세이브 → CreateStorageEntity가 Stage 기준으로 자동 산출

                // Phase D 마이그레이션
                if (data.PlacedObjects == null)
                    data.PlacedObjects = new List<PlacedObjectSaveData>();
                if (data.MerchantStock == null)
                    data.MerchantStock = new List<MerchantStockEntry>();

                // 매물 ItemData 테이블 참조 복원 + 구 포맷(Item=null) 정리.
                // V2→V3 매물 스키마 변경: ItemTableId/SkillId/SkillEffectId → ItemData. 구 엔트리는 Item=null이라 폐기 후
                // EnsureMerchantStockFresh가 새 포맷으로 재롤한다.
                for (int s = data.MerchantStock.Count - 1; s >= 0; s--)
                {
                    MerchantStockEntry entry = data.MerchantStock[s];
                    if (entry == null || entry.Item == null)
                    {
                        data.MerchantStock.RemoveAt(s);
                        continue;
                    }
                    entry.Item.OnLoadCompleted();
                }

                MigratePlacedObjectsFromTypeIds(data);

                data.EntityId = -1;   // 런타임 LOD: 로드 직후엔 모두 추상 상태 (JsonIgnore라 이미 -1이지만 명시)
                AddVillage(data);

                // 손제작 마을(연대기 미연결)만 즉시 실체화 (항상 활성).
                // 연대기 연결 마을은 추상 상태로 로드 — 근처가 되면 UpdateMaterialization이 실체화한다.
                if (data.SettlementId < 0)
                    MaterializeVillage(data.VillageId);
            }

            if (profileBackfills > 0)
            {
                Debug.LogWarning($"[VillageProfile] Backfilled {profileBackfills} " +
                    "villages whose save data had no VillageProfileId.");
            }
            Debug.Log($"[VillageManager] Loaded {_villageList.Count} villages");
        }

        /// <summary>
        /// Phase D 마이그레이션: PlacedObjectTypeIds(ID-only 카운트)만 있는 구 세이브에서
        /// PlacedObjects(좌표/HP) 자동 재생성. 좌표는 마을 중심 주변 무작위 (정확한 복원 불가).
        /// PlacedObjects가 이미 비어있지 않으면 스킵 (정본 우선).
        /// </summary>
        private void MigratePlacedObjectsFromTypeIds(VillageData data)
        {
            if (data.PlacedObjects.Count > 0) return;            // 이미 정본 있음
            if (data.PlacedObjectTypeIds == null) return;
            if (data.PlacedObjectTypeIds.Count == 0) return;

            int regenerated = 0;
            for (int i = 0; i < data.PlacedObjectTypeIds.Count; i++)
            {
                int tableId = data.PlacedObjectTypeIds[i];
                Tables.BuildableItemTable? t = AR.s.Data.GetBuildableItem(tableId);
                if (t == null) continue;

                // Bounds 산출 — 아직 CreateStorageEntity 호출 전이므로 Stage 기준 직접 산출
                int radius = data.BoundsW > 0 ? data.BoundsW / 2 : GetBoundsRadius(data.Stage);
                int cx = Mathf.FloorToInt(data.PositionX);
                int cy = Mathf.FloorToInt(data.PositionY);
                int dx = Random.Range(-radius + 1, radius);
                int dy = Random.Range(-radius + 1, radius);

                data.PlacedObjects.Add(new PlacedObjectSaveData
                {
                    TableId = tableId,
                    TileX = cx + dx,
                    TileY = cy + dy,
                    Hp = t.HP,
                    MaxHp = t.HP,
                    LastUseGameTime = 0f,
                });
                regenerated++;
            }

            if (regenerated > 0)
                Debug.Log($"[Phase D Migration] v{data.VillageId} {regenerated}개 오브젝트 좌표 재배치");
        }

        /// <summary>
        /// 로드 후 호출. VillageData.PlacedObjects → ECS 엔티티 + PlacedObjectComponent 재구성 + Registry 등록.
        /// </summary>
        private void RestorePlacedObjectsFromData(VillageData data,
            bool pendingOnly = false)
        {
            if (data.PlacedObjects == null) return;

            for (int i = 0; i < data.PlacedObjects.Count; i++)
            {
                PlacedObjectSaveData saved = data.PlacedObjects[i];
                if (pendingOnly == true && saved.PlacementPending == false
                    && VillageConstructionPosition.IsAssigned(saved) == true)
                    continue;

                Tables.BuildableItemTable? t = AR.s.Data.GetBuildableItem(saved.TableId);
                if (t == null) continue;

                if (t.SpawnType == GlobalEnum.BuildableSpawnType.Entity)
                {
                    if (TryRestoreCompletedBuilding(data, saved, t, i) == false)
                        continue;
                }
                else if (VillageConstructionPosition.IsAssigned(saved) == false)
                {
                    saved.PlacementPending = true;
                    WarnPlacementOnce(data.VillageId, "completed", i,
                        $"Tile 오브젝트 {saved.TableId}의 미할당 복원은 지원되지 않아 보존");
                    continue;
                }

                saved.PlacementPending = false;
                CreatePlacedObjectRuntime(data, saved, t);
            }
        }

        private bool TryRestoreCompletedBuilding(VillageData data,
            PlacedObjectSaveData saved, Tables.BuildableItemTable table, int index)
        {
            if (VillageConstructionPosition.IsAssigned(saved) == false)
            {
                Vector2Int? tile = FindInitialPlacementTile(data, saved, table);
                if (tile.HasValue == false)
                {
                    saved.PlacementPending = true;
                    WarnPlacementOnce(data.VillageId, "completed", index,
                        $"완공 건물 {saved.TableId} 배치 타일 없음");
                    return false;
                }

                if (AR.s.Map.PlaceObject(tile.Value.x, tile.Value.y, saved.TableId,
                        isUnderConstruction: false, villageId: data.VillageId,
                        currentHp: table.HP, out int buildingId) == false)
                {
                    saved.PlacementPending = true;
                    WarnPlacementOnce(data.VillageId, "completed", index,
                        $"완공 건물 {saved.TableId} Map/Building 배치 실패");
                    return false;
                }

                saved.TileX = tile.Value.x;
                saved.TileY = tile.Value.y;
                saved.BuildingEntityId = buildingId;
                if (saved.FacilityTier > 1)
                    AR.s.Building.SetFacilityTier(buildingId, saved.FacilityTier);
                return true;
            }

            int existingId = saved.BuildingEntityId;
            if (existingId < 0 || AR.s.Building.GetSaveData(existingId) == null)
                existingId = AR.s.Building.FindBuildingAt(saved.TileX, saved.TileY,
                    saved.TableId);

            if (existingId >= 0)
            {
                saved.BuildingEntityId = existingId;
                BuildingSaveData? building = AR.s.Building.GetSaveData(existingId);
                if (building != null && building.IsUnderConstruction == true)
                    AR.s.Building.CompleteBuilding(existingId, table);
                return true;
            }

            if (AR.s.Building.IsTileOccupied(saved.TileX, saved.TileY)
                || AR.s.Map.PlaceObject(saved.TileX, saved.TileY, saved.TableId,
                    isUnderConstruction: false, villageId: data.VillageId,
                    currentHp: table.HP, out int restoredId) == false)
            {
                saved.PlacementPending = true;
                WarnPlacementOnce(data.VillageId, "completed", index,
                    $"완공 건물 {saved.TableId}의 저장 위치 복원 실패");
                return false;
            }

            saved.BuildingEntityId = restoredId;
            return true;
        }

        private void CreatePlacedObjectRuntime(VillageData data,
            PlacedObjectSaveData saved, Tables.BuildableItemTable table)
        {
            int entityId = AR.s.Entity.CreateEntity();
            AR.s.Component.AddComponent(entityId, new PlacedObjectComponent
            {
                VillageId = data.VillageId,
                TableId = saved.TableId,
                TileX = saved.TileX,
                TileY = saved.TileY,
                HP = saved.Hp > 0 ? saved.Hp : table.HP,
                MaxHP = saved.MaxHp > 0 ? saved.MaxHp : table.HP,
                Service = (ProvidedService)table.ProvidedService,
                SetMember = (SetMemberTag)table.SetMembership,
                UsingNpcEntityId = -1,
                LastUseGameTime = saved.LastUseGameTime,
                FacilityTier = saved.FacilityTier,
            });
            _placedObjects.Register(data.VillageId, entityId, saved.TableId,
                new Vector2Int(saved.TileX, saved.TileY));
        }

        /// <summary>
        /// V4 (VILLAGE_REWORK_PLAN §7.2): 초기 시설의 기본 배치기.
        /// 허용 목록 시설(Hub/Market/Inn/특수/Shrine)은 LayoutProfile 지정 슬롯 → 슬롯 주변
        /// 작은 반경 결정론 fallback 순서로 배치하고, 실패하면 null을 반환해 PlacementPending을
        /// 유지한다 — VillageTileFinder 전역 점수 탐색으로 우회하지 않는다.
        /// 카탈로그 밖 TableId(구 세이브 일반 오브젝트)만 기존 최근접 탐색을 계속 사용한다.
        /// </summary>
        private Vector2Int? FindInitialPlacementTile(VillageData data,
            PlacedObjectSaveData saved, Tables.BuildableItemTable table)
        {
            VillageLayoutProfile layout = ResolveLayout(data,
                out int primarySpecialFacilityId);
            if (VillageLayoutCatalog.TryGetSlotRole(saved.TableId,
                    primarySpecialFacilityId, out VillageLayoutSlotRole role) == false)
                return FindMaterializationTile(data, table);

            int centerX = Mathf.FloorToInt(data.PositionX);
            int centerY = Mathf.FloorToInt(data.PositionY);
            int boundsRadius = data.BoundsW > 0
                ? data.BoundsW / 2
                : GetBoundsRadius(data.Stage);

            bool IsFree(int x, int y)
            {
                return IsInitialFacilityTileFree(data, x, y, centerX, centerY,
                    boundsRadius);
            }

            if (VillageSlotPlacer.TryFindTile(centerX, centerY, layout, role,
                    IsFree, out int tileX, out int tileY) == true)
                return new Vector2Int(tileX, tileY);
            return null;
        }

        /// <summary>마을 Profile → Layout 해석. Profile 미확정이면 공통 기본 레이아웃.</summary>
        private VillageLayoutProfile ResolveLayout(VillageData data,
            out int primarySpecialFacilityId)
        {
            primarySpecialFacilityId = 0;
            if (VillageProfileCatalog.TryGet(data.VillageProfileId,
                    out VillageProfile profile) == true)
            {
                primarySpecialFacilityId = profile.PrimarySpecialFacilityId;
                return VillageLayoutCatalog.Resolve(profile.LayoutProfileId);
            }
            return VillageLayoutCatalog.DefaultLayout;
        }

        /// <summary>
        /// 초기 시설 배치 가능 판정: 맵 경계·통행·타일 오브젝트·Entity 건물 점유에 더해
        /// 마을 Bounds 내부(벽 링 안쪽)와 벽 세그먼트 타일(열린 Gate 포함)을 제외한다.
        /// </summary>
        private static bool IsInitialFacilityTileFree(VillageData data, int x, int y,
            int centerX, int centerY, int boundsRadius)
        {
            if (AR.s.Map.IsWorldTileInBounds(x, y) == false)
                return false;
            if (boundsRadius > 0)
            {
                int distFromCenter = Mathf.Max(Mathf.Abs(x - centerX),
                    Mathf.Abs(y - centerY));
                if (distFromCenter >= boundsRadius)
                    return false;   // 벽 링(== radius) 및 바깥 제외
            }
            if (VillageWallRules.FindBuiltSegmentAt(data, x, y) != null)
                return false;       // 열린 Gate처럼 타일이 비어 있어도 벽 자리 보존
            Vector3 world = new Vector3(x + 0.5f, y + 0.5f, 0f);
            if (AR.s.Map.IsWalkable(world) == false)
                return false;
            if (AR.s.Map.GetObjectIdAt(x, y) != 0)
                return false;
            if (AR.s.Building != null && AR.s.Building.IsTileOccupied(x, y) == true)
                return false;
            return true;
        }

        private static Vector2Int? FindMaterializationTile(VillageData data,
            Tables.BuildableItemTable table)
        {
            Vector2Int center = new Vector2Int(Mathf.FloorToInt(data.PositionX),
                Mathf.FloorToInt(data.PositionY));
            int boundsRadius = data.BoundsW > 0
                ? data.BoundsW / 2
                : GetBoundsRadius(data.Stage);
            int maxRadius = boundsRadius > 0
                ? Mathf.Max(DEFAULT_PLACEMENT_RADIUS,
                    boundsRadius - VillageTileFinder.OUTSKIRT_MARGIN_TILES)
                : DEFAULT_PLACEMENT_RADIUS;
            Tables.VillageStageTable? stage = AR.s.Data.GetVillageStage(data.Stage);
            VillageTileFinder.SetRoadReserve(stage != null ? stage.RoadReserveRadius : 0,
                stage != null ? stage.RoadReserveHalfWidth : 0);
            VillageTileFinder.SetPlazaRadius(stage != null ? stage.PlazaRadius : 0);
            return VillageTileFinder.FindEmptyTileNearest(center, maxRadius,
                data.VillageId, table.Category, boundsRadius, table.MinSeparation);
        }

        private void WarnPlacementOnce(int villageId, string kind, int index,
            string message)
        {
            string key = $"{villageId}:{kind}:{index}";
            if (_placementWarnings.Add(key) == true)
                Debug.LogWarning($"[VillageMaterialize] v{villageId} {message}; PlacementPending 재시도 예정");
        }

        // ========== 런타임 LOD: 마을 ECS 실체화/추상화 (materialize / fold-back) ==========
        // 정착지 연대기 층(HistoryManager.UpdateMaterialization)이 마을 중심 청크의 활성 여부로
        // 상승/하강 에지를 감지해 아래 두 메서드를 호출한다. 정본 상태는 VillageData가 항상 보유하며,
        // ECS 엔티티(스토리지/컴포넌트 + 진행중 task + 배치 오브젝트)만 근처일 때 생성되고 멀어지면 파괴된다.

        /// <summary>
        /// 마을을 ECS로 실체화 (추상→구체). 플레이어 근처가 될 때 호출.
        /// 스토리지/컴포넌트 엔티티 + 진행중 task + 배치 오브젝트를 VillageData 정본에서 복원한다.
        /// 멱등: 이미 실체화(EntityId>=0)면 no-op.
        /// </summary>
        public void MaterializeVillage(int villageId)
        {
            if (_villages.TryGetValue(villageId, out VillageData? data) == false)
                return;
            if (data.EntityId >= 0)
            {
                RetryPendingPlacements(villageId);
                return;
            }

            int completedCountBefore = data.PlacedObjectTypeIds.Count;
            int placedRecordCountBefore = data.PlacedObjects.Count;
            int foodCapBefore = VillageResourceSimulation.GetCap(data,
                GlobalEnum.ItemType.Food);
            int woodCapBefore = VillageResourceSimulation.GetCap(data,
                GlobalEnum.ItemType.Wood);
            int stoneCapBefore = VillageResourceSimulation.GetCap(data,
                GlobalEnum.ItemType.Stone);
            bool campfireBefore = data.HasCampfire;

            CreateStorageEntity(data);
            RestorePlacedObjectsFromData(data);
            RestoreTaskFromData(data);
            RestoreWallTilesFromData(data);

            if (data.PlacedObjectTypeIds.Count != completedCountBefore
                || data.PlacedObjects.Count != placedRecordCountBefore
                || VillageResourceSimulation.GetCap(data, GlobalEnum.ItemType.Food)
                    != foodCapBefore
                || VillageResourceSimulation.GetCap(data, GlobalEnum.ItemType.Wood)
                    != woodCapBefore
                || VillageResourceSimulation.GetCap(data, GlobalEnum.ItemType.Stone)
                    != stoneCapBefore
                || data.HasCampfire != campfireBefore)
                Debug.LogError($"[VillageMaterialize] v{villageId} 복원 중 완료 부작용이 중복 적용됨");
        }

        public void RetryPendingPlacements(int villageId)
        {
            if (_villages.TryGetValue(villageId, out VillageData? data) == false
                || data.EntityId < 0)
                return;
            RestorePlacedObjectsFromData(data, pendingOnly: true);
            RestoreTaskFromData(data, pendingOnly: true);
            RestoreWallTilesFromData(data);
        }

        // ========== V4: 기능성 벽 실체화·피해·Gate 개폐 (VILLAGE_REWORK_PLAN §7.5 1안) ==========

        /// <summary>
        /// 완공 벽 세그먼트(VillageData.WallSegments 정본)를 맵 타일로 복원한다.
        /// 멱등: 이미 같은 벽 타일이 있으면 건너뛰고, 다른 오브젝트가 점유했으면 보존한다.
        /// 열린 Gate는 타일을 배치하지 않는다(통행 가능 유지).
        /// </summary>
        private void RestoreWallTilesFromData(VillageData data)
        {
            if (data.WallSegments == null || data.WallSegments.Count == 0)
                return;
            RestoreWallTilesAsync(data.VillageId).Forget();
        }

        private async UniTask RestoreWallTilesAsync(int villageId)
        {
            // 타일 비주얼(RuleTile) 로드 후 배치 — BuildQueue의 Tile 완성 경로와 같은 순서.
            await BuildableTileRegistry.EnsureLoadedAsync(VillageWallRules.PalisadeTableId);
            await BuildableTileRegistry.EnsureLoadedAsync(VillageWallRules.PalisadeGateTableId);

            if (_villages.TryGetValue(villageId, out VillageData? data) == false
                || data.EntityId < 0 || data.WallSegments == null)
                return;   // 로드 중 dematerialize — 다음 실체화에서 재시도

            for (int i = 0; i < data.WallSegments.Count; i++)
            {
                WallSegmentSaveData segment = data.WallSegments[i];
                if (segment.IsBuilt == false)
                    continue;

                int tableId = VillageWallRules.GetTableId(segment);
                int existing = AR.s.Map.GetObjectIdAt(segment.TileX, segment.TileY);

                if (segment.IsOpen == true)
                {
                    // 열린 Gate: 닫힘 타일이 남아 있으면 제거해 통행 상태를 복원
                    if (existing == tableId)
                        AR.s.Map.RemoveObject(segment.TileX, segment.TileY);
                    continue;
                }

                if (existing == tableId)
                    continue;
                if (existing != 0)
                    continue;   // 다른 오브젝트 점유 — 기존 배치 보존 (중복 금지)
                AR.s.Map.PlaceObject(segment.TileX, segment.TileY, tableId);
            }
        }

        /// <summary>
        /// V4 Tile 피해 경로: 벽 세그먼트에 피해 적용. HP 0이면 세그먼트를 파괴 상태로 되돌리고
        /// 맵 타일(Blocked·비주얼)을 제거한다. 벽이 아닌 타일이면 false.
        /// </summary>
        public bool ApplyWallDamage(int villageId, int tileX, int tileY, int damage)
        {
            if (_villages.TryGetValue(villageId, out VillageData? data) == false)
                return false;
            if (VillageWallRules.ApplyWallDamage(data, tileX, tileY, damage,
                    out bool destroyed) == false)
                return false;

            if (destroyed == true)
            {
                AR.s.Map.RemoveObject(tileX, tileY);
                Debug.Log($"[VillageWall] v{villageId} 벽 파괴 at ({tileX},{tileY})");
            }
            SyncWallStatsToComponent(data);
            return true;
        }

        /// <summary>
        /// V4 Gate 개폐 상호작용: 열림 = 타일 제거(통행 가능), 닫힘 = Gate 타일 재배치(충돌 복원).
        /// 닫을 위치에 플레이어가 서 있으면 실패한다.
        /// </summary>
        public bool ToggleGate(int villageId, int segmentId)
        {
            if (_villages.TryGetValue(villageId, out VillageData? data) == false)
                return false;

            // 닫기 전 플레이어 위치 확인 (열림 → 닫힘 전환 예정인 세그먼트 조회)
            WallSegmentSaveData? current = null;
            if (data.WallSegments != null)
            {
                for (int i = 0; i < data.WallSegments.Count; i++)
                {
                    if (data.WallSegments[i].SegmentId == segmentId)
                    {
                        current = data.WallSegments[i];
                        break;
                    }
                }
            }
            if (current == null)
                return false;
            if (current.IsOpen == true && IsPlayerOnTile(current.TileX, current.TileY))
            {
                AR.s.UI.SetNotify("문 위에 서 있어 닫을 수 없습니다");
                return false;
            }

            if (VillageWallRules.TryToggleGate(data, segmentId,
                    out WallSegmentSaveData? gate) == false || gate == null)
                return false;

            if (gate.IsOpen == true)
            {
                if (AR.s.Map.GetObjectIdAt(gate.TileX, gate.TileY)
                        == VillageWallRules.PalisadeGateTableId)
                    AR.s.Map.RemoveObject(gate.TileX, gate.TileY);
                AR.s.UI.SetNotify("문을 열었습니다");
            }
            else
            {
                CloseGateTileAsync(gate.TileX, gate.TileY).Forget();
                AR.s.UI.SetNotify("문을 닫았습니다");
            }
            return true;
        }

        private static async UniTask CloseGateTileAsync(int tileX, int tileY)
        {
            await BuildableTileRegistry.EnsureLoadedAsync(VillageWallRules.PalisadeGateTableId);
            if (AR.s.Map.GetObjectIdAt(tileX, tileY) == 0)
                AR.s.Map.PlaceObject(tileX, tileY, VillageWallRules.PalisadeGateTableId);
        }

        private static bool IsPlayerOnTile(int tileX, int tileY)
        {
            int playerEntityId = AR.s.Data != null ? AR.s.Data.CurrentPlayerEntityId : -1;
            if (playerEntityId < 0)
                return false;
            if (AR.s.Component.TryGetComponent<TransformComponent>(playerEntityId,
                    out var tr) == false)
                return false;
            return Mathf.FloorToInt(tr.Position.x) == tileX
                && Mathf.FloorToInt(tr.Position.y) == tileY;
        }

        /// <summary>
        /// 벽 통계를 VillageComponent 미러에 동기화 — dematerialize flush가 컴포넌트 값을
        /// 정본으로 되돌려 쓰므로(FlushVillageEntityState) 피해·파괴 직후 반드시 맞춘다.
        /// </summary>
        private void SyncWallStatsToComponent(VillageData data)
        {
            if (data.EntityId < 0)
                return;
            if (AR.s.Component.TryGetComponent<VillageComponent>(data.EntityId, out var vc))
            {
                vc.WallSegmentCount = data.WallSegmentCount;
                vc.CompletedWallSegments = data.CompletedWallSegments;
                AR.s.Component.SetComponent(data.EntityId, vc);
            }
        }

        /// <summary>
        /// 마을을 추상으로 되돌림 (구체→추상). 플레이어에게서 멀어질 때 호출.
        /// ECS 상태(진행중 task / 배치 오브젝트 / 컴포넌트)를 VillageData 정본으로 flush한 뒤
        /// 관련 엔티티를 모두 파괴한다. 복원은 MaterializeVillage가 담당(정본 왕복).
        /// 멱등: 이미 추상(EntityId<0)면 no-op.
        /// </summary>
        public void DematerializeVillage(int villageId)
        {
            if (_villages.TryGetValue(villageId, out VillageData? data) == false)
                return;
            if (data.EntityId < 0)
                return;   // 이미 추상

            FlushAndDestroyTasks(data);
            FlushAndDestroyPlacedObjects(data);
            FlushVillageEntityState(data);

            AR.s.Entity.DestroyEntity(data.EntityId);
            data.EntityId = -1;
        }

        /// <summary>
        /// 진행중 build task 컴포넌트 → VillageData.ActiveBuildTasks 미러 후 task 엔티티 파괴.
        /// 복원은 RestoreTaskFromData가 담당. 건설중 빌딩 엔티티(BuildingEntityId)는 청크 층(BuildingManager)이
        /// 별도로 관리하므로 여기서 건드리지 않고 스냅샷에만 보존한다.
        /// </summary>
        private void FlushAndDestroyTasks(VillageData data)
        {
            var taskPool = AR.s.Component.GetComponentPool<ObjectPlacementTaskComponent>();

            List<BuildTaskSnapshot> snapshots = new List<BuildTaskSnapshot>();
            for (int i = 0; i < data.ActiveBuildTasks.Count; i++)
            {
                if (data.ActiveBuildTasks[i].PlacementPending == true)
                    snapshots.Add(data.ActiveBuildTasks[i]);
            }
            List<int> toDestroy = new List<int>();
            for (int i = 0; i < taskPool.Count; i++)
            {
                ObjectPlacementTaskComponent t = taskPool.GetByIndex(i);
                if (t.VillageId != data.VillageId)
                    continue;

                snapshots.Add(new BuildTaskSnapshot
                {
                    TableId = t.TargetTableId,
                    StartedAt = t.StartedAt,
                    AccumulatedHours = t.AccumulatedHours,
                    TileX = t.TileX,
                    TileY = t.TileY,
                    ReservedWood = t.ReservedWoodCost,
                    ReservedStone = t.ReservedStoneCost,
                    BuildingEntityId = t.BuildingEntityId,
                    PlacementPending = false,
                });
                toDestroy.Add(taskPool.GetEntityId(i));
            }

            data.ActiveBuildTasks = snapshots;
            data.CurrentBuildTableId = 0;      // Legacy 단일 필드는 항상 비움
            data.CurrentBuildStartedAt = -1f;

            for (int i = 0; i < toDestroy.Count; i++)
            {
                AR.s.Component.RemoveComponent<ObjectPlacementTaskComponent>(toDestroy[i]);
                AR.s.Entity.DestroyEntity(toDestroy[i]);
            }
        }

        /// <summary>
        /// 배치 오브젝트 컴포넌트 상태(HP/사용시각) → VillageData.PlacedObjects 정본 미러 후
        /// 엔티티 파괴 + 조회 인덱스(PlacedObjectRegistry) 정리. 복원은 RestorePlacedObjectsFromData가 담당.
        /// </summary>
        private void FlushAndDestroyPlacedObjects(VillageData data)
        {
            List<int> entities = _placedObjects.GetAllEntitiesInVillage(data.VillageId);
            for (int i = 0; i < entities.Count; i++)
            {
                int entityId = entities[i];
                if (AR.s.Component.TryGetComponent<PlacedObjectComponent>(entityId, out var po))
                {
                    // 좌표로 정본 엔트리를 찾아 HP/사용시각 갱신 (PlacedObjectSaveData는 class → 참조 변경)
                    for (int j = 0; j < data.PlacedObjects.Count; j++)
                    {
                        PlacedObjectSaveData saved = data.PlacedObjects[j];
                        if (saved.TileX == po.TileX && saved.TileY == po.TileY)
                        {
                            saved.Hp = po.HP;
                            saved.MaxHp = po.MaxHP;
                            saved.LastUseGameTime = po.LastUseGameTime;
                            break;
                        }
                    }
                }
                AR.s.Entity.DestroyEntity(entityId);
            }
            _placedObjects.Clear(data.VillageId);
        }

        /// <summary>
        /// VillageComponent / VillageStorageComponent → VillageData 정본 미러 (SyncTaskToData의 per-village 발췌).
        /// 자원은 생산/소비 경로가 이미 VillageData에 상시 동기화하지만, 파괴 직전 최종 flush로 안전 보장.
        /// </summary>
        private void FlushVillageEntityState(VillageData data)
        {
            if (AR.s.Component.TryGetComponent<VillageComponent>(data.EntityId, out var vc))
            {
                data.Stage = vc.Stage;
                data.BoundsX = vc.Bounds.x;
                data.BoundsY = vc.Bounds.y;
                data.BoundsW = vc.Bounds.width;
                data.BoundsH = vc.Bounds.height;
                data.ThreatLevel = vc.ThreatLevel;
                data.WallSegmentCount = vc.WallSegmentCount;
                data.CompletedWallSegments = vc.CompletedWallSegments;
            }
            data.WallPlanRequested = AR.s.Component.HasComponent<WallPlanRequestTag>(data.EntityId);

            if (AR.s.Component.TryGetComponent<VillageStorageComponent>(data.EntityId, out var s))
            {
                data.Resources[GlobalEnum.ItemType.Food] = s.FoodAmount;
                data.Resources[GlobalEnum.ItemType.Wood] = s.WoodAmount;
                data.Resources[GlobalEnum.ItemType.Stone] = s.StoneAmount;
                data.HungerHoursAccumulated = s.HungerHoursAccumulated;
            }
        }

        private void CreateStorageEntity(VillageData data)
        {
            if (data.EntityId >= 0)
                return;   // 이미 실체화됨 (멱등) — materialize 중복 호출 방어

            int entityId = AR.s.Entity.CreateEntity();
            data.EntityId = entityId;

            VillageStorageComponent storage = new VillageStorageComponent
            {
                VillageId = data.VillageId,
                FoodAmount = VillageResourceSimulation.GetAmount(data, GlobalEnum.ItemType.Food),
                WoodAmount = VillageResourceSimulation.GetAmount(data, GlobalEnum.ItemType.Wood),
                StoneAmount = VillageResourceSimulation.GetAmount(data, GlobalEnum.ItemType.Stone),
                FoodCap = VillageResourceSimulation.GetCap(data, GlobalEnum.ItemType.Food),
                WoodCap = VillageResourceSimulation.GetCap(data, GlobalEnum.ItemType.Wood),
                StoneCap = VillageResourceSimulation.GetCap(data, GlobalEnum.ItemType.Stone),
                HungerHoursAccumulated = data.HungerHoursAccumulated,
                SurplusFlags = 0,
            };
            AR.s.Component.AddComponent(entityId, storage);

            // Phase C: VillageComponent 함께 부착 (Stage / Bounds / ThreatLevel)
            // Bounds가 세이브에 없으면 Stage 기준 기본값 산출
            RectInt bounds;
            if (data.BoundsW > 0 && data.BoundsH > 0)
            {
                bounds = new RectInt(data.BoundsX, data.BoundsY, data.BoundsW, data.BoundsH);
            }
            else
            {
                int radius = GetBoundsRadius(data.Stage);
                bounds = new RectInt(
                    Mathf.FloorToInt(data.PositionX) - radius,
                    Mathf.FloorToInt(data.PositionY) - radius,
                    radius * 2, radius * 2);
                data.BoundsX = bounds.x;
                data.BoundsY = bounds.y;
                data.BoundsW = bounds.width;
                data.BoundsH = bounds.height;
            }
            AR.s.Component.AddComponent(entityId, new VillageComponent
            {
                VillageId = data.VillageId,
                Stage = data.Stage,
                Bounds = bounds,
                ThreatLevel = data.ThreatLevel,
                WallSegmentCount = data.WallSegmentCount,
                CompletedWallSegments = data.CompletedWallSegments,
            });

            // Phase C: 벽 빌더 활성 상태 복원
            if (data.WallPlanRequested)
                AR.s.Component.AddComponent(entityId, new WallPlanRequestTag());
        }

        /// <summary>
        /// Stage별 마을 경계 반경. VillageStageTable에서 조회.
        /// 테이블 미로딩 시 명시적 에러 + 0 반환 (의도된 실패).
        /// </summary>
        public static int GetBoundsRadius(VillageStage stage)
        {
            if (AR.s.Data == null)
            {
                Debug.LogError($"[VillageManager] DataManager not ready — stage={stage}");
                return 0;
            }
            Tables.VillageStageTable? t = AR.s.Data.GetVillageStage(stage);
            if (t == null)
            {
                Debug.LogError($"[VillageManager] VillageStageTable not loaded — stage={stage}");
                return 0;
            }
            return t.BoundsRadius;
        }

        public void SyncStorageFromData(VillageData data)
        {
            if (data.EntityId < 0)
                return;

            if (AR.s.Component.TryGetComponent<VillageStorageComponent>(data.EntityId, out var storage) == false)
                return;

            storage.FoodAmount = VillageResourceSimulation.GetAmount(data, GlobalEnum.ItemType.Food);
            storage.WoodAmount = VillageResourceSimulation.GetAmount(data, GlobalEnum.ItemType.Wood);
            storage.StoneAmount = VillageResourceSimulation.GetAmount(data, GlobalEnum.ItemType.Stone);
            storage.FoodCap = VillageResourceSimulation.GetCap(data, GlobalEnum.ItemType.Food);
            storage.WoodCap = VillageResourceSimulation.GetCap(data, GlobalEnum.ItemType.Wood);
            storage.StoneCap = VillageResourceSimulation.GetCap(data, GlobalEnum.ItemType.Stone);
            storage.HungerHoursAccumulated = data.HungerHoursAccumulated;
            storage.SurplusFlags = 0;
            if (storage.FoodAmount >= storage.FoodCap)
                storage.SurplusFlags |= VillageSurplusFlags.Food;
            if (storage.WoodAmount >= storage.WoodCap)
                storage.SurplusFlags |= VillageSurplusFlags.Wood;
            if (storage.StoneAmount >= storage.StoneCap)
                storage.SurplusFlags |= VillageSurplusFlags.Stone;
            AR.s.Component.SetComponent(data.EntityId, storage);
        }

        // ========== Phase B: 오브젝트 배치 콜백 ==========

        /// <summary>
        /// 오브젝트 배치 완료 시 호출. 데이터 주도 Cap 확장 + Phase D PlacedObject 정본 추가 + ECS 엔티티 발급.
        /// </summary>
        public void OnObjectPlaced(int villageId, int tableId, int tileX, int tileY,
            int buildingEntityId = -1)
        {
            if (_villages.TryGetValue(villageId, out VillageData? v) == false)
                return;

            Tables.BuildableItemTable? t = AR.s.Data.GetBuildableItem(tableId);
            if (t == null)
                return;

            VillageConstructionLedger.ApplyCompletedBuild(v, t, tileX, tileY,
                buildingEntityId);
            SyncStorageFromData(v);

            int entityId = AR.s.Entity.CreateEntity();
            AR.s.Component.AddComponent(entityId, new PlacedObjectComponent
            {
                VillageId = villageId,
                TableId = tableId,
                TileX = tileX,
                TileY = tileY,
                HP = t.HP,
                MaxHP = t.HP,
                Service = (ProvidedService)t.ProvidedService,
                SetMember = (SetMemberTag)t.SetMembership,
                UsingNpcEntityId = -1,
                LastUseGameTime = 0f,
            });
            _placedObjects.Register(villageId, entityId, tableId, new Vector2Int(tileX, tileY));
        }

        // ========== Phase B: 진행 중 태스크 세이브 동기화 ==========

        /// <summary>
        /// 세이브 직전 호출. ECS 컴포넌트 → VillageData 평면 필드 미러링.
        /// 동기화 대상: ObjectPlacementTaskComponent (task entity 풀 검색), VillageComponent, WallPlanRequestTag.
        /// Step C: 마을별 task 목록 → VillageData.ActiveBuildTasks. Legacy CurrentBuild* 필드는 비움.
        /// </summary>
        public void SyncTaskToData()
        {
            // task entity 풀에서 마을별 task 목록 매핑 (Step B/C: N개 동시 task 지원)
            var taskPool = AR.s.Component.GetComponentPool<ObjectPlacementTaskComponent>();
            var tasksByVillage = new Dictionary<int, List<BuildTaskSnapshot>>();
            for (int i = 0; i < taskPool.Count; i++)
            {
                ObjectPlacementTaskComponent t = taskPool.GetByIndex(i);
                if (tasksByVillage.TryGetValue(t.VillageId, out var list) == false)
                {
                    list = new List<BuildTaskSnapshot>();
                    tasksByVillage[t.VillageId] = list;
                }
                list.Add(new BuildTaskSnapshot
                {
                    TableId = t.TargetTableId,
                    StartedAt = t.StartedAt,
                    AccumulatedHours = t.AccumulatedHours,
                    TileX = t.TileX,
                    TileY = t.TileY,
                    ReservedWood = t.ReservedWoodCost,
                    ReservedStone = t.ReservedStoneCost,
                    BuildingEntityId = t.BuildingEntityId,
                    PlacementPending = false,
                });
            }

            for (int villageIndex = 0; villageIndex < _villageList.Count; villageIndex++)
            {
                VillageData v = _villageList[villageIndex];
                // 추상(비실체화) 마을은 VillageData가 이미 정본 — 건드리지 않는다.
                //   ActiveBuildTasks: DematerializeVillage가 flush한 진행중 task 스냅샷(보존 필수)
                //   Legacy CurrentBuild* / VillageComponent 미러도 마지막 실체화 시점 값 유지.
                if (v.EntityId < 0)
                    continue;

                // Legacy 단일 필드는 신규 세이브에선 항상 비움 (구 세이브 호환만 위해 필드 유지)
                v.CurrentBuildTableId = 0;
                v.CurrentBuildStartedAt = -1f;

                List<BuildTaskSnapshot> synchronized = new List<BuildTaskSnapshot>();
                for (int i = 0; i < v.ActiveBuildTasks.Count; i++)
                {
                    if (v.ActiveBuildTasks[i].PlacementPending == true)
                        synchronized.Add(v.ActiveBuildTasks[i]);
                }
                if (tasksByVillage.TryGetValue(v.VillageId, out var list))
                {
                    for (int i = 0; i < list.Count; i++)
                        synchronized.Add(list[i]);
                }
                v.ActiveBuildTasks = synchronized;

                // Phase C: VillageComponent 미러
                if (AR.s.Component.TryGetComponent<VillageComponent>(v.EntityId, out var vc))
                {
                    v.Stage = vc.Stage;
                    v.BoundsX = vc.Bounds.x;
                    v.BoundsY = vc.Bounds.y;
                    v.BoundsW = vc.Bounds.width;
                    v.BoundsH = vc.Bounds.height;
                    v.ThreatLevel = vc.ThreatLevel;
                    v.WallSegmentCount = vc.WallSegmentCount;
                    v.CompletedWallSegments = vc.CompletedWallSegments;
                }

                // Phase C: 벽 빌더 활성 플래그
                v.WallPlanRequested = AR.s.Component.HasComponent<WallPlanRequestTag>(v.EntityId);
            }
        }

        /// <summary>
        /// 로드 후 호출. VillageData.ActiveBuildTasks → 각 task entity로 재구성.
        /// 구 세이브 호환: ActiveBuildTasks 비어있고 CurrentBuildTableId>0이면 단일 task로 마이그레이션.
        /// </summary>
        public void RestoreTaskFromData(VillageData v, bool pendingOnly = false)
        {
            if (v.EntityId < 0) return;

            // Legacy 마이그레이션: 구 세이브의 단일 CurrentBuild* 필드 → ActiveBuildTasks
            if (v.ActiveBuildTasks.Count == 0 && v.CurrentBuildTableId > 0)
            {
                v.ActiveBuildTasks.Add(new BuildTaskSnapshot
                {
                    TableId = v.CurrentBuildTableId,
                    StartedAt = v.CurrentBuildStartedAt,
                    TileX = v.CurrentBuildTileX,
                    TileY = v.CurrentBuildTileY,
                    ReservedWood = v.CurrentBuildReservedWood,
                    ReservedStone = v.CurrentBuildReservedStone,
                });
                v.CurrentBuildTableId = 0;
                v.CurrentBuildStartedAt = -1f;
                Debug.Log($"[BuildQueue] v{v.VillageId} 구 세이브 단일 태스크 → ActiveBuildTasks로 마이그레이션");
            }

            for (int i = 0; i < v.ActiveBuildTasks.Count; i++)
            {
                BuildTaskSnapshot snap = v.ActiveBuildTasks[i];
                if (pendingOnly == true && snap.PlacementPending == false
                    && VillageConstructionPosition.IsAssigned(snap) == true)
                    continue;

                Tables.BuildableItemTable? buildTable = AR.s.Data.GetBuildableItem(snap.TableId);
                if (buildTable == null)
                {
                    snap.PlacementPending = true;
                    WarnPlacementOnce(v.VillageId, "task", i,
                        $"진행 task {snap.TableId} 테이블 누락");
                    continue;
                }
                float buildHours = VillageConstructionRules.GetBuildHours(buildTable);

                if (buildTable.SpawnType == GlobalEnum.BuildableSpawnType.Entity
                    && TryRestoreTaskBuilding(v, snap, buildTable, i) == false)
                    continue;
                if (buildTable.SpawnType == GlobalEnum.BuildableSpawnType.Tile
                    && VillageConstructionPosition.IsAssigned(snap) == false)
                {
                    snap.PlacementPending = true;
                    WarnPlacementOnce(v.VillageId, "task", i,
                        $"Tile task {snap.TableId} 좌표 미할당");
                    continue;
                }

                snap.PlacementPending = false;

                // AR.s.Entity.RegisterExistingEntity 덕분에 BuildingManager.Load가 저장 EntityId를 그대로
                // 재사용 → 저장된 BuildingEntityId가 그대로 유효 (진행도↔HP 동기화 복원).
                // 자가 건설(D4): NPC 재부착 없음 — 타이머가 이어서 진행.
                int taskEntityId = AR.s.Entity.CreateEntity();
                ObjectPlacementTaskComponent task = new ObjectPlacementTaskComponent
                {
                    VillageId = v.VillageId,
                    TargetTableId = snap.TableId,
                    TileX = snap.TileX,
                    TileY = snap.TileY,
                    StartedAt = snap.StartedAt,
                    AccumulatedHours = snap.AccumulatedHours,
                    BuildDurationHours = buildHours,
                    ReservedWoodCost = snap.ReservedWood,
                    ReservedStoneCost = snap.ReservedStone,
                    BuildingEntityId = snap.BuildingEntityId,
                };
                AR.s.Component.AddComponent(taskEntityId, task);

                Debug.Log($"[BuildQueue] v{v.VillageId} 로드 후 태스크 복원 #{i+1}/{v.ActiveBuildTasks.Count}: taskEntity={taskEntityId} TableId={task.TargetTableId}, tile=({task.TileX},{task.TileY}), 시작={task.StartedAt:F1}h, 누적={task.AccumulatedHours:F2}h, building={task.BuildingEntityId}");
            }
        }

        private bool TryRestoreTaskBuilding(VillageData village, BuildTaskSnapshot snap,
            Tables.BuildableItemTable table, int index)
        {
            int progressHp = VillageConstructionRules.GetProgressHp(table,
                snap.AccumulatedHours);
            if (VillageConstructionPosition.IsAssigned(snap) == false)
            {
                Vector2Int? tile = FindMaterializationTile(village, table);
                if (tile.HasValue == false)
                {
                    snap.PlacementPending = true;
                    WarnPlacementOnce(village.VillageId, "task", index,
                        $"진행 task {snap.TableId} 배치 타일 없음");
                    return false;
                }

                if (AR.s.Map.PlaceObject(tile.Value.x, tile.Value.y, snap.TableId,
                        isUnderConstruction: true, villageId: village.VillageId,
                        currentHp: progressHp, out int buildingId) == false)
                {
                    snap.PlacementPending = true;
                    WarnPlacementOnce(village.VillageId, "task", index,
                        $"진행 task {snap.TableId} Map/Building 배치 실패");
                    return false;
                }

                snap.TileX = tile.Value.x;
                snap.TileY = tile.Value.y;
                snap.BuildingEntityId = buildingId;
                return true;
            }

            int existingId = snap.BuildingEntityId;
            if (existingId < 0 || AR.s.Building.GetSaveData(existingId) == null)
                existingId = AR.s.Building.FindBuildingAt(snap.TileX, snap.TileY,
                    snap.TableId);
            if (existingId >= 0
                && AR.s.Building.RestoreUnderConstructionProgress(existingId,
                    village.VillageId, progressHp, table))
            {
                snap.BuildingEntityId = existingId;
                return true;
            }

            if (AR.s.Building.IsTileOccupied(snap.TileX, snap.TileY)
                || AR.s.Map.PlaceObject(snap.TileX, snap.TileY, snap.TableId,
                    isUnderConstruction: true, villageId: village.VillageId,
                    currentHp: progressHp, out int restoredId) == false)
            {
                snap.PlacementPending = true;
                WarnPlacementOnce(village.VillageId, "task", index,
                    $"진행 task {snap.TableId}의 저장 위치 복원 실패");
                return false;
            }

            snap.BuildingEntityId = restoredId;
            return true;
        }

        // ========== Phase D: 세트 판정 API ==========

        /// <summary>
        /// Phase D: 마을 전체 또는 anchor 주변 영역에서 세트 요구 비트가 모두 만족되는지 검사.
        /// ObjectSetCatalog의 정의(요구 비트 + Range)에 따라 자동 분기.
        /// Range == 0 → 마을 전체. Range > 0 → anchor 기준 N×N (체비셰프).
        /// </summary>
        public bool HasObjectSet(int villageId, ObjectSetType setType)
        {
            if (_villages.TryGetValue(villageId, out VillageData? village) == false)
                return false;
            return VillageConstructionLedger.HasObjectSet(village, setType);
        }

        public bool HasObjectSet(int villageId, ObjectSetType setType, Vector2Int anchor)
        {
            if (_villages.TryGetValue(villageId, out VillageData? village) == false)
                return false;
            return VillageConstructionLedger.HasObjectSet(village, setType, anchor);
        }

        /// <summary>
        /// Phase D: 마을 내 PlacedObject 중 특정 ProvidedService 비트를 가진 엔티티 개수.
        /// Tier 승격 조건(주거 N개, MerchantStall 1개 등)에 사용. TableId 분기 0.
        /// </summary>
        public int CountByService(int villageId, ProvidedService service)
        {
            if (_villages.TryGetValue(villageId, out VillageData? village) == false)
                return 0;
            return VillageConstructionLedger.CountByService(village, service);
        }

        // ========== Phase D: 상점 거래 API ==========

        /// <summary>
        /// Phase D: 상점 매각 처리. 인벤토리 슬롯 → Gold + 마을 자원 부분 환원.
        /// 반환: 실제 지급된 Gold (실패 시 -1).
        /// </summary>
        public int SellItemToMerchant(int villageId, int slotIndex, int amount)
        {
            if (_villages.TryGetValue(villageId, out VillageData? v) == false) return -1;
            if (AR.s.Player == null) return -1;

            ARPG.Data.ItemData? slotItem = AR.s.Player.Inventory.GetItemBySlotIndex(slotIndex);
            if (slotItem == null) return -1;

            Tables.ItemTable? item = AR.s.Data.GetItem(slotItem.Id);
            if (item == null || item.BasePrice <= 0 || item.SellRatioBp <= 0) return -1;

            // 1. 인벤토리 차감 (실패 시 atomic abort)
            if (AR.s.Player.Inventory.RemoveItem(slotIndex, amount, out _) == false) return -1;

            // 2. Gold 지급 (PlayerData.Gold 정수)
            int gold = item.BasePrice * amount * item.SellRatioBp / 100;
            if (AR.s.Data?.Player != null) AR.s.Data.Player.Gold += gold;

            // 3. 마을 Storage 부분 환원 (자원만)
            if (item.ReturnResourceType != 0 && item.ReturnRatioBp > 0)
            {
                int returnAmount = amount * item.ReturnRatioBp / 100;
                if (returnAmount > 0)
                    ProduceResource(villageId, (GlobalEnum.ItemType)item.ReturnResourceType, returnAmount);
            }

            Debug.Log($"[Sell] v{villageId} 판매 {item.Name} ×{amount} → +{gold}G");
            AR.s.UI.SetNotify($"판매: {item.Name} ×{amount} → +{gold}G");
            return gold;
        }

        /// <summary>
        /// Phase D: 상점 매물 구매. Gold 차감 + 인벤토리 추가 + 매물 잔량 감소.
        /// 반환: 실제 차감된 Gold (실패 시 -1).
        /// </summary>
        public int BuyItemFromMerchant(int villageId, int stockEntryIndex, int amount)
        {
            if (_villages.TryGetValue(villageId, out VillageData? v) == false) return -1;
            if (AR.s.Player == null) return -1;
            if (AR.s.Data?.Player == null) return -1;
            if (stockEntryIndex < 0 || stockEntryIndex >= v.MerchantStock.Count) return -1;

            MerchantStockEntry entry = v.MerchantStock[stockEntryIndex];
            if (entry.RemainingCount < amount) return -1;
            if (entry.Item == null || entry.Item.Table == null) return -1;

            Tables.ItemTable item = entry.Item.Table;
            if (item.BasePrice <= 0) return -1;

            // Non-Stackable(Equipment/SkillBook/SkillPage)은 항상 1개 — 매물 인스턴스 자체를 손에 넣는다.
            // Stackable(소비/자원류)은 amount만큼 새로 생성. 매물의 Item은 표시용 템플릿 역할.
            if (item.Stackable == false)
                amount = 1;

            int gold = item.BasePrice * amount;
            if (AR.s.Data.Player.Gold < gold) return -1;

            ARPG.Data.ItemData purchase;
            if (item.Stackable == false)
            {
                purchase = entry.Item;
            }
            else
            {
                ARPG.Data.ItemData? created = AR.s.Item.CreateInventoryItemData(item.Id, amount);
                if (created == null) return -1;
                purchase = created;
            }

            // 인벤토리에 추가 시도 (꽉 차 있으면 실패 — Gold 미차감)
            if (AR.s.Player.Inventory.AddItem(purchase) < 0) return -1;

            // 차감
            AR.s.Data.Player.Gold -= gold;
            entry.RemainingCount -= amount;
            v.MerchantStock[stockEntryIndex] = entry;

            // 매물 / Gold 변경을 즉시 영구화 (드랍 픽업과 동일 정책).
            AR.s.Data.Save();

            Debug.Log($"[Shop] v{villageId} 구매 {item.Name} ×{amount} = {gold}G");
            AR.s.UI.SetNotify($"구매: {item.Name} ×{amount} = -{gold}G");
            return gold;
        }

        // ========== Phase D: 매물 풀 재롤 ==========

        private const float MERCHANT_ROLL_INTERVAL_HOURS = 24f;
        private const int MERCHANT_STOCK_SLOTS = 5;

        /// <summary>
        /// Phase D: 24h 게임시간 경과 시 매물 풀 재롤.
        /// 풀 조건: ItemTable.Tier ≤ village.Stage AND BasePrice > 0.
        /// 호출자(UIShopMerchant)는 진입 시 EnsureMerchantStockFresh를 호출.
        /// </summary>
        public void EnsureMerchantStockFresh(int villageId)
        {
            if (_villages.TryGetValue(villageId, out VillageData? v) == false) return;

            float now = AR.s.Time.CurrentGameTime;
            if (v.MerchantStock.Count > 0 && now - v.LastMerchantRollGameTime < MERCHANT_ROLL_INTERVAL_HOURS)
                return;

            RollMerchantStock(v);
            v.LastMerchantRollGameTime = now;
        }

        private bool EnsureVillageProfile(VillageData data, bool isSaveLoad)
        {
            if (VillageProfileCatalog.TryGet(data.VillageProfileId,
                    out VillageProfile storedProfile) == true)
            {
                if (data.SettlementId < 0)
                    EnsureBaseTownPopulation(data, storedProfile, isSaveLoad);
                return false;
            }

            string previousProfileId = data.VillageProfileId;
            VillageProfileResolution resolution;
            if (data.SettlementId < 0)
            {
                resolution = new VillageProfileResolution(
                    VillageProfileCatalog.BaseTownProfile, false, string.Empty);
            }
            else if (AR.s.History.TryGetVillageProfileContext(data.SettlementId,
                    out VillageProfileContext context) == true)
            {
                resolution = VillageProfileResolver.Resolve(context);
            }
            else
            {
                resolution = new VillageProfileResolution(
                    VillageProfileCatalog.DefaultProceduralProfile, true,
                    $"Settlement {data.SettlementId} has no saved Race/Biome context; " +
                    $"{VillageProfileCatalog.DEFAULT_PROCEDURAL_PROFILE_ID} was used.");
            }

            data.VillageProfileId = resolution.Profile.ProfileId;

            if (string.IsNullOrWhiteSpace(previousProfileId) == false)
            {
                WarnProfileOnce($"unknown:{previousProfileId}",
                    $"Village {data.VillageId} references unknown profile " +
                    $"'{previousProfileId}'; fallback '{data.VillageProfileId}' was saved.");
            }
            if (resolution.UsedFallback == true)
            {
                WarnProfileOnce($"fallback:{resolution.Warning}",
                    $"Village {data.VillageId} profile fallback: {resolution.Warning}");
            }

            if (data.SettlementId < 0)
                EnsureBaseTownPopulation(data, resolution.Profile, isSaveLoad);
            return true;
        }

        private void EnsureBaseTownPopulation(VillageData data, VillageProfile profile,
            bool isSaveLoad)
        {
            int fixedPopulation = VillageProfilePopulationPolicy
                .ResolveBaseTownFixedPopulation(data.FixedPopulation, data.Population,
                    profile, out BaseTownPopulationSource source);
            data.FixedPopulation = fixedPopulation;
            data.Population = fixedPopulation;

            if (isSaveLoad == false
                || source == BaseTownPopulationSource.SavedFixedPopulation)
            {
                return;
            }

            WarnProfileOnce($"base-population:{source}",
                $"BaseTown village {data.VillageId} had no valid FixedPopulation; " +
                $"{source} resolved deterministic economic population {fixedPopulation}. " +
                "Physical NPC count was not used.");
        }

        private void WarnProfileOnce(string key, string message)
        {
            if (_profileWarnings.Add(key) == true)
                Debug.LogWarning($"[VillageProfile] {message}");
        }

        private void AddVillage(VillageData data)
        {
            // 중복 VillageId(손상/중복 세이브)는 예외 대신 skip — 두 컬렉션의 동기화를 유지한다.
            if (_villages.TryAdd(data.VillageId, data) == false)
            {
                Debug.LogWarning($"[VillageManager] Duplicate VillageId {data.VillageId} ignored. " +
                    "Existing entry is kept.");
                return;
            }
            _villageList.Add(data);
        }

        private void ClearVillages()
        {
            _villages.Clear();
            _villageList.Clear();
        }

        private static void RollMerchantStock(VillageData v)
        {
            v.MerchantStock.Clear();

            List<Tables.ItemTable> pool = new List<Tables.ItemTable>();
            int stageInt = (int)v.Stage;
            foreach (Tables.ItemTable it in AR.s.Data.GetAllItems())
            {
                if (it.BasePrice <= 0) continue;
                if (it.Tier > stageInt + 1) continue;  // Stage 0(Settlement) → Tier 1까지, Stage 3(Town) → Tier 4까지 (관대한 설정)
                pool.Add(it);
            }

            if (pool.Count == 0) return;

            int slots = Mathf.Min(MERCHANT_STOCK_SLOTS, pool.Count);
            for (int i = 0; i < slots; i++)
            {
                int idx = Random.Range(0, pool.Count);
                Tables.ItemTable picked = pool[idx];
                pool.RemoveAt(idx);

                // 매물 인스턴스를 roll 시점에 완성한다 — SkillBook/SkillPage/Equipment 분기 + 인스턴스 데이터 채움까지
                // ItemManager.CreateInventoryItemData가 단일 진입점.
                ARPG.Data.ItemData? rolled = AR.s.Item.CreateInventoryItemData(picked.Id);
                if (rolled == null)
                {
                    Debug.LogWarning($"[Shop] ItemId({picked.Id}) 매물 생성 실패 — 슬롯 스킵");
                    i--; // 슬롯 카운트 복구
                    if (pool.Count == 0) break;
                    continue;
                }

                v.MerchantStock.Add(new MerchantStockEntry
                {
                    Item = rolled,
                    RemainingCount = picked.Stackable ? Random.Range(3, 11) : 1,
                });
            }

            Debug.Log($"[Shop] v{v.VillageId} 매물 재롤: {v.MerchantStock.Count}건");
        }
    }
}
