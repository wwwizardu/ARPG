using System;
using System.Collections.Generic;
using ARPG.Component;
using ARPG.Data;
using ARPG.Factory;
using ARPG.Village;
using UnityEngine;

namespace ARPG.Dungeon
{
    public partial class DungeonManager
    {
        private static readonly Vector2 _entranceVisualOffset = new(0f, -0.3f);

        private readonly Dictionary<int, VillageDungeonSlots> _villageSlots = new();
        private readonly Dictionary<string, DungeonSlot> _slotsById = new();
        private readonly Dictionary<string, VillageDungeonSlots> _ownersById = new();
        private readonly Dictionary<Vector2Int, List<DungeonSlot>> _entrancesByChunk = new();
        private readonly Dictionary<string, int> _entrances = new();   // DungeonId → entrance EntityId
        private readonly HashSet<string> _entranceSpawning = new();
        private int _entranceGeneration;   // Index rebuilds discard entrance spawns started before them.
        private readonly List<Vector2Int> _visibleChunks = new();
        private readonly List<string> _entranceKeys = new();
        private Sprite _entranceSprite;
        private Sprite _chestSprite;
        private int _villageCursor;

        public void RegisterVillageSlots()
        {
            if (_initialized == false) return;
            _villageSlots.Clear();
            int defaultTableId = Mathf.Max(1, Settings.DefaultDungeonTableId);
            var defaultTable = AR.s.Data.GetDungeonTable(defaultTableId);
            if (defaultTable != null)
                DungeonLayout.NormalizeTableReferences(AR.s.Data.Dungeons, defaultTable);
            // NPC 완료 필드(D58) 로드 보정 — 구 세이브는 기본값 -1, 비정상 값은 "기록 없음"으로
            DungeonLayout.NormalizeNpcCompletionFields(AR.s.Data.Dungeons);
            var saved = AR.s.Data.Dungeons.Villages;
            for (int i = 0; i < saved.Count; i++)
            {
                VillageDungeonSlots villageSlots = saved[i];
                _villageSlots[villageSlots.VillageId] = villageSlots;
            }
            var villages = AR.s.Village.GetAllVillages();
            for (int i = 0; i < villages.Count; i++)
            {
                int id = villages[i].VillageId;
                if (_villageSlots.ContainsKey(id) == true) continue;
                var slots = DungeonLayout.CreateSlots(AR.s.Data.WorldSeed, id, defaultTableId);
                saved.Add(slots);
                _villageSlots.Add(id, slots);
            }
            RefreshNarrativeSelections();
        }

        public void TickPlacement()
        {
            RefreshEntranceIndexIfDirty();
            if (_initialized == false || IsInside == true || _busy == true || AR.s.MyPlayer == null || AR.s.Data.IsSaving == true) return;
            var villages = AR.s.Village.GetAllVillages();
            if (villages.Count == 0) return;
            var watch = System.Diagnostics.Stopwatch.StartNew();
            int candidates = 0;
            int queries = 0;
            float currentGameTime = AR.s.Time.CurrentGameTime;
            for (int scanned = 0; scanned < 12 && scanned < villages.Count; scanned++)
            {
                if (_villageCursor >= villages.Count) _villageCursor = 0;
                var village = villages[_villageCursor++];
                float radius = Mathf.Max(village.BoundsW, village.BoundsH) * 0.5f + Settings.ApproachDistance;
                if (Vector2.SqrMagnitude(PlayerPosition() - village.Position) > radius * radius) continue;
                if (_villageSlots.TryGetValue(village.VillageId, out var slots) == false)
                {
                    RegisterVillageSlots();
                    RebuildEntranceIndex();
                    return;
                }
                for (int s = 0; s < slots.Slots.Count; s++)
                {
                    var slot = slots.Slots[s];
                    if (slot.HasEntrance == true) continue;
                    if (DungeonLayout.IsPlacementReady(slot, currentGameTime) == false) continue;
                    // Fixed slot order and deterministic territory prevent approach-order placement races.
                    for (; candidates < Mathf.Max(1, Settings.CandidatesPerFrame) &&
                           queries + 9 <= Mathf.Max(9, Settings.TileQueriesPerFrame); candidates++)
                    {
                        AR.s.Data.MarkDungeonSlotsSaveDirty(village.VillageId);
                        int attempt = slot.NextCandidate++;
                        CandidatePosition(village, slot, attempt, out int x, out int y);
                        bool valid = ValidateEntrance(village, slot, x, y, ref queries);
                        if (valid == true)
                        {
                            PlaceEntranceAsync(slot, x, y);
                            return;
                        }
                        if (watch.Elapsed.TotalMilliseconds >= Settings.PlacementMilliseconds) return;
                    }
                    return;
                }
                if (watch.Elapsed.TotalMilliseconds >= Settings.PlacementMilliseconds) return;
            }
        }

