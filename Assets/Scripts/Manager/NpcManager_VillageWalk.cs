#nullable enable
using System.Collections.Generic;
using ARPG.AI;
using ARPG.Component;
using ARPG.Creature;
using ARPG.Map;
using ARPG.Scene;
using ARPG.Utility;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace ARPG.Npc
{
    /// <summary>
    /// 마을 안 NPC 표현 — 원정·이주 출발과 귀환을 걸어서 보여 주고, 부상자는 여관으로 걸어 들어간다 (NPC_SIMULATION_DESIGN §3.1 D13·D14).
    /// 결과 판정·난수·저장 형식은 바꾸지 않는다. 실체화된 동안의 모습만 정하고, 걷기 진행은 NpcVillageWalkComponent가 갖는다(저장 안 함).
    /// - 출발: 화면 안 NPC가 이동 경로가 있는 활동을 시작하면 목표 방향의 화면 밖 지점으로 걷고, 화면을 벗어나는 순간 강등한다.
    ///   강등은 지금처럼 SaveAndDeactivateNpc가 현재 위치에서 경로·도착 시각을 다시 계산한다(파티원은 파티 경로가 위치를 정한다).
    /// - 귀환: 화면 밖 NPC의 원정·이주가 끝났을 때 귀환점이 활성 청크면 화면 바로 밖에서 실체화해 귀환점까지 걷는다.
    /// - 이동 중 실체화·결투 종료: 마을 영역 안이면 가는 구간은 출발, 오는 구간은 귀환 걷기. 마을 밖이면 그 자리에서 조우를 기다린다(D11).
    /// - 부상: 여관 마을이면 여관 문으로 걸어가 닿거나 화면을 벗어나면 강등한다. 아니면 멈추지 않고 기본 AI로 움직인다.
    /// 화면 처리는 System_NpcSim·실체화·결투 경로에서만 부른다. 진단이 복제 데이터로 부르는 판정 메서드(ApplyExpeditionWound·
    /// ResolveSoloDelve·CompleteRelocation 등)에는 넣지 않는다 — 복제본에도 IsActive가 저장값으로 들어와 실제 화면 NPC를 움직일 수 있다.
    /// </summary>
    public partial class NpcManager
    {
        /// <summary>목표 지점과 이 거리 안이면 도착으로 본다.</summary>
        public const float VillageWalkReachDistance = 0.75f;
        /// <summary>한 구간(지점 하나)을 이 시간 안에 가지 못하면 실패로 본다.</summary>
        public const float VillageWalkSegmentSeconds = 40f;
        /// <summary>길찾기가 이 시간 동안 연속 실패하면 실패로 본다 (문이 닫힘 등).</summary>
        public const float VillageWalkPathFailSeconds = 3f;
        // 화면 판정 여유: 카메라 영역에서 이만큼 더 나가야 화면 밖이다
        private const float ViewMarginTiles = 1.5f;
        // 화면 밖 지점 = 화면을 벗어나는 점에서 이만큼 더 간 칸. 그 전에 화면 밖 판정으로 강등된다
        private const float OffscreenPointExtraTiles = 3f;
        private const int OffscreenPointSearchSteps = 6;
        // 곧장 막혀 있으면 방향을 이만큼(도) 돌려 다시 찾는다
        private static readonly float[] OffscreenPointAngles = { 0f, 25f, -25f, 50f, -50f };

        /// <summary>귀환 실체화 요청: 화면 바로 밖에서 엔티티를 만들고 귀환점까지 걷는다. 저장 위치는 귀환점 그대로 둔다.</summary>
        private readonly struct NpcArrivalSpawn
        {
            public readonly Vector2 SpawnPosition;
            public readonly Vector2 WalkTarget;

            public NpcArrivalSpawn(Vector2 spawnPosition, Vector2 walkTarget)
            {
                SpawnPosition = spawnPosition;
                WalkTarget = walkTarget;
            }
        }

        /// <summary>
        /// 플레이어 화면 영역 = 메인 카메라 직교 영역 + 여유. 월드 맵 플레이 중이 아니거나 카메라가 없으면 false.
        /// </summary>
        public bool TryGetPlayerViewRect(out Rect view)
        {
            view = default;
            if (CanPresentInWorld() == false)
                return false;
            Camera? camera = Camera.main;
            if (camera == null || camera.orthographic == false)
                return false;

            float halfHeight = camera.orthographicSize + ViewMarginTiles;
            float halfWidth = camera.orthographicSize * camera.aspect + ViewMarginTiles;
            Vector3 center = camera.transform.position;
            view = new Rect(center.x - halfWidth, center.y - halfHeight, halfWidth * 2f, halfHeight * 2f);
            return true;
        }

        /// <summary>
        /// 원정·이주를 시작한 직후 (System_NpcSim). 실체화된 NPC는 멈추지 않고 목표 쪽으로 걸어서 떠난다 (D14).
        /// 이미 화면 밖이면 다음 틱에 System_NpcVillageWalk가 강등한다.
        /// </summary>
        public void ApplyActivityStartPresentation(int entityId)
        {
            if (_npcSaveDict.TryGetValue(entityId, out NpcSaveData? saveData) == false || saveData.IsActive == false)
                return;
            if (IsCompanionInDungeon(entityId) == true)
                return;
            if (TryGetTravelPath(entityId, saveData, AR.s.Time.CurrentGameTime, out NpcActivityState path, out _) == false)
                return;

            StartDepartureWalk(entityId, saveData, path.TargetPosition);
        }

        /// <summary>
        /// 원정·이주가 끝난 직후 실체화된 NPC (System_NpcSim). 조우 대기를 풀고, 부상이면 여관으로 걸어가고(D13),
        /// 아니면 출발 걷기를 끝내 기본 AI로 돌린다. 귀환 걷기는 그대로 이어 간다. 실체화되지 않은 NPC는 TryMaterializeArrivingNpc가 맡는다.
        /// </summary>
        public void ApplyExpeditionEndPresentation(int entityId)
        {
            if (_npcSaveDict.TryGetValue(entityId, out NpcSaveData? saveData) == false || saveData.IsActive == false)
                return;

            ComponentManager cm = AR.s.Component;
            cm.RemoveComponent<NpcActivityPausedTag>(entityId);
            if (saveData.Activity.Phase == NpcActivityPhase.Wounded)
            {
                ApplyWoundPresentation(entityId, saveData);
                return;
            }
            if (cm.TryGetComponent<NpcVillageWalkComponent>(entityId, out NpcVillageWalkComponent walk) == true &&
                walk.Kind != NpcVillageWalkKind.Return)
                FinishVillageWalk(entityId, false);
        }

        /// <summary>
        /// 화면 밖 NPC가 원정·이주에서 돌아온 직후 (System_NpcSim — 그 NPC의 판정이 다 끝난 뒤). 귀환점이 활성 청크면 실체화한다 (D14).
        /// 귀환점이 화면 안이면 귀환 경로 쪽 화면 바로 밖 빈 칸에서 나타나 걸어 들어오고, 이미 화면 밖이면 귀환점에 바로 나타난다.
        /// 그런 칸을 못 찾거나 같은 틱에 새 원정을 골랐거나 부상이면 실체화하지 않는다(다음 청크 활성화 때 평소처럼).
        /// </summary>
        /// <param name="expeditionFrom">귀환 경로가 오는 쪽 (원정 목표 지점, 이주는 출발지)</param>
        public void TryMaterializeArrivingNpc(int entityId, Vector2 expeditionFrom)
        {
            if (_npcSaveDict.TryGetValue(entityId, out NpcSaveData? saveData) == false)
                return;
            if (saveData.IsActive == true || saveData.IsSpawning == true)
                return;
            if (saveData.Condition == CharacterConditions.Dead ||
                saveData.Activity.Phase == NpcActivityPhase.Dead ||
                saveData.Activity.Phase == NpcActivityPhase.Wounded)
                return;
            if (IsCompanionInDungeon(entityId) == true)
                return;
            float now = AR.s.Time.CurrentGameTime;
            if (TryGetTravelPath(entityId, saveData, now, out _, out _) == true)
                return;
            if (TryGetPlayerViewRect(out Rect view) == false)
                return;

            Vector2 arrival = saveData.Position;
            if (AR.s.WorldMap.IsChunkActive(PositionToChunk(arrival)) == false)
                return;
            if (view.Contains(arrival) == false)
            {
                SpawnNpc(entityId, saveData).Forget();
                return;
            }

            Vector2 direction = expeditionFrom - arrival;
            if (direction.sqrMagnitude < 0.01f)
                return;
            float moveRadius = GetNpcMoveRadius(entityId, saveData);
            if (TryFindOffscreenPoint(view, arrival, direction, moveRadius, out Vector2 spawnPosition) == false)
                return;

            SpawnNpc(entityId, saveData, new NpcArrivalSpawn(spawnPosition, arrival)).Forget();
        }

        /// <summary>귀환 실체화의 엔티티 생성이 끝난 시점에도 보여 줄 귀환인지 (SpawnNpc가 await 뒤에 다시 확인한다).</summary>
        private bool IsArrivalSpawnStillValid(int entityId, NpcSaveData saveData, Vector2 spawnPosition)
        {
            if (CanPresentInWorld() == false || AR.s.WorldMap.IsChunkActiveAtWorld(spawnPosition) == false)
                return false;
            if (saveData.Condition == CharacterConditions.Dead ||
                saveData.Activity.Phase == NpcActivityPhase.Dead ||
                saveData.Activity.Phase == NpcActivityPhase.Wounded)
                return false;
            if (IsCompanionInDungeon(entityId) == true)
                return false;
            return TryGetTravelPath(entityId, saveData, AR.s.Time.CurrentGameTime, out _, out _) == false;
        }

        /// <summary>
        /// 결투가 끝난 뒤 (NpcManager_Duel). 활동이 남아 있으면 마을 안은 걷기, 마을 밖은 조우 대기, 부상이면 여관 가기로 다시 정한다.
        /// </summary>
        public void RefreshMaterializedActivityPresentation(int entityId)
        {
            if (_npcSaveDict.TryGetValue(entityId, out NpcSaveData? saveData) == false || saveData.IsActive == false)
                return;
            ApplyMaterializedPresentation(entityId, saveData);
        }

        /// <summary>
        /// 결투가 시작되면 걷기를 멈춘다 — System_AI_Behavior가 결투 AI를 돌려야 한다. 귀환 걷기였으면 순찰 기준점을 귀환점에 둔다.
        /// 결투 뒤 모습은 RefreshMaterializedActivityPresentation이 다시 정한다.
        /// </summary>
        public void CancelVillageWalk(int entityId)
        {
            if (AR.s.Component.TryGetComponent<NpcVillageWalkComponent>(entityId, out NpcVillageWalkComponent walk) == false)
                return;
            FinishVillageWalk(entityId, walk.Kind == NpcVillageWalkKind.Return);
        }

        /// <summary>
        /// 걷기를 끝내고 기본 AI로 돌린다. 기본 AI 순찰은 실체화 자리(SpawnPosition) 주변을 돌므로, 화면 가장자리에서 실체화한 NPC가
        /// 그리로 되돌아가지 않게 순찰 기준점을 옮긴다 — anchorAtTarget이면 걷던 목표, 아니면 지금 자리.
        /// </summary>
        public void FinishVillageWalk(int entityId, bool anchorAtTarget)
        {
            ComponentManager cm = AR.s.Component;
            if (cm.TryGetComponent<NpcVillageWalkComponent>(entityId, out NpcVillageWalkComponent walk) == false)
                return;

            cm.RemoveComponent<NpcVillageWalkComponent>(entityId);
            AIStateHelper.StopMovement(entityId);

            Vector2 anchor = walk.Target;
            if (anchorAtTarget == false && cm.TryGetComponent<TransformComponent>(entityId, out TransformComponent transform) == true)
                anchor = transform.Position;
            if (cm.TryGetComponent<AIStateComponent>(entityId, out AIStateComponent aiState) == true)
            {
                aiState.SpawnPosition = anchor;
                aiState.PatrolTarget = anchor;
                aiState.PatrolArrivalTime = 0f;
                cm.SetComponent(entityId, aiState);
            }
        }

        /// <summary>
        /// System_NpcVillageWalk가 걷던 지점에 닿은 NPC에 대해 부른다. 귀환 = 걷기를 끝내 기본 AI, 여관 문 = 강등(여관 안),
        /// 화면 밖 지점인데 아직 화면 안(플레이어가 따라옴) = 같은 방향으로 다음 지점을 잡는다. 못 잡으면 그 자리에서 강등한다.
        /// </summary>
        public void OnVillageWalkReached(int entityId, NpcVillageWalkComponent walk)
        {
            if (walk.Kind == NpcVillageWalkKind.Return)
            {
                FinishVillageWalk(entityId, false);
                return;
            }
            if (walk.Kind == NpcVillageWalkKind.ToInn &&
                (walk.Target - walk.Heading).sqrMagnitude <= VillageWalkReachDistance * VillageWalkReachDistance)
            {
                DemoteMaterializedNpc(entityId);
                return;
            }

            if (TryRetargetVillageWalk(entityId, ref walk) == false)
            {
                DemoteMaterializedNpc(entityId);
                return;
            }
            AR.s.Component.SetComponent(entityId, walk);
        }

        /// <summary>
        /// 걷기 실패 (제한 시간 초과·길찾기 연속 실패·활성 청크 밖으로 나감). 출발·여관 가기는 그 자리에서 강등하고(문서 D13·D14),
        /// 귀환은 화면 안에서 갑자기 사라지지 않게 걷기만 끝내 기본 AI로 둔다. 활성 청크 밖으로 나간 귀환 NPC는 귀환점에 두고 강등한다.
        /// </summary>
        public void OnVillageWalkFailed(int entityId, NpcVillageWalkComponent walk, bool leftActiveArea)
        {
            if (walk.Kind != NpcVillageWalkKind.Return)
            {
                DemoteMaterializedNpc(entityId);
                return;
            }
            if (leftActiveArea == true)
            {
                DemoteMaterializedNpc(entityId, walk.Target);
                return;
            }
            FinishVillageWalk(entityId, false);
        }

        /// <summary>
        /// 실체화된 NPC 하나를 강등한다 (엔티티 제거 → 오프스크린 레코드). 청크 단위 강등(OnChunkDeactivated)과 같은 SaveAndDeactivateNpc를 쓴다.
        /// 결투 중·던전 동행 중이면 하지 않는다. placeAt이 있으면 그 자리에 둔 뒤 강등한다(활성 영역 밖으로 나간 귀환 NPC).
        /// </summary>
        public void DemoteMaterializedNpc(int entityId, Vector2? placeAt = null)
        {
            if (_npcSaveDict.TryGetValue(entityId, out NpcSaveData? saveData) == false || saveData.IsActive == false)
                return;
            ComponentManager cm = AR.s.Component;
            if (IsCompanionInDungeon(entityId) == true || cm.HasComponent<DuelComponent>(entityId) == true ||
                cm.HasComponent<DestroyTag>(entityId) == true)
                return;

            // 청크 색인은 saveData.Position 기준이다. 실체화 중 전체 저장(SaveAllActiveNpcs)이 위치만 바꿔 두었으면 색인을 먼저 맞춘다.
            Vector2Int indexedChunk = PositionToChunk(saveData.Position);
            if (_chunkNpcs.TryGetValue(indexedChunk, out List<int>? indexed) == false || indexed.Contains(entityId) == false)
            {
                RemoveNpcFromAllChunks(entityId);
                AddNpcToChunk(indexedChunk, entityId);
            }
            if (placeAt.HasValue == true)
            {
                SetNpcPosition(entityId, saveData, placeAt.Value);
                indexedChunk = PositionToChunk(saveData.Position);
            }

            SaveAndDeactivateNpc(entityId, saveData, indexedChunk);
        }

        /// <summary>
        /// 실체화(SpawnNpc) 직후와 결투가 끝난 뒤의 모습. 부상 → 여관 가기(D13). 이동 중이면 마을 영역 안은 가는 구간이면 출발·오는 구간이면 귀환 걷기(D14),
        /// 마을 밖은 그 자리에서 조우를 기다린다(D11, NPC_COMBAT_DESIGN §3.1 예외 포함). 그 밖에는 기본 AI다.
        /// </summary>
        private void ApplyMaterializedPresentation(int entityId, NpcSaveData saveData)
        {
            if (IsCompanionInDungeon(entityId) == true)
                return;
            if (saveData.Activity.Phase == NpcActivityPhase.Wounded)
            {
                ApplyWoundPresentation(entityId, saveData);
                return;
            }

            float now = AR.s.Time.CurrentGameTime;
            if (TryGetTravelPath(entityId, saveData, now, out NpcActivityState path, out Vector2 formationOffset) == false)
                return;
            if (AR.s.Component.TryGetComponent<TransformComponent>(entityId, out TransformComponent transform) == false)
                return;

            Vector2 position = transform.Position;
            if (AR.s.Village.IsInsideVillageArea(position) == false)
            {
                PauseForEncounter(entityId);
                return;
            }
            if (IsReturnLeg(path, position, now) == true)
                BeginVillageWalk(entityId, NpcVillageWalkKind.Return, path.ReturnPosition + formationOffset,
                    path.ReturnPosition + formationOffset);
            else
                StartDepartureWalk(entityId, saveData, path.TargetPosition);
        }

        /// <summary>
        /// 실체화된 부상자 (D13). 여관 마을이면 여관 문으로 걷는다 — 여관이 배치돼 있고 문이 활성 청크면 문까지, 아니면 여관(배치 전이면 마을 중심) 쪽
        /// 화면 밖 지점까지 걷다가 화면을 벗어나면 강등한다. 소속 마을이 없거나 사라진 터면 멈춤만 풀어 기본 AI로 움직인다.
        /// </summary>
        private void ApplyWoundPresentation(int entityId, NpcSaveData saveData)
        {
            ComponentManager cm = AR.s.Component;
            cm.RemoveComponent<NpcActivityPausedTag>(entityId);
            if (AR.s.Village.IsInnHome(saveData.VillageId) == false)
            {
                FinishVillageWalk(entityId, false);
                return;
            }
            if (cm.TryGetComponent<TransformComponent>(entityId, out TransformComponent transform) == false)
                return;

            Vector2 position = transform.Position;
            if (TryChooseInnWalkTarget(entityId, saveData, position, out Vector2 target, out Vector2 heading) == false)
            {
                DemoteMaterializedNpc(entityId);
                return;
            }
            BeginVillageWalk(entityId, NpcVillageWalkKind.ToInn, target, heading);
        }

        private void StartDepartureWalk(int entityId, NpcSaveData saveData, Vector2 heading)
        {
            if (AR.s.Component.TryGetComponent<TransformComponent>(entityId, out TransformComponent transform) == false)
                return;
            Vector2 position = transform.Position;
            float moveRadius = GetNpcMoveRadius(entityId, saveData);
            if (TryGetPlayerViewRect(out Rect view) == false ||
                TryFindOffscreenPoint(view, position, heading - position, moveRadius, out Vector2 target) == false)
            {
                // 화면 밖으로 나갈 길을 못 찾으면 그 자리에서 강등한다 (D14)
                DemoteMaterializedNpc(entityId);
                return;
            }
            BeginVillageWalk(entityId, NpcVillageWalkKind.Departure, target, heading);
        }

        private void BeginVillageWalk(int entityId, NpcVillageWalkKind kind, Vector2 target, Vector2 heading)
        {
            ComponentManager cm = AR.s.Component;
            // 걷기가 이동을 소유한다 — 진행 중이던 줍기·조우 대기·AI 이동을 멈춘다
            CancelNpcPickup(entityId, "마을 걷기 시작");
            cm.RemoveComponent<NpcActivityPausedTag>(entityId);
            AIStateHelper.StopMovement(entityId);

            NpcVillageWalkComponent walk = new NpcVillageWalkComponent
            {
                Kind = kind,
                Target = target,
                Heading = heading,
                ElapsedSeconds = 0f,
                PathFailedSeconds = 0f,
            };
            if (cm.HasComponent<NpcVillageWalkComponent>(entityId) == true)
                cm.SetComponent(entityId, walk);
            else
                cm.AddComponent(entityId, walk);
        }

        /// <summary>마을 밖에서 실체화된 이동 중 NPC는 그 자리에 멈춰 조우를 기다린다 (D11). 청크가 비활성화되면 경로를 이어 간다.</summary>
        private static void PauseForEncounter(int entityId)
        {
            ComponentManager cm = AR.s.Component;
            cm.RemoveComponent<NpcVillageWalkComponent>(entityId);
            if (cm.HasComponent<NpcActivityPausedTag>(entityId) == false)
                cm.AddComponent(entityId, new NpcActivityPausedTag());
            AIStateHelper.StopMovement(entityId);
        }

        private bool TryRetargetVillageWalk(int entityId, ref NpcVillageWalkComponent walk)
        {
            if (_npcSaveDict.TryGetValue(entityId, out NpcSaveData? saveData) == false ||
                AR.s.Component.TryGetComponent<TransformComponent>(entityId, out TransformComponent transform) == false)
                return false;

            Vector2 position = transform.Position;
            Vector2 target;
            if (walk.Kind == NpcVillageWalkKind.ToInn)
            {
                if (TryChooseInnWalkTarget(entityId, saveData, position, out target, out Vector2 heading) == false)
                    return false;
                walk.Heading = heading;
            }
            else if (TryGetPlayerViewRect(out Rect view) == false ||
                     TryFindOffscreenPoint(view, position, walk.Heading - position, GetNpcMoveRadius(entityId, saveData),
                         out target) == false)
            {
                return false;
            }

            walk.Target = target;
            walk.ElapsedSeconds = 0f;
            walk.PathFailedSeconds = 0f;
            return true;
        }

        /// <summary>
        /// 여관 가기 목표. 여관이 배치돼 있고 문이 활성 청크면 문 앞 빈 칸, 아니면 여관(배치 전이면 마을 중심) 쪽 화면 밖 지점.
        /// heading은 문 앞 빈 칸(또는 마을 중심)이라 목표와 같으면 "문에 닿음"이다.
        /// </summary>
        private bool TryChooseInnWalkTarget(int entityId, NpcSaveData saveData, Vector2 position,
            out Vector2 target, out Vector2 heading)
        {
            target = position;
            float moveRadius = GetNpcMoveRadius(entityId, saveData);
            if (AR.s.Village.TryGetInnDoorPosition(saveData.VillageId, out Vector2 door) == true)
            {
                heading = door;
                if (AR.s.Map.IsChunkActiveAtWorld(door) == true && TryResolveFreeCell(door, moveRadius, out Vector2 freeDoor) == true)
                {
                    heading = freeDoor;
                    target = freeDoor;
                    return true;
                }
            }
            else
            {
                heading = AR.s.Village.GetVillage(saveData.VillageId)?.Position ?? position;
            }

            return TryGetPlayerViewRect(out Rect view) == true &&
                   TryFindOffscreenPoint(view, position, heading - position, moveRadius, out target) == true;
        }

        /// <summary>칸이 비어 있으면 그대로, 막혀 있으면 활성 청크 안의 가장 가까운 빈 칸.</summary>
        private static bool TryResolveFreeCell(Vector2 cell, float moveRadius, out Vector2 free)
        {
            if (CollisionUtil.IsBlockedAt(cell, moveRadius) == false)
            {
                free = cell;
                return true;
            }
            return CollisionUtil.TryFindNearestFree(cell, moveRadius, out free, requireActiveChunk: true);
        }

        /// <summary>
        /// 화면 밖 지점 (D14): from에서 direction으로 화면 영역을 벗어나는 점보다 몇 칸 더 간 빈 칸. 활성 청크 안이어야 한다.
        /// 곧장 막혀 있으면 한 칸씩 더 나가 보고, 그래도 없으면 방향을 ±25°·±50° 돌려 찾는다. 방향이 없으면 화면 중심 반대쪽으로 간다.
        /// </summary>
        private static bool TryFindOffscreenPoint(Rect view, Vector2 from, Vector2 direction, float moveRadius,
            out Vector2 point)
        {
            point = from;
            if (direction.sqrMagnitude < 0.0001f)
                direction = from - view.center;
            if (direction.sqrMagnitude < 0.0001f)
                direction = Vector2.down;
            Vector2 baseDirection = direction.normalized;
            MapManager map = AR.s.Map;

            for (int a = 0; a < OffscreenPointAngles.Length; a++)
            {
                Vector2 rayDirection = Rotate(baseDirection, OffscreenPointAngles[a]);
                float startDistance = GetViewExitDistance(view, from, rayDirection) + OffscreenPointExtraTiles;
                for (int step = 0; step < OffscreenPointSearchSteps; step++)
                {
                    Vector2 sample = from + rayDirection * (startDistance + step);
                    Vector2 cell = new Vector2(Mathf.Floor(sample.x) + 0.5f, Mathf.Floor(sample.y) + 0.5f);
                    if (map.IsChunkActiveAtWorld(cell) == false)
                        break;
                    if (view.Contains(cell) == true || CollisionUtil.IsBlockedAt(map, cell, moveRadius) == true)
                        continue;
                    point = cell;
                    return true;
                }
            }
            return false;
        }

        /// <summary>from이 화면 안일 때 direction(단위 벡터)으로 화면 경계까지의 거리. 화면 밖이면 0.</summary>
        private static float GetViewExitDistance(Rect view, Vector2 from, Vector2 direction)
        {
            if (view.Contains(from) == false)
                return 0f;
            float toX = direction.x > 0.0001f ? (view.xMax - from.x) / direction.x
                : direction.x < -0.0001f ? (view.xMin - from.x) / direction.x
                : float.MaxValue;
            float toY = direction.y > 0.0001f ? (view.yMax - from.y) / direction.y
                : direction.y < -0.0001f ? (view.yMin - from.y) / direction.y
                : float.MaxValue;
            return Mathf.Min(toX, toY);
        }

        private static Vector2 Rotate(Vector2 direction, float degrees)
        {
            if (degrees == 0f)
                return direction;
            float radians = degrees * Mathf.Deg2Rad;
            float cos = Mathf.Cos(radians);
            float sin = Mathf.Sin(radians);
            return new Vector2(direction.x * cos - direction.y * sin, direction.x * sin + direction.y * cos);
        }

        /// <summary>
        /// 이동 중인 경로 (솔로 = 자기 활동, 파티원 = 파티 활동 + 대열 오프셋). 이동 경로가 있는 활동이 아직 진행 중일 때만 true.
        /// </summary>
        private bool TryGetTravelPath(int entityId, NpcSaveData saveData, float now, out NpcActivityState path,
            out Vector2 formationOffset)
        {
            NpcActivityState activity = saveData.Activity;
            path = activity;
            formationOffset = Vector2.zero;
            if (activity.PartyId >= 0)
            {
                if (_npcPartyDict.TryGetValue(activity.PartyId, out NpcPartyState? party) == false)
                    return false;
                path = party.Activity;
                int memberIndex = party.MemberIds.IndexOf(entityId);
                formationOffset = GetPartyFormationOffset(memberIndex >= 0 ? memberIndex : 0);
                return path.HasMovementPath == true && path.PhaseEndGameTime > now;
            }
            return NpcActivityResolver.HasTravelPath(activity.Kind) == true && activity.HasMovementPath == true &&
                   activity.PhaseEndGameTime > now;
        }

        /// <summary>가는 구간을 지났는지. 이주는 편도라(OutboundFraction 1) 목적지 쪽에 더 가까우면 도착 구간으로 본다.</summary>
        private static bool IsReturnLeg(NpcActivityState path, Vector2 position, float now)
        {
            if (path.Kind == NpcActivityKind.Relocate)
                return (path.TargetPosition - position).sqrMagnitude < (path.OriginPosition - position).sqrMagnitude;

            float duration = path.PhaseEndGameTime - path.PhaseStartedGameTime;
            if (duration <= 0f)
                return true;
            float progress = Mathf.Clamp01((now - path.PhaseStartedGameTime) / duration);
            return progress >= path.OutboundFraction;
        }

        /// <summary>월드 맵을 플레이 중이라 엔티티를 만들고 화면을 판정할 수 있는지 (던전 안 월드 시뮬은 제외).</summary>
        private static bool CanPresentInWorld()
        {
            return AR.s.CurrentScene is GameScene && AR.s.Dungeon.IsInside == false && AR.s.WorldMap != null;
        }
    }
}
