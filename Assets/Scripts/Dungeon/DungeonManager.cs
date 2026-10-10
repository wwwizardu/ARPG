using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using ARPG.Component;
using ARPG.Data;
using ARPG.Map;
using ARPG.Systems;
using Newtonsoft.Json;
using UnityEngine;

namespace ARPG.Dungeon
{
    /// <summary>Owns dungeon lifetimes and checkpoint transactions; systems schedule the work.</summary>
    public partial class DungeonManager
    {
        private ComponentManager _components;
        private List<ComponentManager.SuspendedEntity> _suspendedWorld = new();
        private DungeonRecord _active;
        private bool _initialized;
        private bool _busy;
        private bool _playerSaveOverride;
        private bool _committing;
        private bool _completionPending;
        private bool _deathPending;
        private bool _menuOpen;
        private bool _recoveryRequired;
        private int _lifetime;
        private float _nextCheckpoint;
        private float _nextRetry;

        public DungeonSettings Settings { get; private set; } = new();
        public MapManager ActiveMap { get; private set; }
        public bool IsInside => ActiveMap != null;
        public bool IsBusy => _busy || _recoveryRequired;
        public bool IsInitialized => _initialized;
        public bool IsCommitting => _committing;
        public bool CompletionPending => _completionPending;
        public bool HasPlayerSaveOverride => _playerSaveOverride;
        public bool RequiresCheckpoint => IsInside || _busy || AR.s.Data.Player.CurrentMapKind == MapKind.Dungeon;
        public bool GameplayPaused => IsBusy || _deathPending ||
                                      (_completionPending == true &&
                                       (AR.s.Npc == null || AR.s.Npc.IsPlayerInDuel == false)) ||
                                      (IsInside && _menuOpen);
        public DungeonRecord ActiveRecord => _active;

        public void Initialize()
        {
            _components = AR.s.Component;
            var settingsAsset = Resources.Load<TextAsset>("DungeonSettings");
            Settings = settingsAsset == null ? new DungeonSettings() :
                JsonConvert.DeserializeObject<DungeonSettings>(settingsAsset.text) ?? new DungeonSettings();
            LoadMarkerSprites();
            _initialized = true;
            RegisterVillageSlots();
            RebuildEntranceIndex();
            _nextCheckpoint = Time.unscaledTime + Settings.AutoSaveSeconds;
        }

        public void SetMenuOpen(bool value) { _menuOpen = value; }

        public void Reset()
        {
            _lifetime++;
            _initialized = false;
            if (_components != null) _components.RestoreSuspended(_suspendedWorld);
            ResetContent();
            ResetEntrances();
            ResetNpcCompletion();
            if (ActiveMap != null)
            {
                ActiveMap.Reset();
                UnityEngine.Object.Destroy(ActiveMap.gameObject);
            }
            ActiveMap = null;
            _active = null;
            _busy = _playerSaveOverride = _committing = _completionPending = _deathPending = _menuOpen = false;
            _recoveryRequired = false;
            _nextCheckpoint = _nextRetry = 0;
        }

