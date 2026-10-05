#nullable enable
using System;
using ARPG.Data;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using UnityEngine;
using ARPG.AI;
using ARPG.Base;
using ARPG.Component;
using ARPG.Creature;
using ARPG.Factory;
using ARPG.Map;
using ARPG.Tables;
using ARPG.Utility;
using ARPG.Village;

namespace ARPG.Npc
{
    /// <summary>
    /// 임시 동행 요청 — 던전 전용 (NPC_COMBAT_DESIGN §4.5, B2).
    /// 예약·진행 상태는 World 파티션의 NpcCompanionState가 소유하고, 이 partial은 요청/취소·던전 소환·귀환 흐름만 담당한다.
    /// 던전 입장·퇴장 트랜잭션은 DungeonManager가 호출한다 (Stage → 커밋 → 소환/귀환, 실패 시 Revert).
    /// </summary>
    public partial class NpcManager
    {
        private const float COMPANION_SPAWN_Z = -0.05f;

        #region 요청 / 취소 (대화 메뉴)

        public bool IsCompanionReserved(int npcId)
        {
            return npcId >= 0 && AR.s.Data.NpcCompanion.ReservedNpcId == npcId;
        }

        /// <summary>던전 안에서 동행 중인 NPC인지. System_NpcSim의 활동 선택·판정 제외 조건.</summary>
        public bool IsCompanionInDungeon(int npcId)
        {
            NpcCompanionState state = AR.s.Data.NpcCompanion;
            return npcId >= 0 && state.ReservedNpcId == npcId && state.IsInDungeon == true;
        }

        /// <summary>
        /// 동행 요청 가능 여부. 임시 단계에서는 호감도 조건 없이 항상 수락하며, 상태 조건만 검사한다.
        /// </summary>
        public bool CanRequestCompanion(int npcId, out string reason)
        {
            reason = string.Empty;
            NpcSaveData? saveData = GetSaveData(npcId);
            if (saveData == null)
            {
                reason = "알 수 없는 NPC입니다.";
                return false;
            }
            if (saveData.Condition == CharacterConditions.Dead || saveData.Activity.Phase == NpcActivityPhase.Dead)
            {
                reason = "세상을 떠난 NPC입니다.";
                return false;
            }
            if (AR.s.Dungeon.IsInside == true)
            {
                reason = "던전 안에서는 동행을 요청할 수 없습니다.";
                return false;
            }

            NpcCompanionState state = AR.s.Data.NpcCompanion;
            if (state.ReservedNpcId == npcId)
            {
                reason = "이미 동행을 약속했습니다.";
                return false;
            }
            if (state.ReservedNpcId >= 0)
            {
                reason = $"이미 {GetDisplayName(state.ReservedNpcId)}와(과) 동행을 약속했습니다.";
                return false;
            }
            if (saveData.Activity.Phase == NpcActivityPhase.Wounded)
            {
                reason = "부상 중이라 동행할 수 없습니다.";
                return false;
            }
            if (saveData.Activity.Phase == NpcActivityPhase.Venturing || saveData.Activity.PartyId >= 0)
            {
                reason = "원정 중이라 동행할 수 없습니다.";
                return false;
            }

            return true;
        }

        public bool RequestCompanion(int npcId, out string message)
        {
            if (CanRequestCompanion(npcId, out message) == false)
                return false;

            AR.s.Data.MarkWorldSaveDirty(WorldSaveGroup.Companion);
            AR.s.Data.NpcCompanion.ReservedNpcId = npcId;
            AR.s.Data.NpcCompanion.IsInDungeon = false;
            message = $"{GetDisplayName(npcId)}: 좋아요, 다음에 들어가는 던전에 함께 가죠.";
            Debug.Log($"[NpcCompanion] 동행 예약 - EntityId: {npcId}");
            return true;
        }

        /// <summary>예약만 한 상태에서 대화로 취소한다. 던전 안 동행은 퇴장으로만 끝난다.</summary>
        public bool CancelCompanion(int npcId, out string message)
        {
            message = string.Empty;
            NpcCompanionState state = AR.s.Data.NpcCompanion;
            if (state.ReservedNpcId != npcId || state.IsInDungeon == true)
                return false;

            ClearCompanionState();
            message = $"{GetDisplayName(npcId)}: 알겠어요, 동행은 없던 걸로 하죠.";
            Debug.Log($"[NpcCompanion] 동행 예약 취소 - EntityId: {npcId}");
            return true;
        }

        #endregion

        #region 던전 입장 → 소환

