#nullable enable
using ARPG.Creature;
using Newtonsoft.Json;
using UnityEngine;

namespace ARPG.Npc
{
    /// <summary>
    /// NPC의 영구 저장 데이터.
    /// 청크 비활성화 시 ECS 컴포넌트에서 스냅샷하여 보관하고,
    /// 청크 재활성화 시 이 데이터로 엔티티를 복원한다.
    /// </summary>
    public class NpcSaveData
    {
        public int NpcTableId;
        public float PositionX;
        public float PositionY;
        public CharacterConditions Condition;
        public int EntityId;
        public bool IsActive;

        /// <summary>스폰 진행 중 플래그 (async CreateNpc 대기 구간). 이중 스폰 차단용. 세이브 대상 아님.</summary>
        [JsonIgnore]
        public bool IsSpawning;

        // 마을 시스템 데이터
        public int VillageId;
        public GlobalEnum.JobType JobType;
        public int SkillLevel;

        [JsonIgnore]
        public Vector2 Position
        {
            get => new Vector2(PositionX, PositionY);
            set { PositionX = value.x; PositionY = value.y; }
        }

        public NpcSaveData(int npcTableId, Vector2 position)
        {
            NpcTableId = npcTableId;
            Position = position;
            Condition = CharacterConditions.Normal;
            EntityId = -1;
            IsActive = false;
            VillageId = 0;
            JobType = GlobalEnum.JobType.None;
            SkillLevel = 0;
        }
    }
}
