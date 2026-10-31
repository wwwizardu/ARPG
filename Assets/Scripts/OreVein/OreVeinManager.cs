#nullable enable
using System;
using System.Collections.Generic;
using ARPG.Component;
using ARPG.Data;
using ARPG.Factory;
using ARPG.Map;
using ARPG.Systems;
using ARPG.Village;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;

namespace ARPG.OreVein
{
    /// <summary>
    /// 월드 광맥 소유자 (WORLD_ORE_VEIN_PLAN). 저장 기록(ore_veins 파트, DataManager.OreVeins)을 단독으로 수정하고,
    /// 청크 활성화 때 굴림·복원, 엔티티 생성·제거, E 안내 후보와 채광 시작, 채광 소비 경계, 마을 건설과 겹친 기록 삭제, 하루 1회 정리를 맡는다.
    /// 채광 진행·취소·완료(광석 지급 포함)는 System_PlayerMining이 판단한다.
    /// 위치 검사는 켜지는 청크의 타일 데이터로만 하고 다른 청크의 지형은 조회하지 않는다 (§3, §8.4).
    /// 지속 상태는 모두 저장 기록에 있고, 이 클래스의 필드는 청크 색인·엔티티 색인·그림 핸들 같은 런타임 값뿐이다 (Reset에서 비운다).
    /// </summary>
    public class OreVeinManager
    {
        // 던전 입구·상자와 같은 정적 엔티티 그리기 순서
        private const int SortingOrder = 10;
        // OreVeinType 순서 (None, Copper, Tin, Iron)
        private static readonly string[] _spriteKeys = { string.Empty, "Sprites/Map/CopperOre", "Sprites/Map/TinOre", "Sprites/Map/IronOre" };
        // 그림 피벗(아래 가운데)을 칸 바닥 쪽에 붙인다. 루트는 칸 중심이라 상호작용 거리는 칸 중심 기준이다
        private static readonly Vector2 _visualOffset = new(0f, -0.45f);

        private bool _initialized;
        private int _lifetime;
        private int _chunkSize;
        private int _lastCleanupDay = int.MinValue;
        // 청크 좌표 → 기록 (저장 목록과 같은 인스턴스)
        private readonly Dictionary<Vector2Int, OreVeinRecord> _recordsByChunk = new();
        // 청크 좌표 → 실체화된 광맥 EntityId (활성 청크에만 있다)
        private readonly Dictionary<Vector2Int, int> _entityByChunk = new();
        // 생성 요청 중인 청크 (Addressables 비동기)
        private readonly HashSet<Vector2Int> _spawning = new();
        // scratch — 사용 직전에 비운다
        private readonly List<Vector2Int> _chunkScratch = new();
        private readonly AsyncOperationHandle<Sprite>[] _sprites = new AsyncOperationHandle<Sprite>[_spriteKeys.Length];
        private bool _spriteErrorLogged;

        public bool IsInitialized => _initialized;
        public int RecordCount => _recordsByChunk.Count;

        public void Initialize()
        {
            _chunkSize = Mathf.Max(1, AR.s.WorldMap.chunkSize);
            RebuildIndex();
            _initialized = true;
        }

        public void Reset()
        {
            _lifetime++;
            _initialized = false;
            RemoveAllEntities();
            _recordsByChunk.Clear();
            _spawning.Clear();
            _chunkScratch.Clear();
            _lastCleanupDay = int.MinValue;
            for (int i = 0; i < _sprites.Length; i++)
            {
                if (_sprites[i].IsValid() == true)
                    Addressables.Release(_sprites[i]);
                _sprites[i] = default;
            }
            _spriteErrorLogged = false;
        }

        // ================= 조회 =================

        public OreVeinRecord? GetRecord(Vector2Int chunkCoord)
            => _recordsByChunk.TryGetValue(chunkCoord, out OreVeinRecord? record) ? record : null;

        /// <summary>이 칸에 지금 광맥이 있는가 (활성 기록만). 던전 입구 배치와 이후 플레이어 건설이 광맥 칸을 거부하는 데 쓴다 (§6.2).</summary>
        public bool IsVeinTile(int worldX, int worldY)
        {
            if (_initialized == false)
                return false;
            return _recordsByChunk.TryGetValue(TileToChunk(worldX, worldY), out OreVeinRecord? record) &&
                   record.State == OreVeinState.Active && record.TileX == worldX && record.TileY == worldY;
        }