        public async Task<bool> EnterAsync(string dungeonId)
        {
            if (_initialized == false || _busy == true || IsInside == true ||
                TryFindSlot(dungeonId, out var villageSlots, out var slot) == false || slot.HasEntrance == false)
                return false;
            Vector2 playerPosition = PlayerPosition();
            Vector2 entrance = new Vector2(slot.EntranceX + 0.5f, slot.EntranceY + 0.5f);
            if (Vector2.SqrMagnitude(playerPosition - entrance) > 9f ||
                AR.s.WorldMap.IsChunkActiveAtWorld(entrance) == false) return false;
            if (AR.s.Npc.IsPlayerInDuel == true)
            {
                Notify("결투 중에는 던전에 입장할 수 없습니다.");
                return false;
            }
            _busy = true;
            int lifetime = _lifetime;
            DungeonRecord record = FindRecord(dungeonId);
            bool created = false;
            PlayerMapSnapshot before = default;
            bool staged = false;
            bool companionStaged = false;
            try
            {
                await DrainAsync(AR.s.WorldMap.Context);
                if (lifetime != _lifetime) return false;
                if (record == null)
                {
                    int tableId = slot.DungeonTableId > 0
                        ? slot.DungeonTableId
                        : Settings.DefaultDungeonTableId;
                    var table = AR.s.Data.GetDungeonTable(tableId);
                    if (table == null)
                    {
                        Notify($"DungeonTable Id {tableId}를 찾을 수 없어 입장을 취소했습니다.");
                        return false;
                    }
                    record = DungeonLayout.Generate(AR.s.Data.WorldSeed, villageSlots.VillageId, slot, table);
                    if (EnsureNarrativeTargetPlacement(record) == false)
                    {
                        Notify("소문에 나온 몬스터를 배치할 수 없어 입장을 취소했습니다.");
                        return false;
                    }
                    created = true;
                }
                if (DungeonLayout.Validate(record, out string error) == false)
                {
                    Notify(error);
                    return false;
                }
                if (AR.s.WorldMap.CanCreateDungeonMap == false)
                {
                    Notify("던전 타일맵 참조가 없어 입장을 취소했습니다.");
                    return false;
                }
                AR.s.Player.Save(AR.s.Data.Player);
                before = new PlayerMapSnapshot(AR.s.Data.Player);
                var player = AR.s.Data.Player;
                var room = record.Rooms[0];
                player.WorldReturnX = entrance.x;
                player.WorldReturnY = entrance.y - 1f;
                player.PositionX = player.WorldReturnX;
                player.PositionZ = player.WorldReturnY;
                player.CurrentMapKind = MapKind.Dungeon;
                player.ActiveDungeonId = dungeonId;
                player.DungeonPositionX = room.X + room.Width / 2 + 0.5f;
                player.DungeonPositionY = room.Y + room.Height / 2 + 0.5f;
                _playerSaveOverride = true;
                staged = true;
                if (created == true) AR.s.Data.Dungeons.Dungeons.Add(record);
                // 임시 동행 예약이 있으면 입장 커밋(World)에 동행 진행 상태를 함께 싣는다 (NPC_COMBAT_DESIGN §4.5)
                companionStaged = AR.s.Npc.StageCompanionDungeonEntry();
                bool saved = await CommitAsync(SaveScope.Player | SaveScope.World | SaveScope.Map |
                                               SaveScope.NpcState | SaveScope.Dungeons);
                if (lifetime != _lifetime) return false;
                if (saved == false)
                {
                    before.Restore(player);
                    if (created == true) AR.s.Data.Dungeons.Dungeons.Remove(record);
                    if (companionStaged == true) AR.s.Npc.RevertCompanionDungeonEntry();
                    Notify("저장에 실패해 던전 입장을 취소했습니다.");
                    return false;
                }
                staged = false;
                if (lifetime != _lifetime) return false;
                ActivateInterior(record);
                // 월드 엔티티가 비실체화된 뒤이므로 같은 EntityId로 던전에 소환한다. 소환 실패는 입장을 실패시키지 않는다.
                await AR.s.Npc.SummonCompanionAsync();
                if (lifetime != _lifetime) return false;
                return true;
            }
            catch (Exception ex)
            {
                if (staged == true)
                {
                    before.Restore(AR.s.Data.Player);
                    if (created == true) AR.s.Data.Dungeons.Dungeons.Remove(record);
                    if (companionStaged == true) AR.s.Npc.RevertCompanionDungeonEntry();
                }
                else if (AR.s.Data.Player.CurrentMapKind == MapKind.Dungeon)
                    _recoveryRequired = true;
                Notify("던전 입장 오류: " + ex.Message);
                return false;
            }
            finally { if (lifetime == _lifetime) { _playerSaveOverride = false; _busy = false; } }
        }