        /// <summary>
        /// 던전 입장 커밋 직전에 호출. 예약 NPC가 소환 가능하면 IsInDungeon을 세워 입장 커밋에 함께 저장되게 한다.
        /// 죽었거나 부상(또는 원정) 중이면 예약을 취소하고 알린다.
        /// </summary>
        /// <returns>동행 진입을 스테이징했으면 true (커밋 실패 시 RevertCompanionDungeonEntry 호출)</returns>
        public bool StageCompanionDungeonEntry()
        {
            NpcCompanionState state = AR.s.Data.NpcCompanion;
            if (state.ReservedNpcId < 0)
                return false;

            if (TryCancelUnavailableCompanion(state.ReservedNpcId) == true)
                return false;

            AR.s.Data.MarkWorldSaveDirty(WorldSaveGroup.Companion);
            state.IsInDungeon = true;
            return true;
        }

        public void RevertCompanionDungeonEntry()
        {
            AR.s.Data.MarkWorldSaveDirty(WorldSaveGroup.Companion);
            AR.s.Data.NpcCompanion.IsInDungeon = false;
        }

        /// <summary>
        /// 던전 내부 활성화 뒤 예약 NPC를 플레이어 옆 통행 가능한 타일에 같은 EntityId로 실체화한다.
        /// 진영 Player + 전투 허용 태그 + Follow 상태로 시작한다. 예외는 모두 여기서 처리한다(입장 자체는 실패시키지 않음).
        /// </summary>
        public async UniTask SummonCompanionAsync()
        {
            try
            {
                NpcCompanionState state = AR.s.Data.NpcCompanion;
                if (state.IsInDungeon == false || state.ReservedNpcId < 0 || AR.s.Dungeon.IsInside == false)
                    return;

                int npcId = state.ReservedNpcId;
                MarkSaveDirty(npcId, NpcSaveGroup.Identity | NpcSaveGroup.Location | NpcSaveGroup.Activity);
                if (TryCancelUnavailableCompanion(npcId) == true)
                    return;

                NpcSaveData? saveData = GetSaveData(npcId);
                if (saveData == null)
                    return;

                // 같은 NPC가 두 곳에 동시에 존재하면 안 된다. 월드 쪽 엔티티는 입장 시 SuspendWorld의
                // 청크 비활성화(SaveAndDeactivateNpc)로 ECS 컴포넌트가 제거되어 있어야 한다.
                ComponentManager cm = AR.s.Component;
                if (saveData.IsActive == true || saveData.IsSpawning == true ||
                    cm.HasComponent<NpcTag>(npcId) == true || cm.HasComponent<TransformComponent>(npcId) == true)
                {
                    Debug.LogError($"[NpcCompanion] 월드 엔티티가 남아 있어 소환을 중단 - EntityId: {npcId}, " +
                                   $"IsActive: {saveData.IsActive}, IsSpawning: {saveData.IsSpawning}");
                    return;
                }
                // 같은 프레임에 비실체화된 월드 GameObject는 Destroy가 지연되어 아직 등록돼 있다. 매핑만 정리한다
                // (지연된 OnDestroy는 registered != this라 새 엔티티를 해제하지 않는다 — EntityBase.OnDestroy).
                if (AR.s.Message.TryGetEntity(npcId, out _) == true)
                    AR.s.Message.UnregisterEntity(npcId);

                int playerId = AR.s.Data.CurrentPlayerEntityId;
                if (AR.s.Component.TryGetComponent<TransformComponent>(playerId, out var playerTransform) == false)
                    return;

                float moveRadius = GetNpcMoveRadius(npcId, saveData);
                if (CollisionUtil.TryFindNearestFree(playerTransform.Position, moveRadius,
                        out Vector2 spawnPosition, requireActiveChunk: true) == false)
                    spawnPosition = playerTransform.Position;

                // IsSpawning: 생성 대기 중 월드 청크 활성화 경로의 이중 스폰을 막는다 (SpawnNpc와 동일 규칙)
                saveData.IsSpawning = true;
                int createdId;
                EntityBase? created;
                try
                {
                    (createdId, created) = await EntityFactory.CreateNpc(saveData.NpcTableId,
                        new Vector3(spawnPosition.x, spawnPosition.y, COMPANION_SPAWN_Z), _npcParent, npcId, saveData);
                }
                finally
                {
                    saveData.IsSpawning = false;
                }

                MarkSaveDirty(npcId, NpcSaveGroup.Identity | NpcSaveGroup.Location | NpcSaveGroup.Activity); // await 이후 다시 표시한다.
                if (createdId < 0 || created == null)
                {
                    Debug.LogWarning($"[NpcCompanion] 동행 NPC 생성 실패 - EntityId: {npcId}");
                    return;
                }

                // await 도중 퇴장했거나 예약이 바뀌었으면 생성물을 폐기한다 (orphan 방지)
                if (AR.s.Dungeon.IsInside == false || state.IsInDungeon == false || state.ReservedNpcId != npcId)
                {
                    AR.s.Entity.DestroyEntity(createdId, allowRecycle: false, keepRegistered: true);
                    Destroy(created.gameObject);
                    return;
                }

                // 월드 청크 인덱스에서 빼 둔다. 퇴장 시 소속 마을 위치로 다시 등록한다.
                RemoveNpcFromChunk(PositionToChunk(saveData.Position), npcId);
                saveData.EntityId = createdId;
                saveData.IsActive = true;

                if (cm.TryGetComponent<NpcVillageComponent>(createdId, out var village) == true)
                {
                    village.VillageId = saveData.VillageId;
                    cm.SetComponent(createdId, village);
                }

                cm.SetComponent(createdId, new FactionComponent { FactionId = Faction.Player });
                cm.AddComponent(createdId, new NpcCombatEnabledTag());
                cm.AddComponent(createdId, new NpcCompanionComponent { LeaderEntityId = playerId, PathFailedSince = 0f });
                AIStateHelper.TransitionToState(createdId, AIState.Follow);

                NotifyCompanion($"{GetDisplayName(npcId)}이(가) 동행합니다.");
                Debug.Log($"[NpcCompanion] 던전 소환 - EntityId: {createdId}, Position: {spawnPosition}");
            }
            catch (Exception ex)
            {
                Debug.LogError("[NpcCompanion] 동행 NPC 소환 오류: " + ex);
            }
        }