        // ================= 청크 생명주기 =================

        public void OnChunkActivated(Vector2Int chunkCoord, MapChunkData chunk)
        {
            if (_initialized == false)
                return;
            OreVeinRecord? record = ResolveChunk(chunkCoord, chunk, AR.s.Time.CurrentGameTime);
            if (record != null)
                SpawnIfNeeded(chunkCoord, record);
        }

        /// <summary>청크가 꺼지면 엔티티만 정리한다. 기록을 지우거나 다시 굴리지 않는다 (§8.2).</summary>
        public void OnChunkDeactivated(Vector2Int chunkCoord)
        {
            if (_initialized == false)
                return;
            if (_recordsByChunk.TryGetValue(chunkCoord, out OreVeinRecord? record) == true && record.State == OreVeinState.Active)
            {
                record.LastActiveGameTime = AR.s.Time.CurrentGameTime;
                AR.s.Data.MarkOreVeinSaveDirty(chunkCoord.x, chunkCoord.y);
            }
            RemoveEntity(chunkCoord);
        }

        /// <summary>
        /// 청크가 켜질 때의 판단 (§6.1). 기록 없음 → 오늘 날짜로 굴림, 활성 → 복원, 고갈 → 재생 시각 전이면 없음·지났으면 지우고 굴림.
        /// 기록된 칸이 장애물에 덮였으면(구 세이브·규칙 밖 변경) 그리지 않고 지운 뒤 같은 활성화에서 다시 굴린다 (§6.2).
        /// 보여 줄 활성 기록을 돌려준다. 엔티티는 만들지 않는다.
        /// </summary>
        public OreVeinRecord? ResolveChunk(Vector2Int chunkCoord, MapChunkData chunk, float now)
        {
            if (_initialized == false)
                return null;
            if (_recordsByChunk.TryGetValue(chunkCoord, out OreVeinRecord? record) == true)
            {
                if (record.State == OreVeinState.Exhausted)
                {
                    if (OreVeinRules.IsRegrowDue(record.RegrowGameTime, now) == false)
                        return null;
                    RemoveRecord(record);
                }
                else if (IsObstacleFree(chunkCoord, chunk, record.TileX - chunkCoord.x * _chunkSize,
                             record.TileY - chunkCoord.y * _chunkSize) == false)
                {
                    RemoveRecord(record);
                }
                else
                {
                    record.LastActiveGameTime = now;
                    AR.s.Data.MarkOreVeinSaveDirty(chunkCoord.x, chunkCoord.y);
                    return record;
                }
            }
            return TryCreateVein(chunkCoord, chunk, now);
        }

        // ================= 채광·배치 경계 =================

        /// <summary>
        /// 채광 1회 소비 (§7.1). 대상은 청크 좌표·굴린 날짜로 가리키고, 같은 활성 기록이고 잔량이 남았을 때만 1 줄인다.
        /// 마지막 잔량이면 고갈로 바꾸고 재생 가능 시각을 적은 뒤 엔티티를 지운다.
        /// 아이템 지급은 호출자(4단계 채광)가 소유하고 같은 프레임에 저장 변경을 표시한다. 그때 쓰는 Player 저장이 이 파트도 싣는다.
        /// </summary>
        public bool TryConsume(Vector2Int chunkCoord, int rollDay, float now)
        {
            if (_initialized == false ||
                _recordsByChunk.TryGetValue(chunkCoord, out OreVeinRecord? record) == false ||
                record.State != OreVeinState.Active || record.RollDay != rollDay || record.Remaining <= 0)
                return false;
            record.Remaining--;
            if (record.Remaining == 0)
            {
                record.State = OreVeinState.Exhausted;
                record.RegrowGameTime = now + OreVeinRules.RegrowHours;
                RemoveEntity(chunkCoord);
            }
            AR.s.Data.MarkOreVeinSaveDirty(chunkCoord.x, chunkCoord.y);
            return true;
        }

        /// <summary>그 청크에 굴린 날짜가 같은 활성 광맥이 있고 잔량이 남았는가 (채광 대상 확인, §7.1).</summary>
        public bool IsMineable(Vector2Int chunkCoord, int rollDay)
            => _initialized == true && _recordsByChunk.TryGetValue(chunkCoord, out OreVeinRecord? record) == true &&
               record.State == OreVeinState.Active && record.RollDay == rollDay && record.Remaining > 0;