        public async Task<bool> RestoreSessionAsync()
        {
            var player = AR.s.Data.Player;
            if (player.CurrentMapKind != MapKind.Dungeon) return true;
            var record = FindRecord(player.ActiveDungeonId);
            if (DungeonLayout.Validate(record, out string error) == false)
            {
                // Keep the stored session untouched. Never regenerate an unsupported active layout.
                _recoveryRequired = true;
                Notify(error + " 저장을 덮어쓰지 말고 호환 버전으로 복구하세요.");
                return false;
            }
            _busy = true;
            int lifetime = _lifetime;
            try
            {
                await DrainAsync(AR.s.WorldMap.Context);
                if (lifetime != _lifetime) return false;
                ActivateInterior(record);
                // 던전 안에서 저장·재접속했으면 동행 NPC를 플레이어 옆에 다시 소환한다 (NPC_COMBAT_DESIGN §4.5, B3).
                // 소환 실패는 복원을 실패시키지 않는다 — 퇴장 시 FinishCompanionDungeonExit가 마을로 되돌린다.
                await AR.s.Npc.SummonCompanionAsync();
                if (lifetime != _lifetime) return false;
                new System_StatCalculation().OnUpdate(0);
                if (_components.TryGetComponent<StatComponent>(player.PlayerId, out var restoredStat) == true)
                {
                    restoredStat.SetCurrentHp(player.PlayerId, Mathf.Clamp(player.CurrentHp, 0, restoredStat.FinalMaxHp));
                    restoredStat.CurrentMp = Mathf.Clamp(player.CurrentMp, 0, restoredStat.FinalMaxMp);
                    _components.SetComponent(player.PlayerId, restoredStat);
                }
                _deathPending = player.CurrentHp <= 0;
                return true;
            }
            catch (Exception ex)
            {
                _recoveryRequired = true;
                Notify("저장된 던전을 복구하지 못했습니다. 저장을 보존합니다: " + ex.Message);
                return false;
            }
            finally { if (lifetime == _lifetime) _busy = false; }
        }

        public async Task<bool> ExitAsync(bool death = false)
        {
            death = death || _deathPending;
            if (_busy == true || IsInside == false) return false;
            if (AR.s.Npc.IsPlayerInDuel == true)
            {
                Notify("결투 중에는 던전에서 나갈 수 없습니다.");
                return false;
            }
            if (_completionPending == true && await CompleteAsync() == false) return false;
            _busy = true;
            int lifetime = _lifetime;
            var player = AR.s.Data.Player;
            PlayerMapSnapshot before = default;
            bool staged = false;
            int companionId = -1;
            DungeonRecord record = _active;
            try
            {
                await DrainAsync(ActiveMap.Context);
                if (lifetime != _lifetime) return false;
                CaptureCheckpoint();
                AR.s.Player.Save(player);
                before = new PlayerMapSnapshot(player);
                if (TryFindReturnPosition(new Vector2(player.WorldReturnX, player.WorldReturnY), out var returnPosition) == false)
                {
                    Notify("입구 주변 복귀 경로가 막혀 있습니다. 공간을 확보한 뒤 다시 시도하세요.");
                    return false;
                }
                player.PositionX = returnPosition.x;
                player.PositionZ = returnPosition.y;
                player.CurrentMapKind = MapKind.World;
                player.ActiveDungeonId = string.Empty;
                if (death == true && _components.TryGetComponent<StatComponent>(player.PlayerId, out var stat) == true)
                    player.CurrentHp = Mathf.Max(1, stat.FinalMaxHp);
                _playerSaveOverride = true;
                staged = true;
                // 소문 해결은 보스 완료 커밋에서 이미 저장했다. 퇴장은 원장을 바꾸지 않는다 (NPC_NARRATIVE_CONTENT_DESIGN §7.4).
                if (record.Completed == true) AR.s.Data.Dungeons.Dungeons.Remove(record);
                // 퇴장(ESC·사망 공통) 시 동행 종료를 퇴장 커밋(World)에 함께 싣는다 (NPC_COMBAT_DESIGN §4.5)
                companionId = AR.s.Npc.StageCompanionDungeonExit();
                bool saved = await CommitAsync(SaveScope.Player | SaveScope.World |
                                               SaveScope.NpcState | SaveScope.Dungeons);
                if (lifetime != _lifetime) return false;
                if (saved == false)
                {
                    before.Restore(player);
                    if (record.Completed == true) AR.s.Data.Dungeons.Dungeons.Add(record);
                    AR.s.Npc.RevertCompanionDungeonExit(companionId);
                    Notify("저장에 실패했습니다. 던전에서 퇴장을 다시 시도할 수 있습니다.");
                    return false;
                }
                staged = false;
                if (lifetime != _lifetime) return false;
                StopTransientCombat();
                ResetContent();
                AR.s.Item.UnloadContextDrops();
                ActiveMap.Reset();
                UnityEngine.Object.Destroy(ActiveMap.gameObject);
                ActiveMap = null;
                _active = null;
                _components.RestoreSuspended(_suspendedWorld);
                // 월드 맵 기준으로 귀환 위치를 계산해야 하므로 ActiveMap 해제 뒤, 청크 재활성화(ResumeWorld) 전에 처리한다.
                AR.s.Npc.FinishCompanionDungeonExit(companionId, returnPosition);
                MovePlayer(returnPosition, death);
                AR.s.WorldMap.ResumeWorld(new Vector3(returnPosition.x, returnPosition.y, 0));
                AR.s.Item.RestoreContextDrops();
                _deathPending = false;
                return true;
            }
            catch (Exception ex)
            {
                if (staged == true)
                {
                    before.Restore(player);
                    if (record.Completed == true && AR.s.Data.Dungeons.Dungeons.Contains(record) == false)
                        AR.s.Data.Dungeons.Dungeons.Add(record);
                    AR.s.Npc.RevertCompanionDungeonExit(companionId);
                }
                Notify("던전 퇴장 오류: " + ex.Message);
                return false;
            }
            finally { if (lifetime == _lifetime) { _playerSaveOverride = false; _busy = false; } }
        }