        #endregion

        #region 던전 퇴장 → 귀환

        /// <summary>
        /// 던전 퇴장 커밋 직전에 호출. 동행 상태를 비워 퇴장 커밋에 함께 저장되게 한다.
        /// </summary>
        /// <returns>동행 중이던 NPC EntityId. 없으면 -1 (커밋 실패 시 RevertCompanionDungeonExit에 그대로 넘긴다)</returns>
        public int StageCompanionDungeonExit()
        {
            NpcCompanionState state = AR.s.Data.NpcCompanion;
            if (state.IsInDungeon == false || state.ReservedNpcId < 0)
                return -1;

            int npcId = state.ReservedNpcId;
            ClearCompanionState();
            return npcId;
        }

        public void RevertCompanionDungeonExit(int npcId)
        {
            if (npcId < 0)
                return;

            AR.s.Data.MarkWorldSaveDirty(WorldSaveGroup.Companion);
            AR.s.Data.NpcCompanion.ReservedNpcId = npcId;
            AR.s.Data.NpcCompanion.IsInDungeon = true;
        }

        /// <summary>
        /// 퇴장 커밋 성공 후, 던전 맵이 내려가고 월드 재개(ResumeWorld) 직전에 호출한다.
        /// 진영 Neutral·태그 제거 후 던전 쪽 엔티티를 비실체화하고, 소속 마을의 NPC 배치 위치로 되돌린 뒤 활동을 Idle로 돌린다.
        /// 청크가 활성이면 이어지는 ResumeWorld의 청크 활성화가 그 자리에 실체화한다 [D18].
        /// </summary>
        /// <param name="fallbackPosition">소속 마을이 없는 NPC의 귀환 위치 (던전 입구 복귀 지점)</param>
        public void FinishCompanionDungeonExit(int npcId, Vector2 fallbackPosition)
        {
            ReturnCompanionHome(npcId, fallbackPosition, worldMapActive: true);
        }

        /// <summary>
        /// 던전 안에서 동행 중인 NPC와 결투가 끝나면(승패·무승부 무관) 동행을 끝내고 NPC를 소속 마을로 귀환시킨다 [D26].
        /// 처리는 던전 퇴장 시 귀환(FinishCompanionDungeonExit)과 같다. 동행 상태가 비므로 이후 퇴장 커밋은 귀환을 다시 하지 않는다.
        /// 동행 예약만 한 NPC(던전 밖)는 대상이 아니다 — 예약은 유지한다.
        /// </summary>
        public void EndCompanionAfterDuel(int npcId)
        {
            if (IsCompanionInDungeon(npcId) == false)
                return;

            ClearCompanionState();
            // 소속 마을이 없는 NPC는 던전 퇴장 때와 같이 플레이어의 던전 입구 복귀 지점으로 돌아간다
            Vector2 fallbackPosition = new Vector2(AR.s.Data.Player.WorldReturnX, AR.s.Data.Player.WorldReturnY);
            ReturnCompanionHome(npcId, fallbackPosition, worldMapActive: AR.s.Dungeon.IsInside == false);
        }

