using System;
using System.Collections.Generic;
using ARPG.Component;
using ARPG.Data;
using ARPG.Factory;
using ARPG.Systems;
using ARPG.Village;
using UnityEngine;

namespace ARPG.Dungeon
{
    public partial class DungeonManager
    {
        // Static interactables keep the old marker layering and sprite placement above their tile center.
        private const int STATIC_ENTITY_SORTING_ORDER = 10;
        private static readonly Vector2 _chestVisualOffset = new(0f, -0.2f);

        private readonly Dictionary<int, int> _monsters = new();
        private readonly HashSet<int> _spawning = new();
        private readonly Dictionary<int, int> _chests = new();   // DungeonObject.Id → chest EntityId
        private readonly List<int> _contentIds = new();

        public void TickContent()
        {
            if (IsInside == false || _busy == true) return;
            int budget = 1;
            for (int i = 0; i < _active.Objects.Count; i++)
            {
                var obj = _active.Objects[i];
                bool nearby = ActiveMap.IsChunkActiveAtWorld(new Vector2(obj.X, obj.Y));
                if (obj.Kind == DungeonObjectKind.Chest)
                {
                    bool opened = _active.OpenedChests.Contains(obj.Id);
                    if (_chests.TryGetValue(obj.Id, out int chestId) == true)
                    {
                        if (nearby == false || opened == true)
                        {
                            RemoveDungeonEntity(chestId, obj.Id);
                            _chests.Remove(obj.Id);
                        }
                    }
                    else if (nearby == true && opened == false && _spawning.Add(obj.Id) == true)
                        SpawnChestAsync(obj);
                    continue;
                }
                if (_monsters.TryGetValue(obj.Id, out int entityId) == true)
                {
                    if (_components.TryGetComponent<DungeonObjectComponent>(entityId, out var identity) == false ||
                        identity.DungeonId != _active.DungeonId || identity.ObjectId != obj.Id)
                    {
                        _monsters.Remove(obj.Id);
                        continue;
                    }
                    if (_components.TryGetComponent<TransformComponent>(entityId, out var transform) == true)
                        nearby = ActiveMap.IsChunkActiveAtWorld(transform.Position);
                    if (nearby == false || _active.KilledObjects.Contains(obj.Id) == true)
                    {
                        CaptureMonster(obj.Id, entityId);
                        RemoveDungeonEntity(entityId, obj.Id);
                        _monsters.Remove(obj.Id);
                    }
                    continue;
                }
                if (_active.Monsters.TryGetValue(obj.Id, out var saved) == true)
                    nearby = ActiveMap.IsChunkActiveAtWorld(new Vector2(saved.X, saved.Y));
                if (nearby == true && budget > 0 && _active.KilledObjects.Contains(obj.Id) == false && _spawning.Add(obj.Id) == true)
                {
                    budget--;
                    SpawnMonsterAsync(obj);
                }
            }
        }

        public bool OpenChest(int objectId)
        {
            if (_busy == true || IsInside == false || TryGetChestInteraction(out var candidate) == false ||
                candidate.DungeonObjectId != objectId || _active.OpenedChests.Contains(objectId) == true) return false;
            long gold = (long)AR.s.Data.Player.Gold + _active.ChestGold;
            if (gold < 0 || gold > int.MaxValue) return false;
            AR.s.Data.MarkDungeonSaveDirty(_active, DungeonSaveGroup.Progress);
            _active.OpenedChests.Add(objectId);
            AR.s.Data.Player.Gold = (int)gold;
            if (_chests.TryGetValue(objectId, out int chestId) == true)
            {
                RemoveDungeonEntity(chestId, objectId);
                _chests.Remove(objectId);
            }
            // 골드에 더해 공통 드롭 판정 — 아이템은 상자 자리에 떨어진다 (ITEM_DROP_BALANCE_DESIGN §7.6)
            DungeonObject chest = FindObject(objectId);
            if (chest != null)
                RollChestDrops(chest);
            Notify($"상자를 열었습니다. 골드 +{_active.ChestGold}");
            return true;
        }

        private bool TryGetChestInteraction(out ServiceUIRouter.Candidate candidate)
        {
            candidate = default;
            var chests = _components.GetComponentPool<DungeonChestTag>();
            if (chests.Count == 0) return false;
            Vector2 player = PlayerPosition();
            float best = 9f;
            bool found = false;
            for (int i = 0; i < chests.Count; i++)
            {
                int entityId = chests.GetEntityId(i);
                if (_components.TryGetComponent<DungeonObjectComponent>(entityId, out var obj) == false ||
                    obj.DungeonId != _active.DungeonId || _active.OpenedChests.Contains(obj.ObjectId) == true ||
                    _components.TryGetComponent<TransformComponent>(entityId, out var transform) == false) continue;
                float distance = Vector2.SqrMagnitude(player - transform.Position);
                if (distance >= best) continue;
                best = distance;
                found = true;
                candidate = new ServiceUIRouter.Candidate { Kind = ServiceUIRouter.CandidateKind.DungeonChest,
                    DungeonObjectId = obj.ObjectId, DungeonId = _active.DungeonId, EntityId = -1,
                    HasTile = true, TileX = Mathf.FloorToInt(transform.Position.x), TileY = Mathf.FloorToInt(transform.Position.y),
                    DistanceSqr = distance };
            }
            return found;
        }

