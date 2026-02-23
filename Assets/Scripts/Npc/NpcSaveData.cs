#nullable enable
using ARPG.Creature;
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
        public Vector2 Position;
        public CharacterConditions Condition;
        public int EntityId;
        public bool IsActive;

        // 마을 시스템 데이터
        public int VillageId;
        public GlobalEnum.JobType JobType;
        public int SkillLevel;

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