        /// <summary>
        /// E 안내 후보 (§7.1): 플레이어에서 1.5칸 안의 가장 가까운 광맥 엔티티. 상점·던전 입구와 같은 후보 경로(ServiceUIRouter)에서 거리로 겨룬다.
        /// </summary>
        public bool TryGetInteraction(out ServiceUIRouter.Candidate candidate)
        {
            candidate = default;
            if (_initialized == false || _entityByChunk.Count == 0)
                return false;
            ComponentManager cm = AR.s.Component;
            if (cm.TryGetComponent<TransformComponent>(AR.s.Data.CurrentPlayerEntityId, out var player) == false)
                return false;
            SparseSet<OreVeinComponent> pool = cm.GetComponentPool<OreVeinComponent>();
            float best = OreVeinRules.InteractRange * OreVeinRules.InteractRange;
            bool found = false;
            for (int i = 0; i < pool.Count; i++)
            {
                OreVeinComponent vein = pool.GetByIndex(i);
                if (_recordsByChunk.TryGetValue(new Vector2Int(vein.ChunkX, vein.ChunkY), out OreVeinRecord? record) == false ||
                    record.State != OreVeinState.Active || record.RollDay != vein.RollDay)
                    continue;
                float distance = Vector2.SqrMagnitude(player.Position - new Vector2(record.TileX + 0.5f, record.TileY + 0.5f));
                if (distance >= best)
                    continue;
                best = distance;
                found = true;
                candidate = new ServiceUIRouter.Candidate
                {
                    Kind = ServiceUIRouter.CandidateKind.OreVein, EntityId = pool.GetEntityId(i), DistanceSqr = distance,
                    HasTile = true, TileX = record.TileX, TileY = record.TileY,
                };
            }
            return found;
        }

        /// <summary>안내 문구: "구리 광맥 채광 (남은 3회)". 광종 이름은 아이템 표 이름을 쓴다.</summary>
        public string GetInteractionPrompt(int oreEntityId)
        {
            if (AR.s.Component.TryGetComponent<OreVeinComponent>(oreEntityId, out var vein) == false ||
                _recordsByChunk.TryGetValue(new Vector2Int(vein.ChunkX, vein.ChunkY), out OreVeinRecord? record) == false)
                return "채광";
            return $"{GetOreName(record.Type)} 광맥 채광 (남은 {record.Remaining}회)";
        }

        /// <summary>
        /// E로 채광 시작 (§7.1): 플레이어에 MiningComponent를 붙인다. 진행·취소·완료는 System_PlayerMining이 판단한다.
        /// 같은 광맥을 이미 캐는 중이면 그대로 둔다.
        /// </summary>
        public bool TryBeginMining(int playerId, int oreEntityId)
        {
            ComponentManager cm = AR.s.Component;
            if (_initialized == false || cm.TryGetComponent<OreVeinComponent>(oreEntityId, out var vein) == false ||
                IsMineable(new Vector2Int(vein.ChunkX, vein.ChunkY), vein.RollDay) == false)
                return false;
            if (cm.TryGetComponent<MiningComponent>(playerId, out var current) == true &&
                current.ChunkX == vein.ChunkX && current.ChunkY == vein.ChunkY && current.RollDay == vein.RollDay)
                return true;
            if (cm.TryGetComponent<TransformComponent>(playerId, out var transform) == false ||
                cm.TryGetComponent<StatComponent>(playerId, out var stat) == false)
                return false;
            cm.SetComponent(playerId, new MiningComponent
            {
                ChunkX = vein.ChunkX, ChunkY = vein.ChunkY, RollDay = vein.RollDay,
                StartPosition = transform.Position, LastHp = stat.CurrentHp,
            });
            AttachProgressView(playerId);
            return true;
        }

        /// <summary>광종 이름 (아이템 표 이름, 없으면 enum 이름).</summary>
        public static string GetOreName(OreVeinType type)
        {
            Tables.ItemTable? table = AR.s.Data.GetItem(OreVeinRules.GetItemId(type));
            return table != null ? table.Name : type.ToString();
        }