        public bool HandleDeath(int entityId, KillCreditComponent credit = default)
        {
            if (IsInside == false) return false;
            if (entityId == AR.s.Data.CurrentPlayerEntityId)
            {
                _deathPending = true;
                return true; // Preserve the player entity before DestroyTag is added.
            }
            if (_components.TryGetComponent<DungeonObjectComponent>(entityId, out var obj) == false ||
                obj.DungeonId != _active.DungeonId) return false;
            AR.s.Data.MarkDungeonSaveDirty(_active, DungeonSaveGroup.Progress);
            // 보스 처치 공로(플레이어·동행)만 기록한다. 소문 대상 몬스터의 처치는 진행 판정에 쓰지 않는다 (NPC_NARRATIVE §3.1).
            if (_active.KilledObjects.Add(obj.ObjectId) && obj.IsBoss)
            {
                _active.BossKilledByPlayerParty = credit.PlayerParty;
                _completionPending = true;
            }
            return false;
        }

        public async Task<bool> CompleteAsync(SaveScope scope = SaveScope.Player | SaveScope.World | SaveScope.Dungeons)
        {
            if (_busy == true || IsInside == false) return false;
            if (AR.s.Npc.IsPlayerInDuel == true) return false;
            if (_active.Completed == true) { _completionPending = false; return true; }
            if (_completionPending == false || TryFindSlot(_active.DungeonId, out var village, out var slot) == false ||
                slot.Generation != _active.Generation) return false;
            _busy = true;
            int lifetime = _lifetime;
            try
            {
                await DrainAsync(ActiveMap.Context);
                if (lifetime != _lifetime) return false;
                if (DungeonCompletionTransaction.TryStage(AR.s.Data.Dungeons, AR.s.Data.Player,
                    _active, AR.s.Data.WorldSeed, AR.s.Time.CurrentGameTime,
                    _active.ReplacementCooldownHours, out var transaction) == false) return false;
                using (transaction)
                {
                    // 보스 처치 결과를 공통 원장에 같은 커밋으로 기록한다. 플레이어 측 처치면 소문 해결과 동행의 행적 소문,
                    // 아니면 미해결 종료다. 맵을 지우기 전에 해결 근거와 입구 위치를 원장에 남긴다 (NPC_NARRATIVE_CONTENT_DESIGN §7.4).
                    var narrative = AR.s.Npc.StageDungeonCompletion(_active, slot.EntranceX, slot.EntranceY,
                        CompanionInDungeonId(), AR.s.Time.CurrentGameTime);
                    bool committed = false;
                    try
                    {
                        if (await CommitAsync(scope | SaveScope.Player | SaveScope.World |
                                              SaveScope.NpcState | SaveScope.Dungeons | SaveScope.Narrative) == false)
                        {
                            Notify("보스 완료 저장 실패. 보상과 입구 교체를 함께 재시도합니다.");
                            return false;
                        }
                        transaction.Commit(); committed = true;
                    }
                    finally
                    {
                        if (committed == false && lifetime == _lifetime)
                            AR.s.Npc.RollbackDungeonCompletion(narrative);
                    }
                }
                if (lifetime != _lifetime) return false;
                _completionPending = false;
                RefreshNarrativeSelections();
                RebuildEntranceIndex();
                Notify("던전 완료! ESC 메뉴에서 던전 나오기를 선택하세요.");
                return true;
            }
            finally { if (lifetime == _lifetime) _busy = false; }
        }

