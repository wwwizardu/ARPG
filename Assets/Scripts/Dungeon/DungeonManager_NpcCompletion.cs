using System;
using ARPG.Data;
using ARPG.Tables;
using UnityEngine;

namespace ARPG.Dungeon
{
    /// <summary>NPC 던전 완료 시도 결과 (NPC_AMBITION_DESIGN §7.1, D58). Completed 외에는 슬롯을 바꾸지 않는다.</summary>
    public enum NpcDungeonCompletionResult
    {
        Completed = 0,
        NotInitialized,
        NoSlot,
        // 원정 중에 그 슬롯의 세대가 이미 넘어갔다 (플레이어 완료 또는 다른 NPC 완료)
        DungeonChanged,
        // 보호 규칙: 플레이어가 던전 안에 있음 (NPC 시뮬레이션이 멈추므로 보통 도달하지 않는다)
        PlayerInside,
        // 입장·배치 저장 트랜잭션 진행 중 — 슬롯 객체를 붙잡고 있는 비동기 작업과 겹치지 않게 한다
        Busy,
        // 보호 규칙 ①: 플레이어가 입장한 던전 (DungeonRecord 존재)
        PlayerEntered,
        // 보호 규칙 ②: 마을당 NPC 완료 쿨다운
        VillageCooldown,
        // 보호 규칙 ③: 이번 완료 뒤 그 마을 던전 슬롯 중 NPC 교체 세대가 절반을 넘게 됨
        NpcReplacedHalf,
        InvalidGeneration,
    }

    /// <summary>
    /// 플레이어 외 주체(NPC)의 슬롯 단위 던전 완료 진입점 (NPC_AMBITION_DESIGN §7.1, §11 DUNGEON_RUNTIME 추가 계약 ①, D57·D58).
    /// 플레이어 완료 트랜잭션(DungeonCompletionTransaction)과 분리되어 있고 플레이어 Gold·DungeonRecord·CompletedCount를 건드리지 않는다.
    /// 즉시 저장하지 않는다 — 다음 저장이 Dungeons 또는 NpcState를 포함하면 둘을 함께 싣는다 (DataManager, D47·D58).
    /// </summary>
    public partial class DungeonManager
    {
        // 마을당 NPC 완료 쿨다운 (§7.1 보호 규칙, 72 게임시간)
        public const float NpcCompletionVillageCooldownHours = 72f;

        // 슬롯 객체를 교체하지 않고 제자리에서 세대를 넘기므로 입구 역인덱스·표현은 다음 TickPlacement에서 다시 만든다.
        // (완료 호출 시점에 곧바로 입구 오브젝트를 건드리지 않아 진단 복제 실행이 라이브 표현을 바꾸지 않는다.)
        private bool _entranceIndexDirty;
        // NPC 완료가 일어날 때마다 1 증가. 저장이 이 값까지 커밋되면 _savedNpcCompletionRevision을 올린다.
        private long _npcCompletionRevision;
        private long _savedNpcCompletionRevision;

        /// <summary>아직 저장되지 않은 NPC 완료가 있는가. 저장 파이프라인이 Dungeons+NpcState를 함께 싣는 조건.</summary>
        public bool HasUnsavedNpcCompletion => _npcCompletionRevision != _savedNpcCompletionRevision;
        public long NpcCompletionRevision => _npcCompletionRevision;

        /// <summary>Dungeons와 NpcState를 함께 커밋한 저장이 성공하면 호출한다. 캡처 시점의 revision을 넘긴다.</summary>
        public void MarkNpcCompletionSaved(long capturedRevision)
        {
            if (capturedRevision > _savedNpcCompletionRevision)
                _savedNpcCompletionRevision = capturedRevision;
        }

        /// <summary>소속 마을에 NPC가 원정할 던전 슬롯이 있는가 (D56 후보 조건). 입구 배치 여부와 무관하다 (D57).</summary>
        public bool HasNpcDelveTarget(int villageId)
        {
            if (_initialized == false || villageId < 0)
                return false;
            return _villageSlots.TryGetValue(villageId, out VillageDungeonSlots slots) == true &&
                   slots.Slots != null && slots.Slots.Count > 0;
        }