        /// <summary>
        /// 마을 건설이 광맥 칸을 덮으면 마을이 우선한다 (§6.2): 그 칸의 활성 기록을 지운다. 그 청크는 다음 활성화 때 다시 굴린다.
        /// MapManager.PlaceObject가 배치에 성공하면 부른다 (건물은 W×H 칸).
        /// </summary>
        public void RemoveVeinsInFootprint(int worldX, int worldY, int width, int height)
        {
            if (_initialized == false || width <= 0 || height <= 0)
                return;
            Vector2Int min = TileToChunk(worldX, worldY);
            Vector2Int max = TileToChunk(worldX + width - 1, worldY + height - 1);
            for (int cx = min.x; cx <= max.x; cx++)
            {
                for (int cy = min.y; cy <= max.y; cy++)
                {
                    if (_recordsByChunk.TryGetValue(new Vector2Int(cx, cy), out OreVeinRecord? record) == false ||
                        record.State != OreVeinState.Active)
                        continue;
                    if (record.TileX >= worldX && record.TileX < worldX + width &&
                        record.TileY >= worldY && record.TileY < worldY + height)
                        RemoveRecord(record);
                }
            }
        }

        // ================= 정리 (§6.3) =================

        /// <summary>
        /// 게임 하루 1회: 마지막 활성 시각부터 72게임시간 지난 활성 기록, 재생 가능 시각이 지난 고갈 기록을 지운다.
        /// 켜진 청크는 마지막 활성 시각이 꺼질 때 갱신되므로 건너뛴다.
        /// </summary>
        public void TickCleanup(float now)
        {
            if (_initialized == false)
                return;
            int day = OreVeinRules.GetDay(now);
            if (day == _lastCleanupDay)
                return;
            _lastCleanupDay = day;
            MapManager worldMap = AR.s.WorldMap;
            List<OreVeinRecord> veins = AR.s.Data.OreVeins;
            for (int i = veins.Count - 1; i >= 0; i--)
            {
                OreVeinRecord record = veins[i];
                if (worldMap.IsChunkActive(new Vector2Int(record.ChunkX, record.ChunkY)) == true)
                    continue;
                if (OreVeinRules.IsCleanupDue(record, now) == true)
                    RemoveRecord(record);
            }
        }

        // ================= 내부 =================

        private OreVeinRecord? TryCreateVein(Vector2Int chunkCoord, MapChunkData chunk, float now)
        {
            int day = OreVeinRules.GetDay(now);
            // 광종은 이 청크의 구역 번호로 고른다 (§4.1). 청크 데이터에 이미 계산돼 있다
            if (OreVeinRules.TryRoll(AR.s.Data.WorldSeed, chunkCoord.x, chunkCoord.y, day, _chunkSize, Mathf.Max(1, chunk.zone),
                    out int localX, out int localY, out OreVeinType type) == false)
                return null;
            // 뽑은 칸에 놓을 수 없으면 그날 이 청크에는 광맥이 없다. 빈 결과는 저장하지 않는다 (§4.1)
            if (CanPlaceVein(chunkCoord, chunk, localX, localY) == false)
                return null;
            var record = new OreVeinRecord
            {
                ChunkX = chunkCoord.x,
                ChunkY = chunkCoord.y,
                Type = type,
                TileX = chunkCoord.x * _chunkSize + localX,
                TileY = chunkCoord.y * _chunkSize + localY,
                Remaining = OreVeinRules.Charges,
                RollDay = day,
                State = OreVeinState.Active,
                LastActiveGameTime = now,
            };
            AR.s.Data.OreVeins.Add(record);
            _recordsByChunk.Add(chunkCoord, record);
            AR.s.Data.MarkOreVeinSaveDirty(chunkCoord.x, chunkCoord.y);
            return record;
        }