        private async void SpawnChestAsync(DungeonObject obj)
        {
            var map = ActiveMap;
            var record = _active;
            int lifetime = _lifetime;
            try
            {
                var position = new Vector3(obj.X + 0.5f, obj.Y + 0.5f, -0.05f);
                var (id, entity) = await EntityFactory.CreateStaticEntity(map, position, _chestSprite,
                    _chestVisualOffset, STATIC_ENTITY_SORTING_ORDER, map.transform);
                if (id < 0 || entity == null) return;
                if (lifetime != _lifetime || ActiveMap != map || record.OpenedChests.Contains(obj.Id) == true)
                {
                    EntityFactory.ReleaseStaticEntity(entity);
                    return;
                }
                _components.AddComponent(id, new DungeonObjectComponent { DungeonId = record.DungeonId, ObjectId = obj.Id });
                _components.AddComponent(id, new DungeonChestTag());
                entity.name = "Dungeon Chest " + obj.Id;
                _chests[obj.Id] = id;
            }
            catch (Exception ex) { Debug.LogError("[Dungeon] Chest spawn: " + ex.Message); }
            finally { if (lifetime == _lifetime && ActiveMap == map) _spawning.Remove(obj.Id); }
        }

        private async void SpawnMonsterAsync(DungeonObject obj)
        {
            var map = ActiveMap;
            var record = _active;
            int lifetime = _lifetime;
            try
            {
                var position = new Vector3(obj.X + 0.5f, obj.Y + 0.5f, -0.01f);
                if (record.Monsters.TryGetValue(obj.Id, out var saved) == true) position = new Vector3(saved.X, saved.Y, -0.01f);
                var (id, entity) = await EntityFactory.CreateMonster(obj.TableId, position, map.transform, obj.Level);
                if (id < 0 || entity == null) return;
                if (lifetime != _lifetime || ActiveMap != map || record.KilledObjects.Contains(obj.Id) == true)
                {
                    if (entity.IsCurrentEntity == true) RemoveDungeonEntity(id);
                    return;
                }
                _components.AddComponent(id, new DungeonObjectComponent { DungeonId = record.DungeonId,
                    ObjectId = obj.Id, IsBoss = obj.Kind == DungeonObjectKind.Boss });
                // 보스도 사망 드롭을 한다 — 보상 크기는 보스 등급(Elite) 행의 판정 횟수·희귀도 (ITEM_DROP_BALANCE_DESIGN §7.6).
                // 완료 골드는 완료 처리(DungeonCompletionTransaction)가 그대로 따로 준다.
                if (saved != null && _components.TryGetComponent<StatComponent>(id, out var stat) == true)
                {
                    stat.SetCurrentHp(id, Mathf.Clamp(saved.Hp, 1, Mathf.Max(1, stat.FinalMaxHp)));
                    _components.SetComponent(id, stat);
                    _components.AddComponent(id, new HpDirtyTag());
                }
                entity.name = obj.Kind == DungeonObjectKind.Boss ? "Dungeon Boss" : "Dungeon Monster";
                _monsters[obj.Id] = id;
            }
            catch (Exception ex) { Debug.LogError("[Dungeon] Monster spawn: " + ex.Message); }
            finally { if (lifetime == _lifetime && ActiveMap == map) _spawning.Remove(obj.Id); }
        }

        private void CaptureLivingMonsters()
        {
            _contentIds.Clear();
            _contentIds.AddRange(_monsters.Keys);
            for (int i = 0; i < _contentIds.Count; i++) CaptureMonster(_contentIds[i], _monsters[_contentIds[i]]);
        }

        private void CaptureMonster(int objectId, int entityId)
        {
            if (_active.KilledObjects.Contains(objectId) == true ||
                _components.TryGetComponent<DungeonObjectComponent>(entityId, out var identity) == false ||
                identity.DungeonId != _active.DungeonId || identity.ObjectId != objectId ||
                _components.TryGetComponent<StatComponent>(entityId, out var stat) == false ||
                _components.TryGetComponent<TransformComponent>(entityId, out var transform) == false) return;
            AR.s.Data.MarkDungeonSaveDirty(_active, DungeonSaveGroup.Monsters);
            _active.Monsters[objectId] = new DungeonMonsterState { Hp = stat.CurrentHp,
                X = transform.Position.x, Y = transform.Position.y };
        }

        private void RemoveDungeonEntity(int id, int objectId = -1)
        {
            // Entity IDs can be recycled after HP destruction: only remove our still-owned entity.
            if (objectId >= 0 && (_active == null ||
                _components.TryGetComponent<DungeonObjectComponent>(id, out var identity) == false ||
                identity.DungeonId != _active.DungeonId || identity.ObjectId != objectId)) return;
            if (_components.TryGetComponent<DungeonObjectComponent>(id, out _) == false &&
                _components.HasComponent<MonsterTag>(id) == false) return;
            DestroyOwnedEntity(id);
        }

        private void DestroyOwnedEntity(int id)
        {
            _components.AddComponent(id, new DestroyTag());
            AR.s.System.GetSystem<System_EntityDestroy>()?.OnLateUpdate(0);
        }

        private void ResetContent()
        {
            _contentIds.Clear(); _contentIds.AddRange(_monsters.Keys);
            for (int i = 0; i < _contentIds.Count; i++) RemoveDungeonEntity(_monsters[_contentIds[i]], _contentIds[i]);
            _contentIds.Clear(); _contentIds.AddRange(_chests.Keys);
            for (int i = 0; i < _contentIds.Count; i++) RemoveDungeonEntity(_chests[_contentIds[i]], _contentIds[i]);
            _monsters.Clear(); _spawning.Clear(); _chests.Clear(); _contentIds.Clear();
        }
    }
}