        public void RefreshEntranceChunk(Vector2Int coord, bool active)
        {
            if (_entrancesByChunk.TryGetValue(coord, out var slots) == false) return;
            for (int i = 0; i < slots.Count; i++)
            {
                var slot = slots[i];
                if (active == true && slot.HasEntrance == true)
                {
                    if (_entrances.ContainsKey(slot.DungeonId) == true || _entranceSpawning.Add(slot.DungeonId) == false) continue;
                    SpawnEntranceAsync(slot);
                }
                else RemoveEntrance(slot.DungeonId);
            }
        }

        public bool TryGetInteraction(out ServiceUIRouter.Candidate candidate)
        {
            candidate = default;
            if (GameplayPaused || _initialized == false) return false;
            if (IsInside) return TryGetChestInteraction(out candidate);
            var entrances = _components.GetComponentPool<DungeonEntranceComponent>();
            if (entrances.Count == 0) return false;
            Vector2 position = PlayerPosition();
            float best = 9f;
            bool found = false;
            for (int i = 0; i < entrances.Count; i++)
            {
                int entityId = entrances.GetEntityId(i);
                string dungeonId = entrances.GetByIndex(i).DungeonId;
                if (_slotsById.TryGetValue(dungeonId, out var slot) == false || slot.HasEntrance == false ||
                    _components.TryGetComponent<TransformComponent>(entityId, out var transform) == false) continue;
                float distance = Vector2.SqrMagnitude(position - transform.Position);
                if (distance >= best) continue;
                best = distance;
                found = true;
                candidate = new ServiceUIRouter.Candidate { Kind = ServiceUIRouter.CandidateKind.Dungeon,
                    DungeonId = dungeonId, EntityId = -1, HasTile = true,
                    TileX = slot.EntranceX, TileY = slot.EntranceY, DistanceSqr = distance };
            }
            return found;
        }

        private async void SpawnEntranceAsync(DungeonSlot slot)
        {
            int lifetime = _lifetime;
            int generation = _entranceGeneration;
            string dungeonId = slot.DungeonId;
            try
            {
                // The root sits on the tile center so the E-distance matches the entering rule in EnterAsync.
                var center = new Vector2(slot.EntranceX + 0.5f, slot.EntranceY + 0.5f);
                var (id, entity) = await EntityFactory.CreateStaticEntity(AR.s.WorldMap, new Vector3(center.x, center.y, -0.05f),
                    _entranceSprite, _entranceVisualOffset, STATIC_ENTITY_SORTING_ORDER);
                if (id < 0 || entity == null) return;
                if (lifetime != _lifetime || generation != _entranceGeneration || IsInside == true ||
                    _slotsById.TryGetValue(dungeonId, out var current) == false || current != slot || slot.HasEntrance == false ||
                    AR.s.WorldMap.IsChunkActiveAtWorld(center) == false || _entrances.ContainsKey(dungeonId) == true)
                {
                    EntityFactory.ReleaseStaticEntity(entity);
                    return;
                }
                _components.AddComponent(id, new DungeonEntranceComponent { DungeonId = dungeonId });
                AttachEntranceSign(id, entity, dungeonId);
                entity.AutoRegisterChildHandlers();
                entity.name = "Dungeon Entrance " + dungeonId;
                _entrances[dungeonId] = id;
            }
            catch (Exception ex) { Debug.LogError("[Dungeon] Entrance spawn: " + ex.Message); }
            finally { if (lifetime == _lifetime && generation == _entranceGeneration) _entranceSpawning.Remove(dungeonId); }
        }

