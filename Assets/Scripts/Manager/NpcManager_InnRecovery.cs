#nullable enable
using System.Collections.Generic;
using ARPG.Component;
using ARPG.Creature;
using ARPG.Village;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace ARPG.Npc
{
    /// <summary>
    /// 원정 부상자의 여관 회복 (NPC_SIMULATION_DESIGN §3.1 D13·D16).
    /// "여관 안" = 부상 중(Phase Wounded)이고 소속 마을이 여관 마을이다(VillageManager.IsInnHome — 사라진 터 제외, 배치 대기 여관 포함).
    /// 새 저장 필드 없이 활동 상태와 마을 시설 기록으로만 판정한다. 여관 안 NPC는 실체화하지 않고 여관 창 목록에서 대화한다.
    /// 부상 판정·중상·치료비·회복 시간·알림·동행 예약 취소는 바꾸지 않는다 (AMBITION D36·D91).
    /// </summary>
    public partial class NpcManager
    {
        /// <summary>여관 안에서 회복 중인지 (D13). 실체화돼 여관으로 걸어가는 중인 NPC도 true다.</summary>
        public bool IsRecoveringInInn(NpcSaveData saveData)
        {
            if (saveData.Condition == CharacterConditions.Dead ||
                saveData.Activity.Phase != NpcActivityPhase.Wounded)
                return false;
            return AR.s.Village.IsInnHome(saveData.VillageId);
        }

        /// <summary>
        /// 여관 창 목록 (D16): 그 마을 소속 "여관 안" NPC를 회복이 빨리 끝나는 순(같으면 EntityId 순)으로 result에 채운다.
        /// </summary>
        public void CollectInnGuests(int villageId, List<int> result)
        {
            result.Clear();
            VillageData? village = AR.s.Village.GetVillage(villageId);
            if (village == null || AR.s.Village.IsInnHome(villageId) == false)
                return;

            List<int> members = village.NpcEntityIds;
            for (int i = 0; i < members.Count; i++)
            {
                int entityId = members[i];
                if (_npcSaveDict.TryGetValue(entityId, out NpcSaveData? saveData) == false ||
                    saveData.VillageId != villageId || IsRecoveringInInn(saveData) == false)
                    continue;

                // 손님 수가 적어 삽입 정렬로 충분하다 (비교 대리자를 만들지 않는다)
                int insertAt = result.Count;
                while (insertAt > 0 && IsInnGuestBefore(saveData, entityId, result[insertAt - 1]) == true)
                    insertAt--;
                result.Insert(insertAt, entityId);
            }
        }

        /// <summary>
        /// 회복이 끝난 순간 (System_NpcSim의 Wounded → Idle). 여관이 배치돼 있으면 여관 문 앞 빈 칸으로 옮기고, 그 청크가 활성이면
        /// 바로 실체화해 기본 AI로 움직인다. 여관이 아직 배치 전이면(마을 미실체화) 위치를 그대로 두고 다음 청크 활성화 때 평소처럼 나타난다.
        /// 여관 마을이 아니라 밖에서 회복한 실체화 NPC는 멈춤·걷기만 풀어 준다.
        /// </summary>
        public void ApplyWoundRecoveredPresentation(int entityId)
        {
            if (_npcSaveDict.TryGetValue(entityId, out NpcSaveData? saveData) == false)
                return;
            if (saveData.IsActive == true)
            {
                AR.s.Component.RemoveComponent<NpcActivityPausedTag>(entityId);
                FinishVillageWalk(entityId, false);
                return;
            }
            if (saveData.IsSpawning == true || AR.s.Village.IsInnHome(saveData.VillageId) == false)
                return;
            if (AR.s.Village.TryGetInnDoorPosition(saveData.VillageId, out Vector2 door) == false)
                return;

            bool chunkActive = CanPresentInWorld() == true && AR.s.WorldMap.IsChunkActiveAtWorld(door) == true;
            if (chunkActive == true &&
                TryResolveFreeCell(door, GetNpcMoveRadius(entityId, saveData), out Vector2 freeDoor) == true)
                door = freeDoor;
            SetNpcPosition(entityId, saveData, door);
            if (chunkActive == true)
                SpawnNpc(entityId, saveData).Forget();
        }

        private bool IsInnGuestBefore(NpcSaveData guest, int guestId, int otherId)
        {
            if (_npcSaveDict.TryGetValue(otherId, out NpcSaveData? other) == false)
                return true;
            float guestEnd = guest.Activity.PhaseEndGameTime;
            float otherEnd = other.Activity.PhaseEndGameTime;
            if (guestEnd != otherEnd)
                return guestEnd < otherEnd;
            return guestId < otherId;
        }
    }
}
