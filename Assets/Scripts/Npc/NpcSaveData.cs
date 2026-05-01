#nullable enable
using ARPG.Creature;
using Newtonsoft.Json;
using UnityEngine;

namespace ARPG.Npc
{
    /// <summary>
    /// Inn 시스템의 NPC 상태.
    /// Resident: 마을의 정식 거주자 (기본값, 기존 동작)
    /// InnVisitor: 여관에 머무는 방문자 — 플레이어가 고용해야 Resident로 승격
    /// </summary>
    public enum NpcStatus
    {
        Resident = 0,
        InnVisitor = 1,
    }

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

        // Inn 고용 시스템 (INN_HIRING_DESIGN.md §2.2)
        public NpcStatus Status;
        public int StayingAtVillageId;   // InnVisitor일 때만 의미 있음 (어느 마을 여관에 머무는가)
        public float ArrivedGameTime;    // InnVisitor일 때만 의미 있음 (만료 계산 기준)

        // 인스턴스별 flavor — 생성 시 직업 기반 풀에서 랜덤 선택, 이후 고정.
        // 같은 NpcTableId라도 NPC마다 다른 소개가 보이도록 데이터를 인스턴스에 둔다.
        public string Description = string.Empty;

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
            Status = NpcStatus.Resident;
            StayingAtVillageId = 0;
            ArrivedGameTime = 0f;
            Description = string.Empty;
        }
    }
}
