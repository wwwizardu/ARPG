#nullable enable
using System.Collections.Generic;
using ARPG.Component;
using ARPG.Map;
using ARPG.Utility;
using UnityEngine;

namespace ARPG.Village
{
    public class VillageManager : MonoBehaviour
    {
        private const int DEFAULT_RESOURCE_CAP = 50;

        private Dictionary<int, VillageData> _villages = new();
        // PlacedObject 조회 인덱스 — 휘발성(세이브 아님). static 대신 매니저 소유, Reset/Load에서 수명 관리.
        private readonly PlacedObjectRegistry _placedObjects = new();
        public PlacedObjectRegistry PlacedObjects => _placedObjects;

        public void Initialize()
        {
            if (_villages.Count > 0)
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
                    MaterializeVillage(i);   // 손제작 마을(연대기 미연결) → 항상 활성 (실체화 유지)

                // 연대기 연결 마을은 추상 상태로 남는다 — 근처가 되면 UpdateMaterialization이 실체화.
                Debug.Log($"[VillageManager] Initial village {i} registered at {villageCenter} (TableId={tableId}, SettlementId={settlementId})");
            }
        }

        public void Reset()
        {
            foreach (VillageData data in _villages.Values)
            {
                if (data.EntityId >= 0)
                {
                    AR.s.Entity.DestroyEntity(data.EntityId, false);
                    data.EntityId = -1;
                }
            }
            _villages.Clear();
            _placedObjects.ClearAll();
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
            _villages[villageId] = data;

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
            foreach (VillageData v in _villages.Values)
            {
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

            int cap = GetCap(data, type);
            int current = data.Resources.TryGetValue(type, out int c) ? c : 0;
            int newAmount = Mathf.Min(current + amount, cap);

            data.Resources[type] = newAmount;

            SyncStorageComponent(data);

            if (showNotify)
                AR.s.UI.SetNotify(BuildResourceStatusNotify(data));
        }

        public bool ConsumeResource(int villageId, GlobalEnum.ItemType type, int amount)
        {
            if (_villages.TryGetValue(villageId, out VillageData? data) == false)
                return false;

            int current = data.Resources.TryGetValue(type, out int c) ? c : 0;
            if (current < amount)
                return false;

            data.Resources[type] = current - amount;

            SyncStorageComponent(data);

            AR.s.UI.SetNotify(BuildResourceStatusNotify(data));
            return true;
        }

        private static string BuildResourceStatusNotify(VillageData data)
        {
            int food = GetInt(data, GlobalEnum.ItemType.Food);
            int wood = GetInt(data, GlobalEnum.ItemType.Wood);
            int stone = GetInt(data, GlobalEnum.ItemType.Stone);
            int foodCap = GetCap(data, GlobalEnum.ItemType.Food);
            int woodCap = GetCap(data, GlobalEnum.ItemType.Wood);
            int stoneCap = GetCap(data, GlobalEnum.ItemType.Stone);
            return $"마을 {data.VillageId} [{data.Stage}]: Food {food}/{foodCap}  Wood {wood}/{woodCap}  Stone {stone}/{stoneCap}";
        }

        public int GetResourceAmount(int villageId, GlobalEnum.ItemType type)
        {
            if (_villages.TryGetValue(villageId, out VillageData? data) == false)
                return 0;

            if (data.Resources.TryGetValue(type, out int amount))
                return amount;

            return 0;
        }

        public Dictionary<int, VillageData>.ValueCollection GetAllVillages()
        {
            return _villages.Values;
        }

        public int GetVillageCount()
        {
            return _villages.Count;
        }

        /// <summary>
        /// 월드 좌표가 소속된 마을 Id 반환. SpawnRadius 이내에서 가장 가까운 마을 선택.
        /// 해당 범위 내 마을이 없으면 -1.
        /// </summary>
        public int FindVillageContaining(int worldX, int worldY)
        {
            float bestSqrDist = float.MaxValue;
            int bestId = -1;

            foreach (VillageData data in _villages.Values)
            {
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

        // ── §0 추상 인구 → 마을 경제 인구(v.Population) 소싱 (NPC_SIMPLIFICATION_PLAN §3.2) ──
        // 정량화(pop/50·cap25)는 기존 NPC 스폰 수 캘리브레이션 재사용 — 생산/Tier 임계값 재튜닝 회피.
        public const int ABSTRACT_POP_DIVISOR = 50;  // 추상 인구 K명당 경제 인구 1
        public const int ABSTRACT_POP_CAP = 25;      // 경제 인구 상한 (pop≈1250에서 도달)

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
            foreach (VillageData v in _villages.Values)
                RefreshPopulationFromAbstract(v);
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
                // 연대기 미연결(수제작 마을, 예: BaseTown) — 추상 인구가 없으므로 물리 코호트 수 유지 (기존 동작)
                v.Population = v.NpcEntityIds.Count;
            }
        }

        public List<VillageData> Save()
        {
            List<VillageData> list = new List<VillageData>(_villages.Count);
            foreach (VillageData village in _villages.Values)
            {
                list.Add(village);
            }
            return list;
        }

        public void Load(List<VillageData> villageDatas)
        {
            _villages.Clear();

            if (villageDatas == null)
                return;

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
                if (data.RegisteredAt <= 0f)
                    data.RegisteredAt = AR.s.Time.CurrentGameTime;

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
                _villages[data.VillageId] = data;

                // 손제작 마을(연대기 미연결)만 즉시 실체화 (항상 활성).
                // 연대기 연결 마을은 추상 상태로 로드 — 근처가 되면 UpdateMaterialization이 실체화한다.
                if (data.SettlementId < 0)
                    MaterializeVillage(data.VillageId);
            }

            Debug.Log($"[VillageManager] Loaded {_villages.Count} villages");
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
        private void RestorePlacedObjectsFromData(VillageData data)
        {
            if (data.PlacedObjects == null) return;

            for (int i = 0; i < data.PlacedObjects.Count; i++)
            {
                PlacedObjectSaveData saved = data.PlacedObjects[i];
                Tables.BuildableItemTable? t = AR.s.Data.GetBuildableItem(saved.TableId);
                if (t == null) continue;

                int entityId = AR.s.Entity.CreateEntity();
                AR.s.Component.AddComponent(entityId, new PlacedObjectComponent
                {
                    VillageId = data.VillageId,
                    TableId = saved.TableId,
                    TileX = saved.TileX,
                    TileY = saved.TileY,
                    HP = saved.Hp > 0 ? saved.Hp : t.HP,
                    MaxHP = saved.MaxHp > 0 ? saved.MaxHp : t.HP,
                    Service = (ProvidedService)t.ProvidedService,
                    SetMember = (SetMemberTag)t.SetMembership,
                    UsingNpcEntityId = -1,
                    LastUseGameTime = saved.LastUseGameTime,
                });
                _placedObjects.Register(data.VillageId, entityId, saved.TableId,
                    new Vector2Int(saved.TileX, saved.TileY));
            }
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
                return;   // 이미 실체화됨

            CreateStorageEntity(data);
            RestoreTaskFromData(data);
            RestorePlacedObjectsFromData(data);
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
                FoodAmount = GetInt(data, GlobalEnum.ItemType.Food),
                WoodAmount = GetInt(data, GlobalEnum.ItemType.Wood),
                StoneAmount = GetInt(data, GlobalEnum.ItemType.Stone),
                FoodCap = GetCap(data, GlobalEnum.ItemType.Food),
                WoodCap = GetCap(data, GlobalEnum.ItemType.Wood),
                StoneCap = GetCap(data, GlobalEnum.ItemType.Stone),
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

        private void SyncStorageComponent(VillageData data)
        {
            if (data.EntityId < 0)
                return;

            if (AR.s.Component.TryGetComponent<VillageStorageComponent>(data.EntityId, out var storage) == false)
                return;

            storage.FoodAmount = GetInt(data, GlobalEnum.ItemType.Food);
            storage.WoodAmount = GetInt(data, GlobalEnum.ItemType.Wood);
            storage.StoneAmount = GetInt(data, GlobalEnum.ItemType.Stone);
            AR.s.Component.SetComponent(data.EntityId, storage);
        }

        private static int GetInt(VillageData data, GlobalEnum.ItemType type)
        {
            return data.Resources.TryGetValue(type, out int v) ? v : 0;
        }

        private static int GetCap(VillageData data, GlobalEnum.ItemType type)
        {
            if (data.ResourceCaps.TryGetValue(type, out int cap))
                return cap;
            return DEFAULT_RESOURCE_CAP;
        }

        // ========== Phase B: 오브젝트 배치 콜백 ==========

        /// <summary>
        /// 오브젝트 배치 완료 시 호출. 데이터 주도 Cap 확장 + Phase D PlacedObject 정본 추가 + ECS 엔티티 발급.
        /// </summary>
        public void OnObjectPlaced(int villageId, int tableId, int tileX, int tileY)
        {
            if (_villages.TryGetValue(villageId, out VillageData? v) == false)
                return;

            Tables.BuildableItemTable? t = AR.s.Data.GetBuildableItem(tableId);
            if (t == null)
                return;

            // Cap 확장: 테이블 컬럼 그대로 가산
            if (t.StorageCap_Food > 0)
                AddCap(v, GlobalEnum.ItemType.Food, t.StorageCap_Food);
            if (t.StorageCap_Wood > 0)
                AddCap(v, GlobalEnum.ItemType.Wood, t.StorageCap_Wood);
            if (t.StorageCap_Stone > 0)
                AddCap(v, GlobalEnum.ItemType.Stone, t.StorageCap_Stone);

            // Phase D: PlacedObject 정본 추가 + ECS 엔티티 발급 + Registry 등록
            PlacedObjectSaveData saved = new PlacedObjectSaveData
            {
                TableId = tableId,
                TileX = tileX,
                TileY = tileY,
                Hp = t.HP,
                MaxHp = t.HP,
                LastUseGameTime = 0f,
            };
            v.PlacedObjects.Add(saved);

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

        private void AddCap(VillageData v, GlobalEnum.ItemType type, int delta)
        {
            int cur = v.ResourceCaps.TryGetValue(type, out int c) ? c : DEFAULT_RESOURCE_CAP;
            int next = cur + delta;
            v.ResourceCaps[type] = next;

            // VillageStorageComponent 동기화
            if (v.EntityId >= 0 &&
                AR.s.Component.TryGetComponent<VillageStorageComponent>(v.EntityId, out var s))
            {
                if (type == GlobalEnum.ItemType.Wood) s.WoodCap = next;
                else if (type == GlobalEnum.ItemType.Stone) s.StoneCap = next;
                else if (type == GlobalEnum.ItemType.Food) s.FoodCap = next;
                AR.s.Component.SetComponent(v.EntityId, s);
            }

            Debug.Log($"[Cap] v{v.VillageId} {type} +{delta} → {next}");
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
                });
            }

            foreach (VillageData v in _villages.Values)
            {
                // 추상(비실체화) 마을은 VillageData가 이미 정본 — 건드리지 않는다.
                //   ActiveBuildTasks: DematerializeVillage가 flush한 진행중 task 스냅샷(보존 필수)
                //   Legacy CurrentBuild* / VillageComponent 미러도 마지막 실체화 시점 값 유지.
                if (v.EntityId < 0)
                    continue;

                // Legacy 단일 필드는 신규 세이브에선 항상 비움 (구 세이브 호환만 위해 필드 유지)
                v.CurrentBuildTableId = 0;
                v.CurrentBuildStartedAt = -1f;

                if (tasksByVillage.TryGetValue(v.VillageId, out var list))
                    v.ActiveBuildTasks = list;
                else
                    v.ActiveBuildTasks.Clear();

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
        public void RestoreTaskFromData(VillageData v)
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
                Tables.BuildableItemTable? buildTable = AR.s.Data.GetBuildableItem(snap.TableId);
                float buildHours = (buildTable != null && buildTable.BuildHours > 0f) ? buildTable.BuildHours : 2f;

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

        // ========== Phase D: 세트 판정 API ==========

        /// <summary>
        /// Phase D: 마을 전체 또는 anchor 주변 영역에서 세트 요구 비트가 모두 만족되는지 검사.
        /// ObjectSetCatalog의 정의(요구 비트 + Range)에 따라 자동 분기.
        /// Range == 0 → 마을 전체. Range > 0 → anchor 기준 N×N (체비셰프).
        /// </summary>
        public bool HasObjectSet(int villageId, ObjectSetType setType, Vector2Int anchor = default)
        {
            if (ObjectSetCatalog.All.TryGetValue(setType, out var def) == false) return false;

            List<int> entities;
            if (def.Range == 0)
            {
                entities = _placedObjects.GetAllEntitiesInVillage(villageId);
            }
            else
            {
                int half = def.Range / 2;
                RectInt rect = new RectInt(anchor.x - half, anchor.y - half, def.Range, def.Range);
                entities = _placedObjects.GetAllEntitiesInBounds(villageId, rect);
            }

            SetMemberTag covered = SetMemberTag.None;
            for (int i = 0; i < entities.Count; i++)
            {
                if (AR.s.Component.TryGetComponent<PlacedObjectComponent>(entities[i], out var po))
                    covered |= po.SetMember;
            }
            return (covered & def.RequiredMask) == def.RequiredMask;
        }

        /// <summary>
        /// Phase D: 마을 내 PlacedObject 중 특정 ProvidedService 비트를 가진 엔티티 개수.
        /// Tier 승격 조건(주거 N개, MerchantStall 1개 등)에 사용. TableId 분기 0.
        /// </summary>
        public int CountByService(int villageId, ProvidedService service)
        {
            var entities = _placedObjects.GetAllEntitiesInVillage(villageId);
            int count = 0;
            for (int i = 0; i < entities.Count; i++)
            {
                if (AR.s.Component.TryGetComponent<PlacedObjectComponent>(entities[i], out var po))
                {
                    if ((po.Service & service) != 0) count++;
                }
            }
            return count;
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