        /// <summary>
        /// 동행 종료 귀환 본체. 진영 Neutral·태그 제거 후 던전 쪽 엔티티를 비실체화하고, 소속 마을의 NPC 배치 위치로 되돌린 뒤 활동을 Idle로 돌린다.
        /// </summary>
        /// <param name="worldMapActive">false면 던전 맵이 활성이라 월드 충돌 검사를 할 수 없다 — 위치만 정하고 보정은 청크 활성화(SpawnNpc)에 맡긴다</param>
        private void ReturnCompanionHome(int npcId, Vector2 fallbackPosition, bool worldMapActive)
        {
            if (npcId < 0)
                return;
            MarkSaveDirty(npcId, NpcSaveGroup.Location | NpcSaveGroup.Activity | NpcSaveGroup.Condition);

            NpcSaveData? saveData = GetSaveData(npcId);
            if (saveData == null)
                return;

            ComponentManager cm = AR.s.Component;
            float now = AR.s.Time.CurrentGameTime;
            // 던전 쪽 엔티티가 아직 살아 있을 때만 정리한다 (사망 파괴가 먼저 끝났으면 ECS 컴포넌트가 없다)
            if (cm.HasComponent<NpcTag>(npcId) == true && AR.s.Message.TryGetEntity(npcId, out var entity) == true)
            {
                // 사망 처리가 아직 파괴 파이프라인을 타지 않았다면 여기서 영구 사망으로 기록한다 (§8).
                if (cm.TryGetComponent<StateComponent>(npcId, out var npcState) == true &&
                    npcState.Condition == CharacterConditions.Dead)
                    MarkNpcDead(npcId, now);

                cm.SetComponent(npcId, new FactionComponent { FactionId = Faction.Neutral });
                cm.RemoveComponent<NpcCombatEnabledTag>(npcId);
                cm.RemoveComponent<NpcCompanionComponent>(npcId);
                AR.s.Entity.DestroyEntity(npcId, allowRecycle: false, keepRegistered: true);
                Destroy(entity.gameObject);
            }

            saveData.IsActive = false;
            // 던전 안에서 저장·길찾기 보정이 던전 좌표를 청크 인덱스에 남겼을 수 있으므로 전체에서 제거한다.
            RemoveNpcFromAllChunks(npcId);

            if (saveData.Condition == CharacterConditions.Dead || saveData.Activity.Phase == NpcActivityPhase.Dead)
            {
                Debug.Log($"[NpcCompanion] 동행 종료 (사망) - EntityId: {npcId}");
                return;
            }

            Vector2 homePosition = ResolveCompanionReturnPosition(saveData, fallbackPosition, worldMapActive);
            saveData.Position = homePosition;
            AddNpcToChunk(PositionToChunk(homePosition), npcId);

            // 다음 활동 선택은 기존 시뮬레이션에 맡긴다
            NpcActivityState activity = saveData.Activity;
            activity.Phase = NpcActivityPhase.Idle;
            activity.Kind = NpcActivityKind.None;
            activity.TargetKey = string.Empty;
            activity.HasMovementPath = false;
            activity.PhaseStartedGameTime = now;
            activity.PhaseEndGameTime = 0f;
            saveData.LastSimGameTime = now;

            NotifyCompanion($"{GetDisplayName(npcId)}이(가) 마을로 돌아갔습니다.");
            Debug.Log($"[NpcCompanion] 동행 종료 - EntityId: {npcId}, 귀환 위치: {homePosition}, VillageId: {saveData.VillageId}");
        }

        /// <summary>
        /// 동행 NPC가 전투로 죽으면 System_EntityDestroy가 MarkNpcDead·Unregister 뒤에 호출한다.
        /// 동행을 즉시 끝내 이후 체크포인트·재접속이 죽은 NPC를 다시 소환하지 않게 한다 (NPC_COMBAT_DESIGN §4.5·§8).
        /// </summary>
        public void EndCompanionOnDeath(int npcId)
        {
            NpcCompanionState state = AR.s.Data.NpcCompanion;
            if (state.ReservedNpcId != npcId || npcId < 0)
                return;

            bool wasInDungeon = state.IsInDungeon;
            ClearCompanionState();
            // 죽은 NPC는 청크 활성화·로드에서 제외되지만, 던전 좌표 항목이 남지 않게 인덱스에서 뺀다
            RemoveNpcFromAllChunks(npcId);
            NotifyCompanion($"{GetDisplayName(npcId)}이(가) 쓰러져 동행이 끝났습니다.");
            Debug.Log($"[NpcCompanion] 동행 종료 (전투 사망) - EntityId: {npcId}, InDungeon: {wasInDungeon}");
        }