        public int GetNpcDelveSlotCount(int villageId)
        {
            if (_initialized == false || _villageSlots.TryGetValue(villageId, out VillageDungeonSlots slots) == false ||
                slots.Slots == null)
                return 0;
            return slots.Slots.Count;
        }

        /// <summary>
        /// 원정 대상 슬롯 정보. 입구가 있으면 그 좌표를 경로 목표로 쓸 수 있게 돌려준다 (없으면 hasEntrance=false).
        /// </summary>
        public bool TryGetNpcDelveSlot(int villageId, int slotIndex, out string dungeonId, out DungeonTable table,
            out bool hasEntrance, out Vector2 entrancePosition)
        {
            dungeonId = string.Empty;
            table = null;
            hasEntrance = false;
            entrancePosition = Vector2.zero;
            if (_initialized == false || _villageSlots.TryGetValue(villageId, out VillageDungeonSlots slots) == false ||
                slots.Slots == null || slotIndex < 0 || slotIndex >= slots.Slots.Count)
                return false;

            DungeonSlot slot = slots.Slots[slotIndex];
            if (slot == null || string.IsNullOrEmpty(slot.DungeonId) == true)
                return false;
            table = GetSlotTable(slot);
            if (table == null)
                return false;

            dungeonId = slot.DungeonId;
            hasEntrance = slot.HasEntrance;
            if (hasEntrance == true)
                entrancePosition = new Vector2(slot.EntranceX + 0.5f, slot.EntranceY + 0.5f);
            return true;
        }

        /// <summary>DungeonId가 가리키는 현재 활성 슬롯의 테이블. 세대가 넘어가 사라진 던전이면 false.</summary>
        public bool TryGetNpcDelveTable(int villageId, string dungeonId, out DungeonTable table)
        {
            table = null;
            if (TryFindVillageSlot(villageId, dungeonId, out _, out DungeonSlot slot) == false)
                return false;
            table = GetSlotTable(slot);
            return table != null;
        }

        /// <summary>
        /// 소문 대상 검증용 (NPC_AMBITION_DESIGN P2b, D72): 마을 슬롯(SlotIndex)의 현재 세대. 슬롯이 없으면 false.
        /// 문자열을 만들지 않으므로 소문 검증처럼 자주 부르는 곳에 쓴다.
        /// </summary>
        public bool TryGetNpcDungeonSlotGeneration(int villageId, int slotIndex, out int generation)
        {
            generation = -1;
            int listIndex = FindSlotListIndex(villageId, slotIndex, out VillageDungeonSlots village);
            if (listIndex < 0 || village == null)
                return false;
            generation = village.Slots[listIndex].Generation;
            return true;
        }

        /// <summary>마을 슬롯 번호(SlotIndex)의 슬롯 목록 인덱스. TryGetNpcDelveSlot의 slotIndex 인자로 쓴다. 없으면 -1.</summary>
        public int GetNpcDelveSlotListIndex(int villageId, int slotIndex)
        {
            return FindSlotListIndex(villageId, slotIndex, out _);
        }

        /// <summary>DungeonId가 가리키는 현재 활성 슬롯의 번호·세대 (소문 생성용). 세대가 넘어가 사라진 던전이면 false.</summary>
        public bool TryGetNpcDelveSlotRef(int villageId, string dungeonId, out int slotIndex, out int generation)
        {
            slotIndex = -1;
            generation = -1;
            if (_initialized == false || TryFindVillageSlot(villageId, dungeonId, out _, out DungeonSlot slot) == false)
                return false;
            slotIndex = slot.SlotIndex;
            generation = slot.Generation;
            return true;
        }