        private void RemoveEntrance(string dungeonId)
        {
            if (_entrances.TryGetValue(dungeonId, out int id) == false) return;
            _entrances.Remove(dungeonId);
            // Entity IDs can be recycled: only destroy the entrance entity this manager still owns.
            if (_components.TryGetComponent<DungeonEntranceComponent>(id, out var entrance) == false ||
                entrance.DungeonId != dungeonId) return;
            DestroyOwnedEntity(id);
        }

        private void RemoveAllEntrances()
        {
            _entranceKeys.Clear();
            _entranceKeys.AddRange(_entrances.Keys);
            for (int i = 0; i < _entranceKeys.Count; i++) RemoveEntrance(_entranceKeys[i]);
            _entranceKeys.Clear();
            // In-flight spawns from before this point are discarded by the generation check.
            _entranceGeneration++;
            _entranceSpawning.Clear();
        }

        private async void PlaceEntranceAsync(DungeonSlot slot, int x, int y)
        {
            _busy = true;
            int lifetime = _lifetime;
            if (_ownersById.TryGetValue(slot.DungeonId, out var owner) == true) AR.s.Data.MarkDungeonSlotsSaveDirty(owner.VillageId);
            slot.EntranceX = x;
            slot.EntranceY = y;
            slot.HasEntrance = true;
            slot.PlacementReadyGameTime = 0f;
            try
            {
                bool saved = await CommitAsync(SaveScope.Dungeons | SaveScope.World);
                if (lifetime != _lifetime) return;
                if (saved == false)
                {
                    if (_ownersById.TryGetValue(slot.DungeonId, out var failedOwner) == true) AR.s.Data.MarkDungeonSlotsSaveDirty(failedOwner.VillageId);
                    slot.HasEntrance = false;
                    slot.NextCandidate = Mathf.Max(0, slot.NextCandidate - 1);
                    return;
                }
                RebuildEntranceIndex();
            }
            catch (Exception ex) { if (_ownersById.TryGetValue(slot.DungeonId, out var failedOwner) == true) AR.s.Data.MarkDungeonSlotsSaveDirty(failedOwner.VillageId); slot.HasEntrance = false; Debug.LogError("[Dungeon] Placement: " + ex.Message); }
            finally { if (lifetime == _lifetime) _busy = false; }
        }

        private void CandidatePosition(VillageData village, DungeonSlot slot, int attempt, out int x, out int y)
        {
            uint hash = DungeonLayout.Hash(AR.s.Data.WorldSeed, village.VillageId, slot.SlotIndex,
                slot.Generation, unchecked(101 + attempt));
            int radius = Mathf.Max(village.BoundsW, village.BoundsH) / 2 + 8 + (int)(hash % 36);
            int offset = (int)((hash >> 8) % (uint)(radius * 2 + 1)) - radius;
            x = Mathf.FloorToInt(village.PositionX);
            y = Mathf.FloorToInt(village.PositionY);
            switch ((hash >> 24) & 3)
            {
                case 0: x += radius; y += offset; break;
                case 1: x -= radius; y += offset; break;
                case 2: y += radius; x += offset; break;
                default: y -= radius; x += offset; break;
            }
        }

        private bool ValidateEntrance(VillageData owner, DungeonSlot slot, int x, int y, ref int queries)
        {
            if (slot.HasPreviousEntrance == true && slot.PreviousX == x && slot.PreviousY == y) return false;
            var position = new Vector2(x + 0.5f, y + 0.5f);
            float ownDistance = Vector2.SqrMagnitude(position - owner.Position);
            var villages = AR.s.Village.GetAllVillages();
            for (int i = 0; i < villages.Count; i++)
            {
                var village = villages[i];
                if (x >= village.BoundsX - 3 && x <= village.BoundsX + village.BoundsW + 3 &&
                    y >= village.BoundsY - 3 && y <= village.BoundsY + village.BoundsH + 3) return false;
                if (village.VillageId != owner.VillageId &&
                    Vector2.SqrMagnitude(position - village.Position) <= ownDistance * 1.4f + 64f) return false;
            }
            var otherSlots = _villageSlots[owner.VillageId].Slots;
            for (int i = 0; i < otherSlots.Count; i++)
                if (otherSlots[i].HasEntrance == true && Vector2.SqrMagnitude(position -
                    new Vector2(otherSlots[i].EntranceX + 0.5f, otherSlots[i].EntranceY + 0.5f)) < 64f) return false;
            for (int dx = -1; dx <= 1; dx++)
                for (int dy = -1; dy <= 1; dy++)
                {
                    queries++;
                    if (AR.s.WorldMap.IsEntranceTileClear(x + dx, y + dy) == false) return false;
                    // 광맥 칸은 피한다 (WORLD_ORE_VEIN_PLAN §6.2)
                    if (AR.s.OreVein.IsVeinTile(x + dx, y + dy) == true) return false;
                }
            return true;
        }