        #endregion

        /// <summary>
        /// 로드 직후 동행 상태 정합성 보정. 예약 NPC가 없거나 죽었으면 예약을 지우고,
        /// 플레이어가 던전 밖인데 IsInDungeon이 남아 있으면 해제한다.
        /// 던전 안 재접속이면 상태를 유지하고, DungeonManager.RestoreSessionAsync가 SummonCompanionAsync로 다시 소환한다.
        /// </summary>
        private void ValidateCompanionState()
        {
            NpcCompanionState state = AR.s.Data.NpcCompanion;
            if (state.ReservedNpcId < 0)
            {
                AR.s.Data.MarkWorldSaveDirty(WorldSaveGroup.Companion);
                state.IsInDungeon = false;
                return;
            }

            NpcSaveData? saveData = GetSaveData(state.ReservedNpcId);
            if (saveData == null || saveData.Condition == CharacterConditions.Dead ||
                saveData.Activity.Phase == NpcActivityPhase.Dead)
            {
                ClearCompanionState();
                return;
            }

            if (state.IsInDungeon == true && AR.s.Data.Player != null &&
                AR.s.Data.Player.CurrentMapKind != MapKind.Dungeon)
            {
                AR.s.Data.MarkWorldSaveDirty(WorldSaveGroup.Companion);
                state.IsInDungeon = false;
            }
        }

        /// <summary>죽었거나 부상·원정 중이면 예약을 취소하고 알린다. 취소했으면 true.</summary>
        private bool TryCancelUnavailableCompanion(int npcId)
        {
            NpcSaveData? saveData = GetSaveData(npcId);
            string reason = string.Empty;
            if (saveData == null || saveData.Condition == CharacterConditions.Dead ||
                saveData.Activity.Phase == NpcActivityPhase.Dead)
                reason = "세상을 떠나";
            else if (saveData.Activity.Phase == NpcActivityPhase.Wounded)
                reason = "부상 중이라";
            else if (saveData.Activity.Phase == NpcActivityPhase.Venturing || saveData.Activity.PartyId >= 0)
                reason = "원정 중이라";

            if (string.IsNullOrEmpty(reason) == true)
                return false;

            ClearCompanionState();
            NotifyCompanion($"{GetDisplayName(npcId)}이(가) {reason} 동행이 취소되었습니다.");
            return true;
        }

        private Vector2 ResolveCompanionReturnPosition(NpcSaveData saveData, Vector2 fallbackPosition, bool worldMapActive)
        {
            VillageData? village = AR.s.Village.GetVillage(saveData.VillageId);
            if (village == null)
                return fallbackPosition;

            // 기존 NPC 스폰·배치 규칙 재사용 [D18]
            VillageTable? table = AR.s.Data.GetVillageTable(village.TableId);
            float radius = table != null ? table.SpawnRadius : 0f;
            if (worldMapActive == true)
                return GetRandomSpawnPositionInVillage(village, radius, saveData.NpcTableId);

            // 던전 안(결투 후 귀환 D26): AR.s.Map이 던전 맵이라 월드 타일 충돌을 볼 수 없다.
            // 같은 반경 규칙으로 위치만 정하고, 막힌 자리는 청크 활성화 때 SpawnNpc(TryResolveNpcSpawnPosition)가 보정한다.
            Vector2 candidate = village.Position;
            if (radius > 0f)
            {
                float angle = UnityEngine.Random.Range(0f, Mathf.PI * 2f);
                float r = UnityEngine.Random.Range(0f, radius);
                candidate += new Vector2(Mathf.Cos(angle) * r, Mathf.Sin(angle) * r);
            }
            return candidate;
        }

        private void RemoveNpcFromAllChunks(int entityId)
        {
            foreach (KeyValuePair<Vector2Int, List<int>> pair in _chunkNpcs)
                pair.Value.Remove(entityId);
        }

        private static void ClearCompanionState()
        {
            AR.s.Data.MarkWorldSaveDirty(WorldSaveGroup.Companion);
            AR.s.Data.NpcCompanion.ReservedNpcId = -1;
            AR.s.Data.NpcCompanion.IsInDungeon = false;
        }

        private static void NotifyCompanion(string message)
        {
            Debug.Log("[NpcCompanion] " + message);
            AR.s.UI.SetNotify(message);
        }
    }
}