        /// <summary>
        /// 보호 규칙만 평가한다 (슬롯을 바꾸지 않음). 진단·검증용으로도 쓴다.
        /// ① 플레이어가 입장한 던전(DungeonRecord 존재) ② 마을당 72h 쿨다운 ③ 이번 완료 뒤 NPC 교체 슬롯이 절반 초과
        /// + 플레이어가 던전 안 / 저장 트랜잭션 진행 중이면 거부.
        /// </summary>
        public NpcDungeonCompletionResult EvaluateNpcCompletion(int villageId, string dungeonId, float gameTime)
        {
            if (_initialized == false)
                return NpcDungeonCompletionResult.NotInitialized;
            if (_villageSlots.TryGetValue(villageId, out VillageDungeonSlots village) == false || village.Slots == null)
                return NpcDungeonCompletionResult.NoSlot;
            if (TryFindVillageSlot(villageId, dungeonId, out _, out DungeonSlot slot) == false)
                return NpcDungeonCompletionResult.DungeonChanged;
            if (IsInside == true)
                return NpcDungeonCompletionResult.PlayerInside;
            if (_busy == true || _recoveryRequired == true)
                return NpcDungeonCompletionResult.Busy;
            if (FindRecord(slot.DungeonId) != null)
                return NpcDungeonCompletionResult.PlayerEntered;
            if (village.LastNpcCompletionGameTime >= 0f &&
                gameTime - village.LastNpcCompletionGameTime < NpcCompletionVillageCooldownHours)
                return NpcDungeonCompletionResult.VillageCooldown;

            // 이번 완료 뒤 NPC 교체 슬롯 수가 절반을 넘으면 거부한다 (분모 = 마을 던전 슬롯 수).
            // 대상 슬롯을 뺀 다른 슬롯의 교체 수(others) + 1(이번 완료). 2·3슬롯 마을 모두 교체 세대는 최대 1개이고,
            // 이미 교체 세대인 슬롯을 다시 완료하는 것은 교체 수가 늘지 않으므로 허용된다.
            int others = 0;
            for (int i = 0; i < village.Slots.Count; i++)
            {
                DungeonSlot other = village.Slots[i];
                if (ReferenceEquals(other, slot) == false && DungeonLayout.IsNpcReplaced(other) == true)
                    others++;
            }
            if ((others + 1) * 2 > village.Slots.Count)
                return NpcDungeonCompletionResult.NpcReplacedHalf;
            if (slot.Generation == int.MaxValue)
                return NpcDungeonCompletionResult.InvalidGeneration;
            return NpcDungeonCompletionResult.Completed;
        }

