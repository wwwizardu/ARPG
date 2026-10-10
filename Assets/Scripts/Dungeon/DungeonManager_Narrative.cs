using ARPG.Data;
using ARPG.Npc.Narrative;
using UnityEngine;

namespace ARPG.Dungeon
{
    /// <summary>
    /// 던전 쪽 사실 제공 (NPC_NARRATIVE_CONTENT_DESIGN §5.1·§7.4). DungeonManager는 슬롯·배치·보스 처치를 소유하고
    /// 콘텐츠 원장은 갖지 않는다. 세대가 생기면 NpcManager에 선정을 맡기고, 첫 입장 때 소문의 몬스터를 실제로 배치한다.
    /// </summary>
    public partial class DungeonManager
    {
        private static readonly string[] _bearingNames = { "북쪽", "북동쪽", "동쪽", "남동쪽", "남쪽", "남서쪽", "서쪽", "북서쪽" };

        /// <summary>
        /// 마을 주민 배치가 끝났다 (새 월드·불러오기 공통, MapManager.CreateMap). 이때부터 소문 선정과 소문 주인 선택을 한다.
        /// 새로 붙은 소문의 입구 자막은 다음 입구 색인 갱신 때 붙는다.
        /// </summary>
        public void OnVillagesPopulated()
        {
            AR.s.Npc.SetNarrativeOwnerSelectionReady();
            if (RefreshNarrativeSelections() == true)
                _entranceIndexDirty = true;
        }

        /// <summary>
        /// 모든 슬롯의 현재 세대에 콘텐츠를 정하고(같은 세대는 다시 고르지 않음) 쓰이지 않는 원장 기록을 정리한다.
        /// 세대가 바뀌는 지점(초기화·플레이어 완료·NPC 완료 뒤 색인 갱신)에서 부른다. 새 선정이 있었으면 true.
        /// </summary>
        private bool RefreshNarrativeSelections()
        {
            if (_initialized == false || AR.s.Npc.NarrativeOwnerSelectionReady == false)
                return false;
            bool created = false;
            var villages = AR.s.Data.Dungeons.Villages;
            for (int v = 0; v < villages.Count; v++)
                for (int s = 0; s < villages[v].Slots.Count; s++)
                {
                    DungeonSlot slot = villages[v].Slots[s];
                    if (AR.s.Npc.EnsureDungeonNarrative(villages[v].VillageId, slot, FindRecord(slot.DungeonId) != null, GetSlotTable(slot)) == true)
                        created = true;
                }
            AR.s.Npc.PruneNarrativeLedger(AR.s.Time.CurrentGameTime);
            return created;
        }

        /// <summary>
        /// 첫 입장 때 소문이 사실이도록 대상 몬스터를 실제로 둔다 (§2.3). 보스면 이미 있다. 일반 몬스터면 같은 종이 없을 때
        /// 일반 몬스터 한 자리를 그 종으로 바꾼다 (수·보스·보상은 그대로). 특정 개체를 원인으로 연결하지 않는다.
        /// 둘 곳이 없으면 false — 입장을 커밋 전에 취소한다.
        /// </summary>
        private bool EnsureNarrativeTargetPlacement(DungeonRecord record)
            => PlaceNarrativeTarget(record, AR.s.Npc.GetNarrativeContent(AR.s.Npc.GetDungeonNarrative(record.DungeonId)));

        /// <summary>배치 보장 순수 규칙. 진단이 실제 레이아웃에 같은 규칙을 적용해 검사한다.</summary>
        public static bool PlaceNarrativeTarget(DungeonRecord record, NarrativeContentDefinition content)
        {
            if (content == null || content.TargetType != NarrativeTargetType.Monster)
                return true;
            DungeonObject firstMonster = null;
            for (int i = 0; i < record.Objects.Count; i++)
            {
                DungeonObject obj = record.Objects[i];
                if ((obj.Kind == DungeonObjectKind.Boss || obj.Kind == DungeonObjectKind.Monster) && obj.TableId == content.TargetId)
                    return true;
                if (firstMonster == null && obj.Kind == DungeonObjectKind.Monster && obj.RoomId > 0)
                    firstMonster = obj;
            }
            if (firstMonster == null)
                return false;
            firstMonster.TableId = content.TargetId;
            return true;
        }

        /// <summary>그 던전 세대의 입구가 지금 놓여 있으면 좌표를 돌려준다.</summary>
        public bool TryGetNarrativeEntrance(string dungeonId, out int x, out int y)
        {
            x = 0;
            y = 0;
            if (TryFindSlot(dungeonId, out _, out DungeonSlot slot) == false || slot.HasEntrance == false)
                return false;
            x = slot.EntranceX;
            y = slot.EntranceY;
            return true;
        }

        /// <summary>
        /// 던전 위치 표현: "이 마을 동쪽 약 40걸음 떨어진 던전". 말하는 NPC가 다른 마을로 이주했으면 "예전 마을"로 말한다.
        /// </summary>
        public string DescribeLocation(int villageId, int x, int y, int speakerVillageId)
        {
            var village = AR.s.Village.GetVillage(villageId);
            if (village == null)
                return "근처 던전";
            Vector2 offset = new Vector2(x + 0.5f, y + 0.5f) - village.Position;
            int bearing = (Mathf.RoundToInt(Mathf.Atan2(offset.x, offset.y) * Mathf.Rad2Deg / 45f) % 8 + 8) % 8;
            string prefix = speakerVillageId == villageId ? "이 마을" : "예전 마을";
            return $"{prefix} {_bearingNames[bearing]} 약 {Mathf.RoundToInt(offset.magnitude)}걸음 떨어진 던전";
        }

        /// <summary>그 슬롯 던전 표의 완료 골드 (행적 소문의 가치). 슬롯이 없으면 0.</summary>
        public int GetSlotCompletionGold(int villageId, int slotIndex)
        {
            int listIndex = FindSlotListIndex(villageId, slotIndex, out VillageDungeonSlots village);
            if (listIndex < 0 || village == null)
                return 0;
            var table = GetSlotTable(village.Slots[listIndex]);
            return table != null ? Mathf.Max(0, table.CompletionGold) : 0;
        }
    }
}