        /// <summary>
        /// 칸 중심에서 distance 미만 거리에 놓인 던전 입구가 있는가 (광맥 위치 조건, WORLD_ORE_VEIN_PLAN §5).
        /// 청크별 입구 목록에서 주변 청크만 보므로 지형을 조회하지 않는다.
        /// </summary>
        public bool HasEntranceWithin(int x, int y, float distance)
        {
            int size = AR.s.WorldMap.chunkSize;
            int reach = Mathf.Max(1, Mathf.CeilToInt(distance / size));
            int chunkX = Mathf.FloorToInt((float)x / size);
            int chunkY = Mathf.FloorToInt((float)y / size);
            var center = new Vector2(x + 0.5f, y + 0.5f);
            float limit = distance * distance;
            for (int cx = chunkX - reach; cx <= chunkX + reach; cx++)
                for (int cy = chunkY - reach; cy <= chunkY + reach; cy++)
                {
                    if (_entrancesByChunk.TryGetValue(new Vector2Int(cx, cy), out var slots) == false) continue;
                    for (int i = 0; i < slots.Count; i++)
                        if (slots[i].HasEntrance == true && Vector2.SqrMagnitude(center -
                            new Vector2(slots[i].EntranceX + 0.5f, slots[i].EntranceY + 0.5f)) < limit) return true;
                }
            return false;
        }

        private bool TryFindSlot(string id, out VillageDungeonSlots owner, out DungeonSlot slot)
        {
            _ownersById.TryGetValue(id, out owner);
            return _slotsById.TryGetValue(id, out slot) && owner != null;
        }

        private void RebuildEntranceIndex()
        {
            RemoveAllEntrances();
            _entrancesByChunk.Clear();
            _slotsById.Clear();
            _ownersById.Clear();
            var villages = AR.s.Data.Dungeons.Villages;
            for (int v = 0; v < villages.Count; v++)
                for (int s = 0; s < villages[v].Slots.Count; s++)
                {
                    var slot = villages[v].Slots[s];
                    _slotsById[slot.DungeonId] = slot;
                    _ownersById[slot.DungeonId] = villages[v];
                    if (slot.HasEntrance == false) continue;
                    int size = AR.s.WorldMap.chunkSize;
                    var coord = new Vector2Int(Mathf.FloorToInt((float)slot.EntranceX / size), Mathf.FloorToInt((float)slot.EntranceY / size));
                    if (_entrancesByChunk.TryGetValue(coord, out var list) == false)
                        _entrancesByChunk.Add(coord, list = new List<DungeonSlot>());
                    list.Add(slot);
                }
            if (IsInside == true) return;
            AR.s.WorldMap.GetActiveChunkCoordsNonAlloc(_visibleChunks);
            for (int i = 0; i < _visibleChunks.Count; i++) RefreshEntranceChunk(_visibleChunks[i], true);
        }

        private void LoadMarkerSprites()
        {
            _entranceSprite = Resources.Load<Sprite>("Dungeon/DungeonEntrance");
            _chestSprite = Resources.Load<Sprite>("Dungeon/DungeonChest");
            if (_entranceSprite == null || _chestSprite == null)
                Debug.LogError("[Dungeon] Dungeon entrance or chest sprite is missing from Resources/Dungeon.");
        }

        private void ResetEntrances()
        {
            RemoveAllEntrances();
            _entranceSprite = null;
            _chestSprite = null;
            _villageSlots.Clear(); _slotsById.Clear(); _ownersById.Clear();
            _entrancesByChunk.Clear(); _visibleChunks.Clear(); _entranceKeys.Clear(); _villageCursor = 0;
        }
    }
}