        public void CaptureCheckpoint()
        {
            if (IsInside == false || _active == null) return;
            AR.s.Data.MarkDungeonSaveDirty(_active, DungeonSaveGroup.Tiles);
            _active.TileModifications = ActiveMap.SaveTileModifications();
            CaptureLivingMonsters();
        }

        public async void TickCheckpoint()
        {
            if (_initialized == false || _busy == true || IsInside == false || Time.unscaledTime < _nextRetry) return;
            _nextRetry = Time.unscaledTime + 3f;
            try
            {
                if (_completionPending == true && await CompleteAsync() == false) return;
                if (_deathPending == true) { await ExitAsync(true); return; }
                if (Time.unscaledTime < _nextCheckpoint) return;
                _nextCheckpoint = Time.unscaledTime + Mathf.Max(10f, Settings.AutoSaveSeconds);
                await AR.s.Data.SaveAsync(SaveScope.Player | SaveScope.World | SaveScope.Dungeons);
            }
            catch (Exception ex) { Notify("던전 체크포인트 오류: " + ex.Message); }
        }

        // 던전 안에 실제로 소환된 동행 NPC. 없으면 -1.
        private int CompanionInDungeonId()
        {
            var state = AR.s.Data.NpcCompanion;
            return state.IsInDungeon == true && state.ReservedNpcId >= 0 &&
                   _components.HasComponent<NpcCompanionComponent>(state.ReservedNpcId) == true
                ? state.ReservedNpcId : -1;
        }

        private async Task<bool> CommitAsync(SaveScope scope)
        {
            int lifetime = _lifetime;
            _committing = true;
            try { return await AR.s.Data.SaveDungeonTransactionAsync(scope); }
            finally { if (lifetime == _lifetime) _committing = false; }
        }

        private async Task DrainAsync(MapContext context)
        {
            await AR.s.Data.WaitForSaveIdleAsync();
            while (context.PendingLoads > 0) await Task.Yield();
        }

        private void ActivateInterior(DungeonRecord record)
        {
            StopTransientCombat();
            AR.s.Item.UnloadContextDrops();
            AR.s.WorldMap.SuspendWorld();
            _suspendedWorld = _components.SuspendExceptPlayer(AR.s.Data.CurrentPlayerEntityId);
            _active = record;
            // A queued checkpoint may have persisted the boss kill just before completion committed.
            // Resume the idempotent transaction even if the process ended between those two events.
            _completionPending = DungeonCompletionTransaction.NeedsCompletion(record);
            ActiveMap = AR.s.WorldMap.CreateDungeonMap(record);
            var p = AR.s.Data.Player;
            Vector2 position = new Vector2(p.DungeonPositionX, p.DungeonPositionY);
            if (DungeonLayout.IsFloor(record, Mathf.FloorToInt(position.x), Mathf.FloorToInt(position.y)) == false)
                position = new Vector2(record.Rooms[0].X + 2.5f, record.Rooms[0].Y + 2.5f);
            MovePlayer(position, false);
            ActiveMap.UpdateChunksAroundPlayer(new Vector3(position.x, position.y));
            AR.s.Item.RestoreContextDrops();
            _nextCheckpoint = Time.unscaledTime + Settings.AutoSaveSeconds;
        }

        private void MovePlayer(Vector2 position, bool revive)
        {
            int id = AR.s.Data.CurrentPlayerEntityId;
            if (_components.TryGetComponent<TransformComponent>(id, out var transform) == true)
            {
                transform.Position = position;
                _components.SetComponent(id, transform);
            }
            if (AR.s.MyPlayer != null) AR.s.MyPlayer.transform.position = new Vector3(position.x, position.y, -0.01f);
            if (_components.TryGetComponent<VelocityComponent>(id, out var velocity) == true)
            {
                velocity.Direction = Vector2.zero;
                _components.SetComponent(id, velocity);
            }
            _components.SetComponent(id, new InputComponent());
            _components.SetComponent(id, new PlayerNearbyServicesComponent
            {
                NearestNpcEntityId = -1, NearestShopEntityId = -1, NearestForgeEntityId = -1,
                NearestInnEntityId = -1, NearestShrineEntityId = -1, NearestCivicEntityId = -1,
                NearestHubEntityId = -1, NearestGateVillageId = -1, NearestVillageId = -1,
            });
            _components.RemoveComponent<JumpComponent>(id);
            _components.RemoveComponent<DestroyTag>(id);
            _components.RemoveComponent<KillCreditComponent>(id);
            _components.SetComponent(id, new StateComponent
            {
                Condition = Creature.CharacterConditions.Normal,
                ConditionPrev = Creature.CharacterConditions.Normal,
                MoveState = Creature.MovementStates.Idle,
                MovementStatePrev = Creature.MovementStates.Idle,
            });
            if (revive == true && _components.TryGetComponent<StatComponent>(id, out var stat) == true)
            {
                stat.SetCurrentHp(id, Mathf.Max(1, AR.s.Data.Player.CurrentHp));
                _components.SetComponent(id, stat);
                _components.AddComponent(id, new HpDirtyTag());
            }
        }