        /// <summary>
        /// NPC가 던전을 완료한다 (D57·D58). 보호 규칙을 모두 통과하면 슬롯 세대를 넘긴다:
        /// Generation+1, 새 DungeonId, 입구가 있었으면 사라지고 그 좌표를 이전 입구로 기록, 교체 쿨다운 뒤 새 입구 배치.
        /// 새 세대는 NPC 교체 세대로 표시하고 마을의 마지막 NPC 완료 시각을 기록한다.
        /// 플레이어 Gold·DungeonRecord·전역 CompletedCount는 바꾸지 않는다. 저장은 호출하지 않는다.
        /// </summary>
        public NpcDungeonCompletionResult TryCompleteByNpc(int villageId, string dungeonId, float gameTime)
        {
            NpcDungeonCompletionResult result = EvaluateNpcCompletion(villageId, dungeonId, gameTime);
            if (result != NpcDungeonCompletionResult.Completed)
                return result;

            TryFindVillageSlot(villageId, dungeonId, out VillageDungeonSlots village, out DungeonSlot slot);
            DungeonTable table = GetSlotTable(slot);
            float cooldownHours = table != null ? Math.Max(0f, table.ReplacementCooldownHours) : 0f;
            int nextGeneration = slot.Generation + 1;
            bool hadEntrance = slot.HasEntrance;
            // 플레이어 결과가 없으므로 소문은 미해결로 끝나고 부탁 칸이 빈다 (NPC_NARRATIVE_CONTENT_DESIGN §3.1·§4.1).
            // 슬롯을 바꾸기 전에 입구 위치를 원장에 남긴다. 저장은 NPC 완료와 같은 다음 저장에 실린다.
            AR.s.Npc.EndDungeonNarrativeUnresolved(slot.DungeonId, hadEntrance, slot.EntranceX, slot.EntranceY, gameTime);

            // 슬롯 객체를 제자리에서 바꾼다. 붙잡고 있던 역인덱스 참조도 HasEntrance=false를 보게 되어
            // 다음 인덱스 재구성 전에 옛 입구로 입장·상호작용할 수 없다.
            AR.s.Data.MarkDungeonSlotsSaveDirty(villageId);
            slot.Generation = nextGeneration;
            slot.DungeonId = DungeonLayout.Id(AR.s.Data.WorldSeed, village.VillageId, slot.SlotIndex, nextGeneration);
            if (hadEntrance == true)
            {
                slot.HasPreviousEntrance = true;
                slot.PreviousX = slot.EntranceX;
                slot.PreviousY = slot.EntranceY;
            }
            slot.HasEntrance = false;
            slot.EntranceX = 0;
            slot.EntranceY = 0;
            slot.PlacementReadyGameTime = Math.Max(0f, gameTime) + cooldownHours;
            slot.NextCandidate = 0;
            slot.NpcReplacedGeneration = nextGeneration;
            village.LastNpcCompletionGameTime = Math.Max(0f, gameTime);

            _npcCompletionRevision++;
            _entranceIndexDirty = true;
            Debug.Log($"[NpcAmbition] DungeonSlotAdvanced village={villageId} slot={slot.SlotIndex} " +
                      $"{dungeonId} -> {slot.DungeonId} hadEntrance={hadEntrance} " +
                      $"placementReady={slot.PlacementReadyGameTime:F1} t={gameTime:F2}");
            return NpcDungeonCompletionResult.Completed;
        }

        private bool TryFindVillageSlot(int villageId, string dungeonId, out VillageDungeonSlots village,
            out DungeonSlot slot)
        {
            slot = null;
            if (_villageSlots.TryGetValue(villageId, out village) == false || village.Slots == null ||
                string.IsNullOrEmpty(dungeonId) == true)
                return false;
            for (int i = 0; i < village.Slots.Count; i++)
            {
                DungeonSlot candidate = village.Slots[i];
                if (candidate != null && candidate.DungeonId == dungeonId)
                {
                    slot = candidate;
                    return true;
                }
            }
            return false;
        }

        private int FindSlotListIndex(int villageId, int slotIndex, out VillageDungeonSlots village)
        {
            village = null;
            if (_initialized == false || _villageSlots.TryGetValue(villageId, out village) == false ||
                village.Slots == null)
                return -1;
            for (int i = 0; i < village.Slots.Count; i++)
            {
                DungeonSlot candidate = village.Slots[i];
                if (candidate != null && candidate.SlotIndex == slotIndex)
                    return i;
            }
            return -1;
        }

        private DungeonTable GetSlotTable(DungeonSlot slot)
        {
            int tableId = slot.DungeonTableId > 0 ? slot.DungeonTableId : Settings.DefaultDungeonTableId;
            return AR.s.Data.GetDungeonTable(tableId);
        }

        /// <summary>NPC 완료로 더러워진 입구 역인덱스를 다시 만든다. TickPlacement 첫머리에서 호출한다.</summary>
        private void RefreshEntranceIndexIfDirty()
        {
            if (_entranceIndexDirty == false || _initialized == false || _busy == true)
                return;
            _entranceIndexDirty = false;
            RefreshNarrativeSelections();
            RebuildEntranceIndex();
        }

        private void ResetNpcCompletion()
        {
            _entranceIndexDirty = false;
            _npcCompletionRevision = 0;
            _savedNpcCompletionRevision = 0;
        }
    }
}