        /// <summary>위치 조건 (§5). 길찾기 도달 여부와 다른 광맥과의 간격은 검사하지 않는다.</summary>
        private bool CanPlaceVein(Vector2Int chunkCoord, MapChunkData chunk, int localX, int localY)
        {
            if (IsObstacleFree(chunkCoord, chunk, localX, localY) == false)
                return false;
            int worldX = chunkCoord.x * _chunkSize + localX;
            int worldY = chunkCoord.y * _chunkSize + localY;
            // 마을 안에는 만들지 않는다: 청사진 사각형, 마을 영역(저장 경계, 없으면 Stage 반경, 사라진 터 포함)
            if (AR.s.WorldMap.IsOutsideBlueprints(worldX, worldY, 0) == false ||
                AR.s.Village.IsInsideVillageArea(new Vector2(worldX + 0.5f, worldY + 0.5f)) == true)
                return false;
            if (AR.s.Dungeon.HasEntranceWithin(worldX, worldY, OreVeinRules.EntranceClearance) == true)
                return false;
            // 플레이어가 설 인접 칸이 같은 청크 안에 있어야 한다 (옆 청크 지형은 보지 않는다)
            return IsObstacleFree(chunkCoord, chunk, localX + 1, localY) ||
                   IsObstacleFree(chunkCoord, chunk, localX - 1, localY) ||
                   IsObstacleFree(chunkCoord, chunk, localX, localY + 1) ||
                   IsObstacleFree(chunkCoord, chunk, localX, localY - 1);
        }

        /// <summary>청크 안 칸이 언덕·막힘·오브젝트·건물 점유가 아닌가. 청크 밖 칸은 false.</summary>
        private bool IsObstacleFree(Vector2Int chunkCoord, MapChunkData chunk, int localX, int localY)
        {
            if (localX < 0 || localY < 0 || localX >= _chunkSize || localY >= _chunkSize)
                return false;
            if (OreVeinRules.IsTerrainClear(chunk.tiles[localX, localY]) == false)
                return false;
            return AR.s.Building.IsTileOccupied(chunkCoord.x * _chunkSize + localX, chunkCoord.y * _chunkSize + localY) == false;
        }

        private void RemoveRecord(OreVeinRecord record)
        {
            var chunkCoord = new Vector2Int(record.ChunkX, record.ChunkY);
            _recordsByChunk.Remove(chunkCoord);
            AR.s.Data.OreVeins.Remove(record);
            AR.s.Data.MarkOreVeinSaveDirty(record.ChunkX, record.ChunkY);
            RemoveEntity(chunkCoord);
        }

        /// <summary>로드한 기록으로 색인을 만든다. 빈 칸·잘못된 값·청크 밖 칸·같은 청크 중복은 버린다 (다음 ore_veins 저장에서 빠진다).</summary>
        private void RebuildIndex()
        {
            _recordsByChunk.Clear();
            List<OreVeinRecord> veins = AR.s.Data.OreVeins;
            int write = 0;
            for (int i = 0; i < veins.Count; i++)
            {
                OreVeinRecord? record = veins[i];
                if (record == null || IsValidRecord(record) == false ||
                    _recordsByChunk.TryAdd(new Vector2Int(record.ChunkX, record.ChunkY), record) == false)
                    continue;
                if (float.IsNaN(record.LastActiveGameTime) == true || float.IsInfinity(record.LastActiveGameTime) == true)
                    record.LastActiveGameTime = 0f;
                veins[write++] = record;
            }
            int dropped = veins.Count - write;
            veins.RemoveRange(write, dropped);
            if (dropped > 0)
                Debug.LogWarning($"[OreVein] Dropped {dropped} invalid or duplicate ore vein records on load.");
        }

        private bool IsValidRecord(OreVeinRecord record)
        {
            if (TileToChunk(record.TileX, record.TileY) != new Vector2Int(record.ChunkX, record.ChunkY) ||
                record.Type == OreVeinType.None)
                return false;
            return record.State switch
            {
                OreVeinState.Active => record.Remaining > 0 && record.Remaining <= OreVeinRules.Charges,
                OreVeinState.Exhausted => float.IsNaN(record.RegrowGameTime) == false && float.IsInfinity(record.RegrowGameTime) == false,
                _ => false,
            };
        }

        private Vector2Int TileToChunk(int worldX, int worldY)
            => new(Mathf.FloorToInt((float)worldX / _chunkSize), Mathf.FloorToInt((float)worldY / _chunkSize));

        // ================= 엔티티 =================

        private void SpawnIfNeeded(Vector2Int chunkCoord, OreVeinRecord record)
        {
            if (_entityByChunk.ContainsKey(chunkCoord) == true || _spawning.Add(chunkCoord) == false)
                return;
            SpawnAsync(chunkCoord, record).Forget();
        }