        private Vector2 PlayerPosition()
            => _components.TryGetComponent<TransformComponent>(AR.s.Data.CurrentPlayerEntityId, out var transform)
                ? transform.Position : Vector2.zero;

        private bool TryFindReturnPosition(Vector2 origin, out Vector2 result)
        {
            // Local safe tile search, with full collision radius clearance around the center.
            for (int radius = 0; radius <= 16; radius++)
                for (int x = -radius; x <= radius; x++)
                    for (int y = -radius; y <= radius; y++)
                    {
                        if (Mathf.Max(Mathf.Abs(x), Mathf.Abs(y)) != radius) continue;
                        int tx = Mathf.FloorToInt(origin.x) + x, ty = Mathf.FloorToInt(origin.y) + y;
                        if (AR.s.WorldMap.IsTileBlocked(tx, ty) == true) continue;
                        result = new Vector2(tx + 0.5f, ty + 0.5f);
                        return true;
                    }
            result = origin;
            return false;
        }

        private void StopTransientCombat()
        {
            var states = _components.GetComponentPool<SkillStateComponent>();
            for (int i = 0; i < states.Count; i++)
            {
                var state = states.GetByIndex(i);
                state.Reset();
                states.Set(states.GetEntityId(i), state);
            }
            ClearComponents<SkillCommandComponent>();
            ClearComponents<AttackTelegraphComponent>();
            ClearComponents<SkillLandingCommitComponent>();
            ClearComponents<SkillMovementPenaltyComponent>();
            MarkTransientForRemoval<ProjectileComponent>();
            MarkTransientForRemoval<AreaEffectComponent>();
            MarkTransientForRemoval<LifetimeComponent>();
            AR.s.System.GetSystem<System_EntityDestroy>()?.OnLateUpdate(0);
        }

        private void MarkTransientForRemoval<T>() where T : struct
        {
            var pool = _components.GetComponentPool<T>();
            for (int i = 0; i < pool.Count; i++) _components.AddComponent(pool.GetEntityId(i), new DestroyTag());
        }

        private void ClearComponents<T>() where T : struct
        {
            var pool = _components.GetComponentPool<T>();
            for (int i = pool.Count - 1; i >= 0; i--) _components.RemoveComponent<T>(pool.GetEntityId(i));
        }

        private DungeonRecord FindRecord(string id)
        {
            var records = AR.s.Data.Dungeons.Dungeons;
            for (int i = 0; i < records.Count; i++) if (records[i].DungeonId == id) return records[i];
            return null;
        }

        private static void Notify(string message)
        {
            Debug.Log("[Dungeon] " + message);
            AR.s.UI.SetNotify(message);
        }

        private readonly struct PlayerMapSnapshot
        {
            private readonly MapKind _kind;
            private readonly string _id;
            private readonly float _x, _y, _dx, _dy, _rx, _ry;
            private readonly int _hp;
            public PlayerMapSnapshot(PlayerData p)
            {
                _kind = p.CurrentMapKind; _id = p.ActiveDungeonId; _x = p.PositionX; _y = p.PositionZ;
                _dx = p.DungeonPositionX; _dy = p.DungeonPositionY; _rx = p.WorldReturnX; _ry = p.WorldReturnY; _hp = p.CurrentHp;
            }
            public void Restore(PlayerData p)
            {
                p.CurrentMapKind = _kind; p.ActiveDungeonId = _id; p.PositionX = _x; p.PositionZ = _y;
                p.DungeonPositionX = _dx; p.DungeonPositionY = _dy; p.WorldReturnX = _rx; p.WorldReturnY = _ry; p.CurrentHp = _hp;
            }
        }
    }
}