        private async UniTaskVoid SpawnAsync(Vector2Int chunkCoord, OreVeinRecord record)
        {
            int lifetime = _lifetime;
            try
            {
                Sprite? sprite = await LoadSpriteAsync(record.Type);
                if (lifetime != _lifetime || sprite == null)
                    return;
                var center = new Vector2(record.TileX + 0.5f, record.TileY + 0.5f);
                var (id, entity) = await EntityFactory.CreateStaticEntity(AR.s.WorldMap, new Vector3(center.x, center.y, -0.05f),
                    sprite, _visualOffset, SortingOrder);
                if (id < 0 || entity == null)
                    return;
                // 생성 중에 청크가 꺼졌거나 기록이 바뀌었으면(고갈·마을 건설·정리) 버린다
                if (lifetime != _lifetime || IsCurrent(chunkCoord, record) == false ||
                    AR.s.WorldMap.IsChunkActive(chunkCoord) == false || _entityByChunk.ContainsKey(chunkCoord) == true)
                {
                    EntityFactory.ReleaseStaticEntity(entity);
                    return;
                }
                AR.s.Component.AddComponent(id, new OreVeinComponent
                    { ChunkX = chunkCoord.x, ChunkY = chunkCoord.y, RollDay = record.RollDay });
                entity.name = $"Ore Vein {record.Type} ({record.TileX},{record.TileY})";
                _entityByChunk[chunkCoord] = id;
            }
            catch (Exception ex) { Debug.LogError("[OreVein] Spawn: " + ex.Message); }
            finally { if (lifetime == _lifetime) _spawning.Remove(chunkCoord); }
        }

        private bool IsCurrent(Vector2Int chunkCoord, OreVeinRecord record)
            => _recordsByChunk.TryGetValue(chunkCoord, out OreVeinRecord? current) == true &&
               ReferenceEquals(current, record) == true && record.State == OreVeinState.Active;

        /// <summary>광종별 그림을 그 광종의 첫 생성 때 한 번 불러와 Reset까지 보관한다.</summary>
        private async UniTask<Sprite?> LoadSpriteAsync(OreVeinType type)
        {
            int index = (int)type;
            if (index <= 0 || index >= _spriteKeys.Length)
                return null;
            if (_sprites[index].IsValid() == false)
                _sprites[index] = Addressables.LoadAssetAsync<Sprite>(_spriteKeys[index]);
            AsyncOperationHandle<Sprite> handle = _sprites[index];
            try { await handle.ToUniTask(); }
            catch (Exception ex)
            {
                if (_spriteErrorLogged == false)
                {
                    _spriteErrorLogged = true;
                    Debug.LogError($"[OreVein] Ore vein sprite '{_spriteKeys[index]}' failed to load: {ex.Message}");
                }
                return null;
            }
            if (handle.IsValid() == false || handle.Status != AsyncOperationStatus.Succeeded)
                return null;
            return handle.Result;
        }

        private void RemoveEntity(Vector2Int chunkCoord)
        {
            if (_entityByChunk.TryGetValue(chunkCoord, out int id) == false)
                return;
            _entityByChunk.Remove(chunkCoord);
            ComponentManager cm = AR.s.Component;
            // EntityId는 재사용될 수 있다: 이 매니저가 아직 소유한 광맥 엔티티만 지운다
            if (cm.TryGetComponent<OreVeinComponent>(id, out var vein) == false ||
                vein.ChunkX != chunkCoord.x || vein.ChunkY != chunkCoord.y)
                return;
            cm.AddComponent(id, new DestroyTag());
            AR.s.System.GetSystem<System_EntityDestroy>()?.OnLateUpdate(0);
        }

        /// <summary>채광 진행 막대(임시)를 플레이어 엔티티 자식으로 한 번 붙인다. 이후 System_PlayerMining의 메시지로만 갱신된다.</summary>
        private static void AttachProgressView(int playerId)
        {
            if (AR.s.Message.TryGetEntity(playerId, out var entity) == false || entity == null ||
                entity.GetComponentInChildren<MiningProgressView>(true) != null)
                return;
            var view = new GameObject("Mining progress");
            view.transform.SetParent(entity.transform, false);
            view.AddComponent<MiningProgressView>().RegisterTo(entity);
        }

        private void RemoveAllEntities()
        {
            _chunkScratch.Clear();
            _chunkScratch.AddRange(_entityByChunk.Keys);
            for (int i = 0; i < _chunkScratch.Count; i++)
                RemoveEntity(_chunkScratch[i]);
            _chunkScratch.Clear();
        }
    }
}
